#!/usr/bin/env python3
"""Bake CEV-Eris low-wall+glass overlays into CardinalFlags window RSIs."""

from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from eris_cardinal_bake import TILE, WINDOW_ALPHA, bake_cardinal_set, pack_dirs, write_rsi
from eris_dmi import DIR_S, dir_frames, parse_dmi

ERIS_WALLS = Path("/Users/russellrozario/Desktop/SS13-14/CEV-Eris/icons/walls.dmi")
ERIS_STRUCT = Path("/Users/russellrozario/Desktop/SS13-14/CEV-Eris/icons/obj/structures.dmi")
OUT = Path("/Users/russellrozario/Desktop/SS13-14/Oxyd14/Resources/Textures/Oxyd/erisported/windows")

COPYRIGHT = (
    "Window states composited from CEV-Eris icons/walls.dmi low-wall and glass "
    "overlays; directional frames from icons/obj/structures.dmi "
    "(https://github.com/discordia-space/CEV-Eris)."
)

# proto RSI folder, CardinalFlags state prefix, Eris overlay prefixes, optional glass tint
FULLTILE = [
    ("glass", "window", ["eris_low", "glass"], None),
    ("reinf", "rwindow", ["eris_low", "reinf_glass"], None),
    ("tinted", "twindow", ["eris_low", "glass"], 0.42),
    ("plasma", "pwindow", ["eris_low", "plasma_glass"], None),
    ("plasma_reinf", "rpwindow", ["eris_low", "plasma_reinf_glass"], None),
]

# directional RSI state name → Eris structures.dmi state
DIRECTIONAL = {
    "window": "window",
    "reinforced_window": "rwindow",
    "tinted_window": "twindow",
    "plasma_window": "plasmawindow",
    "plasma_reinforced_window": "plasmarwindow",
}


def cardinal_states(prefix: str) -> list[dict]:
    states = [{"name": "full"}]
    for i in range(16):
        states.append({"name": f"{prefix}{i}"})
    return states


def bake_fulltile(tiles: dict) -> None:
    for folder, prefix, layers, tint in FULLTILE:
        baked = bake_cardinal_set(
            tiles, layers, tint_last=tint, rebake=True, glass_alpha=WINDOW_ALPHA
        )
        files = {"full": baked[0].copy()}
        for mask, im in baked.items():
            files[f"{prefix}{mask}"] = im
        write_rsi(OUT / f"{folder}.rsi", files, cardinal_states(prefix), COPYRIGHT)
        print(f"fulltile {folder}: {len(files)} frames")


def bake_directional(struct: dict) -> None:
    files = {}
    states = []
    for rsi_name, dmi_name in DIRECTIONAL.items():
        frames = dir_frames(struct, dmi_name)
        files[rsi_name] = frames[DIR_S]  # SS14 directional is 1-dir, rotated by entity
        states.append({"name": rsi_name})
        corner = f"{rsi_name}_corner"
        files[corner] = pack_dirs(frames, 2)
        states.append({"name": corner, "directions": 4})
    write_rsi(OUT / "directional.rsi", files, states, COPYRIGHT)
    print(f"directional: {len(files)} frames")


def main():
    _, _, walls = parse_dmi(ERIS_WALLS)
    _, _, struct = parse_dmi(ERIS_STRUCT)
    bake_fulltile(walls)
    bake_directional(struct)


if __name__ == "__main__":
    main()
