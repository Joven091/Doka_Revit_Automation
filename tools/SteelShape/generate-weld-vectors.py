"""Independent decimal volume checks for the optional two-component weld."""
from pathlib import Path
from decimal import Decimal as D, getcontext
import json

getcontext().prec = 100
ROOT = Path(__file__).parent
rows = []
def clean(x):
    text = format(x, 'f')
    return text.rstrip('0').rstrip('.') if '.' in text else text
def add(name, shape, dimensions, weld):
    values = {k: str(v) for k, v in dimensions.items()}
    w = {k: str(v) for k, v in weld.items()}
    component_sum = D(w['L1']) + D(w['L2'])
    total_mm = component_sum * (1000 if w.get('lengthUnit', 'mm') == 'm' else 1)
    total_length = total_mm if shape == 'plate' else total_mm / 1000
    count = D(values.get('n', '1'))
    # Independent solid-volume arithmetic in metres, rather than the engine's mm² conversion.
    if shape == 'plate':
        steel = (D(values['W']) / 1000) * (D(values['t']) / 1000) * (total_length / 1000) * D(7850) * count
    else:
        steel = (D(values['W']) / 1000) * (D(values['t']) / 1000) * total_length * D(7850)
    if w['type'] == 'fillet':
        area_m2 = (D(w['z']) / 1000) ** 2 / 2
    else:
        # Gap volume and triangular face caps; no full-section duplication.
        area_m2 = (D(w['gapMm']) / 1000) * (D(w['joinedThicknessMm']) / 1000)
        area_m2 += D(w['faces']) * (D(w['capWidthMm']) / 1000) * (D(w['capHeightMm']) / 1000) / 2
    added = area_m2 * (D(w['lengthMm']) / 1000) * D(w['runs']) * D(7850) * count
    finished = total_length + (D(w.get('gapMm', '0')) if shape == 'plate' else D(w.get('gapMm', '0')) / 1000)
    rows.append({'id': name, 'shape': shape, 'input': {**values, 'L': '999', 'weld': {**w, 'enabled': True}}, 'steelTotalKg': clean(steel), 'weldTotalKg': clean(added), 'totalKg': clean(steel + added), 'steelLength': clean(total_length), 'finishedLength': None if w.get('connection') == 'L' else clean(finished)})

flat = {'W': 100, 't': 10}
fillet = {'type': 'fillet', 'lengthMode': 'manual', 'lengthUnit': 'm', 'L1': 2.5, 'L2': 3.5, 'z': 4, 'lengthMm': 300, 'runs': 1}
butt = {'type': 'butt', 'lengthMode': 'manual', 'lengthUnit': 'm', 'L1': 2.5, 'L2': 3.5, 'joinedThicknessMm': 10, 'gapMm': 2, 'capWidthMm': 12, 'capHeightMm': 1, 'faces': 2, 'lengthMm': 100, 'runs': 1}
add('fillet-small', 'flat_bar', flat, fillet)
add('fillet-fractional', 'flat_bar', flat, {**fillet, 'L1': '1.2', 'L2': '4.05', 'z': '3.5', 'lengthMm': '750.25', 'runs': 2})
add('fillet-two-seams', 'flat_bar', flat, {**fillet, 'runs': 2})
add('fillet-very-small', 'flat_bar', flat, {**fillet, 'z': '.1', 'lengthMm': 1})
add('fillet-plate-three-assemblies', 'plate', {'W': 1000, 't': 10, 'n': 3}, {**fillet, 'lengthUnit': 'mm', 'L1': 1250, 'L2': 750, 'z': 6, 'lengthMm': 1000, 'runs': 2})
add('butt-gap-two-caps', 'flat_bar', flat, butt)
add('butt-flush-no-gap', 'flat_bar', flat, {**butt, 'gapMm': 0, 'capHeightMm': 0})
add('butt-gap-only', 'flat_bar', flat, {**butt, 'capHeightMm': 0})
add('butt-one-cap-only', 'flat_bar', flat, {**butt, 'gapMm': 0, 'capHeightMm': 2, 'faces': 1})
add('butt-two-identical-segments', 'flat_bar', flat, {**butt, 'lengthMm': 50, 'runs': 2})
add('butt-two-plate-assemblies', 'plate', {'W': 1000, 't': 10, 'n': 2}, {**butt, 'lengthUnit': 'mm', 'L1': 1000, 'L2': 1000, 'capWidthMm': 16, 'capHeightMm': 2, 'lengthMm': 1000})
add('butt-fractional', 'flat_bar', {'W': '125.5', 't': '3.2'}, {**butt, 'L1': '.125', 'L2': '.375', 'joinedThicknessMm': '3.2', 'gapMm': '.5', 'capWidthMm': '6.4', 'capHeightMm': '.75', 'lengthMm': '125.5', 'faces': 1})
add('fillet-millimetres', 'flat_bar', flat, {**fillet, 'lengthUnit': 'mm', 'L1': 2500, 'L2': 3500})
add('butt-millimetres', 'flat_bar', flat, {**butt, 'lengthUnit': 'mm', 'L1': 2500, 'L2': 3500})
add('L-fillet-millimetres', 'flat_bar', flat, {**fillet, 'connection': 'L', 'lengthUnit': 'mm', 'L1': 2500, 'L2': 3500})
add('L-butt-millimetres', 'flat_bar', flat, {**butt, 'connection': 'L', 'lengthUnit': 'mm', 'L1': 2500, 'L2': 3500})
add('fillet-default-mm', 'flat_bar', flat, {k: v for k, v in {**fillet, 'L1': 2500, 'L2': 3500}.items() if k != 'lengthUnit'})
(ROOT / 'weld-vectors.json').write_text(json.dumps(rows, indent=2) + '\n')
print('Generated', len(rows), 'independent weld volume vectors.')

