#!/usr/bin/env python3
"""Turn the app background art into a tintable tile.

The source (game-assets/images/app-bg.png) is a flat grey pattern on a
transparent field. A UI tint multiplies the texture's RGB, so grey can only ever
darken toward the tint -- it can never reach full lemonade yellow. Whitening the
RGB while keeping the alpha makes the tint exact: white * yellow == yellow, and
the client can then pick any color (and any opacity) from code.

Also downscales, because the source is 5392x4254 (~92 MB as RGBA32 in memory)
and the pattern is only ever shown small.

Run after any change to app-bg.png, then tools/sync_unity.sh.
"""

import pathlib
import sys

from PIL import Image

SCALE = 4  # source badges are ~540px; /4 lands them near their on-screen size

root = pathlib.Path(__file__).resolve().parent.parent
source = root / "game-assets/images/app-bg.png"
target = root / "game-assets/images/app-bg-tile.png"

if not source.exists():
    sys.exit(f"missing {source}")

image = Image.open(source).convert("RGBA")
alpha = image.getchannel("A")

# Pure white everywhere; only the alpha carries the artwork's shape.
white = Image.new("RGBA", image.size, (255, 255, 255, 0))
white.putalpha(alpha)

size = (image.width // SCALE, image.height // SCALE)
white = white.resize(size, Image.LANCZOS)
white.save(target)

print(f"{source.name} {image.size} -> {target.name} {size}")
