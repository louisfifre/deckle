// Deckle logo generator. Run `node logo/generate.mjs` to rewrite the SVG files
// beside it and the app's icons under src/Deckle.App/Assets/Icons.
//
// The mark is four stacked sheets (d in front, a at the back) plus a flat blade (e).
// A rear sheet shows only what the sheet in front leaves visible, minus a gap: the
// gap is a parallel offset of the front sheet's edges, so it is equal everywhere by
// construction, and an inner corner is the front corner's radius plus the gap.
//
// The numbers come from the first logo, a generated image: they were measured on
// its trace, then regularised (one top angle, vertical right edges, one gap, evenly
// spaced tips, whole-degree rays). They live in the trace's coordinate space, y
// down; the SVG output is shifted to a 0-based square.
// Angles are degrees, counter-clockwise as seen on screen, 0 pointing right.

import { writeFileSync } from 'node:fs';
import { deflateSync } from 'node:zlib';
import { fileURLToPath } from 'node:url';

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

// A closed shape through `pts` ([x, y, radius]), each corner replaced by a
// circular arc. Kept as corners (start, end, radius, sweep) so the same shape
// writes an SVG path and flattens to a polygon for the icon raster.
function rounded(pts) {
  const n = pts.length, corners = [];
  for (let i = 0; i < n; i++) {
    const P = pts[i], A = pts[(i + n - 1) % n], B = pts[(i + 1) % n];
    const la = Math.hypot(A[0] - P[0], A[1] - P[1]), lb = Math.hypot(B[0] - P[0], B[1] - P[1]);
    const u = [(A[0] - P[0]) / la, (A[1] - P[1]) / la], v = [(B[0] - P[0]) / lb, (B[1] - P[1]) / lb];
    const half = Math.acos(Math.max(-1, Math.min(1, u[0] * v[0] + u[1] * v[1]))) / 2;
    let r = P[2] || 0, t = r / Math.tan(half);
    // A corner never eats more than about half of an edge it shares.
    const max = Math.min(la, lb) * 0.48;
    if (t > max) { t = max; r = t * Math.tan(half); }
    const k = r / Math.sin(half), bl = Math.hypot(u[0] + v[0], u[1] + v[1]);
    corners.push({
      s: [P[0] + u[0] * t, P[1] + u[1] * t], e: [P[0] + v[0] * t, P[1] + v[1] * t], r,
      c: [P[0] + (u[0] + v[0]) / bl * k, P[1] + (u[1] + v[1]) / bl * k],
      sweep: u[0] * v[1] - u[1] * v[0] < 0 ? 1 : 0,
    });
  }
  return corners;
}

