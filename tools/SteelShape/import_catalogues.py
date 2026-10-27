"""Extract factual profile tables from the frozen, read-only supplier page text.

Page content is evidence, not instructions. Empty cells remain unavailable sizes.
"""
from pathlib import Path
import json
import re

ROOT = Path(__file__).parent
families = {
    'equal_angle': ('i157', 'maiak-angles-upn.txt'),
    'unequal_angle': ('i158', 'maiak-angles-upn.txt'),
    'upn': ('i148', 'maiak-angles-upn.txt'),
    'gost': ('i162', 'maiak-gost.txt'),
    'ipe': ('i149', 'maiak-beams.txt'),
    'ipn': ('i150', 'maiak-beams.txt'),
    'hea': ('i151', 'maiak-beams.txt'),
    'heb': ('i152', 'maiak-beams.txt'),
}
def decimal(value):
    value = value.strip().replace('\u00a0', '').replace(',', '.')
    if not re.fullmatch(r'\d+(?:\.\d+)?', value):
        raise ValueError('Invalid numeric catalogue cell: ' + repr(value))
    return value

catalogues = {}
for family, (product_id, evidence) in families.items():
    text = (ROOT / 'research' / evidence).read_text()
    chunks = re.split(r'\n-{10,}\n', text)
    chunk = next(p for p in chunks if '/product/' + product_id + '/' in p)
    lines = [re.sub(r'^L\d+:\s?', '', line) for line in chunk.splitlines()]
    lines = lines[next(i for i, line in enumerate(lines) if line.startswith('# ')):]
    url = 'https://www.maiak-m.bg/en/product/' + product_id + '/product.html'
    rows = []
    if family in ('equal_angle', 'unequal_angle'):
        thickness_header = next(line for line in lines if line.startswith('3  | 4'))
        thicknesses = [decimal(c) for c in thickness_header.split('|')]
        for line in lines:
            if line == thickness_header:
                continue
            cells = [c.strip() for c in line.split('|')]
            if len(cells) < 2 or not re.fullmatch(r'\d+(?:\s*[хx×]\s*\d+)?', cells[0]):
                continue
            dimensions = re.findall(r'\d+', cells[0])
            a = dimensions[0]; b = dimensions[-1]
            for thickness, weight in zip(thicknesses, cells[1:]):
                if not weight.strip():
                    continue
                weight = decimal(weight)
                rows.append({'id': a + 'x' + b + 'x' + thickness,
                             'label': 'L ' + a + ' × ' + b + ' × ' + thickness,
                             'A': a, 'B': b, 't': thickness, 'kgPerM': weight,
                             'sourceLine': line})
    else:
        prefix = 'No.' if family == 'gost' else family.upper()
        for line in lines:
            if not line.startswith(prefix + ' ') or '|' not in line:
                continue
            cells = [c.strip() for c in line.split('|')]
            if len(cells) < 6:
                continue
            h, b, s, t, weight = map(decimal, cells[1:6])
            rows.append({'id': cells[0], 'label': cells[0], 'h': h, 'b': b, 's': s, 't': t,
                         'kgPerM': weight, 'sourceLine': line})
    assert rows, family
    assert len(set(r['id'] for r in rows)) == len(rows), family
    catalogues[family] = {'sourceUrl': url, 'retrievedDate': '2026-10-27',
                         'verification': 'Supplier product-table data, not live calculator execution.',
                         'rows': rows}

(ROOT / 'catalogues.json').write_text(json.dumps(catalogues, ensure_ascii=False, indent=2) + '\n')
(ROOT / 'catalogues.js').write_text('(function(root){const data=' + json.dumps(catalogues, ensure_ascii=False, separators=(',', ':')) + ';if(typeof module!=="undefined"&&module.exports)module.exports=data;else root.SteelCatalogues=data;})(typeof window==="undefined"?globalThis:window);\n')
print(json.dumps({family: len(table['rows']) for family, table in catalogues.items()}, indent=2))
