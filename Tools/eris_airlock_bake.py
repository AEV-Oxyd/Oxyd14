#!/usr/bin/env python3
"""Bake CEV-Eris 1-tile airlock DMIs into Oxyd14 4-dir RSIs."""

from __future__ import annotations

import json
import struct
import zlib
from pathlib import Path

from PIL import Image

ERIS_DOORS = Path("/Users/russellrozario/Desktop/SS13-14/CEV-Eris/icons/obj/doors")
OUT = Path("/Users/russellrozario/Desktop/SS13-14/Oxyd14/Resources/Textures/Oxyd/erisported/airlocks")

COPYRIGHT = (
    "Airlock states taken from CEV-Eris icons/obj/doors "
    "(https://github.com/discordia-space/CEV-Eris)."
)

TILE = 32
DIRS = 4  # S N E W

SOLID_BODIES = {
    "int": "doorint.dmi",
    "command": "Doorcom.dmi",
    "security": "Doorsec.dmi",
    "engineering": "Dooreng.dmi",
    "atmos": "Dooratmo.dmi",
    "medical": "doormed.dmi",
    "science": "doorsci.dmi",
    "mining": "Doormining.dmi",
    "maint": "Doormaint.dmi",
    "external": "Doorext.dmi",
    "freezer": "Doorfreezer.dmi",
    "centcomm": "Doorele.dmi",
}

GLASS_BODIES = {
    "int": "Doorglass.dmi",
    "command": "Doorcomglass.dmi",
    "security": "Doorsecglass.dmi",
    "engineering": "Doorengglass.dmi",
    "atmos": "Dooratmoglass.dmi",
    "medical": "doormedglass.dmi",
    "science": "doorsciglass.dmi",
    "mining": "Doorminingglass.dmi",
    "external": "Doorext.dmi",
}

ASSEMBLY = {
    "int": "door_as_0",
    "command": "door_as_com0",
    "security": "door_as_sec0",
    "engineering": "door_as_eng0",
    "atmos": "door_as_atmo0",
    "medical": "door_as_med0",
    "science": "door_as_sci0",
    "mining": "door_as_ming0",
    "maint": "door_as_mai0",
    "external": "door_as_ext0",
    "freezer": "door_as_fre0",
    "centcomm": "door_as_0",
}

ASSEMBLY_GLASS = {
    "int": "door_as_g0",
    "command": "door_as_gcom0",
    "security": "door_as_gsec0",
    "engineering": "door_as_geng0",
    "atmos": "door_as_gatmo0",
    "medical": "door_as_gmed0",
    "science": "door_as_gsci0",
    "mining": "door_as_gming0",
    "external": "door_as_ext0",
}


def png_chunks(data: bytes):
    i = 8
    while i < len(data):
        ln = struct.unpack(">I", data[i : i + 4])[0]
        typ = data[i + 4 : i + 8]
        payload = data[i + 8 : i + 8 + ln]
        yield typ, payload
        i += 12 + ln
        if typ == b"IEND":
            break


def parse_dmi(path: Path):
    data = path.read_bytes()
    meta = None
    for typ, payload in png_chunks(data):
        if typ != b"zTXt":
            continue
        _, rest = payload.split(b"\x00", 1)
        if rest[:1] != b"\x00":
            continue
        text = zlib.decompress(rest[1:]).decode("utf-8", "replace")
        if "# BEGIN DMI" in text:
            meta = text
            break
    if meta is None:
        raise RuntimeError(f"no DMI meta in {path}")

    tw = th = 32
    states = []
    cur = None
    for line in meta.splitlines():
        s = line.strip()
        if s.startswith("width ="):
            tw = int(s.split("=")[1])
        elif s.startswith("height ="):
            th = int(s.split("=")[1])
        elif s.startswith("state ="):
            cur = {
                "name": s.split("=", 1)[1].strip().strip('"'),
                "dirs": 1,
                "frames": 1,
            }
            states.append(cur)
        elif cur and s.startswith("dirs ="):
            cur["dirs"] = int(s.split("=")[1])
        elif cur and s.startswith("frames ="):
            cur["frames"] = int(s.split("=")[1])

    sheet = Image.open(path).convert("RGBA")
    cols = sheet.width // tw
    tiles = {}
    idx = 0
    for st in states:
        n = st["dirs"] * st["frames"]
        frames = []
        for _ in range(n):
            r, c = divmod(idx, cols)
            frames.append(
                sheet.crop((c * tw, r * th, c * tw + tw, r * th + th))
            )
            idx += 1
        # dirs=1: frames[f]; dirs=4: BYOND order is frame-major then dir
        tiles[st["name"]] = {
            "dirs": st["dirs"],
            "frames": st["frames"],
            "images": frames,
        }
    return tw, th, tiles


