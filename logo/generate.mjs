// Deckle logo generator. Run `node logo/generate.mjs` to rewrite the SVG files
// and the sheet beside it, and the app's icons under src/Deckle.App/Assets/Icons.
//
// The mark is a stack of sheets (d in front, a at the back) plus a flat blade (e).
// A rear sheet shows only what the sheet in front leaves visible, minus a gap: the
// gap is a parallel offset of the front sheet's edges, so it is equal everywhere by
// construction, and an inner corner is the front corner's radius plus the gap.
//
// The mark has two forms, because a gap that reads at 64 px closes at 16 px:
// `normal`, four blades, and `small`, three blades and a wider gap, for where the
// mark is shown small (the tray, a title bar). A third, `five`, is the first
// drawing: it is kept as a reference SVG and no icon uses it.
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
const INNER_RADIUS = 10;
const PIVOT = [500, 960]; // where the blades converge; the tips sit around it

// A sheet: its left edge, a point its top edge passes through, its right edge,
// its tip, the ray from the tip up to the right edge (`bottom`), corner radii.
// The front sheet has no bend: `right` is the ray from its tip, `bottomLeft`
// the ray from its tip to its left edge.
const FRONT = { left: vertical(348), top: [395, 639], tip: [485, 945], right: 98, bottomLeft: 122, rTL: 16, rTR: 16, rBL: 8 };
const BACK = { left: line([555, 337], 72), top: [728.5, 320], right: vertical(876), tip: [507.5, 964.5], bottom: 40, rTL: 34, rTR: 40, rBR: 36 };
const FLAT = { tip: [515, 971], bottom: 22, rApex: 14, rEnd: 22 };

// A form: the gap between a sheet and the one behind it, the tip radius, the
// front sheet, the sheets behind it from front to back (each with the letter
// its blade keeps across forms), the flat blade. Fewer blades take a wider gap
// and rounder corners, so the mark stays open when it is a few pixels wide.
const FORMS = {
  normal: {
    gap: 44, tipRadius: 3,
    front: { ...FRONT, top: [395, 620], right: 92, rTL: 22, rTR: 22, rBL: 10 },
    rear: [
      { id: 'b', left: line([440, 470], 80), top: [570, 455], right: vertical(668), tip: [496, 955], bottom: 62, rTL: 30, rTR: 34, rBR: 30 },
      { id: 'a', ...BACK, tip: [506, 964] }],
    flat: { ...FLAT, rApex: 18, rEnd: 28 },
  },
  small: {
    gap: 56, tipRadius: 4,
    front: { ...FRONT, top: [400, 560], right: 80, rTL: 30, rTR: 30, rBL: 12 },
    rear: [{ id: 'a', ...BACK, left: line([520, 360], 76), tip: [504, 962], bottom: 42 }],
    flat: { ...FLAT, rApex: 22, rEnd: 32 },
  },
  five: {
    gap: 24, tipRadius: 1.5,
    front: FRONT,
    rear: [
      { id: 'c', left: vertical(396), top: [470, 545], right: vertical(548), tip: [492.5, 951.5], bottom: 78, rTL: 20, rTR: 22, rBR: 20 },
      { id: 'b', left: line([464, 462], 78), top: [590, 444], right: vertical(700), tip: [500, 958], bottom: 58, rTL: 26, rTR: 30, rBR: 28 },
      { id: 'a', ...BACK }],
    flat: FLAT,
  },
};

// The blades of a form, in letter order: [{ id, corners }]. `a` is always the
// back sheet, `d` the front one, `e` the flat blade.
function buildBlades(form) {
  const { gap, tipRadius } = form;
  const top = s => line(s.top, -TOP_ANGLE);
  const out = [];

  const d = form.front, dRight = line(d.tip, d.right), dTop = top(d);
  const dTL = cross(d.left, dTop), dTR = cross(dTop, dRight), dBL = cross(line(d.tip, d.bottomLeft), d.left);
  out.push({ id: 'd', corners: rounded([[...dTL, d.rTL], [...dTR, d.rTR], [...d.tip, tipRadius], [...dBL, d.rBL]]) });
  let front = { top: dTop, right: dRight, bottom: null, c: centre([dTL, dTR, d.tip, dBL]), rTR: d.rTR, rBR: 0 };
  let back;

  for (const s of form.rear) {
    const sTop = top(s), bottom = line(s.tip, s.bottom);
    const TL = cross(s.left, sTop), TR = cross(sTop, s.right), BR = cross(s.right, bottom);
    const oTop = offset(front.top, gap, front.c), oRight = offset(front.right, gap, front.c);
    const pts = [[...TL, s.rTL], [...TR, s.rTR], [...BR, s.rBR], [...s.tip, tipRadius]];
    // The gap is not offset below the bend: the edge runs from this sheet's own
    // tip, so the gap narrows towards the pivot as in the first logo.
    if (front.bottom) pts.push([...cross(offset(front.bottom, gap, front.c), oRight), front.rBR + gap]);
    pts.push([...cross(oRight, oTop), front.rTR + gap]);
    pts.push([...cross(oTop, s.left), INNER_RADIUS]);
    out.push({ id: s.id, corners: rounded(pts) });
    front = { top: sTop, right: s.right, bottom, c: centre([TL, TR, BR, s.tip]), rTR: s.rTR, rBR: s.rBR };
    back = s;
  }

  // e, the flat blade: its apex sits under the back sheet's right edge, one gap below it.
  const F = form.flat;
  const apex = cross(offset(front.bottom, gap, front.c), back.right);
  const end = cross(line(apex, -TOP_ANGLE), line(F.tip, F.bottom));
  out.push({ id: 'e', corners: rounded([[...apex, F.rApex], [...end, F.rEnd], [...F.tip, tipRadius]]) });
  return out.sort((p, q) => p.id.localeCompare(q.id));
}

