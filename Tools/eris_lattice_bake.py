#!/usr/bin/env python3
"""Bake CEV-Eris smoothlattice.dmi into TileBorder RSI states keyed by Eris dir_sum.

Source: CEV-Eris icons/obj/smoothlattice.dmi (lattice0–lattice15 + lattice-simple).
Eris picks icon_state = "lattice[dir_sum]" from BYOND cardinals only (N=1 S=2 E=4 W=8).

These frames are absolute cardinal patterns — they must NOT be rotated via Decal.Angle.
Runtime (BorderRotate: false) uses TileBorderMask.CardinalDirSum(mask) as the state key
(00..0f) with Angle zero and ALWAYS emits a decal (including 0x0F).
Requires DecalSystem TileBorder-* allowlist on isSpace (Lattice). Fill is lattices_base.png.
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


def state_name(dir_sum: int) -> str:
    return f"{dir_sum:02x}"


def lattice_frame(tiles: dict, dir_sum: int) -> Image.Image:
    name = f"lattice{dir_sum}"
    st = tiles[name]
    return frame(tiles, name, DIR_S if st["dirs"] == 4 else 0, 0).copy()


def clear_rsi_pngs(rsi_dir: Path) -> None:
    if not rsi_dir.is_dir():
        return
    for p in rsi_dir.glob("*.png"):
        p.unlink()


def bake_lattices(tiles: dict) -> int:
    rsi_dir = OUT_TEX / f"{STEM}.rsi"
    files: dict[str, Image.Image] = {}
    states: list[dict] = []
    for dir_sum in range(16):
        name = state_name(dir_sum)
        files[name] = lattice_frame(tiles, dir_sum)
        states.append({"name": name})

    clear_rsi_pngs(rsi_dir)
    write_rsi(rsi_dir, files, states, COPYRIGHT)

    OUT_TEX.mkdir(parents=True, exist_ok=True)
    base = frame(tiles, "lattice-simple", DIR_S, 0).copy()
    base.save(OUT_TEX / f"{STEM}_base.png")
    return len(files)


def lattices_yaml_block() -> str:
    lines = [
        f"# {STEM}.rsi",
        f"# lattices dir_sum 00-0f by Tools/eris_lattice_bake.py (BorderRotate: false)",
    ]
    for dir_sum in range(16):
        st = state_name(dir_sum)
        lines.append("- type: decal")
        lines.append("  parent: TileBorderBase")
        lines.append(f"  id: TileBorder-{STEM}-{st}")
        lines.append("  sprite:")
        lines.append(f"    sprite: Oxyd/erisported/{STEM}.rsi")
        lines.append(f"    state: {st}")
        lines.append("")
    return "\n".join(lines)


def append_or_replace_yaml() -> str:
    block = lattices_yaml_block()
    text = OUT_YAML.read_text() if OUT_YAML.is_file() else ""
    section_re = re.compile(r"(?ms)^# lattices\.rsi\n.*?(?=^# [^\n]+\.rsi\n|\Z)")
    if section_re.search(text):
        new_text, n = section_re.subn(block if block.endswith("\n") else block + "\n", text, count=1)
        if n != 1:
            raise SystemExit("failed to replace lattices yaml section")
        OUT_YAML.write_text(new_text)
        return "replaced"
    if text and not text.endswith("\n"):
        text += "\n"
    OUT_YAML.write_text(text + (block if block.endswith("\n") else block + "\n"))
    return "appended"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--skip-yaml", action="store_true")
    args = parser.parse_args()

    _, _, tiles = parse_dmi(ERIS_DMI)
    for i in range(16):
        if f"lattice{i}" not in tiles:
            raise KeyError(f"missing lattice{i} in {ERIS_DMI}")
    n = bake_lattices(tiles)
    print(f"{STEM}: {n} dir_sum states -> {OUT_TEX / (STEM + '.rsi')}")
    print(f"fill: {OUT_TEX / (STEM + '_base.png')}")

    if not args.skip_yaml:
        action = append_or_replace_yaml()
        print(f"yaml: {OUT_YAML} ({action} {STEM} x 16 states)")


if __name__ == "__main__":
    main()
