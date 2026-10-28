#!/usr/bin/env python3
"""Extract static NT states from the sibling Eris checkout using the existing DMI reader."""
import json
from pathlib import Path
from eris_dmi import parse_dmi, first_frame

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT.parent / "CEV-Eris"
STATES = {
    "faction_items": ("icons/obj/faction_item.dmi", ["nt_sword_truth", "last_shelter"]),
    "oddity": ("icons/obj/oddities.dmi", ["nt_seal"]),
    "machinery": ("icons/obj/neotheology_machinery.dmi", ["reader_off", "nt_obelisk", "cruciforge"]),
    "eye": ("icons/obj/eotp.dmi", ["Eye_of_the_Protector"]),
    "upgrades": ("icons/obj/module.dmi", ["cruciform_upgrade", "natures_blessing", "faiths_shield", "cleansing_presence", "martyr_gift", "wrath_of_god", "speed_of_the_chosen"]),
}

if __name__ == "__main__":
    for name, (source, states) in STATES.items():
        width, height, tiles = parse_dmi(SOURCE / source)
        output = ROOT / f"Resources/Textures/Oxyd/NeoTheology/{name}.rsi"
        output.mkdir(parents=True, exist_ok=True)
        for state in states:
            image = first_frame(tiles, state)
            assert image.size == (width, height) and image.getbbox(), state
            image.save(output / f"{state}.png")
        metadata = {
            "version": 1, "size": {"x": width, "y": height},
            "license": None,  # Source has no per-icon license declaration; do not invent one.
            "copyright": f"CEV-Eris contributors; static state extracted from {source} at 5f7847f26. https://github.com/discordia-space/CEV-Eris",
            "states": [{"name": state} for state in states],
        }
        (output / "meta.json").write_text(json.dumps(metadata, indent=2) + "\n")
