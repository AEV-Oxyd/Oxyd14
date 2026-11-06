#!/usr/bin/env python3
"""Bake the Eris cryo cell (cryogenics_split.dmi) into a 32x64 SS14 RSI.

Eris draws the pod as bottom tile + *_top overlays at pixel_z=32. SS14's
cryopod.rsi is one 32x64 canvas per state (top half renders above the tile).
States are composited accordingly.

Usage: eris_cryo_bake.py <path-to-CEV-Eris-checkout>
"""

from __future__ import annotations

import json
import shutil
import sys
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
from eris_dmi import TILE, frame, parse_dmi

if len(sys.argv) != 2:
    sys.exit(__doc__)

_REPO_ROOT = Path(__file__).resolve().parents[1]
ERIS = Path(sys.argv[1]).resolve() / "icons" / "obj"
OUT = _REPO_ROOT / "Resources" / "Textures" / "Oxyd" / "erisported"
BASE_RSI = _REPO_ROOT / "Resources" / "Textures" / "Structures" / "Machines" / "Medical" / "cryopod.rsi"

COPYRIGHT = (
    "States taken from CEV-Eris (https://github.com/discordia-space/CEV-Eris), "
    "converted for SS14 via Tools/eris_cryo_bake.py."
)


def compose(bottom: Image.Image, top: Image.Image) -> Image.Image:
    """Stack a 32x32 top overlay over a 32x32 bottom into a 32x64 canvas."""
    img = Image.new("RGBA", (TILE, TILE * 2), (0, 0, 0, 0))
    img.paste(top, (0, 0))
    img.paste(bottom, (0, TILE))
    return img


def main() -> None:
    _, _, tiles = parse_dmi(ERIS / "cryogenics_split.dmi")
    rsi = OUT / "eris_cryo.rsi"
    rsi.mkdir(parents=True, exist_ok=True)

    states: list[dict] = []

    def write_static(name: str, bottom_state: str, top_state: str) -> None:
        img = compose(frame(tiles, bottom_state, 0, 0), frame(tiles, top_state, 0, 0))
        img.save(rsi / f"{name}.png")
        states.append({"name": name})
        print(f"  {name} <- {bottom_state}+{top_state}")

    # Empty/open pod, unpowered pod, powered pod (lid handled by cover states).
    write_static("pod-open", "pod0", "pod0_top")
    write_static("pod-off", "pod0", "pod0_top")
    write_static("pod-on", "pod1", "pod1_top")
    write_static("cover-off", "lid0", "lid0_top")

    # cover-on: 4-frame animation (lid1/lid1_top each have 4 frames), stacked vertically.
    n_frames = 4
    anim = Image.new("RGBA", (TILE, TILE * 2 * n_frames), (0, 0, 0, 0))
    for f in range(n_frames):
        anim.paste(compose(frame(tiles, "lid1", 0, f), frame(tiles, "lid1_top", 0, f)),
                   (0, f * TILE * 2))
    anim.save(rsi / "cover-on.png")
    states.append({"name": "cover-on", "delays": [[0.3] * n_frames]})
    print("  cover-on <- lid1+lid1_top x4 frames")

    # Reuse the base game's maintenance-panel overlay so the wire panel still draws.
    panel = BASE_RSI / "pod-panel.png"
    if panel.exists():
        shutil.copy(panel, rsi / "pod-panel.png")
        states.append({"name": "pod-panel"})
        print("  pod-panel <- base cryopod.rsi")

    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": COPYRIGHT,
        "size": {"x": TILE, "y": TILE * 2},
        "states": states,
    }
    (rsi / "meta.json").write_text(json.dumps(meta, indent=2) + "\n")
    print(f"eris_cryo.rsi: {len(states)} states")

    # Chem dispenser + ChemMaster (mixer) — append to eris_chemmachines.rsi.
    _, _, chem = parse_dmi(ERIS / "chemical.dmi")
    chem_rsi = OUT / "eris_chemmachines.rsi"
    meta_path = chem_rsi / "meta.json"
    meta = json.loads(meta_path.read_text())
    have = {s["name"] for s in meta["states"]}
    wanted = {
        "dispenser": "dispenser",
        "mixer0": "mixer0",
        "mixer0_nopower": "mixer0_nopower",
        "mixer1": "mixer1",
        "mixer1_nopower": "mixer1_nopower",
    }
    added = 0
    for out_name, dmi_state in wanted.items():
        if out_name in have:
            continue
        try:
            frame(chem, dmi_state, 0, 0).save(chem_rsi / f"{out_name}.png")
        except KeyError:
            print(f"  !! missing {dmi_state} in chemical.dmi")
            continue
        meta["states"].append({"name": out_name})
        added += 1
        print(f"  chemmachines += {out_name} <- {dmi_state}")
    meta_path.write_text(json.dumps(meta, indent=2) + "\n")
    print(f"eris_chemmachines.rsi: +{added} states")


if __name__ == "__main__":
    main()
