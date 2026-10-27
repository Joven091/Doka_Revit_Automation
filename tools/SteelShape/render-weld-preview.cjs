'use strict';
const fs = require('node:fs'), path = require('node:path');
const { Resvg } = require('./test-dependencies.cjs')('@resvg/resvg-js');
const engine = require('./engine.js'), sketches = require('./schematics.js');
const vectors = JSON.parse(fs.readFileSync(path.join(__dirname, 'weld-vectors.json')));
const output = path.resolve(process.argv[2] || path.join(__dirname, 'previews'));
fs.mkdirSync(output, { recursive: true });
const css = '.dimension{color:#526b83}.dimension line,.dimension path{stroke:currentColor;stroke-width:1.4;fill:none}.dimension .extension{stroke:#a9bac9;stroke-width:1}.dimension text{fill:currentColor;font-family:DejaVu Sans,sans-serif;font-size:15px;font-weight:600;stroke:none}.dimension .label-back{fill:#f8fafc;stroke:none}';
for (const index of [12, 13, 14, 15, 6]) {
  const row = vectors[index], result = engine.calculate(row.shape, row.input);
  const svg = sketches.weld(result).replace('<svg ', '<svg xmlns="http://www.w3.org/2000/svg" ').replace(/(<svg[^>]*>)/, '$1<style>' + css + '</style>');
  fs.writeFileSync(path.join(output, row.id + '.png'), new Resvg(svg, { fitTo: { mode: 'width', value: 720 }, background: '#f8fafc', font: { defaultFontFamily: 'DejaVu Sans' } }).render().asPng());
}
for (const [name, connection, type] of [['empty-straight-fillet', 'straight', 'fillet'], ['empty-L-fillet', 'L', 'fillet'], ['empty-L-butt', 'L', 'butt']]) {
  const svg = sketches.weldGuide({ weld: { connection, type, L1: '', L2: '', z: '', lengthMm: '', joinedThicknessMm: '', gapMm: '', capWidthMm: '', capHeightMm: '' } }).replace('<svg ', '<svg xmlns="http://www.w3.org/2000/svg" ').replace(/(<svg[^>]*>)/, '$1<style>' + css + '</style>');
  fs.writeFileSync(path.join(output, name + '.png'), new Resvg(svg, { fitTo: { mode: 'width', value: 660 }, background: '#f8fafc', font: { defaultFontFamily: 'DejaVu Sans' } }).render().asPng());
}
console.log('Rendered straight and L weld layouts with entered values and empty dimension guides.');
let cells = '';
engine.shapes.forEach((definition, i) => {
  const input = engine.defaults(definition.id), outline = engine.automaticWeldPath(definition.id, input), x = (i % 4) * 200, y = Math.floor(i / 4) * 210;
  const svg = sketches.weldCoverage(definition, outline.dimensions).replace('<svg ', '<svg x="25" y="3" width="150" height="142" ').replaceAll('coverage-', definition.id + '-coverage-').replace('id="weld-coverage-schematic"', 'id="coverage-' + definition.id + '"');
  cells += `<g transform="translate(${x} ${y})"><rect x="2" y="2" width="196" height="206" rx="7" fill="#f8fafc" stroke="#d9e2eb"/>${svg}<text x="100" y="171" text-anchor="middle" font-family="DejaVu Sans" font-size="14" fill="#203047">${definition.id.toUpperCase()}</text><text x="100" y="195" text-anchor="middle" font-family="DejaVu Sans" font-size="14" fill="#60738b">${engine.format(outline.lengthMm)} mm</text></g>`;
});
const gallery = `<svg xmlns="http://www.w3.org/2000/svg" width="800" height="840" viewBox="0 0 800 840">${cells}</svg>`;
fs.writeFileSync(path.join(output, 'automatic-weld-paths.png'), new Resvg(gallery, { background:'#ffffff', font:{ defaultFontFamily:'DejaVu Sans' } }).render().asPng());
console.log('Rendered all 16 automatic weld outlines.');
