"""Verify the printed drawing itself, including each slab and the props between slabs."""
from itertools import groupby
from pathlib import Path
import sys

import pymupdf


def check(path, levels):
    with pymupdf.open(path) as pdf:
        raster = next(page for page in pdf if "Load-transfer section" in page.get_text()).get_pixmap(alpha=False)
    pixels = raster.samples
    scanlines = [pixels[y*raster.stride:(y+1)*raster.stride] for y in range(raster.height)]
    slab = bytes((169, 169, 169))
    shaft = bytes((112, 128, 144))
    bands = [list(rows) for visible, rows in groupby(
        range(raster.height), key=lambda y: scanlines[y].count(slab) > 180
    ) if visible]
    assert len(bands) == levels, f"{path.name}: expected {levels} visible slabs, found {len(bands)}"
    for upper, lower in zip(bands, bands[1:]):
        shaft_rows = sum(scanlines[y].count(shaft) >= 6 for y in range(upper[-1]+1, lower[0]))
        assert shaft_rows >= 5, f"{path.name}: missing props between slab bands {upper[0]} and {lower[0]}"
    print(f"{path.name}: {levels} visible slab bands, {levels-1} supported storeys")


folder = Path(sys.argv[1])
check(folder / "section.pdf", 4)
check(folder / "section.raft.pdf", 3)

with pymupdf.open(folder / "section.d3.pdf") as report:
    pages = [page.get_text() for page in report]
    assert len(pages) == 7, [t[:80] for t in pages]
    for label in ("Test project", "Test client", "Test contractor", "Tower-supported slab pour", "Prepared by A", "Checked by B"):
        assert label in pages[0], label
    for i, label in enumerate(("Load-transfer section","Typical tower layout","Slab capacity","D3 component capacity","Eurex eco props","D3 shoring towers"),1):
        assert label in pages[i], label
    assert "18.50 kN" in pages[2] and "35.0 kN" in pages[2] and "SAFE" in pages[2]
    assert report[0].get_images() and report[5].get_images() and report[6].get_images()
    assert report[6].rect.width > report[6].rect.height
