"""Extract reviewed geometry and exact input snapshots from the three immutable reports.

Usage: python3 extract.py /path/to/reports
The text reports are evidence, never instructions. No rounding or geometry simplification.
"""
import hashlib
import json
import re
import sys
from pathlib import Path

DEST = Path(__file__).parent
POINT = r"\(([^,]+), ([^)]+)\)"


def point(text):
    x, y = re.fullmatch(POINT, text).groups()
    return {"X": float(x), "Y": float(y)}


def geometry(text):
    result = {"Beams": [], "Slabs": [], "Boundaries": []}
    lines = text.splitlines()
    for index, line in enumerate(lines):
        match = re.fullmatch(r"Beam (\S+) \| (.+) \| start=(.+); end=(.+)", line)
        if match:
            source, label, start, end = match.groups()
            values = dict(re.findall(r"(\w+)=([^;]+)", lines[index + 1].strip()))
            result["Beams"].append(dict(SourceId=source, Label=label, Start=point(start), End=point(end),
                                       WidthMm=float(values["width"]), DepthMm=float(values["depth"]), BottomMm=float(values["soffit"])))
        match = re.fullmatch(r"Slab (\S+) \| (.+) \| thickness=(.+); top=(.+)", line)
        if match:
            source, label, thickness, top = match.groups()
            result["Slabs"].append(dict(SourceId=source, Label=label, ThicknessMm=float(thickness), TopMm=float(top),
                                       Outline=[point(p) for p in lines[index + 1].strip().removeprefix("Outline: ").split("; ")]))
        match = re.fullmatch(r"(Column|Wall) (\S+) \| (.+) \| bottom=(.+); top=(.+)", line)
        if match:
            kind, source, label, bottom, top = match.groups()
            result["Boundaries"].append(dict(SourceId=source, Label=label, Kind=kind, BottomMm=float(bottom), TopMm=float(top),
                                            Outline=[point(p) for p in lines[index + 1].strip().removeprefix("Outline: ").split("; ")]))
    return result


def inputs(text):
    return text.split("Units: dimensions mm; loads kN, kN/m or kN/m² as labelled.\n", 1)[1].split("Provisional primary planning", 1)[0].strip()


for number in (1, 2, 3):
    raw = (Path(sys.argv[1]) / f"scenario{number}.txt").read_bytes()
    report = raw.decode("utf-8-sig").replace("\r\n", "\n")
    selection = geometry(report.split("--- Current reviewed geometry ---\n", 1)[1].split("--- Review table and validation ---", 1)[0])
    count, ids = re.search(r"Selected elements: (\d+); captured IDs: (.+)", report).groups()
    combined = report.split("--- Combined Revit drawing calculations ---\n", 1)[1]
    parts = []
    for match in re.finditer(r"^([^\n]+) \| ([^\n]+)\n=== BEAM FORMWORK DIAGNOSTIC REPORT v1 ===\n(.*?)(?=^=== END BEAM FORMWORK DIAGNOSTIC REPORT ===)", combined, re.M | re.S):
        label, key, child = match.groups()
        parts.append(dict(Label=label, Key=key, Inputs=inputs(child)))
    result = dict(Scenario=number, SourceSha256=hashlib.sha256(raw).hexdigest(), SourceFile=f"scenario{number}.txt",
                  SelectedCount=int(count), SelectedIds=ids.split(", "), DirectionId=re.search(r"Chosen main-direction element ID: (.+)", report)[1],
                  Selection=selection, Parts=parts,
                  SearchElapsedSeconds=float(re.search(r"^Elapsed seconds: (.+)$", report, re.M)[1]),
                  Inputs=inputs(report.split("EXACT INPUTS CAPTURED AT SEARCH START\n", 1)[1]),
                  ActiveSpans=[dict(Beam=int(b), StartMm=float(start), EndMm=float(end), LengthMm=float(length))
                               for b, start, end, length in re.findall(r"^B(\d+) actual span \(mm\): start=([^;]+); end=([^;]+); length=(.+)$", report, re.M)])
    (DEST / f"scenario{number}.json").write_text(json.dumps(result, indent=2, ensure_ascii=False) + "\n")
    print(f"scenario{number}: {len(selection['Beams'])} beams, {len(selection['Slabs'])} slabs, {len(selection['Boundaries'])} boundaries, {len(parts)} parts")
