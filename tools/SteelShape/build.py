from pathlib import Path
import argparse
import re

ROOT = Path(__file__).parent
parser = argparse.ArgumentParser(description='Build the self-contained offline calculator suite.')
parser.add_argument('output', nargs='?', type=Path, default=ROOT / 'Steel-weight-calculators.html')
target = parser.parse_args().output
target.parent.mkdir(parents=True, exist_ok=True)
page = (ROOT / 'page.template.html').read_text()
for name, file in [('CATALOGUES', 'catalogues.js'), ('RHS_CORE', 'rhs-core.js'), ('ENGINE', 'engine.js'), ('SCHEMATICS', 'schematics.js')]:
    page = page.replace('/* ' + name + ' */', (ROOT / file).read_text())
target.write_text(page)
for i, script in enumerate(re.findall(r'<script>(.*?)</script>', page, re.S)):
    (ROOT / ('generated-' + str(i) + '.js')).write_text(script)
print(str(target))
