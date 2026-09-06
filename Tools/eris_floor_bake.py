#!/usr/bin/env python3
"""Bake CEV-Eris floor rim overlays into canonical TileBorder adjacency-mask RSIs.

Source of truth: CEV-Eris flooring DMIs (icon_base_edges / icon_base_corners).
Runtime keeps Decal.Angle — we bake only rotation-canonical masks (bit-rotate by 2
under 90°), ~69 frames in 0..254. Interior 0xFF is not baked (no rim).

Bit order matches Robust Direction / TileBorderMask:
  S=0, SE=1, E=2, NE=3, N=4, NW=5, W=6, SW=7
  bit i set => neighbour present in Direction i (same border group).

BYOND DMI dirs=8 storage order (verified empirically on tiles_steel edges):
  0=S, 1=N, 2=E, 3=W, 4=SE, 5=SW, 6=NE, 7=NW

Eris floor_icon.dm overlay rules (linked = neighbour present):
  - cardinal edge from _edges when cardinal neighbour ABSENT
  - outer corner from _edges diagonal when BOTH adjacent cardinals ABSENT
  - inner corner from _corners diagonal when BOTH adjacent cardinals PRESENT
    and diagonal ABSENT (only if TURF_HAS_INNER_CORNERS)
"""

from __future__ import annotations

import argparse
import json
import shutil
from pathlib import Path

from PIL import Image

from eris_cardinal_bake import TILE, write_rsi
from eris_dmi import frame, parse_dmi

ERIS_FLOORING = Path("/Users/russellrozario/Desktop/SS13-14/CEV-Eris/icons/turf/flooring")
OXYD = Path(__file__).resolve().parents[1]
OUT_TEX = OXYD / "Resources/Textures/Oxyd/erisported"
OUT_YAML = OXYD / "Resources/Prototypes/_Oxyd/Decals/tile_borders.yml"

COPYRIGHT = "Ported station floor art (https://github.com/discordia-space/CEV-Eris)."

# BYOND dirs=8 indices into DMI state images.
BYOND8_S, BYOND8_N, BYOND8_E, BYOND8_W = 0, 1, 2, 3
BYOND8_SE, BYOND8_SW, BYOND8_NE, BYOND8_NW = 4, 5, 6, 7

# Robust Direction bit indices.
BIT_S, BIT_SE, BIT_E, BIT_NE = 0, 1, 2, 3
BIT_N, BIT_NW, BIT_W, BIT_SW = 4, 5, 6, 7

OLD_PIECE_STATES = (
    "n", "s", "e", "w",
    "out-ne", "out-nw", "out-se", "out-sw",
    "in-ne", "in-nw", "in-se", "in-sw",
)

