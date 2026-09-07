#!/usr/bin/env python3
"""Render a visual preview sheet for the lattice directionality fix.

Purely diagnostic: reads the CEV-Eris DMI and the baked RSI, composites the
goal scenarios the way the game will render them (one Eris frame per tile over
space), and writes PNGs + an HTML contact sheet under .freebuff/lattice_preview.
"""
from __future__ import annotations

import sys
from pathlib import Path

from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "Tools"))

from eris_dmi import DIR_S, frame, parse_dmi  # noqa: E402

HERE = Path(__file__).resolve().parent
DMI = Path("/Users/russellrozario/Desktop/SS13-14/CEV-Eris/icons/obj/smoothlattice.dmi")

BG = (12, 14, 22, 255)
LABEL = (210, 220, 245, 255)
CELL = 6  # pixel scale for scenario canvases


def eframe(tiles, d: int) -> Image.Image:
    st = tiles[f"lattice{d}"]
    return frame(tiles, f"lattice{d}", DIR_S if st["dirs"] == 4 else 0, 0).convert("RGBA")


def dir_sum(north: bool, south: bool, east: bool, west: bool) -> int:
    d = 0
    if north:
        d |= 1
    if south:
        d |= 2
    if east:
        d |= 4
    if west:
        d |= 8
    return d


def draw_label(img: Image.Image, text: str) -> Image.Image:
    d = ImageDraw.Draw(img)
    d.text((8, 6), text, fill=LABEL)
    return img


def scenario(tiles, kind: str, isolated) -> Image.Image:
    """kind -> (w,h, fn(cx,cy)->dirsum|-1). Frames drawn over BG; faint cell grid."""
    grids = {
        "H pair": (2, 1, lambda x, y: 4 if x == 0 else 8),
        "V pair": (1, 2, lambda x, y: 1 if y == 1 else 2),
        "L triomino (center N+E)": (3, 2, lambda x, y: {
            (1, 1): dir_sum(True, False, True, False),   # center N+E
            (2, 1): dir_sum(False, False, False, True),  # W-only (east of center)
            (1, 0): dir_sum(False, True, False, False),  # S-only (north of center)
        }[(x, y)] if (x, y) in ((1, 1), (2, 1), (1, 0)) else -1),
        "3x3 plaza": (3, 3, lambda x, y: dir_sum(
            y > 0, y < 2, x < 2, x > 0) if 0 <= x < 3 and 0 <= y < 3 else -1),
        "isolated": (1, 1, lambda x, y: 0),
    }
    w, h, fn = grids[kind]
    raw = Image.new("RGBA", (w * 32, h * 32), (0, 0, 0, 0))
    px = raw.load()
    for yy in range(raw.height):
        for xx in range(raw.width):
            px[xx, yy] = BG
    for cy in range(h):
        for cx in range(w):
            ds = fn(cx, cy)
            if ds < 0:
                continue
            art = isolated if ds == 0 else eframe(tiles, ds)
            raw.paste(art, (cx * 32, cy * 32), art)
    big = raw.resize((raw.width * CELL, raw.height * CELL), Image.NEAREST)
    return draw_label(big, kind)


def hstack(imgs, pad=14, gap=28, header: str = "") -> Image.Image:
    W = sum(i.width for i in imgs) + gap * (len(imgs) - 1) + 2 * pad
    H = max(i.height for i in imgs) + 2 * pad
    out = Image.new("RGBA", (W, H), (8, 9, 14, 255))
    x = pad
    for i in imgs:
        out.paste(i, (x, pad), i)
        x += i.width + gap
    if header:
        draw_label(out, header)
    return out


def main() -> None:
    _, _, tiles = parse_dmi(DMI)
    l0 = eframe(tiles, 0)

    def mask_ring(pxs: int) -> Image.Image:
        im = l0.copy()
        p = im.load()
        for y in range(32):
            for x in range(32):
                if x < pxs or x > 31 - pxs or y < pxs or y > 31 - pxs:
                    p[x, y] = (0, 0, 0, 0)
        return im

    def scaled(sz: int) -> Image.Image:
        im = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
        small = l0.resize((sz, sz), Image.LANCZOS)
        off = (32 - sz) // 2
        im.alpha_composite(small, (off, off))
        p = im.load()
        for y in range(32):
            for x in range(32):
                if p[x, y][3] < 60:
                    p[x, y] = (0, 0, 0, 0)
        return im

    cands = {
        "A: trim 5px margin (crisp)": mask_ring(5),
        "B: shrink to 23px": scaled(23),
        "C: shrink to 26px": scaled(26),
        "current full mesh (lattice0)": l0,
    }

    # Candidates at CELL scale with name plates.
    bigs = []
    for name, art in cands.items():
        cell = Image.new("RGBA", (32 * CELL, 32 * CELL + 20), (0, 0, 0, 0))
        cell.alpha_composite(art.resize((32 * CELL, 32 * CELL), Image.NEAREST))
        bigs.append(draw_label(cell, name))
    cand_sheet = hstack(bigs, header="ISOLATED TILE (dir_sum 0) candidates - pick one look")

    # Scenario canvases using candidate A for the isolated cell.
    iso = cands["A: trim 5px margin (crisp)"]
    scens = [scenario(tiles, k, iso) for k in
             ("isolated", "H pair", "V pair", "L triomino (center N+E)", "3x3 plaza")]
    scen_sheet = hstack(scens, header="GOAL SCENARIOS (frames over empty space; isolated uses candidate A)")

    both = Image.new("RGBA", (max(cand_sheet.width, scen_sheet.width) + 24,
                              cand_sheet.height + scen_sheet.height + 36), (8, 9, 14, 255))
    both.paste(cand_sheet, (12, 12), cand_sheet)
    both.paste(scen_sheet, (12, cand_sheet.height + 24), scen_sheet)
    out = HERE / "lattice_preview.png"
    both.save(out)
    print("saved", out, both.size)


if __name__ == "__main__":
    main()
