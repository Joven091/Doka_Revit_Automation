(function (root) {
  'use strict';
  const esc = value => String(value).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
  const number = (value, fallback = 1) => { const n = Number(value); return Number.isFinite(n) && n > 0 ? n : fallback; };
  const short = value => String(value).length > 13 ? (Number.isFinite(Number(value)) ? Number(value).toExponential(3) : String(value).slice(0, 7) + '…') : String(value);
  const path = points => 'M' + points.map(p => p.join(' ')).join('L') + 'Z';
  function rect(x, y, w, h, r = 0) {
    r = Math.min(r, w / 2, h / 2);
    return `M${x + r} ${y}H${x + w - r}Q${x + w} ${y} ${x + w} ${y + r}V${y + h - r}Q${x + w} ${y + h} ${x + w - r} ${y + h}H${x + r}Q${x} ${y + h} ${x} ${y + h - r}V${y + r}Q${x} ${y} ${x + r} ${y}Z`;
  }
  const defs = prefix => `<defs><linearGradient id="${prefix}-steel" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#E6E8EA"/><stop offset="1" stop-color="#ADB5BD"/></linearGradient><marker id="${prefix}-start" viewBox="0 0 6 6" refX="3" refY="3" markerWidth="5" markerHeight="5" orient="auto"><path d="M6 0L0 3L6 6Z" fill="#586772"/></marker><marker id="${prefix}-end" viewBox="0 0 6 6" refX="3" refY="3" markerWidth="5" markerHeight="5" orient="auto"><path d="M0 0L6 3L0 6Z" fill="#586772"/></marker></defs>`;
  function label(prefix, key, value, unit, x, y, rotate = null) {
    return `<text data-label="${esc(key)}" x="${x}" y="${y}" text-anchor="middle"${rotate ? ` transform="rotate(${rotate} ${x} ${y})"` : ''}>${esc(key)} = ${esc(short(value))} ${unit}</text>`;
  }
  function horizontal(prefix, key, value, unit, left, right, edge, y) {
    return `<g class="dimension" data-dimension="${esc(key)}"><path class="extension" d="M${left} ${edge}V${y + 10}M${right} ${edge}V${y + 10}"/><line x1="${left}" x2="${right}" y1="${y}" y2="${y}" marker-start="url(#${prefix}-start)" marker-end="url(#${prefix}-end)"/>${label(prefix, key, value, unit, (left + right) / 2, y + 25)}</g>`;
  }
  function vertical(prefix, key, value, left, top, bottom) {
    return `<g class="dimension" data-dimension="${esc(key)}"><path class="extension" d="M${left + 29} ${top}H${left - 10}M${left + 29} ${bottom}H${left - 10}"/><line x1="${left}" x2="${left}" y1="${top}" y2="${bottom}" marker-start="url(#${prefix}-start)" marker-end="url(#${prefix}-end)"/>${label(prefix, key, value, 'mm', left - 15, (top + bottom) / 2, -90)}</g>`;
  }
  function leader(prefix, key, value, x, y, lineY = 58) {
    return `<g class="dimension" data-dimension="${esc(key)}"><circle cx="${x}" cy="${y}" r="2.8" fill="currentColor"/><path d="M${x} ${y}L278 ${lineY}H329"/><rect class="label-back" x="251" y="${lineY - 27}" width="104" height="23"/>${label(prefix, key, value, 'mm', 300, lineY - 9)}</g>`;
  }
  function geometry(definition, dimensions, maxWidth = 154, maxHeight = 160) {
    const circular = definition.kind === 'tube' || definition.kind === 'round';
    const profiled = definition.kind === 'beam' || definition.kind === 'channel';
    let a, b, vKey, hKey;
    if (circular) { a = b = number(dimensions.D); vKey = null; hKey = 'D'; }
    else if (profiled) { a = number(dimensions.h); b = number(dimensions.b); vKey = 'h'; hKey = 'b'; }
    else if (definition.kind === 'angle') { a = number(dimensions.A); b = number(dimensions.B); vKey = 'A'; hKey = definition.id === 'equal_angle' ? 'A' : 'B'; }
    else if (definition.kind === 'hollow') { a = number(dimensions.A); b = number(dimensions.B); vKey = 'A'; hKey = definition.id === 'shs' ? 'A' : 'B'; }
    else if (definition.id === 'square_bar') { a = b = number(dimensions.A); vKey = hKey = 'A'; }
    else { a = number(dimensions.t); b = number(dimensions.W); vKey = 't'; hKey = 'W'; }
    let w, h;
    if (circular) w = h = Math.min(maxWidth, maxHeight);
    else if (definition.id === 'flat_bar' || definition.id === 'plate') { w = maxWidth; h = Math.max(9, Math.min(maxHeight * .5, a / b * w)); }
    else { const ratio = Math.max(.28, Math.min(3.5, b / a || 1)); h = Math.min(maxHeight, maxWidth / ratio); w = h * ratio; }
    const scale = Math.min(w / b, h / a), rawT = number(dimensions.t, 1), rawS = number(dimensions.s, 1);
    const t = Math.max(2.4, Math.min(Math.min(w, h) * .4, rawT * scale));
    const s = Math.max(2.5, Math.min(w * .35, rawS * scale));
    const polygon = [[0, 0], [w, 0], [w, h], [0, h]];
    if (definition.kind === 'angle') polygon.splice(0, polygon.length, [0, 0], [t, 0], [t, h - t], [w, h - t], [w, h], [0, h]);
    if (definition.kind === 'channel') {
      const tip = definition.tapered ? .65 * t : t, base = definition.tapered ? 1.35 * t : t;
      polygon.splice(0, polygon.length, [0, 0], [w, 0], [w, tip], [s, base], [s, h - base], [w, h - tip], [w, h], [0, h]);
    }
    if (definition.kind === 'beam') {
      const l = (w - s) / 2, r = (w + s) / 2, tip = definition.tapered ? .65 * t : t, base = definition.tapered ? 1.35 * t : t;
      polygon.splice(0, polygon.length, [0, 0], [w, 0], [w, tip], [r, base], [r, h - base], [w, h - tip], [w, h], [0, h], [0, h - tip], [l, h - base], [l, base], [0, tip]);
    }
    return { circular, w, h, t, s, polygon, vKey, hKey };
  }
  function front(definition, g, x, y, prefix, ellipse = false) {
    const stroke = 'stroke="#586772" stroke-width="1.6" stroke-linejoin="round"';
    if (g.circular) {
      const rx = ellipse ? g.w * .30 : g.w / 2, ry = g.h / 2;
      let body = `<ellipse cx="${x + g.w / 2}" cy="${y + ry}" rx="${rx}" ry="${ry}" fill="url(#${prefix}-steel)" ${stroke}/>`;
      if (definition.kind === 'tube') body += `<ellipse cx="${x + g.w / 2}" cy="${y + ry}" rx="${Math.max(1, rx - g.t * (ellipse ? .6 : 1))}" ry="${Math.max(1, ry - g.t)}" fill="#F5F5F5" ${stroke}/>`;
      return body;
    }
    let body;
    if (definition.kind === 'hollow') {
      body = `<path d="${rect(x, y, g.w, g.h, g.t * 1.5)}" fill="url(#${prefix}-steel)" ${stroke}/><path d="${rect(x + g.t, y + g.t, g.w - 2 * g.t, g.h - 2 * g.t, g.t * .5)}" fill="#F5F5F5" ${stroke}/>`;
    } else body = `<path d="${path(g.polygon.map(p => [p[0] + x, p[1] + y]))}" fill="url(#${prefix}-steel)" ${stroke}/>`;
    return body;
  }
  function section(definition, result) {
    const d = result.dimensions, g = geometry(definition, d), x = 174 - g.w / 2, y = 132 - g.h / 2, prefix = 'cross';
    const description = `${definition.title}. ${g.vKey ? g.vKey + ' ' + d[g.vKey] + ' mm. ' : ''}${g.hKey} ${d[g.hKey]} mm.${d.t ? ' t ' + d.t + ' mm.' : ''}${d.s ? ' s ' + d.s + ' mm.' : ''}`;
    let drawing = front(definition, g, x, y, prefix);
    if (g.vKey) drawing += vertical(prefix, g.vKey, d[g.vKey], x - 29, y, y + g.h);
    drawing += horizontal(prefix, g.hKey, d[g.hKey], 'mm', x, x + g.w, y + g.h, y + g.h + 26);
    if (definition.kind === 'tube' || definition.kind === 'hollow') drawing += leader(prefix, 't', d.t, x + g.w - g.t / 2, y + g.h * .37);
    if (definition.kind === 'angle') drawing += leader(prefix, 't', d.t, x + g.w * .65, y + g.h - g.t / 2);
    if (definition.kind === 'beam' || definition.kind === 'channel') {
      drawing += leader(prefix, 't', d.t, x + g.w * .78, y + g.t * .46);
      drawing += leader(prefix, 's', d.s, x + (definition.kind === 'beam' ? g.w / 2 : g.s / 2), y + g.h * .57, 186);
    }
    return `<svg id="section-schematic" viewBox="0 0 360 300" role="img" aria-label="${esc(description)}"><title>Cross-section</title>${defs(prefix)}${drawing}</svg>`;
  }
  function member(definition, result) {
    const d = result.dimensions, g = geometry(definition, d, 76, 102), x = 55, y = 150 - g.h / 2, dx = 169, dy = -54, prefix = 'member';
    let drawing = '';
    if (g.circular) {
      const cx = x + g.w / 2, rx = g.w * .30, ry = g.h / 2, cy = y + ry;
      drawing += `<path d="M${cx} ${cy - ry}L${cx + dx} ${cy + dy - ry}A${rx} ${ry} 0 0 1 ${cx + dx} ${cy + dy + ry}L${cx} ${cy + ry}Z" fill="#A8B0B7" stroke="#78858F" stroke-width="1.4"/>`;
    } else {
      drawing += `<path d="${path(g.polygon.map(p => [p[0] + x + dx, p[1] + y + dy]))}" fill="#DFE3E6" stroke="#78858F" stroke-width="1.2"/>`;
      for (let i = 0; i < g.polygon.length; i++) {
        const a = g.polygon[i], b = g.polygon[(i + 1) % g.polygon.length], ex = b[0] - a[0], ey = b[1] - a[1];
        if (ex * dy - ey * dx < 0) drawing += `<path d="${path([[a[0] + x, a[1] + y], [a[0] + x + dx, a[1] + y + dy], [b[0] + x + dx, b[1] + y + dy], [b[0] + x, b[1] + y]])}" fill="${Math.abs(ex) > Math.abs(ey) ? '#CCD1D5' : '#A8B0B7'}" stroke="#78858F" stroke-width="1.1"/>`;
      }
    }
    drawing += front(definition, g, x, y, prefix, g.circular);
    const lx = x + g.w, ly = y + g.h, unit = definition.id === 'plate' ? 'mm' : 'm';
    drawing += `<g class="dimension" data-dimension="L"><path class="extension" d="M${lx} ${ly}L${lx + 11} ${ly + 34}M${lx + dx} ${ly + dy}L${lx + dx + 11} ${ly + dy + 34}"/><line x1="${lx + 9}" y1="${ly + 27}" x2="${lx + dx + 9}" y2="${ly + dy + 27}" marker-start="url(#${prefix}-start)" marker-end="url(#${prefix}-end)"/><g transform="translate(${lx + dx / 2 + 13} ${ly + dy / 2 + 42}) rotate(-17.72)"><rect class="label-back" x="-72" y="-18" width="144" height="25" rx="3"/>${label(prefix, 'L', result.input.L, unit, 0, 0)}</g></g>`;
    return `<svg id="length-schematic" viewBox="0 0 380 300" role="img" aria-label="Member length L ${esc(result.input.L)} ${unit}. Schematic, not to scale."><title>Member length</title>${defs(prefix)}${drawing}</svg>`;
  }
  function placeholder(title) { return `<svg viewBox="0 0 360 300" role="img" aria-label="${esc(title)}"><text x="180" y="145" text-anchor="middle" class="placeholder">Enter valid dimensions</text></svg>`; }
  function weldCoverage(definition, dimensions) {
    const g = geometry(definition, dimensions, 98, 104), x = 75 - g.w / 2, y = 68 - g.h / 2, prefix = 'coverage';
    let drawing = front(definition, g, x, y, prefix);
    const stroke = 'fill="none" stroke="#D4AA00" stroke-width="4" stroke-linejoin="round"';
    if (g.circular) drawing += `<ellipse cx="${x + g.w / 2}" cy="${y + g.h / 2}" rx="${g.w / 2}" ry="${g.h / 2}" ${stroke}/>`;
    else if (definition.kind === 'hollow') drawing += `<path d="${rect(x, y, g.w, g.h, g.t * 1.5)}" ${stroke}/>`;
    else drawing += `<path d="${path(g.polygon.map(p => [p[0] + x, p[1] + y]))}" ${stroke}/>`;
    return `<svg id="weld-coverage-schematic" viewBox="0 0 150 142" role="img" aria-label="Yellow outline shows one continuous weld all around the section; internal hollow boundary is excluded."><title>Weld all around the section</title>${defs(prefix)}${drawing}<text x="75" y="137" text-anchor="middle" font-family="Segoe UI,Arial,sans-serif" font-size="12" fill="#806500">Weld all around</text></svg>`;
  }
  function weldSelector(definition, model, choices = {}, activeEdge = '') {
    const physicalW=number(model.modelWidth),physicalH=number(model.modelHeight), ratio=physicalW/physicalH;
    let h=Math.min(185,290/ratio),w=h*ratio; w=Math.max(85,w);h=Math.max(38,h);
    const sx=w/physicalW,sy=h/physicalH,ox=(440-w)/2,oy=(255-h)/2;
    const pt=p=>[ox+p[0]*sx,oy+p[1]*sy];
    const show=v=>Number.isFinite(Number(v))?Number(v).toFixed(2):'—';
    function command(edge,first=0,last=1,move=true) {
      let a,b,tail;
      if(edge.kind==='line') { a=pt(edge.from.map((v,i)=>v+(edge.to[i]-v)*first));b=pt(edge.from.map((v,i)=>v+(edge.to[i]-v)*last));tail='L'+b.join(' '); }
      else { const angles=[edge.start+(edge.end-edge.start)*first,edge.start+(edge.end-edge.start)*last].map(v=>v*Math.PI/180); const points=angles.map(v=>pt([edge.center[0]+edge.radius*Math.cos(v),edge.center[1]+edge.radius*Math.sin(v)]));a=points[0];b=points[1];tail=`A${edge.radius*sx} ${edge.radius*sy} 0 0 1 ${b.join(' ')}`; }
      return (move?'M'+a.join(' '):'')+tail;
    }
    const closed=model.segments.map((edge,i)=>command(edge,0,1,i===0)).join(' ')+'Z';
    let drawing=`<path d="${closed}" fill="#CCD1D5" stroke="#78858F" stroke-width="1"/>`;
    const t=number(model.dimensions.t),kind=definition.kind;
    if(kind==='tube') drawing+=`<ellipse cx="${ox+w/2}" cy="${oy+h/2}" rx="${Math.max(0,(physicalW/2-t)*sx)}" ry="${Math.max(0,(physicalH/2-t)*sy)}" fill="#F5F5F5" stroke="#ADB5BD"/>`;
    if(kind==='hollow') drawing+=`<rect x="${ox+t*sx}" y="${oy+t*sy}" width="${Math.max(0,(physicalW-2*t)*sx)}" height="${Math.max(0,(physicalH-2*t)*sy)}" rx="${Math.max(0,(model.radius-t)*sx)}" ry="${Math.max(0,(model.radius-t)*sy)}" fill="#F5F5F5" stroke="#ADB5BD"/>`;
    for(const edge of model.segments) {
      const raw=Object.prototype.hasOwnProperty.call(choices,edge.id)?choices[edge.id]:'full',max=Number(edge.lengthMm),entered=raw==='full'?max:Number(String(raw).replace(',','.'));
      const fraction=Number.isFinite(entered)&&max>0?Math.max(0,Math.min(1,entered/max)):0,invalid=!Number.isFinite(entered)||entered<0||entered>max;
      const selected=fraction>0,full=command(edge),edgeName=esc(edge.name),description=`${edgeName}: ${show(entered)} of ${show(max)} mm welded`;
      drawing+=`<g class="weld-edge" data-weld-edge="${edge.id}" role="button" tabindex="${max>0?'0':'-1'}" aria-pressed="${selected}" aria-disabled="${max<=0}" aria-label="${description}" data-partial="${selected&&fraction<1}" data-invalid="${invalid}"><title>${description}</title>`;
      if(activeEdge===edge.id) drawing+=`<path d="${full}" fill="none" stroke="#FFF2BF" stroke-width="11" vector-effect="non-scaling-stroke" pointer-events="none"/>`;
      drawing+=`<path class="weld-edge-base" d="${full}" fill="none" stroke="#78858F" stroke-width="3" vector-effect="non-scaling-stroke" pointer-events="none"/>`;
      if(selected) drawing+=`<path class="weld-edge-selected" d="${command(edge,(1-fraction)/2,(1+fraction)/2)}" fill="none" stroke="${invalid?'#B42318':'#D4AA00'}" stroke-width="5" vector-effect="non-scaling-stroke" pointer-events="none"/>`;
      if(max>0) drawing+=`<path d="${full}" fill="none" stroke="transparent" stroke-width="16" vector-effect="non-scaling-stroke" pointer-events="stroke"/>`;
      drawing+='</g>';
    }
    return `<svg id="weld-coverage-schematic" viewBox="0 0 440 280" role="group" aria-label="Click edges to select welds. Yellow means welded; grey means unwelded."><title>Interactive weld selection</title>${drawing}<text x="220" y="274" text-anchor="middle" font-family="Segoe UI,Arial,sans-serif" font-size="14" fill="#586772">Yellow: welded · Grey: unwelded</text></svg>`;
  }
  function weldDrawing(input, summary = null, layoutOnly = false) {
    const w = input.weld || {}, corner = w.connection === 'L', type = w.type === 'butt' ? 'butt' : 'fillet', guide = !summary, prefix = layoutOnly ? 'layout' : 'joint';
    const unit = w.lengthUnit || 'mm';
    const value = (key, suffix = 'mm') => {
      const raw = String(w[key] == null ? '' : w[key]).trim().replace(',', '.');
      return /^(?:\d+(?:\.\d*)?|\.\d+)$/.test(raw) && Number.isFinite(Number(raw)) && Number(raw) >= 0 ? esc(short(raw)) + ' ' + suffix : '—';
    };
    function measure(key, caption, x1, y1, x2, y2, tx, ty, rotate = 0, suffix = 'mm') {
      const lines = caption.split('|').map((text, i) => `<tspan x="${tx}"${i ? ' dy="17"' : ''}>${text}</tspan>`).join('');
      return `<g class="dimension" data-dimension="weld-${key}"><line x1="${x1}" y1="${y1}" x2="${x2}" y2="${y2}" marker-start="url(#${prefix}-start)" marker-end="url(#${prefix}-end)"/><text x="${tx}" y="${ty}" text-anchor="middle"${rotate ? ` transform="rotate(${rotate} ${tx} ${ty})"` : ''}>${lines}<tspan x="${tx}" dy="18">${value(key, suffix)}</tspan></text></g>`;
    }
    let drawing = `<text x="18" y="25" fill="#202A33" font-size="16" font-weight="600">${corner ? 'L connection · 90°' : 'Straight connection'}</text>`;
    if (corner) {
      drawing += '<path d="M55 82H275L245 112H55Z" fill="#BCC3C8" stroke="#586772"/><path d="M275 82V241H245V112Z" fill="#DFE3E6" stroke="#586772"/><path d="M274 83L246 111" stroke="#D4AA00" stroke-width="6"/>';
      drawing += measure('L1', 'First piece length', 55, 65, 260, 65, 155, 130, 0, unit);
      drawing += measure('L2', 'Second piece|length', 290, 97, 290, 241, 357, 174, 0, unit);
      drawing += '<path d="M273 96L324 58H374" fill="none" stroke="#806500"/><text x="350" y="48" text-anchor="middle" fill="#806500" font-size="14">Welded corner</text>';
    } else {
      drawing += '<rect x="35" y="80" width="185" height="28" fill="#BCC3C8" stroke="#586772"/><rect x="220" y="80" width="185" height="28" fill="#DFE3E6" stroke="#586772"/><rect x="216" y="77" width="8" height="34" fill="#F2CD24" stroke="#806500"/><path d="M223 81L266 51H312" fill="none" stroke="#806500"/><text x="289" y="43" text-anchor="middle" fill="#806500" font-size="14">Welded joint</text>';
      drawing += measure('L1', 'First piece length', 35, 127, 220, 127, 126, 151, 0, unit) + measure('L2', 'Second piece length', 220, 127, 405, 127, 312, 151, 0, unit);
    }
    const base = corner ? 286 : 214;
    if (!layoutOnly) {
      drawing += `<path d="M18 ${base - 19}H422" stroke="#DFE3E6"/><text x="18" y="${base + 3}" fill="#202A33" font-size="16" font-weight="600">${type === 'fillet' ? 'Fillet weld · cross-section' : 'Butt weld · cross-section'}</text>`;
      if (type === 'fillet') {
        const y = base + 45;
        drawing += `<path d="M95 ${y - 8}H115V${y + 72}H217V${y + 92}H95Z" fill="#DFE3E6" stroke="#586772"/><path d="M115 ${y}V${y + 72}H187Z" fill="#F2CD24" stroke="#806500"/>`;
        drawing += measure('z', 'Leg size', 72, y, 72, y + 72, 43, y + 40, -90) + measure('z', 'Leg size', 115, y + 113, 187, y + 113, 151, y + 137);
        drawing += `<text x="266" y="${y + 27}" fill="#806500" font-size="14">Yellow = weld</text><text x="266" y="${y + 54}" fill="#586772" font-size="13">Two equal legs</text>`;
      } else {
        const y = base + 101, gap = guide || Number(w.gapMm) > 0 ? 16 : 0, left = 164 - gap / 2, right = 164 + gap / 2;
        const cap = guide || Number(w.capHeightMm) > 0, faces = String(w.faces || '1');
        drawing += `<rect x="91" y="${y}" width="${left - 91}" height="62" fill="#BCC3C8" stroke="#586772"/><rect x="${right}" y="${y}" width="${237 - right}" height="62" fill="#DFE3E6" stroke="#586772"/>`;
        if (gap) drawing += `<rect x="${left}" y="${y}" width="${gap}" height="62" fill="#F2CD24" stroke="#806500"/>`;
        if (cap) {
          const previewCap = guide && !(Number(w.capHeightMm) > 0) ? 'fill="none" stroke="#806500" stroke-dasharray="4 3"' : 'fill="#F2CD24" stroke="#806500"';
          drawing += `<path d="M112 ${y}L164 ${y - 22}L216 ${y}Z" ${previewCap}/>`;
          if (faces === '2') drawing += `<path d="M112 ${y + 62}L164 ${y + 84}L216 ${y + 62}Z" ${previewCap}/>`;
        }
        if (w.lengthMode === 'manual') drawing += measure('joinedThicknessMm', 'Thickness', 63, y, 63, y + 62, 34, y + 36, -90);
        drawing += measure('capWidthMm', 'Cap width', 112, y - 33, 216, y - 33, 164, y - 59);
        drawing += measure('capHeightMm', 'Cap height', 258, y - 22, 258, y, 332, y - 8);
        drawing += measure('gapMm', 'Gap between pieces', left, y + 103, right, y + 103, 164, y + 126);
        drawing += `<text x="280" y="${y + 54}" fill="#586772" font-size="13">${esc(faces)} reinforced side${faces === '2' ? 's' : ''}</text>`;
      }
    }
    const height = layoutOnly ? (corner ? 265 : 190) : base + (type === 'butt' ? 257 : 226);
    return `<svg id="${layoutOnly ? 'connection-schematic' : 'weld-schematic'}" data-guide="${guide}" viewBox="0 0 440 ${height}" role="img" aria-label="${corner ? 'L connection at 90 degrees' : 'Straight connection'}. First piece length ${esc(w.L1 || 'not entered')} ${esc(unit)}, second piece length ${esc(w.L2 || 'not entered')} ${esc(unit)}. ${type} weld dimension guide. Schematic, not to scale." style="font-family:Segoe UI,Arial,sans-serif"><title>Piece lengths and weld dimensions</title>${defs(prefix)}${drawing}</svg>`;
  }
  function weld(result) { return weldDrawing(result.input, result.weld); }
  function weldGuide(input) { return weldDrawing(input); }
  function connectionGuide(input) { return weldDrawing(input, null, true); }
  const api = { section, member, weld, weldGuide, connectionGuide, weldCoverage, weldSelector, placeholder };
  if (typeof module !== 'undefined' && module.exports) module.exports = api; else root.SteelSketch = api;
})(typeof window === 'undefined' ? globalThis : window);