# stem -> (dmi filename under icons/turf/flooring, icon_base, has_inner_corners)
# Inner corners: plating/under/hull explicit; steel/white/dark/techmaint inherit
# TURF_HAS_INNER_CORNERS from /decl/flooring/tiling.
FLOOR_MAP: list[tuple[str, str, str, bool]] = [
    # --- foundation 19 (do not modify / do not re-bake unless intentional) ---
    ("tiles_steel", "tiles_steel.dmi", "tiles", True),
    ("steel_gray_perforated", "tiles_steel.dmi", "gray_perforated", True),
    ("steel_gray_platform", "tiles_steel.dmi", "gray_platform", True),
    ("steel_cargo", "tiles_steel.dmi", "cargo", True),
    ("techmaint", "tiles_maint.dmi", "techmaint", True),
    ("tiles_white", "tiles_white.dmi", "tiles", True),
    ("white_brown_perforated", "tiles_white.dmi", "brown_perforated", True),
    ("tiles_dark", "tiles_dark.dmi", "tiles", True),
    ("dark_gray_platform", "tiles_dark.dmi", "gray_platform", True),
    ("dark_techfloor", "tiles_dark.dmi", "techfloor", True),
    ("steel_techfloor_grid", "tiles_steel.dmi", "techfloor_grid", True),
    ("hullcenter", "hull.dmi", "hullcenter", True),
    ("plating", "plating.dmi", "plating", True),
    ("under", "plating.dmi", "under", True),
    ("steel_techfloor", "tiles_steel.dmi", "techfloor", True),
    ("steel_orangecorner", "tiles_steel.dmi", "orangecorner", True),
    ("steel_bluecorner", "tiles_steel.dmi", "bluecorner", True),
    ("steel_monofloor", "tiles_steel.dmi", "monofloor", True),
    ("techmaint_panels", "tiles_maint.dmi", "techmaint_panels", True),
    # --- PR C mapper-only ZERO/WEAK (append; leave A strong retargets alone) ---
    # Derelict (has_inner=False — edges dirs=8, no _corners)
    ("derelict1", "derelict.dmi", "derelict1", False),
    ("derelict2", "derelict.dmi", "derelict2", False),
    ("derelict3", "derelict.dmi", "derelict3", False),
    ("derelict4", "derelict.dmi", "derelict4", False),
    # Steel
    ("steel_danger", "tiles_steel.dmi", "danger", True),
    ("steel_cyancorner", "tiles_steel.dmi", "cyancorner", True),
    ("steel_violetcorener", "tiles_steel.dmi", "violetcorener", True),
    ("steel_brown_platform", "tiles_steel.dmi", "brown_platform", True),
    ("steel_panels", "tiles_steel.dmi", "panels", True),
    ("steel_brown_perforated", "tiles_steel.dmi", "brown_perforated", True),
    ("steel_bar_dance", "tiles_steel.dmi", "bar_dance", True),
    ("steel_bar_light", "tiles_steel.dmi", "bar_light", True),
    # White
    ("white_danger", "tiles_white.dmi", "danger", True),
    ("white_cyancorner", "tiles_white.dmi", "cyancorner", True),
    ("white_violetcorener", "tiles_white.dmi", "violetcorener", True),
    ("white_brown_platform", "tiles_white.dmi", "brown_platform", True),
    ("white_panels", "tiles_white.dmi", "panels", True),
    ("white_techfloor", "tiles_white.dmi", "techfloor", True),
    ("white_techfloor_grid", "tiles_white.dmi", "techfloor_grid", True),
    ("white_gray_perforated", "tiles_white.dmi", "gray_perforated", True),
    ("white_cargo", "tiles_white.dmi", "cargo", True),
    ("white_gray_platform", "tiles_white.dmi", "gray_platform", True),
    ("white_bluecorner", "tiles_white.dmi", "bluecorner", True),
    ("white_orangecorner", "tiles_white.dmi", "orangecorner", True),
    ("white_monofloor", "tiles_white.dmi", "monofloor", True),
    # Dark
    ("dark_danger", "tiles_dark.dmi", "danger", True),
    ("dark_cyancorner", "tiles_dark.dmi", "cyancorner", True),
    ("dark_violetcorener", "tiles_dark.dmi", "violetcorener", True),
    ("dark_brown_platform", "tiles_dark.dmi", "brown_platform", True),
    ("dark_panels", "tiles_dark.dmi", "panels", True),
    ("dark_techfloor_grid", "tiles_dark.dmi", "techfloor_grid", True),
    ("dark_brown_perforated", "tiles_dark.dmi", "brown_perforated", True),
    ("dark_gray_perforated", "tiles_dark.dmi", "gray_perforated", True),
    ("dark_cargo", "tiles_dark.dmi", "cargo", True),
    ("dark_bluecorner", "tiles_dark.dmi", "bluecorner", True),
    ("dark_orangecorner", "tiles_dark.dmi", "orangecorner", True),
    ("dark_monofloor", "tiles_dark.dmi", "monofloor", True),
]


def linked(mask: int, bit: int) -> bool:
    return bool(mask & (1 << bit))


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