function polygon(corners) {
  const out = [];
  for (const { s, e, c, r } of corners) {
    out.push(s);
    if (r <= 0.01) continue;
    const a0 = Math.atan2(s[1] - c[1], s[0] - c[0]);
    let da = Math.atan2(e[1] - c[1], e[0] - c[0]) - a0;
    if (da > Math.PI) da -= 2 * Math.PI; else if (da < -Math.PI) da += 2 * Math.PI;
    const steps = Math.max(2, Math.ceil(Math.abs(da) / 0.08));
    for (let i = 1; i <= steps; i++) out.push([c[0] + r * Math.cos(a0 + da * i / steps), c[1] + r * Math.sin(a0 + da * i / steps)]);
  }
  return out;
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

const BLADE_IDS = ['a', 'b', 'c', 'd', 'e'];

// --- Colour -----------------------------------------------------------------

// Both gradients are placed in the drawing's space (userSpaceOnUse), not in each
// blade's own box: the blades read as cut from one sheet of colour.

// At rest: one linear gradient across the five blades.
const REST = { from: PIVOT, to: [1029, 642],
  stops: [[0, '#05B2FC'], [0.25, '#1F7DFB'], [0.5, '#4F49FB'], [0.75, '#7137FB'], [1, '#8A2EFB']] };

// Lit: one radial gradient centred on the pivot, shared by every lit blade.
const EMBER = { centre: PIVOT, radius: 720,
  stops: [[0.15, '#FFC21A'], [0.42, '#FF8A1F'], [0.66, '#FF4D2E'], [0.86, '#F5245E'], [1, '#E01E8C']] };

// The dot an icon carries while a user incident is open: Windows' caution yellow.
const INCIDENT = '#FFB900';

// What each state lights. The names are the app's: recording while the microphone
// is open, rewriting while a profile rewrites the transcript.
const STATES = { rest: [], recording: ['a'], rewriting: ['a', 'e'] };

// --- SVG --------------------------------------------------------------------

const SVG_ORIGIN = [298.5, 230.5];
const SVG_SIZE = 760;
const f = n => Math.round(n * 100) / 100;
const fx = x => f(x - SVG_ORIGIN[0]);
const fy = y => f(y - SVG_ORIGIN[1]);

function pathData(corners) {
  let d = '';
  corners.forEach(({ s, e, r, sweep }, i) => {
    d += `${i ? 'L' : 'M'}${fx(s[0])} ${fy(s[1])}`;
    if (r > 0.01) d += `A${f(r)} ${f(r)} 0 0 ${sweep} ${fx(e[0])} ${fy(e[1])}`;
  });
  return d + 'Z';
}

const stopTags = list => list.map(([o, c]) => `<stop offset="${o}" stop-color="${c}"/>`).join('');

function svg(blades, title, lit) {
  let defs = `<linearGradient id="rest" gradientUnits="userSpaceOnUse" x1="${fx(REST.from[0])}" y1="${fy(REST.from[1])}" x2="${fx(REST.to[0])}" y2="${fy(REST.to[1])}">${stopTags(REST.stops)}</linearGradient>`;
  if (lit.length) defs += `\n<radialGradient id="ember" gradientUnits="userSpaceOnUse" cx="${fx(EMBER.centre[0])}" cy="${fy(EMBER.centre[1])}" r="${EMBER.radius}">${stopTags(EMBER.stops)}</radialGradient>`;
  const paths = BLADE_IDS
    .map(id => `<path id="blade-${id}" d="${pathData(blades[id])}" fill="url(#${lit.includes(id) ? 'ember' : 'rest'})"/>`).join('\n');
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${SVG_SIZE} ${SVG_SIZE}" role="img" aria-label="${title}">
<defs>
${defs}
</defs>
${paths}
</svg>
`;
}

// --- Icon raster ------------------------------------------------------------

// The icons are drawn here rather than exported from a drawing tool, so a change
// of number above reaches every size in one run. Each pixel averages a grid of
// samples; a sample takes the colour of the shape that holds it.

const rgb = hex => [1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16));
const ramp = list => list.map(([o, c]) => [o, rgb(c)]);
function sampleRamp(stops, t) {
  if (t <= stops[0][0]) return stops[0][1];
  for (let i = 1; i < stops.length; i++) {
    if (t > stops[i][0]) continue;
    const [o0, c0] = stops[i - 1], [o1, c1] = stops[i], k = (t - o0) / (o1 - o0);
    return c0.map((v, j) => v + (c1[j] - v) * k);
  }
  return stops[stops.length - 1][1];
}
const restStops = ramp(REST.stops), emberStops = ramp(EMBER.stops);
const restAxis = [REST.to[0] - REST.from[0], REST.to[1] - REST.from[1]];
const restLength2 = restAxis[0] ** 2 + restAxis[1] ** 2;
const restColour = (x, y) => sampleRamp(restStops, ((x - REST.from[0]) * restAxis[0] + (y - REST.from[1]) * restAxis[1]) / restLength2);
const emberColour = (x, y) => sampleRamp(emberStops, Math.hypot(x - EMBER.centre[0], y - EMBER.centre[1]) / EMBER.radius);

function shape(points, colour) {
  const xs = points.map(p => p[0]), ys = points.map(p => p[1]);
  return { points, colour, x0: Math.min(...xs), x1: Math.max(...xs), y0: Math.min(...ys), y1: Math.max(...ys) };
}
function holds(s, x, y) {
  if (x < s.x0 || x > s.x1 || y < s.y0 || y > s.y1) return false;
  let inside = false;
  const p = s.points;
  for (let i = 0, j = p.length - 1; i < p.length; j = i++)
    if ((p[i][1] > y) !== (p[j][1] > y) && x < (p[j][0] - p[i][0]) * (y - p[i][1]) / (p[j][1] - p[i][1]) + p[i][0]) inside = !inside;
  return inside;
}

// The square an icon shows: the mark's own box, centred, with a thin margin, so
// the mark takes all the pixels a 16 px tray icon has.
function iconView(shapes) {
  const x0 = Math.min(...shapes.map(s => s.x0)), x1 = Math.max(...shapes.map(s => s.x1));
  const y0 = Math.min(...shapes.map(s => s.y0)), y1 = Math.max(...shapes.map(s => s.y1));
  const side = Math.max(x1 - x0, y1 - y0) * 1.03;
  return { x: (x0 + x1 - side) / 2, y: (y0 + y1 - side) / 2, side };
}

// RGBA pixels, straight alpha.
function raster(blades, lit, incident, size) {
  const shapes = BLADE_IDS.map(id => shape(polygon(blades[id]), lit.includes(id) ? emberColour : restColour));
  const view = iconView(shapes);
  if (incident) {
    // The dot sits in the top-left corner, the one the mark leaves empty.
    const c = [view.x + view.side * 0.125, view.y + view.side * 0.125], r = view.side * 0.115, dot = rgb(INCIDENT);
    const circle = Array.from({ length: 64 }, (_, i) => [c[0] + r * Math.cos(i * Math.PI / 32), c[1] + r * Math.sin(i * Math.PI / 32)]);
    shapes.unshift(shape(circle, () => dot));
  }
  const grid = size >= 128 ? 4 : 8, px = new Uint8Array(size * size * 4), step = view.side / size / grid;
  for (let py = 0; py < size; py++) for (let pxl = 0; pxl < size; pxl++) {
    let r = 0, g = 0, b = 0, hit = 0;
    for (let sy = 0; sy < grid; sy++) for (let sx = 0; sx < grid; sx++) {
      const x = view.x + (pxl * grid + sx + 0.5) * step, y = view.y + (py * grid + sy + 0.5) * step;
      const s = shapes.find(s => holds(s, x, y));
      if (!s) continue;
      const c = s.colour(x, y);
      r += c[0]; g += c[1]; b += c[2]; hit++;
    }
    if (!hit) continue;
    const o = (py * size + pxl) * 4;
    px[o] = Math.round(r / hit); px[o + 1] = Math.round(g / hit); px[o + 2] = Math.round(b / hit);
    px[o + 3] = Math.round(255 * hit / (grid * grid));
  }
  return px;
}

// --- PNG and ICO containers -------------------------------------------------

const CRC = Array.from({ length: 256 }, (_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xEDB88320 ^ (c >>> 1) : c >>> 1; return c >>> 0; });
function crc32(buf) { let c = 0xFFFFFFFF; for (const b of buf) c = CRC[(c ^ b) & 0xFF] ^ (c >>> 8); return (c ^ 0xFFFFFFFF) >>> 0; }
function chunk(type, data) {
  const head = Buffer.alloc(8); head.writeUInt32BE(data.length, 0); head.write(type, 4, 'ascii');
  const tail = Buffer.alloc(4); tail.writeUInt32BE(crc32(Buffer.concat([head.subarray(4), data])), 0);
  return Buffer.concat([head, data, tail]);
}
function png(px, size) {
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(size, 0); ihdr.writeUInt32BE(size, 4); ihdr[8] = 8; ihdr[9] = 6;
  const rows = Buffer.alloc(size * (size * 4 + 1));
  for (let y = 0; y < size; y++) Buffer.from(px.buffer, y * size * 4, size * 4).copy(rows, y * (size * 4 + 1) + 1);
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', ihdr), chunk('IDAT', deflateSync(rows, { level: 9 })), chunk('IEND', Buffer.alloc(0))]);
}
// A 32-bit bitmap frame as an .ico stores it: bottom-up BGRA, then an all-zero
// 1-bit mask, the alpha channel doing the masking.
function bitmapFrame(px, size) {
  const maskRow = Math.ceil(size / 32) * 4, out = Buffer.alloc(40 + size * size * 4 + maskRow * size);
  out.writeUInt32LE(40, 0); out.writeInt32LE(size, 4); out.writeInt32LE(size * 2, 8);
  out.writeUInt16LE(1, 12); out.writeUInt16LE(32, 14); out.writeUInt32LE(size * size * 4, 20);
  for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) {
    const i = (y * size + x) * 4, o = 40 + ((size - 1 - y) * size + x) * 4;
    out[o] = px[i + 2]; out[o + 1] = px[i + 1]; out[o + 2] = px[i]; out[o + 3] = px[i + 3];
  }
  return out;
}
// Bitmap frames up to 128 px, a PNG frame at 256 px: the mix every Windows
// loader reads.
const ICON_SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256];
function ico(frames) {
  const head = Buffer.alloc(6 + 16 * frames.length); head.writeUInt16LE(1, 2); head.writeUInt16LE(frames.length, 4);
  let at = head.length;
  frames.forEach(({ size, data }, i) => {
    const o = 6 + 16 * i;
    head[o] = size & 0xFF; head[o + 1] = size & 0xFF; head.writeUInt16LE(1, o + 4); head.writeUInt16LE(32, o + 6);
    head.writeUInt32LE(data.length, o + 8); head.writeUInt32LE(at, o + 12);
    at += data.length;
  });
  return Buffer.concat([head, ...frames.map(fr => fr.data)]);
}

// --- Files ------------------------------------------------------------------

const blades = buildBlades();
const beside = name => fileURLToPath(new URL(name, import.meta.url));
const iconFile = name => fileURLToPath(new URL(`../src/Deckle.App/Assets/Icons/${name}`, import.meta.url));

writeFileSync(beside('deckle.svg'), svg(blades, 'Deckle', STATES.rest));
writeFileSync(beside('deckle-recording.svg'), svg(blades, 'Deckle, recording', STATES.recording));
writeFileSync(beside('deckle-rewriting.svg'), svg(blades, 'Deckle, rewriting', STATES.rewriting));

for (const [state, lit] of Object.entries(STATES)) {
  for (const incident of [false, true]) {
    const frames = ICON_SIZES.map(size => {
      const px = raster(blades, lit, incident, size);
      return { size, data: size === 256 ? png(px, size) : bitmapFrame(px, size) };
    });
    writeFileSync(iconFile(`deckle--state--${state}${incident ? '--incident--true' : ''}.ico`), ico(frames));
  }
  // A XAML title bar takes a bitmap: see IconAssets.
  writeFileSync(iconFile(`deckle--state--${state}--32px.png`), png(raster(blades, lit, false, 32), 32));
}
