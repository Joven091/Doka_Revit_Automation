"""Verify the printed drawing itself, including each slab and the props between slabs."""
from itertools import groupby
from pathlib import Path
import sys

import pymupdf


def check(path, levels):
    with pymupdf.open(path) as pdf:
        raster = pdf[0].get_pixmap(alpha=False)
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
