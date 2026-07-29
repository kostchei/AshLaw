#!/usr/bin/env python3
"""Measure d20-scale formula fits against the digitized AT-1..AT-8 charts.

The charts are percentile-labelled, but the runtime scale is:

    chart roll = 5 * net d20 result
    net d20 result = raw d20 + attack modifiers - defence modifiers

Consequently a modified net result of 30 reads chart row 146-150. Open-ended-up
rows are deliberately excluded. The script compares the currently configured
linear formula with the best simple linear and power-curve fits.
"""

from __future__ import annotations

import csv
import json
import math
import re
from dataclasses import dataclass
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
SOURCE = REPO / "docs" / "reference" / "merp-attack-tables.csv"
RULES = REPO / "vendor" / "ash-v1-rules" / "data" / "combat_system_data.json"

ARMOURS = ("Plate", "Chain", "Leather", "None")
CATEGORY_KEYS = {
    "AT-1": "at_1_1_handed_slashing",
    "AT-2": "at_2_1_handed_concussion",
    "AT-3": "at_3_2_handed_weapons",
    "AT-4": "at_4_missile_weapons",
    "AT-5": "at_5_tooth_and_claw",
    "AT-6": "at_6_grappling_unbalancing",
    "AT-7": "at_7_spell_bolts",
    "AT-8": "at_8_spell_balls",
}
CSV_COLUMNS = {
    "Plate": "plate",
    "Chain": "chain",
    "None": "none",
}
NUMBER = re.compile(r"^\+?(\d+)")


@dataclass(frozen=True)
class Fit:
    kind: str
    threshold: int
    multiplier: float
    exponent: float
    quadratic: float
    nrmse: float
    max_error: float


def token_hits(token: str) -> int | None:
    match = NUMBER.match(token.strip())
    return int(match.group(1)) if match else None


