#!/usr/bin/env python3
"""Rename exported card images to def-id names and write game-data/images.json.

Usage: python3 tools/rename_images.py [--dry-run]

The print-export numbering maps 1:1 to CSV row order (confirmed by the designer:
row 1 = <prefix>1). Supporting-card images follow the Supporting Cards PDF page
order. Renames are idempotent: already-renamed files are left alone.

Resulting names:
  lemon:       <def-id>.png (copies: <def-id>-2.png, ...)
  black-market:<def-id>-<shape>.png            (unique: copies differ by shape)
  titles:      <def-id>.png
  supporting:  bargain-stand-01..21, classic-stand-01..18, gourmet-stand-01..15,
               turf-1..6, player-aid-1..5, bragging-rights-01..11,
               whiniest-baby, timeout-1..2, spoiled-rotten
"""

import csv
import json
import re
import sys
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CSV_DIR = ROOT / "game-assets" / "csv"
IMG_DIR = ROOT / "game-assets" / "images"
OUT_DIR = ROOT / "game-data"

DRY_RUN = "--dry-run" in sys.argv


def clean(text):
    return re.sub(r"\s+", " ", text.replace("<b>", " ")).strip()


def slug(name):
    s = name.lower()
    s = re.sub(r"[''!,.]", "", s)
    s = re.sub(r"[^a-z0-9]+", "-", s)
    return s.strip("-")


def export_name(prefix, record):
    """The print exporter's base names ended in '1' and appended 2..N for later
    records: record 1 -> 'Lemon-1.png', record 2 -> 'Lemon-12.png', 69 -> 'Lemon-169.png'."""
    return f"{prefix}.png" if record == 1 else f"{prefix}{record}.png"


def rename(folder, old_name, new_name):
    src = IMG_DIR / folder / old_name
    dst = IMG_DIR / folder / new_name
    if dst.exists() and not src.exists():
        return  # already renamed
    if not src.exists():
        raise SystemExit(f"missing image: {src}")
    if dst.exists():
        raise SystemExit(f"collision: {dst} already exists")
    if not DRY_RUN:
        src.rename(dst)
    print(f"{folder}/{old_name} -> {new_name}")


manifest = {
    "note": "All paths relative to game-assets/images/. Regenerate with tools/rename_images.py.",
    "lemon": {},
    "blackMarket": defaultdict(dict),
    "titles": {},
    "stands": defaultdict(list),
    "turf": {},
    "supporting": {"braggingRights": [], "playerAids": [], "timeout": []},
    "backs": {
        "lemon": "backs/Lemon-Back.jpg",
        "blackMarket": "backs/BlackMarket-Back.jpg",
        "firstDibs": "backs/FirstDibs-Back.jpg",
        "lemonLord": "backs/LemonLord-Back.jpg",
        "braggingRights": "backs/BraggingRights-Back.jpg",
    },
    "money": {
        "1": "money/1-Dollar.jpg",
        "2": "money/2-Dollars.jpg",
        "5": "money/5-Dollars.jpg",
        "10": "money/10-Dollars.jpg",
    },
}

# ------------------------------------------------------------------- lemon
rows = list(csv.DictReader(open(CSV_DIR / "Lemon Deck.csv")))
copy_counter = defaultdict(int)
for i, row in enumerate(rows, start=1):
    def_id = slug(row["Title"])
    copy_counter[def_id] += 1
    n = copy_counter[def_id]
    new_name = f"{def_id}.png" if n == 1 else f"{def_id}-{n}.png"
    rename("lemon", export_name("Lemon-1", i), new_name)
    if n == 1:
        manifest["lemon"][def_id] = f"lemon/{new_name}"

# ------------------------------------------------------------ black market
rows = list(csv.DictReader(open(CSV_DIR / "Black Market Deck.csv")))
seen = set()
for i, row in enumerate(rows, start=1):
    effect = clean(row["Main"])
    m = re.match(r"Add (\d) to your", effect)
    def_id = slug(row["Title"]) + (f"-{m.group(1)}" if m else "")
    shape = row["Shape"].lower()
    new_name = f"{def_id}-{shape}.png"
    if new_name in seen:  # same def+shape twice: keep both, suffixed
        new_name = f"{def_id}-{shape}-2.png"
    seen.add(new_name)
    rename("black-market", export_name("BlackMarket-1", i), new_name)
    manifest["blackMarket"][def_id][shape] = f"black-market/{new_name}"

# ------------------------------------------------------------------ titles
rows = list(csv.DictReader(open(CSV_DIR / "Title Deck.csv")))
for i, row in enumerate(rows, start=1):
    def_id = slug(row["Title"])
    rename("titles", export_name("Title-Card-1", i), f"{def_id}.png")
    manifest["titles"][def_id] = f"titles/{def_id}.png"

# -------------------------------------------------------- supporting cards
# Page ranges follow the Supporting Cards PDF (see rulebook component list).
SUPPORTING_RANGES = [
    ("bargain-stand", 1, 21),
    ("classic-stand", 22, 39),
    ("gourmet-stand", 40, 54),
    ("turf", 55, 60),          # power pour numbers 1..6 in page order
    ("player-aid", 61, 65),
    ("bragging-rights", 66, 76),
    ("whiniest-baby", 77, 77),
    ("timeout", 78, 79),
    ("spoiled-rotten", 80, 80),
]


def supporting_source(page):
    return "Supporting-Card.png" if page == 1 else f"Supporting-Card{page}.png"


for base, start, end in SUPPORTING_RANGES:
    for page in range(start, end + 1):
        idx = page - start + 1
        count = end - start + 1
        if count == 1:
            new_name = f"{base}.png"
        elif base == "turf":
            new_name = f"turf-{idx}.png"  # idx == power pour number
        else:
            new_name = f"{base}-{idx:02d}.png"
        rename("supporting-cards", supporting_source(page), new_name)

        path = f"supporting-cards/{new_name}"
        if base.endswith("-stand"):
            manifest["stands"][base.replace("-stand", "")].append(path)
        elif base == "turf":
            manifest["turf"][str(idx)] = path
        elif base == "bragging-rights":
            manifest["supporting"]["braggingRights"].append(path)
        elif base == "player-aid":
            manifest["supporting"]["playerAids"].append(path)
        elif base == "timeout":
            manifest["supporting"]["timeout"].append(path)
        else:
            manifest["supporting"][
                "whiniestBaby" if base == "whiniest-baby" else "spoiledRotten"] = path

# ---------------------------------------------------------------- validate
missing = []
def walk(node):
    if isinstance(node, str) and "/" in node and node != manifest["note"]:
        if not (IMG_DIR / node).exists() and not DRY_RUN:
            missing.append(node)
    elif isinstance(node, dict):
        for v in node.values():
            walk(v)
    elif isinstance(node, list):
        for v in node:
            walk(v)

manifest["blackMarket"] = dict(manifest["blackMarket"])
manifest["stands"] = dict(manifest["stands"])
walk(manifest)
if missing:
    raise SystemExit(f"manifest references missing files: {missing[:5]}")

if not DRY_RUN:
    out = OUT_DIR / "images.json"
    out.write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n")
    print(f"\nwrote {out.relative_to(ROOT)}")
print(f"lemon defs: {len(manifest['lemon'])}, bm defs: {len(manifest['blackMarket'])}, "
      f"titles: {len(manifest['titles'])}")
