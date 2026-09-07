#!/usr/bin/env python3
"""Composite CEV-Eris 4-corner overlays into SS14 CardinalFlags 0–15 tiles.

IconSmooth CardinalFlags bits (IconSmoothSystem.cs): North=1 South=2 East=4 West=8.
Eris overlay rule: code/game/turfs/simulated/wall_icon.dm get_overlay_connection_type.
Diagonal neighbours are never set, so overlay type "full" is never selected.
"""

from __future__ import annotations

import json
from pathlib import Path

from PIL import Image, ImageChops

from eris_dmi import DIR_E, DIR_N, DIR_S, DIR_W, TILE, dir_frames, frame

# BYOND cardinal (same numeric values as SS14 CardinalFlags).
NORTH, SOUTH, EAST, WEST = 1, 2, 4, 8

# overlay_direction → (horizontal neighbour dir, vertical neighbour dir)
OVERLAY_NEIGHBOURS = {
    SOUTH: (WEST, SOUTH),  # bottom-left
    NORTH: (WEST, NORTH),  # top-left
    EAST: (EAST, NORTH),  # top-right
    WEST: (EAST, SOUTH),  # bottom-right
}

OVERLAY_DIR_INDEX = {SOUTH: DIR_S, NORTH: DIR_N, EAST: DIR_E, WEST: DIR_W}

EW_MASKS = (12, 13)  # E+W, N+E+W
REBAKE_Y0 = 8
DEFAULT_SAMPLE_X0 = 12  # hull audit: isolated columns 12–19


def has_dir(mask: int, d: int) -> bool:
    return bool(mask & d)


def connection_type(overlay_dir: int, mask: int) -> str:
    h_dir, v_dir = OVERLAY_NEIGHBOURS[overlay_dir]
    horizontal = has_dir(mask, h_dir)
    vertical = has_dir(mask, v_dir)
    if horizontal:
        if vertical:
            return "corner"  # diagonal never set under CardinalFlags
        return "horizontal"
    return "vertical" if vertical else "unconnected"


def composite_prefix(tiles: dict, prefix: str, mask: int) -> Image.Image:
    out = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))
    for overlay_dir in (SOUTH, NORTH, EAST, WEST):
        typ = connection_type(overlay_dir, mask)
        name = f"{prefix}_{typ}"
        fr = frame(tiles, name, OVERLAY_DIR_INDEX[overlay_dir])
        out = Image.alpha_composite(out, fr)
    return out


def composite_layers(layers: list[Image.Image]) -> Image.Image:
    out = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))
    for layer in layers:
        out = Image.alpha_composite(out, layer)
    return out


def ew_rebake(comp: Image.Image, isolated: Image.Image, sample_x0: int) -> Image.Image:
    """Keep y<8 from the CardinalFlags composite; tile an 8px isolated strip below."""
    out = comp.copy()
    px = out.load()
    src = isolated.load()
    for y in range(REBAKE_Y0, TILE):
        for x in range(TILE):
            sx = sample_x0 + (x % 8)
            px[x, y] = src[sx, y]
    return out


def band_variance(im: Image.Image, x0: int, x1: int, y0: int = REBAKE_Y0) -> float:
    """Mean per-pixel luminance variance of an 8-wide opaque band. Lower is flatter."""
    px = im.load()
    vals = []
    for y in range(y0, TILE):
        for x in range(x0, x1):
            r, g, b, a = px[x, y]
            if a < 8:
                continue
            vals.append(0.2126 * r + 0.7152 * g + 0.0722 * b)
    if len(vals) < 8:
        return 1e9
    mean = sum(vals) / len(vals)
    return sum((v - mean) ** 2 for v in vals) / len(vals)


def pick_sample_x0(isolated: Image.Image, prefer: int = DEFAULT_SAMPLE_X0) -> int:
    """Flattest opaque 8px column in y>=8. Prefer `prefer` if within 10% of the min."""
    scores = [(band_variance(isolated, x0, x0 + 8), x0) for x0 in range(0, TILE - 7)]
    scores.sort()
    best_var, best_x = scores[0]
    prefer_var = band_variance(isolated, prefer, prefer + 8)
    if prefer_var <= best_var * 1.10:
        return prefer
    return best_x


# CEV-Eris /turf/wall window_alpha. Glass is a separate overlay; SS14 has one
# sprite, so glass pixels must replace metal (not composite onto it).
WINDOW_ALPHA = 180