def load_samples() -> dict[tuple[str, str], list[tuple[int, float]]]:
    rows_by_table: dict[str, list[dict[str, str]]] = {}
    with SOURCE.open(newline="", encoding="utf-8") as handle:
        for row in csv.DictReader(handle):
            rows_by_table.setdefault(row["table"], []).append(row)

    samples: dict[tuple[str, str], list[tuple[int, float]]] = {}
    for table, rows in rows_by_table.items():
        regular_rows = [
            row for row in rows
            if "open-ended-up" not in row["note"].lower()
        ]
        highest_regular_roll = max(int(row["roll_max"]) for row in regular_rows)
        for armour in ARMOURS:
            points: list[tuple[int, float]] = []
            for net_roll in range(1, highest_regular_roll // 5 + 1):
                chart_roll = net_roll * 5
                row = next(
                    (
                        candidate
                        for candidate in regular_rows
                        if int(candidate["roll_min"]) <= chart_roll
                        <= int(candidate["roll_max"])
                    ),
                    None,
                )
                if row is None:
                    continue
                if armour == "Leather":
                    rigid = token_hits(row["rigid_leather"])
                    soft = token_hits(row["soft_leather"])
                    value = (
                        None if rigid is None or soft is None
                        else (rigid + soft) / 2.0
                    )
                else:
                    value = token_hits(row[CSV_COLUMNS[armour]])
                # Fumble/failure cells are mechanics, not damage-curve points.
                if value is not None:
                    points.append((net_roll, float(value)))
            samples[(table, armour)] = points
    return samples


def predict(
    x: int,
    threshold: int,
    multiplier: float,
    exponent: float,
    quadratic: float = 0.0,
) -> int:
    distance = max(0.0, x - threshold)
    continuous = (
        multiplier * distance + quadratic * distance * distance
        if quadratic
        else multiplier * distance**exponent
    )
    return max(0, math.floor(continuous))


def error(
    points: list[tuple[int, float]],
    threshold: int,
    multiplier: float,
    exponent: float,
    quadratic: float = 0.0,
) -> tuple[float, float]:
    residuals = [
        predict(x, threshold, multiplier, exponent, quadratic) - y
        for x, y in points
    ]
    scale = max(y for _, y in points)
    rmse = math.sqrt(sum(value * value for value in residuals) / len(residuals))
    return rmse / scale, max(abs(value) for value in residuals) / scale


def best_scale(
    points: list[tuple[int, float]],
    threshold: int,
    exponent: float,
) -> float:
    pairs = [
        (max(0.0, x - threshold) ** exponent, y)
        for x, y in points
    ]
    denominator = sum(basis * basis for basis, _ in pairs)
    if denominator == 0:
        return 0.0
    least_squares = sum(basis * y for basis, y in pairs) / denominator

    # floor() makes the objective discontinuous. Search narrowly around the
    # continuous least-squares solution and retain the best discrete result.
    low = max(0.0, least_squares * 0.8)
    high = max(low + 0.001, least_squares * 1.2)
    candidates = [low + (high - low) * index / 80 for index in range(81)]
    return min(
        candidates,
        key=lambda scale: error(points, threshold, scale, exponent),
    )


def fit(points: list[tuple[int, float]], kind: str) -> Fit:
    exponents = [1.0] if kind == "linear" else [
        0.50 + index * 0.02 for index in range(101)
    ]
    candidates: list[Fit] = []
    for threshold in range(0, max(x for x, _ in points)):
        for exponent in exponents:
            multiplier = best_scale(points, threshold, exponent)
            nrmse, maximum = error(
                points, threshold, multiplier, exponent
            )
            candidates.append(
                Fit(
                    kind,
                    threshold,
                    multiplier,
                    exponent,
                    0.0,
                    nrmse,
                    maximum,
                )
            )
    return min(candidates, key=lambda item: (item.nrmse, item.max_error))


def fit_quadratic(points: list[tuple[int, float]]) -> Fit:
    candidates: list[Fit] = []
    for threshold in range(0, max(x for x, _ in points)):
        rows = [(max(0.0, x - threshold), y) for x, y in points]
        s2 = sum(distance**2 for distance, _ in rows)
        s3 = sum(distance**3 for distance, _ in rows)
        s4 = sum(distance**4 for distance, _ in rows)
        sy1 = sum(distance * y for distance, y in rows)
        sy2 = sum(distance**2 * y for distance, y in rows)
        determinant = s2 * s4 - s3 * s3
        if determinant == 0:
            continue
        linear = (sy1 * s4 - sy2 * s3) / determinant
        quadratic = (s2 * sy2 - s3 * sy1) / determinant
        if linear < 0 or quadratic < 0:
            continue
        linear_values = [
            max(0.0, linear * (0.7 + index * 0.02))
            for index in range(31)
        ]
        quadratic_values = [
            max(0.0, quadratic * (0.7 + index * 0.02))
            for index in range(31)
        ]
        for candidate_linear in linear_values:
            for candidate_quadratic in quadratic_values:
                nrmse, maximum = error(
                    points,
                    threshold,
                    candidate_linear,
                    1.0,
                    candidate_quadratic,
                )
                candidates.append(
                    Fit(
                        "quadratic",
                        threshold,
                        candidate_linear,
                        1.0,
                        candidate_quadratic,
                        nrmse,
                        maximum,
                    )
                )
    return min(candidates, key=lambda item: (item.max_error, item.nrmse))


def current_fit(
    points: list[tuple[int, float]], target: dict[str, float]
) -> Fit:
    nrmse, maximum = error(
        points,
        int(target["damage_origin"]),
        float(target["multiplier"]),
        1.0,
        float(target["quadratic"]),
    )
    return Fit(
        "current",
        int(target["damage_origin"]),
        float(target["multiplier"]),
        1.0,
        float(target["quadratic"]),
        nrmse,
        maximum,
    )


def main() -> int:
    samples = load_samples()
    rules = json.loads(RULES.read_text(encoding="utf-8"))[
        "combat_system_data"
    ]["attack_tables"]

    print(
        "Table Armor    Current NRMSE   Best linear                 "
        "Selected (linear when NRMSE <=5% and final-row error <=2 hits)"
    )
    print("-" * 112)
    failures: list[str] = []
    for table in CATEGORY_KEYS:
        for armour in ARMOURS:
            points = samples[(table, armour)]
            target = rules[CATEGORY_KEYS[table]]["armor_targets"][armour]
            current = current_fit(points, target)
            current_final_x, current_final_value = points[-1]
            current_final_error = abs(
                predict(
                    current_final_x,
                    current.threshold,
                    current.multiplier,
                    current.exponent,
                    current.quadratic,
                )
                - current_final_value
            )
            if current.nrmse > 0.05 or current_final_error > 2:
                failures.append(
                    f"{table}/{armour}: NRMSE {current.nrmse:.1%}, "
                    f"final-row error {current_final_error:g} hits"
                )
            linear = fit(points, "linear")
            final_x, final_value = points[-1]
            linear_final_error = abs(
                predict(
                    final_x,
                    linear.threshold,
                    linear.multiplier,
                    linear.exponent,
                    linear.quadratic,
                )
                - final_value
            )
            if linear.nrmse <= 0.05 and linear_final_error <= 2:
                selected = linear
            else:
                quadratic = fit_quadratic(points)
                quadratic_final_error = abs(
                    predict(
                        final_x,
                        quadratic.threshold,
                        quadratic.multiplier,
                        quadratic.exponent,
                        quadratic.quadratic,
                    )
                    - final_value
                )
                selected = (
                    quadratic
                    if quadratic.nrmse <= 0.05
                    and quadratic_final_error <= 2
                    else fit(points, "power")
                )
            print(
                f"{table:<5} {armour:<8} "
                f"{current.nrmse:6.1%} max {current.max_error:5.1%}   "
                f"t={linear.threshold:2d} m={linear.multiplier:5.3f} "
                f"{linear.nrmse:5.1%}   "
                f"{selected.kind:<6} t={selected.threshold:2d} "
                f"m={selected.multiplier:6.3f} p={selected.exponent:4.2f} "
                f"q={selected.quadratic:6.3f} "
                f"{selected.nrmse:5.1%} max {selected.max_error:5.1%}"
            )
    if failures:
        print("\nConfigured curve failures:")
        for failure in failures:
            print(f"  - {failure}")
        return 1
    print("\nAll configured AT-1..AT-8 damage curves are within 5% NRMSE.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