def composite_rim(tiles: dict, icon_base: str, mask: int, has_inner: bool) -> Image.Image:
    """Alpha-composite Eris edge/corner overlays for one adjacency mask."""
    edges = f"{icon_base}_edges"
    corners = f"{icon_base}_corners"
    out = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))

    n = linked(mask, BIT_N)
    s = linked(mask, BIT_S)
    e = linked(mask, BIT_E)
    w = linked(mask, BIT_W)
    ne = linked(mask, BIT_NE)
    nw = linked(mask, BIT_NW)
    se = linked(mask, BIT_SE)
    sw = linked(mask, BIT_SW)

    def add(state: str, dir_index: int) -> None:
        nonlocal out
        out = Image.alpha_composite(out, frame(tiles, state, dir_index))

    # Cardinal edges — neighbour ABSENT
    if not s:
        add(edges, BYOND8_S)
    if not n:
        add(edges, BYOND8_N)
    if not e:
        add(edges, BYOND8_E)
    if not w:
        add(edges, BYOND8_W)

    # Outer corners — both adjacent cardinals ABSENT (from _edges diagonals)
    if not n and not e:
        add(edges, BYOND8_NE)
    if not n and not w:
        add(edges, BYOND8_NW)
    if not s and not e:
        add(edges, BYOND8_SE)
    if not s and not w:
        add(edges, BYOND8_SW)

    # Inner corners — both cardinals PRESENT, diagonal ABSENT (from _corners)
    if has_inner:
        if n and e and not ne:
            add(corners, BYOND8_NE)
        if n and w and not nw:
            add(corners, BYOND8_NW)
        if s and e and not se:
            add(corners, BYOND8_SE)
        if s and w and not sw:
            add(corners, BYOND8_SW)

    return out


def clear_rsi_pngs(rsi_dir: Path) -> None:
    """Remove PNG states inside the RSI (never touches sibling *_base.png)."""
    if not rsi_dir.is_dir():
        return
    for p in rsi_dir.glob("*.png"):
        p.unlink()


def ensure_fill(tiles: dict, icon_base: str, stem: str) -> None:
    """Write {stem}_base.png from DMI icon_base frame 0 if missing. Never overwrite."""
    base_png = OUT_TEX / f"{stem}_base.png"
    if base_png.is_file():
        return
    if icon_base not in tiles:
        raise KeyError(f"missing fill state {icon_base!r} for stem {stem}")
    img = frame(tiles, icon_base, 0)
    base_png.parent.mkdir(parents=True, exist_ok=True)
    img.save(base_png)
    print(f"fill: wrote {base_png.name}")


def bake_floor(
    stem: str,
    dmi_name: str,
    icon_base: str,
    has_inner: bool,
    tiles_cache: dict[str, dict],
    masks: list[int],
) -> int:
    dmi_path = ERIS_FLOORING / dmi_name
    if dmi_name not in tiles_cache:
        _, _, tiles_cache[dmi_name] = parse_dmi(dmi_path)
    tiles = tiles_cache[dmi_name]

    edges = f"{icon_base}_edges"
    corners = f"{icon_base}_corners"
    if edges not in tiles or tiles[edges]["dirs"] != 8:
        raise KeyError(f"{dmi_name}: missing dirs=8 state {edges!r}")
    if has_inner and (corners not in tiles or tiles[corners]["dirs"] != 8):
        raise KeyError(f"{dmi_name}: missing dirs=8 state {corners!r}")

    ensure_fill(tiles, icon_base, stem)

    rsi_dir = OUT_TEX / f"{stem}.rsi"
    files: dict[str, Image.Image] = {}
    states: list[dict] = []
    for mask in masks:
        name = state_name(mask)
        files[name] = composite_rim(tiles, icon_base, mask, has_inner)
        states.append({"name": name})

    clear_rsi_pngs(rsi_dir)
    write_rsi(rsi_dir, files, states, COPYRIGHT)

    return len(files)


