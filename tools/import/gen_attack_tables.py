#!/usr/bin/env python3
"""Validate the d20 attack-table re-fit against MERP, and generate the summary CSV.

combat_system_data.json is the single source of truth. attack_tables_summary.csv is a
build artifact generated from it and must never be hand-edited -- hand-maintaining a
derived file is what produced the AT-3/AT-4 divergence this script exists to prevent.

Validation: for each armour column with MERP reference data, the fitted curve

    z = max(0, net_roll - damage_origin)
    hits = floor(multiplier * z + quadratic * z**2)

is evaluated at net_roll = 30 (MERP table row 146-150) and compared against the
published MERP damage value. Anything outside tolerance is a hard failure.

Usage:
    python gen_attack_tables.py           validate and regenerate the CSV
    python gen_attack_tables.py --check   validate only, write nothing (for CI)

Exits non-zero on any validation failure. There is no fallback path: a table that does
not match its MERP source is an error, not something to warn about and carry on from.
"""

from __future__ import annotations

import argparse
import csv
import json
import math
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
DATA = REPO / "vendor" / "ash-v1-rules" / "data"
JSON_PATH = DATA / "combat_system_data.json"
CSV_PATH = DATA / "attack_tables_summary.csv"
REF_PATH = REPO / "tools" / "import" / "merp_reference.json"

ARMOURS = ("Plate", "Chain", "Leather", "None")
CSV_HEADER = [
    "Category_ID",
    "Category_Name",
    "Armor_Type",
    "Hit_Threshold",
    "Damage_Origin",
    "Multiplier",
    "Quadratic",
    "Base_Crit_A",
    "Crit_Interval",
]


class ValidationError(Exception):
    """A table disagrees with its MERP source, or the data is structurally invalid."""


def category_id(key: str, name: str) -> str:
    """'AT-3: 2-Handed Weapons & Polearms' -> 'AT-3'; environmental gets its own id."""
    if key == "environmental_fall_crush":
        return "ENV"
    head = name.split(":")[0].strip()
    if not head.startswith("AT-"):
        raise ValidationError(f"{key}: cannot derive a category id from name {name!r}")
    return head


def category_name(name: str) -> str:
    return name.split(":", 1)[1].strip() if ":" in name else name.strip()


def predicted_hits(target: dict, net_roll: int) -> int:
    distance = max(0, net_roll - target["damage_origin"])
    return max(
        0,
        math.floor(
            distance * target["multiplier"]
            + distance * distance * target["quadratic"]
        ),
    )


def collapse_leather(max_hits: dict) -> float:
    """MERP's two leather columns -> the engine's single Leather column."""
    return (max_hits["Leather_Rigid"] + max_hits["Leather_Soft"]) / 2.0


def validate(tables: dict, ref: dict) -> list[str]:
    """Return a report of every checked column. Raises on the first structural problem."""
    net_roll = ref["net_roll_at_final_row"]
    tol = ref["tolerance_hits"]
    report: list[str] = []
    failures: list[str] = []

    for key, table in tables.items():
        targets = table.get("armor_targets")
        if targets is None:
            raise ValidationError(f"{key}: missing 'armor_targets'")
        missing = [a for a in ARMOURS if a not in targets]
        if missing:
            raise ValidationError(f"{key}: missing armour columns {missing}")
        unknown = [a for a in targets if a not in ARMOURS]
        if unknown:
            raise ValidationError(f"{key}: unknown armour columns {unknown}")
        for armour, t in targets.items():
            for field in (
                "hit_threshold",
                "damage_origin",
                "multiplier",
                "quadratic",
                "base_crit_a",
                "crit_interval",
            ):
                if field not in t:
                    raise ValidationError(f"{key}/{armour}: missing '{field}'")
            if t["multiplier"] < 0:
                raise ValidationError(f"{key}/{armour}: multiplier must not be negative")
            if t["quadratic"] < 0:
                raise ValidationError(f"{key}/{armour}: quadratic must not be negative")
            if t["multiplier"] == 0 and t["quadratic"] == 0:
                raise ValidationError(
                    f"{key}/{armour}: multiplier or quadratic must be positive"
                )
            if t["crit_interval"] <= 0:
                raise ValidationError(f"{key}/{armour}: crit_interval must be positive")

        if key in ref["unvalidated"]:
            report.append(f"  {key}: SKIPPED (not covered by the final-row check)")
            continue
        if key not in ref["tables"]:
            raise ValidationError(
                f"{key}: not in merp_reference.json 'tables' and not listed as unvalidated"
            )

        max_hits = ref["tables"][key]["max_hits"]
        expected = {
            "Plate": float(max_hits["Plate"]),
            "Chain": float(max_hits["Chain"]),
            "Leather": collapse_leather(max_hits),
            "None": float(max_hits["None"]),
        }
        for armour in ARMOURS:
            got = predicted_hits(targets[armour], net_roll)
            want = expected[armour]
            delta = got - want
            ok = abs(delta) <= tol
            report.append(
                f"  {key:<28} {armour:<8} MERP {want:5.1f}  fit {got:3d}  "
                f"delta {delta:+5.1f}  {'ok' if ok else 'FAIL'}"
            )
            if not ok:
                failures.append(
                    f"{key}/{armour}: fitted curve yields {got} hits at net roll "
                    f"{net_roll}, MERP table says {want:.1f} (tolerance +/-{tol})"
                )

    if failures:
        raise ValidationError(
            "attack table fit does not match MERP source:\n  - " + "\n  - ".join(failures)
        )
    return report


def write_csv(tables: dict) -> None:
    rows = []
    for key, table in tables.items():
        cid = category_id(key, table["name"])
        cname = category_name(table["name"])
        for armour in ARMOURS:
            t = table["armor_targets"][armour]
            rows.append(
                [
                    cid,
                    cname,
                    armour,
                    t["hit_threshold"],
                    t["damage_origin"],
                    t["multiplier"],
                    t["quadratic"],
                    t["base_crit_a"],
                    t["crit_interval"],
                ]
            )
    with CSV_PATH.open("w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh, lineterminator="\n")
        w.writerow(CSV_HEADER)
        w.writerows(rows)
    print(f"wrote {CSV_PATH.relative_to(REPO)} ({len(rows)} rows)")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--check", action="store_true", help="validate only, write nothing")
    args = ap.parse_args()

    tables = json.loads(JSON_PATH.read_text(encoding="utf-8"))["combat_system_data"][
        "attack_tables"
    ]
    ref = json.loads(REF_PATH.read_text(encoding="utf-8"))

    print("Validating d20 re-fit against MERP attack tables:")
    for line in validate(tables, ref):
        print(line)
    print("all validated tables within tolerance")

    if args.check:
        current = CSV_PATH.read_text(encoding="utf-8") if CSV_PATH.exists() else ""
        write_to = CSV_PATH.with_suffix(".csv.tmp")
        try:
            real, globals()["CSV_PATH"] = CSV_PATH, write_to  # noqa: F821
            write_csv(tables)
            regenerated = write_to.read_text(encoding="utf-8")
        finally:
            globals()["CSV_PATH"] = real
            write_to.unlink(missing_ok=True)
        if current != regenerated:
            raise ValidationError(
                f"{CSV_PATH.name} is stale. Run gen_attack_tables.py to regenerate it; "
                "do not edit it by hand."
            )
        print(f"{CSV_PATH.name} is up to date")
        return 0

    write_csv(tables)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except ValidationError as exc:
        print(f"\nERROR: {exc}", file=sys.stderr)
        sys.exit(1)
