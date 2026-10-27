'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const engine = require('./engine.js'), tables = require('./catalogues.js');
let checks = 0;
function check(name, action) { action(); checks++; }
check('sixteen calculators', () => { assert.equal(engine.shapes.length, 16); assert.equal(new Set(engine.shapes.map(s => s.id)).size, 16); });
for (const row of JSON.parse(fs.readFileSync(path.join(__dirname, 'formula-vectors.json')))) check('independent decimal formula ' + row.id, () => {
  const result = engine.calculate(row.shape, row.input); assert.equal(result.unitMassKg, row.unitMassKg); assert.equal(result.totalKg, row.totalKg); assert.equal(result.metric, row.metric);
});
for (const row of JSON.parse(fs.readFileSync(path.join(__dirname, 'rhs-vectors.json')))) check('retained RHS vector ' + row.id, () => {
  const result = engine.calculate('rhs', row.input); assert.equal(result.kgPerM, row.kgPerM); assert.equal(result.totalKg, row.totalKg);
});
for (const row of JSON.parse(fs.readFileSync(path.join(__dirname, 'rhs-reference-cases.json')))) check('user supplied RHS reference ' + row.id, () => {
  const result = engine.calculate('rhs', row.input); assert.equal(engine.format(result.kgPerM), row.reference.kgPerM); assert.equal(engine.format(result.totalKg), row.reference.totalKg);
});
// Independently transcribed anchors from current supplier product pages, with intermediate empty angle cells retained.
const anchors = [
  ['equal_angle', { A: '20', t: '4', L: '6' }, '1.15', '6.90'],
  ['equal_angle', { A: '50', t: '5', L: '6' }, '3.77', '22.62'],
  ['equal_angle', { A: '70', t: '8', L: '6' }, '8.37', '50.22'],
  ['equal_angle', { A: '150', t: '15', L: '6.35' }, '33.80', '214.63'],
  ['unequal_angle', { pair: '40x25', t: '4', L: '6' }, '1.93', '11.58'],
  ['unequal_angle', { pair: '100x75', t: '7', L: '6' }, '9.32', '55.92'],
  ['unequal_angle', { pair: '150x75', t: '9', L: '6' }, '15.40', '92.40'],
  ['unequal_angle', { pair: '200x150', t: '12', L: '6' }, '32.00', '192.00'],
  ['gost', { profile: 'No. 18', L: '6' }, '16.60', '99.60'],
  ['gost', { profile: 'No. 27', L: '6' }, '27.70', '166.20'],
  ['upn', { profile: 'UPN 50 x 25', L: '6' }, '3.86', '23.16'],
  ['upn', { profile: 'UPN 50 x 38', L: '6' }, '5.59', '33.54'],
  ['upn', { profile: 'UPN 200', L: '6' }, '25.30', '151.80'],
  ['ipe', { profile: 'IPE 80', L: '6' }, '6.00', '36.00'],
  ['ipe', { profile: 'IPE 200', L: '6' }, '22.40', '134.40'],
  ['ipn', { profile: 'IPN 80', L: '6' }, '5.94', '35.64'],
  ['ipn', { profile: 'IPN 200', L: '6' }, '26.20', '157.20'],
  ['hea', { profile: 'HEA 100', L: '6' }, '16.70', '100.20'],
  ['hea', { profile: 'HEA 200', L: '6' }, '42.30', '253.80'],
  ['heb', { profile: 'HEB 450', L: '6' }, '171.00', '1026.00'],
  ['heb', { profile: 'HEB 1000', L: '6' }, '314.00', '1884.00']
];
for (const [id, input, mass, total] of anchors) check('published mass anchor ' + id + ' ' + JSON.stringify(input), () => { const r = engine.calculate(id, input); assert.equal(engine.format(r.unitMassKg), mass); assert.equal(engine.format(r.totalKg), total); });
check('nominal and actual HEA heights remain distinct', () => { assert.equal(engine.profile('hea', { profile: 'HEA 100' }).h, '96'); assert.equal(engine.profile('hea', { profile: 'HEA 200' }).h, '190'); });
for (const definition of engine.shapes) check('default inputs valid for ' + definition.id, () => { const r = engine.calculate(definition.id, engine.defaults(definition.id)); assert.ok(Number(r.unitMassKg) > 0); assert.ok(Number(r.totalKg) > 0); });
for (const [family, table] of Object.entries(tables)) check('catalogue integrity ' + family, () => {
  assert.ok(table.sourceUrl.startsWith('https://www.maiak-m.bg/en/product/')); assert.equal(new Set(table.rows.map(p => p.id)).size, table.rows.length);
  for (const row of table.rows) { assert.ok(Number(row.kgPerM) > 0); if (row.h) { assert.ok(Number(row.h) > 2 * Number(row.t)); assert.ok(Number(row.b) > Number(row.s)); } else { assert.ok(Number(row.t) < Math.min(Number(row.A), Number(row.B))); } }
});
for (const id of ['plate', 'seamless', 'chs', 'shs', 'rhs', 'round_bar', 'square_bar', 'flat_bar']) {
  const input = engine.defaults(id), field = engine.shape(id).fields[0].key;
  for (const value of ['', '0', '-1', 'NaN', 'Infinity', '<script>', '1.2.3']) check('invalid ' + id + ' ' + value, () => assert.throws(() => engine.calculate(id, { ...input, [field]: value })));
}
check('plate count must be an integer', () => assert.throws(() => engine.calculate('plate', { ...engine.defaults('plate'), n: '1.5' }), /whole/));
check('plate quantity multiplies the unrounded piece mass', () => { const r = engine.calculate('plate', { t: '10', W: '1000', L: '2000', n: '3' }); assert.equal(r.unitMassKg, '157'); assert.equal(r.totalKg, '471'); assert.equal(r.metric, 'piece'); });
check('a metre cannot silently replace plate millimetres', () => { const r = engine.calculate('plate', { t: '10', W: '1000', L: '2', n: '1' }); assert.equal(r.totalKg, '0.157'); });
check('pipe opening cannot collapse', () => assert.throws(() => engine.calculate('chs', { D: '100', t: '50', L: '6' }), /less than/));
check('square opening cannot collapse', () => assert.throws(() => engine.calculate('shs', { A: '100', t: '50', L: '6' })));
check('unavailable equal angle thickness is not interpolated', () => assert.throws(() => engine.calculate('equal_angle', { A: '20', t: '5', L: '6' }), /listed/));
check('unavailable unequal angle is not invented', () => assert.throws(() => engine.calculate('unequal_angle', { pair: '77x33', t: '5', L: '6' }), /listed/));
check('unknown nominal beam is not approximated', () => assert.throws(() => engine.calculate('ipe', { profile: 'IPE 123', L: '6' }), /listed/));
check('unknown calculator is rejected', () => assert.throws(() => engine.calculate('unknown', { L: '6' })));
check('decimal comma inputs', () => { assert.equal(engine.calculate('flat_bar', { W: '100,5', t: '3,2', L: '6,35' }).totalKg, engine.calculate('flat_bar', { W: '100.5', t: '3.2', L: '6.35' }).totalKg); });
const summary = { checks, status: 'passed', calculators: engine.shapes.length, catalogueEntries: Object.values(tables).reduce((n, table) => n + table.rows.length, 0), formulaVectors: JSON.parse(fs.readFileSync(path.join(__dirname, 'formula-vectors.json'))).length,
  websiteLiveExecution: false, scope: 'Independent formula arithmetic, current published supplier table anchors and retained user-provided RHS examples; not live website parity.' };
fs.writeFileSync(path.join(__dirname, 'engine-test-results.json'), JSON.stringify(summary, null, 2) + '\n'); console.log(JSON.stringify(summary, null, 2));
