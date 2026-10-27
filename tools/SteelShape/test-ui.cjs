'use strict';
const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path'), vm = require('node:vm');
const { parseHTML } = require('./test-dependencies.cjs')('linkedom');
const engine = require('./engine.js');
const html = fs.readFileSync(process.argv[2] || path.join(__dirname, 'Steel-weight-calculators.html'), 'utf8');
const { window } = parseHTML(html), document = window.document;
// linkedom supplies a DOM, not a rendering browser. Implement its missing select.value setter as the browser does.
const descriptor = Object.getOwnPropertyDescriptor(window.HTMLSelectElement.prototype, 'value');
if (!descriptor.set) Object.defineProperty(window.HTMLSelectElement.prototype, 'value', { configurable: true, get: descriptor.get, set(value) { for (const option of this.querySelectorAll('option')) option.removeAttribute('selected'); const selected = Array.from(this.querySelectorAll('option')).find(option => option.value === String(value)); if (selected) selected.setAttribute('selected', ''); } });
const context = vm.createContext({ window, document, console });
for (const [, script] of html.matchAll(/<script>([\s\S]*?)<\/script>/g)) vm.runInContext(script, context);
const $ = id => document.getElementById(id);
let checks = 0;
function check(name, action) { action(); checks++; console.log('PASS ' + name); }
function chooseShape(id) { document.querySelector('button[data-shape="' + id + '"]').dispatchEvent(new window.Event('click')); }
function enter(key, value) { const input = $('field-' + key); input.value = value; input.dispatchEvent(new window.Event(input.tagName === 'SELECT' ? 'change' : 'input')); }
function ready() {
  assert.equal($('input-error').textContent, ''); assert.notEqual($('mass').textContent, '—');
  for (const container of [$('section-drawing'), $('length-drawing')]) {
    const svg = container.querySelector('svg'); assert.ok(svg); assert.ok(svg.getAttribute('aria-label')); assert.doesNotMatch(svg.outerHTML, /NaN|Infinity|undefined/);
  }
  const ids = Array.from(document.querySelectorAll('[id]')).map(e => e.id); assert.equal(ids.length, new Set(ids).size);
}
check('all sixteen calculators available', () => { assert.equal($('calculator-menu').querySelectorAll('button').length, 16); assert.equal($('calculator-picker').querySelectorAll('option').length, 16); });
check('initial RHS preserves approved example and two views', () => { ready(); assert.equal($('mass').textContent, '6.66'); assert.equal($('total').textContent, '39.97'); });
for (const definition of engine.shapes) check('navigation, defaults and labelled schematics for ' + definition.id, () => {
  chooseShape(definition.id); ready(); const result = engine.calculate(definition.id, engine.defaults(definition.id));
  assert.equal($('page-heading').textContent, definition.title); assert.equal($('mass').textContent, engine.format(result.unitMassKg)); assert.equal($('total').textContent, engine.format(result.totalKg));
  assert.equal(document.querySelectorAll('button[aria-pressed="true"]').length, 1);
  for (const reference of Array.from(document.querySelectorAll('[marker-start],[marker-end]'))) for (const attribute of ['marker-start', 'marker-end']) { const value = reference.getAttribute(attribute); if (value) assert.ok(document.getElementById(value.match(/#([^)]*)/)[1])); }
});
check('profile choice changes dimensions, mass and sketch together', () => { chooseShape('hea'); enter('profile', 'HEA 100'); ready(); assert.equal($('field-h').value, '96'); assert.equal($('field-b').value, '100'); assert.equal($('mass').textContent, '16.70'); assert.match($('section-drawing').textContent, /h = 96 mm/); assert.equal($('field-h').readOnly, true); });
check('no nominal 100 mm height substituted for actual 96 mm HEA', () => { assert.match($('section-drawing').querySelector('svg').getAttribute('aria-label'), /h 96 mm/); });
check('UPN equal nominal heights can have distinct widths and masses', () => { chooseShape('upn'); enter('profile', 'UPN 50 x 25'); assert.equal($('mass').textContent, '3.86'); enter('profile', 'UPN 50 x 38'); assert.equal($('mass').textContent, '5.59'); assert.equal($('field-b').value, '38'); });
check('equal angle thickness choices follow the selected side', () => { chooseShape('equal_angle'); enter('A', '20'); ready(); assert.deepEqual(Array.from($('field-t').options).map(o => o.value), ['3', '4']); assert.equal($('mass').textContent, '0.88'); enter('t', '4'); assert.equal($('mass').textContent, '1.15'); });
check('unequal angle thickness gaps are retained', () => { chooseShape('unequal_angle'); enter('pair', '150x75'); ready(); assert.deepEqual(Array.from($('field-t').options).map(o => o.value), ['9', '10', '12', '15']); enter('t', '9'); assert.equal($('mass').textContent, '15.40'); });
check('the plate calculator uses millimetres and a piece output', () => { chooseShape('plate'); ready(); assert.equal($('mass-unit').textContent, 'kg/piece'); assert.equal($('length-caption').textContent, 'Plate length'); assert.match($('length-drawing').textContent, /L = 2000 mm/); enter('n', '3'); assert.equal($('total').textContent, '471.00'); });
check('noninteger quantities clear outputs and keep piece units', () => { enter('n', '1.5'); assert.match($('input-error').textContent, /whole/); assert.equal($('mass').textContent, '—'); assert.equal($('mass-unit').textContent, 'kg/piece'); assert.match($('section-drawing').textContent, /valid dimensions/); enter('n', '1'); ready(); });
check('calculator selection retains entered values independently', () => { chooseShape('rhs'); enter('L', '2'); assert.equal($('total').textContent, '13.32'); chooseShape('ipe'); enter('L', '3'); assert.equal($('total').textContent, '67.20'); chooseShape('rhs'); assert.equal($('field-L').value, '2'); assert.equal($('total').textContent, '13.32'); });
check('invalid hollow section clears old geometry and recovers', () => { enter('t', '25'); assert.equal($('mass').textContent, '—'); assert.match($('section-drawing').textContent, /valid dimensions/); assert.equal($('section-drawing').querySelector('[data-dimension]'), null); enter('t', '3'); ready(); });
check('focused parameter highlights the corresponding dimension', () => { $('field-B').dispatchEvent(new window.Event('focus')); const active = document.querySelectorAll('.dimension.active'); assert.equal(active.length, 1); assert.equal(active[0].dataset.dimension, 'B'); $('field-B').dispatchEvent(new window.Event('blur')); assert.equal(document.querySelectorAll('.dimension.active').length, 0); });
check('pipe wall validation rejects a collapsed opening', () => { chooseShape('chs'); enter('D', '100'); enter('t', '50'); assert.match($('input-error').textContent, /diameter/); assert.equal($('total').textContent, '—'); enter('t', '4'); ready(); });
check('mobile selection operates the same calculator state', () => { $('calculator-picker').value = 'round_bar'; $('calculator-picker').dispatchEvent(new window.Event('change')); ready(); assert.equal($('page-heading').textContent, 'Round steel bars'); assert.equal($('mass-unit').textContent, 'kg/m'); });
check('approved user-provided RHS examples still pass through the UI', () => { chooseShape('rhs'); for (const row of JSON.parse(fs.readFileSync(path.join(__dirname, 'rhs-reference-cases.json')))) { for (const [key, value] of Object.entries(row.input)) enter(key, value); ready(); assert.equal($('mass').textContent, row.reference.kgPerM); assert.equal($('total').textContent, row.reference.totalKg); } });
check('development comparison controls are not in the product', () => { const main = document.querySelector('main').textContent; for (const text of ['Add to comparison', 'Open reference website', 'comparison prototype', 'Website agreement is not verified']) assert.ok(!main.includes(text)); assert.equal(document.querySelectorAll('main form').length, 1); assert.equal(document.querySelector('.navigation h2').textContent,'Steel Shape'); assert.doesNotMatch(main,/Engineering Dashboard/); });
function toggleWeld(enabled) { $('weld-enabled').checked = enabled; $('weld-enabled').dispatchEvent(new window.Event('change')); }
function enterWeld(key, value) { const input = $('weld-' + key); input.value = value; input.dispatchEvent(new window.Event(input.tagName === 'SELECT' ? 'change' : 'input')); }
const autoReferences = JSON.parse(fs.readFileSync(path.join(__dirname, 'auto-weld-vectors.json')));
check('no Calculate button is needed for live results', () => { assert.equal(document.querySelectorAll('button[type="submit"]').length, 0); enter('L', '7'); assert.notEqual($('total').textContent, '4.44'); });
check('requested headings and notes are absent from the interface', () => { assert.equal($('sketch-heading'), null); assert.equal($('weld-summary'), null); assert.equal($('steel-total'), null); assert.equal($('weld-total'), null); assert.equal($('weld-formula'), null); assert.equal($('finished-length'), null); assert.doesNotMatch(document.querySelector('main').textContent, /Your section|Your plate|Added weld:/); });
let originalSection, originalMember;
check('enabling incomplete weld preserves the original two section schematics', () => {
  enter('A', '100'); enter('B', '50'); enter('t', '3'); enter('L', '6');
  originalSection = $('section-drawing').innerHTML; originalMember = $('length-drawing').innerHTML; toggleWeld(true);
  assert.equal($('weld-L1').value, '3000'); assert.equal($('weld-L2').value, '3000'); assert.equal($('total').textContent, '—'); assert.equal($('section-drawing').innerHTML, originalSection); assert.equal($('length-drawing').innerHTML, originalMember); assert.equal($('length-caption').textContent, 'Member length');
});
check('automatic weld length is read-only and highlighted on the actual section', () => {
  assert.equal($('weld-lengthMm').readOnly, true); assert.equal($('weld-lengthMm').value, '292.27'); assert.ok($('weld-coverage').querySelector('svg')); assert.match($('weld-coverage').textContent, /Interactive weld selection/); assert.equal($('weld-runs'), null); assert.doesNotMatch($('weld-fields').textContent, /identical|seams|passes/);
});
check('fillet size has no note below it and uses millimetres', () => { assert.match($('weld-z').parentElement.textContent, /Fillet leg size \(mm\)/); assert.equal($('weld-z').parentElement.querySelector('small'), null); assert.equal($('help-z'), null); assert.match($('weld-L1').parentElement.textContent, /First piece length \(mm\)/); });
check('fillet total requires only size and piece lengths', () => {
  enterWeld('z', '4'); ready(); assert.equal($('mass').textContent, '6.66'); assert.equal($('total').textContent, '39.98'); assert.equal($('section-drawing').innerHTML, originalSection); assert.equal($('length-drawing').innerHTML, originalMember);
});
check('attempting to edit the automatic display cannot alter the allowance', () => { enterWeld('lengthMm', '1'); ready(); assert.equal($('weld-lengthMm').value, '292.27'); assert.equal($('total').textContent, '39.98'); });
check('section edits update the automatic weld length and allowance', () => {
  enter('B', '100'); ready(); assert.equal($('weld-lengthMm').value, '392.27'); const ref = engine.calculate('rhs', {A:'100', B:'100', t:'3', L:'6', weld:{enabled:true, L1:'3000', L2:'3000', z:'4'}}); assert.equal($('total').textContent, engine.format(ref.totalKg)); enter('B', '50'); ready();
});
check('invalid section dimensions clear a stale automatic length', () => { enter('B', '5'); assert.equal($('weld-lengthMm').value, ''); assert.equal($('weld-coverage').innerHTML, ''); assert.equal($('total').textContent, '—'); enter('B','50'); ready(); assert.equal($('weld-lengthMm').value, '292.27'); });
check('L layout is confined to the optional weld diagram', () => { const total = $('total').textContent; enterWeld('connection', 'L'); ready(); assert.match($('weld-drawing').textContent, /L connection · 90°/); assert.equal($('length-drawing').innerHTML, originalMember); assert.equal($('section-drawing').innerHTML, originalSection); assert.equal($('total').textContent, total); enterWeld('connection', 'straight'); });
check('component lengths still update the original member length diagram', () => { enterWeld('L1', '2000'); enterWeld('L2', '4500'); ready(); assert.equal($('field-L').value, '6.5'); assert.equal($('total').textContent, '43.32'); assert.match($('length-drawing').textContent, /L = 6.5 m/); enterWeld('L2', '4000'); ready(); });
check('butt weld derives gap steel from the selected section', () => {
  enterWeld('type', 'butt'); assert.equal($('weld-z'), null); assert.equal($('weld-joinedThicknessMm'), null); assert.equal($('weld-runs'), null); assert.equal($('weld-lengthMm').value, '292.27');
  enterWeld('gapMm', '2'); enterWeld('capWidthMm', '12'); enterWeld('capHeightMm', '1'); enterWeld('faces', '2'); ready();
  assert.equal($('total').textContent, '40.01'); assert.match($('weld-drawing').textContent, /Butt weld/); assert.equal($('length-drawing').innerHTML, originalMember);
});
check('flush butt with no gap adds zero net mass', () => { enterWeld('gapMm', '0'); enterWeld('capHeightMm', '0'); ready(); assert.equal($('total').textContent, '39.97'); });
check('butt cap side selection changes the allowance', () => { enterWeld('gapMm', '2'); enterWeld('capHeightMm', '1'); enterWeld('faces', '1'); ready(); assert.equal($('total').textContent, '39.99'); enterWeld('faces', '2'); assert.equal($('total').textContent, '40.01'); });
check('invalid weld keeps both the guide and the original main schematic', () => { enterWeld('gapMm', '-1'); assert.equal($('total').textContent, '—'); assert.ok($('weld-drawing').querySelector('[data-dimension]')); assert.equal($('section-drawing').innerHTML, originalSection); assert.equal($('length-drawing').innerHTML, originalMember); enterWeld('gapMm', '2'); ready(); });
check('empty dimensions retain guides for all four weld layouts', () => {
  enterWeld('L1', ''); enterWeld('L2', '');
  for (const connection of ['straight', 'L']) for (const type of ['fillet', 'butt']) { enterWeld('connection', connection); enterWeld('type', type); const svg = $('weld-drawing').querySelector('svg'); assert.ok(svg); assert.doesNotMatch(svg.outerHTML, /NaN|Infinity|undefined/); assert.equal($('total').textContent, '—'); assert.match(svg.textContent, connection === 'L' ? /L connection/ : /Straight connection/); }
  enterWeld('connection', 'straight'); enterWeld('type', 'fillet'); enterWeld('L1', '2000'); enterWeld('L2', '4000'); ready();
});
check('disabling weld returns to the original member calculation', () => { toggleWeld(false); ready(); assert.equal($('weld-content').hidden, true); assert.equal($('field-L').readOnly, false); assert.equal($('total').textContent, '39.97'); assert.equal($('length-drawing').innerHTML, originalMember); });
check('plate automatic outline and assembly quantities are both included', () => {
  chooseShape('plate'); enter('n', '3'); toggleWeld(true); enterWeld('L1','1250'); enterWeld('L2','750'); enterWeld('z','6'); ready(); assert.equal($('weld-lengthMm').value, '2020.00'); assert.equal($('mass').textContent, '157.00'); assert.equal($('total').textContent, '471.86'); assert.equal($('length-caption').textContent, 'Plate length');
});
for (const definition of engine.shapes) check('automatic outline and original schematic for ' + definition.id, () => {
  chooseShape(definition.id); if ($('weld-enabled').checked) toggleWeld(false);
  const defaults = engine.defaults(definition.id);
  for (const [key, value] of Object.entries(defaults)) enter(key, value);
  toggleWeld(true); enterWeld('type', 'fillet');
  const half = engine.convertLength(engine.splitLength(defaults.L), definition.id === 'plate' ? 'mm' : 'm', 'mm'); enterWeld('L1', half); enterWeld('L2', half); enterWeld('z','4'); ready();
  const ref = autoReferences.find(row => row.shape === definition.id && row.kind === 'fillet'); assert.equal($('weld-lengthMm').value, engine.format(ref.lengthMm)); assert.ok($('weld-coverage').querySelector('svg')); assert.equal($('weld-runs'), null); assert.equal($('length-drawing').querySelector('#length-schematic').getAttribute('viewBox'), '0 0 380 300'); assert.equal(document.querySelector('.schematic-panel').textContent.includes('Your section'), false);
});
function clickEdge(id) { const target=$('weld-coverage').querySelector('[data-weld-edge="'+id+'"]'); assert.ok(target); target.dispatchEvent(new window.Event('click',{bubbles:true})); }
function chooseEdge(id) { $('weld-edge-picker').value=id; $('weld-edge-picker').dispatchEvent(new window.Event('change')); }
function wholeEdge(checked) { $('weld-edge-full').checked=checked; $('weld-edge-full').dispatchEvent(new window.Event('change')); }
function partialLength(value) { $('weld-edge-length').value=value; $('weld-edge-length').dispatchEvent(new window.Event('input')); }
function clearEdges() { $('weld-clear').dispatchEvent(new window.Event('click')); }
let selectorSection,selectorMember;
check('interactive selector starts with full selection and has no All around button',()=>{
  chooseShape('flat_bar');if($('weld-enabled').checked)toggleWeld(false);enter('W','100');enter('t','10');enter('L','6');toggleWeld(true);enterWeld('L1','3000');enterWeld('L2','3000');enterWeld('type','fillet');enterWeld('z','4');ready();
  assert.equal($('weld-lengthMm').value,'220.00');assert.equal($('weld-coverage').querySelectorAll('[aria-pressed="true"]').length,4);assert.ok($('weld-clear'));assert.ok(!Array.from(document.querySelectorAll('button')).some(button=>/all around/i.test(button.textContent)));
  selectorSection=$('section-drawing').innerHTML;selectorMember=$('length-drawing').innerHTML;
});
check('edge click toggles the actual welded length and total',()=>{clickEdge('edge-0');ready();assert.equal($('weld-lengthMm').value,'120.00');assert.equal($('total').title,'Unrounded: 47.107536');assert.equal($('weld-coverage').querySelector('[data-weld-edge="edge-0"]').getAttribute('aria-pressed'),'false');clickEdge('edge-0');assert.equal($('weld-lengthMm').value,'220.00');});
check('Clear gives steel-only weight even with no weld size entered',()=>{enterWeld('z','');clearEdges();ready();assert.equal($('weld-lengthMm').value,'0.00');assert.equal($('total').textContent,'47.10');assert.equal($('weld-coverage').querySelectorAll('[aria-pressed="true"]').length,0);enterWeld('z','4');});
check('clicking one cleared edge restores only that edge',()=>{clickEdge('edge-0');ready();assert.equal($('weld-lengthMm').value,'100.00');assert.equal($('total').title,'Unrounded: 47.10628');assert.equal($('weld-edge-picker').value,'edge-0');assert.equal($('weld-edge-full').checked,true);});
check('partial editor changes the highlighted portion without mouse dragging',()=>{wholeEdge(false);partialLength('40');ready();assert.equal($('weld-lengthMm').value,'40.00');assert.equal($('total').title,'Unrounded: 47.102512');assert.equal($('weld-coverage').querySelector('[data-weld-edge="edge-0"]').dataset.partial,'true');assert.equal($('weld-edge-length').readOnly,false);});
check('edge picker focuses a different edge without changing its weld',()=>{chooseEdge('edge-3');assert.equal($('weld-edge-length').value,'0');assert.equal($('weld-lengthMm').value,'40.00');wholeEdge(true);assert.equal($('weld-lengthMm').value,'50.00');});
check('keyboard space toggles an edge and prevents scrolling',()=>{const target=$('weld-coverage').querySelector('[data-weld-edge="edge-3"]'),event=new window.Event('keydown',{bubbles:true,cancelable:true});Object.defineProperty(event,'key',{value:' '});target.dispatchEvent(event);ready();assert.equal($('weld-lengthMm').value,'40.00');assert.equal(event.defaultPrevented,true);});
check('too-long partial is reported and the entered value remains editable',()=>{chooseEdge('edge-0');partialLength('101');assert.match($('input-error').textContent,/cannot exceed/);assert.equal($('weld-edge-length').value,'101');assert.equal($('weld-lengthMm').value,'');assert.equal($('total').textContent,'—');assert.ok($('weld-coverage').querySelector('svg'));partialLength('40');ready();});
check('weld selections leave the main section and member schematics unchanged',()=>{assert.equal($('section-drawing').innerHTML,selectorSection);assert.equal($('length-drawing').innerHTML,selectorMember);});
check('partial selection persists independently between calculators',()=>{chooseShape('ipe');chooseShape('flat_bar');ready();assert.equal($('weld-lengthMm').value,'40.00');chooseEdge('edge-0');assert.equal($('weld-edge-length').value,'40');});
check('section growth retains entered millimetres; shrinkage is validated',()=>{enter('W','150');ready();assert.equal($('weld-lengthMm').value,'40.00');assert.equal($('total').title,'Unrounded: 70.652512');enter('W','30');assert.match($('input-error').textContent,/cannot exceed/);enter('W','100');ready();});
check('partial butt gap fill follows selected material',()=>{enterWeld('type','butt');enterWeld('gapMm','2');enterWeld('capWidthMm','12');enterWeld('capHeightMm','1');enterWeld('faces','1');ready();assert.equal($('total').title,'Unrounded: 47.108164');chooseEdge('edge-2');partialLength('40');ready();assert.equal($('weld-lengthMm').value,'80.00');assert.equal($('total').title,'Unrounded: 47.110048');});
check('circular sections provide four independently selectable arcs',()=>{chooseShape('chs');enter('D','114.3');enter('t','4');enter('L','6');if(!$('weld-enabled').checked)toggleWeld(true);enterWeld('L1','3000');enterWeld('L2','3000');enterWeld('type','fillet');enterWeld('z','4');clearEdges();clickEdge('edge-0');ready();assert.equal($('weld-coverage').querySelectorAll('[data-weld-edge]').length,4);assert.equal($('weld-lengthMm').value,'89.77');wholeEdge(false);partialLength('25');ready();assert.equal($('weld-lengthMm').value,'25.00');assert.equal($('weld-coverage').querySelector('[data-weld-edge="edge-0"]').dataset.partial,'true');});
const summary = { checks, status: 'passed', scope: 'Local DOM interactions, clickable and keyboard weld edges/arcs, partial editing, selection persistence, automatic totals, unchanged main schematics and removed controls. Not real-browser rendering or live website parity.' };
fs.writeFileSync(path.join(__dirname, 'ui-test-results.json'), JSON.stringify(summary, null, 2) + '\n'); console.log(JSON.stringify(summary, null, 2));
