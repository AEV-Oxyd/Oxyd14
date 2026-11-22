#!/usr/bin/env python3
"""Bake Eris Moebius medical item/machine sprites from CEV-Eris DMIs into RSIs.

Usage: eris_medical_bake.py <path-to-CEV-Eris-checkout>
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
from eris_dmi import TILE, dir_frames, first_frame, frame, parse_dmi

if len(sys.argv) != 2:
    sys.exit(__doc__)

ERIS = Path(sys.argv[1]).resolve() / "icons" / "obj"
OUT = Path(__file__).resolve().parents[1] / "Resources" / "Textures" / "Oxyd" / "erisported"

COPYRIGHT = (
    "States taken from CEV-Eris (https://github.com/discordia-space/CEV-Eris), "
    "converted for SS14 via Tools/eris_medical_bake.py."
)


def write_rsi(rsi_dir: Path, files: dict[str, Image.Image], states: list[dict],
              copyright: str, size: int = TILE):
    rsi_dir.mkdir(parents=True, exist_ok=True)
    for name, im in files.items():
        im.save(rsi_dir / f"{name}.png")
    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": copyright,
        "size": {"x": size, "y": size},
        "states": states,
    }
    (rsi_dir / "meta.json").write_text(json.dumps(meta, indent=2) + "\n")


def bake(name: str, dmi: str, wanted: dict[str, str], size: int = TILE):
    """wanted maps output state name -> dmi state name (single-direction items)."""
    _, _, tiles = parse_dmi(dmi)
    files: dict[str, Image.Image] = {}
    states: list[dict] = []
    for out_name, dmi_state in wanted.items():
        try:
            im = first_frame(tiles, dmi_state)
        except KeyError:
            print(f"  !! missing {dmi_state} in {dmi}")
            continue
        if size != TILE or im.size != (size, size):
            im = im.resize((size, size), Image.LANCZOS)
        files[out_name] = im
        states.append({"name": out_name})
    write_rsi(OUT / name, files, states, COPYRIGHT, size)
    print(f"{name}: {len(files)} states")


def bake_dirs(name: str, dmi: str, wanted: dict[str, str]):
    """4-directional structures (sleeper pod). Output states directions=4."""
    _, _, tiles = parse_dmi(dmi)
    files: dict[str, Image.Image] = {}
    states: list[dict] = []
    for out_name, dmi_state in wanted.items():
        try:
            st = tiles[dmi_state]
        except KeyError:
            print(f"  !! missing {dmi_state} in {dmi}")
            continue
        if st["dirs"] == 1:
            im = st["images"][0]
            files[out_name] = im
            states.append({"name": out_name})
        else:
            # SS14 expects one png per dir: <state>.<dir>.png? No — RSI stores
            # directional states as a horizontal strip in one png.
            sheet = Image.new("RGBA", (TILE * st["dirs"], TILE), (0, 0, 0, 0))
            for d in range(st["dirs"]):
                sheet.paste(frame(tiles, dmi_state, d), (d * TILE, 0))
            files[out_name] = sheet
            states.append({"name": out_name, "directions": st["dirs"]})
    write_rsi(OUT / name, files, states, COPYRIGHT)
    print(f"{name}: {len(files)} states")


def main():
    # Surgical tools (Eris tools.dmi) — includes tier-3/4/5 scalpels and powered saws.
    bake("eris_surgery_tools.rsi", ERIS / "tools.dmi", {
        "scalpel": "scalpel_t3",
        "scalpel_adv": "scalpel_t4",
        "scalpel_laser": "scalpel_t5",
        "hemostat": "hemostat",
        "retractor": "retractor",
        "cautery": "cautery",
        "bonesetter": "bone setter",
        "saw": "metal_saw",
        "saw_circular": "saw",
        "saw_advanced": "advanced_saw",
        "drill": "drill",
    })

    # Medical stacks (Eris stack/items.dmi)
    bake("eris_medstacks.rsi", ERIS / "stack" / "items.dmi", {
        "brutepack": "brutepack",
        "brutepack_1": "brutepack1",
        "brutepack_2": "brutepack2",
        "brutepack_3": "brutepack3",
        "brutepack_4": "brutepack4",
        "brutepack_5": "brutepack5",
        "ointment": "ointment5",
        "traumakit": "traumakit",
        "burnkit": "burnkit",
        "splint": "splint",
        "nanopaste": "nanopaste",
        "hm_brutepack": "hm_brutepack",
    })

    # Autoinjectors + hypospray (Eris syringe.dmi)
    bake("eris_autoinjectors.rsi", ERIS / "syringe.dmi", {
        "autoinjector": "autoinjector",
        "autoinjector0": "autoinjector0",
        "hypospray": "hypo",
        "combat_hypo": "combat_hypo",
        "borghypo": "borghypo",
    })

    # First aid kits + organ freezer (Eris storage.dmi)
    bake("eris_medkits.rsi", ERIS / "storage.dmi", {
        "firstaid": "firstaid",
        "advfirstaid": "advfirstaid",
        "surgeon": "surgeon",
        "freezer": "freezer",
        "freezer_red": "freezer_red",
        "ointment": "ointment",
        "antitoxin": "antitoxin",
        "nt_kit": "nt_kit",
    })

    # Bodybags + stasis bags (Eris bodybag.dmi + cryobag.dmi)
    _, _, body = parse_dmi(ERIS / "bodybag.dmi")
    _, _, cryo = parse_dmi(ERIS / "cryobag.dmi")
    files: dict[str, Image.Image] = {}
    states: list[dict] = []
    pairs = {
        "bodybag_folded": (body, "bodybag_folded"),
        "bodybag_open": (body, "bodybag_open"),
        "bodybag_closed": (body, "bodybag_closed"),
        "bodybag_full": (body, "bodybag_full"),
        "stasis_folded": (cryo, "bodybag_folded"),
        "stasis_open": (cryo, "bodybag_open"),
        "stasis_full": (cryo, "bodybag_full"),
        "stasis_used": (cryo, "bodybag_used"),
    }
    for out_name, (tiles, dmi_state) in pairs.items():
        try:
            files[out_name] = first_frame(tiles, dmi_state)
            states.append({"name": out_name})
        except KeyError:
            print(f"  !! missing {dmi_state}")
    write_rsi(OUT / "eris_bodybags.rsi", files, states, COPYRIGHT)
    print(f"eris_bodybags.rsi: {len(files)} states")

    # Medical stand / IV (Eris medical_stand.dmi + iv_drip.dmi)
    bake("eris_ivstand.rsi", ERIS / "medical_stand.dmi", {
        "empty": "medical_stand_empty",
        "reagent10": "reagent10",
        "reagent25": "reagent25",
        "reagent50": "reagent50",
        "reagent75": "reagent75",
        "reagent80": "reagent80",
        "reagent100": "reagent100",
        "tube": "tube",
        "tank_anest": "tank_anest",
        "tank_other": "tank_other",
        "beaker": "beaker",
    })

    # Sleeper + body scanner (Eris Cryogenic2.dmi)
    bake("eris_sleeper.rsi", ERIS / "Cryogenic2.dmi", {
        "sleeper_empty": "sleeper_0",
        "sleeper_occupied": "sleeper_1",
        "scanner_off": "scanner_off",
        "scanner_empty": "scanner_0",
        "scanner_occupied": "scanner_1",
        "celltop_off": "celltop_0",
        "celltop_on": "celltop_1",
    })

    # Autodoc (96x96 machine — downscaled to tile for consistency)
    bake("eris_autodoc.rsi", ERIS / "autodoc.dmi", {
        "idle": "powered_on",
        "off": "powered_off",
        "active": "active",
    }, size=TILE)

    # Chem machinery (Eris machines/chemistry.dmi)
    bake("eris_chemmachines.rsi", ERIS / "machines" / "chemistry.dmi", {
        "centrifuge": "centrifuge",
        "centrifuge_off": "centrifuge_off",
        "centrifuge_moving": "centrifuge_moving",
        "electrolysis": "electrolysis",
        "electrolysis_off": "electrolysis_off",
        "electrolysis_working": "electrolysis_working",
    })

    # Health scanner (Eris device.dmi)
    bake("eris_healthscanner.rsi", ERIS / "device.dmi", {
        "health": "health",
        "health0": "health0",
        "health2": "health2",
    })

    # Operating table + morgue pieces
    bake("eris_optable.rsi", ERIS / "surgery.dmi", {
        "optable_idle": "optable-idle",
        "optable_active": "optable-active",
    })

    # Station objects: morgue + crematorium drawers
    bake("eris_morgue.rsi", ERIS / "stationobjs.dmi", {
        "morgue_empty": "morgue0",
        "morgue_body": "morgue1",
        "morgue_open": "morgue2",
        "morgue_tray": "morguet",
        "crema_empty": "crema0",
        "crema_body": "crema1",
        "crema_open": "crema2",
    })


if __name__ == "__main__":
    main()