def first_frame(tiles, name) -> Image.Image | None:
    st = tiles.get(name)
    if not st or not st["images"]:
        return None
    return st["images"][0]


def anim_frames(tiles, name) -> list[Image.Image]:
    st = tiles.get(name)
    if not st:
        return []
    # dirs=1: one image per frame
    if st["dirs"] == 1:
        return list(st["images"])
    # dirs=4: frame-major, take dir 0 of each frame
    out = []
    for f in range(st["frames"]):
        out.append(st["images"][f * st["dirs"]])
    return out


def pack_rsi(tiles: list[Image.Image], cols: int | None = None) -> Image.Image:
    """Pack tiles LTR, wrapping at cols. Default: 2 for 4 stills, else len/4."""
    n = len(tiles)
    if cols is None:
        cols = 2 if n == 4 else max(1, n // 4 if n >= 4 else n)
    rows = (n + cols - 1) // cols
    out = Image.new("RGBA", (cols * TILE, rows * TILE), (0, 0, 0, 0))
    for i, im in enumerate(tiles):
        r, c = divmod(i, cols)
        out.paste(im, (c * TILE, r * TILE))
    return out


def dup4(im: Image.Image) -> list[Image.Image]:
    return [im.copy() for _ in range(DIRS)]


def dup4_anim(frames: list[Image.Image]) -> list[Image.Image]:
    # RSI order: all frames of dir0, then dir1, ...
    out = []
    for _ in range(DIRS):
        out.extend(im.copy() for im in frames)
    return out


def delays_for(n: int, total: float) -> list[float]:
    if n <= 0:
        return [total]
    d = round(total / n, 4)
    vals = [d] * n
    vals[-1] = round(total - d * (n - 1), 4)
    return vals


def px_dist(a, b) -> int:
    return abs(a[0] - b[0]) + abs(a[1] - b[1]) + abs(a[2] - b[2])


def is_emissive(px) -> bool:
    r, g, b, a = px
    if a < 8:
        return False
    mx, mn = max(r, g, b), min(r, g, b)
    return mx > 140 and (mx - mn) > 40


def light_mask(closed: Image.Image, locked: Image.Image) -> set[tuple[int, int]]:
    """Status lights: pixels that change closed→locked and look emissive."""
    mask = set()
    for y in range(TILE):
        for x in range(TILE):
            c = closed.getpixel((x, y))
            l = locked.getpixel((x, y))
            if c != l and (is_emissive(c) or is_emissive(l)):
                mask.add((x, y))
    return mask


def extract_unlit(src: Image.Image, mask: set[tuple[int, int]]) -> Image.Image:
    out = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))
    for x, y in mask:
        px = src.getpixel((x, y))
        if px[3] > 0:
            out.putpixel((x, y), px)
    return out


