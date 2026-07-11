#!/usr/bin/env python3
"""Render the rulebook PDF into per-page images plus a searchable text manifest.

Outputs:
  game-assets/images/rulebook/page-NN.jpg  (faithful page visuals, via pdftoppm)
  game-data/rulebook.json                  (per-page text layer, via pdftotext)

Both land in StreamingAssets through the existing tools/sync_unity.sh flow.
Requires poppler (pdfinfo / pdftoppm / pdftotext on PATH).
"""
import json
import pathlib
import re
import subprocess
import xml.etree.ElementTree as ET

root = pathlib.Path(__file__).resolve().parent.parent
pdf = root / "game-assets" / "Lemonade Wars Rulebook.pdf"
out_dir = root / "game-assets" / "images" / "rulebook"

info = subprocess.run(["pdfinfo", str(pdf)], capture_output=True, text=True, check=True).stdout
pages = int(re.search(r"Pages:\s+(\d+)", info).group(1))

if out_dir.exists():
    for old in out_dir.iterdir():
        old.unlink()
out_dir.mkdir(parents=True, exist_ok=True)

subprocess.run(
    ["pdftoppm", "-jpeg", "-jpegopt", "quality=88", "-r", "180", str(pdf), str(out_dir / "page")],
    check=True,
)

# Word bounding boxes (PDF point coordinates, top-left origin) let the viewer
# paint highlight rectangles directly onto the page images.
bbox_xml = subprocess.run(
    ["pdftotext", "-bbox", str(pdf), "-"],
    capture_output=True, text=True, check=True,
).stdout
ns = {"x": "http://www.w3.org/1999/xhtml"}
bbox_pages = ET.fromstring(bbox_xml).findall(".//x:page", ns)

entries = []
for n in range(1, pages + 1):
    text = subprocess.run(
        ["pdftotext", "-layout", "-f", str(n), "-l", str(n), str(pdf), "-"],
        capture_output=True, text=True, check=True,
    ).stdout
    image = next(out_dir.glob(f"page-*{n:02d}.jpg"))
    bbox_page = bbox_pages[n - 1]
    words = [
        [w.text or "",
         round(float(w.get("xMin")), 1), round(float(w.get("yMin")), 1),
         round(float(w.get("xMax")), 1), round(float(w.get("yMax")), 1)]
        for w in bbox_page.findall("x:word", ns)
    ]
    entries.append({
        "page": n,
        "image": f"rulebook/{image.name}",
        "text": text.strip(),
        "w": float(bbox_page.get("width")),
        "h": float(bbox_page.get("height")),
        "words": words,
    })

manifest = root / "game-data" / "rulebook.json"
manifest.write_text(json.dumps({"pages": entries}, indent=1))
sizes = sum(f.stat().st_size for f in out_dir.iterdir())
print(f"rulebook: {pages} pages, {sizes // 1024} KB of images, manifest {manifest.name}")
