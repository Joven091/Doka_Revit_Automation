'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),engine=require('./engine.js');
let checks=0;
function check(name,action){action();checks++;console.log('PASS '+name);}
const rows=JSON.parse(fs.readFileSync(path.join(__dirname,'selection-vectors.json')));
for(const row of rows)check(row.name,()=>{const r=engine.calculate(row.shape,row.input);assert.equal(r.weld.lengthPerRunMm,row.lengthMm);assert.equal(r.steelTotalKg,row.steelTotalKg);assert.equal(r.weld.totalKg,row.weldTotalKg);assert.equal(r.weld.gapMassPerJointKg,row.gapPerJointKg);assert.equal(r.totalKg,row.totalKg);});
for(const definition of engine.shapes)check('all and none for '+definition.id,()=>{
  const input=engine.defaults(definition.id),model=engine.automaticWeldPath(definition.id,input),all=engine.selectedWeldPath(model),none=engine.selectedWeldPath(model,Object.fromEntries(model.segments.map(edge=>[edge.id,'0'])));
  assert.equal(all.lengthMm,model.lengthMm);assert.equal(all.gapShareNumerator,all.gapShareDenominator);assert.equal(none.lengthMm,'0');assert.equal(none.gapShareNumerator,'0');assert.equal(new Set(model.segments.map(edge=>edge.id)).size,model.segments.length);
  const half=engine.convertLength(engine.splitLength(input.L),definition.id==='plate'?'mm':'m','mm');
  const r=engine.calculate(definition.id,{...input,weld:{enabled:true,L1:half,L2:half,edges:Object.fromEntries(model.segments.map(edge=>[edge.id,'0']))}});assert.equal(r.totalKg,engine.calculate(definition.id,input).totalKg);assert.equal(r.weld.totalKg,'0');
});
const model=engine.automaticWeldPath('flat_bar',{W:'100',t:'10'});
for(const bad of ['', '-1','NaN','101'])check('reject invalid partial '+bad,()=>assert.throws(()=>engine.selectedWeldPath(model,{'edge-0':bad})));
check('unknown edges rejected',()=>assert.throws(()=>engine.selectedWeldPath(model,{'edge-99':'10'}),/unavailable/));
check('arrays cannot silently become selections',()=>assert.throws(()=>engine.selectedWeldPath(model,[]),/invalid/));
check('decimal commas accepted in partial lengths',()=>assert.equal(engine.selectedWeldPath(model,{'edge-0':'25,5'}).segments[0].weldedLengthMm,'25.5'));
check('dimension change preserves a partial millimetre length',()=>{const bigger=engine.automaticWeldPath('flat_bar',{W:'150',t:'10'});assert.equal(engine.selectedWeldPath(bigger,{'edge-0':'40'}).segments[0].weldedLengthMm,'40');});
check('section shrinking below partial length is reported',()=>assert.throws(()=>engine.selectedWeldPath(engine.automaticWeldPath('flat_bar',{W:'30',t:'10'}),{'edge-0':'40'}),/cannot exceed/));
const summary={checks,status:'passed',independentSelectionVectors:rows.length,scope:'Selected edges/arcs, partial lengths, nominal wall-region gap volume with overlap counted once, all/none, exact units and validation. Not a weld penetration or connection design.'};
fs.writeFileSync(path.join(__dirname,'selection-test-results.json'),JSON.stringify(summary,null,2)+'\n');console.log(JSON.stringify(summary,null,2));
