#!/usr/bin/env python3
"""Stamp CEV-Eris grille frames into a CardinalFlags RSI (16 identical states)."""

from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from eris_cardinal_bake import write_rsi
from eris_dmi import first_frame, parse_dmi

ERIS_STRUCT = Path("/Users/russellrozario/Desktop/SS13-14/CEV-Eris/icons/obj/structures.dmi")
OUT = Path("/Users/russellrozario/Desktop/SS13-14/Oxyd14/Resources/Textures/Oxyd/erisported/grilles.rsi")

COPYRIGHT = (
    "Grille states taken from CEV-Eris icons/obj/structures.dmi "
    "(https://github.com/discordia-space/CEV-Eris)."
)


def main():
    _, _, tiles = parse_dmi(ERIS_STRUCT)
    intact = first_frame(tiles, "grille")
    broken = first_frame(tiles, "grille-b")
    files = {"full": intact.copy(), "broken": broken.copy()}
    states = [{"name": "full"}, {"name": "broken"}]
    for i in range(16):
        files[f"grille{i}"] = intact.copy()
        states.append({"name": f"grille{i}"})
    write_rsi(OUT, files, states, COPYRIGHT)
    print(f"grilles: {len(files)} frames")


if __name__ == "__main__":
    main()
