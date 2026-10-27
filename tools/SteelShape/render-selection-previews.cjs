'use strict';
const fs=require('node:fs'),path=require('node:path'),engine=require('./engine.js'),sketches=require('./schematics.js');
const {Resvg}=require('./test-dependencies.cjs')('@resvg/resvg-js');
const output=path.resolve(process.argv[2]||path.join(__dirname,'previews'));fs.mkdirSync(output,{recursive:true});
const examples=[
  {shape:'rhs',name:'selector-rhs',selected:{'edge-0':'20','edge-2':'60'},active:'edge-0'},
  {shape:'ipe',name:'selector-ipe',selected:{'edge-0':'50','edge-3':'80'},active:'edge-3'},
  {shape:'chs',name:'selector-circle',selected:{'edge-0':'25'},active:'edge-0'},
  {shape:'upn',name:'selector-upn',selected:{'edge-0':'full','edge-8':'60'},active:'edge-8'},
];
for(const example of examples){
  const definition=engine.shape(example.shape),model=engine.automaticWeldPath(example.shape,engine.defaults(example.shape));
  const choices={...Object.fromEntries(model.segments.map(edge=>[edge.id,'0'])),...example.selected};
  const svg=sketches.weldSelector(definition,model,choices,example.active).replace('<svg ','<svg xmlns="http://www.w3.org/2000/svg" ');
  fs.writeFileSync(path.join(output,example.name+'.png'),new Resvg(svg,{fitTo:{mode:'width',value:660},background:'#f8fafc',font:{defaultFontFamily:'DejaVu Sans'}}).render().asPng());
}
console.log('Rendered partial rectangular, IPE, circular and UPN weld selectors.');
