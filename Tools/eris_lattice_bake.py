#!/usr/bin/env python3
"""Bake CEV-Eris smoothlattice.dmi into a TileBorder adjacency-mask RSI.

Source: CEV-Eris icons/obj/smoothlattice.dmi (lattice0–lattice15 + lattice-simple).
Eris picks icon_state = "lattice[dir_sum]" from BYOND cardinals only
(N=1 S=2 E=4 W=8). We map each rotation-canonical 8-neighbour TileBorder mask
to that cardinal dir_sum and bake the south/still frame.

Bit order matches Robust Direction / TileBorderMask:
  S=0, SE=1, E=2, NE=3, N=4, NW=5, W=6, SW=7

Bake only rotation-canonical masks (69 values in 0..254); interior 0xFF is not
baked (fill comes from lattices_base.png). Runtime applies Decal.Angle.
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))

from eris_cardinal_bake import write_rsi
from eris_dmi import DIR_S, frame, parse_dmi

SCRIPT_DIR = Path(__file__).resolve().parent
OXYD = SCRIPT_DIR.parent
ERIS_DMI = Path("/Users/russellrozario/Desktop/SS13-14/CEV-Eris/icons/obj/smoothlattice.dmi")
OUT_TEX = OXYD / "Resources/Textures/Oxyd/erisported"
OUT_YAML = OXYD / "Resources/Prototypes/_Oxyd/Decals/tile_borders.yml"

STEM = "lattices"
COPYRIGHT = "Ported station lattice art (https://github.com/discordia-space/CEV-Eris)."

# Robust Direction bit indices.
BIT_S, BIT_E, BIT_N, BIT_W = 0, 2, 4, 6


def rot90_cw(mask: int) -> int:
    """90° CW on the 8-neighbour ring: new_bit[(i+2)%8] = old_bit[i]."""
    return ((mask << 2) | (mask >> 6)) & 0xFF


def canonical_mask(mask: int) -> int:
    """Minimum numeric value among {m, rot90, rot180, rot270}."""
    best = mask
    r = mask
    for _ in range(3):
        r = rot90_cw(r)
        if r < best:
            best = r
    return best


def enumerate_canonical_masks() -> list[int]:
    """All rotation-canonical masks in 0..254 (excludes interior 0xFF)."""
    return sorted({canonical_mask(m) for m in range(255)})


def state_name(mask: int) -> str:
    return f"{mask:02x}"


def eris_dir_sum(mask: int) -> int:
    """Map TileBorder 8-neighbour mask to Eris lattice dir_sum (cardinals only)."""
    dir_sum = 0
    if mask & (1 << BIT_N):
        dir_sum += 1  # BYOND NORTH
    if mask & (1 << BIT_S):
        dir_sum += 2  # BYOND SOUTH
    if mask & (1 << BIT_E):
        dir_sum += 4  # BYOND EAST
    if mask & (1 << BIT_W):
        dir_sum += 8  # BYOND WEST
    return dir_sum


def lattice_frame(tiles: dict, dir_sum: int) -> Image.Image:
    name = f"lattice{dir_sum}"
    st = tiles[name]
    # dirs=1 (lattice0/15): still frame 0. dirs=4: SOUTH / index 0 still.
    return frame(tiles, name, DIR_S if st["dirs"] == 4 else 0, 0).copy()


def clear_rsi_pngs(rsi_dir: Path) -> None:
    if not rsi_dir.is_dir():
        return
    for p in rsi_dir.glob("*.png"):
        p.unlink()


def bake_lattices(tiles: dict, masks: list[int]) -> int:
    rsi_dir = OUT_TEX / f"{STEM}.rsi"
    files: dict[str, Image.Image] = {}
    states: list[dict] = []
    for mask in masks:
        name = state_name(mask)
        files[name] = lattice_frame(tiles, eris_dir_sum(mask))
        states.append({"name": name})

    clear_rsi_pngs(rsi_dir)
    write_rsi(rsi_dir, files, states, COPYRIGHT)

    # Fill/interior for fully-surrounded tiles (no rim decal at 0xFF).
    base = frame(tiles, "lattice-simple", DIR_S, 0).copy()
    OUT_TEX.mkdir(parents=True, exist_ok=True)
    base.save(OUT_TEX / f"{STEM}_base.png")
    return len(files)


def lattices_yaml_block(masks: list[int]) -> str:
    lines = [
        f"# {STEM}.rsi",
        f"# lattices appended by Tools/eris_lattice_bake.py",
    ]
    for mask in masks:
        st = state_name(mask)
        lines.append("- type: decal")
        lines.append("  parent: TileBorderBase")
        lines.append(f"  id: TileBorder-{STEM}-{st}")
        lines.append("  sprite:")
        lines.append(f"    sprite: Oxyd/erisported/{STEM}.rsi")
        lines.append(f"    state: {st}")
        lines.append("")
    return "\n".join(lines)


def append_or_replace_yaml(masks: list[int]) -> str:
    """Append lattices section, or replace an existing lattices-only section."""
    block = lattices_yaml_block(masks)
    text = OUT_YAML.read_text() if OUT_YAML.is_file() else ""

    # Match from lattices section header through EOF or next stem header.
    section_re = re.compile(
        r"(?ms)^# lattices\.rsi\n.*?(?=^# [^\n]+\.rsi\n|\Z)"
    )
    if "TileBorder-lattices-00" in text or section_re.search(text):
        new_text, n = section_re.subn(block if block.endswith("\n") else block + "\n", text, count=1)
        if n == 0:
            # Marker present but section header missing — strip old lattice ids then append.
            # Avoid rewriting other stems: only remove TileBorder-lattices-* blocks.
            id_re = re.compile(
                r"(?ms)^- type: decal\n  parent: TileBorderBase\n  id: TileBorder-lattices-[0-9a-f]{2}\n"
                r"  sprite:\n    sprite: Oxyd/erisported/lattices\.rsi\n    state: [0-9a-f]{2}\n\n?"
            )
            new_text = id_re.sub("", text)
            if not new_text.endswith("\n"):
                new_text += "\n"
            new_text += block if block.endswith("\n") else block + "\n"
        OUT_YAML.write_text(new_text)
        return "replaced"
    if text and not text.endswith("\n"):
        text += "\n"
    OUT_YAML.write_text(text + (block if block.endswith("\n") else block + "\n"))
    return "appended"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--skip-yaml",
        action="store_true",
        help="Do not append/replace lattices entries in tile_borders.yml",
    )
    args = parser.parse_args()

    masks = enumerate_canonical_masks()
    assert len(masks) == 69, f"expected 69 canonical masks, got {len(masks)}"
    assert 0xFF not in masks
    assert 0 in masks

    _, _, tiles = parse_dmi(ERIS_DMI)
    for i in range(16):
        name = f"lattice{i}"
        if name not in tiles:
            raise KeyError(f"missing {name} in {ERIS_DMI}")
    if "lattice-simple" not in tiles:
        raise KeyError(f"missing lattice-simple in {ERIS_DMI}")

    n = bake_lattices(tiles, masks)
    print(f"{STEM}: {n} states -> {OUT_TEX / (STEM + '.rsi')}")
    print(f"fill: {OUT_TEX / (STEM + '_base.png')}")

    if not args.skip_yaml:
        action = append_or_replace_yaml(masks)
        print(f"yaml: {OUT_YAML} ({action} {STEM} x {len(masks)} states)")

    print(f"canonical_masks={len(masks)} names={state_name(masks[0])}..{state_name(masks[-1])}")


if __name__ == "__main__":
    main()