// --- Colour -----------------------------------------------------------------

// Both gradients are placed in the drawing's space (userSpaceOnUse), not in each
// blade's own box: the blades read as cut from one sheet of colour.

// At rest: one linear gradient across the blades.
const REST = { from: PIVOT, to: [1029, 642],
  stops: [[0, '#05B2FC'], [0.25, '#1F7DFB'], [0.5, '#4F49FB'], [0.75, '#7137FB'], [1, '#8A2EFB']] };

// Lit: one radial gradient centred on the pivot, shared by every lit blade.
const EMBER = { centre: PIVOT, radius: 720,
  stops: [[0.15, '#FFC21A'], [0.42, '#FF8A1F'], [0.66, '#FF4D2E'], [0.86, '#F5245E'], [1, '#E01E8C']] };

// The dot an icon carries while a user incident is open: Windows' caution yellow.
const INCIDENT = '#FFB900';

// The tile behind the mark where it stands among other apps (the taskbar,
// Alt+Tab). It is keyed by the Windows mode it is shown on: pale on a dark
// taskbar, dark on a light one. The mark takes TILE_MARK of the tile's side.
const TILE = { dark: '#E6E8F0', light: '#14142B' };
const TILE_MARK = 1 / 1.34, TILE_RADIUS = 0.22;

// What each state lights. The names are the app's: recording while the microphone
// is open, rewriting while a profile rewrites the transcript.
const STATES = { rest: [], recording: ['a'], rewriting: ['a', 'e'] };

// --- Views ------------------------------------------------------------------

// The square a mark is shown in: its own box, centred, with a thin margin, so it
// takes all the pixels a 16 px tray icon has. `scale` widens it for a tile.
function markView(blades, scale = 1) {
  const pts = blades.flatMap(b => polygon(b.corners));
  const xs = pts.map(p => p[0]), ys = pts.map(p => p[1]);
  const x0 = Math.min(...xs), x1 = Math.max(...xs), y0 = Math.min(...ys), y1 = Math.max(...ys);
  const side = Math.max(x1 - x0, y1 - y0) * 1.03 * scale;
  return { x: (x0 + x1 - side) / 2, y: (y0 + y1 - side) / 2, side };
}
const tileView = blades => markView(blades, 1 / TILE_MARK);

// --- SVG --------------------------------------------------------------------

const SVG_ORIGIN = [298.5, 230.5];
const SVG_SIZE = 760;
const f = n => Math.round(n * 100) / 100;

function pathData(corners, ox = 0, oy = 0) {
  let d = '';
  corners.forEach(({ s, e, r, sweep }, i) => {
    d += `${i ? 'L' : 'M'}${f(s[0] - ox)} ${f(s[1] - oy)}`;
    if (r > 0.01) d += `A${f(r)} ${f(r)} 0 0 ${sweep} ${f(e[0] - ox)} ${f(e[1] - oy)}`;
  });
  return d + 'Z';
}

const stopTags = list => list.map(([o, c]) => `<stop offset="${o}" stop-color="${c}"/>`).join('');
const restGradient = (ox, oy) => `<linearGradient id="rest" gradientUnits="userSpaceOnUse" x1="${f(REST.from[0] - ox)}" y1="${f(REST.from[1] - oy)}" x2="${f(REST.to[0] - ox)}" y2="${f(REST.to[1] - oy)}">${stopTags(REST.stops)}</linearGradient>`;
const emberGradient = (ox, oy) => `<radialGradient id="ember" gradientUnits="userSpaceOnUse" cx="${f(EMBER.centre[0] - ox)}" cy="${f(EMBER.centre[1] - oy)}" r="${EMBER.radius}">${stopTags(EMBER.stops)}</radialGradient>`;

