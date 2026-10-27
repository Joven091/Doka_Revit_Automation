'use strict';
const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path');
const engine = require('./engine.js');
let checks = 0;
function check(name, action) { action(); checks++; console.log('PASS ' + name); }
const rows = JSON.parse(fs.readFileSync(path.join(__dirname, 'weld-vectors.json')));
const autoRows = JSON.parse(fs.readFileSync(path.join(__dirname, 'auto-weld-vectors.json')));
for (const row of autoRows) check('automatic outline and mass ' + row.shape + ' ' + row.kind, () => {
  const r = engine.calculate(row.shape, row.input);
  assert.equal(r.weld.lengthPerRunMm, row.lengthMm); assert.equal(r.steelTotalKg, row.steelTotalKg); assert.equal(r.weld.totalKg, row.weldTotalKg); assert.equal(r.totalKg, row.totalKg); assert.equal(r.weld.runs, '1'); assert.equal(r.weld.automaticLength, true);
});
check('automatic length ignores obsolete manual length and count', () => { const row = autoRows.find(r => r.shape === 'rhs' && r.kind === 'fillet'); const r = engine.calculate('rhs', { ...row.input, weld: { ...row.input.weld, lengthMm: '-100', runs: '100' } }); assert.equal(r.totalKg, row.totalKg); });
check('changing outside dimensions changes the automatic weld path', () => { const p = engine.automaticWeldPath('rhs', { A:'100', B:'50', t:'3' }); const q = engine.automaticWeldPath('rhs', { A:'100', B:'100', t:'3' }); assert.equal(Number(q.lengthMm) - Number(p.lengthMm), 100); });
check('automatic butt gap mass uses the full nominal solid section', () => { const r = engine.calculate('square_bar', { A:'20', L:'6', weld:{ enabled:true, type:'butt', L1:'3000', L2:'3000', gapMm:'2', capWidthMm:'5', capHeightMm:'0', faces:'1' } }); assert.equal(r.weld.totalKg, '0.00628'); });
for (const row of rows) check('independent volume ' + row.id, () => {
  const r = engine.calculate(row.shape, row.input);
  assert.equal(r.steelTotalKg, row.steelTotalKg); assert.equal(r.weld.totalKg, row.weldTotalKg); assert.equal(r.totalKg, row.totalKg); assert.equal(r.input.L, row.steelLength); assert.equal(r.weld.finishedLength, row.finishedLength);
});
const fillet = rows[0].input.weld, butt = rows[5].input.weld;
for (const definition of engine.shapes) check('optional weld for ' + definition.id, () => {
  const base = engine.defaults(definition.id), half = engine.splitLength(base.L), expected = engine.calculate(definition.id, base);
  const r = engine.calculate(definition.id, { ...base, weld: { ...fillet, lengthUnit: definition.id === 'plate' ? 'mm' : 'm', L1: half, L2: half } });
  assert.equal(r.unitMassKg, expected.unitMassKg); assert.equal(r.steelTotalKg, expected.totalKg); assert.ok(Number(r.totalKg) > Number(expected.totalKg));
});
check('disabled invalid weld leaves original RHS reference unchanged', () => { const r = engine.calculate('rhs', { ...engine.defaults('rhs'), weld: { enabled: false, z: '-10', L1: '', L2: '' } }); assert.equal(engine.format(r.totalKg), '39.97'); assert.equal(r.weld, undefined); });
check('catalogue profile uses raw kg/m and cut lengths', () => { const r = engine.calculate('ipe', { profile: 'IPE 200', L: '999', weld: { ...fillet, L1: '2', L2: '4' } }); assert.equal(r.kgPerM, '22.4'); assert.equal(r.steelTotalKg, '134.4'); assert.equal(r.totalKg, '134.41884'); });
check('small weld is added before final rounding', () => { const r = engine.calculate('rhs', { ...engine.defaults('rhs'), weld: { ...fillet, L1: '3', L2: '3' } }); assert.equal(engine.format(r.totalKg), '39.99'); assert.equal(engine.format(r.weld.totalKg, 5), '0.01884'); });
check('split and combined lengths use decimal arithmetic', () => { assert.equal(engine.splitLength('0,3'), '0.15'); assert.equal(engine.combinedLength('0.1', '0.2'), '0.3'); });
check('millimetre component default cannot silently become metres', () => { const r = engine.calculate('flat_bar', { ...engine.defaults('flat_bar'), weld: { ...fillet, lengthUnit: undefined, L1: '3000', L2: '3000' } }); assert.equal(r.input.L, '6'); assert.equal(r.steelTotalKg, '47.1'); assert.equal(r.weld.lengthUnit, 'mm'); });
check('decimal length conversion stays exact both ways', () => { assert.equal(engine.convertLength('6.35', 'm', 'mm'), '6350'); assert.equal(engine.convertLength('1500.25', 'mm', 'm'), '1.50025'); });
check('L connection retains material mass without a false finished straight length', () => { for (const weld of [fillet, butt]) { const straight = engine.calculate('flat_bar', { ...engine.defaults('flat_bar'), weld }); const corner = engine.calculate('flat_bar', { ...engine.defaults('flat_bar'), weld: { ...weld, connection: 'L' } }); assert.equal(corner.totalKg, straight.totalKg); assert.equal(corner.weld.finishedLength, null); assert.equal(corner.weld.connection, 'L'); } });
check('unknown connection shape is rejected', () => assert.throws(() => engine.calculate('flat_bar', { ...engine.defaults('flat_bar'), weld: { ...fillet, connection: 'Z' } }), /straight or L/));
check('unsupported length unit is rejected', () => assert.throws(() => engine.calculate('flat_bar', { ...engine.defaults('flat_bar'), weld: { ...fillet, lengthUnit: 'cm' } }), /mm or m/));
check('decimal comma weld inputs normalize consistently', () => { const input = rows[11].input; const converted = { ...input, weld: Object.fromEntries(Object.entries(input.weld).map(([key, value]) => [key, typeof value === 'string' ? value.replace('.', ',') : value])) }; assert.equal(engine.calculate(rows[11].shape, converted).totalKg, rows[11].totalKg); });
check('bevel fill is not counted twice in butt net mass', () => {
  // For a 10 mm full-depth single-V at 90°, bevel removal is 100 mm²;
  // deposited groove area is 100 + 2*10 = 120 mm². Net addition is 20 mm².
  const removedArea = 100, depositedArea = 120;
  const r = engine.calculate('flat_bar', { ...engine.defaults('flat_bar'), weld: { ...butt, capHeightMm: '0' } });
  assert.equal(Number(r.weld.addedAreaMm2), depositedArea - removedArea); assert.equal(r.weld.totalKg, '0.0157');
});
for (const [key, value, message] of [['z', '0', /greater/], ['z', '-1', /decimal/], ['z', '', /decimal/], ['lengthMm', '0', /greater/], ['lengthMm', 'Infinity', /decimal/], ['runs', '0', /greater/], ['runs', '1.5', /whole/], ['L1', '0', /greater/], ['L2', '', /decimal/], ['type', 'unknown', /Choose/]]) check('fillet rejects ' + key + '=' + value, () => assert.throws(() => engine.calculate('flat_bar', { ...engine.defaults('flat_bar'), weld: { ...fillet, [key]: value } }), message));
for (const [key, value, message] of [['gapMm', '-1', /decimal/], ['capHeightMm', '-1', /decimal/], ['joinedThicknessMm', '0', /greater/], ['capWidthMm', '', /decimal/], ['capWidthMm', '1', /cover/], ['faces', '3', /1 or 2/], ['faces', '1.5', /1 or 2/]]) check('butt rejects ' + key + '=' + value, () => assert.throws(() => engine.calculate('flat_bar', { ...engine.defaults('flat_bar'), weld: { ...butt, [key]: value } }), message));
const summary = { checks, status: 'passed', independentVolumeVectors: rows.length + autoRows.length, scope: 'Automatic nominal section outlines and mass for all 16 shapes, retained manual arithmetic fixtures, fillet and butt net added mass, exact units, straight/L layouts, quantities and validation. Not fabrication geometry, weld strength or live supplier parity.' };
fs.writeFileSync(path.join(__dirname, 'weld-test-results.json'), JSON.stringify(summary, null, 2) + '\n'); console.log(JSON.stringify(summary, null, 2));
