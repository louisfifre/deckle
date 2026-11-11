// Deckle logo generator. Run `node logo/generate.mjs` to rewrite the SVG files beside it.
//
// The mark is four stacked sheets (d in front, a at the back) plus a flat blade (e).
// A rear sheet shows only what the sheet in front leaves visible, minus a gap: the
// gap is a parallel offset of the front sheet's edges, so it is equal everywhere by
// construction, and an inner corner is the front corner's radius plus the gap.
//
// The numbers come from the first logo, a generated image: they were measured on
// its trace, then regularised (one top angle, vertical right edges, one gap, evenly
// spaced tips, whole-degree rays). They live in the trace's coordinate space, y
// down; ORIGIN shifts the output to a 0-based square.
// Angles are degrees, counter-clockwise as seen on screen, 0 pointing right.

import { writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const ORIGIN = [298.5, 230.5];
const SIZE = 760;

// --- Geometry helpers -------------------------------------------------------

const rad = a => a * Math.PI / 180;
const line = (p, a) => ({ p, d: [Math.cos(rad(a)), -Math.sin(rad(a))] });
const vertical = x => ({ p: [x, 0], d: [0, 1] });
function cross(l1, l2) {
  const [x1, y1] = l1.p, [a, b] = l1.d, [x2, y2] = l2.p, [c, d] = l2.d;
  const t = ((x2 - x1) * d - (y2 - y1) * c) / (a * d - b * c);
  return [x1 + a * t, y1 + b * t];
}
// Parallel line at distance g, on the side facing away from `inside`.
function offset(l, g, inside) {
  let n = [-l.d[1], l.d[0]];
  if ((l.p[0] - inside[0]) * n[0] + (l.p[1] - inside[1]) * n[1] < 0) n = [-n[0], -n[1]];
  return { p: [l.p[0] + n[0] * g, l.p[1] + n[1] * g], d: l.d };
}
const centre = pts => [0, 1].map(i => pts.reduce((s, p) => s + p[i], 0) / pts.length);
const f = n => Math.round(n * 100) / 100;
const fx = x => f(x - ORIGIN[0]);
const fy = y => f(y - ORIGIN[1]);

// Closed path through `pts` ([x, y, radius]), each corner replaced by a circular arc.
function rounded(pts) {
  const n = pts.length; let d = '';
  for (let i = 0; i < n; i++) {
    const P = pts[i], A = pts[(i + n - 1) % n], B = pts[(i + 1) % n];
    const la = Math.hypot(A[0] - P[0], A[1] - P[1]), lb = Math.hypot(B[0] - P[0], B[1] - P[1]);
    const u = [(A[0] - P[0]) / la, (A[1] - P[1]) / la], v = [(B[0] - P[0]) / lb, (B[1] - P[1]) / lb];
    const half = Math.acos(Math.max(-1, Math.min(1, u[0] * v[0] + u[1] * v[1]))) / 2;
    let r = P[2] || 0, t = r / Math.tan(half);
    // A corner never eats more than about half of an edge it shares.
    const max = Math.min(la, lb) * 0.48;
    if (t > max) { t = max; r = t * Math.tan(half); }
    const s = [P[0] + u[0] * t, P[1] + u[1] * t], e = [P[0] + v[0] * t, P[1] + v[1] * t];
    const sweep = u[0] * v[1] - u[1] * v[0] < 0 ? 1 : 0;
    d += `${i ? 'L' : 'M'}${fx(s[0])} ${fy(s[1])}`;
    if (r > 0.01) d += `A${f(r)} ${f(r)} 0 0 ${sweep} ${fx(e[0])} ${fy(e[1])}`;
  }
  return d + 'Z';
}

// --- The mark ---------------------------------------------------------------

const TOP_ANGLE = 27;   // every top edge slopes down to the right by this much
const GAP = 24;         // between a sheet and the one behind it
const TIP_RADIUS = 1.5;
const INNER_RADIUS = 10;
const PIVOT = [500, 960]; // where the blades converge; the tips sit around it

// Per sheet: its left edge, a point its top edge passes through, its right edge,
// its tip, the ray from the tip up to the right edge (`bottom`), corner radii.
const SHEETS = {
  d: { left: vertical(348), top: [395, 639], tip: [485, 945], right: 98, bottomLeft: 122, rTL: 16, rTR: 16, rBL: 8 },
  c: { left: vertical(396), top: [470, 545], right: vertical(548), tip: [492.5, 951.5], bottom: 78, rTL: 20, rTR: 22, rBR: 20 },
  b: { left: line([464, 462], 78), top: [590, 444], right: vertical(700), tip: [500, 958], bottom: 58, rTL: 26, rTR: 30, rBR: 28 },
  a: { left: line([555, 337], 72), top: [728.5, 320], right: vertical(876), tip: [507.5, 964.5], bottom: 40, rTL: 34, rTR: 40, rBR: 36 },
};
const FLAT = { tip: [515, 971], bottom: 22, rApex: 14, rEnd: 22 };

function buildBlades() {
  const top = k => line(SHEETS[k].top, -TOP_ANGLE);
  const out = {};

  // d, the front sheet, has no bend: its right edge runs straight to the tip.
  const d = SHEETS.d, dRight = line(d.tip, d.right), dTop = top('d');
  const dTL = cross(d.left, dTop), dTR = cross(dTop, dRight), dBL = cross(line(d.tip, d.bottomLeft), d.left);
  out.d = rounded([[...dTL, d.rTL], [...dTR, d.rTR], [...d.tip, TIP_RADIUS], [...dBL, d.rBL]]);
  let front = { top: dTop, right: dRight, bottom: null, c: centre([dTL, dTR, d.tip, dBL]), rTR: d.rTR, rBR: 0 };

  for (const k of ['c', 'b', 'a']) {
    const s = SHEETS[k], sTop = top(k), bottom = line(s.tip, s.bottom);
    const TL = cross(s.left, sTop), TR = cross(sTop, s.right), BR = cross(s.right, bottom);
    const oTop = offset(front.top, GAP, front.c), oRight = offset(front.right, GAP, front.c);
    const pts = [[...TL, s.rTL], [...TR, s.rTR], [...BR, s.rBR], [...s.tip, TIP_RADIUS]];
    // The gap is not offset below the bend: the edge runs from this sheet's own
    // tip, so the gap narrows towards the pivot as in the first logo.
    if (front.bottom) pts.push([...cross(offset(front.bottom, GAP, front.c), oRight), front.rBR + GAP]);
    pts.push([...cross(oRight, oTop), front.rTR + GAP]);
    pts.push([...cross(oTop, s.left), INNER_RADIUS]);
    out[k] = rounded(pts);
    front = { top: sTop, right: s.right, bottom, c: centre([TL, TR, BR, s.tip]), rTR: s.rTR, rBR: s.rBR };
  }

  // e, the flat blade: its apex sits under sheet a's right edge, one gap below it.
  const apex = cross(offset(front.bottom, GAP, front.c), SHEETS.a.right);
  const end = cross(line(apex, -TOP_ANGLE), line(FLAT.tip, FLAT.bottom));
  out.e = rounded([[...apex, FLAT.rApex], [...end, FLAT.rEnd], [...FLAT.tip, TIP_RADIUS]]);
  return out;
}

// --- Colour -----------------------------------------------------------------

// Both gradients are placed in the drawing's space (userSpaceOnUse), not in each
// blade's own box: the blades read as cut from one sheet of colour.

// At rest: one linear gradient across the five blades.
const REST = { from: PIVOT, to: [1029, 642],
  stops: [[0, '#05B2FC'], [0.25, '#1F7DFB'], [0.5, '#4F49FB'], [0.75, '#7137FB'], [1, '#8A2EFB']] };

// Lit: one radial gradient centred on the pivot, shared by every lit blade.
const EMBER = { centre: PIVOT, radius: 720,
  stops: [[0.15, '#FFC21A'], [0.42, '#FF8A1F'], [0.66, '#FF4D2E'], [0.86, '#F5245E'], [1, '#E01E8C']] };

const stops = list => list.map(([o, c]) => `<stop offset="${o}" stop-color="${c}"/>`).join('');

// lit: ids of the blades that take the ember gradient.
function svg(blades, title, lit = []) {
  let defs = `<linearGradient id="rest" gradientUnits="userSpaceOnUse" x1="${fx(REST.from[0])}" y1="${fy(REST.from[1])}" x2="${fx(REST.to[0])}" y2="${fy(REST.to[1])}">${stops(REST.stops)}</linearGradient>`;
  if (lit.length) defs += `\n<radialGradient id="ember" gradientUnits="userSpaceOnUse" cx="${fx(EMBER.centre[0])}" cy="${fy(EMBER.centre[1])}" r="${EMBER.radius}">${stops(EMBER.stops)}</radialGradient>`;
  const paths = ['a', 'b', 'c', 'd', 'e']
    .map(id => `<path id="blade-${id}" d="${blades[id]}" fill="url(#${lit.includes(id) ? 'ember' : 'rest'})"/>`).join('\n');
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${SIZE} ${SIZE}" role="img" aria-label="${title}">
<defs>
${defs}
</defs>
${paths}
</svg>
`;
}

// --- Files ------------------------------------------------------------------

const blades = buildBlades();
const files = {
  'deckle.svg': svg(blades, 'Deckle'),
  'deckle-transcribing.svg': svg(blades, 'Deckle, transcribing', ['a']),
  'deckle-rewriting.svg': svg(blades, 'Deckle, transcribing and rewriting', ['a', 'e']),
};
for (const [name, content] of Object.entries(files))
  writeFileSync(fileURLToPath(new URL(name, import.meta.url)), content);
