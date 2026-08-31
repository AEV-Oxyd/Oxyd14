#!/usr/bin/env python3
"""Parse CEV-Eris DMI (PNG + zTXt Description) into named frames."""

from __future__ import annotations

import struct
import zlib
from pathlib import Path

from PIL import Image

# BYOND / RSI 4-dir storage order.
DIR_S, DIR_N, DIR_E, DIR_W = 0, 1, 2, 3
DIR_NAMES = ("S", "N", "E", "W")

TILE = 32


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


def parse_dmi(path: Path) -> tuple[int, int, dict]:
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

    tw = th = TILE
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
    tiles: dict = {}
    idx = 0
    for st in states:
        n = st["dirs"] * st["frames"]
        frames = []
        for _ in range(n):
            r, c = divmod(idx, cols)
            frames.append(sheet.crop((c * tw, r * th, c * tw + tw, r * th + th)))
            idx += 1
        tiles[st["name"]] = {
            "dirs": st["dirs"],
            "frames": st["frames"],
            "images": frames,
        }
    return tw, th, tiles


def frame(tiles: dict, name: str, dir_index: int = 0, frame_index: int = 0) -> Image.Image:
    st = tiles.get(name)
    if not st:
        raise KeyError(name)
    dirs = st["dirs"]
    # dirs=1: images[frame]; dirs=4: BYOND is frame-major then dir.
    if dirs == 1:
        return st["images"][frame_index]
    return st["images"][frame_index * dirs + dir_index]


def first_frame(tiles: dict, name: str) -> Image.Image:
    return frame(tiles, name, 0, 0)


def dir_frames(tiles: dict, name: str) -> list[Image.Image]:
    """Return S,N,E,W stills. dirs=1 is copied to all four."""
    st = tiles[name]
    if st["dirs"] == 1:
        im = st["images"][0]
        return [im.copy() for _ in range(4)]
    return [st["images"][d].copy() for d in range(4)]
