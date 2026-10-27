(function (root) {
  'use strict';
  const SOURCE = 'https://www.maiak-m.bg/en/calculator/i5/Hollow-structural-sections-rectangular.html';
  const PI = '3.141592653589793';
  const MODEL = 'Inferred rounded-corner RHS candidate: area = 2t(A+B) - (12-2π)t², density 7850 kg/m³, π = 3.141592653589793, total uses unrounded kg/m. Matches three user-supplied website examples at two decimals; website source code and wider-range behavior remain unverified.';
  function gcd(a, b) { a = a < 0n ? -a : a; while (b) { const r = a % b; a = b; b = r; } return a || 1n; }
  function fraction(n, d = 1n) { if (d === 0n) throw new Error('Cannot divide by zero.'); if (d < 0n) { n = -n; d = -d; } const g = gcd(n, d); return { n: n / g, d: d / g }; }
  const add = (a, b) => fraction(a.n * b.d + b.n * a.d, a.d * b.d);
  const sub = (a, b) => fraction(a.n * b.d - b.n * a.d, a.d * b.d);
  const mul = (a, b) => fraction(a.n * b.n, a.d * b.d);
  const cmp = (a, b) => { const d = a.n * b.d - b.n * a.d; return d < 0n ? -1 : d > 0n ? 1 : 0; };
  function parse(value, label = 'Value') {
    const s = String(value == null ? '' : value).trim().replace(',', '.');
    if (!/^[+-]?(?:\d+(?:\.\d*)?|\.\d+)$/.test(s)) throw new Error(label + ': enter a decimal number without thousands separators.');
    const negative = s.startsWith('-'), unsigned = s.replace(/^[+-]/, ''), pieces = unsigned.split('.');
    const digits = (pieces[0] || '0') + (pieces[1] || '');
    return fraction(BigInt(digits) * (negative ? -1n : 1n), 10n ** BigInt((pieces[1] || '').length));
  }
  function fixed(value, places) {
    const scale = 10n ** BigInt(places), negative = value.n < 0n, n = (negative ? -value.n : value.n) * scale;
    let whole = n / value.d; if ((n % value.d) * 2n >= value.d) whole++;
    let s = whole.toString().padStart(places + 1, '0');
    if (places) s = s.slice(0, -places) + '.' + s.slice(-places);
    return (negative && whole ? '-' : '') + s;
  }
  function exact(value) {
    let d = value.d, twos = 0, fives = 0;
    while (d % 2n === 0n) { d /= 2n; twos++; }
    while (d % 5n === 0n) { d /= 5n; fives++; }
    if (d !== 1n) return value.n + '/' + value.d;
    const s = fixed(value, Math.max(twos, fives));
    return s.includes('.') ? s.replace(/0+$/, '').replace(/\.$/, '') : s;
  }
  function calculate(input) {
    const a = parse(input.A, 'Side A'), b = parse(input.B, 'Side B'), t = parse(input.t, 'Thickness t'), length = parse(input.L, 'Length L');
    for (const pair of [[a, 'Side A'], [b, 'Side B'], [t, 'Thickness t'], [length, 'Length L']]) if (pair[0].n <= 0n) throw new Error(pair[1] + ' must be greater than zero.');
    const twiceT = mul(fraction(2n), t);
    if (cmp(twiceT, a) >= 0 || cmp(twiceT, b) >= 0) throw new Error('For this hollow-section model, twice the thickness must be less than both outside dimensions.');
    const sharpArea = mul(twiceT, sub(add(a, b), twiceT));
    const cornerCorrection = mul(mul(fraction(2n), sub(fraction(4n), parse(PI))), mul(t, t));
    const area = sub(sharpArea, cornerCorrection);
    const mass = mul(area, fraction(7850n, 1000000n));
    return {
      input: { A: exact(a), B: exact(b), t: exact(t), L: exact(length) },
      densityKgM3: '7850', pi: PI, areaMm2: exact(area), kgPerM: exact(mass), totalKg: exact(mul(mass, length)),
      basis: MODEL, websiteAgreement: 'unverified'
    };
  }
  function compareValue(candidate, reference) {
    const value = parse(reference, 'Website result');
    if (value.n < 0n) throw new Error('Website mass cannot be negative.');
    const normalized = String(reference).trim().replace(',', '.'), places = (normalized.split('.')[1] || '').length;
    const rounded = fixed(parse(candidate), places);
    return { entered: String(reference).trim(), decimalPlaces: places, candidateAtReferencePrecision: rounded,
      matchesDisplayedPrecision: cmp(parse(rounded), value) === 0, candidateMinusReference: exact(sub(parse(candidate), value)) };
  }
  function compareCase(input, reference = {}) {
    const result = calculate(input), hasMass = String(reference.kgPerM == null ? '' : reference.kgPerM).trim() !== '', hasTotal = String(reference.totalKg == null ? '' : reference.totalKg).trim() !== '';
    const mass = hasMass ? compareValue(result.kgPerM, reference.kgPerM) : null, total = hasTotal ? compareValue(result.totalKg, reference.totalKg) : null;
    let status = !hasMass && !hasTotal ? 'reference_missing' : !hasMass || !hasTotal ? 'reference_incomplete' : mass.matchesDisplayedPrecision && total.matchesDisplayedPrecision ? 'matches_entered_displayed_values' : 'differs_from_entered_values';
    if ((mass && !mass.matchesDisplayedPrecision) || (total && !total.matchesDisplayedPrecision)) status = 'differs_from_entered_values';
    let roundingObservation = null;
    if (mass && mass.matchesDisplayedPrecision && total && !total.matchesDisplayedPrecision) {
      const displayedMassTimesLength = exact(mul(parse(reference.kgPerM), parse(input.L)));
      if (compareValue(displayedMassTimesLength, reference.totalKg).matchesDisplayedPrecision) roundingObservation = 'Multiplying the entered displayed kg/m by length matches the entered total. This suggests a rounding-order difference; it does not establish the website formula.';
    }
    return { candidate: result, reference: { kgPerM: mass, totalKg: total }, status, roundingObservation };
  }
  function buildReport(cases) {
    const rows = cases.map(row => ({ id: row.id, referenceProvenance: row.referenceProvenance || 'Manually entered by the tester.', comparison: compareCase(row.input, row.reference) }));
    const completed = rows.filter(row => row.comparison.reference.kgPerM && row.comparison.reference.totalKg);
    return { kind: 'RHS_WEIGHT_COMPARISON', version: 1, capturedUtc: new Date().toISOString(), sourceUrl: SOURCE,
      referenceProvenance: 'Website outputs supplied by the user in chat or manually entered by the tester; see each row. No live website execution has been performed by this prototype.',
      formula: MODEL, websiteFormulaVerified: false, widerRangeAgreementEstablished: false,
      completeReferenceCases: completed.length, matchingCompleteCases: completed.filter(row => row.comparison.status === 'matches_entered_displayed_values').length,
      limitation: 'Matching entered results establishes agreement only for those cases and their displayed precision. Website rounding and boundary validation remain unverified.', rows };
  }
  const api = { calculate, compareCase, compareValue, buildReport, format: (value, places = 2) => fixed(parse(value), places), sourceUrl: SOURCE, model: MODEL };
  if (typeof module !== 'undefined' && module.exports) module.exports = api; else root.RhsWeight = api;
})(typeof window === 'undefined' ? globalThis : window);