def generate_yaml(stems: list[str], masks: list[int]) -> None:
    """Full rewrite — kept for reference; prefer append_yaml for PR C+."""
    lines = [
        "# Server-generated floor rims. Not mapper-placeable; stripped from map YAML.",
        "# States are rotation-canonical adjacency masks (hex, no 0x): \"00\"..\"fe\".",
        "# Interior 0xFF is not baked. Runtime applies Decal.Angle for non-canonical orientations.",
        "# Generated by Tools/eris_floor_bake.py — do not hand-edit.",
        "- type: decal",
        "  abstract: true",
        "  id: TileBorderBase",
        "  tags: [\"tile-border\"]",
        "  showMenu: false",
        "  defaultCleanable: false",
        "  defaultSnap: true",
        "",
    ]
    for stem in stems:
        lines.append(f"# {stem}.rsi")
        for mask in masks:
            st = state_name(mask)
            lines.append("- type: decal")
            lines.append("  parent: TileBorderBase")
            lines.append(f"  id: TileBorder-{stem}-{st}")
            lines.append("  sprite:")
            lines.append(f"    sprite: Oxyd/erisported/{stem}.rsi")
            lines.append(f"    state: {st}")
            lines.append("")
    OUT_YAML.write_text("\n".join(lines))


def yaml_has_stem(text: str, stem: str) -> bool:
    """True if tile_borders.yml already contains this stem's section."""
    if f"# {stem}.rsi" in text:
        return True
    if f"id: TileBorder-{stem}-00" in text:
        return True
    return False


def append_yaml(stems: list[str], masks: list[int]) -> list[str]:
    """Append # {stem}.rsi sections for stems not already present. Never deletes."""
    if not OUT_YAML.is_file():
        raise FileNotFoundError(f"missing {OUT_YAML}; cannot append without existing YAML")
    text = OUT_YAML.read_text()
    # Ensure trailing newline before append
    if text and not text.endswith("\n"):
        text += "\n"

    added: list[str] = []
    chunks: list[str] = []
    for stem in stems:
        if yaml_has_stem(text, stem):
            print(f"yaml: skip existing {stem}")
            continue
        lines = [f"# {stem}.rsi"]
        for mask in masks:
            st = state_name(mask)
            lines.append("- type: decal")
            lines.append("  parent: TileBorderBase")
            lines.append(f"  id: TileBorder-{stem}-{st}")
            lines.append("  sprite:")
            lines.append(f"    sprite: Oxyd/erisported/{stem}.rsi")
            lines.append(f"    state: {st}")
            lines.append("")
        chunks.append("\n".join(lines))
        added.append(stem)

    if chunks:
        with OUT_YAML.open("a", encoding="utf-8") as f:
            if not text.endswith("\n\n") and not text.endswith("\n"):
                f.write("\n")
            elif not text.endswith("\n"):
                f.write("\n")
            for chunk in chunks:
                f.write(chunk)
                if not chunk.endswith("\n"):
                    f.write("\n")
        print(f"yaml: appended {len(added)} stems -> {OUT_YAML}")
    else:
        print("yaml: nothing to append")
    return added


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--only",
        nargs="*",
        help="Bake only these RSI stems (default: all FLOOR_MAP)",
    )
    parser.add_argument(
        "--skip-yaml",
        action="store_true",
        help="Do not append tile_borders.yml",
    )
    args = parser.parse_args()

    masks = enumerate_canonical_masks()
    assert len(masks) == 69, f"expected 69 canonical masks, got {len(masks)}"
    assert 0xFF not in masks
    assert 0 in masks

    selected = FLOOR_MAP
    if args.only:
        want = set(args.only)
        selected = [row for row in FLOOR_MAP if row[0] in want]
        missing = want - {row[0] for row in selected}
        if missing:
            raise SystemExit(f"unknown stems: {sorted(missing)}")

    cache: dict[str, dict] = {}
    counts: dict[str, int] = {}
    for stem, dmi, icon_base, has_inner in selected:
        n = bake_floor(stem, dmi, icon_base, has_inner, cache, masks)
        counts[stem] = n
        print(f"{stem}: {n} states -> {OUT_TEX / (stem + '.rsi')}")

    if not args.skip_yaml:
        # APPEND-ONLY: never rewrite whole tile_borders.yml.
        # When --only: append just those stems; when all: append any missing from FLOOR_MAP.
        yaml_stems = [row[0] for row in selected]
        appended = append_yaml(yaml_stems, masks)
        print(f"yaml append-only: requested={len(yaml_stems)} added={len(appended)}")

    print(f"canonical_masks={len(masks)} names={state_name(masks[0])}..{state_name(masks[-1])}")
    print(f"OXYD={OXYD}")


if __name__ == "__main__":
    main()
