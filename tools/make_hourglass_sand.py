#!/usr/bin/env python3
"""Split the hourglass icon into an empty glass and the sand that fills it.

Reads game-assets/icons/Hourglass.png (the glass with its sand drawn in) and writes,
all on one 512x512 canvas so they overlay exactly:

  HourglassEmpty.png        the glass alone, in the art's own colour
  HourglassSandTop.png      the upper bulb packed full, as a white mask to tint
  HourglassSandBottom.png   the lower bulb packed full, as a white mask to tint

The client reveals the masks from the bottom up to drain one bulb into the other
(TableView.ActionCountdowns.cs). It needs to know where each bulb's sand starts and
stops on the canvas: this script prints those four fractions, which are copied into
that file as constants. Re-run and re-copy them if the hourglass art changes.

The art is symmetric top to bottom, which is what makes the bottom bulb recoverable:
its sand pile is fused to the base, so the pile is carved out using the upper bulb's
hollow, mirrored.

    python3 tools/make_hourglass_sand.py      # needs Pillow and numpy
"""
import pathlib

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ICONS = pathlib.Path(__file__).resolve().parent.parent / "game-assets" / "icons"
OUT_SIZE = 512
# In source pixels (the art is 1181 square).
SAND_TO_GLASS_GAP = 28   # the air the art leaves between its sand and the glass
CARVE_MARGIN = 6         # the mirrored hollow stops this short of the lower glass, which is not a perfect mirror
SAND_UNDER_BASE = 12     # the lower sand runs this far under the base, so no seam shows where it rests


def flood(mask, seed):
    """The connected region of True cells containing seed (x, y)."""
    # A copy: an image made straight from an array is read-only, and the fill silently does nothing to it.
    canvas = Image.fromarray(np.where(mask, 255, 0).astype(np.uint8)).copy()
    ImageDraw.floodfill(canvas, seed, 128)
    return np.array(canvas) == 128


def erode(mask, pixels):
    for _ in range(pixels):
        inner = mask.copy()
        inner[1:, :] &= mask[:-1, :]
        inner[:-1, :] &= mask[1:, :]
        inner[:, 1:] &= mask[:, :-1]
        inner[:, :-1] &= mask[:, 1:]
        mask = inner
    return mask


def soften(mask):
    """A boolean mask as 0..1 coverage with an anti-aliased edge."""
    image = Image.fromarray(np.where(mask, 255, 0).astype(np.uint8))
    return np.array(image.filter(ImageFilter.GaussianBlur(1.2))).astype(np.float32) / 255.0


def save(name, rgb, coverage):
    rgba = np.dstack([rgb, np.clip(coverage * 255.0, 0, 255)]).astype(np.uint8)
    Image.fromarray(rgba).resize((OUT_SIZE, OUT_SIZE), Image.LANCZOS).save(ICONS / name)


def main():
    art = np.array(Image.open(ICONS / "Hourglass.png").convert("RGBA")).astype(np.float32)
    rgb, coverage = art[..., :3], art[..., 3] / 255.0
    ink = coverage > 0.5
    height = ink.shape[0]

    cap_row = int(np.where(ink.any(axis=1))[0][0]) + 10
    cap_column = int(np.where(ink[cap_row])[0][0]) + 10
    glass = flood(ink, (cap_column, cap_row))            # the frame, with the lower pile fused to it
    outside = flood(~glass, (0, 0))
    rows = np.where(glass.any(axis=1))[0]
    top_row, bottom_row = int(rows[0]), int(rows[-1])
    waist_row = (top_row + bottom_row) // 2

    # Everything inside the upper bulb: its air, its loose sand, and the soft edges between.
    upper_hollow = ~glass & ~outside
    upper_hollow[waist_row:, :] = False
    lower_hollow = np.zeros_like(upper_hollow)
    lower_hollow[top_row:bottom_row + 1] = upper_hollow[top_row:bottom_row + 1][::-1]

    emptied = erode(upper_hollow, 4) | erode(lower_hollow, CARVE_MARGIN)
    empty_coverage = coverage * (1.0 - soften(emptied))
    save("HourglassEmpty.png", rgb, empty_coverage)

    empty_glass = empty_coverage > 0.5
    hollow = ~empty_glass & ~flood(~empty_glass, (0, 0))
    sand = erode(hollow, SAND_TO_GLASS_GAP)
    upper_sand = sand.copy()
    upper_sand[waist_row:, :] = False
    lower_sand = sand.copy()
    lower_sand[:waist_row, :] = False
    settle = SAND_TO_GLASS_GAP + SAND_UNDER_BASE
    lower_sand[settle:, :] |= lower_sand[:-settle, :]

    white = np.full_like(rgb, 255.0)
    save("HourglassSandTop.png", white, soften(upper_sand))
    save("HourglassSandBottom.png", white, soften(lower_sand))

    def span(mask):
        filled = np.where(mask.any(axis=1))[0]
        return 1.0 - (filled[-1] + 1) / height, 1.0 - filled[0] / height

    print("canvas fractions from the bottom, for TableView.ActionCountdowns.cs:")
    print("  upper sand  floor %.3f  brim %.3f" % span(upper_sand))
    print("  lower sand  floor %.3f  brim %.3f" % span(lower_sand))


if __name__ == "__main__":
    main()
