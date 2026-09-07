#!/usr/bin/env python3
"""Blit CEV-Eris girder frames into Oxyd14 girder RSI state names."""

from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from eris_cardinal_bake import write_rsi
from eris_dmi import first_frame, parse_dmi

ERIS_STRUCT = Path("/Users/russellrozario/Desktop/SS13-14/CEV-Eris/icons/obj/structures.dmi")
OUT = Path("/Users/russellrozario/Desktop/SS13-14/Oxyd14/Resources/Textures/Oxyd/erisported/girders.rsi")

COPYRIGHT = (
    "Girder states taken from CEV-Eris icons/obj/structures.dmi "
    "(https://github.com/discordia-space/CEV-Eris)."
)

# SS14 state name → Eris DMI state
STATES = {
    "wall_girder": "girder",
    "reinforced_wall_girder": "reinforced",
    "displaced": "displaced",
    "girder_low": "girder_low",
    "reinforced_low": "reinforced_low",
    "displaced_low": "displaced_low",
}


def main():
    _, _, tiles = parse_dmi(ERIS_STRUCT)
    files = {}
    meta_states = []
    for rsi_name, dmi_name in STATES.items():
        files[rsi_name] = first_frame(tiles, dmi_name).copy()
        meta_states.append({"name": rsi_name})
        print(f"  {rsi_name} ← {dmi_name} {files[rsi_name].size}")
    write_rsi(OUT, files, meta_states, COPYRIGHT)
    print(f"girders: {len(files)} frames")


if __name__ == "__main__":
    main()
