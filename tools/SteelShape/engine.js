(function (root) {
  'use strict';
  const tables = typeof module !== 'undefined' && module.exports ? require('./catalogues.js') : root.SteelCatalogues;
  const rhs = typeof module !== 'undefined' && module.exports ? require('./rhs-core.js') : root.RhsWeight;
  const PI = '3.141592653589793';
  const field = (key, label, unit, value) => ({ key, label, unit, value });
  const shapes = [
    { id: 'plate', title: 'Steel sheets and plates', kind: 'plate', fields: [field('t', 'Thickness t', 'mm', '10'), field('W', 'Width W', 'mm', '1000'), field('L', 'Length L', 'mm', '2000'), field('n', 'Number of pieces', 'pieces', '1')] },
    { id: 'seamless', title: 'Seamless steel pipes — circular', kind: 'tube', fields: [field('D', 'Outside diameter D', 'mm', '60.3'), field('t', 'Wall thickness t', 'mm', '3.2'), field('L', 'Length L', 'm', '6')] },
    { id: 'chs', title: 'Hollow structural sections — circular', kind: 'tube', fields: [field('D', 'Outside diameter D', 'mm', '114.3'), field('t', 'Wall thickness t', 'mm', '4'), field('L', 'Length L', 'm', '6')] },
    { id: 'shs', title: 'Hollow structural sections — square', kind: 'hollow', fields: [field('A', 'Outside side A', 'mm', '100'), field('t', 'Wall thickness t', 'mm', '4'), field('L', 'Length L', 'm', '6')] },
    { id: 'rhs', title: 'Hollow structural sections — rectangular', kind: 'hollow', fields: [field('A', 'Outside height A', 'mm', '100'), field('B', 'Outside width B', 'mm', '50'), field('t', 'Wall thickness t', 'mm', '3'), field('L', 'Length L', 'm', '6')] },
    { id: 'round_bar', title: 'Round steel bars', kind: 'round', fields: [field('D', 'Diameter D', 'mm', '50'), field('L', 'Length L', 'm', '6')] },
    { id: 'square_bar', title: 'Square steel bars', kind: 'solid', fields: [field('A', 'Side A', 'mm', '20'), field('L', 'Length L', 'm', '6')] },
    { id: 'flat_bar', title: 'Flat bars', kind: 'solid', fields: [field('W', 'Width W', 'mm', '100'), field('t', 'Thickness t', 'mm', '10'), field('L', 'Length L', 'm', '6')] },
    { id: 'equal_angle', title: 'Equal angles', kind: 'angle', family: 'equal_angle' },
    { id: 'unequal_angle', title: 'Unequal angles', kind: 'angle', family: 'unequal_angle' },
    { id: 'gost', title: 'Channels — GOST', kind: 'channel', family: 'gost', tapered: true },
    { id: 'upn', title: 'Channels — UPN', kind: 'channel', family: 'upn', tapered: true },
    { id: 'ipn', title: 'Beams — IPN', kind: 'beam', family: 'ipn', tapered: true },
    { id: 'ipe', title: 'Beams — IPE', kind: 'beam', family: 'ipe' },
    { id: 'hea', title: 'Beams — HEA (IPBL)', kind: 'beam', family: 'hea' },
    { id: 'heb', title: 'Beams — HEB (IPB)', kind: 'beam', family: 'heb' }
  ];
  function gcd(a, b) { a = a < 0n ? -a : a; while (b) { const r = a % b; a = b; b = r; } return a || 1n; }
  function fraction(n, d = 1n) { if (d <= 0n) throw new Error('Invalid decimal denominator.'); const g = gcd(n, d); return { n: n / g, d: d / g }; }
  const mul = (a, b) => fraction(a.n * b.n, a.d * b.d);
  const add = (a, b) => fraction(a.n * b.d + b.n * a.d, a.d * b.d);
  const sub = (a, b) => fraction(a.n * b.d - b.n * a.d, a.d * b.d);
  const div = (a, b) => fraction(a.n * b.d, a.d * b.n);
  function finiteDecimal(value, places = 24) {
    const scale = 10n ** BigInt(places), scaled = value.n * scale;
    let rounded = scaled / value.d; if ((scaled % value.d) * 2n >= value.d) rounded++;
    return fraction(rounded, scale);
  }
  function decimal(value, label = 'Value', allowZero = false) {
    const s = String(value == null ? '' : value).trim().replace(',', '.');
    if (!/^[+]?(?:\d+(?:\.\d*)?|\.\d+)$/.test(s)) throw new Error(label + ': enter a positive decimal number without thousands separators.');
    const parts = s.replace(/^\+/, '').split('.'), number = fraction(BigInt((parts[0] || '0') + (parts[1] || '')), 10n ** BigInt((parts[1] || '').length));
    if (number.n < 0n || (!allowZero && number.n === 0n)) throw new Error(label + (allowZero ? ' must be zero or greater.' : ' must be greater than zero.'));
    return number;
  }
  function exact(number) {
    let denominator = number.d, twos = 0, fives = 0;
    while (denominator % 2n === 0n) { denominator /= 2n; twos++; }
    while (denominator % 5n === 0n) { denominator /= 5n; fives++; }
    if (denominator !== 1n) return number.n + '/' + number.d;
    const places = Math.max(twos, fives), scale = 10n ** BigInt(places);
    let s = (number.n * scale / number.d).toString().padStart(places + 1, '0');
    if (places) s = (s.slice(0, -places) + '.' + s.slice(-places)).replace(/0+$/, '').replace(/\.$/, '');
    return s;
  }
  function shape(id) { const item = shapes.find(s => s.id === id); if (!item) throw new Error('Unknown steel shape.'); return item; }
  function profiles(id) { const definition = shape(id); return definition.family ? tables[definition.family].rows : []; }
  function defaults(id) {
    const definition = shape(id);
    if (!definition.family) return Object.fromEntries(definition.fields.map(f => [f.key, f.value]));
    if (definition.kind === 'angle') return definition.id === 'equal_angle' ? { A: '50', t: '5', L: '6' } : { pair: '100x75', t: '8', L: '6' };
    const rows = profiles(id), selected = rows.find(p => p.id === (id === 'gost' ? 'No. 20' : id.toUpperCase() + ' 200')) || rows[0];
    return { profile: selected.id, L: '6' };
  }
  function profile(id, input) {
    const definition = shape(id), rows = profiles(id);
    let selected;
    if (definition.kind === 'angle') {
      const t = exact(decimal(input.t, 'Thickness t'));
      if (id === 'equal_angle') { const a = exact(decimal(input.A, 'Side A')); selected = rows.find(r => r.A === a && r.t === t); }
      else selected = rows.find(r => r.A + 'x' + r.B === input.pair && r.t === t);
    } else selected = rows.find(r => r.id === input.profile);
    if (!selected) throw new Error('Select a listed profile and thickness. An unavailable size cannot be interpolated.');
    return selected;
  }
  function calculateSteel(id, input) {
    const definition = shape(id), length = decimal(input.L, 'Length L');
    let area, mass, single, dimensions, selected = null;
    if (definition.family) {
      selected = profile(id, input); mass = decimal(selected.kgPerM); dimensions = { ...selected, L: exact(length) };
      return { shape: id, kind: definition.kind, input: { ...input, L: exact(length) }, dimensions, kgPerM: exact(mass), unitMassKg: exact(mass), totalKg: exact(mul(mass, length)), metric: 'metre', profileId: selected.id,
        basis: 'Published supplier catalogue mass per metre, multiplied by unrounded entered length.', sourceUrl: tables[id].sourceUrl };
    }
    const values = {};
    for (const f of definition.fields) values[f.key] = decimal(input[f.key], f.label);
    const normalized = Object.fromEntries(Object.entries(values).map(([k, v]) => [k, exact(v)]));
    if (id === 'rhs' || id === 'shs') {
      const result = rhs.calculate({ A: normalized.A, B: id === 'shs' ? normalized.A : normalized.B, t: normalized.t, L: normalized.L });
      return { shape: id, kind: definition.kind, input: normalized, dimensions: { ...normalized, B: id === 'shs' ? normalized.A : normalized.B }, kgPerM: result.kgPerM, unitMassKg: result.kgPerM, totalKg: result.totalKg, areaMm2: result.areaMm2, metric: 'metre', basis: result.basis };
    }
    if (id === 'plate') {
      if (values.n.d !== 1n) throw new Error('Number of pieces must be a whole positive number.');
      area = mul(values.W, values.t); single = mul(mul(area, length), fraction(7850n, 1000000000n));
      return { shape: id, kind: definition.kind, input: normalized, dimensions: normalized, unitMassKg: exact(single), singlePieceKg: exact(single), totalKg: exact(mul(single, values.n)), areaMm2: exact(area), metric: 'piece', basis: 'Volume in cubic millimetres × 7850/1,000,000,000 kg/mm³.' };
    }
    if (definition.kind === 'tube') {
      if (values.t.n * 2n * values.D.d >= values.D.n * values.t.d) throw new Error('Twice the wall thickness must be less than the outside diameter.');
      area = mul(decimal(PI), mul(values.t, sub(values.D, values.t)));
    } else if (id === 'round_bar') area = mul(fraction(1n, 4n), mul(decimal(PI), mul(values.D, values.D)));
    else if (id === 'square_bar') area = mul(values.A, values.A);
    else if (id === 'flat_bar') area = mul(values.W, values.t);
    else throw new Error('No formula for the selected steel shape.');
    mass = mul(area, fraction(7850n, 1000000n));
    return { shape: id, kind: definition.kind, input: normalized, dimensions: normalized, kgPerM: exact(mass), unitMassKg: exact(mass), totalKg: exact(mul(mass, length)), areaMm2: exact(area), metric: 'metre', basis: 'Nominal section area × 0.00785 kg/m per mm², unrounded mass × length; π = ' + PI + '.' };
  }
  function splitLength(value) { return exact(mul(decimal(value, 'Length L'), fraction(1n, 2n))); }
  function combinedLength(first, second) { return exact(add(decimal(first, 'First piece length'), decimal(second, 'Second piece length'))); }
  function convertLength(value, from, to) {
    if (!['mm', 'm'].includes(from) || !['mm', 'm'].includes(to)) throw new Error('Length unit must be mm or m.');
    return exact(mul(decimal(value, 'Material length'), from === to ? fraction(1n) : from === 'm' ? fraction(1000n) : fraction(1n, 1000n)));
  }
  function weldSteelLength(id, weld) { return convertLength(combinedLength(weld.L1, weld.L2), weld.lengthUnit || 'mm', id === 'plate' ? 'mm' : 'm'); }
  function weldSegments(definition, d) {
    const v = key => decimal(d[key]), n = key => Number(d[key]), two = fraction(2n), pi = decimal(PI), zero = fraction(0n);
    const segments = [], weights = [];
    let width, height, radius = null;
    function line(name, from, to, length) { segments.push({ id:'edge-' + segments.length, name, kind:'line', from, to, lengthMm:exact(length) }); weights.push(zero); }
    function arc(name, center, r, start, end, length) { segments.push({ id:'edge-' + segments.length, name, kind:'arc', center, radius:r, start, end, lengthMm:exact(length) }); weights.push(zero); }
    function polygon(points, names, lengths) { points.forEach((point, i) => line(names[i], point, points[(i+1)%points.length], lengths[i])); }
    if (definition.kind === 'tube' || definition.kind === 'round') {
      width = height = n('D'); const quarter = mul(mul(pi, v('D')), fraction(1n,4n));
      ['Upper-right arc','Lower-right arc','Lower-left arc','Upper-left arc'].forEach((name, i) => { arc(name, [width/2,height/2], width/2, -90+i*90, i*90, quarter); weights[i] = fraction(1n); });
    } else if (definition.kind === 'hollow') {
      width=n('B'); height=n('A'); const a=v('A'), b=v('B'), t=v('t');
      let r=mul(t,fraction(3n,2n)); const smaller=a.n*b.d<=b.n*a.d?a:b, limit=mul(smaller,fraction(1n,2n));
      if(r.n*limit.d>limit.n*r.d) r=limit; radius=Number(exact(r)); const rr=radius;
      const horizontal=sub(b,mul(two,r)), vertical=sub(a,mul(two,r)), curve=mul(mul(pi,r),fraction(1n,2n));
      line('Top edge',[rr,0],[width-rr,0],horizontal); arc('Upper-right corner',[width-rr,rr],rr,-90,0,curve);
      line('Right edge',[width,rr],[width,height-rr],vertical); arc('Lower-right corner',[width-rr,height-rr],rr,0,90,curve);
      line('Bottom edge',[width-rr,height],[rr,height],horizontal); arc('Lower-left corner',[rr,height-rr],rr,90,180,curve);
      line('Left edge',[0,height-rr],[0,rr],vertical); arc('Upper-left corner',[rr,rr],rr,180,270,curve);
      const inner=r.n*t.d>t.n*r.d?sub(r,t):zero;
      const cornerArea=mul(mul(pi,sub(mul(r,r),mul(inner,inner))),fraction(1n,4n));
      segments.forEach((segment,i)=> { weights[i]=segment.kind==='arc'?cornerArea:mul(t,decimal(segment.lengthMm,'Edge length',true)); });
    } else if (definition.kind === 'beam' || definition.kind === 'channel') {
      width=n('b'); height=n('h'); const b=v('b'), h=v('h'), s=v('s'), t=v('t'), middle=sub(h,mul(two,t));
      const nnS=n('s'), nnT=n('t');
      if(definition.kind==='beam') {
        const left=(width-nnS)/2,right=(width+nnS)/2,arm=mul(sub(b,s),fraction(1n,2n));
        polygon([[0,0],[width,0],[width,nnT],[right,nnT],[right,height-nnT],[width,height-nnT],[width,height],[0,height],[0,height-nnT],[left,height-nnT],[left,nnT],[0,nnT]],
          ['Top flange — outside','Top flange — right tip','Top flange — lower right','Web — right','Bottom flange — upper right','Bottom flange — right tip','Bottom flange — outside','Bottom flange — left tip','Bottom flange — upper left','Web — left','Top flange — lower left','Top flange — left tip'],
          [b,t,arm,middle,arm,t,b,t,arm,middle,arm,t]);
      } else {
        const arm=sub(b,s);
        polygon([[0,0],[width,0],[width,nnT],[nnS,nnT],[nnS,height-nnT],[width,height-nnT],[width,height],[0,height],[0,height-nnT],[0,nnT]],
          ['Top flange — outside','Top flange — tip','Top flange — inside','Web — inside','Bottom flange — inside','Bottom flange — tip','Bottom flange — outside','Back — bottom flange','Web — back','Back — top flange'],
          [b,t,arm,middle,arm,t,b,t,middle,t]);
      }
    } else if(definition.kind==='angle') {
      width=n('B'); height=n('A'); const a=v('A'),b=v('B'),t=v('t'),nnT=n('t');
      polygon([[0,0],[nnT,0],[nnT,height-nnT],[width,height-nnT],[width,height],[0,height]],
        ['Vertical leg — tip','Vertical leg — inside','Horizontal leg — inside','Horizontal leg — tip','Horizontal leg — outside','Vertical leg — outside'],[t,sub(a,t),sub(b,t),t,b,a]);
    } else {
      const w=definition.id==='square_bar'?v('A'):v('W'),h=definition.id==='square_bar'?v('A'):v('t'); width=Number(exact(w));height=Number(exact(h));
      polygon([[0,0],[width,0],[width,height],[0,height]],['Top edge','Right edge','Bottom edge','Left edge'],[w,h,w,h]);
    }
    segments.forEach((segment,i)=>{ segment.weightNumerator=weights[i].n.toString(); segment.weightDenominator=weights[i].d.toString(); });
    let materialRectangles=null;
    if(definition.kind==='beam'||definition.kind==='channel') {
      const t=n('t'),s=n('s'),left=definition.kind==='beam'?(width-s)/2:0;
      materialRectangles=[[0,0,width,t],[0,height-t,width,height],[left,t,left+s,height-t]];
      segments.forEach((edge,i)=>{ edge.depthMm=(definition.kind==='beam'?[3,9]:[3,8]).includes(i)?d.s:d.t; });
    } else if(definition.kind==='angle') {
      const t=n('t'); materialRectangles=[[0,0,t,height],[t,height-t,width,height]];segments.forEach(edge=>{edge.depthMm=d.t;});
    } else if(definition.kind!=='tube'&&definition.kind!=='round'&&definition.kind!=='hollow') {
      materialRectangles=[[0,0,width,height]];segments.forEach(edge=>{edge.depthMm=String(Math.min(width,height));});
    }
    return { segments, modelWidth:width, modelHeight:height, radius, materialRectangles };
  }
  function selectedMaterialShare(path, segments) {
    const zero=fraction(0n),half=fraction(1n,2n),cmp=(a,b)=>a.n*b.d-b.n*a.d;
    const lo=(a,b)=>cmp(a,b)<=0?a:b,hi=(a,b)=>cmp(a,b)>=0?a:b;
    const coordinate=value=>decimal(Number(value).toFixed(12),'Nominal coordinate',true);
    const material=path.materialRectangles.map(rect=>rect.map(coordinate));
    const pieces=[];
    for(const edge of segments) {
      const amount=decimal(edge.weldedLengthMm,'Selected edge length',true);if(!amount.n)continue;
      const from=edge.from.map(coordinate),to=edge.to.map(coordinate),depth=decimal(edge.depthMm),x=mul(add(from[0],to[0]),half),y=mul(add(from[1],to[1]),half),extent=mul(amount,half);
      let strip;
      if(edge.from[1]===edge.to[1]) strip=edge.to[0]>edge.from[0]?[sub(x,extent),y,add(x,extent),add(y,depth)]:[sub(x,extent),sub(y,depth),add(x,extent),y];
      else strip=edge.to[1]>edge.from[1]?[sub(x,depth),sub(y,extent),x,add(y,extent)]:[x,sub(y,extent),add(x,depth),add(y,extent)];
      for(const rect of material) { const clipped=[hi(strip[0],rect[0]),hi(strip[1],rect[1]),lo(strip[2],rect[2]),lo(strip[3],rect[3])];if(cmp(clipped[2],clipped[0])>0&&cmp(clipped[3],clipped[1])>0)pieces.push(clipped); }
    }
    const xs=Array.from(new Map(pieces.flatMap(rect=>[rect[0],rect[2]]).map(x=>[x.n+'/'+x.d,x])).values()).sort((a,b)=>cmp(a,b)<0?-1:cmp(a,b)>0?1:0);
    let area=zero;
    for(let i=1;i<xs.length;i++) {
      const bands=pieces.filter(rect=>cmp(rect[0],xs[i-1])<=0&&cmp(rect[2],xs[i])>=0).map(rect=>[rect[1],rect[3]]).sort((a,b)=>cmp(a[0],b[0])<0?-1:cmp(a[0],b[0])>0?1:0);
      let start=null,end=null,height=zero;
      for(const band of bands) { if(start===null){start=band[0];end=band[1];}else if(cmp(band[0],end)<=0){end=hi(end,band[1]);}else{height=add(height,sub(end,start));start=band[0];end=band[1];} }
      if(start!==null)height=add(height,sub(end,start));area=add(area,mul(sub(xs[i],xs[i-1]),height));
    }
    const full=material.reduce((sum,rect)=>add(sum,mul(sub(rect[2],rect[0]),sub(rect[3],rect[1]))),zero);
    return full.n?div(area,full):zero;
  }
  function selectedWeldPath(path, overrides) {
    if(overrides != null && (typeof overrides!=='object'||Array.isArray(overrides))) throw new Error('Weld edge selections are invalid.');
    const choices=overrides||{},known=new Set(path.segments.map(edge=>edge.id));
    for(const id of Object.keys(choices)) if(!known.has(id)) throw new Error('A selected weld edge is unavailable for this section.');
    let length=fraction(0n),selectedWeight=fraction(0n),totalWeight=fraction(0n);
    const segments=path.segments.map(edge=>{
      const max=decimal(edge.lengthMm,'Available edge length',true), raw=Object.prototype.hasOwnProperty.call(choices,edge.id)?choices[edge.id]:'full';
      const amount=raw==='full'?max:decimal(raw,edge.name+' weld length',true);
      if(amount.n*max.d>max.n*amount.d) throw new Error(edge.name+': weld length cannot exceed '+edge.lengthMm+' mm.');
      const weight=fraction(BigInt(edge.weightNumerator),BigInt(edge.weightDenominator)); totalWeight=add(totalWeight,weight);
      if(max.n) selectedWeight=add(selectedWeight,mul(weight,div(amount,max)));
      length=add(length,amount); return {...edge,weldedLengthMm:exact(amount),full:raw==='full'};
    });
    const share=path.materialRectangles?selectedMaterialShare(path,segments):totalWeight.n?div(selectedWeight,totalWeight):fraction(0n);
    return {...path,segments,outlineLengthMm:path.lengthMm,lengthMm:exact(length),gapShareNumerator:share.n.toString(),gapShareDenominator:share.d.toString()};
  }
  function automaticWeldPath(id, input) {
    const definition = shape(id), steel = calculateSteel(id, { ...input, L: '1', n: '1' }), d = steel.dimensions;
    const two = fraction(2n), pi = decimal(PI), value = key => decimal(d[key]);
    let length, basis;
    if (definition.kind === 'tube' || definition.kind === 'round') {
      length = mul(pi, value('D')); basis = 'Outside circumference πD.';
    } else if (definition.kind === 'hollow') {
      let radius = mul(value('t'), fraction(3n, 2n));
      const sideA = value('A'), sideB = value('B');
      const limit = mul(sideA.n * sideB.d <= sideB.n * sideA.d ? sideA : sideB, fraction(1n, 2n));
      if (radius.n * limit.d > limit.n * radius.d) radius = limit;
      length = sub(mul(two, add(value('A'), value('B'))), mul(sub(fraction(8n), mul(two, pi)), radius));
      basis = 'Rounded outside perimeter; radius min(1.5t, half the smaller side).';
    } else if (definition.kind === 'beam' || definition.kind === 'channel') {
      length = sub(add(mul(two, value('h')), mul(fraction(4n), value('b'))), mul(two, value('s')));
      basis = 'Nominal complete section outline 2h + 4b − 2s; root radii and flange taper are not included.';
    } else if (definition.kind === 'angle') {
      length = mul(two, add(value('A'), value('B'))); basis = 'Nominal complete angle outline 2(A+B), including the inside corner.';
    } else if (id === 'square_bar') {
      length = mul(fraction(4n), value('A')); basis = 'Square outside perimeter 4A.';
    } else {
      length = mul(two, add(value('W'), value('t'))); basis = 'Rectangular outside perimeter 2(W+t).';
    }
    const model=weldSegments(definition,d);
    return { lengthMm: exact(length), dimensions: d, coverage: 'Selected edges of the nominal section outline', basis, ...model };
  }
  function calculate(id, input) {
    if (!input.weld || input.weld.enabled !== true) return calculateSteel(id, input);
    const weld = input.weld;
    const lengthUnit = weld.lengthUnit || 'mm', connection = weld.connection || 'straight';
    if (!['straight', 'L'].includes(connection)) throw new Error('Choose a straight or L connection.');
    const length = weldSteelLength(id, weld);
    const result = calculateSteel(id, { ...input, L: length });
    const type = weld.type || 'fillet';
    if (!['fillet', 'butt'].includes(type)) throw new Error('Choose a fillet or butt weld.');
    const automatic = weld.lengthMode !== 'manual', path = automatic ? selectedWeldPath(automaticWeldPath(id, input),weld.edges) : null;
    const runLength = decimal(automatic ? path.lengthMm : weld.lengthMm, 'Weld length along the joint', automatic), runs = automatic ? fraction(1n) : decimal(weld.runs, 'Number of identical weld seams');
    if(automatic&&!runLength.n) return {...result,steelTotalKg:result.totalKg,input:{...result.input,weld:{...weld,type,connection,lengthUnit,lengthMode:'section-outline',lengthMm:'0'}},weld:{type,connection,lengthUnit,automaticLength:true,lengthPerRunMm:'0',runs:'1',totalKg:'0',perJointKg:'0',gapMassPerJointKg:'0',addedAreaMm2:'0',finishedLength:connection==='straight'?length:null,basis:'No welded edges selected.'}};
    if (runs.d !== 1n) throw new Error('Number of identical weld seams must be a whole positive number.');
    let area, parameters, finishedLength = connection === 'straight' ? length : null, basis;
    if (type === 'fillet') {
      const z = decimal(weld.z, 'Fillet leg size');
      area = mul(mul(z, z), fraction(1n, 2n)); parameters = { z: exact(z) };
      basis = 'Equal-leg fillet area z²/2. Extra weld metal is added to the nominal steel volume.';
    } else {
      const thickness = automatic ? null : decimal(weld.joinedThicknessMm, 'Material thickness at joint'), gap = decimal(weld.gapMm, 'Gap between pieces', true);
      const width = decimal(weld.capWidthMm, 'Weld cap width'), height = decimal(weld.capHeightMm, 'Weld cap height', true), faces = decimal(weld.faces, 'Reinforced faces');
      if (faces.d !== 1n || faces.n > 2n) throw new Error('Reinforced faces must be 1 or 2.');
      if (height.n > 0n && width.n * gap.d < gap.n * width.d) throw new Error('Weld cap width must cover the gap between pieces.');
      const caps = mul(mul(mul(width, height), faces), fraction(1n, 2n));
      area = automatic ? caps : add(mul(thickness, gap), caps);
      if (connection === 'straight') finishedLength = exact(add(decimal(length), mul(gap, id === 'plate' ? fraction(1n) : fraction(1n, 1000n))));
      parameters = { ...(thickness ? { joinedThicknessMm: exact(thickness) } : {}), gapMm: exact(gap), capWidthMm: exact(width), capHeightMm: exact(height), faces: exact(faces) };
      basis = automatic ? 'Gap mass follows the selected nominal wall regions; overlapping regions are counted once. Reinforcement is modelled as triangular caps around the nominal outside outline. Bevel fill replaces removed steel of the same density. No backing or splice plates are included.' : 'Net added butt-weld area g × d + faces × b × h/2. The nominal steel volume already includes the joined section; bevel fill replaces removed steel of the same density. Triangular reinforcement is additional. No backing or splice plates are included.';
    }
    let perJoint = mul(mul(mul(area, runLength), runs), fraction(7850n, 1000000000n));
    let gapKg = fraction(0n);
    if (automatic && type === 'butt') {
      const gap = decimal(weld.gapMm, 'Gap between pieces', true);
      gapKg = id === 'plate' ? mul(mul(decimal(result.areaMm2), gap), fraction(7850n, 1000000000n)) : mul(mul(decimal(result.kgPerM), gap), fraction(1n, 1000n));
      const share=fraction(BigInt(path.gapShareNumerator),BigInt(path.gapShareDenominator));
      if(share.n!==share.d) gapKg=finiteDecimal(mul(gapKg,share));
      perJoint = add(perJoint, gapKg);
    }
    const assemblies = id === 'plate' ? decimal(input.n, 'Number of pieces') : fraction(1n);
    const weldTotal = mul(perJoint, assemblies), total = add(decimal(result.totalKg), weldTotal);
    return { ...result, input: { ...result.input, weld: { ...weld, ...parameters, type, connection, lengthUnit, lengthMode: automatic ? 'section-outline' : 'manual', L1: exact(decimal(weld.L1)), L2: exact(decimal(weld.L2)), lengthMm: exact(runLength), runs: exact(runs) } },
      steelTotalKg: result.totalKg, totalKg: exact(total), weld: {
        type, connection, lengthUnit, automaticLength: automatic, pathBasis: path ? path.basis : null, ...parameters, addedAreaMm2: exact(area), lengthPerRunMm: exact(runLength), runs: exact(runs), finishedLength, gapMassPerJointKg: exact(gapKg),
        perJointKg: exact(perJoint), assemblies: exact(assemblies), totalKg: exact(weldTotal), densityKgM3: '7850',
        basis: basis + (automatic && type === 'butt' ? ' Cap area × automatic outline length × 7850/1,000,000,000 kg/mm³, plus nominal section gap mass; one joint per assembly.' : ' Added area × weld length × 7850/1,000,000,000 kg/mm³; one joint per assembly.')
      } };
  }
  const api = { shapes, shape, defaults, profiles, profile, calculate, automaticWeldPath, selectedWeldPath, splitLength, combinedLength, convertLength, weldSteelLength, format: rhs.format };
  if (typeof module !== 'undefined' && module.exports) module.exports = api; else root.SteelWeight = api;
})(typeof window === 'undefined' ? globalThis : window);
