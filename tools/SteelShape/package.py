"""Package the reviewed offline HTML and its reproducible source/evidence kit."""
from pathlib import Path
from datetime import datetime, timezone
import argparse
import hashlib
import json
import re
import zipfile

ROOT = Path(__file__).parent
parser = argparse.ArgumentParser()
parser.add_argument('html', nargs='?', type=Path, default=ROOT / 'Steel-weight-calculators.html')
parser.add_argument('--output-dir', type=Path, default=ROOT)
args = parser.parse_args()
args.output_dir.mkdir(parents=True, exist_ok=True)
html = args.html.read_bytes()
page = html.decode('utf-8')
assert not re.search(r'<(?:script|link)\b[^>]*(?:src|href)\s*=', page, re.I), 'External dependency found.'
assert not re.search(r'/\* (?:CATALOGUES|RHS_CORE|ENGINE|SCHEMATICS) \*/', page), 'Unbuilt template.'
for token, filename in [('CATALOGUES', 'catalogues.js'), ('RHS_CORE', 'rhs-core.js'), ('ENGINE', 'engine.js'), ('SCHEMATICS', 'schematics.js')]:
    assert (ROOT / filename).read_text() in page, filename + ' does not match built HTML.'
engine_checks = json.loads((ROOT / 'engine-test-results.json').read_text())
ui_checks = json.loads((ROOT / 'ui-test-results.json').read_text())
weld_checks = json.loads((ROOT / 'weld-test-results.json').read_text())
selection_checks = json.loads((ROOT / 'selection-test-results.json').read_text())
browser_checks = json.loads((ROOT / 'browser-test-results.json').read_text())
assert all(check['status'] == 'passed' for check in [engine_checks, ui_checks, weld_checks, selection_checks, browser_checks])
assert browser_checks['artifactSha256'] == hashlib.sha256(html).hexdigest(), 'Browser evidence is for a different HTML build.'
catalogues = json.loads((ROOT / 'catalogues.json').read_text())
counts = {family: len(table['rows']) for family, table in catalogues.items()}
assert sum(counts.values()) == 332
rhs_hash = hashlib.sha256((ROOT / 'rhs-core.js').read_bytes()).hexdigest()
assert rhs_hash == 'c2ad832473cf4191c8287fadf49e39e39f7f1a95d385af1033b47d2dfb9f9f6a'
report = {
    'createdUtc': datetime.now(timezone.utc).isoformat(),
    'artifact': 'Steel-weight-calculators.html',
    'sha256': hashlib.sha256(html).hexdigest(),
    'calculators': 16,
    'offline': True,
    'engine': engine_checks,
    'ui': ui_checks,
    'weld': weld_checks,
    'selection': selection_checks,
    'browser': browser_checks,
    'catalogueCounts': counts,
    'originalRhsCoreSha256': rhs_hash,
    'visualReview': 'All 16 default schematics and nominal outlines plus selected RHS, IPE, CHS and UPN paths rendered and inspected. Real Chromium desktop/mobile captures inspected; the HTML was loaded in memory because file URLs are blocked by browser policy.',
    'liveWebsiteParity': 'Not verified. Available access read input pages and product tables but could not execute the supplier calculator forms.',
    'sources': {family: table['sourceUrl'] for family, table in catalogues.items()},
}
report_bytes = (json.dumps(report, ensure_ascii=False, indent=2) + '\n').encode('utf-8')
report_path = args.output_dir / 'Steel-weight-calculators-validation.json'
report_path.write_bytes(report_bytes)
files = [
    'README.md', 'SOURCE-AUDIT.md', 'build.py', 'package.py', 'import_catalogues.py',
    'page.template.html', 'engine.js', 'rhs-core.js', 'schematics.js',
    'catalogues.js', 'catalogues.json', 'formula-vectors.json', 'rhs-vectors.json', 'rhs-reference-cases.json',
    'test-engine.cjs', 'test-ui.cjs', 'test-dependencies.cjs', 'render-gallery.cjs',
    'test-weld.cjs', 'generate-weld-vectors.py', 'weld-vectors.json', 'auto-weld-vectors.json', 'render-weld-preview.cjs',
    'test-selection.cjs', 'generate-selection-vectors.py', 'selection-vectors.json', 'render-selection-previews.cjs', 'test-browser.cjs',
    'engine-test-results.json', 'ui-test-results.json', 'weld-test-results.json', 'selection-test-results.json', 'browser-test-results.json',
    'previews/browser-weld-desktop.png', 'previews/browser-weld-mobile.png',
]
files += [str(p.relative_to(ROOT)) for p in sorted((ROOT / 'research').glob('*.txt'))]
archive = args.output_dir / 'Steel-weight-calculators-source.zip'
with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as bundle:
    bundle.writestr('Steel-weight-calculators/Steel-weight-calculators.html', html)
    bundle.writestr('Steel-weight-calculators/validation.json', report_bytes)
    hashes = {'Steel-weight-calculators.html': report['sha256'], 'validation.json': hashlib.sha256(report_bytes).hexdigest()}
    for name in files:
        data = (ROOT / name).read_bytes()
        bundle.writestr('Steel-weight-calculators/' + name, data)
        hashes[name] = hashlib.sha256(data).hexdigest()
    bundle.writestr('Steel-weight-calculators/checksums.json', json.dumps(hashes, indent=2) + '\n')
with zipfile.ZipFile(archive) as bundle:
    assert bundle.testzip() is None
    assert bundle.read('Steel-weight-calculators/Steel-weight-calculators.html') == html
print(json.dumps({'html': str(args.html), 'sourceZip': str(archive), 'validation': str(report_path), 'calculators': 16, 'catalogueEntries': 332, 'checks': sum(check['checks'] for check in [engine_checks,ui_checks,weld_checks,selection_checks,browser_checks]), 'sha256': report['sha256']}, indent=2))