def strip_lights(src: Image.Image, mask: set[tuple[int, int]]) -> Image.Image:
    out = src.copy()
    px = out.load()
    for x, y in mask:
        # nearest non-mask neighbor
        fill = None
        for rad in range(1, 6):
            for dy in range(-rad, rad + 1):
                for dx in range(-rad, rad + 1):
                    nx, ny = x + dx, y + dy
                    if 0 <= nx < TILE and 0 <= ny < TILE and (nx, ny) not in mask:
                        cand = px[nx, ny]
                        if cand[3] > 0:
                            fill = cand
                            break
                if fill:
                    break
            if fill:
                break
        if fill is None:
            r, g, b, a = px[x, y]
            fill = (max(0, r // 3), max(0, g // 3), max(0, b // 3), a)
        px[x, y] = fill
    return out


def moving_light_mask(frame: Image.Image, colors: list[tuple], thresh: int = 48) -> set[tuple[int, int]]:
    mask = set()
    if not colors:
        return mask
    for y in range(TILE):
        for x in range(TILE):
            px = frame.getpixel((x, y))
            if px[3] < 8:
                continue
            if min(px_dist(px[:3], c) for c in colors) <= thresh:
                mask.add((x, y))
    return mask


def write_meta(path: Path, states: list[dict]):
    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": COPYRIGHT,
        "size": {"x": TILE, "y": TILE},
        "states": states,
    }
    path.write_text(json.dumps(meta, indent=2) + "\n")


def state_still(name: str) -> dict:
    return {"name": name, "directions": 4}


def state_anim(name: str, n: int, total: float) -> dict:
    d = delays_for(n, total)
    return {"name": name, "directions": 4, "delays": [d, d, d, d]}


def save_rsi(rsi_dir: Path, files: dict[str, Image.Image], states: list[dict]):
    rsi_dir.mkdir(parents=True, exist_ok=True)
    for name, im in files.items():
        im.save(rsi_dir / f"{name}.png")
    write_meta(rsi_dir / "meta.json", states)


def load_assembly():
    _, _, tiles = parse_dmi(ERIS_DOORS / "door_assembly.dmi")
    return tiles


def bake_body(name: str, dmi: str, assembly_key: str, assembly_tiles, fallback_tiles=None):
    _, _, tiles = parse_dmi(ERIS_DOORS / dmi)
    closed = first_frame(tiles, "door_closed")
    locked = first_frame(tiles, "door_locked") or closed
    opened = first_frame(tiles, "door_open")
    if closed is None or opened is None:
        raise RuntimeError(f"{dmi} missing closed/open")

    opening = anim_frames(tiles, "door_opening")
    closing = anim_frames(tiles, "door_closing")
    if not opening:
        opening = [closed, opened]
    if not closing:
        closing = list(reversed(opening))

    mask = light_mask(closed, locked)
    light_colors = [closed.getpixel(p)[:3] for p in mask] + [
        locked.getpixel(p)[:3] for p in mask
    ]
    light_colors = [c for c in light_colors if max(c) > 20]

    body_closed = strip_lights(closed, mask)
    open_mask = moving_light_mask(opened, light_colors) & mask if light_colors else mask
    body_open = strip_lights(opened, open_mask if open_mask else mask)

    # Keep baked lights on opening/closing frames; unlit flicks stay at the
    # idle status-light pixels so AirlockSystem does not overlay door trim.
    empty = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))
    body_opening = list(opening)
    unlit_opening = [extract_unlit(fr, mask) if i == 0 else empty for i, fr in enumerate(opening)]
    body_closing = list(closing)
    unlit_closing = [extract_unlit(fr, mask) if i == 0 else empty for i, fr in enumerate(closing)]

    assembly_im = first_frame(assembly_tiles, assembly_key)
    if assembly_im is None:
        assembly_im = body_closed

    files = {
        "closed": pack_rsi(dup4(body_closed), 2),
        "open": pack_rsi(dup4(body_open), 2),
        "assembly": pack_rsi(dup4(assembly_im), 2),
        "opening": pack_rsi(dup4_anim(body_opening), len(body_opening)),
        "closing": pack_rsi(dup4_anim(body_closing), len(body_closing)),
    }
    states = [
        state_still("assembly"),
        state_anim("opening", len(body_opening), 0.8),
        state_still("closed"),
        state_still("open"),
        state_anim("closing", len(body_closing), 0.8),
    ]
    extras = {
        "mask": mask,
        "light_colors": light_colors,
        "tiles": tiles,
        "closed": closed,
        "locked": locked,
        "opened": opened,
        "unlit_closed": extract_unlit(closed, mask),
        "unlit_bolted": extract_unlit(locked, mask),
        "unlit_open": extract_unlit(opened, moving_light_mask(opened, light_colors) or mask),
        "unlit_opening": unlit_opening,
        "unlit_closing": unlit_closing,
        "n_open": len(body_opening),
        "n_close": len(body_closing),
    }
    return files, states, extras