def apply_glass(wall: Image.Image, glass: Image.Image, alpha: int = WINDOW_ALPHA) -> Image.Image:
    """Punch glass through the wall: glass RGB at `alpha`, wall metal elsewhere."""
    out = wall.copy()
    wp = out.load()
    gp = glass.load()
    for y in range(TILE):
        for x in range(TILE):
            gr, gg, gb, ga = gp[x, y]
            if ga == 0:
                continue
            wp[x, y] = (gr, gg, gb, alpha)
    return out


def bake_mask(
    tiles: dict,
    prefixes: list[str],
    mask: int,
    isolated: Image.Image | None = None,
    sample_x0: int = DEFAULT_SAMPLE_X0,
    tint_last: float | None = None,
    glass_alpha: int | None = None,
) -> Image.Image:
    layers = [composite_prefix(tiles, p, mask) for p in prefixes]
    if tint_last is not None and layers:
        layers[-1] = tint_layer(layers[-1], tint_last)
    if glass_alpha is not None and len(layers) >= 2:
        wall = layers[0]
        for extra in layers[1:-1]:
            wall = Image.alpha_composite(wall, extra)
        comp = apply_glass(wall, layers[-1], glass_alpha)
    else:
        comp = composite_layers(layers)
    if mask in EW_MASKS and isolated is not None:
        return ew_rebake(comp, isolated, sample_x0)
    return comp


def tint_layer(im: Image.Image, factor: float) -> Image.Image:
    out = im.copy()
    px = out.load()
    for y in range(TILE):
        for x in range(TILE):
            r, g, b, a = px[x, y]
            if a == 0:
                continue
            px[x, y] = (int(r * factor), int(g * factor), int(b * factor), a)
    return out


def bake_cardinal_set(
    tiles: dict,
    prefixes: list[str],
    tint_last: float | None = None,
    rebake: bool = True,
    glass_alpha: int | None = None,
) -> dict[int, Image.Image]:
    isolated = bake_mask(
        tiles, prefixes, 0, tint_last=tint_last, glass_alpha=glass_alpha
    )
    sample_x0 = pick_sample_x0(isolated)
    out = {}
    for mask in range(16):
        out[mask] = bake_mask(
            tiles,
            prefixes,
            mask,
            isolated=isolated if rebake else None,
            sample_x0=sample_x0,
            tint_last=tint_last,
            glass_alpha=glass_alpha,
        )
    return out


def pack_dirs(frames: list[Image.Image], cols: int = 2) -> Image.Image:
    n = len(frames)
    rows = (n + cols - 1) // cols
    out = Image.new("RGBA", (cols * TILE, rows * TILE), (0, 0, 0, 0))
    for i, im in enumerate(frames):
        r, c = divmod(i, cols)
        out.paste(im, (c * TILE, r * TILE))
    return out


def write_rsi(rsi_dir: Path, files: dict[str, Image.Image], states: list[dict], copyright: str):
    rsi_dir.mkdir(parents=True, exist_ok=True)
    for name, im in files.items():
        im.save(rsi_dir / f"{name}.png")
    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": copyright,
        "size": {"x": TILE, "y": TILE},
        "states": states,
    }
    (rsi_dir / "meta.json").write_text(json.dumps(meta, indent=2) + "\n")


def pixel_diff(a: Image.Image, b: Image.Image) -> int:
    if a.size != b.size:
        return a.size[0] * a.size[1]
    d = ImageChops.difference(a.convert("RGBA"), b.convert("RGBA"))
    return sum(1 for px in d.getdata() if px[0] or px[1] or px[2] or px[3])


def verify_against_hulls() -> None:
    """Rebuild eris_wall CardinalFlags and compare to committed walls.rsi."""
    from eris_dmi import parse_dmi

    eris = Path("/Users/russellrozario/Desktop/SS13-14/CEV-Eris/icons/walls.dmi")
    hull = Path(
        "/Users/russellrozario/Desktop/SS13-14/Oxyd14/Resources/Textures/Oxyd/erisported/walls.rsi"
    )
    _, _, tiles = parse_dmi(eris)
    baked = bake_cardinal_set(tiles, ["eris_wall"], rebake=False)
    print(f"hull sample_x0={pick_sample_x0(baked[0])}")
    for mask in range(16):
        committed = Image.open(hull / f"solid{mask}.png").convert("RGBA")
        diffs = pixel_diff(baked[mask], committed)
        print(f"solid{mask}: {diffs} diffs")


if __name__ == "__main__":
    verify_against_hulls()
