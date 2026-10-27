'use strict';
const fs = require('node:fs'), path = require('node:path');
const { Resvg } = require('./test-dependencies.cjs')('@resvg/resvg-js');
const engine = require('./engine.js'), sketches = require('./schematics.js');
const output = path.resolve(process.argv[2] || path.join(__dirname, 'previews'));
fs.mkdirSync(output, { recursive: true });
const css = '.dimension{color:#526b83}.dimension line,.dimension path{stroke:currentColor;stroke-width:1.4;fill:none}.dimension .extension{stroke:#a9bac9;stroke-width:1}.dimension text{fill:currentColor;font-family:DejaVu Sans,sans-serif;font-size:15px;font-weight:600;stroke:none}.dimension .label-back{fill:#f8fafc;stroke:none}';
for (let page = 0; page < 2; page++) {
  let cells = '';
  engine.shapes.slice(page * 8, page * 8 + 8).forEach((definition, i) => {
    const result = engine.calculate(definition.id, engine.defaults(definition.id)), x = (i % 2) * 780, y = Math.floor(i / 2) * 355;
    const section = sketches.section(definition, result).replace('<svg ', '<svg x="4" y="35" width="370" height="308" ').replaceAll('cross-', definition.id + '-cross-').replace('id="section-schematic"', 'id="' + definition.id + '-section"');
    const member = sketches.member(definition, result).replace('<svg ', '<svg x="383" y="35" width="382" height="302" ').replaceAll('member-', definition.id + '-member-').replace('id="length-schematic"', 'id="' + definition.id + '-length"');
    cells += `<g transform="translate(${x} ${y})"><rect x="2" y="2" width="776" height="351" rx="8" fill="#f8fafc" stroke="#d9e2eb"/><text x="18" y="27" font-family="DejaVu Sans" font-size="17" fill="#203047">${definition.title}</text>${section}${member}</g>`;
  });
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="1560" height="1420" viewBox="0 0 1560 1420"><style>${css}</style>${cells}</svg>`;
  fs.writeFileSync(path.join(output, 'schematics-' + (page + 1) + '.png'), new Resvg(svg, { background: '#ffffff' }).render().asPng());
}
console.log('Rendered 16 calculator schematics to ' + output);