def bake_effects(extras_src, label: str):
    tiles = extras_src["tiles"]
    mask = extras_src["mask"]

    deny_frames = anim_frames(tiles, "door_deny")
    if not deny_frames:
        deny_frames = [extras_src["closed"]]
    deny_unlit = [extract_unlit(fr, mask) for fr in deny_frames]

    spark = first_frame(tiles, "door_spark") or first_frame(tiles, "sparks_damaged")
    sparks_broken = first_frame(tiles, "sparks_broken") or spark
    sparks_damaged = first_frame(tiles, "sparks_damaged") or spark
    sparks_open = first_frame(tiles, "sparks_open") or spark
    welded = first_frame(tiles, "welded")
    panel = first_frame(tiles, "panel_open")

    if spark is None:
        spark = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))
    if welded is None:
        welded = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))
    if panel is None:
        panel = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))
    if sparks_broken is None:
        sparks_broken = spark
    if sparks_damaged is None:
        sparks_damaged = spark
    if sparks_open is None:
        sparks_open = spark

    panel_anim = [panel.copy() for _ in range(6)]
    emergency = extras_src["unlit_closed"]

    files = {
        "closed_unlit": pack_rsi(dup4(extras_src["unlit_closed"]), 2),
        "open_unlit": pack_rsi(dup4(extras_src["unlit_open"]), 2),
        "bolted_unlit": pack_rsi(dup4(extras_src["unlit_bolted"]), 2),
        "emergency_unlit": pack_rsi(dup4(emergency), 2),
        "opening_unlit": pack_rsi(dup4_anim(extras_src["unlit_opening"]), extras_src["n_open"]),
        "closing_unlit": pack_rsi(dup4_anim(extras_src["unlit_closing"]), extras_src["n_close"]),
        "deny_unlit": pack_rsi(dup4_anim(deny_unlit), len(deny_unlit)),
        "panel_open": pack_rsi(dup4(panel), 2),
        "panel_closed": pack_rsi(dup4(Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))), 2),
        "panel_opening": pack_rsi(dup4_anim(panel_anim), 6),
        "panel_closing": pack_rsi(dup4_anim(list(reversed(panel_anim))), 6),
        "welded": pack_rsi(dup4(welded), 2),
        "sparks": pack_rsi(dup4(spark), 2),
        "sparks_broken": pack_rsi(dup4(sparks_broken), 2),
        "sparks_damaged": pack_rsi(dup4(sparks_damaged), 2),
        "sparks_open": pack_rsi(dup4(sparks_open), 2),
    }
    states = [
        state_still("bolted_unlit"),
        state_still("open_unlit"),
        state_still("closed_unlit"),
        state_anim("closing_unlit", extras_src["n_close"], 0.8),
        state_anim("deny_unlit", len(deny_unlit), 0.3),
        state_anim("opening_unlit", extras_src["n_open"], 0.8),
        state_anim("emergency_unlit", 1, 0.4) if False else state_still("emergency_unlit"),
        state_anim("panel_closing", 6, 0.3),
        state_still("panel_closed"),
        state_still("panel_open"),
        state_anim("panel_opening", 6, 0.3),
        state_still("sparks"),
        state_still("sparks_broken"),
        state_still("sparks_damaged"),
        state_still("sparks_open"),
        state_still("welded"),
    ]
    # emergency_unlit in SS14 is animated 4 frames; we keep still (AirlockSystem only shows it idle)
    return files, states


def main():
    assembly_tiles = load_assembly()

    solid_extras = {}
    for name, dmi in SOLID_BODIES.items():
        files, states, extras = bake_body(name, dmi, ASSEMBLY[name], assembly_tiles)
        save_rsi(OUT / "standard" / f"{name}.rsi", files, states)
        solid_extras[name] = extras
        print(f"solid {name}: mask={len(extras['mask'])} open={extras['n_open']} close={extras['n_close']}")

    glass_extras = {}
    for name, dmi in GLASS_BODIES.items():
        files, states, extras = bake_body(
            name, dmi, ASSEMBLY_GLASS.get(name, "door_as_g0"), assembly_tiles
        )
        save_rsi(OUT / "glass" / f"{name}.rsi", files, states)
        glass_extras[name] = extras
        print(f"glass {name}: mask={len(extras['mask'])} open={extras['n_open']} close={extras['n_close']}")

    # Shared solid effects from engineering (canonical 16-state door).
    files, states = bake_effects(solid_extras["engineering"], "solid")
    save_rsi(OUT / "effects" / "airlock-effects.rsi", files, states)
    files, states = bake_effects(glass_extras["int"], "glass")
    save_rsi(OUT / "effects" / "airlock-effects-glass.rsi", files, states)
    files, states = bake_effects(solid_extras["external"], "external")
    save_rsi(OUT / "effects" / "airlock-effects-external.rsi", files, states)
    print("effects written")


if __name__ == "__main__":
    main()
