#!/usr/bin/env python3
"""Pre-generate sharp mip pyramids for the card art.

Unity auto-generates mipmaps with a plain box filter, which turns minified card
text to mush; a negative mip bias just trades the mush for aliasing. This packs
proper Lanczos downscales instead: for every image, each mip level (Unity's
dims: width>>L x height>>L, stopping below 24px) is resized from the ORIGINAL
with Lanczos and stacked top-to-bottom into a sibling `<name>.mips.<ext>` file.
At load, UiKit.LoadTextureSharp splits the stack back into the texture's mip
levels (see the matching layout math there).

Run after adding/changing art, then tools/sync_unity.sh.
"""

import pathlib

from PIL import Image

MIN_DIM = 24

root = pathlib.Path(__file__).resolve().parent.parent / "game-assets/images"

packed = 0
for path in sorted(root.rglob("*")):
    suffix = path.suffix.lower()
    if suffix not in (".png", ".jpg", ".jpeg"):
        continue
    if ".mips" in path.name:
        continue
    if path.name.startswith("app-bg"):
        continue  # the wallpaper tile draws ~1:1; mips would be dead weight

    image = Image.open(path)
    w, h = image.size
    levels = []
    level = 1
    while (w >> level) >= MIN_DIM and (h >> level) >= MIN_DIM:
        levels.append(image.resize((w >> level, h >> level), Image.LANCZOS))
        level += 1
    if not levels:
        continue

    mode = "RGB" if suffix in (".jpg", ".jpeg") else "RGBA"
    fill = (0, 0, 0) if mode == "RGB" else (0, 0, 0, 0)
    canvas = Image.new(mode, (levels[0].width, sum(l.height for l in levels)), fill)
    y = 0
    for l in levels:
        canvas.paste(l.convert(mode), (0, y))
        y += l.height

    out = path.with_name(path.stem + ".mips" + suffix)
    if mode == "RGB":
        canvas.save(out, quality=90)
    else:
        canvas.save(out)
    packed += 1

print(f"packed sharp mips for {packed} images under {root}")
