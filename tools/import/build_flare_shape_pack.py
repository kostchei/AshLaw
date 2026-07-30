#!/usr/bin/env python3
"""Build AshLaw's normalized Shape Pack v1 from Flare's open art.

The source repository is flareteam/flare-game (CC-BY-SA-3.0-or-later).
This importer intentionally emits only a small starter pack. Additions belong
in SHAPES below so their provenance, scale, footprint, and animation selection
remain reviewable.
"""

from __future__ import annotations

import argparse
import configparser
import json
import shutil
from dataclasses import dataclass
from pathlib import Path

from PIL import Image, ImageDraw, ImageEnhance


@dataclass(frozen=True)
class ShapeSource:
    shape_id: str
    image: str
    animation: str
    animation_names: tuple[str, ...]
    directions: int
    render_scale: tuple[int, int]
    footprint: tuple[int, int]
    height: int
    flags: str
    sort_bias: int = 0


SHAPES = (
    ShapeSource(
        shape_id="avatar.knight",
        image="mods/fantasycore/images/npcs/knight.png",
        animation="mods/fantasycore/animations/npcs/knight.txt",
        animation_names=("stance",),
        directions=8,
        render_scale=(5, 8),
        footprint=(128, 128),
        height=64,
        flags="animated",
    ),
    ShapeSource(
        shape_id="monster.goblin",
        image="mods/fantasycore/images/enemies/goblin.png",
        animation="mods/fantasycore/animations/enemies/goblin.txt",
        animation_names=("stance", "run", "die"),
        directions=8,
        render_scale=(5, 8),
        footprint=(128, 128),
        height=56,
        flags="animated",
    ),
    ShapeSource(
        shape_id="loot.shortsword",
        image="mods/fantasycore/images/loot/shortsword.png",
        animation="mods/fantasycore/animations/loot/shortsword.txt",
        animation_names=("power",),
        directions=1,
        render_scale=(1, 4),
        footprint=(64, 64),
        height=8,
        flags="none",
    ),
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--flare-root", type=Path, required=True)
    parser.add_argument("--flare-revision", required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument(
        "--saturation",
        type=float,
        default=0.65,
        help="Colour saturation multiplier for AshLaw's muted art direction.",
    )
    return parser.parse_args()


def read_flare_animation(path: Path) -> dict[str, dict[str, object]]:
    text = path.read_text(encoding="utf-8")
    parser = configparser.ConfigParser(
        interpolation=None,
        strict=False,
        delimiters=("=",),
    )
    parser.optionxform = str
    parser.read_string("[global]\n" + text)

    animations: dict[str, dict[str, object]] = {}
    for section_name in parser.sections():
        if section_name == "global":
            continue
        section = parser[section_name]
        # ConfigParser cannot retain duplicate keys. Read the frame rows directly
        # from the section instead.
        frame_lines = section_rows(text, section_name, "frame")
        frames = []
        for line in frame_lines:
            values = [int(value) for value in line.split(",")]
            if len(values) != 8:
                raise ValueError(f"{path}: malformed frame row {line!r}")
            sequence, direction, x, y, width, height, origin_x, origin_y = values
            frames.append(
                {
                    "sequence": sequence,
                    "direction": direction,
                    "x": x,
                    "y": y,
                    "width": width,
                    "height": height,
                    "origin_x": origin_x,
                    "origin_y": origin_y,
                }
            )

        duration = parse_duration(section.get("duration", "0ms"))
        animations[section_name] = {
            "frames": int(section["frames"]),
            "duration": duration,
            "playback": playback(section.get("type", "looped")),
            "rows": frames,
        }
    return animations


def section_rows(text: str, target: str, key: str) -> list[str]:
    active = False
    rows: list[str] = []
    for raw_line in text.splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#"):
            continue
        if line.startswith("[") and line.endswith("]"):
            active = line[1:-1] == target
            continue
        if active and line.startswith(f"{key}="):
            rows.append(line.split("=", 1)[1])
    return rows


def parse_duration(value: str) -> int:
    if not value.endswith("ms"):
        raise ValueError(f"Unsupported Flare duration {value!r}")
    return int(value[:-2])


def playback(value: str) -> str:
    return {
        "looped": "loop",
        "back_forth": "ping_pong",
        "play_once": "once",
    }[value]


def desaturate(image: Image.Image, saturation: float) -> Image.Image:
    rgba = image.convert("RGBA")
    alpha = rgba.getchannel("A")
    rgb = ImageEnhance.Color(rgba.convert("RGB")).enhance(saturation)
    rgb.putalpha(alpha)
    return rgb


def pack_mask(crop: Image.Image) -> bytes:
    alpha = crop.convert("RGBA").getchannel("A")
    output = bytearray()
    byte = 0
    bit_count = 0
    for value in alpha.getdata():
        byte = (byte << 1) | (1 if value > 8 else 0)
        bit_count += 1
        if bit_count == 8:
            output.append(byte)
            byte = 0
            bit_count = 0
    if bit_count:
        output.append(byte << (8 - bit_count))
    return bytes(output)


def build_flare_shape(
    source: ShapeSource,
    flare_root: Path,
    output: Path,
    saturation: float,
) -> dict[str, object]:
    source_image = Image.open(flare_root / source.image).convert("RGBA")
    atlas = desaturate(source_image, saturation)
    atlas_name = source.shape_id.replace(".", "-") + ".png"
    atlas.save(output / atlas_name, optimize=True)

    parsed = read_flare_animation(flare_root / source.animation)
    mask_name = source.shape_id.replace(".", "-") + ".mask"
    mask_data = bytearray()
    animations = []
    for animation_name in source.animation_names:
        flare_animation = parsed[animation_name]
        frames_per_direction = int(flare_animation["frames"])
        frame_duration = max(
            1,
            round(int(flare_animation["duration"]) / frames_per_direction),
        )
        source_rows = flare_animation["rows"]
        frames = []
        for direction in range(source.directions):
            for sequence in range(frames_per_direction):
                row = next(
                    row
                    for row in source_rows
                    if row["direction"] == direction
                    and row["sequence"] == sequence
                )
                crop = atlas.crop(
                    (
                        row["x"],
                        row["y"],
                        row["x"] + row["width"],
                        row["y"] + row["height"],
                    )
                )
                row = dict(row)
                row["duration_ms"] = frame_duration
                row["mask_offset"] = len(mask_data)
                mask_data.extend(pack_mask(crop))
                frames.append(row)

        animations.append(
            {
                "name": animation_name,
                "playback": flare_animation["playback"],
                "directions": source.directions,
                "frames_per_direction": frames_per_direction,
                "frames": frames,
            }
        )

    (output / mask_name).write_bytes(mask_data)
    return {
        "id": source.shape_id,
        "atlas": atlas_name,
        "atlas_width": atlas.width,
        "atlas_height": atlas.height,
        "mask": mask_name,
        "render_scale_numerator": source.render_scale[0],
        "render_scale_denominator": source.render_scale[1],
        "footprint": {
            "width": source.footprint[0],
            "depth": source.footprint[1],
        },
        "height": source.height,
        "flags": source.flags,
        "sort_bias": source.sort_bias,
        "animations": animations,
    }


def build_chest(output: Path) -> dict[str, object]:
    atlas = Image.new("RGBA", (64, 32))
    draw = ImageDraw.Draw(atlas)

    draw_chest(draw, 0, open_lid=False)
    draw_chest(draw, 32, open_lid=True)
    atlas_name = "container-chest.png"
    atlas.save(output / atlas_name, optimize=True)

    mask_name = "container-chest.mask"
    masks = bytearray()
    frames = []
    for sequence, x in enumerate((0, 32)):
        crop = atlas.crop((x, 0, x + 32, 32))
        frames.append(
            {
                "sequence": sequence,
                "direction": 0,
                "x": x,
                "y": 0,
                "width": 32,
                "height": 32,
                "origin_x": 16,
                "origin_y": 27,
                "duration_ms": 250,
                "mask_offset": len(masks),
            }
        )
        masks.extend(pack_mask(crop))
    (output / mask_name).write_bytes(masks)

    return {
        "id": "container.chest",
        "atlas": atlas_name,
        "atlas_width": 64,
        "atlas_height": 32,
        "mask": mask_name,
        "render_scale_numerator": 2,
        "render_scale_denominator": 1,
        "footprint": {"width": 128, "depth": 128},
        "height": 40,
        "flags": "solid",
        "sort_bias": 0,
        "animations": [
            {
                "name": "state",
                "playback": "once",
                "directions": 1,
                "frames_per_direction": 2,
                "frames": frames,
            }
        ],
    }


def draw_chest(draw: ImageDraw.ImageDraw, x: int, open_lid: bool) -> None:
    edge = (55, 35, 23, 255)
    dark = (77, 46, 26, 255)
    wood = (132, 82, 39, 255)
    light = (170, 112, 51, 255)
    metal = (205, 157, 66, 255)

    draw.polygon(
        [(x + 5, 17), (x + 16, 12), (x + 27, 17), (x + 16, 23)],
        fill=wood,
        outline=edge,
    )
    draw.polygon(
        [(x + 5, 17), (x + 16, 23), (x + 16, 28), (x + 5, 22)],
        fill=dark,
        outline=edge,
    )
    draw.polygon(
        [(x + 16, 23), (x + 27, 17), (x + 27, 22), (x + 16, 28)],
        fill=(61, 38, 25, 255),
        outline=edge,
    )
    draw.line([(x + 7, 17), (x + 25, 17)], fill=light, width=2)
    draw.rectangle((x + 15, 18, x + 18, 22), fill=metal)

    if open_lid:
        draw.polygon(
            [(x + 5, 14), (x + 16, 6), (x + 27, 12), (x + 16, 18)],
            fill=wood,
            outline=edge,
        )
        draw.line([(x + 7, 14), (x + 25, 12)], fill=light, width=2)


def write_attribution(
    output: Path,
    flare_root: Path,
    flare_revision: str,
) -> None:
    shutil.copyfile(flare_root / "LICENSE.txt", output / "LICENSE.flare.txt")
    (output / "ATTRIBUTION.md").write_text(
        f"""# Flare starter shape pack

The knight, goblin, and shortsword atlases are adapted from
[flareteam/flare-game](https://github.com/flareteam/flare-game) at commit
`{flare_revision}`.

Flare Game is Copyright ©2010–2013 Clint Bellanger. Contributors retain
copyright in their original contributions. Art and data are distributed under
CC-BY-SA 3.0, with later versions permitted. See `LICENSE.flare.txt` and the
[full Flare credits](https://github.com/flareteam/flare-game/wiki/Credits).

AshLaw's chest frames and normalized metadata are released as part of this
CC-BY-SA-3.0-or-later asset pack. The importer and engine code remain covered by
AshLaw's repository software license.
""",
        encoding="utf-8",
    )


def clean_generated_files(output: Path) -> None:
    output.mkdir(parents=True, exist_ok=True)
    for path in output.iterdir():
        if path.is_file() and path.suffix in {
            ".png",
            ".mask",
            ".json",
            ".md",
            ".txt",
        }:
            path.unlink()


def main() -> None:
    args = parse_args()
    flare_root = args.flare_root.resolve()
    output = args.output.resolve()
    if not (flare_root / "LICENSE.txt").is_file():
        raise SystemExit(f"Not a flare-game checkout: {flare_root}")
    if not 0 <= args.saturation <= 2:
        raise SystemExit("--saturation must be between 0 and 2")

    clean_generated_files(output)
    shapes = [
        build_flare_shape(
            source,
            flare_root,
            output,
            args.saturation,
        )
        for source in SHAPES
    ]
    shapes.append(build_chest(output))

    document = {
        "schema_version": 1,
        "pack_id": "flare-starter",
        "attribution": {
            "title": "Flare starter art adapted for AshLaw",
            "source": "https://github.com/flareteam/flare-game",
            "license": "CC-BY-SA-3.0-or-later",
            "revision": args.flare_revision,
            "authors": [
                "Clint Bellanger",
                "Flare Game contributors",
                "AshLaw contributors",
            ],
        },
        "shapes": shapes,
    }
    (output / "shape-pack.json").write_text(
        json.dumps(document, indent=2) + "\n",
        encoding="utf-8",
    )
    write_attribution(output, flare_root, args.flare_revision)

    print(f"Wrote {len(shapes)} shapes to {output}")
    for shape in shapes:
        frame_count = sum(
            len(animation["frames"])
            for animation in shape["animations"]
        )
        print(f"  {shape['id']}: {frame_count} frames")


if __name__ == "__main__":
    main()
