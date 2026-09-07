#!/usr/bin/env python3
"""Flatten CEV-Eris railing overlays into SS14 4-dir railing states."""

from __future__ import annotations

import sys
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))

from eris_cardinal_bake import TILE, pack_dirs, write_rsi
from eris_dmi import dir_frames, parse_dmi

ERIS_RAILING = Path("/Users/russellrozario/Desktop/SS13-14/CEV-Eris/icons/obj/railing.dmi")
OUT = Path("/Users/russellrozario/Desktop/SS13-14/Oxyd14/Resources/Textures/Oxyd/erisported/railings.rsi")

COPYRIGHT = (
    "Railing states flattened from CEV-Eris icons/obj/railing.dmi "
    "(https://github.com/discordia-space/CEV-Eris)."
)


def composite_dirs(*layer_sets: list[Image.Image]) -> list[Image.Image]:
    out = []
    for i in range(4):
        acc = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))
        for layers in layer_sets:
            acc = Image.alpha_composite(acc, layers[i])
        out.append(acc)
    return out


def main():
    _, _, tiles = parse_dmi(ERIS_RAILING)
    r0 = dir_frames(tiles, "railing0")
    r1 = dir_frames(tiles, "railing1")
    corner = dir_frames(tiles, "corneroverlay")
    front_l = dir_frames(tiles, "frontoverlay_l")
    front_r = dir_frames(tiles, "frontoverlay_r")
    mcorner = dir_frames(tiles, "mcorneroverlay")

    files = {
        "side": pack_dirs(r0),
        "corner": pack_dirs(composite_dirs(r1, corner, front_l, front_r)),
        "corner_small": pack_dirs(composite_dirs(r0, mcorner)),
        "round": pack_dirs(composite_dirs(r1, front_l, front_r)),
    }
    states = [
        {"name": "side", "directions": 4},
        {"name": "corner", "directions": 4},
        {"name": "corner_small", "directions": 4},
        {"name": "round", "directions": 4},
    ]
    write_rsi(OUT, files, states, COPYRIGHT)
    print(f"railings: {list(files)}")


if __name__ == "__main__":
    main()
