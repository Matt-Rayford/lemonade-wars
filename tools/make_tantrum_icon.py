#!/usr/bin/env python3
"""Cut a circular icon out of the Tantrum card's artwork.

Reads game-assets/images/lemon/tantrum.png (750x1050 card), crops a circle from
the middle of the art region, and writes a 300x300 RGBA icon with a feathered
edge to game-assets/icons/Tantrum.png. Pure stdlib — no Pillow needed.
"""
import pathlib
import struct
import zlib

root = pathlib.Path(__file__).resolve().parent.parent
src = root / "game-assets" / "images" / "lemon" / "tantrum.png"
dst = root / "game-assets" / "icons" / "Tantrum.png"

# Circle center/radius in card pixels: horizontally centered, in the artwork
# band below the title bar.
CENTER_X, CENTER_Y, RADIUS = 375, 405, 195
OUT = 300  # output icon size


def read_png(path):
    data = path.read_bytes()
    pos, idat, meta = 8, b"", {}
    while pos < len(data):
        length, ctype = struct.unpack(">I4s", data[pos:pos + 8])
        chunk = data[pos + 8:pos + 8 + length]
        if ctype == b"IHDR":
            meta["w"], meta["h"], meta["depth"], meta["color"] = struct.unpack(">IIBB", chunk[:10])
        elif ctype == b"IDAT":
            idat += chunk
        pos += 12 + length
    if meta["depth"] != 8 or meta["color"] not in (2, 6):
        raise SystemExit(f"unsupported PNG format: depth={meta['depth']} color={meta['color']}")
    bpp = 4 if meta["color"] == 6 else 3
    raw = zlib.decompress(idat)
    stride = meta["w"] * bpp
    rows = []
    prior = bytearray(stride)
    pos = 0
    for _ in range(meta["h"]):
        filt = raw[pos]
        line = bytearray(raw[pos + 1:pos + 1 + stride])
        pos += 1 + stride
        if filt == 1:
            for i in range(bpp, stride):
                line[i] = (line[i] + line[i - bpp]) & 0xFF
        elif filt == 2:
            for i in range(stride):
                line[i] = (line[i] + prior[i]) & 0xFF
        elif filt == 3:
            for i in range(stride):
                left = line[i - bpp] if i >= bpp else 0
                line[i] = (line[i] + ((left + prior[i]) >> 1)) & 0xFF
        elif filt == 4:
            for i in range(stride):
                a = line[i - bpp] if i >= bpp else 0
                b = prior[i]
                c = prior[i - bpp] if i >= bpp else 0
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                pred = a if pa <= pb and pa <= pc else (b if pb <= pc else c)
                line[i] = (line[i] + pred) & 0xFF
        rows.append(bytes(line))
        prior = line
    return meta["w"], meta["h"], bpp, rows


def write_png(path, size, pixels):
    def chunk(ctype, payload):
        body = ctype + payload
        return struct.pack(">I", len(payload)) + body + struct.pack(">I", zlib.crc32(body))

    raw = b"".join(b"\x00" + bytes(pixels[y]) for y in range(size))
    png = (b"\x89PNG\r\n\x1a\n" +
           chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)) +
           chunk(b"IDAT", zlib.compress(raw, 9)) +
           chunk(b"IEND", b""))
    path.write_bytes(png)


width, height, bpp, rows = read_png(src)
scale = (RADIUS * 2) / OUT
half = OUT / 2
out_rows = []
for oy in range(OUT):
    line = bytearray(OUT * 4)
    for ox in range(OUT):
        sx = int(CENTER_X - RADIUS + (ox + 0.5) * scale)
        sy = int(CENTER_Y - RADIUS + (oy + 0.5) * scale)
        sx = min(max(sx, 0), width - 1)
        sy = min(max(sy, 0), height - 1)
        pixel = rows[sy][sx * bpp:sx * bpp + 3]
        distance = ((ox + 0.5 - half) ** 2 + (oy + 0.5 - half) ** 2) ** 0.5
        # 2px feathered rim for a clean anti-aliased edge.
        alpha = min(max((half - distance) / 2.0, 0.0), 1.0)
        line[ox * 4:ox * 4 + 4] = bytes(pixel) + bytes([int(alpha * 255)])
    out_rows.append(line)

write_png(dst, OUT, out_rows)
print(f"wrote {dst} ({OUT}x{OUT}, circle r={RADIUS} at ({CENTER_X},{CENTER_Y}) of {src.name})")