# Automatic nominal section outline anchors, with independent metre-volume arithmetic.
pi = D('3.141592653589793')
rounded = lambda a, b, t: (2 * D(t) * (D(a) + D(b)) - (12 - 2 * pi) * D(t) ** 2) * D('.00785')
auto_sections = [
    ('plate', {'t':'10', 'W':'1000', 'n':'2'}, D(2020), D('78.5')),
    ('seamless', {'D':'60.3', 't':'3.2'}, pi * D('60.3'), pi * D('3.2') * D('57.1') * D('.00785')),
    ('chs', {'D':'114.3', 't':'4'}, pi * D('114.3'), pi * 4 * D('110.3') * D('.00785')),
    ('shs', {'A':'100', 't':'4'}, D(400) - (12 - 3 * pi) * 4, rounded(100,100,4)),
    ('rhs', {'A':'100', 'B':'50', 't':'3'}, D(300) - (12 - 3 * pi) * 3, rounded(100,50,3)),
    ('round_bar', {'D':'50'}, pi * 50, pi * 50**2 / 4 * D('.00785')),
    ('square_bar', {'A':'20'}, D(80), D('3.14')),
    ('flat_bar', {'W':'100', 't':'10'}, D(220), D('7.85')),
    ('equal_angle', {'A':'50','t':'5'}, D(200), D('3.77')),
    ('unequal_angle', {'pair':'100x75','t':'8'}, D(350), D('10.6')),
    ('gost', {'profile':'No. 20'}, D('694.8'), D('18.41')),
    ('upn', {'profile':'UPN 200'}, D(683), D('25.3')),
    ('ipn', {'profile':'IPN 200'}, D(745), D('26.2')),
    ('ipe', {'profile':'IPE 200'}, D('788.8'), D('22.4')),
    ('hea', {'profile':'HEA 200'}, D(1167), D('42.3')),
    ('heb', {'profile':'HEB 200'}, D(1182), D('61.3')),
]
auto_rows = []
for shape, dimensions, outline_mm, kg_per_m in auto_sections:
    for kind in ['fillet', 'butt']:
        component = '1000' if shape == 'plate' else '3000'
        count = D(dimensions.get('n', '1'))
        steel_length_m = 2 * D(component) / 1000
        steel = kg_per_m * steel_length_m * count
        # Convert all weld dimensions into metres before constructing volume.
        outline_m = outline_mm / 1000
        if kind == 'fillet':
            added = (D('.004') ** 2 / 2) * outline_m * 7850 * count
        else:
            gap_mass = kg_per_m * D('.002')
            caps_mass = 2 * (D('.012') * D('.001') / 2) * outline_m * 7850
            added = (gap_mass + caps_mass) * count
        w = {'enabled': True, 'type':kind, 'L1':component, 'L2':component, 'z':'4', 'gapMm':'2', 'capWidthMm':'12', 'capHeightMm':'1', 'faces':'2'}
        auto_rows.append({'shape':shape, 'kind':kind, 'input':{**dimensions, 'L':'999', 'weld':w}, 'lengthMm':clean(outline_mm), 'steelTotalKg':clean(steel), 'weldTotalKg':clean(added), 'totalKg':clean(steel+added)})
(ROOT / 'auto-weld-vectors.json').write_text(json.dumps(auto_rows, indent=2) + '\n')
print('Generated', len(auto_rows), 'independent automatic-outline weld vectors.')