function svg(blades, title, lit) {
  const [ox, oy] = SVG_ORIGIN;
  const defs = restGradient(ox, oy) + (lit.length ? '\n' + emberGradient(ox, oy) : '');
  const paths = blades
    .map(b => `<path id="blade-${b.id}" d="${pathData(b.corners, ox, oy)}" fill="url(#${lit.includes(b.id) ? 'ember' : 'rest'})"/>`).join('\n');
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
const plain = hex => { const c = rgb(hex); return () => c; };

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

// RGBA pixels, straight alpha. `tile` is a colour, or nothing for the bare mark.
function raster(blades, lit, size, { incident = false, tile = null } = {}) {
  const shapes = blades.map(b => shape(polygon(b.corners), lit.includes(b.id) ? emberColour : restColour));
  const view = tile ? tileView(blades) : markView(blades);
  if (incident) {
    // The dot sits in the top-left corner, the one the mark leaves empty.
    const c = [view.x + view.side * 0.125, view.y + view.side * 0.125], r = view.side * 0.115;
    const circle = Array.from({ length: 64 }, (_, i) => [c[0] + r * Math.cos(i * Math.PI / 32), c[1] + r * Math.sin(i * Math.PI / 32)]);
    shapes.unshift(shape(circle, plain(INCIDENT)));
  }
  if (tile) {
    const r = view.side * TILE_RADIUS, x0 = view.x, y0 = view.y, x1 = x0 + view.side, y1 = y0 + view.side;
    shapes.push(shape(polygon(rounded([[x0, y0, r], [x1, y0, r], [x1, y1, r], [x0, y1, r]])), plain(tile)));
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
function ico(draw) {
  const frames = ICON_SIZES.map(size => ({ size, data: size === 256 ? png(draw(size), size) : bitmapFrame(draw(size), size) }));
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

// --- The sheet --------------------------------------------------------------

// One fixed grid that shows what the app ships: the two forms, bare and on the
// tile, in the three states, from 16 to 64 px. Two switches change only how it
// is shown: the Windows mode (page colour, and the tile that goes with it) and
// the rendering (the vector drawing, or the exact pixels an .ico holds, at the
// same size). No script: the switches are radio buttons read by CSS.
function sheet(forms) {
  const sizes = [16, 20, 24, 32, 48, 64];
  const n = v => Math.round(v * 10) / 10;
  const symbols = Object.entries(forms).map(([name, blades]) => {
    const v = markView(blades);
    return `<symbol id="${name}" viewBox="${n(v.x)} ${n(v.y)} ${n(v.side)} ${n(v.side)}">`
      + blades.map(b => `<path class="${b.id}" d="${pathData(b.corners)}"/>`).join('') + `</symbol>`;
  });
  const cell = (name, state, s, tiled) => {
    const blades = forms[name], lit = STATES[state], size = `width="${s}" height="${s}"`;
    const img = (options, cls = '') => `<img ${size}${cls} alt="" src="data:image/png;base64,${png(raster(blades, lit, s, options), s).toString('base64')}">`;
    if (!tiled) return `<td><svg class="${state}" ${size}><use href="#${name}" width="100%" height="100%"/></svg>${img()}</td>`;
    const t = tileView(blades), m = markView(blades);
    return `<td><svg class="${state}" ${size} viewBox="${n(t.x)} ${n(t.y)} ${n(t.side)} ${n(t.side)}"><rect x="${n(t.x)}" y="${n(t.y)}" width="${n(t.side)}" height="${n(t.side)}" rx="${n(t.side * TILE_RADIUS)}"/><use href="#${name}" x="${n(m.x)}" y="${n(m.y)}" width="${n(m.side)}" height="${n(m.side)}"/></svg>`
      + img({ tile: TILE.dark }, ' class="dark"') + img({ tile: TILE.light }, ' class="light"') + `</td>`;
  };
  let rows = '';
  for (const state of Object.keys(STATES)) Object.keys(forms).forEach((name, fi) => [false, true].forEach((tiled, ti) => {
    const first = fi === 0 && ti === 0;
    rows += `<tr${first ? ' class="first"' : ''}>${first ? `<th rowspan="${Object.keys(forms).length * 2}">${state}</th>` : ''}${ti ? '' : `<th rowspan="2">${name}</th>`}<th>${tiled ? 'tile' : 'bare'}</th>${sizes.map(s => cell(name, state, s, tiled)).join('')}</tr>\n`;
  }));
  const group = (name, options) => `<div class="param"><span>${name}</span>` + options.map((o, i) =>
    `<input type="radio" name="${name}" id="${name}-${o}"${i ? '' : ' checked'}><label for="${name}-${o}">${o}</label>`).join('') + `</div>`;

  return `<!doctype html>
<!-- Written by logo/generate.mjs: change the generator, not this file. -->
<html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Deckle — logo sheet</title><style>
:root{--bg:#000;--fg:#fff;--line:#242424;--tile:${TILE.dark}}
body:has(#mode-light:checked){--bg:#fff;--fg:#000;--line:#e6e6e6;--tile:${TILE.light}}
body{margin:0;padding:24px;background:var(--bg);color:var(--fg);font:12px/16px "Cascadia Mono",Consolas,monospace}
h1{font-size:12px;font-weight:400;margin:0 0 20px;text-transform:uppercase;letter-spacing:.12em}
form{display:flex;gap:40px;margin-bottom:24px}
.param{display:flex;gap:16px}
.param>span{opacity:.5}
input{position:absolute;opacity:0;pointer-events:none}
label{cursor:pointer;opacity:.5;border-bottom:1px solid transparent}
label:hover{opacity:1}
input:checked+label{opacity:1;border-bottom-color:var(--fg)}
input:focus-visible+label{outline:1px solid var(--fg);outline-offset:3px}
table{border-collapse:collapse;table-layout:fixed}
col.state{width:104px}col.form{width:72px}col.backing{width:64px}
th,td{height:80px;padding:0;font-weight:400;text-align:center;vertical-align:middle}
thead th{height:32px}
th{opacity:.5;text-align:left;white-space:nowrap}
thead th:nth-child(n+4){text-align:center}
thead tr>*,tr.first>*{border-top:1px solid var(--line)}
td svg,td img{display:block;margin:auto}
td img,body:has(#rendering-pixels:checked) td svg{display:none}
body:has(#rendering-pixels:checked) td img{display:block;image-rendering:pixelated}
body:has(#rendering-pixels:checked):has(#mode-light:checked) td img.dark,
body:has(#rendering-pixels:checked):not(:has(#mode-light:checked)) td img.light{display:none}
svg{--rest:url(#rest);--a:var(--rest);--e:var(--rest)}
svg.recording{--a:url(#ember)}
svg.rewriting{--a:url(#ember);--e:url(#ember)}
rect{fill:var(--tile)}path{fill:var(--rest)}path.a{fill:var(--a)}path.e{fill:var(--e)}
</style><body><h1>Deckle — logo sheet</h1>
<svg width="0" height="0" style="position:absolute"><defs>
${restGradient(0, 0)}
${emberGradient(0, 0)}
${symbols.join('\n')}
</defs></svg>
<form>
${group('mode', ['dark', 'light'])}
${group('rendering', ['drawing', 'pixels'])}
</form>
<table>
<colgroup><col class="state"><col class="form"><col class="backing">${sizes.map(s => `<col style="width:${s + 32}px">`).join('')}</colgroup>
<thead><tr><th>state</th><th>form</th><th>backing</th>${sizes.map(s => `<th>${s}</th>`).join('')}</tr></thead>
${rows}</table>
</body></html>
`;
}

// --- Files ------------------------------------------------------------------

const normal = buildBlades(FORMS.normal), small = buildBlades(FORMS.small), five = buildBlades(FORMS.five);
const beside = name => fileURLToPath(new URL(name, import.meta.url));
const iconFile = name => fileURLToPath(new URL(`../src/Deckle.App/Assets/Icons/${name}`, import.meta.url));

for (const [state, lit] of Object.entries(STATES)) {
  const suffix = state === 'rest' ? '' : `-${state}`, title = state === 'rest' ? 'Deckle' : `Deckle, ${state}`;
  writeFileSync(beside(`deckle${suffix}.svg`), svg(normal, title, lit));
  writeFileSync(beside(`deckle-small${suffix}.svg`), svg(small, title, lit));

  // The tray: the small form, bare, so the mark takes the whole height.
  for (const incident of [false, true])
    writeFileSync(iconFile(`deckle--state--${state}${incident ? '--incident--true' : ''}.ico`), ico(size => raster(small, lit, size, { incident })));
  // A XAML title bar takes a bitmap, shown at 16 px: see IconAssets.
  writeFileSync(iconFile(`deckle--state--${state}--32px.png`), png(raster(small, lit, 32), 32));
}
// A window's icon (taskbar, Alt+Tab): the normal form on the tile of each
// Windows mode. A window shows the microphone, not the rewrite.
for (const state of ['rest', 'recording']) for (const [mode, tile] of Object.entries(TILE))
  writeFileSync(iconFile(`deckle--state--${state}--mode--${mode}.ico`), ico(size => raster(normal, STATES[state], size, { tile })));

writeFileSync(beside('deckle-five.svg'), svg(five, 'Deckle, first drawing', []));
writeFileSync(beside('sheet.html'), sheet({ normal, small }));
