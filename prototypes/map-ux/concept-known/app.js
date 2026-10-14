/* Hollow Universe galaxy map, concept Known (round 3), forked from concept AB.
   The map shows what you know: your bases, the systems you visited, what your drones scanned and the bright stars you
   can see from home (knowledge.js, MOCK). Every known star gets a shape (tiles.js); unknown space is a quiet haze with
   no points and no labels (stars.js). Drones fly out, survey and report finds. No territory, stations or factions.
   Classic script (works from file://); index.html imports three.js as a module and calls HollowMap.start().
   data.js (window.HD) does search, intents, jump counts and route planning. */
(function () {
'use strict';

// Icons: concept A's set (16 px grid), plus a few for the zoom frame.
const I = {
  fuel: '<svg viewBox="0 0 16 16"><path d="M8 2.2c2.3 3 3.6 5 3.6 6.8a3.6 3.6 0 0 1-7.2 0C4.4 7.2 5.7 5.2 8 2.2Z" fill="currentColor"/></svg>',
  station: '<svg viewBox="0 0 16 16"><rect x="4.3" y="4.3" width="7.4" height="7.4" rx="1.3" transform="rotate(45 8 8)" fill="none" stroke="currentColor" stroke-width="1.6"/><circle cx="8" cy="8" r="1.4" fill="currentColor"/></svg>',
  wrench: '<svg viewBox="0 0 16 16"><path d="M10.6 2.4a3.4 3.4 0 0 0-3.3 4.4L2.8 11.3a1.4 1.4 0 0 0 2 2l4.5-4.5a3.4 3.4 0 0 0 4.4-3.3l-2 1.1-1.7-.9-.3-1.9Z" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linejoin="round"/></svg>',
  cart: '<svg viewBox="0 0 16 16"><path d="M2 3h2l1.4 7h7l1.3-5H5" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round" stroke-linecap="round"/><circle cx="6.4" cy="13" r="1.1" fill="currentColor"/><circle cx="11.4" cy="13" r="1.1" fill="currentColor"/></svg>',
  ship: '<svg viewBox="0 0 16 16"><path d="M8 1.8 12.6 14 8 11.2 3.4 14Z" fill="currentColor"/></svg>',
  home: '<svg viewBox="0 0 16 16"><path d="M2.5 7.4 8 3l5.5 4.4V13a.5.5 0 0 1-.5.5H9.6V10H6.4v3.5H3a.5.5 0 0 1-.5-.5Z" fill="currentColor"/></svg>',
  land: '<svg viewBox="0 0 16 16"><path d="M8 2v6.5m0 0L5.6 6.1M8 8.5l2.4-2.4" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/><path d="M2 12.2c2-1.6 4-1.6 6 0s4 1.6 6 0" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"/></svg>',
  earth: '<svg viewBox="0 0 16 16"><circle cx="8" cy="8" r="5.6" fill="none" stroke="currentColor" stroke-width="1.5"/><path d="M4.4 5.2c1.4.2 2 1 1.7 2.1-.3.9.6 1.4 1.4 1.6.9.3.8 1.5.3 2.4M9.6 2.8c-.3 1 .3 1.7 1.3 1.8 1 .2 1.4 1 1.2 2" fill="none" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/></svg>',
  water: '<svg viewBox="0 0 16 16"><path d="M2 6.2c2-1.6 4-1.6 6 0s4 1.6 6 0M2 10.2c2-1.6 4-1.6 6 0s4 1.6 6 0" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"/></svg>',
  ring: '<svg viewBox="0 0 16 16"><circle cx="8" cy="8" r="3.6" fill="currentColor"/><ellipse cx="8" cy="8" rx="7" ry="2.4" transform="rotate(-18 8 8)" fill="none" stroke="currentColor" stroke-width="1.3"/></svg>',
  giant: '<svg viewBox="0 0 16 16"><circle cx="8" cy="8" r="5.8" fill="none" stroke="currentColor" stroke-width="1.5"/><path d="M2.6 6.4h10.8M2.6 9.6h10.8" stroke="currentColor" stroke-width="1.3"/></svg>',
  star: '<svg viewBox="0 0 16 16"><path d="M8 1.6c.5 3.3 1.6 5.4 6.4 6.4-4.8 1-5.9 3.1-6.4 6.4C7.5 11.1 6.4 9 1.6 8c4.8-1 5.9-3.1 6.4-6.4Z" fill="currentColor"/></svg>',
  flag: '<svg viewBox="0 0 16 16"><path d="M4 14V2.6M4 3h7.5L10 5.6l1.5 2.6H4" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round"/></svg>',
  warn: '<svg viewBox="0 0 16 16"><path d="M8 2.2 14.2 13H1.8Z" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linejoin="round"/><path d="M8 6.5v3M8 11.2v.2" stroke="currentColor" stroke-width="1.7" stroke-linecap="round"/></svg>',
  gem: '<svg viewBox="0 0 16 16"><path d="M4.6 2.8h6.8L14 6.2 8 13.6 2 6.2Z M2 6.2h12M6 2.8 8 6.2l2-3.4M8 6.2v7.4" fill="none" stroke="currentColor" stroke-width="1.3" stroke-linejoin="round"/></svg>',
  core: '<svg viewBox="0 0 16 16"><circle cx="8" cy="8" r="2.2" fill="currentColor"/><path d="M8 3.2a4.8 4.8 0 0 1 4.8 4.8M8 12.8A4.8 4.8 0 0 1 3.2 8" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"/></svg>',
  arm: '<svg viewBox="0 0 16 16"><path d="M8 8.6c-.9 0-1.3-.8-1-1.5.5-1.1 2.1-1.2 2.9-.3 1.2 1.3.5 3.4-1 4-2.2.9-4.5-.6-4.8-2.9C3.7 4.8 6.5 2.4 9.6 3c3.4.7 5 4.5 3.6 7.7" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"/></svg>',
  sector: '<svg viewBox="0 0 16 16"><path d="M8 1.8 13.6 5v6L8 14.2 2.4 11V5Z M2.4 5 8 8.2 13.6 5M8 8.2v6" fill="none" stroke="currentColor" stroke-width="1.3" stroke-linejoin="round"/></svg>',
  clock: '<svg viewBox="0 0 16 16"><circle cx="8" cy="8" r="5.8" fill="none" stroke="currentColor" stroke-width="1.5"/><path d="M8 4.8V8l2.2 1.4" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/></svg>',
  route: '<svg viewBox="0 0 16 16"><circle cx="3.6" cy="12.4" r="1.8" fill="currentColor"/><circle cx="12.4" cy="3.6" r="1.8" fill="currentColor"/><path d="M3.6 12.4c0-3 2.2-3.4 4.4-3.6 2.4-.2 4.4-1.2 4.4-5.2" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-dasharray="2.2 2.2"/></svg>',
  target: '<svg viewBox="0 0 16 16"><circle cx="8" cy="8" r="5.2" fill="none" stroke="currentColor" stroke-width="1.5"/><circle cx="8" cy="8" r="1.6" fill="currentColor"/><path d="M8 1.2v2.2M8 12.6v2.2M1.2 8h2.2M12.6 8h2.2" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/></svg>',
  fav: '<svg viewBox="0 0 16 16"><path d="m8 2.2 1.7 3.6 3.9.5-2.9 2.7.8 3.9L8 11l-3.5 1.9.8-3.9-2.9-2.7 3.9-.5Z" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linejoin="round"/></svg>',
  favOn: '<svg viewBox="0 0 16 16"><path d="m8 2.2 1.7 3.6 3.9.5-2.9 2.7.8 3.9L8 11l-3.5 1.9.8-3.9-2.9-2.7 3.9-.5Z" fill="currentColor" stroke="currentColor" stroke-width="1.4" stroke-linejoin="round"/></svg>',
  open: '<svg viewBox="0 0 16 16"><circle cx="5" cy="8" r="2.6" fill="currentColor"/><circle cx="10.6" cy="8" r="1.4" fill="currentColor"/><circle cx="14" cy="8" r="0.9" fill="currentColor"/></svg>',
  close: '<svg viewBox="0 0 16 16"><path d="m4.2 4.2 7.6 7.6m0-7.6-7.6 7.6" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/></svg>',
  back: '<svg viewBox="0 0 16 16"><path d="M10 3.2 5.2 8l4.8 4.8" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>',
  chev: '<svg viewBox="0 0 16 16"><path d="m6 3.5 4.5 4.5L6 12.5" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/></svg>',
  play: '<svg viewBox="0 0 16 16"><path d="M4.6 2.8v10.4L13 8Z" fill="currentColor"/></svg>',
  stop: '<svg viewBox="0 0 16 16"><rect x="4" y="4" width="8" height="8" rx="1.5" fill="currentColor"/></svg>',
  check: '<svg viewBox="0 0 16 16"><path d="m3.4 8.4 3 3 6.2-6.6" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>',
  system: '<svg viewBox="0 0 16 16"><circle cx="8" cy="8" r="2" fill="currentColor"/><ellipse cx="8" cy="8" rx="6.4" ry="3" transform="rotate(-24 8 8)" fill="none" stroke="currentColor" stroke-width="1.4"/></svg>',
  drone: '<svg viewBox="0 0 16 16"><path d="M8 3.4 12.6 8 8 12.6 3.4 8Z" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round"/><circle cx="8" cy="8" r="1.5" fill="currentColor"/><path d="M8 .9v1.5M8 13.6v1.5M.9 8h1.5M13.6 8h1.5" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/></svg>',
  ruin: '<svg viewBox="0 0 16 16"><path d="M1.8 13.6h12.4M3.2 13.6V7.4a4.8 4.8 0 0 1 7.3-4.1M12.8 13.6V8.6M6.1 13.6V8.4a1.9 1.9 0 0 1 3.8 0v5.2M12.4 5.2l.9-.9" fill="none" stroke="currentColor" stroke-width="1.45" stroke-linecap="round" stroke-linejoin="round"/></svg>',
  wreck: '<svg viewBox="0 0 16 16"><path d="M1.8 10.6 6.6 9.2l1.2 1.4M9.4 8.6l4.8-1.4-1.8 4.6H4.6M7.8 10.6l1.6-2M6.2 6.6l1-3.4M9.6 6.4l1.6-2.6" fill="none" stroke="currentColor" stroke-width="1.45" stroke-linecap="round" stroke-linejoin="round"/></svg>',
  unknown: '<svg viewBox="0 0 16 16"><circle cx="8" cy="8" r="6" fill="none" stroke="currentColor" stroke-width="1.4" stroke-dasharray="2.2 2.2"/><path d="M6.4 6.5a1.7 1.7 0 1 1 2.4 1.5c-.5.3-.8.6-.8 1.2M8 11v.1" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/></svg>',
  eye: '<svg viewBox="0 0 16 16"><path d="M1.6 8s2.4-4.4 6.4-4.4S14.4 8 14.4 8s-2.4 4.4-6.4 4.4S1.6 8 1.6 8Z" fill="none" stroke="currentColor" stroke-width="1.4"/><circle cx="8" cy="8" r="1.9" fill="currentColor"/></svg>',
  pause: '<svg viewBox="0 0 16 16"><rect x="4" y="3.2" width="2.6" height="9.6" rx=".8" fill="currentColor"/><rect x="9.4" y="3.2" width="2.6" height="9.6" rx=".8" fill="currentColor"/></svg>',
  ff: '<svg viewBox="0 0 16 16"><path d="M3.4 4.2 7.2 8l-3.8 3.8M8.6 4.2 12.4 8l-3.8 3.8" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/></svg>',
  rew: '<svg viewBox="0 0 16 16"><path d="M12.6 4.2 8.8 8l3.8 3.8M7.4 4.2 3.6 8l3.8 3.8" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/></svg>',
  planet: '<svg viewBox="0 0 16 16"><circle cx="8" cy="8" r="3.4" fill="currentColor"/><ellipse cx="8" cy="8" rx="7" ry="2.3" transform="rotate(-20 8 8)" fill="none" stroke="currentColor" stroke-width="1.2"/></svg>',
  dot: '<svg viewBox="0 0 16 16"><circle cx="8" cy="8" r="2" fill="currentColor"/></svg>',
  beltI: '<svg viewBox="0 0 16 16"><g fill="currentColor"><circle cx="3" cy="9" r="1"/><circle cx="6" cy="7.4" r="1"/><circle cx="9.4" cy="8.6" r="1"/><circle cx="12.8" cy="7" r="1"/></g></svg>',
};
const iconOf = id => I[id] || I.star;

window.HollowMap = { start };

function start(lib) {
  try { run(lib); }
  catch (e) {
    console.error(e);
    const l = document.getElementById('loading');
    if (l) l.innerHTML = '<span>Could not start the map: ' + String(e && e.message || e) + '</span>';
  }
}

function run({ THREE, Line2, LineMaterial, LineGeometry }) {
  const t0 = performance.now();
  const $ = (s, el) => (el || document).querySelector(s);
  const HD = window.HD;
  const L = window.HOLLOW_LOCAL, G = window.HOLLOW_GALAXY, M = window.HOLLOW_MOCK;
  if (!L || !G || !M || !HD) throw new Error('data files missing (../data/*.js and data.js)');
  if (!window.HollowStars) throw new Error('stars.js missing');
  const SYS = L.systems, N = SYS.name.length;
  const V3 = THREE.Vector3;

  // ---------- helpers ----------
  const clamp = (x, a, b) => x < a ? a : x > b ? b : x;
  const lerp = (a, b, t) => a + (b - a) * t;
  const smooth = (e0, e1, x) => { const t = clamp((x - e0) / (e1 - e0), 0, 1); return t * t * (3 - 2 * t); };
  const easeInOut = t => t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2;
  const easeOut = t => 1 - Math.pow(1 - t, 3);
  const log10 = Math.log10;
  const fmt = (v, d = 0) => v.toLocaleString('en-US', { minimumFractionDigits: d, maximumFractionDigits: d });
  const fmtLy = v => HD.ly(v);
  const esc = s => String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
  const cap = s => s ? s[0].toUpperCase() + s.slice(1) : s;
  const hashStr = s => { let h = 2166136261; for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619); } return h >>> 0; };
  const mulberry = seed => () => { seed |= 0; seed = seed + 0x6D2B79F5 | 0; let t = Math.imul(seed ^ seed >>> 15, 1 | seed); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296; };
  const bytes = b64 => { const s = atob(b64); const u = new Uint8Array(s.length); for (let i = 0; i < s.length; i++) u[i] = s.charCodeAt(i); return u; };
  const hexRGB = h => { const c = parseInt(h.slice(1), 16); return [(c >> 16 & 255) / 255, (c >> 8 & 255) / 255, (c & 255) / 255]; };
  const angDiff = (a, b) => { let d = b - a; while (d > Math.PI) d -= 2 * Math.PI; while (d < -Math.PI) d += 2 * Math.PI; return d; };
  const params = new URLSearchParams((location.hash || '').replace(/^#/, '') || (location.search || '').replace(/^\?/, ''));

  // concept A's palette: one mint accent for you, your route and what you chose; red and amber only for danger
  const ACCENT = '#5fe3c0', ACC_RGB = hexRGB(ACCENT), DANGER = '#ff6b5e', HAZARD = '#ffb05e';

  // ---------- data ----------
  const F = { SCOOP: 1, STATION: 2, LAND: 4, EARTH: 8, WATER: 16, CAT: 32, HOME: 64, PLAYER: 128, DANGER: 256, STORM: 512, RAID: 1024 };
  const PX = new Float32Array(N), PY = new Float32Array(N), PZ = new Float32Array(N);
  const MAG = new Float32Array(N), COL = new Float32Array(N * 3), FL = new Uint32Array(N);
  for (let i = 0; i < N; i++) {
    PX[i] = SYS.x[i]; PY[i] = SYS.y[i]; PZ[i] = SYS.z[i];
    let m = SYS.mag[i]; if (m >= 29) m = 11.5; MAG[i] = m;
    const c = hexRGB(SYS.col[i]); COL[3 * i] = c[0]; COL[3 * i + 1] = c[1]; COL[3 * i + 2] = c[2];
    const s = SYS.sum[i]; let f = 0;
    if (SYS.scoop[i]) f |= F.SCOOP; if (SYS.cat[i]) f |= F.CAT;
    if (s[3] > 0) f |= F.LAND; if (s[6] > 0) f |= F.EARTH; if (s[7] > 0) f |= F.WATER;
    FL[i] = f;
  }
  FL[M.home.sys] |= F.HOME;
  const playersBy = new Map();
  for (const p of M.players) playersBy.set(p.sys, p);
  const HZ = HD.DANGER, dangerOf = HD.dangerOf;   // hazard zones: only the natural ion storm is left
  for (let i = 0; i < N; i++) if (dangerOf[i] >= 0) FL[i] |= F.DANGER | F.STORM;
  const byName = new Map(); for (let i = 0; i < N; i++) byName.set(SYS.name[i].toLowerCase(), i);
  const findStar = n => { if (!n) return -1; const i = byName.get(String(n).toLowerCase()); return i === undefined ? -1 : i; };
  const SECTORS = G.sectors.list; // [name, sx, sy, sz, cx, cy, cz]
  const sectorAt = new Map(); SECTORS.forEach((s, k) => sectorAt.set(s[1] + ',' + s[2] + ',' + s[3], k));
  const HOME_SECTOR = SECTORS[0][0];
  const sectorOfPos = (x, y, z) => {
    const sx = 64 + Math.floor((x + 512) / 1024), sy = 64 + Math.floor((y + 512) / 1024), sz = 38 + Math.floor((z + 512) / 1024);
    const k = sectorAt.get(sx + ',' + sy + ',' + sz);
    return k === undefined ? -1 : k;
  };
  const shortName = i => { const n = SYS.name[i]; return (!SYS.cat[i] && n.startsWith(HOME_SECTOR + ' ')) ? n.slice(HOME_SECTOR.length + 1) : n; };
  const GC = new V3(G.meta.galacticCentre[0], G.meta.galacticCentre[1], G.meta.galacticCentre[2]);
  const refuelAt = i => HD.canRefuel[i] === 1;

  // Knowledge (MOCK, knowledge.js): what you know. 0 unknown, 1 seen, 2 scanned by a drone, 3 visited, 4 your base.
  const K = window.HOLLOW_KNOWLEDGE;
  if (!K) throw new Error('knowledge.js missing');
  const LVW = ['Unknown', 'Seen', 'Scanned', 'Visited', 'Yours'];
  const known = i => K.level[i] > 0;
  const baseAt = i => K.baseAt(i);
  const playerFound = i => K.playerFound(i);
  const findsAt = i => K.findsAt(i);
  const HOME = M.home.sys;
  const isOutpost = i => { const b = K.baseAt(i); return !!b && b.role === 'outpost'; };
  const FIND_IC = { monument: 'ruin', wreck: 'wreck', resource: 'gem', player: 'flag', fuel: 'fuel' };
  const FIND_WORD = { monument: 'Ruins', wreck: 'Wreck', resource: 'Resource', player: 'Player base', fuel: 'Fuel star' };
  const KCOL = { yours: ACCENT, visited: '#e2eaf6', scanned: '#80acff', seen: '#c8d0de', find: '#c4b5fd', player: '#ff9ccf' };
  const knowWord = i => {
    const l = K.level[i];
    if (l === 4) return i === HOME ? 'Your home' : 'Your outpost';
    return LVW[l];
  };

  const ship = {
    name: M.ship.name, sys: M.ship.sys, fuel: M.ship.fuel, max: M.ship.fuelJumps, range: M.ship.jumpRangeLy,
    pos: new V3(PX[M.ship.sys], PY[M.ship.sys], PZ[M.ship.sys]),
  };
  const syncShip = () => HD.setShip(ship.sys, ship.fuel);
  const starPos = i => new V3(PX[i], PY[i], PZ[i]);
  const distShip = i => Math.hypot(PX[i] - ship.pos.x, PY[i] - ship.pos.y, PZ[i] - ship.pos.z);

  // spatial grid (12.5 ly cells) for the jump bubble and "nearest star"
  const CELL = 12.5, OFF = 14, DIM = 28;
  const ck = v => Math.min(DIM - 1, Math.max(0, Math.floor(v / CELL) + OFF));
  const cellStart = new Int32Array(DIM * DIM * DIM + 1), cellItems = new Int32Array(N);
  {
    const key = new Int32Array(N);
    for (let i = 0; i < N; i++) { key[i] = (ck(PX[i]) * DIM + ck(PY[i])) * DIM + ck(PZ[i]); cellStart[key[i] + 1]++; }
    for (let k = 0; k < DIM * DIM * DIM; k++) cellStart[k + 1] += cellStart[k];
    const fill = cellStart.slice();
    for (let i = 0; i < N; i++) cellItems[fill[key[i]]++] = i;
  }
  function eachNear(x, y, z, r, fn) {
    const r2 = r * r, n = Math.ceil(r / CELL), cx = ck(x), cy = ck(y), cz = ck(z);
    for (let a = Math.max(0, cx - n); a <= Math.min(DIM - 1, cx + n); a++)
      for (let b = Math.max(0, cy - n); b <= Math.min(DIM - 1, cy + n); b++)
        for (let c = Math.max(0, cz - n); c <= Math.min(DIM - 1, cz + n); c++) {
          const k = (a * DIM + b) * DIM + c;
          for (let j = cellStart[k]; j < cellStart[k + 1]; j++) {
            const i = cellItems[j], dx = PX[i] - x, dy = PY[i] - y, dz = PZ[i] - z, d2 = dx * dx + dy * dy + dz * dz;
            if (d2 <= r2) fn(i, d2);
          }
        }
  }
  function nearestStar(p, maxR) { let best = -1, bd = maxR * maxR; eachNear(p.x, p.y, p.z, maxR, (i, d2) => { if (d2 < bd) { bd = d2; best = i; } }); return best; }

  // ---------- filters, favourites, recents ----------
  let reach = new Set();
  function updateReach() { reach = new Set(); eachNear(ship.pos.x, ship.pos.y, ship.pos.z, ship.range, i => { if (i !== ship.sys) reach.add(i); }); }
  updateReach();
  // The first five chips are also the legend of the tiles' colours; the rest filter what you know.
  const FILTERS = [
    { id: 'yours', name: 'Yours', color: KCOL.yours, test: i => K.level[i] === 4, rare: true, mock: true, help: 'Your home base and outposts' },
    { id: 'visited', name: 'Visited', color: KCOL.visited, test: i => K.level[i] === 3, rare: true, mock: true, help: 'Systems you have been to' },
    { id: 'scanned', name: 'Scanned', color: KCOL.scanned, test: i => K.level[i] === 2, mock: true, help: 'Systems your drones surveyed' },
    { id: 'seen', name: 'Seen', color: KCOL.seen, test: i => K.level[i] === 1, rare: true, mock: true, help: 'Bright stars you can see from home: name and colour only' },
    { id: 'finds', name: 'Finds', color: KCOL.find, test: i => findsAt(i).length > 0, rare: true, mock: true, help: 'Ruins, wrecks, resources, players and fuel stars your drones reported' },
    { id: 'reach', name: 'Reachable', color: ACCENT, test: i => reach.has(i) && known(i), rare: true, help: 'Known stars in jump range now' },
    { id: 'fuel', name: 'Fuel', color: '#f4d58d', test: i => refuelAt(i) && known(i), help: 'Known fuel stars (scoopable)' },
    { id: 'land', name: 'Landable', color: '#e2c49a', test: i => (FL[i] & F.LAND) && K.level[i] >= 2, help: 'Landable worlds where a drone or a visit has looked' },
    { id: 'earth', name: 'Earth-like', color: '#6fc3ff', test: i => (FL[i] & F.EARTH) && K.level[i] >= 2, rare: true },
  ];
  const FILTER_PRIORITY = ['finds', 'yours', 'visited', 'earth', 'reach', 'seen', 'land', 'scanned', 'fuel'];
  const activeFilters = new Set();
  const counts = {};
  function recount() { const kl = K.knownList; for (const f of FILTERS) { let c = 0; for (const i of kl) if (f.test(i)) c++; counts[f.id] = c; } }
  recount();
  const filterOf = new Int8Array(N).fill(-1); // index into FILTERS of the colour a star takes, -1 none, -2 dimmed
  const store = {
    get(k, d) { try { const v = localStorage.getItem('hollowAB.' + k); return v ? JSON.parse(v) : d; } catch (e) { return d; } },
    set(k, v) { try { localStorage.setItem('hollowAB.' + k, JSON.stringify(v)); } catch (e) { /* storage unavailable */ } },
  };
  // seeded so the empty search panel shows what recents and favourites look like (noted as mock)
  let recents = store.get('recents', [2, 7, 3, findStar('Altair')]).filter(i => i >= 0 && i < N);
  const favs = new Set(store.get('favs', [M.home.sys, 0, findStar('Vega')]).filter(i => i >= 0 && i < N));
  const addRecent = i => { recents = [i].concat(recents.filter(x => x !== i)).slice(0, 8); store.set('recents', recents); };

  // ---------- renderer ----------
  const canvas = $('#gl');
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, powerPreference: 'high-performance' });
  const DPR = Math.min(window.devicePixelRatio || 1, 2);
  renderer.setPixelRatio(DPR);
  renderer.setClearColor(0x04060a, 1);
  renderer.autoClear = false;
  const scene = new THREE.Scene();
  const camera = new THREE.PerspectiveCamera(42, 1, 0.1, 1e6);
  let W = 1, H = 1;

  // Shared point shader for the galaxy sample and the regional haze.
  const PT_VS = `
    attribute vec3 aColor; attribute float aBright; attribute float aYoung;
    uniform float uOpacity, uPx, uSize, uOld, uYoungA, uGamma;
    varying vec3 vCol; varying float vA;
    void main() {
      vec4 mv = modelViewMatrix * vec4(position, 1.0);
      gl_Position = projectionMatrix * mv;
      float b = aBright;
      float old = uOld * (0.12 + 0.88 * pow(b, uGamma));
      float yng = uYoungA * (0.3 + 0.7 * b);
      vCol = mix(aColor, vec3(0.62, 0.76, 0.98), aYoung * 0.5);
      vA = clamp(mix(old, yng, aYoung), 0.0, 1.0) * uOpacity;
      gl_PointSize = uSize * uPx * mix(0.9 + 0.6 * b, 1.25 + 0.5 * b, aYoung);
    }`;
  const PT_FS = `
    varying vec3 vCol; varying float vA;
    void main() {
      float r = length(gl_PointCoord - 0.5) * 2.0;
      float a = 1.0 - smoothstep(0.0, 1.0, r); a *= a;
      gl_FragColor = vec4(vCol, a * vA);
    }`;
  const pointMat = u => new THREE.ShaderMaterial({
    uniforms: { uOpacity: { value: 1 }, uPx: { value: DPR }, uSize: { value: u.size }, uOld: { value: u.old }, uYoungA: { value: u.young }, uGamma: { value: u.gamma } },
    vertexShader: PT_VS, fragmentShader: PT_FS, transparent: true, depthWrite: false, depthTest: false, blending: THREE.AdditiveBlending,
  });

  // Galaxy sample: 250,000 points from the density model.
  const GS = G.stars;
  const gPos = new Float32Array(bytes(GS.pos).buffer);
  const gCol = bytes(GS.col), gBright = bytes(GS.bright), gLvl = bytes(GS.lvl), gComp = bytes(GS.comp);
  const gYoung = new Uint8Array(GS.count); for (let i = 0; i < GS.count; i++) gYoung[i] = (gLvl[i] <= 1 || gComp[i] === 0) ? 255 : 0;
  const gGeo = new THREE.BufferGeometry();
  gGeo.setAttribute('position', new THREE.BufferAttribute(gPos, 3));
  gGeo.setAttribute('aColor', new THREE.BufferAttribute(gCol, 3, true));
  gGeo.setAttribute('aBright', new THREE.BufferAttribute(gBright, 1, true));
  gGeo.setAttribute('aYoung', new THREE.BufferAttribute(gYoung, 1, true));
  // concept Known: the galaxy is haze (soft, faint points), since almost all of it is unknown
  const galaxyPts = new THREE.Points(gGeo, pointMat({ size: 3.6, old: 0.03, young: 0.085, gamma: 5.0 }));
  galaxyPts.frustumCulled = false; galaxyPts.renderOrder = 1; scene.add(galaxyPts);

  // Face-on glow of the density model.
  const glowAll = bytes(G.glow.all.data), glowYoung = bytes(G.glow.young.data), GN = G.glow.size, GCELL = G.glow.cellLy;
  const decodeGlow = (arr, meta, k) => arr[k] ? Math.pow(10, meta.log10Min + (arr[k] - 1) / 254 * (meta.log10Max - meta.log10Min)) : 0;
  function glowAt(arr, meta, x, z) {
    const fx = (x - G.glow.x0) / GCELL - 0.5, fz = (z - G.glow.z0) / GCELL - 0.5;
    const ix = Math.floor(fx), iz = Math.floor(fz), tx = fx - ix, tz = fz - iz;
    const v = (a, b) => (a < 0 || b < 0 || a >= GN || b >= GN) ? 0 : decodeGlow(arr, meta, b * GN + a);
    return lerp(lerp(v(ix, iz), v(ix + 1, iz), tx), lerp(v(ix, iz + 1), v(ix + 1, iz + 1), tx), tz);
  }
  const glowTex = (() => {
    const c = document.createElement('canvas'); c.width = c.height = GN;
    const ctx = c.getContext('2d'), img = ctx.createImageData(GN, GN);
    for (let k = 0; k < GN * GN; k++) {
      const a = glowAll[k] ? (glowAll[k] - 1) / 254 : 0, y = glowYoung[k] ? (glowYoung[k] - 1) / 254 : 0;
      const wa = Math.pow(a, 2.6), wy = Math.pow(y, 2.2);
      img.data[4 * k] = 255 * Math.min(1, wa * 1.0 + wy * 0.32);
      img.data[4 * k + 1] = 255 * Math.min(1, wa * 0.8 + wy * 0.52);
      img.data[4 * k + 2] = 255 * Math.min(1, wa * 0.58 + wy * 0.95);
      img.data[4 * k + 3] = 255;
    }
    ctx.putImageData(img, 0, 0);
    const c2 = document.createElement('canvas'); c2.width = c2.height = 1024;
    const x2 = c2.getContext('2d'); x2.filter = 'blur(4px)'; x2.imageSmoothingQuality = 'high'; x2.drawImage(c, 0, 0, 1024, 1024);
    const t = new THREE.CanvasTexture(c2); t.minFilter = THREE.LinearMipmapLinearFilter; t.generateMipmaps = true; return t;
  })();
  const glowMat = new THREE.ShaderMaterial({
    uniforms: { uTex: { value: glowTex }, uOpacity: { value: 1 } },
    vertexShader: 'varying vec2 vUv; void main(){ vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position,1.0); }',
    fragmentShader: 'uniform sampler2D uTex; uniform float uOpacity; varying vec2 vUv; void main(){ vec3 c = texture2D(uTex, vUv).rgb; gl_FragColor = vec4(c * uOpacity, 1.0); }',
    transparent: true, depthWrite: false, depthTest: false, blending: THREE.AdditiveBlending, side: THREE.DoubleSide,
  });
  const glowPlane = new THREE.Mesh(new THREE.PlaneGeometry(GN * GCELL, GN * GCELL), glowMat);
  glowPlane.rotation.x = -Math.PI / 2;
  glowPlane.position.set(G.glow.x0 + GN * GCELL / 2, -45, G.glow.z0 + GN * GCELL / 2);
  glowPlane.renderOrder = 0; glowPlane.frustumCulled = false; scene.add(glowPlane);

  // Regional haze around Sol: points in proportion to the density model (a picture of density, not stars).
  const haze = (() => {
    const rnd = mulberry(0x48ab17), HALF = 9600, STEP = 120, n = Math.round(2 * HALF / STEP);
    const build = (arr, meta, pow) => {
      const w = new Float64Array(n * n); let tot = 0;
      for (let b = 0; b < n; b++) for (let a = 0; a < n; a++) {
        const x = -HALF + (a + 0.5) * STEP, z = -HALF + (b + 0.5) * STEP;
        tot += Math.pow(glowAt(arr, meta, x, z), pow); w[b * n + a] = tot;
      }
      return { w, tot };
    };
    const A = build(glowAll, G.glow.all, 1.6), Y = build(glowYoung, G.glow.young, 1.35);
    const COUNT = 70000, YOUNG = 0.3;
    const pos = new Float32Array(COUNT * 3), col = new Uint8Array(COUNT * 3), br = new Uint8Array(COUNT), yg = new Uint8Array(COUNT);
    const gauss = () => { let u = 0; while (u === 0) u = rnd(); const v = rnd(); return Math.sqrt(-2 * Math.log(u)) * Math.cos(2 * Math.PI * v); };
    for (let k = 0; k < COUNT; k++) {
      const young = rnd() < YOUNG, T = young ? Y : A;
      const r = rnd() * T.tot; let lo = 0, hi = n * n - 1;
      while (lo < hi) { const mid = (lo + hi) >> 1; if (T.w[mid] < r) lo = mid + 1; else hi = mid; }
      const a = lo % n, b = (lo / n) | 0;
      const x = -HALF + (a + rnd()) * STEP, z = -HALF + (b + rnd()) * STEP;
      const mid = -68 + z * (47 / 26673);
      pos[3 * k] = x; pos[3 * k + 1] = mid + gauss() * (young ? 130 : 320); pos[3 * k + 2] = z;
      if (young) { col[3 * k] = 170 + rnd() * 40; col[3 * k + 1] = 200 + rnd() * 30; col[3 * k + 2] = 255; }
      else { const s = (rnd() * N) | 0; col[3 * k] = COL[3 * s] * 255; col[3 * k + 1] = COL[3 * s + 1] * 255; col[3 * k + 2] = COL[3 * s + 2] * 255; }
      br[k] = 255 * Math.pow(rnd(), 2.2); yg[k] = young ? 255 : 0;
    }
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    geo.setAttribute('aColor', new THREE.BufferAttribute(col, 3, true));
    geo.setAttribute('aBright', new THREE.BufferAttribute(br, 1, true));
    geo.setAttribute('aYoung', new THREE.BufferAttribute(yg, 1, true));
    const p = new THREE.Points(geo, pointMat({ size: 1.8, old: 0.95, young: 1.0, gamma: 0.8 }));
    p.frustumCulled = false; p.renderOrder = 1; scene.add(p); return p;
  })();

  // Sectors: 1,024-ly cubes; the layer through Sol drawn as soft named tiles on the midplane.
  const SECTOR_Y = -68;
  const sectorMat = new THREE.ShaderMaterial({
    uniforms: { uOpacity: { value: 0 }, uHover: { value: new THREE.Vector2(999, 999) }, uCur: { value: new THREE.Vector2(0, 0) }, uSel: { value: new THREE.Vector2(999, 999) }, uAcc: { value: new THREE.Color(ACCENT) } },
    vertexShader: 'varying vec2 vXZ; void main(){ vec4 w = modelMatrix * vec4(position,1.0); vXZ = w.xz; gl_Position = projectionMatrix * viewMatrix * w; }',
    fragmentShader: `
      uniform float uOpacity; uniform vec2 uHover, uCur, uSel; uniform vec3 uAcc; varying vec2 vXZ;
      void main() {
        vec2 g = (vXZ + 512.0) / 1024.0; vec2 cell = floor(g); vec2 f = fract(g);
        vec2 q = abs(f - 0.5) * 1024.0 - (512.0 - 34.0 - 150.0);
        float rd = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - 150.0;
        float px = fwidth(rd);
        float border = 1.0 - smoothstep(0.0, px * 1.6, abs(rd));
        float fill = 1.0 - smoothstep(-260.0, 0.0, rd);
        float hov = 1.0 - step(0.5, length(cell - uHover));
        float cur = 1.0 - step(0.5, length(cell - uCur));
        float sel = 1.0 - step(0.5, length(cell - uSel));
        float fade = 1.0 - smoothstep(2400.0, 4300.0, length(vXZ));
        float hi = max(max(hov * 0.7, cur * 0.6), sel);
        float a = border * (0.15 + 0.5 * hi) + fill * (0.016 + 0.06 * hi);
        vec3 c = mix(vec3(0.80, 0.84, 0.90), uAcc, hi);
        gl_FragColor = vec4(c, a * fade * uOpacity);
      }`,
    transparent: true, depthWrite: false, depthTest: false, side: THREE.DoubleSide,
  });
  const sectorPlane = new THREE.Mesh(new THREE.PlaneGeometry(10240, 10240), sectorMat);
  sectorPlane.rotation.x = -Math.PI / 2; sectorPlane.position.y = SECTOR_Y; sectorPlane.renderOrder = 2; sectorPlane.frustumCulled = false;
  scene.add(sectorPlane);
  const regionSectors = SECTORS.map((s, k) => ({ k, s })).filter(o => o.s[2] === 64);

  // Spiral arms as soft ribbons on the midplane (Region level), named along their path.
  const ARMS = G.armPaths.map(p => {
    const pts = p.line.map(q => new V3(q[0], q[1], q[2]));
    const wts = p.line.map(q => q[3]);
    const curve = new THREE.CatmullRomCurve3(pts, false, 'centripetal');
    const S = Math.max(60, pts.length * 8), width = p.id === 'orion-spur' ? 1500 : 2600;
    const pos = [], edge = [], wt = [], idx = [], samples = [];
    for (let j = 0; j <= S; j++) {
      const t = j / S, c = curve.getPoint(t), tan = curve.getTangent(t);
      const nx = -tan.z, nz = tan.x, nl = Math.hypot(nx, nz) || 1;
      const fi = t * (wts.length - 1), i0 = Math.floor(fi), w = lerp(wts[i0], wts[Math.min(wts.length - 1, i0 + 1)], fi - i0);
      pos.push(c.x + nx / nl * width / 2, c.y, c.z + nz / nl * width / 2, c.x - nx / nl * width / 2, c.y, c.z - nz / nl * width / 2);
      edge.push(1, -1); wt.push(w, w); samples.push({ p: c, w });
      if (j < S) { const a = 2 * j; idx.push(a, a + 1, a + 2, a + 1, a + 3, a + 2); }
    }
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    geo.setAttribute('aEdge', new THREE.Float32BufferAttribute(edge, 1));
    geo.setAttribute('aW', new THREE.Float32BufferAttribute(wt, 1));
    geo.setIndex(idx);
    const mat = new THREE.ShaderMaterial({
      uniforms: { uOpacity: { value: 0 }, uColor: { value: new THREE.Color(0.48, 0.64, 0.95) } },
      vertexShader: 'attribute float aEdge; attribute float aW; varying float vE; varying float vW; void main(){ vE = aEdge; vW = aW; gl_Position = projectionMatrix * modelViewMatrix * vec4(position,1.0); }',
      fragmentShader: 'uniform vec3 uColor; uniform float uOpacity; varying float vE; varying float vW; void main(){ float a = exp(-vE*vE*3.2) * vW * uOpacity; gl_FragColor = vec4(uColor, a); }',
      transparent: true, depthWrite: false, depthTest: false, blending: THREE.AdditiveBlending, side: THREE.DoubleSide,
    });
    const mesh = new THREE.Mesh(geo, mat); mesh.renderOrder = 1; mesh.frustumCulled = false; scene.add(mesh);
    return { id: p.id, name: p.name, mesh, mat, samples, hi: 0 };
  });
  const armLabelName = (arm, p) => arm.id !== 'norma-outer' ? arm.name : (Math.hypot(p.x - GC.x, p.z - GC.z) < 30000 ? 'Norma Arm' : 'Outer Arm');
  function armOf(p) {
    let best = null, bd = Infinity;
    for (const a of ARMS) for (const s of a.samples) {
      const d = Math.hypot(s.p.x - p.x, s.p.z - p.z) / Math.max(0.25, s.w);
      if (d < bd) { bd = d; best = { arm: a, at: s.p }; }
    }
    return best ? armLabelName(best.arm, best.at) : 'Milky Way disk';
  }
  const STRUCTS = G.structures;

  // ---------- the star field (stars.js) ----------
  const field = window.HollowStars.create({ THREE, scene, dpr: DPR, stars: { n: N, x: PX, y: PY, z: PZ, mag: MAG, col: COL, cat: SYS.cat } });
  let listSet = null; // stars of the current "Ask the map" list
  // what matters most in a tile (Tab order): yours, finds, visited, named stars, landable worlds, near you
  const importance = new Float32Array(N);
  function updateImportance() {
    for (let i = 0; i < N; i++) {
      let w = HD.isProper[i] ? 0.5 : SYS.cat[i] ? 0.2 : 0;
      w += K.level[i] * 0.15;
      if (findsAt(i).length) w += 0.8;
      if (baseAt(i)) w += 0.9;
      const l = SYS.sum[i][3]; if (l) w += 0.12 + 0.04 * Math.min(l, 5);
      const d = distShip(i); if (d < 60) w += 0.4 * (1 - d / 60);
      importance[i] = w;
    }
    field.setImportance(importance);
  }
  updateImportance();
  function restyle() {
    const act = FILTER_PRIORITY.filter(id => activeFilters.has(id)).map(id => FILTERS.find(f => f.id === id));
    for (let i = 0; i < N; i++) {
      let mode = 0, c = null, fi = -1;
      if (listSet) { if (listSet.has(i)) { mode = 1; c = ACC_RGB; } else mode = 2; }
      else if (act.length) {
        mode = 2;
        for (const f of act) if (f.test(i)) { mode = 1; c = f.rgb || (f.rgb = hexRGB(f.color)); fi = FILTERS.indexOf(f); break; }
      }
      filterOf[i] = mode === 2 ? -2 : fi;
      field.setStyle(i, mode, c);
    }
    field.commit(); tiles.dirty();
    dangerMeshes.forEach(d => { d.boost = list && list.danger ? 1 : 0; });
  }
  function applyFilters() { restyle(); renderChips(); updateHash(); }

  // Polar distance grid around the ship (Neighbourhood level).
  const gridMat = new THREE.ShaderMaterial({
    uniforms: { uOpacity: { value: 0 } },
    vertexShader: 'varying vec2 vP; void main(){ vP = position.xy; gl_Position = projectionMatrix * modelViewMatrix * vec4(position,1.0); }',
    fragmentShader: `
      uniform float uOpacity; varying vec2 vP;
      float ln(float v, float w) { float fw = fwidth(v); return 1.0 - smoothstep(0.0, fw * w, abs(v)); }
      void main() {
        float r = length(vP);
        float rings = max(max(ln(r - 25.0, 1.3), ln(r - 50.0, 1.3)), max(ln(r - 100.0, 1.3), ln(r - 150.0, 1.3)));
        rings = max(rings, ln(r - 10.0, 1.1) * 0.7);
        float axes = max(ln(vP.x, 1.0), ln(vP.y, 1.0)) * smoothstep(6.0, 14.0, r) * 0.45;
        float fade = 1.0 - smoothstep(105.0, 158.0, r);
        float a = (rings * 0.36 + axes * 0.26) * fade * uOpacity;
        gl_FragColor = vec4(0.80, 0.85, 0.92, a);
      }`,
    transparent: true, depthWrite: false, depthTest: false, side: THREE.DoubleSide,
  });
  const grid = new THREE.Mesh(new THREE.CircleGeometry(160, 160), gridMat);
  grid.rotation.x = -Math.PI / 2; grid.renderOrder = 2; grid.frustumCulled = false; scene.add(grid);

  // Tiles (tiles.js): every known star owns a cell; cells merge into tiles (at most about 200 on screen).
  // Places that matter keep a tile of their own: yours, visited, finds, the selection, the target and the route.
  const pinnedSet = new Set();
  function updatePinned() {
    pinnedSet.clear();
    for (const i of K.knownList) if (K.level[i] >= 3 || findsAt(i).length) pinnedSet.add(i);
    if (sel && sel.t === 'star') pinnedSet.add(sel.i);
    if (targetSys >= 0) pinnedSet.add(targetSys);
    const rs = activeRoute(); if (rs) rs.stops.forEach(q => pinnedSet.add(q.sys));
    if (listSet) for (const i of listSet) pinnedSet.add(i);
  }
  const tiles = window.HollowTiles.create({ canvas: $('#cells'), dpr: DPR, stars: { x: PX, y: PY, z: PZ }, K, pinned: i => pinnedSet.has(i) });
  const fxCanvas = $('#fx'), fx = fxCanvas.getContext('2d');

  // Fresnel shells: the jump-range bubble and the (mock) danger zones.
  const shellMat = (rgb, base, rim) => new THREE.ShaderMaterial({
    uniforms: { uColor: { value: new THREE.Color(rgb[0], rgb[1], rgb[2]) }, uOpacity: { value: 0 }, uBase: { value: base }, uRim: { value: rim } },
    vertexShader: 'varying vec3 vN; varying vec3 vV; void main(){ vec4 w = modelMatrix * vec4(position,1.0); vN = normalize(mat3(modelMatrix) * normal); vV = normalize(cameraPosition - w.xyz); gl_Position = projectionMatrix * viewMatrix * w; }',
    fragmentShader: 'uniform vec3 uColor; uniform float uOpacity, uBase, uRim; varying vec3 vN; varying vec3 vV; void main(){ float f = 1.0 - abs(dot(normalize(vN), normalize(vV))); float a = (pow(f, 3.0) * uRim + uBase) * uOpacity; gl_FragColor = vec4(uColor, a); }',
    transparent: true, depthWrite: false, depthTest: false, side: THREE.FrontSide, blending: THREE.AdditiveBlending,
  });
  const sphereGeo = new THREE.SphereGeometry(1, 64, 40);
  const bubble = new THREE.Mesh(sphereGeo, shellMat(ACC_RGB, 0.02, 0.42));
  bubble.scale.setScalar(ship.range); bubble.renderOrder = 3; scene.add(bubble);
  const circlePts = (r, n = 128) => { const a = []; for (let k = 0; k <= n; k++) { const t = k / n * Math.PI * 2; a.push(new V3(Math.cos(t) * r, 0, Math.sin(t) * r)); } return a; };
  const unitCircle = new THREE.BufferGeometry().setFromPoints(circlePts(1, 360).slice(0, -1));
  const bubbleRing = new THREE.Line(new THREE.BufferGeometry().setFromPoints(circlePts(1)), new THREE.LineBasicMaterial({ color: new THREE.Color(ACCENT), transparent: true, opacity: 0, depthTest: false }));
  bubbleRing.scale.setScalar(ship.range); bubbleRing.renderOrder = 3; scene.add(bubbleRing);
  const dangerMeshes = HZ.map(d => {
    const m = new THREE.Mesh(sphereGeo, shellMat(hexRGB(d.kind === 'hazard' ? HAZARD : DANGER), 0.03, 0.4));
    m.position.set(d.centre[0], d.centre[1], d.centre[2]); m.scale.setScalar(d.radiusLy); m.renderOrder = 3; scene.add(m);
    return { d, m, boost: 0 };
  });

  // Drop lines: the height of the hovered and selected star above the ship's plane.
  const mkDrop = (color, op) => {
    const g = new THREE.BufferGeometry(); g.setAttribute('position', new THREE.BufferAttribute(new Float32Array(6), 3));
    const l = new THREE.Line(g, new THREE.LineBasicMaterial({ color, transparent: true, opacity: op, depthTest: false }));
    const foot = new THREE.Line(new THREE.BufferGeometry().setFromPoints(circlePts(1, 48)), new THREE.LineBasicMaterial({ color, transparent: true, opacity: op, depthTest: false }));
    l.renderOrder = foot.renderOrder = 5; l.frustumCulled = foot.frustumCulled = false; scene.add(l); scene.add(foot);
    return { l, foot, op };
  };
  const dropSel = mkDrop(new THREE.Color(ACCENT), 0.5), dropHov = mkDrop(0xffffff, 0.3);
  function setDrop(dr, i, alpha) {
    const vis = i >= 0 && alpha > 0.01;
    dr.l.visible = dr.foot.visible = vis; if (!vis) return;
    const a = dr.l.geometry.attributes.position.array;
    a[0] = PX[i]; a[1] = PY[i]; a[2] = PZ[i]; a[3] = PX[i]; a[4] = ship.pos.y; a[5] = PZ[i];
    dr.l.geometry.attributes.position.needsUpdate = true;
    const fs = Math.max(0.35, Math.pow(10, cam.cur.ld) * 0.012);
    dr.foot.position.set(PX[i], ship.pos.y, PZ[i]); dr.foot.scale.setScalar(fs);
    dr.l.material.opacity = dr.foot.material.opacity = dr.op * alpha;
  }

  // Route (concept A's look): a softly curved mint line with a glow, stop markers and a travelling comet.
  let routeCtx = null, routeGroup = null, routeCurve = null, stopPts = null, comet = null, cometT = 0;
  const routeLines = [];
  const stopMat = new THREE.ShaderMaterial({
    uniforms: { uPx: { value: DPR }, uAcc: { value: new THREE.Color(ACCENT) }, uDan: { value: new THREE.Color(DANGER) }, uOpacity: { value: 1 } },
    vertexShader: `attribute float aKind; attribute float aDone; uniform float uPx; varying float vK; varying float vD;
      void main(){ vK = aKind; vD = aDone; gl_Position = projectionMatrix * modelViewMatrix * vec4(position,1.0);
        float s = aKind == 0.0 ? 9.0 : aKind == 5.0 ? 8.0 : aKind == 2.0 ? 17.0 : 13.0; gl_PointSize = s * uPx; }`,
    fragmentShader: `uniform vec3 uAcc; uniform vec3 uDan; uniform float uOpacity; varying float vK; varying float vD;
      void main(){ float r = length(gl_PointCoord - 0.5) * 2.0; if (r > 1.0) discard;
        vec3 ring = vK == 4.0 ? uDan : vK == 5.0 ? vec3(0.62, 0.66, 0.74) : uAcc;
        float edge = smoothstep(1.0, 0.82, r);
        float inner = smoothstep(0.62, 0.5, r);
        vec3 fill = vK == 1.0 ? vec3(1.0) : vK == 2.0 ? uAcc : vec3(0.03, 0.05, 0.07);
        if (vK == 3.0) fill = vec3(1.0);
        vec3 c = mix(ring, fill, inner);
        if (vD > 0.5) c = mix(c, vec3(0.35), 0.6);
        gl_FragColor = vec4(c, edge * uOpacity); }`,
    transparent: true, depthTest: false, depthWrite: false,
  });
  const cometMat = new THREE.ShaderMaterial({
    uniforms: { uPx: { value: DPR }, uAcc: { value: new THREE.Color(ACCENT) }, uA: { value: 1 } },
    vertexShader: 'uniform float uPx; void main(){ gl_Position = projectionMatrix * modelViewMatrix * vec4(position,1.0); gl_PointSize = 22.0 * uPx; }',
    fragmentShader: 'uniform vec3 uAcc; uniform float uA; void main(){ float r = length(gl_PointCoord - 0.5) * 2.0; if (r > 1.0) discard; float a = pow(1.0 - r, 3.0); gl_FragColor = vec4(mix(uAcc, vec3(1.0), a * 0.7), a * uA); }',
    transparent: true, depthTest: false, depthWrite: false, blending: THREE.AdditiveBlending,
  });
  const activeRoute = () => routeCtx ? routeCtx.active : null;
  function drawRoute() {
    if (routeGroup) {
      scene.remove(routeGroup);
      routeGroup.traverse(o => { if (o.geometry) o.geometry.dispose(); if (o.material && o.material !== stopMat && o.material !== cometMat) o.material.dispose(); });
      routeGroup = null; routeCurve = null; stopPts = null; comet = null; routeLines.length = 0;
    }
    const r = activeRoute(); if (!r) return;
    routeGroup = new THREE.Group();
    const raw = r.stops.map(s => starPos(s.sys));
    routeCurve = raw.length > 2 ? new THREE.CatmullRomCurve3(raw, false, 'centripetal', 0.2) : new THREE.LineCurve3(raw[0], raw[1]);
    // known stretches are A's mint line; stretches through unknown space are a quiet dashed line
    const nSeg = raw.length - 1, per = 14;
    const segKnown = k => known(r.stops[k].sys) && known(r.stops[k + 1].sys);
    const mk = (pts, w, op, dashed) => {
      const g = new LineGeometry(); g.setPositions(pts);
      const m = new LineMaterial({ color: new THREE.Color(dashed ? 0xc9d3e2 : ACCENT), linewidth: w, transparent: true, opacity: op, depthTest: false, depthWrite: false, blending: THREE.AdditiveBlending,
        dashed: !!dashed, dashSize: dashed ? dashed : 1, gapSize: dashed ? dashed * 0.8 : 1 });
      m.resolution.set(W, H);
      const l = new Line2(g, m); if (dashed) l.computeLineDistances();
      l.renderOrder = 5; l.frustumCulled = false; l.userData.base = op; routeLines.push(l); return l;
    };
    for (let k0 = 0; k0 < nSeg;) {
      const kn = segKnown(k0); let k1 = k0;
      while (k1 + 1 < nSeg && segKnown(k1 + 1) === kn) k1++;
      const pts = [];
      for (let q = k0 * per; q <= (k1 + 1) * per; q++) { const p = routeCurve.getPoint(q / (nSeg * per)); pts.push(p.x, p.y, p.z); }
      if (kn) routeGroup.add(mk(pts, 16, 0.07), mk(pts, 7, 0.16), mk(pts, 2.6, 0.95));
      else routeGroup.add(mk(pts, 7, 0.05), mk(pts, 2, 0.75, 1.1));
      k0 = k1 + 1;
    }
    if (r.far) {   // beyond the 150 ly of star data: a straight dashed line on to the far target, all of it unknown
      const a0 = raw[raw.length - 1], b0 = r.far.p, len = a0.distanceTo(b0);
      const fl = mk([a0.x, a0.y, a0.z, b0.x, b0.y, b0.z], 2, 0.7, Math.max(1.1, len / 90)); fl.userData.far = true; routeGroup.add(fl);
    }
    const n = r.stops.length, sp = new Float32Array(n * 3), sk = new Float32Array(n);
    r.stops.forEach((s, k) => {
      sp[3 * k] = PX[s.sys]; sp[3 * k + 1] = PY[s.sys]; sp[3 * k + 2] = PZ[s.sys];
      sk[k] = k === 0 ? 3 : k === n - 1 && !r.far ? 2 : !known(s.sys) ? 5 : s.danger >= 0 ? 4 : s.refuel ? 1 : 0;
    });
    const sg = new THREE.BufferGeometry();
    sg.setAttribute('position', new THREE.BufferAttribute(sp, 3)); sg.setAttribute('aKind', new THREE.BufferAttribute(sk, 1));
    sg.setAttribute('aDone', new THREE.BufferAttribute(new Float32Array(n), 1).setUsage(THREE.DynamicDrawUsage));
    stopPts = new THREE.Points(sg, stopMat); stopPts.renderOrder = 6; stopPts.frustumCulled = false; routeGroup.add(stopPts);
    const cg = new THREE.BufferGeometry(); cg.setAttribute('position', new THREE.BufferAttribute(new Float32Array(3), 3));
    comet = new THREE.Points(cg, cometMat); comet.renderOrder = 7; comet.frustumCulled = false; routeGroup.add(comet);
    cometT = 0;
    scene.add(routeGroup);
  }
  function setRouteProgress(k) {
    if (!stopPts) return;
    const a = stopPts.geometry.getAttribute('aDone');
    for (let i = 0; i < a.count; i++) a.array[i] = i < k ? 1 : 0;
    a.needsUpdate = true;
  }

  // ---------- System level, Realistic: its own scene, log-scaled orbits ----------
  const sysScene = new THREE.Scene();
  const sysCam = new THREE.PerspectiveCamera(36, 1, 0.1, 20000);
  sysScene.add(new THREE.AmbientLight(0xffffff, 0.16));
  const sysLight = new THREE.PointLight(0xffffff, 2.8, 0, 0); sysScene.add(sysLight);
  const veilScene = new THREE.Scene(), veilCam = new THREE.OrthographicCamera(-1, 1, 1, -1, 0, 1);
  const veilMat = new THREE.ShaderMaterial({ uniforms: { uA: { value: 0 }, uC: { value: new THREE.Color(0.016, 0.024, 0.04) } }, vertexShader: 'void main(){ gl_Position = vec4(position.xy, 0.0, 1.0); }', fragmentShader: 'uniform float uA; uniform vec3 uC; void main(){ gl_FragColor = vec4(uC, uA); }', transparent: true, depthTest: false, depthWrite: false });
  const VEIL_DARK = new THREE.Color(0.016, 0.024, 0.04), VEIL_NAVY = new THREE.Color(0.036, 0.05, 0.078);
  veilScene.add(new THREE.Mesh(new THREE.PlaneGeometry(2, 2), veilMat));
  const radialTex = (() => {
    const c = document.createElement('canvas'); c.width = c.height = 256; const x = c.getContext('2d');
    const g = x.createRadialGradient(128, 128, 0, 128, 128, 128);
    g.addColorStop(0, 'rgba(255,255,255,1)'); g.addColorStop(0.08, 'rgba(255,255,255,0.85)'); g.addColorStop(0.25, 'rgba(255,255,255,0.28)');
    g.addColorStop(0.55, 'rgba(255,255,255,0.07)'); g.addColorStop(1, 'rgba(255,255,255,0)');
    x.fillStyle = g; x.fillRect(0, 0, 256, 256); return new THREE.CanvasTexture(c);
  })();
  const BODY_COL = { 'Gas giant': 0xd8b38a, 'Ice giant': 0x9ccfe3, 'Rocky world': 0xa69585, 'Rocky ice world': 0xaab5c2, 'Icy world': 0xdde8f3,
    'Earth-like world': 0x5aa889, 'Water world': 0x3f7ccc, 'Ammonia world': 0xbfbd78, 'Metal-rich world': 0x8d8884, 'Asteroid belt': 0x9a8f84, Moon: 0xa7a39e };
  const CLASS_COL = { O: '#9db4ff', B: '#aabfff', A: '#cad8ff', F: '#f6f4ff', G: '#fff2e0', K: '#ffd2a1', M: '#ffb37a', D: '#e6edff', L: '#d0784e', T: '#b0566a', Y: '#8d4a6b', N: '#c8d8ff', W: '#9db4ff', C: '#ff9a6a' };
  const classCol = cls => CLASS_COL[(cls || 'G')[0]] || '#fff2e0';
  const starKind = cls => cls ? HD.starKind(cls) : 'Star';
  let SY = null;
  const sphere = new THREE.SphereGeometry(1, 48, 32);
  const sysFocal = () => H / (2 * Math.tan(sysCam.fov * Math.PI / 360));
  function schematicBodies(i) {
    const s = SYS.sum[i], nm = SYS.name[i];
    const [nst, npl, nmo, nland, ngas, nice, nearth, nwater, nring, nbelt] = s;
    const out = [[nm, 0, SYS.cls[i], -1, 0, 0, 0, SYS.teff[i], 'none', 0, 0]];
    const rocky = Math.max(0, npl - ngas - nice - nearth - nwater), inner = Math.ceil(rocky / 2), types = [];
    for (let k = 0; k < inner; k++) types.push('Rocky world');
    for (let k = 0; k < nearth; k++) types.push('Earth-like world');
    for (let k = 0; k < nwater; k++) types.push('Water world');
    for (let k = 0; k < rocky - inner; k++) types.push('Rocky ice world');
    for (let k = 0; k < ngas; k++) types.push('Gas giant');
    for (let k = 0; k < nice; k++) types.push('Ice giant');
    let landLeft = nland, ringLeft = nring;
    types.forEach((t, k) => {
      const giant = /giant/.test(t), land = !giant && landLeft > 0 ? (landLeft--, 1) : 0;
      const ring = giant && ringLeft > 0 ? (ringLeft--, 1) : 0;
      out.push([nm + ' ' + (k + 1), 1, t, 0, 0.32 * Math.pow(1.75, k), giant ? 42000 : 5200, 0, 0, '', land, ring]);
    });
    for (let k = 1; k < nst; k++) out.push([nm + ' ' + String.fromCharCode(65 + k), 0, '', 0, 0.32 * Math.pow(1.75, types.length + k), 300000, 0, 0, '', 0, 0]);
    for (let k = 0; k < nbelt; k++) out.push([nm + ' belt ' + (k + 1), 3, 'Asteroid belt', 0, 0.32 * Math.pow(1.75, 1.5 + k * 2.2), 0, 0, 0, '', 0, 0]);
    const hosts = []; out.forEach((b, k) => { if (b[1] === 1) hosts.push(k); });
    hosts.sort((a, b) => out[b][5] - out[a][5]);
    for (let k = 0; k < nmo && hosts.length; k++) {
      const h = hosts[k % hosts.length];
      out.push([out[h][0] + ' ' + String.fromCharCode(97 + Math.floor(k / hosts.length)), 2, 'Moon', h, 0.002 * (1 + k), 900, 0, 0, '', landLeft > 0 ? (landLeft--, 1) : 0, 0]);
    }
    return out;
  }
  // what you know about a system's worlds: nothing until a drone or a visit has looked (level 2 and up)
  const bodiesOf = i => {
    if (K.level[i] < 2) return { bodies: [[SYS.name[i], 0, SYS.cls[i], -1, 0, 0, 0, SYS.teff[i], 'none', 0, 0]], schematic: true, unknownWorlds: true };
    const raw = L.bodies[String(i)]; return raw ? { bodies: raw, schematic: false } : { bodies: schematicBodies(i), schematic: true };
  };

  // ---- Planet textures (Realistic view): small procedural maps by world type, seeded by the body's name ----
  function makeNoise(seed) {
    const r = mulberry(seed), perm = [];
    for (let k = 0; k < 256; k++) perm.push(k);
    for (let k = 255; k > 0; k--) { const j = Math.floor(r() * (k + 1)); const t = perm[k]; perm[k] = perm[j]; perm[j] = t; }
    const P = new Uint8Array(512); for (let k = 0; k < 512; k++) P[k] = perm[k & 255];
    const V = new Float32Array(256); for (let k = 0; k < 256; k++) V[k] = r();
    const n3 = (x, y, z) => {
      const X = Math.floor(x), Y = Math.floor(y), Z = Math.floor(z), fx = x - X, fy = y - Y, fz = z - Z;
      const u = fx * fx * (3 - 2 * fx), v = fy * fy * (3 - 2 * fy), w = fz * fz * (3 - 2 * fz);
      const x0 = X & 255, x1 = (X + 1) & 255, y0 = Y & 255, y1 = (Y + 1) & 255, z0 = Z & 255, z1 = (Z + 1) & 255;
      const h = (a, b, c) => V[P[P[P[a] + b] + c]];
      const l = (p, q, t) => p + (q - p) * t;
      return l(l(l(h(x0, y0, z0), h(x1, y0, z0), u), l(h(x0, y1, z0), h(x1, y1, z0), u), v),
               l(l(h(x0, y0, z1), h(x1, y0, z1), u), l(h(x0, y1, z1), h(x1, y1, z1), u), v), w);
    };
    return (x, y, z, oct) => { let s = 0, a = 0.5, f = 1, n = 0; for (let k = 0; k < oct; k++) { s += a * n3(x * f, y * f, z * f); n += a; a *= 0.5; f *= 2.03; } return s / n; };
  }
  const mixc = (a, b, t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];
  const sat = t => t < 0 ? 0 : t > 1 ? 1 : t;
  function paintPlanet(type, name, w, h) {
    const seed = hashStr(name), nz = makeNoise(seed), rnd = mulberry(seed ^ 0x5bd1);
    const c = document.createElement('canvas'); c.width = w; c.height = h;
    const g = c.getContext('2d'), img = g.createImageData(w, h), d = img.data;
    const shift = rnd() * 0.16 - 0.08, ox = rnd() * 50, oy = rnd() * 50;
    for (let yy = 0; yy < h; yy++) {
      const lat = (0.5 - (yy + 0.5) / h) * Math.PI, cl = Math.cos(lat), sl = Math.sin(lat), al = Math.abs(lat);
      for (let xx = 0; xx < w; xx++) {
        const lon = (xx + 0.5) / w * Math.PI * 2, px = cl * Math.cos(lon), py = sl, pz = cl * Math.sin(lon);
        let col;
        if (type === 'Earth-like world' || type === 'Water world') {
          const e = nz(px * 1.9 + ox, py * 1.9, pz * 1.9 + oy, 5), sea = type === 'Water world' ? 0.66 : 0.53;
          if (e > sea) col = mixc([74, 112, 62], [168, 148, 104], sat((e - sea) * 7 + al * 0.5 - 0.2));
          else col = mixc([16, 42, 96], [38, 96, 156], sat((e - 0.3) * 3.4));
          if (al > 1.12 + (nz(px * 4, py * 4, pz * 4, 2) - 0.5) * 0.5) col = [236, 242, 248];
          const cloud = nz(px * 3.2 + 9, py * 5.5, pz * 3.2 + 3, 4);
          if (cloud > 0.56) col = mixc(col, [244, 247, 252], sat((cloud - 0.56) * 3.2) * 0.85);
        } else if (type === 'Gas giant' || type === 'Ammonia world') {
          const t = Math.sin(lat * 11 + (nz(px * 2.2, py * 2.2 + ox, pz * 2.2, 4) - 0.5) * 6) * 0.5 + 0.5;
          const pal = type === 'Ammonia world' ? [[226, 206, 142], [176, 150, 86], [140, 116, 70]] : [[238, 218, 178], [204, 160, 112], [150, 104, 70]];
          col = t < 0.5 ? mixc(pal[0], pal[1], t * 2) : mixc(pal[1], pal[2], (t - 0.5) * 2);
          col = mixc(col, [250, 240, 220], sat(nz(px * 6, py * 14, pz * 6, 3) - 0.55) * 0.8);
        } else if (type === 'Ice giant') {
          const t = Math.sin(lat * 7 + (nz(px * 1.6, py * 1.6 + ox, pz * 1.6, 3) - 0.5) * 3) * 0.5 + 0.5;
          col = mixc([156, 222, 234], [84, 160, 186], t * 0.7);
        } else if (type === 'Icy world') {
          const e = nz(px * 3 + ox, py * 3, pz * 3, 4), rr = 1 - Math.abs(nz(px * 5, py * 5, pz * 5 + oy, 3) * 2 - 1);
          col = mixc([222, 232, 242], [182, 204, 224], sat((e - 0.4) * 2));
          if (rr > 0.9) col = mixc(col, [120, 160, 200], (rr - 0.9) * 6);
        } else {
          const base = type === 'Rocky ice world' ? [[150, 154, 162], [206, 212, 220]] : type === 'Metal-rich world' ? [[92, 86, 82], [150, 140, 132]]
            : type === 'Moon' ? [[118, 118, 122], [182, 182, 186]] : [[132, 104, 84], [190, 160, 128]];
          const e = nz(px * 2.6 + ox, py * 2.6, pz * 2.6 + oy, 5);
          col = mixc(base[0], base[1], sat((e - 0.3) * 2.2));
          const cr = nz(px * 9, py * 9, pz * 9, 2);
          if (cr > 0.66) col = mixc(col, base[0].map(v => v * 0.7), sat((cr - 0.66) * 6) * 0.8);
          if (type === 'Rocky ice world' && nz(px * 2 + 4, py * 2, pz * 2, 3) > 0.56) col = mixc(col, [236, 240, 246], 0.75);
          if (type === 'Metal-rich world' && nz(px * 20, py * 20, pz * 20, 1) > 0.8) col = [210, 204, 196];
        }
        const o = 4 * (yy * w + xx);
        d[o] = Math.min(255, col[0] * (1 + shift)); d[o + 1] = Math.min(255, col[1]); d[o + 2] = Math.min(255, col[2] * (1 - shift)); d[o + 3] = 255;
      }
    }
    g.putImageData(img, 0, 0);
    const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace; t.anisotropy = 4; return t;
  }
  const texCache = new Map(), texQueue = [];
  function queueTexture(n) {
    const key = n.type + '|' + n.name;
    if (texCache.has(key)) { n.mesh.material.map = texCache.get(key); n.mesh.material.color.set(0xffffff); n.mesh.material.needsUpdate = true; return; }
    texQueue.push(n);
  }
  function pumpTextures(budgetMs) {
    const t0 = performance.now();
    while (texQueue.length && performance.now() - t0 < budgetMs) {
      const n = texQueue.shift();
      if (!SY || !SY.nodes.includes(n)) continue;
      const key = n.type + '|' + n.name, big = n.kind === 1 && n.size > 2;
      const t = texCache.get(key) || paintPlanet(n.kind === 2 && !BODY_COL[n.type] ? 'Moon' : n.type, n.name, big ? 192 : 112, big ? 96 : 56);
      texCache.set(key, t);
      n.mesh.material.map = t; n.mesh.material.color.set(0xffffff); n.mesh.material.needsUpdate = true;
    }
  }

  // ---- Orbits in time: Kepler periods from the data (planet masses from gravity and radius) ----
  const STAR_MASS = { O: 20, B: 6, A: 2, F: 1.3, G: 1, K: 0.75, M: 0.35, D: 0.6, L: 0.07, T: 0.05, Y: 0.03, N: 1.4, W: 15, C: 1.2 };
  const massSun = cls => STAR_MASS[(cls || 'G')[0]] || 1;
  const massEarth = n => n.g > 0 && n.rkm > 0 ? n.g * Math.pow(n.rkm / 6371, 2) : /giant/.test(n.type || '') ? 300 : 1;
  const EPOCH = Date.UTC(2026, 9, 13, 12, 0);   // the map's "now": 13 Oct 2026 12:00 UTC
  const sim = { days: 0, playing: false, speed: 2, trueScale: false, blend: 0 };
  const SPEEDS = [[1 / 24, '1 hour/s'], [0.25, '6 hours/s'], [1, '1 day/s'], [7, '1 week/s'], [30, '30 days/s'], [365, '1 year/s']];
  function buildSystem(i) {
    if (SY && SY.i === i) return SY;
    if (SY) { sysScene.remove(SY.root); SY.root.traverse(o => { if (o.material) o.material.dispose(); if (o.geometry && o.geometry !== sphere && o.geometry !== sphereGeo && o.geometry !== unitCircle) o.geometry.dispose(); }); }
    const { bodies: raw, schematic, unknownWorlds } = bodiesOf(i);
    const sysName = SYS.name[i];
    const nodes = raw.map((b, k) => ({ k, name: b[0], kind: b[1], type: b[2], parent: b[3], a: b[4], rkm: b[5], g: b[6], t: b[7], atm: b[8], land: b[9], rings: b[10], kids: [], pos: new V3(), size: 1, orbit: 0, theta: 0 }));
    nodes.forEach(n => { n.short = n.name.startsWith(sysName + ' ') ? n.name.slice(sysName.length + 1) : n.name; if (n.parent >= 0 && nodes[n.parent]) nodes[n.parent].kids.push(n); });
    const prim = nodes[0], root = new THREE.Group();
    prim.size = 5.5;
    nodes.forEach(n => {
      if (n === prim) return;
      if (n.kind === 0) n.size = 3.2;
      else if (n.kind === 1) n.size = clamp(1.0 + 1.55 * log10(1 + n.rkm / 1600), 1.0, 4.8);
      else if (n.kind === 2) n.size = clamp(0.38 + 0.55 * log10(1 + n.rkm / 700), 0.38, 1.25);
    });
    const ang = s => (hashStr(s) % 3600) / 3600 * Math.PI * 2;
    nodes.forEach(n => { n.theta0 = n.theta = ang(n.name); });
    nodes.forEach(n => {
      if (n === prim) return;
      const moons = n.kids.slice().sort((p, q) => p.a - q.a);
      moons.forEach((m, j) => { m.orbit = n.size + 1.7 + j * 1.3; });
      n.ext = moons.length ? moons[moons.length - 1].orbit + 0.8 : n.size + (n.rings ? n.size * 1.3 : 0);
    });
    const orb = prim.kids.slice().sort((p, q) => p.a - q.a);
    const as = orb.map(n => Math.max(n.a, 1e-4));
    const l0 = as.length ? log10(Math.min(...as)) : 0, l1 = as.length ? log10(Math.max(...as)) : 1, aMax = as.length ? Math.max(...as) : 1;
    let prevR = prim.size + 8, prevExt = 0;
    orb.forEach((n, j) => {
      const u = l1 > l0 ? (log10(Math.max(n.a, 1e-4)) - l0) / (l1 - l0) : 0;
      let r = 20 + u * 112;
      if (n.kind === 3) n.ext = 3;
      r = Math.max(r, j === 0 ? prim.size + 10 + n.ext : prevR + prevExt + n.ext + 3.2);
      n.orbit = r; prevR = r; prevExt = n.ext;
      n.orbitTrue = 7 + (Math.max(n.a, 1e-4) / aMax) * 125;     // True scale: distances in proportion
    });
    // periods (days): around the primary from its mass; moons from their planet's mass (g and radius)
    const Ms = massSun(SYS.cls[i]);
    nodes.forEach(n => {
      if (n === prim) return;
      const par = nodes[n.parent] || prim, a = Math.max(n.a, schematic ? 0.002 : 1e-5);
      const M0 = par === prim || par.kind === 0 ? (par === prim ? Ms : massSun(par.type)) : massEarth(par) * 3.003e-6;
      n.period = 365.25 * Math.sqrt(Math.pow(a, 3) / Math.max(M0, 1e-9));
      if (schematic && n.kind === 2) n.period = 4 + (hashStr(n.name) % 30);
    });
    const maxR = orb.length ? Math.max(...orb.map(n => n.orbit + n.ext)) : 30;
    const orbitLine = (op, moon) => {
      const l = new THREE.LineLoop(unitCircle, new THREE.LineBasicMaterial({ color: moon ? 0x3d6698 : 0x4a7fc0, transparent: true, opacity: op }));
      l.userData.baseOp = op; root.add(l); return l;
    };
    const starCol = new THREE.Color(SYS.col[i]);
    nodes.forEach(n => {
      if (n.kind === 0) {
        const c = n === prim ? starCol : new THREE.Color(classCol(n.type));
        const core = new THREE.Mesh(sphere, new THREE.MeshBasicMaterial({ color: c.clone().lerp(new THREE.Color(1, 1, 1), 0.45), transparent: true }));
        core.scale.setScalar(n.size); root.add(core);
        const glow = new THREE.Sprite(new THREE.SpriteMaterial({ map: radialTex, color: c, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending }));
        glow.scale.setScalar(n.size * (n === prim ? 11 : 8)); glow.userData.baseOp = 1; root.add(glow);
        n.mesh = core; n.glow = glow;
        if (n !== prim) n.orbitLine = orbitLine(0.32);
      } else if (n.kind === 3) {
        const cnt = 900, p = new Float32Array(cnt * 3), rnd = mulberry(hashStr(n.name));
        for (let k = 0; k < cnt; k++) { const t = rnd() * Math.PI * 2, r = 1 + (rnd() - 0.5) * 4.2 / n.orbit; p[3 * k] = Math.cos(t) * r; p[3 * k + 1] = (rnd() - 0.5) * 0.7 / n.orbit; p[3 * k + 2] = Math.sin(t) * r; }
        const g = new THREE.BufferGeometry(); g.setAttribute('position', new THREE.BufferAttribute(p, 3));
        const pts = new THREE.Points(g, new THREE.PointsMaterial({ color: 0xa49a8e, size: 1.6, sizeAttenuation: false, transparent: true, opacity: 0.55, depthWrite: false }));
        pts.userData.baseOp = 0.55; root.add(pts); n.mesh = null; n.belt = pts;
      } else {
        const mat = new THREE.MeshStandardMaterial({ color: BODY_COL[n.type] || 0xa0a0a0, roughness: 0.92, metalness: 0, transparent: true });
        const m = new THREE.Mesh(sphere, mat); m.scale.setScalar(n.size); root.add(m); n.mesh = m;
        if (n.rings) {
          const rg = new THREE.Mesh(new THREE.RingGeometry(1.45, 2.25, 96), new THREE.MeshBasicMaterial({ color: 0xd9ccb4, transparent: true, opacity: 0.42, side: THREE.DoubleSide, depthWrite: false }));
          rg.rotation.x = -Math.PI / 2 + 0.42; rg.userData.baseOp = 0.42; root.add(rg); n.ring = rg;
        }
        n.orbitLine = orbitLine(n.kind === 2 ? 0.3 : 0.55, n.kind === 2);
      }
    });
    const homeK = M.home.sys === i ? M.home.bodyIndex : -1;
    const pl = playerFound(i) ? playersBy.get(i) : null, fs = findsAt(i);
    nodes.forEach(n => {
      n.home = n.k === homeK; n.station = null; n.player = pl && pl.bodyIndex === n.k ? pl : null;
      n.find = fs.find(f => f.body === n.name) || null;
      n.res = HD.resByBody.get(i + ':' + n.k) || 0;
    });
    sysScene.add(root);
    SY = { i, nodes, root, maxR, schematic, prim, raw, unknownWorlds, orb };
    layoutSystem(true);
    nodes.forEach(n => { if (n.mesh && n.kind !== 0) queueTexture(n); });
    return SY;
  }
  // place every body for the current time and scale (Compressed, True or in between)
  function layoutSystem() {
    if (!SY) return;
    const b = sim.blend, sz = lerp(1, 0.55, b);
    const place = (n, cx, cz, r) => {
      n.theta = n.theta0 + (n.period ? sim.days / n.period * Math.PI * 2 : 0);
      n.pos.set(cx + Math.cos(n.theta) * r, 0, cz + Math.sin(n.theta) * r);
      if (n.mesh) { n.mesh.position.copy(n.pos); n.mesh.scale.setScalar(n.size * (n === SY.prim ? lerp(1, 0.6, b) : sz)); }
      if (n.glow) { n.glow.position.copy(n.pos); n.glow.scale.setScalar(n.size * (n === SY.prim ? 11 : 8) * (n === SY.prim ? lerp(1, 0.6, b) : sz)); }
      if (n.ring) { n.ring.position.copy(n.pos); n.ring.scale.setScalar(n.size * sz); }
      if (n.orbitLine) { n.orbitLine.position.set(cx, 0, cz); n.orbitLine.scale.setScalar(Math.max(0.01, r)); }
      n.kids.forEach(m => place(m, n.pos.x, n.pos.z, m.orbit * sz));
    };
    SY.prim.pos.set(0, 0, 0);
    if (SY.prim.mesh) { SY.prim.mesh.position.set(0, 0, 0); SY.prim.mesh.scale.setScalar(SY.prim.size * lerp(1, 0.6, b)); }
    if (SY.prim.glow) SY.prim.glow.scale.setScalar(SY.prim.size * 11 * lerp(1, 0.6, b));
    for (const n of SY.orb) {
      const r = lerp(n.orbit, n.orbitTrue, b);
      if (n.kind === 3) { n.pos.set(r, 0, 0); if (n.belt) n.belt.scale.setScalar(r); continue; }
      place(n, 0, 0, r);
    }
  }

  // ---------- System level, Simple: concept A's calm row of worlds (HTML) ----------
  const simpleEl = $('#simple');
  let simple = null; // { i, bodies, sel, schematic, order }
  const PT = {
    'Metal-rich world': ['#d2c3b6', '#6c5a4f'], 'Rocky world': ['#d7b596', '#6e533f'], 'Earth-like world': ['#8cc8f0', '#2a6b4a'],
    'Water world': ['#7db4f2', '#1c3f84'], 'Icy world': ['#f2f7fb', '#8ea6ba'], 'Rocky ice world': ['#d4d9df', '#68717c'],
    'Gas giant': ['#ecd3a8', '#9b734c'], 'Ice giant': ['#b0e6ee', '#3a7f94'], 'Ammonia world': ['#e2cf93', '#7b6837'], Moon: ['#d8dbe0', '#646a72'],
  };
  function ballStyle(type, size, starCol) {
    if (starCol) return `width:${size}px;height:${size}px;background:radial-gradient(circle at 50% 50%, #fff 0, ${starCol} 45%, ${starCol} 70%);box-shadow:0 0 ${size * 0.6}px ${size * 0.15}px ${starCol}80`;
    const c = PT[type] || ['#cfcfcf', '#555'];
    let bg = `radial-gradient(circle at 32% 30%, ${c[0]} 0%, ${c[1]} 62%, #07090d 100%)`;
    if (type === 'Gas giant' || type === 'Ice giant') bg = `radial-gradient(circle at 32% 30%, transparent 0%, transparent 55%, rgba(4,6,10,.75) 100%), repeating-linear-gradient(170deg, ${c[0]} 0 ${Math.max(2, size / 9)}px, ${c[1]} ${Math.max(2, size / 9)}px ${Math.max(3, size / 5)}px), ${c[1]}`;
    if (type === 'Earth-like world') bg = `radial-gradient(circle at 32% 30%, rgba(255,255,255,.35) 0, transparent 38%), radial-gradient(circle at 60% 62%, #3f8a55 0 18%, transparent 30%), radial-gradient(circle at 32% 30%, ${c[0]} 0%, #2b5fa8 60%, #07090d 100%)`;
    return `width:${size}px;height:${size}px;background:${bg}`;
  }
  function buildSimple(i, pickBody) {
    const { bodies, schematic } = bodiesOf(i);
    const name = SYS.name[i];
    const short = n => n.indexOf(name + ' ') === 0 ? n.slice(name.length + 1) : n;
    const top = [];
    bodies.forEach((b, k) => { if (k > 0 && b[1] !== 2) top.push(k); });
    const keyOf = k => { const b = bodies[k]; if (b[3] <= 0) return b[4]; const host = bodies[b[3]]; return host[4] + b[4] * 1e-3; };
    top.sort((a, c) => keyOf(a) - keyOf(c));
    const moonsOf = k => bodies.map((b, q) => [b, q]).filter(([b]) => b[1] === 2 && b[3] === k);
    let h = `<div class="sys-stage"><div class="sys-scroll"><div class="sys-track" style="--n:${top.length}"><div class="sys-star" style="--c:${SYS.col[i]}"></div>`;
    for (const k of top) {
      const b = bodies[k];
      if (b[1] === 3) { h += `<div class="sbody belt" data-b="${k}"><div class="slot"></div><div class="meta"><b>${esc(short(b[0]))}</b><span>${schematic ? 'Asteroid belt' : b[4] + ' AU'}</span></div></div>`; continue; }
      if (b[1] === 0) { h += `<div class="sbody star2" data-b="${k}" style="--c:${classCol(b[2])}"><div class="slot"><div class="pball" style="${ballStyle(null, 44, classCol(b[2]))}"></div></div><div class="meta"><b>${esc(short(b[0]))}</b><span>${esc(b[2] || 'Companion star')}${schematic ? '' : ' · ' + b[4] + ' AU'}</span></div></div>`; continue; }
      const sz = Math.round(Math.min(64, Math.max(10, 7 + Math.sqrt(b[5] / 1000) * 6.2)));
      const ms = moonsOf(k);
      const moonHTML = ms.slice(0, 12).map(([m]) => `<i class="${m[9] ? 'l' : ''}" style="--s:${Math.round(Math.min(12, Math.max(4, 3 + Math.sqrt(m[5] / 1000) * 2.4)))}px" title="${esc(m[0])}"></i>`).join('') + (ms.length > 12 ? `<em>+${ms.length - 12} moons</em>` : '');
      const home = i === M.home.sys && k === M.home.bodyIndex;
      const fd = findsAt(i).find(f => f.body === b[0]);
      h += `<div class="sbody" data-b="${k}"><div class="slot"><div class="pball${b[10] ? ' ringed' : ''}" style="${ballStyle(b[2], sz)}"></div></div>
        <div class="meta"><b>${home ? `<span class="mk-ic home">${I.home}</span>` : fd ? `<span class="mk-ic find">${I[FIND_IC[fd.kind]]}</span>` : ''}${esc(short(b[0]))}</b><span>${esc(b[2].replace(' world', ''))}</span>${b[9] ? '<span class="lb">Landable</span>' : ''}</div>
        <div class="moons">${moonHTML}</div></div>`;
    }
    h += `</div></div></div><div class="sys-detail"></div>`;
    if (bodiesOf(i).unknownWorlds) h += `<div class="sys-unknown">${I.unknown}<div><b>Nothing is known about its worlds</b><span>${K.level[i] === 1 ? 'You have only seen this star from home.' : 'No drone or ship of yours has been here.'} A drone survey would map its planets and moons.</span></div><button class="btn primary" data-sact="drone">${I.drone}Send a drone</button></div>`;
    else if (schematic) h += `<div class="sys-note">Schematic from the survey summary · full body lists exist within 40 ly in this prototype</div>`;
    simpleEl.innerHTML = h;
    simple = { i, bodies, sel: -1, schematic, order: top };
    let pick = pickBody ?? -1;
    if (pick < 0 && i === M.home.sys) pick = M.home.bodyIndex;
    if (pick < 0) pick = bodies.findIndex(b => b[2] === 'Earth-like world');
    if (pick < 0) pick = bodies.findIndex(b => b[1] === 1 && b[9]);
    if (pick < 0) pick = top[0] ?? -1;
    if (pick >= 0) selectSimpleBody(pick, true);
  }
  function selectSimpleBody(k, quiet) {
    if (!simple) return;
    const { i, bodies, schematic } = simple;
    const b = bodies[k]; if (!b) return;
    simple.sel = k; sysSel = k;
    simpleEl.querySelectorAll('.sbody').forEach(el => el.classList.toggle('on', +el.dataset.b === k));
    const el = simpleEl.querySelector(`.sbody[data-b="${k}"]`); if (el && !quiet) el.scrollIntoView({ inline: 'nearest', block: 'nearest', behavior: 'smooth' });
    $('.sys-detail', simpleEl).innerHTML = bodyDetailHTML(i, bodies, k, schematic);
    if (!quiet) { renderInspector(); updateHash(); }
  }
  function bodyDetailHTML(i, bodies, k, schematic) {
    const b = bodies[k];
    if (b[1] === 3) return `<div class="dt"><h3>${esc(b[0])}</h3><p>Asteroid belt${schematic ? '' : ' · ' + b[4] + ' AU from ' + esc(bodies[Math.max(0, b[3])][0])}</p></div>`;
    if (b[1] === 0) return `<div class="dball"><div class="pball" style="${ballStyle(null, 56, k === 0 ? SYS.col[i] : classCol(b[2]))}"></div></div><div class="dt"><h3>${esc(b[0])}</h3><p>${esc(b[2] || 'Companion star')}${k === 0 ? ' · primary star' : ''}${b[4] ? ' · ' + b[4] + ' AU' : ''}${b[7] ? ' · ' + fmt(b[7]) + ' K' : ''}</p></div>`;
    const mask = HD.resByBody.get(i + ':' + k);
    const res = mask ? HD.resNames(mask) : [];
    const moons = bodies.filter(m => m[1] === 2 && m[3] === k).length;
    const parent = b[1] === 2 ? 'Moon of ' + bodies[b[3]][0] : schematic ? 'Orbits ' + bodies[0][0] : b[4] + ' AU from ' + bodies[Math.max(0, b[3])][0];
    const isHome = i === M.home.sys && k === M.home.bodyIndex;
    const fd = findsAt(i).find(f => f.body === b[0]);
    const tC = Math.round(b[7] - 273.15);
    const facts = schematic
      ? `<div><b>${b[9] ? 'Yes' : 'No'}</b><span>Landable</span></div><div><b>${esc(b[2])}</b><span>Type</span></div><div><b>Schematic</b><span>Survey summary</span></div>`
      : `<div><b>${b[9] ? 'Yes' : 'No'}</b><span>Landable</span></div><div><b>${b[6] < 0.1 ? b[6].toFixed(3) : b[6].toFixed(2)} g</b><span>Gravity</span></div>
         <div><b>${tC > 0 ? '+' : ''}${fmt(tC)} °C</b><span>Surface</span></div><div><b>${esc(cap(b[8] || '—'))}</b><span>Atmosphere</span></div>
         <div><b>${fmt(b[5])} km</b><span>Radius</span></div>`;
    return `<div class="dball"><div class="pball${b[10] ? ' ringed' : ''}" style="${ballStyle(b[2], 64)}"></div></div>
      <div class="dt"><h3>${esc(b[0])}${isHome ? ' <span class="badge acc">Home base</span>' : ''}${fd ? ` <span class="badge find">${esc(fd.title)}</span>` : ''}</h3>
      <p>${esc(b[2])} · ${esc(parent)}${moons ? ' · ' + HD.plural(moons, 'moon') : ''}${b[10] ? ' · rings' : ''}</p>
      <div class="facts${schematic ? ' f3' : ''}">${facts}</div>
      ${res.length ? `<div class="res">${res.map(r => `<em>${esc(cap(r))}</em>`).join('')}<em class="mockhint">mock hints</em></div>` : ''}</div>`;
  }

  // ---------- camera ----------
  const LOG = { galaxy: log10(98000), region: log10(5200), nbr: log10(100), home: log10(150), min: log10(2.2), max: log10(180000) };
  const HOME_PITCH = 1.1;   // the default view: your home and the known space around it, seen from above at a slant
  const B_GAL = log10(20000), B_REG = log10(700);
  const cam = {
    cur: { yaw: 0.0, pitch: HOME_PITCH, ld: LOG.home, a: ship.pos.clone() },
    goal: { yaw: 0.0, pitch: HOME_PITCH, ld: LOG.home, a: ship.pos.clone() },
  };
  const scam = { cur: { yaw: 0, pitch: 0.55, ld: 2.4, a: new V3() }, goal: { yaw: 0, pitch: 0.55, ld: 2.4, a: new V3() }, min: 1.2, max: 3 };
  const target = new V3();
  let tween = null, stween = null, topView = false, savedPitch = 0.62, INSTANT_NOW = false;
  let mode = 'map', sysBlend = 0, sysGoal = 0, sysT0 = 0, sysFrom = 0, sysView = 'simple', simpleExit = 0;
  // Tilt is free through the whole range, under the plane too; only the last degree at each pole is kept, so the view
  // never flips over the top (owner, round 2: no clamped tilt)
  const PITCH_MIN = -1.5533, PITCH_MAX = 1.5533;
  let viewShift = 0;
  function applyOffset(cm) {
    if (viewShift < 0.5) { cm.aspect = W / H; if (cm.view && cm.view.enabled) cm.clearViewOffset(); else cm.updateProjectionMatrix(); return; }
    cm.aspect = (W + 2 * viewShift) / H;
    cm.setViewOffset(W + 2 * viewShift, H, 2 * viewShift, 0, W, H);
  }
  function placeCamera() {
    const c = cam.cur, d = Math.pow(10, c.ld), t = smooth(4.05, 4.78, c.ld);
    target.copy(c.a).lerp(GC, t);
    const cp = Math.cos(c.pitch), sp = Math.sin(c.pitch);
    camera.position.set(target.x - Math.sin(c.yaw) * cp * d, target.y + sp * d, target.z - Math.cos(c.yaw) * cp * d);
    camera.near = Math.max(0.02, d * 0.002); camera.far = d * 6 + 300000;
    applyOffset(camera); camera.lookAt(target); camera.updateMatrixWorld();
  }
  function placeSysCamera() {
    const c = scam.cur, d = Math.pow(10, c.ld), cp = Math.cos(c.pitch), sp = Math.sin(c.pitch);
    sysCam.position.set(c.a.x - Math.sin(c.yaw) * cp * d, c.a.y + sp * d, c.a.z - Math.cos(c.yaw) * cp * d);
    sysCam.near = Math.max(0.05, d * 0.01); sysCam.far = d * 20 + 2000;
    applyOffset(sysCam); sysCam.lookAt(c.a); sysCam.updateMatrixWorld();
  }
  function smoothCam(c, dt, k = 10) {
    const f = 1 - Math.exp(-dt * k);
    c.cur.yaw += angDiff(c.cur.yaw, c.goal.yaw) * f;
    c.cur.pitch += (c.goal.pitch - c.cur.pitch) * f;
    c.cur.ld += (c.goal.ld - c.cur.ld) * f;
    c.cur.a.lerp(c.goal.a, f);
  }
  function snapCam(c) { c.cur.yaw = c.goal.yaw; c.cur.pitch = c.goal.pitch; c.cur.ld = c.goal.ld; c.cur.a.copy(c.goal.a); }
  function makeTween(c, to, dur, bump, done) {
    return { c, from: { yaw: c.goal.yaw, pitch: c.goal.pitch, ld: c.goal.ld, a: c.goal.a.clone() },
      to: { yaw: to.yaw ?? c.goal.yaw, pitch: to.pitch ?? c.goal.pitch, ld: to.ld ?? c.goal.ld, a: (to.a || c.goal.a).clone() },
      t0: performance.now(), dur: dur * 1000, bump: bump || 0, done };
  }
  function stepTween(tw, now) {
    const p = clamp((now - tw.t0) / tw.dur, 0, 1), e = easeInOut(p), c = tw.c.goal;
    c.yaw = tw.from.yaw + angDiff(tw.from.yaw, tw.to.yaw) * e;
    c.pitch = lerp(tw.from.pitch, tw.to.pitch, e);
    c.ld = lerp(tw.from.ld, tw.to.ld, e) + tw.bump * Math.sin(Math.PI * p);
    c.a.lerpVectors(tw.from.a, tw.to.a, e);
    if (p >= 1 && tw.done) { const d = tw.done; tw.done = null; d(); }
    return p >= 1;
  }
  function flyTo(to, dur = 1.2, bump = 0, done) { tween = makeTween(cam, to, dur, bump, done); if (INSTANT_NOW) { stepTween(tween, Infinity); tween = null; snapCam(cam); } }
  function sysFlyTo(to, dur = 0.9, done) { stween = makeTween(scam, to, dur, 0, done); if (INSTANT_NOW) { stepTween(stween, Infinity); stween = null; snapCam(scam); } }
  function frameStars(lst, extraPts) {
    const pts = lst.map(starPos).concat(extraPts || []);
    if (!pts.length) return;
    const mid = new V3(); pts.forEach(p => mid.add(p)); mid.multiplyScalar(1 / pts.length);
    let ext = 0; pts.forEach(p => { ext = Math.max(ext, p.distanceTo(mid)); });
    flyTo({ a: mid, ld: clamp(log10(ext * 2.7 + 20), log10(28), log10(420)), pitch: topView ? PITCH_MAX : clamp(cam.goal.pitch, 0.4, 1.1) }, cam.goal.ld > B_REG ? 1.6 : 1.1);
  }

  // ---------- levels ----------
  const levelOf = () => (mode === 'system' && sysBlend > 0.5) ? 3 : cam.cur.ld > B_GAL ? 0 : cam.cur.ld > B_REG ? 1 : 2;
  const goalLevel = () => mode === 'system' ? 3 : cam.goal.ld > B_GAL ? 0 : cam.goal.ld > B_REG ? 1 : 2;
  function goLevel(k) {
    hideHint();
    if (k === 3) { const i = sel && sel.t === 'star' ? sel.i : hov && hov.t === 'star' ? hov.i : ship.sys; enterSystem(i); return; }
    if (mode === 'system') exitSystem(true);
    const a = cam.goal.a.length() > 400 ? ship.pos.clone() : cam.goal.a.clone();
    if (k === 0) flyTo({ ld: LOG.galaxy, pitch: topView ? PITCH_MAX : 0.62, a }, 1.7);
    if (k === 1) flyTo({ ld: LOG.region, pitch: topView ? PITCH_MAX : 0.86, a }, 1.4);
    if (k === 2) flyTo({ ld: LOG.home, pitch: topView ? PITCH_MAX : cam.goal.ld > B_REG ? HOME_PITCH : cam.goal.pitch, a: (sel && sel.t === 'star' && cam.goal.ld > B_REG) ? cam.goal.a.clone() : a }, 1.3);
  }
  function enterSystem(i, opts = {}) {
    if (i < 0) return;
    hideHint(); closeSearch();
    buildSystem(i);
    const was = mode; mode = 'system'; sysFrom = sysBlend; sysGoal = 1; sysT0 = performance.now(); simpleExit = 0;
    if (opts.view) sysView = opts.view;
    if (was !== 'system') {
      flyTo({ a: starPos(i), ld: log10(0.9) }, 1.0);
      scam.goal.yaw = scam.cur.yaw = cam.goal.yaw; scam.goal.pitch = scam.cur.pitch = 0.56;
    }
    sysSel = opts.body ?? -1;
    scam.min = log10(Math.max(14, SY.maxR * 0.18)); scam.max = log10(SY.maxR * 4.2);
    scam.goal.a.set(0, 0, 0); scam.goal.ld = log10(SY.maxR * 2.25); scam.cur.ld = scam.goal.ld + 0.35; scam.cur.a.set(0, 0, 0);
    buildSimple(i, opts.body);
    if (sysSel < 0 && sysView === 'realistic') sysSel = defaultBody(i);
    if (sysView === 'realistic' && sysSel >= 0) focusBody(sysSel, true);
    if (INSTANT_NOW) { sysBlend = 1; snapCam(scam); }
    if (!(sel && sel.t === 'star' && sel.i === i)) { sel = { t: 'star', i }; list = null; listSet = null; restyle(); }
    addRecent(i);
    renderInspector(true); renderSysMode(); renderSysmap(); updateHash();
  }
  function defaultBody(i) {
    const b = SY ? SY.raw : bodiesOf(i).bodies;
    let k = i === M.home.sys ? M.home.bodyIndex : -1;
    if (k < 0) k = b.findIndex(q => q[2] === 'Earth-like world');
    if (k < 0) k = b.findIndex(q => q[1] === 1 && q[9]);
    if (k < 0) k = b.length > 1 ? 1 : 0;
    return k;
  }
  function exitSystem(silent) {
    if (mode !== 'system') return;
    mode = 'map'; sysFrom = sysBlend; sysGoal = 0; sysT0 = performance.now(); sysSel = -1;
    if (!silent) flyTo({ ld: log10(26), a: starPos(SY.i) }, 1.0);
    if (INSTANT_NOW) sysBlend = 0;
    sim.playing = false;
    renderInspector(); renderSysMode(); renderSysmap(); updateHash();
  }
  function setSysView(v) {
    sysView = v; renderSysMode();
    if (v === 'realistic' && sysSel < 0 && SY) sysSel = defaultBody(SY.i);
    if (v === 'realistic' && sysSel >= 0) focusBody(sysSel);
    renderSysmap();
    if (v === 'simple' && simple && sysSel >= 0 && simple.bodies[sysSel]) selectSimpleBody(sysSel, true);
    renderInspector(); updateHash();
  }

  // ---------- selection & hover ----------
  let sel = null, hov = null, sysSel = -1, sysHov = -1, targetSys = -1;
  let list = null, ctxBadge = null, fromList = false, auto = null;
  function select(obj, opts = {}) {
    if (auto && obj && !(obj.t === 'star' && auto.r.stops.some(s => s.sys === obj.i))) { toast(I.play, 'Autopilot is flying · Space or Esc to stop'); return; }
    sel = obj;
    if (!(obj && routeCtx && obj.t === 'star' && obj.i === routeCtx.dest)) clearRoute();
    if (obj && obj.t === 'star') {
      ctxBadge = opts.ctx || null; fromList = !!opts.fromList;
      if (!fromList && list) { list = null; listSet = null; restyle(); }
      addRecent(obj.i);
    } else if (obj) { ctxBadge = null; fromList = false; if (list) { list = null; listSet = null; restyle(); } }
    if (obj && (obj.t === 'void' || obj.t === 'group')) { ctxBadge = null; fromList = false; }
    if (obj && obj.t === 'sector') { const s = SECTORS[obj.k]; sectorMat.uniforms.uSel.value.set(s[1] - 64, s[3] - 38); }
    else sectorMat.uniforms.uSel.value.set(999, 999);
    renderInspector(true); updateHash();
    if (!obj || opts.keepCam) return;
    if (obj.t === 'sector' && cam.goal.ld > B_REG) { const s = SECTORS[obj.k]; flyTo({ a: new V3(s[4], 0, s[6]) }, 1.0); }
  }
  function clearAll() {
    if (auto) return;
    sel = null; list = null; listSet = null; ctxBadge = null; fromList = false; clearRoute(); restyle();
    sectorMat.uniforms.uSel.value.set(999, 999);
    renderInspector(); updateHash();
  }
  function flyToStar(i, ld) {
    hideHint();
    if (mode === 'system') exitSystem(true);
    const d = ld ?? Math.min(Math.max(cam.goal.ld, log10(18)), log10(60));
    flyTo({ a: starPos(i), ld: d, pitch: topView ? PITCH_MAX : clamp(cam.goal.pitch, 0.35, 1.2) }, cam.goal.ld > B_REG ? 1.6 : 1.1);
  }
  function selectStar(i, opts = {}) {
    if (i < 0) return;
    if (mode === 'system' && !(SY && SY.i === i)) exitSystem(true);
    select({ t: 'star', i }, { keepCam: true, ctx: opts.ctx, fromList: opts.fromList });
    if (!opts.noFly) flyToStar(i, opts.ld ?? log10(34));
  }

  // ---------- UI: chrome ----------
  const ui = {
    crumbs: $('#crumbs'), levelname: $('#levelname'), levelsub: $('#levelsub'), rail: $('#rail'), thumb: $('.rail-thumb'), railFill: $('.rail-fill'),
    stops: [...document.querySelectorAll('.rail-stop')], chips: $('#chips'), insp: $('#insp-scroll'), hc: $('#hovercard'), toast: $('#toast'),
    ship: $('#shipcard'), pill: $('#searchpill'), hint: $('#hint'), scalebar: $('#scalebar'), scaletxt: $('#scaletxt'), sysmode: $('#sysmode'),
    top: $('#btn-top'), me: $('#btn-me'), zin: $('#btn-in'), zout: $('#btn-out'), labels: $('#labels'), markers: $('#markers'), stage: $('#stage'),
  };
  ui.stops.forEach(b => b.addEventListener('click', () => goLevel(+b.dataset.level)));
  ui.top.addEventListener('click', () => toggleTop());
  ui.me.addEventListener('click', () => centreOnMe());
  ui.ship.addEventListener('click', () => centreOnMe());
  ui.zin.addEventListener('click', () => zoomBy(-0.35));
  ui.zout.addEventListener('click', () => zoomBy(0.35));
  ui.sysmode.addEventListener('click', e => { const b = e.target.closest('button[data-v]'); if (b) setSysView(b.dataset.v); });
  ui.dronebtn = $('#dronebtn'); ui.dronepanel = $('#dronepanel'); ui.reports = $('#reports'); ui.fx = $('#fx');
  ui.dronebtn.addEventListener('click', () => toggleDrones());
  let toastTimer = 0;
  function toast(icon, html, ms = 2400) { ui.toast.innerHTML = (icon || '') + '<span>' + html + '</span>'; ui.toast.classList.add('show'); clearTimeout(toastTimer); toastTimer = setTimeout(() => ui.toast.classList.remove('show'), ms); }
  let hintHidden = false;
  function hideHint() { if (!hintHidden) { hintHidden = true; ui.hint.classList.add('hide'); } }
  setTimeout(hideHint, 14000);
  function renderSysMode() {
    const on = mode === 'system';
    ui.sysmode.classList.toggle('show', on);
    ui.sysmode.querySelectorAll('button').forEach(b => b.classList.toggle('on', b.dataset.v === sysView));
    const th = ui.sysmode.querySelector('.seg-thumb'), act = ui.sysmode.querySelector(`button[data-v="${sysView}"]`);
    if (th && act) { th.style.left = act.offsetLeft + 'px'; th.style.width = act.offsetWidth + 'px'; }
    panelsDirty = true;
  }

  function toggleTop(on) {
    topView = on ?? !topView; hideHint();
    ui.top.classList.toggle('on', topView);
    const c = mode === 'system' && sysBlend > 0.5 ? scam : cam;
    if (topView) { savedPitch = c.goal.pitch < 1.4 ? c.goal.pitch : savedPitch; }
    const to = { pitch: topView ? PITCH_MAX : (savedPitch || 0.62) };
    if (c === scam) sysFlyTo(to, 0.8); else flyTo(to, 0.9);
    updateHash();
  }
  function centreOnMe() {
    hideHint();
    if (mode === 'system') { if (SY.i !== ship.sys) { exitSystem(true); flyTo({ a: ship.pos.clone(), ld: LOG.nbr }, 1.3); } return; }
    flyTo({ a: ship.pos.clone(), ld: cam.goal.ld > B_REG ? cam.goal.ld : Math.min(cam.goal.ld, LOG.nbr) }, 1.2);
  }
  function zoomBy(dl) {
    hideHint();
    if (mode === 'system') {
      if (sysView === 'simple') { if (dl > 0) exitSystem(); return; }
      scam.goal.ld = clamp(scam.goal.ld + dl, scam.min, scam.max + 0.4); if (scam.goal.ld > scam.max + 0.3) exitSystem(); return;
    }
    tween = null;
    cam.goal.ld = clamp(cam.goal.ld + dl, LOG.min, LOG.max);
    afterZoom(dl);
  }
  function afterZoom(dl) {
    if (dl < 0 && cam.goal.ld <= LOG.min + 0.001 && mode === 'map') {
      const i = nearestStar(cam.goal.a, 4);
      if (i >= 0) { enterSystem(i); return; }
    }
    if (dl < 0 && cam.goal.ld < B_REG + 0.25 && cam.goal.a.length() > 170) flyTo({ a: ship.pos.clone() }, 1.2);
  }

  // Filter chips
  function renderChips() {
    ui.chips.innerHTML = FILTERS.map((f, k) => (k === 5 ? '<span class="chip-sep"></span>' : '') +
      `<button class="chip${activeFilters.has(f.id) ? ' on' : ''}" data-f="${f.id}" style="--cc:${f.color}" title="${esc(f.help || f.name)}${f.mock ? ' (mock data)' : ''}"><i></i><span class="lbltxt">${f.name}</span><span class="cnt">${fmt(counts[f.id])}</span></button>`).join('');
  }
  ui.chips.addEventListener('click', e => {
    const b = e.target.closest('.chip'); if (!b) return;
    const id = b.dataset.f; if (activeFilters.has(id)) activeFilters.delete(id); else activeFilters.add(id);
    if (list) { list = null; listSet = null; if (!sel) renderInspector(); }
    applyFilters();
    if (activeFilters.has(id)) {
      const n = counts[id];
      toast(I.check, `<b>${fmt(n)}</b>&nbsp;${FILTERS.find(f => f.id === id).name.toLowerCase()} system${n === 1 ? '' : 's'} highlighted`, 1600);
    }
  });

  // Ship card (concept A's ship pill)
  function renderShip() {
    const pips = Array.from({ length: ship.max }, (_, k) => `<i class="${k < ship.fuel ? '' : 'off'}"></i>`).join('');
    const where = auto ? 'jumping · ' + esc(shortName(ship.sys)) : 'at ' + esc(shortName(ship.sys)) + (ship.sys === M.home.sys ? ' · home' : '');
    const tgt = targetSys >= 0 ? `<span class="dot">·</span><span class="tgt">${I.target}${esc(shortName(targetSys))}</span>` : `<span class="dot">·</span><span>jump ${fmt(ship.range, 1)} ly</span>`;
    const nf = K.finds.filter(f => !f.hidden).length;
    const terr = `<div class="ship-terr"><i></i>Known · ${fmt(K.knownList.length)} systems · ${HD.plural(nf, 'find')}</div>`;
    ui.ship.innerHTML = `<div class="ship-ico">${I.ship}</div><div class="ship-txt"><div class="ship-name"><b>${esc(ship.name)}</b><span>${where}</span></div>
      <div class="ship-meta"><span class="fuel${ship.fuel <= 2 ? ' low' : ''}">${pips}</span><span>fuel ${ship.fuel}/${ship.max}</span>${tgt}</div>${terr}</div>`;
    panelsDirty = true;
  }

  // ---------- Search: concept A's big panel ----------
  const qEl = $('#q'), topEl = $('#top'), panelEl = $('#panel');
  let panelOpen = false, results = [], activeIdx = -1;
  const CHIPS = [['finds', 'What drones found'], ['fuel', 'Nearest fuel star'], ['home', 'Home'], ['players', 'Player bases'], ['earth', 'Earth-like worlds'], ['landable', 'Landable nearby'], ['ringed', 'Ringed within 50 ly'], ['danger', 'Hazards']];
  function highlight(name, q) {
    const n = name.toLowerCase(), k = q.trim().toLowerCase(), i = k ? n.indexOf(k) : -1;
    if (i < 0) return esc(name);
    return esc(name.slice(0, i)) + '<mark>' + esc(name.slice(i, i + k.length)) + '</mark>' + esc(name.slice(i + k.length));
  }
  function riHTML(it) {
    if (it.type === 'intent') return `<div class="ri intent${it.id === 'danger' ? ' danger' : ''}">${iconOf(it.icon)}</div>`;
    if (it.type === 'recent') return `<div class="ri">${I.clock}</div>`;
    if (it.type === 'system') return it.icon === 'home' ? `<div class="ri intent">${I.home}</div>` : `<div class="ri${known(it.sys) ? '' : ' unk'}"><span class="sd" style="--c:${SYS.col[it.sys]}"></span></div>`;
    if (it.type === 'outpost') return `<div class="ri intent">${I.flag}</div>`;
    if (it.type === 'find') return `<div class="ri find">${I[FIND_IC[it.find.kind]]}</div>`;
    if (it.type === 'group') return `<div class="ri intent">${it.group.kind === 'home' ? I.home : I.drone}</div>`;
    return `<div class="ri">${I[it.icon] || I.star}</div>`;
  }
  function jumpsLabel(it) {
    if (it.far) return 'beyond survey';
    if (it.jumps == null) return '';
    if (it.jumps === 0) return 'you are here';
    if (it.jumps < 0) return 'unreachable';
    return HD.plural(it.jumps, 'jump');
  }
  function rowHTML(it, idx, q) {
    const kw = it.type === 'system' && it.sys != null ? K.level[it.sys] : -1;
    const tag = it.mock ? '<span class="tag">MOCK</span>' : kw >= 0 ? `<span class="tag k${kw}">${LVW[kw]}</span>` : '';
    return `<button class="row${idx === activeIdx ? ' active' : ''}" data-i="${idx}">${riHTML(it)}
      <div class="rt"><div class="rn">${highlight(it.name, q)}${tag}</div><div class="rs">${esc(it.sub || '')}</div></div>
      <div class="rd">${it.dist != null ? `<span class="dist">${HD.ly(it.dist)}</span>` : ''}<span class="jumps">${jumpsLabel(it)}</span></div></button>`;
  }
  function renderPanel() {
    const q = qEl.value;
    topEl.classList.toggle('has-q', !!q);
    let html = '<div class="pnl-inner">';
    results = [];
    if (!q.trim()) {
      html += `<div class="sec-h">Ask the map</div><div class="ichips">` +
        CHIPS.map(([id, t]) => `<button class="ichip" data-intent="${id}">${iconOf(HD.intentById[id].icon)}${t}</button>`).join('') + '</div>';
      const fl = [...favs].slice(0, 4);
      if (fl.length) {
        html += `<div class="sec-h">Favourites</div><div class="favs">` + fl.map(s => {
          const isHome = s === M.home.sys;
          return `<button class="fav" data-sys="${s}"><div class="fi">${isHome ? I.home : I.favOn}</div><b>${esc(isHome ? 'Home base' : SYS.name[s])}</b><span>${isHome ? esc(SYS.name[s]) : HD.ly(HD.dShip(s))}</span></button>`;
        }).join('') + '</div>';
      }
      if (recents.length) {
        html += `<div class="sec-h">Recents</div>`;
        recents.slice(0, 4).forEach(s => {
          const it = HD.describePlace({ type: 'system', name: SYS.name[s], sys: s });
          results.push(it);
          html += rowHTML(Object.assign({}, it, { type: 'recent' }), results.length - 1, '');
        });
      }
    } else {
      const r = HD.search(q);
      const groups = r.placesFirst ? [['Places', r.places], ['Ask the map', r.intents]] : [['Ask the map', r.intents], ['Places', r.places]];
      let any = false;
      for (const [title, items] of groups) {
        if (!items.length) continue;
        any = true;
        html += `<div class="sec-h">${title}</div>`;
        for (const it of items) { results.push(it); html += rowHTML(it, results.length - 1, q); }
      }
      if (!any) html += `<div class="empty-note">Nothing called “${esc(q)}” within 150 ly. Try a star name, a sector, “Perseus Arm”, or ask for “nearest fuel” or “finds”.</div>`;
      if (activeIdx < 0 && results.length) activeIdx = 0;
      html = html.replace(/class="row" data-i="0"/, 'class="row active" data-i="0"');
    }
    html += `<div class="panel-foot"><span><kbd>↑</kbd><kbd>↓</kbd>choose</span><span><kbd>↵</kbd>go</span><span><kbd>esc</kbd>back</span></div></div>`;
    panelEl.innerHTML = html;
  }
  function openSearch(q) {
    hideHint();
    if (!panelOpen) { panelOpen = true; document.body.classList.add('search-open'); activeIdx = -1; if (q == null) qEl.value = ''; }
    if (q != null) qEl.value = q;
    renderPanel();
    qEl.focus(); qEl.setSelectionRange(qEl.value.length, qEl.value.length);
  }
  function closeSearch() { if (!panelOpen) return; panelOpen = false; document.body.classList.remove('search-open'); qEl.blur(); }
  function setActive(i) {
    if (!results.length) return;
    activeIdx = (i + results.length) % results.length;
    panelEl.querySelectorAll('.row').forEach(r => r.classList.toggle('active', +r.dataset.i === activeIdx));
    const el = panelEl.querySelector(`.row[data-i="${activeIdx}"]`); if (el) el.scrollIntoView({ block: 'nearest' });
  }
  function choose(it) {
    if (!it) return;
    closeSearch();
    if (it.type === 'intent') runIntent(it.id, it.radius);
    else if (it.type === 'structure') { if (mode === 'system') exitSystem(true); select({ t: 'struct', id: it.id }, { keepCam: true }); flyTo({ ld: LOG.galaxy, pitch: topView ? PITCH_MAX : 0.62, a: ship.pos.clone() }, 1.7); }
    else if (it.type === 'sector') {
      if (mode === 'system') exitSystem(true);
      const k = SECTORS.findIndex(s => s[0] === it.name);
      if (k >= 0) { select({ t: 'sector', k }, { keepCam: true }); flyTo({ a: k === 0 ? ship.pos.clone() : new V3(SECTORS[k][4], 0, SECTORS[k][6]), ld: LOG.region, pitch: topView ? PITCH_MAX : 0.86 }, 1.5); }
    }
    else if (it.type === 'base') selectStar(it.sys, { ctx: { icon: 'flag', text: it.player.name } });
    else if (it.type === 'outpost') selectStar(it.sys, { ctx: { icon: 'flag', text: it.base.name } });
    else if (it.type === 'find') selectStar(it.sys, { ctx: { icon: FIND_IC[it.find.kind], text: it.find.title } });
    else if (it.type === 'group') selectGroup(it.group.index, true);
    else if (it.sys != null) selectStar(it.sys);
  }
  qEl.addEventListener('input', () => { activeIdx = -1; renderPanel(); });
  qEl.addEventListener('keydown', e => {
    if (e.key === 'ArrowDown') { e.preventDefault(); setActive(activeIdx + 1); }
    else if (e.key === 'ArrowUp') { e.preventDefault(); setActive(activeIdx - 1); }
    else if (e.key === 'Enter') { e.preventDefault(); choose(results[Math.max(0, activeIdx)]); }
    else if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); if (qEl.value) { qEl.value = ''; activeIdx = -1; renderPanel(); } else closeSearch(); }
  });
  $('#qclear').addEventListener('click', () => { qEl.value = ''; activeIdx = -1; qEl.focus(); renderPanel(); });
  panelEl.addEventListener('mousedown', e => e.preventDefault()); // keep focus in the field
  panelEl.addEventListener('click', e => {
    const chip = e.target.closest('[data-intent]'); if (chip) { closeSearch(); runIntent(chip.dataset.intent); return; }
    const fv = e.target.closest('.fav'); if (fv) { choose({ type: 'system', sys: +fv.dataset.sys }); return; }
    const row = e.target.closest('.row'); if (row) choose(results[+row.dataset.i]);
  });
  ui.pill.addEventListener('click', () => openSearch());
  $('#scrim').addEventListener('pointerdown', () => closeSearch());

  // Intents ("Ask the map")
  function runIntent(id, radius) {
    const t = HD.intentById[id]; if (!t) return;
    const rad = radius || t.radius || 150;
    const res = t.run(rad);
    if (mode === 'system') exitSystem(true);
    if (res.single != null) { selectStar(res.single, { ctx: { icon: t.icon, text: t.title + ' · ' + res.why } }); return; }
    clearRoute();
    list = { id, title: res.title || t.title, items: res.list, unit: res.unit, icon: t.icon, mock: t.mock, danger: res.danger };
    listSet = new Set(res.list); sel = null; ctxBadge = null; fromList = false;
    restyle(); renderInspector(true); updateHash();
    const extra = [ship.pos.clone()];
    if (res.danger) HZ.forEach(d => { extra.push(new V3(d.centre[0] + d.radiusLy, d.centre[1], d.centre[2]), new V3(d.centre[0] - d.radiusLy, d.centre[1], d.centre[2])); });
    if (cam.goal.ld > B_REG + 0.3) cam.goal.ld = Math.min(cam.goal.ld, B_REG);
    frameStars(res.list.slice(0, res.danger ? 2 : 12), extra);
  }

  // ---------- Inspector: concept A's cards, saying what you know ----------
  const fact = (v, k) => `<div><b>${v}</b><span>${k}</span></div>`;
  const tempC = k => `${fmt(Math.round(k - 273.15))} °C`;
  const homeP = starPos(HOME);
  const groupOfStar = i => K.groupOf[i] >= 0 ? K.groups[K.groupOf[i]] : null;
  function knowBadge(sys) {
    const l = K.level[sys], w = K.whenOf(sys);
    if (l === 4) return `<span class="badge acc">${sys === HOME ? 'Home base' : esc(baseAt(sys).name)}</span>`;
    if (l === 3) return `<span class="badge kv">${I.check}${cap(w || 'visited')}</span>`;
    if (l === 2) return `<span class="badge ks">${I.drone}${cap(w || 'scanned')}</span>`;
    if (l === 1) return `<span class="badge kseen">${I.eye}Seen from home</span>`;
    return `<span class="badge kunk">${I.unknown}Unknown</span>`;
  }
  function placeHTML(sys) {
    const here = sys === ship.sys, name = SYS.name[sys], cls = SYS.cls[sys], lv = K.level[sys];
    const what = lv ? `${cls} · ${starKind(cls)}` : SYS.cat[sys] ? `${cls} (catalogue)` : 'Unknown star';
    const sub = [what, here ? 'You are here' : HD.ly(distShip(sys)), here ? null : HD.jumpsText(sys)].filter(Boolean).join(' · ');
    const fav = favs.has(sys), sector = SECTORS[sectorOfPos(PX[sys], PY[sys], PZ[sys])];
    const back = fromList && list ? `<button class="icon-btn" data-act="back" title="Back to results (Esc)">${I.back}</button>` : '';
    let h = `<div class="ph">${back}<div class="orb${lv ? '' : ' unk'}" style="--c:${lv ? SYS.col[sys] : '#7d8898'}"></div><div class="ttl"><p class="eyebrow">${esc(knowWord(sys))} · ${esc(sector ? sector[0] : 'Uncharted')}</p><h2>${esc(name)}</h2><p>${esc(sub)}</p></div>
      <button class="icon-btn${fav ? ' on' : ''}" data-act="fav" title="Favourite (F)">${fav ? I.favOn : I.fav}</button><button class="icon-btn" data-act="close" title="Close (Esc)">${I.close}</button></div>`;
    const badges = [];
    if (ctxBadge) badges.push(`<span class="ctx">${iconOf(ctxBadge.icon)}${esc(ctxBadge.text)}</span>`);
    badges.push(knowBadge(sys));
    if (reach.has(sys)) badges.push('<span class="badge acc">In jump range</span>');
    badges.push(!lv ? '<span class="badge">Fuel unknown</span>' : refuelAt(sys) ? '<span class="badge fuelb">Fuel star</span>' : '<span class="badge">No fuel</span>');
    if (dangerOf[sys] >= 0) badges.push(`<span class="badge haz">${esc(HZ[dangerOf[sys]].name)}</span>`);
    if (targetSys === sys) badges.push('<span class="badge acc">Target</span>');
    h += `<div class="badges">${badges.join('')}</div>`;
    if (lv >= 2 || here) {
      h += `<div class="actions">
        <button class="btn primary" data-act="route" ${here ? 'disabled' : ''} title="Plot a route (R)">${I.route}Route</button>
        <button class="btn" data-act="target" ${here ? 'disabled' : ''} title="Target (T)">${I.target}${targetSys === sys ? 'Targeted' : 'Target'}</button>
        <button class="btn" data-act="open" title="Open the system (Enter)">${I.open}Open system</button></div>
        <button class="linkbtn" data-act="drone">${I.drone}Send a drone to survey ${DRONE_R} ly around it</button>`;
    } else {
      h += `<div class="actions">
        <button class="btn primary" data-act="drone" title="Send a drone to survey 20 ly around it">${I.drone}Send drone here</button>
        <button class="btn" data-act="route" title="Plot a route (R)">${I.route}Route</button>
        <button class="btn" data-act="target" title="Target (T)">${I.target}${targetSys === sys ? 'Targeted' : 'Target'}</button></div>`;
    }
    if (dangerOf[sys] >= 0) { const dz = HZ[dangerOf[sys]]; h += `<div class="warn haz">${I.warn}<div><b>${esc(dz.name)}</b><span>${esc(dz.effect)}</span></div></div>`; }
    if (lv <= 1) h += unknownHTML(sys);
    else h += worldsHTML(sys);
    h += basesHTML(sys) + findsHTML(sys) + partOfHTML(sys);
    if (lv >= 2) {
      const res = HD.resNames(M.resources.systemMask[sys]);
      if (res.length) h += `<div class="sec"><h4>Resource hints<span>mock</span></h4><div class="res">${res.map(r => `<em>${esc(cap(r))}</em>`).join('')}</div></div>`;
    }
    h += starFactsHTML(sys);
    return h;
  }
  function unknownHTML(sys) {
    const lv = K.level[sys], n = K.countWithin([PX[sys], PY[sys], PZ[sys]], DRONE_R);
    const what = lv === 1
      ? `You have seen it from home, so you know its name, its colour and that it ${refuelAt(sys) ? 'can' : 'cannot'} be scooped. Nothing about its worlds.`
      : SYS.cat[sys] ? 'Its name, place and spectral class are in the public catalogue. Nothing else is known: no worlds, no fuel, no finds.'
      : 'Its name and place follow from the galaxy’s naming grid. Nothing else is known: no star class, no worlds, no fuel, no finds.';
    return `<div class="sec"><h4>What you know</h4><div class="knowbox"><p>${what}</p>
      <p class="kb2">A drone would survey about ${fmt(n)} systems within ${DRONE_R} ly of it and report what it finds.</p></div></div>`;
  }
  function worldsHTML(sys) {
    const s = SYS.sum[sys], pr = planetsRow(sys, 16);
    return `<div class="sec"><h4>Worlds<span>${HD.plural(s[1], 'planet')} · ${HD.plural(s[2], 'moon')} · ${s[3]} landable${s[0] > 1 ? ' · ' + HD.plural(s[0], 'star') : ''}</span></h4>
      <div class="planets" data-act="open" title="Open the system">${pr.html}</div>
      <div class="legend"><span><i></i>landable</span><span>${pr.real ? 'Full survey · open the system for details' : 'Drone summary · full body lists within 40 ly'}</span></div></div>`;
  }
  function basesHTML(sys) {
    const b = baseAt(sys), p = playerFound(sys) ? playersBy.get(sys) : null;
    if (!b && !p) return '';
    let h = `<div class="sec"><h4>Bases${p ? '<span>found by a drone</span>' : ''}</h4>`;
    if (b) h += `<div class="st-row"><div class="si" style="color:var(--accent)">${b.role === 'home' ? I.home : I.flag}</div><div class="sn"><b>${esc(b.role === 'home' ? 'Your home base' : b.name)}</b><span>${b.body ? 'On ' + esc(b.body) : 'In this system'}${b.role === 'home' ? ' · ' + esc(M.home.bodyType) : ' · your outpost'}</span></div></div>`;
    if (p) h += `<div class="st-row"><div class="si" style="color:var(--player)">${I.flag}</div><div class="sn"><b>${esc(p.name)}</b><span>${cap(p.relation)} player · on ${esc(p.body)}</span></div></div>`;
    return h + '</div>';
  }
  function findRow(f, withSys) {
    return `<button class="st-row find k-${f.kind}" data-act="find" data-sys="${f.sys}"><div class="si">${I[FIND_IC[f.kind]]}</div><div class="sn"><b>${esc(f.title)}</b><span>${esc(f.sub)}${withSys ? ' · ' + esc(shortName(f.sys)) : ''}</span></div>${withSys ? `<div class="fd">${HD.ly(distShip(f.sys))}</div>` : ''}</button>`;
  }
  function findsHTML(sys) {
    const fs = findsAt(sys).filter(f => f.kind !== 'player');
    if (!fs.length) return '';
    return `<div class="sec"><h4>Finds<span>${esc(fs[0].group ? 'reported by ' + fs[0].group.drone : '')} · mock</span></h4>${fs.map(f => findRow(f, false)).join('')}</div>`;
  }
  function partOfHTML(sys) {
    const g = groupOfStar(sys); if (!g) return '';
    return `<div class="sec"><h4>Part of</h4><button class="st-row grp" data-act="group" data-g="${g.index}"><div class="si">${g.kind === 'home' ? I.home : g.kind === 'survey' ? I.drone : I.route}</div><div class="sn"><b>${esc(g.name)}</b><span>${esc(g.summary())}${g.when ? ' · ' + esc(g.when) : ''}</span></div><div class="fd">${I.chev}</div></button></div>`;
  }
  function starFactsHTML(sys) {
    const solD = Math.hypot(PX[sys], PY[sys], PZ[sys]), lv = K.level[sys];
    const mag = SYS.mag[sys] > 25 ? '—' : SYS.mag[sys].toFixed(1);
    if (!lv && !SYS.cat[sys]) return `<div class="sec"><h4>Star</h4><div class="facts">${fact(HD.ly(solD), 'From Sol')}${fact(HD.ly(Math.hypot(PX[sys] - homeP.x, PY[sys] - homeP.y, PZ[sys] - homeP.z)), 'From home')}</div></div>`;
    return `<div class="sec"><h4>Star</h4><div class="facts">
      ${lv ? fact(fmt(SYS.teff[sys]) + ' K', 'Temperature') : ''}${fact(mag, 'Abs. magnitude')}${fact(HD.ly(solD), 'From Sol')}${fact(SYS.cat[sys] ? 'Catalogue' : 'Generated', SYS.cat[sys] ? 'Real star (HYG)' : 'Procedural')}</div></div>`;
  }
  function planetsRow(sys, maxN) {
    const bodies = L.bodies[String(sys)];
    if (bodies) {
      const items = [];
      bodies.forEach((b, i) => { if (b[1] === 1 || b[1] === 3 || (b[1] === 0 && b[3] >= 0)) items.push({ b, i }); });
      items.sort((a, b) => a.b[4] - b.b[4]);
      const html = items.slice(0, maxN).map(({ b }) => {
        if (b[1] === 3) return `<div class="pl" title="${esc(b[0])} · ${esc(b[2])}"><div class="pball" style="width:6px;height:18px;border-radius:3px;background-image:radial-gradient(circle, rgba(220,210,195,.8) 1px, transparent 1.4px);background-size:4px 5px"></div><span class="lm"></span></div>`;
        if (b[1] === 0) return `<div class="pl" title="${esc(b[0])} · ${esc(b[2])} companion star"><div class="pball" style="${ballStyle(null, 16, classCol(b[2]))}"></div><span class="lm"></span></div>`;
        const sz = Math.round(Math.min(30, Math.max(6, 4 + Math.sqrt(b[5] / 1000) * 3.1)));
        return `<div class="pl" title="${esc(b[0])} · ${esc(b[2])}${b[9] ? ' · landable' : ''}"><div class="pball${b[10] ? ' ringed' : ''}" style="${ballStyle(b[2], sz)}"></div><span class="lm${b[9] ? ' on' : ''}"></span></div>`;
      }).join('');
      return { html: html + (items.length > maxN ? `<span class="more">+${items.length - maxN}</span>` : ''), real: true };
    }
    const s = SYS.sum[sys];
    const n = s[1], gg = s[4], ig = s[5], el = s[6], ww = s[7], rocky = Math.max(0, n - gg - ig - el - ww);
    const lst = [];
    for (let k = 0; k < rocky; k++) lst.push(['Rocky world', 7 + ((sys * 7 + k * 13) % 6)]);
    for (let k = 0; k < el; k++) lst.push(['Earth-like world', 13]);
    for (let k = 0; k < ww; k++) lst.push(['Water world', 14]);
    for (let k = 0; k < gg; k++) lst.push(['Gas giant', 24 + (k % 2) * 4]);
    for (let k = 0; k < ig; k++) lst.push(['Ice giant', 18]);
    let land = s[3];
    const html = lst.slice(0, maxN).map(([t, sz], k) => {
      const l = (t === 'Rocky world' || t === 'Earth-like world' || t === 'Water world') && land-- > 0;
      return `<div class="pl" title="${esc(t)}"><div class="pball${k >= lst.length - s[8] && s[8] ? ' ringed' : ''}" style="${ballStyle(t, sz)}"></div><span class="lm${l ? ' on' : ''}"></span></div>`;
    }).join('');
    return { html: (html || '<span class="more">No planets</span>') + (lst.length > maxN ? `<span class="more">+${lst.length - maxN}</span>` : ''), real: false };
  }
  function listHTML() {
    const c = list, n = c.items.length;
    let h = `<div class="ph"><div class="orb list${c.danger ? ' danger' : ''}">${iconOf(c.icon)}</div><div class="ttl"><p class="eyebrow">${c.mock ? 'Mock resource hints' : 'Ask the map · what you know'}</p><h2>${esc(c.title)}</h2>
      <p>${n ? HD.plural(n, 'system') + (c.danger ? ' · avoid or plan around them' : ' · nearest first') : 'Nothing you know of yet · send a drone'}</p></div>
      <button class="icon-btn" data-act="close" title="Close (Esc)">${I.close}</button></div><div class="list">`;
    c.items.slice(0, 80).forEach(sys => {
      const it = { type: 'system', sys, name: SYS.name[sys], sub: (c.unit ? c.unit(sys) + ' · ' : '') + starKind(SYS.cls[sys]), dist: HD.dShip(sys), jumps: HD.jumpsTo(sys), icon: 'star' };
      h += rowHTML(it, -1, '').replace('class="row', 'class="row lrow').replace('data-i="-1"', `data-sys="${sys}"`);
    });
    if (n > 80) h += `<div class="note" style="padding:6px 12px">${n - 80} more · refine with “within 30 ly”</div>`;
    return h + '</div>';
  }
  function groupHTML(gi) {
    const g = K.groups[gi], c = new V3(g.centre[0], g.centre[1], g.centre[2]);
    const kindWord = g.kind === 'home' ? 'Home' : g.kind === 'survey' ? 'Drone survey · ' + g.drone + ' · ' + g.when : 'Past trip · ' + g.when;
    const fs = g.finds.filter(f => !f.hidden);
    let h = `<div class="ph"><div class="orb list">${g.kind === 'home' ? I.home : g.kind === 'survey' ? I.drone : I.route}</div><div class="ttl"><p class="eyebrow">${esc(kindWord)}</p><h2>${esc(g.name)}</h2>
      <p>${esc(g.summary())} · ${HD.ly(c.distanceTo(ship.pos))} from you</p></div><button class="icon-btn" data-act="close" title="Close (Esc)">${I.close}</button></div>`;
    h += `<p class="desc">${g.kind === 'home' ? 'Your home base, your outposts and the systems you have been to around them. Your space is only the feeling of having bases here: nobody claims anything.'
      : g.kind === 'survey' ? `Every system within ${Math.round(g.radius)} ly of this point, scanned by ${esc(g.drone)}: stars, worlds, resources and anything left behind.`
      : 'Systems you passed through on this trip.'}</p>`;
    h += `<div class="actions"><button class="btn primary" data-act="zoomgroup" data-g="${gi}">${I.target}Show it all</button>${g.kind === 'survey' ? `<button class="btn" data-act="routegroup" data-g="${gi}">${I.route}Route to its centre</button>` : ''}</div>`;
    if (g.kind === 'home') h += `<div class="sec"><h4>Your bases</h4>${K.bases.map(b => `<button class="st-row grp" data-act="find" data-sys="${b.sys}"><div class="si" style="color:var(--accent)">${b.role === 'home' ? I.home : I.flag}</div><div class="sn"><b>${esc(b.name)}</b><span>${esc(SYS.name[b.sys])}${b.body ? ' · on ' + esc(b.body.replace(SYS.name[b.sys] + ' ', 'planet ')) : ''}</span></div><div class="fd">${HD.ly(distShip(b.sys))}</div></button>`).join('')}</div>`;
    if (fs.length) h += `<div class="sec"><h4>Finds<span>${HD.plural(fs.length, 'find')} · mock</span></h4>${fs.map(f => findRow(f, true)).join('')}</div>`;
    const top = g.members.slice().sort((a, b) => importance[b] - importance[a]).slice(0, 8);
    h += `<div class="sec"><h4>Notable systems<span>${HD.plural(g.members.length, 'system')}</span></h4><div class="list">` + top.map(sys => {
      const it = { type: 'system', sys, name: SYS.name[sys], sub: HD.whatsThere(sys), dist: HD.dShip(sys), jumps: HD.jumpsTo(sys), icon: 'star' };
      return rowHTML(it, -1, '').replace('class="row', 'class="row lrow').replace('data-i="-1"', `data-sys="${sys}"`);
    }).join('') + '</div></div>';
    return h;
  }
  function dirWords(p) {
    const d = p.clone().sub(homeP), horiz = Math.hypot(d.x, d.z), up = d.y;
    const toward = Math.abs(d.z) > Math.abs(d.x) ? (d.z > 0 ? 'toward the Galactic centre' : 'away from the Galactic centre') : (d.x > 0 ? 'along the Sun’s orbit' : 'against the Sun’s orbit');
    return `${HD.ly(horiz)} ${toward}, ${HD.ly(Math.abs(up))} ${up >= 0 ? 'above' : 'below'} home`;
  }
  function voidHTML(p) {
    const n = K.countWithin([p.x, p.y, p.z], DRONE_R), ns = nearestStar(p, 40);
    let nk = -1, nkd = Infinity; for (const i of K.knownList) { const d = Math.hypot(PX[i] - p.x, PY[i] - p.y, PZ[i] - p.z); if (d < nkd) { nkd = d; nk = i; } }
    let h = `<div class="ph"><div class="orb unk void">${I.unknown}</div><div class="ttl"><p class="eyebrow">Unknown space</p><h2>Unknown space</h2><p>${HD.ly(p.distanceTo(ship.pos))} from you · ${esc(dirWords(p))}</p></div>
      <button class="icon-btn" data-act="close" title="Close (Esc)">${I.close}</button></div>`;
    h += `<p class="desc">Nothing is known here. From the star density, about ${fmt(n)} systems lie within ${DRONE_R} ly of this point. A drone would survey them all and report what it finds.</p>`;
    h += `<div class="actions"><button class="btn primary" data-act="drone">${I.drone}Send drone here</button>${ns >= 0 ? `<button class="btn" data-act="routevoid">${I.route}Route there</button>` : ''}</div>`;
    h += `<div class="sec"><div class="facts">${fact(HD.ly(p.distanceTo(homeP)), 'From home')}${fact('≈ ' + fmt(n), 'Systems within ' + DRONE_R + ' ly')}${nk >= 0 ? fact(esc(shortName(nk)), 'Nearest known · ' + HD.ly(nkd)) : ''}${fact(fmt(Math.round(p.y - homeP.y)) + ' ly', 'Height above home')}</div></div>`;
    return h;
  }
  function routeHTML() {
    const R = routeCtx, r = R.active, dest = R.dest, far = r.far;
    const unk = r.stops.filter((q, k) => k > 0 && !known(q.sys)).length;
    const totalJumps = r.jumps + (far ? far.jumps : 0);
    let h = `<div class="ph"><button class="icon-btn" data-act="back" title="Back (Esc)">${I.back}</button><div class="ttl"><p class="eyebrow">Route${far ? ' · beyond the star data' : ''}</p><h2>${esc(far ? far.name : known(dest) || SYS.cat[dest] ? SYS.name[dest] : 'Unknown system')}</h2>
      <p>From ${esc(SYS.name[r.from])}${r.from === M.home.sys ? ' (home)' : ''} · ${far ? HD.ly(far.total) + ' away' : known(dest) ? starKind(SYS.cls[dest]) : 'unknown space'}</p></div>
      <button class="icon-btn" data-act="close" title="Close (Esc)">${I.close}</button></div>`;
    h += `<div class="rsum"><span class="big">${far ? '≈ ' + fmt(totalJumps) + ' jumps' : HD.plural(r.jumps, 'jump')}</span><span class="sep">·</span><span class="mid">${r.refuels ? HD.plural(r.refuels, 'fuel stop') + (far ? ' first' : '') : 'No refuelling'}</span><span class="sep">·</span><span class="mid">${HD.minutes(r.seconds + (far ? far.jumps * 34 : 0))}</span></div>
      <p class="rsub">${far ? `${HD.plural(r.jumps, 'jump')} across the 150 ly this map knows stars for, then ≈ ${fmt(far.jumps)} through unknown space · ` : ''}${HD.ly(r.lengthLy)} travelled · ${HD.RANGE} ly jumps · tank ${HD.FUEL_MAX} jumps · fuel stars only · planned in ${Math.max(1, Math.round(R.ms))} ms</p>`;
    if (unk || far) h += `<div class="warn unkw">${I.unknown}<div><b>${far ? 'Most of this trip is unknown space' : HD.plural(unk, 'stop') + ' in unknown space'}</b><span>${far ? 'The autopilot plots it leg by leg as the ship scans ahead; fuel stops out there are found on the way. Drones can scout it first.' : 'The planner knows where stars are, not what they hold: the ship scans each one on arrival, so fuel there is unconfirmed.'}</span></div></div>`;
    if (R.safe) {
      const f = R.fast, sf = R.safe;
      h += `<div class="alts">
        <button class="alt${r === f ? ' on' : ''}" data-act="alt-fast"><b>Fastest · ${HD.plural(f.jumps, 'jump')}</b><span class="tagw">Through ${esc(f.dangers.map(d => d.region.name).join(', '))}</span></button>
        <button class="alt${r === sf ? ' on' : ''}" data-act="alt-safe"><b>Safer · ${HD.plural(sf.jumps, 'jump')}</b><span class="tags">Avoids the storm · +${sf.jumps - f.jumps} jump${sf.jumps - f.jumps === 1 ? '' : 's'}</span></button></div>`;
    }
    for (const d of r.dangers) {
      const z = d.region;
      h += `<div class="warn haz">${I.warn}<div><b>Passes the ${esc(z.name)}</b><span>${esc(z.effect)} ${HD.plural(d.count, 'stop')} inside.</span></div></div>`;
    }
    h += `<div class="go">${auto ? `<button class="btn wide" data-act="autostop">${I.stop}Stop autopilot</button>` : `<button class="btn primary wide" data-act="auto">${I.play}Start autopilot</button>`}</div>
      <div class="prog${auto ? ' on' : ''}"><div class="pt"></div><div class="pbar"><i></i></div></div>`;
    h += `<button class="steps-t${R.stepsOpen ? ' open' : ''}" data-act="steps">${I.chev}${HD.plural(r.jumps + (far ? 1 : 0), 'step')}</button><ol class="steps${R.stepsOpen ? ' open' : ''}">`;
    const n = r.stops.length;
    r.stops.forEach((s, k) => {
      let kind = '', icn = '<i></i>', cls = '';
      const kn = known(s.sys), nm = kn || SYS.cat[s.sys] ? SYS.name[s.sys] : 'Unknown system';
      if (k === 0) { cls = 'start'; kind = 'Start · ' + (s.sys === M.home.sys ? 'home base' : starKind(SYS.cls[s.sys])); }
      else {
        kind = kn ? HD.ly(s.hop) + ' · ' + SYS.cls[s.sys] : 'Unknown · ' + HD.ly(s.hop);
        if (!kn) { cls = 'unk'; icn = `<i>${I.unknown}</i>`; }
        if (s.danger >= 0) { cls = 'dan'; icn = `<i>${I.warn}</i>`; kind = HZ[s.danger].name + ' · ' + HD.ly(s.hop); }
        if (s.refuel) { cls = kn ? 'ref' : 'ref unkref'; icn = `<i>${I.fuel}</i>`; kind = (kn ? 'Fuel star · scoop the ' + starKind(SYS.cls[s.sys]).toLowerCase() : 'Fuel if it can be scooped · unconfirmed') + ' · ' + HD.ly(s.hop); }
        if (k === n - 1 && !far) { cls = 'end'; icn = `<i>${I.target}</i>`; kind = 'Arrive · ' + HD.ly(s.hop); }
      }
      const fo = s.refuel ? s.fuelOut : s.fuelIn;
      const mf = '<span class="minifuel">' + Array.from({ length: HD.FUEL_MAX }, (_, q) => `<i class="${q < fo ? '' : 'off'}"></i>`).join('') + '</span>';
      h += `<li data-sys="${s.sys}"${kn ? '' : ' class="unknown"'}><span class="sk ${cls}">${icn}</span><div class="sn"><b>${esc(nm)}</b><span>${esc(kind)}</span></div><div class="sr">${mf}</div></li>`;
    });
    if (far) h += `<li class="unknown far" data-far="1"><span class="sk unk"><i>${I.unknown}</i></span><div class="sn"><b>Unknown space</b><span>≈ ${fmt(far.jumps)} jumps · ${HD.ly(far.ly)} · plotted leg by leg</span></div><div class="sr"></div></li>
      <li data-far="1"><span class="sk end"><i>${I.target}</i></span><div class="sn"><b>${esc(far.name)}</b><span>Arrive</span></div><div class="sr"></div></li>`;
    return h + '</ol>';
  }
  const STRUCT_TEXT = {
    'galactic-centre': 'The heart of the Milky Way. A black hole of about four million suns sits inside a dense disk of old stars. Everything on this map orbits it once every 230 million years.',
    'bar': 'A long bar of old, reddish stars runs through the core, about 20,000 light-years end to end. The spiral arms begin near its tips.',
    'scutum-centaurus-arm': 'One of the two great arms, crowded with young blue stars and star-forming clouds on the far side of the core.',
    'sagittarius-carina-arm': 'The arm just inside ours, 4,200 ly toward the core. Its bright young clusters are the nearest big nurseries.',
    'perseus-arm': 'The next great arm outward, 4,600 ly beyond the Sun. Rich, young and wholly unknown to you.',
    'norma-arm': 'The inner end of the arm that wraps all the way round to the Outer Arm, hugging the bar.',
    'outer-arm': 'The thin outer edge of the disk. Stars are sparse here and the dark between them is wide.',
    'orion-spur': 'Home. A short spur between the Sagittarius and Perseus arms. Everything you know lies inside it, in a patch a few dozen light-years across.',
  };
  function structHTML(id) {
    const s = STRUCTS.find(x => x.id === id); if (!s) return '';
    const d = Math.hypot(s.label[0], s.label[1], s.label[2]);
    const kindWord = { centre: 'Galactic Centre', bar: 'Galactic bar', arm: 'Spiral arm', spur: 'Spur · you are here' }[s.kind];
    return `<div class="ph"><div class="orb ${s.kind === 'centre' || s.kind === 'bar' ? 'core' : 'arm'}"></div><div class="ttl"><p class="eyebrow">Galaxy · unknown to you</p><h2>${esc(s.name)}</h2><p>${kindWord} · ${HD.ly(d)} from Sol</p></div>
      <button class="icon-btn" data-act="close" title="Close (Esc)">${I.close}</button></div>
      <p class="desc">${STRUCT_TEXT[s.id] || esc(s.note || '')}</p>
      <div class="sec"><div class="facts">
        ${fact(HD.ly(d), 'From Sol')}${fact('≈ ' + fmt(Math.ceil(d / HD.RANGE * 1.08)), 'Jumps at ' + HD.RANGE + ' ly')}
        ${fact(fmt(Math.round(d)) + ' yr', 'Light travel time')}${fact(s.radiusFromCentreLy ? HD.ly(s.radiusFromCentreLy) : '—', 'From the centre')}</div></div>
      <div class="actions">${s.id === 'orion-spur' ? `<button class="btn primary" data-act="neigh">${I.ship}Back to known space</button>` : `<button class="btn primary" data-act="farroute" data-struct="${s.id}">${I.route}Route there</button><button class="btn" data-act="neigh">${I.ship}Known space</button>`}</div>`;
  }
  function sectorHTML(k) {
    const s = SECTORS[k], d = Math.hypot(s[4], s[5], s[6]), here = k === 0;
    const est = glowAt(glowAll, G.glow.all, s[4], s[6]) * (1024 * 1024) / (GCELL * GCELL) * (s[2] === 64 ? 0.8 : 0.12);
    const n = est > 1e6 ? fmt(est / 1e6, est > 1e7 ? 0 : 1) + ' M' : fmt(Math.round(est / 1000) * 1000);
    return `<div class="ph"><div class="orb sector">${I.sector}</div><div class="ttl"><p class="eyebrow">Sector · ${esc(armOf(new V3(s[4], 0, s[6])))}</p><h2>${esc(s[0])}</h2>
      <p>1,024 ly cube · grid ${s[1]}, ${s[2]}, ${s[3]} · ${here ? 'contains Sol' : HD.ly(d) + ' from Sol'}</p></div><button class="icon-btn" data-act="close" title="Close (Esc)">${I.close}</button></div>
      <p class="desc">Every system in Hollow belongs to a sector; generated names start with the sector name${here ? ', which is why so many stars around you are called “Nolcas Peris …”' : '.'} You know ${here ? fmt(K.knownList.length) + ' of its systems' : 'none of its systems'}.</p>
      <div class="sec"><div class="facts">${fact('≈ ' + n, 'Systems (density model)')}${fact(s[2] === 64 ? 'Galactic plane' : s[2] > 64 ? 'Above the plane' : 'Below the plane', 'Layer')}</div></div>
      <div class="actions">${here ? '' : `<button class="btn primary" data-act="farroute" data-sector="${k}">${I.route}Route there</button>`}<button class="btn${here ? ' primary' : ''}" data-act="neigh">${I.ship}${here ? 'Zoom to known space' : 'Known space'}</button></div>`;
  }
  function systemHTML() {
    const i = SY.i, here = i === ship.sys, s = SYS.sum[i], fav = favs.has(i), lv = K.level[i];
    const sub = [SYS.cls[i], starKind(SYS.cls[i]), here ? 'You are here' : HD.ly(distShip(i)) + ' · ' + HD.jumpsText(i)].join(' · ');
    let h = `<div class="ph"><div class="orb" style="--c:${SYS.col[i]}"></div><div class="ttl"><p class="eyebrow">System · Simple view</p><h2>${esc(SYS.name[i])}</h2><p>${esc(sub)}</p></div>
      <button class="icon-btn${fav ? ' on' : ''}" data-act="fav" title="Favourite (F)">${fav ? I.favOn : I.fav}</button><button class="icon-btn" data-act="closesys" title="Back to the map (Esc)">${I.close}</button></div>`;
    const badges = [knowBadge(i)];
    badges.push(!lv ? '<span class="badge">Fuel unknown</span>' : refuelAt(i) ? '<span class="badge fuelb">Fuel star</span>' : '<span class="badge">No fuel</span>');
    if (SY.schematic && !SY.unknownWorlds) badges.push('<span class="badge">Schematic</span>');
    if (dangerOf[i] >= 0) badges.push(`<span class="badge haz">${esc(HZ[dangerOf[i]].name)}</span>`);
    h += `<div class="badges">${badges.join('')}</div>`;
    h += `<div class="actions">
      <button class="btn primary" data-act="route" ${here ? 'disabled' : ''}>${I.route}Route here</button>
      ${lv <= 1 ? `<button class="btn" data-act="drone">${I.drone}Drone</button>` : `<button class="btn" data-act="target" ${here ? 'disabled' : ''}>${I.target}${targetSys === i ? 'Targeted' : 'Target'}</button>`}
      <button class="btn" data-act="closesys">${I.back}Map</button></div>`;
    if (SY.unknownWorlds) h += unknownHTML(i);
    else h += `<div class="sec"><h4>Worlds<span>${HD.plural(s[1], 'planet')} · ${HD.plural(s[2], 'moon')} · ${s[3]} landable</span></h4><p class="note">Choose a world in the row${simple && simple.order.length > 1 ? ' · <kbd>←</kbd> <kbd>→</kbd> to step through' : ''}. Switch to <b>Realistic</b> for orbits, moons and time.</p></div>`;
    h += basesHTML(i) + findsHTML(i) + partOfHTML(i);
    if (lv >= 2) {
      const res = HD.resNames(M.resources.systemMask[i]);
      if (res.length) h += `<div class="sec"><h4>Resource hints<span>mock</span></h4><div class="res">${res.map(r => `<em>${esc(cap(r))}</em>`).join('')}</div></div>`;
    }
    h += starFactsHTML(i);
    return h;
  }

  // ---------- System level, Realistic: Hollow's own system map (bodies left, time on top, body card right) ----------
  const TYPE_COL = { 'Earth-like world': '#5ccf8c', 'Water world': '#6fb4ff', 'Gas giant': '#e3b47a', 'Ice giant': '#7fd3e6', 'Rocky world': '#d2ad8c',
    'Rocky ice world': '#b8c6d6', 'Icy world': '#dce9f5', 'Metal-rich world': '#c2b6ab', 'Ammonia world': '#d8c46e', Moon: '#c0c4ca', 'Asteroid belt': '#b4aa9e' };
  const typeCol = n => n.kind === 0 ? classCol(n.type || SYS.cls[SY.i]) : TYPE_COL[n.type] || '#c8ced8';
  const smEl = { left: $('#sm-left'), sys: $('#sm-sys'), bodies: $('#sm-bodies'), time: $('#sm-time'), keys: $('#sm-keys') };
  const kv = (k, v) => `<dt>${k}</dt><dd>${v}</dd>`;
  const fmtDays = d => d < 2 ? fmt(d * 24, 1) + ' h' : d < 1000 ? fmt(d, 1) + ' d' : fmt(d / 365.25, 2) + ' yr';
  function bodyCardHTML() {
    const i = SY.i, n = SY.nodes[Math.max(0, sysSel)] || SY.prim;
    let h = `<div class="hb"><p class="h-eyebrow">Selected body</p><h2>${esc(n.name)}</h2><p class="h-type" style="color:${typeCol(n)}">${esc(n.kind === 0 ? (n.type || SYS.cls[i]) + ' · ' + starKind(n.type || SYS.cls[i]) : n.type || 'Body')}</p><div class="h-rule"></div><dl class="h-kv">`;
    if (SY.unknownWorlds) {
      h += kv('Class', esc(SYS.cls[i])) + kv('Worlds', 'Unknown') + kv('From you', HD.ly(distShip(i))) + '</dl>';
      return h + `<div class="h-note">Nothing is known about this system's worlds. A drone survey would map them.</div><div class="h-acts"><button class="hbtn pri" data-act="drone">${I.drone}Send a drone</button></div></div>`;
    }
    if (n.kind === 0) {
      const s = SYS.sum[i];
      h += kv('Class', esc(n.type || SYS.cls[i])) + kv('Temperature', n.t ? fmt(n.t) + ' K' : '—') + (n === SY.prim ? kv('Planets', s[1]) + kv('Moons', s[2]) + kv('Landable', s[3]) : kv('Orbits', esc(SY.prim.name)) + kv('Semi-major axis', n.a.toPrecision(3) + ' AU'));
      h += kv('Fuel', refuelAt(i) && n === SY.prim ? 'Scoopable' : n === SY.prim ? 'Not scoopable' : '—');
    } else if (n.kind === 3) {
      h += kv('Orbits', esc(SY.prim.name)) + kv('Distance', SY.schematic ? '—' : n.a.toPrecision(3) + ' AU') + kv('Landable', 'No');
    } else {
      const par = SY.nodes[n.parent] || SY.prim, me = massEarth(n), moons = n.kids.filter(m => m.kind === 2).length;
      h += kv('Orbits', esc(par.short === par.name ? par.name : par.name)) + kv('Semi-major axis', SY.schematic ? '—' : (n.a < 0.01 ? fmt(n.a * 149.6e6) + ' km' : n.a.toPrecision(3) + ' AU'))
        + kv('Orbital period', SY.schematic && n.kind !== 2 ? '—' : fmtDays(n.period))
        + (SY.schematic ? '' : kv('Mass', (me < 0.01 ? me.toPrecision(2) : fmt(me, me < 10 ? 3 : 1)) + ' × Earth') + kv('Radius', fmt(n.rkm) + ' km') + kv('Gravity', n.g.toFixed(2) + ' g') + kv('Temperature', fmt(n.t) + ' K')
          + kv('Atmosphere', esc(cap(n.atm || 'none'))))
        + kv('Landable', n.land ? 'Yes' : 'No') + kv('Moons', moons) + (n.rings ? kv('Rings', 'Yes') : '');
    }
    h += '</dl>';
    const tags = [];
    if (n.home) tags.push(`<div class="h-tag acc">${I.home}<b>Your home base</b></div>`);
    if (n.player) tags.push(`<div class="h-tag pl">${I.flag}<b>${esc(n.player.name)}</b><span>${cap(n.player.relation)} player</span></div>`);
    const fd = findsAt(i).find(f => f.body === n.name);
    if (fd) tags.push(`<div class="h-tag fd">${I[FIND_IC[fd.kind]]}<b>${esc(fd.title)}</b><span>${esc(FIND_WORD[fd.kind])} · mock</span></div>`);
    if (tags.length) h += `<div class="h-tags">${tags.join('')}</div>`;
    const res = n.kind > 0 ? HD.resNames(n.res) : [];
    if (res.length) h += `<p class="h-sub">Resources · mock hints</p><div class="res">${res.map(r => `<em>${esc(cap(r))}</em>`).join('')}</div>`;
    return h + '</div>';
  }
  function sysCardHTML() {
    const i = SY.i, s = SYS.sum[i], lv = K.level[i];
    const counts = SY.unknownWorlds ? 'Worlds unknown' : [HD.plural(s[0], 'star'), HD.plural(s[1], 'planet'), HD.plural(s[2], 'moon'), s[9] ? HD.plural(s[9], 'belt') : ''].filter(Boolean).join(' · ');
    return `<p class="h-eyebrow">System map</p><h2>${esc(SYS.name[i])}</h2><p class="h-cls">${esc(SYS.cls[i])} · ${esc(starKind(SYS.cls[i]))}</p>
      <p class="h-counts">${counts}</p><p class="h-dist">${i === ship.sys ? 'You are here' : HD.ly(distShip(i)) + ' from you'} · ${esc(cap(K.whenOf(i) || LVW[lv].toLowerCase()))}</p>
      <button class="hbtn wide" data-sm="back">${I.back}Galaxy map</button>
      <div class="h-acts"><button class="hbtn" data-sm="route" ${i === ship.sys ? 'disabled' : ''}>${I.route}Route here</button><button class="hbtn" data-sm="target" ${i === ship.sys ? 'disabled' : ''}>${I.target}${targetSys === i ? 'Targeted' : 'Target'}</button>${lv <= 1 ? `<button class="hbtn" data-sm="drone">${I.drone}Drone</button>` : ''}</div>`;
  }
  function bodiesListHTML() {
    const order = []; const walk = (n, d) => { order.push([n, d]); n.kids.slice().sort((p, q) => p.a - q.a).forEach(m => walk(m, d + 1)); }; walk(SY.prim, 0);
    return `<p class="h-eyebrow">Bodies</p><div class="h-list">` + order.map(([n, d]) => {
      const ic = n.kind === 0 ? I.star : n.kind === 3 ? I.beltI : n.kind === 2 ? I.dot : I.planet;
      const f = findsAt(SY.i).find(q => q.body === n.name);
      return `<button class="h-row${d > 1 ? ' moon' : ''}${n.kind === 0 ? ' star' : ''}${sysSel === n.k ? ' on' : ''}" data-body="${n.k}" style="--tc:${typeCol(n)}"><span class="h-ic">${ic}</span><span class="h-n">${n.home ? `<span class="mk-ic home">${I.home}</span>` : f ? `<span class="mk-ic find">${I[FIND_IC[f.kind]]}</span>` : ''}${esc(n === SY.prim ? n.name : n.short)}</span><span class="h-t">${esc(n.kind === 0 ? (n.type || SYS.cls[SY.i]) : n.type || '')}</span></button>`;
    }).join('') + (SY.unknownWorlds ? '<div class="h-note">Its planets and moons are unknown.</div>' : '') + '</div>';
  }
  function renderSysmap() {
    const on = mode === 'system' && sysView === 'realistic' && SY;
    document.body.classList.toggle('sysreal', !!on);
    panelsDirty = true;
    if (!on) return;
    smEl.sys.innerHTML = sysCardHTML();
    smEl.bodies.innerHTML = bodiesListHTML();
    const r = smEl.bodies.querySelector('.h-row.on'); if (r) r.scrollIntoView({ block: 'nearest' });
    renderTime();
  }
  let timeKey = '';
  function renderTime() {
    const d = new Date(EPOCH + sim.days * 86400000);
    const MON = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    const date = `${d.getUTCDate()} ${MON[d.getUTCMonth()]} ${d.getUTCFullYear()}`, tm = `${String(d.getUTCHours()).padStart(2, '0')}:${String(d.getUTCMinutes()).padStart(2, '0')} UTC`;
    const st = sim.playing ? SPEEDS[sim.speed][1] : 'Paused';
    const key = date + tm + st + sim.playing + sim.trueScale;
    if (key === timeKey) return; timeKey = key;
    smEl.time.innerHTML = `<div class="tb-btns"><button class="tbtn" data-tm="slower" title="Slower (,)">${I.rew}</button><button class="tbtn" data-tm="play" title="Play / pause (Space)">${sim.playing ? I.pause : I.play}</button><button class="tbtn" data-tm="faster" title="Faster (.)">${I.ff}</button></div>
      <div class="tb-date"><b>${date}&nbsp;&nbsp;${tm}</b><span>${st}</span></div>
      <div class="tb-seg"><button class="${sim.trueScale ? '' : 'on'}" data-tm="comp" title="Compressed (L)">Compressed</button><button class="${sim.trueScale ? 'on' : ''}" data-tm="true" title="True scale (L)">True scale</button></div>`;
  }
  smEl.time.addEventListener('click', e => {
    const b = e.target.closest('[data-tm]'); if (!b) return;
    const t = b.dataset.tm;
    if (t === 'play') sim.playing = !sim.playing;
    else if (t === 'slower') { sim.speed = Math.max(0, sim.speed - 1); sim.playing = true; }
    else if (t === 'faster') { sim.speed = Math.min(SPEEDS.length - 1, sim.speed + 1); sim.playing = true; }
    else if (t === 'comp') sim.trueScale = false;
    else if (t === 'true') sim.trueScale = true;
    renderTime();
  });
  smEl.left.addEventListener('click', e => {
    const row = e.target.closest('.h-row'); if (row) { const k = +row.dataset.body; sysSel = k; focusBody(k); renderInspector(); renderSysmap(); updateHash(); return; }
    const b = e.target.closest('[data-sm]'); if (!b || b.disabled) return;
    const a = b.dataset.sm, i = SY ? SY.i : -1;
    if (a === 'back') exitSystem();
    else if (a === 'route' && i >= 0) { exitSystem(true); planTo(i); }
    else if (a === 'target' && i >= 0) { setTarget(i); renderSysmap(); }
    else if (a === 'drone' && i >= 0) sendDrone({ sys: i });
  });
  simpleEl.addEventListener('click', e => { const b = e.target.closest('[data-sact="drone"]'); if (b && simple) sendDrone({ sys: simple.i }); });

  let lastInspKey = '', panelsDirty = true;
  function inspectorHTML() {
    if (mode === 'system' && SY) return sysView === 'realistic' ? ['body' + SY.i + ':' + sysSel, bodyCardHTML()] : ['sys' + SY.i + sysView, systemHTML()];
    if (routeCtx && sel && ((sel.t === 'star' && sel.i === routeCtx.dest && !routeCtx.active.far) || (sel.t === 'farroute' && routeCtx.active.far))) return ['route' + routeCtx.dest + (routeCtx.active.far ? 'far' : ''), routeHTML()];
    if (sel && sel.t === 'star') return ['star' + sel.i + ':' + K.level[sel.i], placeHTML(sel.i)];
    if (sel && sel.t === 'void') return ['void' + sel.p.x.toFixed(1) + sel.p.z.toFixed(1), voidHTML(sel.p)];
    if (sel && sel.t === 'group') return ['group' + sel.g + ':' + K.groups[sel.g].members.length, groupHTML(sel.g)];
    if (list) return ['list' + list.id + list.title, listHTML()];
    if (sel && sel.t === 'sector') return ['sector' + sel.k, sectorHTML(sel.k)];
    if (sel && sel.t === 'struct') return ['struct' + sel.id, structHTML(sel.id)];
    return ['', ''];
  }
  function renderInspector(swap) {
    const [key, h] = inspectorHTML();
    const open = !!key;
    document.body.classList.toggle('insp-open', open);
    ui.insp.parentElement.classList.toggle('hollow', mode === 'system' && sysView === 'realistic');
    panelsDirty = true;
    if (!open) { lastInspKey = ''; return; }
    const scroll = ui.insp.scrollTop;
    ui.insp.innerHTML = h;
    if (swap && key !== lastInspKey) { ui.insp.classList.remove('swap'); void ui.insp.offsetWidth; ui.insp.classList.add('swap'); ui.insp.scrollTop = 0; }
    else ui.insp.scrollTop = scroll;
    lastInspKey = key;
    if (auto) updateAutopilotUI();
  }
  ui.insp.addEventListener('click', e => {
    const lrow = e.target.closest('.lrow'); if (lrow) { selectStar(+lrow.dataset.sys, { fromList: !!list, ctx: list ? { icon: list.icon, text: list.title } : null }); return; }
    const brow = e.target.closest('.brow'); if (brow) { const k = +brow.dataset.body; sysSel = sysSel === k ? -1 : k; focusBody(sysSel); renderInspector(); updateHash(); return; }
    const li = e.target.closest('.steps li'); if (li && !auto) { if (li.dataset.far) { const f = routeCtx && routeCtx.active.far; if (f) flyTo({ a: f.p.clone(), ld: Math.max(cam.goal.ld, log10(f.total * 0.9)) }, 1.6); } else flyTo({ a: starPos(+li.dataset.sys), ld: Math.min(cam.goal.ld, log10(45)) }, 1.0); return; }
    const b = e.target.closest('[data-act]'); if (!b || b.disabled) return;
    const act = b.dataset.act, cur = mode === 'system' && SY ? SY.i : sel && sel.t === 'star' ? sel.i : routeCtx ? routeCtx.dest : -1;
    if (act === 'close') { if (auto) stopAutopilot(); clearAll(); }
    else if (act === 'back') backOut();
    else if (act === 'route' && cur >= 0) { if (mode === 'system') exitSystem(true); planTo(cur); }
    else if (act === 'target' && cur >= 0) setTarget(cur);
    else if (act === 'fav' && cur >= 0) toggleFav(cur);
    else if (act === 'open' && cur >= 0) enterSystem(cur);
    else if (act === 'closesys') exitSystem();
    else if (act === 'auto') startAutopilot();
    else if (act === 'autostop') stopAutopilot();
    else if (act === 'drone') { if (mode === 'system' && SY) sendDrone({ sys: SY.i }); else if (sel && sel.t === 'void') sendDrone({ p: sel.p.clone() }); else if (cur >= 0) sendDrone({ sys: cur }); }
    else if (act === 'find') selectStar(+b.dataset.sys, { ctx: null });
    else if (act === 'group') selectGroup(+b.dataset.g, false);
    else if (act === 'zoomgroup') frameGroup(+b.dataset.g);
    else if (act === 'routegroup') { const g = K.groups[+b.dataset.g], i = nearestStar(new V3(g.centre[0], g.centre[1], g.centre[2]), 40); if (i >= 0) planTo(i); }
    else if (act === 'routevoid' && sel && sel.t === 'void') { const i = nearestStar(sel.p, 40); if (i >= 0) planTo(i); }
    else if (act === 'farroute') { if (b.dataset.struct) { const s = STRUCTS.find(x => x.id === b.dataset.struct); planFar(s.name, new V3(s.label[0], s.label[1], s.label[2])); } else { const s = SECTORS[+b.dataset.sector]; planFar(s[0], new V3(s[4], s[5], s[6])); } }
    else if (act === 'alt-fast' || act === 'alt-safe') { if (auto) return; routeCtx.active = act === 'alt-safe' ? routeCtx.safe : routeCtx.fast; drawRoute(); renderInspector(); updateHash(); }
    else if (act === 'steps') { routeCtx.stepsOpen = !routeCtx.stepsOpen; b.classList.toggle('open', routeCtx.stepsOpen); $('.steps', ui.insp).classList.toggle('open', routeCtx.stepsOpen); }
    else if (act === 'neigh') { clearAll(); goLevel(2); flyTo({ a: ship.pos.clone(), ld: LOG.home, pitch: topView ? PITCH_MAX : HOME_PITCH }, 1.4); }
    else if (act === 'region') { clearAll(); goLevel(1); }
  });
  function selectGroup(gi, fly) {
    if (mode === 'system') exitSystem(true);
    select({ t: 'group', g: gi }, { keepCam: true });
    if (fly) frameGroup(gi);
  }
  function frameGroup(gi) {
    const g = K.groups[gi]; if (!g) return;
    hideHint(); if (mode === 'system') exitSystem(true);
    const c = new V3(g.centre[0], g.centre[1], g.centre[2]);
    flyTo({ a: c, ld: clamp(log10(Math.max(g.radius, 8) * 3.4 + 12), log10(30), log10(420)), pitch: topView ? PITCH_MAX : cam.goal.pitch }, 1.3);
  }
  function setTarget(i) {
    targetSys = targetSys === i ? -1 : i;
    renderShip(); renderInspector();
    toast(I.target, targetSys < 0 ? 'Target cleared' : 'Target set · ' + esc(SYS.name[i]) + ' · ' + HD.jumpsText(i));
  }
  function toggleFav(i) {
    if (favs.has(i)) { favs.delete(i); toast(I.fav, 'Removed from favourites'); }
    else { favs.add(i); toast(I.favOn, 'Added to favourites · ' + esc(SYS.name[i])); }
    store.set('favs', [...favs]);
    renderInspector();
  }
  let sysFollow = false;
  function focusBody(k, instant) {
    if (!SY) return;
    const n = k >= 0 ? SY.nodes[k] : null;
    sysFollow = !!n;
    if (n) sysFlyTo({ a: n.pos.clone().multiplyScalar(SY.root.scale.x), ld: Math.min(scam.goal.ld, log10(clamp((n.ext || n.size) * 15 + 26, 42, SY.maxR * 1.3))) }, instant ? 0.001 : 0.9);
    else sysFlyTo({ a: new V3(), ld: log10(SY.maxR * 2.25) }, 0.9);
  }

  // ---------- Route (concept A's planner and itinerary; unknown stretches shown as unknown) ----------
  function clearRoute() {
    if (!routeCtx || auto) return;
    routeCtx = null; drawRoute();
  }
  function planTo(dest, opts = {}) {
    if (dest === ship.sys) { toast(I.check, 'You are already here'); return; }
    const tStart = performance.now();
    const fast = HD.planRoute(ship.sys, dest, { fuel: ship.fuel });
    if (!fast || !fast.ok) { toast(I.warn, 'No route within jump range'); return; }
    let safe = null;
    if (fast.dangers.length) { const s = HD.planRoute(ship.sys, dest, { fuel: ship.fuel, avoid: true }); if (s && s.ok && s.dangers.length < fast.dangers.length) safe = s; }
    routeCtx = { dest, fast, safe, active: opts.safe && safe ? safe : fast, ms: performance.now() - tStart, stepsOpen: !!opts.steps };
    targetSys = dest; renderShip();
    sel = { t: 'star', i: dest }; list = null; listSet = null; ctxBadge = null; fromList = false; restyle();
    drawRoute(); renderInspector(true); updateHash();
    if (mode === 'map' && (cam.goal.ld < B_REG + 0.3 || INSTANT_NOW)) frameStars(routeCtx.active.stops.map(s => s.sys));
  }
  // Anywhere stays reachable: past the 150 ly of star data, the route runs on in a straight line through unknown space.
  function planFar(name, p) {
    if (mode === 'system') exitSystem(true);
    const tStart = performance.now(), dir = p.clone().sub(ship.pos).normalize();
    let best = -1, bs = -Infinity;
    for (let i = 0; i < N; i++) {
      const r = Math.hypot(PX[i], PY[i], PZ[i]); if (r < 120) continue;
      const q = starPos(i).sub(ship.pos), along = q.dot(dir), side = q.clone().addScaledVector(dir, -along).length();
      const sc = along - side * 2.5; if (sc > bs) { bs = sc; best = i; }
    }
    const local = best >= 0 ? HD.planRoute(ship.sys, best, { fuel: ship.fuel }) : null;
    if (!local || !local.ok) { toast(I.warn, 'No route out of the known stars in that direction'); return; }
    const ly = starPos(best).distanceTo(p);
    local.far = { name, p: p.clone(), ly, jumps: Math.ceil(ly / HD.RANGE * 1.08), total: ship.pos.distanceTo(p) };
    routeCtx = { dest: best, fast: local, safe: null, active: local, ms: performance.now() - tStart, stepsOpen: true };
    targetSys = -1; renderShip();
    sel = { t: 'farroute' }; list = null; listSet = null; ctxBadge = null; fromList = false; restyle();
    drawRoute(); renderInspector(true); updateHash();
    flyTo({ a: ship.pos.clone().lerp(p, 0.5), ld: clamp(log10(local.far.total * 1.5), LOG.region - 0.4, LOG.galaxy), pitch: topView ? PITCH_MAX : 0.9 }, 1.8);
  }

  // ---------- Autopilot (animates the ship along the route; every stop becomes visited) ----------
  const HOP_S = 1.05, STOP_S = 0.32, REFUEL_S = 0.9;
  function startAutopilot() {
    if (!routeCtx || auto) return;
    const r = routeCtx.active;
    if (r.stops[0].sys !== ship.sys) { toast(I.warn, 'Re-plot the route from where you are'); return; }
    if (mode === 'system') exitSystem(true);
    auto = { r, k: 0, phase: 'hop', t: 0 };
    toast(I.play, 'Autopilot engaged · ' + HD.plural(r.jumps, 'jump') + ' to ' + esc(r.far ? 'the edge of the star data' : SYS.name[r.to]));
    flyTo({ a: ship.pos.clone(), ld: clamp(cam.goal.ld, log10(40), log10(110)) }, 0.8);
    renderInspector(); renderShip();
  }
  function stopAutopilot(silent) {
    if (!auto) return;
    const r = auto.r, at = r.stops[auto.k].sys;
    auto = null; ship.sys = at; ship.pos.copy(starPos(at)); syncShip(); updateReach(); updateImportance();
    renderShip();
    if (!silent) toast(I.stop, 'Autopilot stopped at ' + esc(SYS.name[at]));
    if (at !== r.to && !r.far) planTo(r.to, { steps: routeCtx && routeCtx.stepsOpen }); else { routeCtx = null; drawRoute(); renderInspector(); }
  }
  function tickAutopilot(dt) {
    const a = auto; if (!a || !routeCurve) return;
    const r = a.r, n = r.stops.length;
    a.t += dt;
    if (a.phase === 'hop') {
      const u = Math.min(1, a.t / HOP_S), e = u < 0.5 ? 2 * u * u : 1 - Math.pow(-2 * u + 2, 2) / 2;
      ship.pos.copy(routeCurve.getPoint(Math.min(1, (a.k + e) / (n - 1))));
      if (!tween) cam.goal.a.copy(ship.pos);
      if (u >= 1) {
        a.k++; a.t = 0;
        const s = r.stops[a.k];
        const cost = (HD.stormy[s.sys] || HD.stormy[r.stops[a.k - 1].sys]) ? 2 : 1;
        ship.fuel = Math.max(0, ship.fuel - cost); ship.sys = s.sys; HD.ship.sys = s.sys; HD.ship.fuel = ship.fuel;
        a.phase = s.refuel ? 'refuel' : 'stop';
        if (K.visit(s.sys, performance.now())) knowledgeChanged();      // the ship scans every star it stops at
        setRouteProgress(a.k); updateReach(); counts.reach = reach.size; if (activeFilters.has('reach')) restyle();
        renderShip(); renderChips(); updateAutopilotUI();
      }
    } else {
      const dur = a.phase === 'refuel' ? REFUEL_S : STOP_S;
      if (a.phase === 'refuel' && a.t > dur * 0.6 && ship.fuel < ship.max) { ship.fuel = ship.max; HD.ship.fuel = ship.fuel; renderShip(); }
      if (a.t >= dur) {
        if (a.k >= n - 1) { arrive(); return; }
        a.phase = 'hop'; a.t = 0; updateAutopilotUI();
      }
    }
  }
  function arrive() {
    const r = auto.r;
    auto = null; ship.sys = r.to; ship.pos.copy(starPos(r.to)); syncShip(); updateReach(); updateImportance();
    routeCtx = null; drawRoute();
    if (targetSys === r.to) targetSys = -1;
    renderShip();
    if (r.far) toast(I.unknown, `Edge of this prototype's star data · in the game the autopilot carries on, ≈ ${fmt(r.far.jumps)} jumps to ${esc(r.far.name)}`, 5200);
    else toast(I.check, 'Arrived at ' + esc(SYS.name[r.to]), 3000);
    sel = { t: 'star', i: r.to }; renderInspector(true); updateHash();
  }
  function updateAutopilotUI() {
    const a = auto; if (!a) return;
    const r = a.r, n = r.stops.length;
    const prog = $('.prog', ui.insp); if (!prog) return;
    prog.classList.add('on');
    const k = a.k, next = Math.min(n - 1, k + (a.phase === 'hop' ? 1 : 0));
    const nm = q => known(q) || SYS.cat[q] ? SYS.name[q] : 'an unknown system';
    const label = a.phase === 'refuel' ? `Scooping fuel at <b>${esc(nm(r.stops[k].sys))}</b>` : a.phase === 'hop' ? `Jump ${k + 1} of ${n - 1} · <b>${esc(nm(r.stops[next].sys))}</b>` : `Arrived at <b>${esc(nm(r.stops[k].sys))}</b>`;
    $('.pt', prog).innerHTML = `<span>${label}</span><span>${Math.round(k / (n - 1) * 100)}%</span>`;
    $('.pbar i', prog).style.width = (k / (n - 1) * 100) + '%';
    ui.insp.querySelectorAll('.steps li').forEach((li, i) => { li.classList.toggle('done', i < k); li.classList.toggle('now', i === next); });
    const go = $('.go', ui.insp); if (go && !go.querySelector('[data-act="autostop"]')) go.innerHTML = `<button class="btn wide" data-act="autostop">${I.stop}Stop autopilot</button>`;
  }
  // knowledge changed (a survey, a visit): redraw what depends on it
  function knowledgeChanged() {
    field.setKnowledge(K.level, K.since);
    recount(); renderChips(); renderShip(); updateImportance(); tiles.dirty();
    if (sel && (sel.t === 'star' || sel.t === 'group')) renderInspector();
  }

  // ---------- Drones (mock): your drones fly out, survey a sphere of space and report what they find ----------
  const DRONE_R = 20, DRONE_SPEED = 20;           // ly surveyed around the target; ly per game hour
  const drones = K.drones.map(d => Object.assign({}, d, {
    pos: new V3(...(d.at || d.from || starPos(HOME).toArray())),
    from: d.from ? new V3(...d.from) : null, targetP: d.target ? new V3(...d.target) : null,
    etaS: (d.etaMin || 0) * 60, leg: 0, legDur: 0, phaseT: 0,
  }));
  {   // Tern is on its way home: place it along its leg
    const t = drones.find(d => d.state === 'returning');
    if (t) { t.pos.copy(t.from).lerp(t.targetP, t.progress); t.legTotalS = t.etaS / Math.max(0.05, 1 - t.progress); }
    const m = drones.find(d => d.state === 'surveying');
    if (m) m.surveyTotalS = m.etaS / Math.max(0.05, 1 - m.progress);
  }
  const fmtEta = sec => { const m = Math.max(1, Math.round(sec / 60)); return m < 60 ? m + ' min' : Math.floor(m / 60) + ' h ' + (m % 60) + ' min'; };
  let droneOpen = false, targeting = null, reveals = [];
  function droneWhere(d) {
    if (d.state === 'idle') return 'At home · ready';
    if (d.state === 'surveying') return `Surveying · ${Math.round(d.progress * 100)}% · done in ${fmtEta(d.etaS)}`;
    if (d.state === 'returning') return `Returning · ${d.carrying ? HD.plural(d.carrying, 'find') + ' reported · ' : ''}home in ${fmtEta(d.etaS)}`;
    if (d.state === 'outbound') return `Outbound · ${HD.ly(d.legLy)} · ${fmtEta(d.legLy / DRONE_SPEED * 3600)} (shown in ${d.legDur.toFixed(0)} s)`;
    if (d.state === 'scanning') return 'Scanning · ' + DRONE_R + ' ly around the target';
    if (d.state === 'homing') return 'Flying home · time compressed';
    return d.state;
  }
  function renderDroneBtn() {
    const busy = drones.filter(d => d.state !== 'idle').length;
    ui.dronebtn.innerHTML = `<span class="db-ic">${I.drone}</span><span class="db-t">Drones</span><span class="db-dots">${drones.map(d => `<i class="s-${d.state}" title="${esc(d.name)} · ${esc(d.state)}"></i>`).join('')}</span>`;
    ui.dronebtn.classList.toggle('on', droneOpen);
    ui.dronebtn.title = `${drones.length} drones · ${busy} busy (E)`;
  }
  function renderDronePanel() {
    if (!droneOpen) { ui.dronepanel.classList.remove('show'); return; }
    ui.dronepanel.classList.add('show');
    const selTxt = sel && sel.t === 'star' ? SYS.name[sel.i] : sel && sel.t === 'void' ? 'the unknown point you picked' : null;
    let h = `<div class="dp-h"><b>Drones</b><span>${drones.filter(d => d.state === 'idle').length} ready · mock</span></div>`;
    for (const d of drones) {
      const busy = d.state !== 'idle', prog = d.state === 'surveying' ? d.progress : d.state === 'returning' ? d.progress : d.state === 'outbound' ? d.leg : d.state === 'homing' ? d.leg : 0;
      h += `<div class="dp-row s-${d.state}"><div class="dp-ic">${I.drone}</div><div class="dp-t"><b>${esc(d.name)}</b><span>${esc(droneWhere(d))}</span>${busy ? `<div class="dp-bar"><i style="width:${(prog * 100).toFixed(1)}%"></i></div>` : ''}</div>
        <div class="dp-a">${busy ? `<button class="dbtn" data-dr="show" data-d="${d.id}" title="Show on the map">${I.target}</button>${d.state === 'surveying' || d.state === 'returning' ? `<button class="dbtn" data-dr="skip" data-d="${d.id}" title="Skip ahead (prototype)">${I.ff}</button>` : ''}`
          : `<button class="dbtn wide" data-dr="send" data-d="${d.id}">${I.drone}${selTxt ? 'Send to selection' : 'Send…'}</button>`}</div></div>`;
    }
    h += `<div class="dp-sub">Reports</div>` + K.reports.slice(0, 4).map(r => `<button class="dp-rep" data-dr="report" data-g="${r.group.index}"><span>${esc(r.drone)}</span><b>${esc(r.group.name)}</b><em>${esc(r.group.summary())} · ${esc(r.when)}</em></button>`).join('');
    h += `<p class="dp-tip">Pick any star or a point in unknown space, then <b>Send drone here</b>. A survey maps every system within ${DRONE_R} ly.</p>`;
    ui.dronepanel.innerHTML = h;
  }
  function toggleDrones(on) { droneOpen = on ?? !droneOpen; renderDroneBtn(); renderDronePanel(); panelsDirty = true; }
  ui.dronepanel.addEventListener('click', e => {
    const b = e.target.closest('[data-dr]'); if (!b) return;
    const d = drones.find(q => q.id === b.dataset.d), a = b.dataset.dr;
    if (a === 'report') { selectGroup(+b.dataset.g, true); return; }
    if (!d) return;
    if (a === 'show') { const p = d.state === 'surveying' || d.state === 'scanning' ? d.targetP : d.pos; if (mode === 'system') exitSystem(true); flyTo({ a: p.clone(), ld: Math.max(cam.goal.ld, log10(120)) }, 1.2); }
    else if (a === 'skip') skipDrone(d);
    else if (a === 'send') {
      if (sel && sel.t === 'star') sendDrone({ sys: sel.i }, d);
      else if (sel && sel.t === 'void') sendDrone({ p: sel.p.clone() }, d);
      else { targeting = d; ui.stage.classList.add('targeting'); toast(I.drone, `Click a star or anywhere in space for ${esc(d.name)} to survey · Esc to cancel`, 4000); }
    }
  });
  function sendDrone(t, d) {
    d = d || drones.find(q => q.state === 'idle');
    if (!d) { const b = drones.slice().sort((a, b2) => a.etaS - b2.etaS)[0]; toast(I.drone, `All drones are busy · ${esc(b.name)} is back in ${fmtEta(b.etaS)} · skip ahead in the Drones panel`, 3600); toggleDrones(true); return; }
    const p = t.p ? t.p.clone() : starPos(t.sys);
    d.state = 'outbound'; d.from = d.pos.clone(); d.targetP = p; d.leg = 0;
    d.legLy = d.from.distanceTo(p); d.legDur = clamp(1.6 + d.legLy / 22, 2.2, 4.2); d.label = t.sys != null ? SYS.name[t.sys] : 'an unknown point';
    targeting = null; ui.stage.classList.remove('targeting');
    toast(I.drone, `${esc(d.name)} is on its way to ${esc(d.label)} · ${HD.ly(d.legLy)} · about ${fmtEta(d.legLy / DRONE_SPEED * 3600)}, shown in ${d.legDur.toFixed(0)} s`, 3800);
    renderDroneBtn(); renderDronePanel();
  }
  function skipDrone(d) {
    if (d.state === 'surveying') { d.etaS = 0.01; }
    else if (d.state === 'returning') { d.state = 'homing'; d.from = d.pos.clone(); d.targetP = starPos(HOME); d.leg = 0; d.legDur = 1.6; }
    renderDronePanel();
  }
  function completeSurvey(d, now) {
    const c = d.targetP;
    const g = K.addSurvey({ centre: [c.x, c.y, c.z], radius: DRONE_R, drone: d.name, now });
    reveals.push({ c: c.clone(), t0: now, r: DRONE_R });
    knowledgeChanged();
    showReport(g, d);
    d.carrying = g.finds.length;
    return g;
  }
  function tickDrones(dt, now) {
    let changed = false;
    for (const d of drones) {
      if (d.state === 'surveying') {
        d.etaS -= dt; d.progress = clamp(1 - d.etaS / d.surveyTotalS, 0, 1);
        if (d.etaS <= 0) { completeSurvey(d, now); d.state = 'homing'; d.from = d.targetP.clone(); d.pos.copy(d.from); d.targetP = starPos(HOME); d.leg = 0; d.legDur = 3; changed = true; }
      } else if (d.state === 'returning') {
        d.etaS -= dt; d.progress = clamp(1 - d.etaS / d.legTotalS, 0, 1); d.pos.copy(d.from).lerp(d.targetP, d.progress);
        if (d.etaS <= 0) { d.state = 'idle'; d.pos.copy(starPos(HOME)); changed = true; }
      } else if (d.state === 'outbound' || d.state === 'homing') {
        d.leg = Math.min(1, d.leg + dt / d.legDur); d.pos.copy(d.from).lerp(d.targetP, easeInOut(d.leg));
        if (d.leg >= 1) {
          if (d.state === 'outbound') { d.state = 'scanning'; d.phaseT = 0; }
          else { d.state = 'idle'; d.pos.copy(starPos(HOME)); toast(I.drone, `${esc(d.name)} is home and ready`, 2200); }
          changed = true;
        }
      } else if (d.state === 'scanning') {
        d.phaseT += dt;
        if (d.phaseT >= 1.5) { completeSurvey(d, now); d.state = 'homing'; d.from = d.pos.clone(); d.targetP = starPos(HOME); d.leg = 0; d.legDur = clamp(1.6 + d.from.distanceTo(d.targetP) / 22, 2.2, 4.2); changed = true; }
      }
    }
    if (changed) { renderDroneBtn(); }
    if (droneOpen && (changed || (now | 0) % 500 < 20)) renderDronePanel();
    reveals = reveals.filter(r => now - r.t0 < 2600);
  }
  // Reports: a summary card, then one card per find (they stay until dismissed; the Drones panel keeps them all)
  function showReport(g, d) {
    // one card per kind first (a player's base, ruins, a resource, a fuel star, then wrecks), at most four
    const CARD_ORDER = { player: 0, monument: 1, resource: 2, fuel: 3, wreck: 4 }, seenK = new Set(), first = [], rest = [];
    for (const f of g.finds.filter(q => !q.hidden).sort((a, b) => CARD_ORDER[a.kind] - CARD_ORDER[b.kind])) (seenK.has(f.kind) ? rest : (seenK.add(f.kind), first)).push(f);
    const fs = first.concat(rest);
    const card = document.createElement('div'); card.className = 'rep sum';
    card.innerHTML = `<div class="rep-h">${I.drone}<b>${esc(d.name)} · survey complete</b><button class="rep-x" data-rp="x" title="Dismiss">${I.close}</button></div>
      <div class="rep-t">${esc(g.name)}</div><div class="rep-s">${esc(g.summary())} · ${DRONE_R} ly around ${esc(d.label || 'the target')}</div>
      <div class="rep-a"><button class="hbtn" data-rp="group" data-g="${g.index}">${I.target}Show region</button></div>`;
    ui.reports.prepend(card);
    let lastCard = card;
    fs.slice(0, 4).forEach((f, k) => setTimeout(() => {
      const c = document.createElement('div'); c.className = 'rep k-' + f.kind;
      c.innerHTML = `<div class="rep-h"><span class="rep-ic">${I[FIND_IC[f.kind]]}</span><span class="rep-k">${esc(FIND_WORD[f.kind])}</span><button class="rep-x" data-rp="x" title="Dismiss">${I.close}</button></div>
        <div class="rep-t">${esc(f.title)}</div><div class="rep-s">${esc(f.sub)} · ${esc(SYS.name[f.sys])} · ${HD.ly(distShip(f.sys))}</div>
        <div class="rep-a"><button class="hbtn" data-rp="show" data-sys="${f.sys}">${I.target}Show</button><button class="hbtn" data-rp="route" data-sys="${f.sys}">${I.route}Route</button></div>`;
      lastCard.after(c); lastCard = c;
      trimReports();
    }, 650 * (k + 1)));
    trimReports();
  }
  function trimReports() { const cs = [...ui.reports.children]; cs.slice(5).forEach(c => c.remove()); panelsDirty = true; }
  ui.reports.addEventListener('click', e => {
    const b = e.target.closest('[data-rp]'); if (!b) return;
    const a = b.dataset.rp, card = b.closest('.rep');
    if (a === 'x') { card.classList.add('out'); setTimeout(() => card.remove(), 250); return; }
    if (a === 'group') selectGroup(+b.dataset.g, true);
    if (a === 'show') selectStar(+b.dataset.sys);
    if (a === 'route') planTo(+b.dataset.sys);
  });
  // drone markers, trails, survey rings and the reveal wave (2D, on the fx canvas)
  const droneMk = drones.map(d => { const el = document.createElement('div'); el.className = 'mk m-drone'; el.innerHTML = `<span class="dm-ic">${I.drone}</span><span class="dm-n">${esc(d.name)}</span>`; ui.markers.appendChild(el); return { el, op: -1, tr: '' }; });
  function drawFx(now, alpha) {
    const w = fxCanvas.width / DPR, h = fxCanvas.height / DPR;
    if (Math.round(W * DPR) !== fxCanvas.width || Math.round(H * DPR) !== fxCanvas.height) { fxCanvas.width = Math.round(W * DPR); fxCanvas.height = Math.round(H * DPR); }
    fx.setTransform(DPR, 0, 0, DPR, 0, 0); fx.clearRect(0, 0, Math.max(w, W), Math.max(h, H));
    drones.forEach((d, k) => setMk(droneMk[k], 0, 0, 0));
    if (alpha < 0.02) return;
    const pr = (p, o) => proj(camera, p.x, p.y, p.z, o);
    const a = {}, b = {};
    const pxPerLy = (p) => { pr(p, a); return a.ok ? sysFocalMap() / a.w : 0; };
    fx.lineCap = 'round';
    drones.forEach((d, k) => {
      const showMk = d.state !== 'idle';
      if (d.state === 'outbound' || d.state === 'returning' || d.state === 'homing') {
        pr(d.from || starPos(HOME), a); pr(d.targetP, b);
        if (a.ok && b.ok) { fx.setLineDash([3, 5]); fx.lineWidth = 1.2; fx.strokeStyle = `rgba(160, 236, 214, ${(0.55 * alpha).toFixed(3)})`; fx.beginPath(); fx.moveTo(a.x, a.y); fx.lineTo(b.x, b.y); fx.stroke(); fx.setLineDash([]); }
      }
      if (d.state === 'surveying' || d.state === 'scanning') {
        pr(d.targetP, a);
        if (a.ok) {
          const rp = DRONE_R * sysFocalMap() / a.w, ph = (now / 1400) % 1;
          fx.lineWidth = 1.2; fx.setLineDash([4, 5]); fx.strokeStyle = `rgba(128, 172, 255, ${(0.6 * alpha).toFixed(3)})`; fx.beginPath(); fx.arc(a.x, a.y, rp, 0, Math.PI * 2); fx.stroke(); fx.setLineDash([]);
          fx.strokeStyle = `rgba(128, 172, 255, ${(0.5 * (1 - ph) * alpha).toFixed(3)})`; fx.beginPath(); fx.arc(a.x, a.y, rp * ph, 0, Math.PI * 2); fx.stroke();
          fx.fillStyle = `rgba(128, 172, 255, ${(0.05 * alpha).toFixed(3)})`; fx.beginPath(); fx.arc(a.x, a.y, rp, 0, Math.PI * 2); fx.fill();
        }
      }
      if (showMk) { pr(d.pos, a); if (a.ok) { setMk(droneMk[k], a.x, a.y, alpha); droneMk[k].el.classList.toggle('busy', d.state !== 'idle'); } }
    });
    for (const r of reveals) {
      pr(r.c, a); if (!a.ok) continue;
      const u = clamp((now - r.t0) / 1500, 0, 1), rp = r.r * sysFocalMap() / a.w;
      fx.lineWidth = 2; fx.strokeStyle = `rgba(170, 200, 255, ${((1 - u) * 0.85 * alpha).toFixed(3)})`; fx.beginPath(); fx.arc(a.x, a.y, Math.max(1, rp * easeOut(u)), 0, Math.PI * 2); fx.stroke();
    }
    // hovering unknown space: the footprint a drone survey would cover
    if (hov && hov.t === 'void' && mouse.in && !drag) {
      pr(hov.p, a);
      if (a.ok) {
        const rp = DRONE_R * sysFocalMap() / a.w;
        fx.lineWidth = 1.2; fx.setLineDash([3, 4]); fx.strokeStyle = `rgba(214, 224, 240, ${(0.42 * alpha).toFixed(3)})`; fx.beginPath(); fx.arc(a.x, a.y, rp, 0, Math.PI * 2); fx.stroke(); fx.setLineDash([]);
      }
    }
    void pxPerLy;
  }
  const sysFocalMap = () => H / (2 * Math.tan(camera.fov * Math.PI / 360));

  // ---------- Labels: projected HTML, placed by priority, never overlapping each other or the panels ----------
  const measureCtx = document.createElement('canvas').getContext('2d');
  const widthCache = new Map();
  const ST = {
    star: { f: '500 12px Inter', h: 16 }, major: { f: '600 12.5px Inter', h: 16 }, dim: { f: '450 11px Inter', h: 15 }, sel: { f: '600 13px Inter', h: 24, pill: 18 },
    special: { f: '600 12px Inter', h: 16 }, sector: { f: '600 11px Inter', ls: 0.18, up: 1, h: 15 }, arm: { f: '500 12px Inter', ls: 0.34, up: 1, h: 16 },
    struct: { f: '500 13px Inter', ls: 0.26, up: 1, h: 17 }, structs: { f: '500 11.5px Inter', ls: 0.26, up: 1, h: 16 }, ring: { f: '500 10.5px Inter', ls: 0.04, h: 14 },
    dir: { f: '500 10px Inter', ls: 0.12, up: 1, h: 14 }, danger: { f: '600 11px Inter', ls: 0.08, up: 1, h: 15 }, pin: { f: '650 12px Inter', h: 40 },
    body: { f: '600 12.5px Inter', h: 30 }, body2: { f: '450 11px Inter', h: 30 }, moon: { f: '500 11px Inter', h: 15 },
    terr: { f: '650 11.5px Inter', ls: 0.16, up: 1, h: 30 }, terr2: { f: '500 10.5px Inter', h: 30 },
  };
  function textW(text, st) {
    const key = st.f + '|' + text; let w = widthCache.get(key);
    if (w === undefined) {
      measureCtx.font = st.f; const t = st.up ? text.toUpperCase() : text;
      w = measureCtx.measureText(t).width + (st.ls ? st.ls * parseFloat(st.f.split(' ')[1]) * t.length : 0);
      widthCache.set(key, w);
    }
    return w;
  }
  const STAR_LABEL_CAP = 22; // the Neighbourhood never shows more star names than this
  const LB = new Map();
  let placed = [], cands = [], frameNo = 0, panelRects = [], lastPanelT = 0;
  function readPanels(now) {
    if (!panelsDirty && now - lastPanelT < 400) return;
    panelsDirty = false; lastPanelT = now; panelRects = [];
    for (const el of [ui.crumbs, $('#levelcap'), ui.rail, $('#topright'), ui.chips, $('#tools'), ui.sysmode, ui.dronepanel, ui.reports, $('#sm-left'), $('#sm-time'), $('#sm-keys'), document.body.classList.contains('insp-open') ? $('#inspector') : $('#footer')]) {
      if (!el || el.classList.contains('hide') || (el === ui.sysmode && !el.classList.contains('show')) || (el === ui.dronepanel && !el.classList.contains('show')) || getComputedStyle(el).display === 'none' || getComputedStyle(el).visibility === 'hidden') continue;
      const r = el.getBoundingClientRect(); if (r.width > 0) panelRects.push([r.left - 6, r.top - 6, r.right + 6, r.bottom + 6]);
    }
  }
  const hits = (r, lst) => { for (const q of lst) if (r[0] < q[2] && r[2] > q[0] && r[1] < q[3] && r[3] > q[1]) return true; return false; };
  function tryPlace(c) {
    const pad = c.pad || 2; let tries;
    if (c.mode === 'center') tries = c.nudge ? [[c.x - c.w / 2, c.y - c.h / 2], [c.x - c.w / 2, c.y + c.nudge], [c.x - c.w / 2, c.y - c.h - c.nudge]] : [[c.x - c.w / 2, c.y - c.h / 2]];
    else if (c.mode === 'above') tries = [[c.x - c.w / 2, c.y - c.h - (c.off || 0)]];
    else if (c.mode === 'rot') {
      for (const q of (c.alts || [c])) {
        const cs = Math.abs(Math.cos(q.ang)), sn = Math.abs(Math.sin(q.ang)), hx = cs * c.w / 2 + sn * c.h / 2, hy = sn * c.w / 2 + cs * c.h / 2;
        const r = [q.x - hx, q.y - hy, q.x + hx, q.y + hy];
        if (r[0] < 0 || r[1] < 0 || r[2] > W || r[3] > H || hits(r, panelRects) || hits(r, placed)) continue;
        placed.push(r); c.x = q.x; c.y = q.y; c.ang = q.ang; return [q.x - c.w / 2, q.y - c.h / 2];
      }
      return null;
    } else {
      const o = c.off || 7;
      tries = [[c.x + o, c.y - c.h / 2], [c.x - o - c.w, c.y - c.h / 2], [c.x - c.w / 2, c.y - o - c.h + 2], [c.x - c.w / 2, c.y + o - 2]];
    }
    for (const t of tries) {
      const r = [t[0] - pad, t[1] - pad, t[0] + c.w + pad, t[1] + c.h + pad];
      if (r[0] < 2 || r[1] < 2 || r[2] > W - 2 || r[3] > H - 2) continue;
      if (hits(r, panelRects) || hits(r, placed)) continue;
      placed.push(r); return t;
    }
    return null;
  }
  function addCand(c) { if (c.alpha > 0.03 && c.x > -50 && c.y > -50 && c.x < W + 50 && c.y < H + 50) cands.push(c); }
  const projV = new V3();
  function proj(cm, x, y, z, out) {
    const e = cm._vp;
    const cw = e[3] * x + e[7] * y + e[11] * z + e[15];
    if (cw <= 1e-6) { out.ok = false; return out; }
    out.x = ((e[0] * x + e[4] * y + e[8] * z + e[12]) / cw * 0.5 + 0.5) * W;
    out.y = (0.5 - (e[1] * x + e[5] * y + e[9] * z + e[13]) / cw * 0.5) * H;
    out.w = cw; out.ok = true; return out;
  }
  const vpMat = new THREE.Matrix4();
  function prepVP(cm) { vpMat.multiplyMatrices(cm.projectionMatrix, cm.matrixWorldInverse); cm._vp = vpMat.elements.slice(); }
  function flushLabels(now) {
    cands.sort((a, b) => b.pri - a.pri);
    let n = 0, stars = 0;
    for (const c of cands) {
      if (n >= 70) break;
      if (c.star != null && stars >= STAR_LABEL_CAP) continue;
      const p = tryPlace(c); if (!p) continue;
      n++; if (c.star != null) stars++;
      let rec = LB.get(c.key);
      if (!rec) {
        const el = document.createElement('div'); el.className = 'lbl'; ui.labels.appendChild(el);
        rec = { el, html: null, cls: null }; LB.set(c.key, rec);
      }
      if (rec.html !== c.html) { rec.el.innerHTML = c.html; rec.html = c.html; }
      const cls = 'lbl ' + c.cls + (c.obj ? ' hit' : '');
      if (rec.cls !== cls) { rec.el.className = cls; rec.cls = cls; }
      const fc = c.fc || '';
      if (rec.fc !== fc) { rec.el.style.setProperty('--fc', fc || 'inherit'); rec.fc = fc; }
      rec.obj = c.obj || null; rec.seen = frameNo; rec.dx = p[0] - c.x; rec.dy = p[1] - c.y; rec.wp = c.wp; rec.cm = c.cm;
      const tr = `translate3d(${Math.round(p[0])}px,${Math.round(p[1])}px,0)` + (c.mode === 'rot' ? ` rotate(${c.ang}rad)` : '');
      if (rec.tr !== tr) { rec.el.style.transform = tr; rec.tr = tr; }
      const op = c.alpha.toFixed(2);
      if (rec.op !== op) { rec.el.style.opacity = op; rec.op = op; }
      if (c.mode === 'rot') rec.el.style.transformOrigin = '50% 50%';
    }
    for (const [key, rec] of LB) {
      if (rec.seen === frameNo) { rec.gone = 0; continue; }
      if (!rec.gone) { rec.gone = now; rec.el.style.opacity = '0'; rec.op = '0'; rec.el.classList.remove('hit'); rec.cls = null; }
      else if (now - rec.gone > 450) { rec.el.remove(); LB.delete(key); continue; }
      if (rec.wp && rec.cm && rec.cm._vp) {
        proj(rec.cm, rec.wp[0], rec.wp[1], rec.wp[2], projV);
        if (projV.ok) { const tr = `translate3d(${Math.round(projV.x + rec.dx)}px,${Math.round(projV.y + rec.dy)}px,0)`; if (rec.tr !== tr) { rec.el.style.transform = tr; rec.tr = tr; } }
      }
    }
  }

  // Markers (concept A's look)
  const mk = cls => { const el = document.createElement('div'); el.className = 'mk ' + cls; el.style.opacity = '0'; ui.markers.appendChild(el); return { el, op: -1, tr: '' }; };
  const MK = { known: mk('m-known'), ship: mk('m-ship'), sel: mk('m-sel'), hov: mk('m-hover'), tgt: mk('m-target'), body: mk('m-body'), bhov: mk('m-bhov') };
  const leaderSvg = $('#leader'), leader = $('#leader line');
  MK.ship.el.innerHTML = '<div class="pulse"></div>';
  function setMk(m, x, y, alpha) {
    const op = alpha > 0.01 ? Math.min(1, alpha).toFixed(2) : '0';
    if (m.op !== op) { m.el.style.opacity = op; m.op = op; }
    if (alpha > 0.01) { const tr = `translate3d(${x.toFixed(1)}px,${y.toFixed(1)}px,0)`; if (m.tr !== tr) { m.el.style.transform = tr; m.tr = tr; } }
  }

  const labelledLast = new Set();
  function starLabels(alpha) {
    if (alpha < 0.03 || !field.isProjected()) return;
    const extra = new Map();
    const add = (i, p) => { if (i >= 0) extra.set(i, Math.max(extra.get(i) || 0, p)); };
    const anyF = activeFilters.size > 0 || !!listSet;
    add(ship.sys, 800); add(M.home.sys, 460); add(targetSys, 600);
    K.bases.forEach(b => add(b.sys, 440));
    if (!anyF) { for (const f of K.finds) if (!f.hidden) add(f.sys, f.kind === 'fuel' ? 120 : 330); for (const f of favs) if (known(f)) add(f, 8); }
    const rs = activeRoute();
    if (rs) rs.stops.forEach((s, k) => { if (known(s.sys) || (k === rs.stops.length - 1 && !rs.far)) add(s.sys, k === rs.stops.length - 1 ? 700 : s.refuel || s.danger >= 0 ? 520 : 420); });
    if (listSet) list.items.slice(0, 40).forEach((i, k) => add(i, 380 - k));
    else if (anyF) for (const id of activeFilters) { const f = FILTERS.find(q => q.id === id), fi = FILTERS.indexOf(f); if (f.rare) for (let i = 0; i < N; i++) if (filterOf[i] === fi) add(i, 60); }
    if (sel && sel.t === 'star') add(sel.i, 1000); if (hov && hov.t === 'star') add(hov.i, 900);
    for (const i of labelledLast) add(i, extra.has(i) ? extra.get(i) + 1 : 1);
    const c = field.labelCandidates(extra);
    labelledLast.clear();
    for (let k = 0; k < c.length; k += 2) {
      const i = c[k], pri = c[k + 1], sp = extra.get(i) || 0, fo = filterOf[i];
      let cls = 'lbl-star', st = ST.star;
      const isSel = sel && sel.t === 'star' && sel.i === i;
      const isDest = rs && rs.to === i;
      const isHov = hov && hov.t === 'star' && hov.i === i;
      if (isSel || isDest) { cls += isDest ? ' dest' : ' sel'; st = ST.sel; }
      else if (isHov) { cls += ' hov'; st = ST.sel; }
      else if (i === ship.sys || i === M.home.sys) { cls += ' you'; st = ST.sel; }
      else if (rs && sp >= 420) { cls += ' rt'; st = ST.special; }
      else if (listSet && listSet.has(i)) { cls += ' result'; st = ST.special; }
      else if (anyF && fo >= 0 && FILTERS[fo].rare) { cls += ' filter'; st = ST.special; }
      else if (sp >= 300 || favs.has(i)) { cls += ' special'; st = ST.special; }
      else if (field.app[i] < 5 || SYS.cat[i]) { cls += ' major'; st = ST.major; }
      else { cls += ' dim'; st = ST.dim; }
      let icon = '';
      const f0 = findsAt(i)[0];
      if (i === M.home.sys) icon = 'home'; else if (isOutpost(i)) icon = 'outpost'; else if (f0) icon = f0.kind === 'player' ? 'player' : FIND_IC[f0.kind]; else if (!known(i)) icon = 'unknown'; else if (favs.has(i)) icon = 'favOn';
      if (rs && dangerOf[i] >= 0 && rs.stops.some(s => s.sys === i)) icon = 'warn';
      else if (rs && rs.stops.some(s => s.sys === i && s.refuel) && known(i)) icon = 'fuel';
      if (!known(i)) cls += ' unk';
      // a find is named by what was found (the star's name is on the card); outposts by their own name
      const text = f0 && f0.kind !== 'fuel' && !isSel && !isHov ? f0.title : isOutpost(i) && !isSel && !isHov ? baseAt(i).name : shortName(i);
      const w = textW(text, st) + (icon ? 18 : 0) + (st.pill || 0);
      const html = (icon ? `<span class="ic ic-${icon}">${I[icon === 'outpost' || icon === 'player' ? 'flag' : icon]}</span>` : '') + esc(text);
      const hasMk = i === ship.sys || i === targetSys || isSel || (hov && hov.t === 'star' && hov.i === i);
      cands.push({ key: 's' + i, pri, x: field.sx[i], y: field.sy[i], w, h: st.h, cls, html, alpha, off: hasMk ? (i === targetSys ? 20 : 16) : 8, pad: 4, obj: { t: 'star', i }, wp: [PX[i], PY[i], PZ[i]], cm: camera, fc: anyF && fo >= 0 ? FILTERS[fo].color : '', star: i });
    }
  }

  // ---------- Input ----------
  const ray = new THREE.Raycaster(), ndc = new THREE.Vector2(), plane = new THREE.Plane(new V3(0, 1, 0), -SECTOR_Y), hitP = new V3();
  const mouse = { x: -1, y: -1, in: false };
  let drag = null, pickPending = false, lastClick = 0, cycle = null;
  function pickAt(mx, my) {
    const lvl = levelOf();
    if (lvl === 3) {
      if (!SY || sysView !== 'realistic') return null;
      let best = null, bd = 1e9;
      for (const n of SY.nodes) {
        if (n.kind === 3) continue;
        proj(sysCam, n.pos.x, n.pos.y, n.pos.z, projV); if (!projV.ok) continue;
        const rad = Math.max(10, n.size * sysFocal() / projV.w);
        const d = Math.hypot(projV.x - mx, projV.y - my);
        if (d < rad + 6 && d < bd) { bd = d; best = { t: 'body', k: n.k }; }
      }
      return best;
    }
    if (lvl === 2) {
      // exactly one known star under the cursor: the one whose cell holds it; outside every cell, unknown space
      const p = tiles.pick(mx, my);
      if (!p) { const q = voidPoint(mx, my); return q ? { t: 'void', p: q } : null; }
      if (cycle && cycle.tile === p.tile && cycle.list.length > 1) { const c = cycle.list[cycle.idx]; return { t: 'star', i: c.i, tile: p.tile, list: cycle.list, cycled: true }; }
      return { t: 'star', i: p.i, tile: p.tile };
    }
    if (lvl === 1) {
      ndc.set(mx / W * 2 - 1, -(my / H) * 2 + 1); ray.setFromCamera(ndc, camera);
      if (ray.ray.intersectPlane(plane, hitP)) {
        const k = sectorOfPos(hitP.x, 0, hitP.z);
        if (k >= 0 && Math.hypot(hitP.x, hitP.z) < 3600) return { t: 'sector', k };
      }
    }
    return null;
  }
  // the point in space under the cursor: on the plane through the orbit centre, facing the camera
  const viewPlane = new THREE.Plane(), camDir = new V3();
  function voidPoint(mx, my) {
    ndc.set(mx / W * 2 - 1, -(my / H) * 2 + 1); ray.setFromCamera(ndc, camera);
    camera.getWorldDirection(camDir); viewPlane.setFromNormalAndCoplanarPoint(camDir, target);
    const q = new V3(); if (!ray.ray.intersectPlane(viewPlane, q)) return null;
    return q.length() < 160 ? q : null;
  }
  // the stars of a tile, likeliest first (Tab steps through them)
  function tileList(i) {
    const m = tiles.membersOf(i).filter(j => j !== i);
    m.sort((a, b) => (importance[b] - importance[a]) || (MAG[a] - MAG[b]));
    return [{ i, d: 0 }].concat(m.slice(0, 60).map(j => ({ i: j, d: 0 })));
  }
  const labelObjAt = el => { const l = el && el.closest && el.closest('.lbl.hit'); if (!l) return null; for (const rec of LB.values()) if (rec.el === l) return rec.obj; return null; };
  let hovInfo = null;
  function setHover(o) {
    if (o && o.t === 'star' && o.tile && !o.list) { if (hovInfo && hovInfo.tile === o.tile && hovInfo.list) o.list = hovInfo.list; else o.list = tileList(o.i); }
    hovInfo = o && o.t === 'star' ? o : null;
    const same = (a, b) => a === b || (a && b && a.t === b.t && a.i === b.i && a.k === b.k && a.id === b.id && a.g === b.g && a.t !== 'void');
    if (o && o.t === 'body') { sysHov = o.k; hov = null; }
    else { sysHov = -1; if (!same(hov, o)) hov = o; }
    if (hov && hov.t === 'sector') { const s = SECTORS[hov.k]; sectorMat.uniforms.uHover.value.set(s[1] - 64, s[3] - 38); } else sectorMat.uniforms.uHover.value.set(999, 999);
    ui.stage.classList.toggle('pointing', !!o);
    renderHoverCard(o);
  }
  function renderHoverCard(o) {
    const hc = ui.hc;
    if (!o || drag) { hc.classList.remove('show'); return; }
    let h = '';
    if (o.t === 'star') {
      const i = o.i, d = distShip(i), j = HD.jumpsTo(i), tags = [], lv = K.level[i];
      tags.push(`<span class="k k${lv}">${esc(knowWord(i))}</span>`);
      if (i === ship.sys) tags.push('<span class="a">You are here</span>'); else if (d <= ship.range) tags.push('<span class="a">In range</span>'); else if (j > 0) tags.push(`<span>${HD.plural(j, 'jump')}</span>`);
      if (lv && refuelAt(i)) tags.push('<span class="w">Fuel</span>');
      if (lv >= 2 && SYS.sum[i][3]) tags.push(`<span>${SYS.sum[i][3]} landable</span>`);
      if (lv >= 2 && (FL[i] & F.EARTH)) tags.push('<span class="a">Earth-like</span>');
      if (FL[i] & F.DANGER) tags.push('<span class="d">Ion storm</span>');
      for (const f of findsAt(i)) tags.push(`<span class="f k-${f.kind}">${esc(f.title)}</span>`);
      const t = o.tile, many = o.list && o.list.length > 1 ? (t ? t.members.length : o.list.length) : 0;
      const tileLine = t && t.members.length > 1 ? `<div class="hc-tile">${t.group ? esc(t.group.name) + ' · ' : ''}this tile holds ${fmt(t.members.length)} systems</div>` : '';
      const hint = o.cycled ? `${o.list.findIndex(c => c.i === i) + 1} of ${many} in this tile · <kbd>Tab</kbd> next · click to select`
        : many ? `Click to select · <kbd>Tab</kbd> for ${fmt(many - 1)} more in this tile, or zoom in` : 'Click for details · double-click to fly there';
      h = `<div class="hc-name"><i style="--c:${lv ? SYS.col[i] : '#7d8898'}"></i>${esc(SYS.name[i])}</div><div class="hc-sub">${lv ? esc(SYS.cls[i]) + ' · ' + starKind(SYS.cls[i]) : 'Nothing known'} · ${fmtLy(d)}</div>${tileLine}<div class="hc-row">${tags.join('')}</div><div class="hc-hint">${hint}</div>`;
    } else if (o.t === 'void') {
      const n = K.countWithin([o.p.x, o.p.y, o.p.z], DRONE_R);
      h = `<div class="hc-name"><span class="hc-ic">${I.unknown}</span>Unknown space</div><div class="hc-sub">${fmtLy(o.p.distanceTo(ship.pos))} from you · ≈ ${fmt(n)} systems within ${DRONE_R} ly</div><div class="hc-hint">Click to send a drone here · nothing is known</div>`;
    } else if (o.t === 'group') {
      const g = K.groups[o.g];
      h = `<div class="hc-name"><span class="hc-ic">${g.kind === 'home' ? I.home : g.kind === 'survey' ? I.drone : I.route}</span>${esc(g.name)}</div><div class="hc-sub">${esc(g.summary())}${g.when ? ' · ' + esc(g.when) : ''}</div><div class="hc-hint">Click for the report</div>`;
    } else if (o.t === 'sector') {
      const s = SECTORS[o.k];
      h = `<div class="hc-name">${esc(s[0])}</div><div class="hc-sub">Sector · ${o.k === 0 ? 'you are here' : fmtLy(Math.hypot(s[4], s[5], s[6])) + ' away'}</div><div class="hc-hint">Click for details</div>`;
    } else if (o.t === 'struct') {
      const s = STRUCTS.find(x => x.id === o.id);
      h = `<div class="hc-name">${esc(s.name)}</div><div class="hc-sub">${s.kind === 'arm' ? 'Spiral arm' : s.kind === 'spur' ? 'Spur · you live here' : s.kind === 'bar' ? 'Central bar' : 'Supermassive black hole'}</div>`;
    } else if (o.t === 'pin') {
      h = `<div class="hc-name">${esc(ship.name)}</div><div class="hc-sub">${esc(SYS.name[ship.sys])} · ${esc(HOME_SECTOR)}</div><div class="hc-hint">Click to zoom in</div>`;
    } else if (o.t === 'body' && SY) {
      const n = SY.nodes[o.k];
      const meta = n.kind === 0 ? esc(n.type || 'Star') : SY.schematic || n.kind === 3 ? esc(n.type) : `${esc(n.type)} · ${n.g.toFixed(2)} g · ${tempC(n.t)}`;
      h = `<div class="hc-name">${esc(n.name)}</div><div class="hc-sub">${meta}</div>${n.land ? '<div class="hc-row"><span class="a">Landable</span></div>' : ''}`;
    }
    hc.innerHTML = h; hc.classList.add('show');
    positionHoverCard();
  }
  function positionHoverCard() {
    const hc = ui.hc; if (!hc.classList.contains('show')) return;
    const w = hc.offsetWidth, h = hc.offsetHeight;
    let x = mouse.x + 18, y = mouse.y + 18;
    const right = W - (document.body.classList.contains('insp-open') ? 368 + 40 : 20);
    if (x + w > right) x = mouse.x - w - 18;
    if (y + h > H - 80) y = mouse.y - h - 18;
    hc.style.transform = `translate3d(${Math.round(x)}px,${Math.round(y)}px,0)`;
  }
  // Chooser: when several stars are too close to call, list them instead of guessing.
  const chooserEl = $('#chooser');
  let chooser = null;
  function openChooser(lst, x, y) {
    const items = lst.slice(0, 6);
    chooser = { items, idx: 0 };
    chooserEl.innerHTML = `<div class="ch-h">${items.length} stars here<span>likeliest first</span></div>` + items.map((c, k) => {
      const i = c.i, d = distShip(i);
      const what = [i === ship.sys ? 'You are here' : HD.ly(d) + ' · ' + HD.jumpsText(i), HD.whatsThere(i)].join(' · ');
      return `<button class="ch-row${k === 0 ? ' active' : ''}" data-k="${k}"><span class="sd" style="--c:${SYS.col[i]}"></span><span class="ch-t"><b>${esc(SYS.name[i])}</b><span>${esc(what)}</span></span></button>`;
    }).join('') + `<div class="ch-foot"><span><kbd>↑</kbd><kbd>↓</kbd>choose</span><span><kbd>↵</kbd>select</span><span><kbd>esc</kbd>cancel</span></div>`;
    chooserEl.classList.add('show');
    const w = chooserEl.offsetWidth, h = chooserEl.offsetHeight;
    let px = x + 16, py = y - 20;
    const right = W - (document.body.classList.contains('insp-open') ? 368 + 40 : 20);
    if (px + w > right) px = x - w - 16;
    py = clamp(py, 70, H - h - 20);
    chooserEl.style.transform = `translate3d(${Math.round(px)}px,${Math.round(py)}px,0)`;
    ui.hc.classList.remove('show');
    setHover({ t: 'star', i: items[0].i, list: lst });
    ui.hc.classList.remove('show');
  }
  function closeChooser() { if (!chooser) return; chooser = null; chooserEl.classList.remove('show'); }
  function chooserMove(k) {
    if (!chooser) return;
    chooser.idx = (k + chooser.items.length) % chooser.items.length;
    chooserEl.querySelectorAll('.ch-row').forEach(r => r.classList.toggle('active', +r.dataset.k === chooser.idx));
    setHover({ t: 'star', i: chooser.items[chooser.idx].i }); ui.hc.classList.remove('show');
  }
  function chooserPick(k) { if (!chooser) return; const i = chooser.items[k].i; closeChooser(); clickObj({ t: 'star', i }); }
  chooserEl.addEventListener('pointerover', e => { const r = e.target.closest('.ch-row'); if (r && chooser && +r.dataset.k !== chooser.idx) chooserMove(+r.dataset.k); });
  chooserEl.addEventListener('click', e => { const r = e.target.closest('.ch-row'); if (r) chooserPick(+r.dataset.k); });
  function cycleNext(dir) {
    const base = hovInfo && hovInfo.list && hovInfo.list.length > 1 ? hovInfo.list : null;
    if (!base) return false;
    if (!cycle) cycle = { x: mouse.x, y: mouse.y, list: base, idx: 0, tile: hovInfo.tile };
    cycle.idx = (cycle.idx + dir + cycle.list.length) % cycle.list.length;
    const c = cycle.list[cycle.idx];
    setHover({ t: 'star', i: c.i, list: cycle.list, cycled: true, tile: cycle.tile });
    return true;
  }

  function clickObj(o) {
    if (targeting && o && (o.t === 'star' || o.t === 'void')) { sendDrone(o.t === 'star' ? { sys: o.i } : { p: o.p.clone() }, targeting); return; }
    if (o && o.t === 'void') { select({ t: 'void', p: o.p.clone() }, { keepCam: true }); return; }
    if (o && o.t === 'group') { selectGroup(o.g, false); return; }
    if (!o) { if ((sel || list) && mode !== 'system' && !auto) clearAll(); return; }
    if (o.t === 'pin') { goLevel(cam.goal.ld > B_GAL ? 1 : 2); return; }
    if (o.t === 'body') { sysSel = o.k; focusBody(sysSel); renderInspector(); renderSysmap(); updateHash(); return; }
    if (o.t === 'star') { if (listSet && listSet.has(o.i)) select(o, { keepCam: true, fromList: true, ctx: { icon: list.icon, text: list.title } }); else select(o); return; }
    select(o, { keepCam: o.t === 'struct' });
  }
  function dblObj(o) {
    if (!o) return;
    if (o.t === 'void') { flyTo({ a: o.p.clone(), ld: Math.min(cam.goal.ld, log10(80)) }, 1.0); return; }
    if (o.t === 'group') { frameGroup(o.g); return; }
    if (o.t === 'star') { select(o, { keepCam: true }); const near = cam.goal.ld < log10(40) && cam.goal.a.distanceTo(starPos(o.i)) < 3; if (near) enterSystem(o.i); else flyToStar(o.i, log10(26)); }
    if (o.t === 'sector') { const s = SECTORS[o.k]; if (o.k === 0) goLevel(2); else flyTo({ a: new V3(s[4], 0, s[6]), ld: log10(2600) }, 1.2); }
    if (o.t === 'body') { sysSel = o.k; focusBody(o.k); scam.goal.ld = Math.max(scam.min, scam.goal.ld - 0.35); renderInspector(); renderSysmap(); }
    if (o.t === 'pin') goLevel(2);
  }
  ui.stage.addEventListener('pointerdown', e => {
    if (panelOpen) { closeSearch(); return; }
    if (chooser) { closeChooser(); return; }
    drag = { x: e.clientX, y: e.clientY, sx: e.clientX, sy: e.clientY, pan: e.button !== 0 || e.shiftKey, moved: false, lbl: labelObjAt(e.target) };
    ui.stage.setPointerCapture(e.pointerId);
  });
  ui.stage.addEventListener('pointermove', e => {
    mouse.x = e.clientX; mouse.y = e.clientY; mouse.in = true;
    if (cycle) { const p = tiles.pick(mouse.x, mouse.y); if (!p || p.tile !== cycle.tile) cycle = null; }
    if (drag) {
      const dx = e.clientX - drag.x, dy = e.clientY - drag.y; drag.x = e.clientX; drag.y = e.clientY;
      if (!drag.moved && Math.hypot(e.clientX - drag.sx, e.clientY - drag.sy) > 4) { drag.moved = true; ui.stage.classList.add('drag'); ui.hc.classList.remove('show'); hideHint(); tween = null; stween = null; }
      if (!drag.moved) return;
      if (mode === 'system' && sysView === 'simple') return;
      const c = mode === 'system' && sysBlend > 0.5 ? scam : cam;
      if (drag.pan) {
        const cm = c === scam ? sysCam : camera;
        if (c === cam && smooth(4.05, 4.78, cam.cur.ld) > 0.5) return;
        const d = Math.pow(10, c.goal.ld), k = 2 * d * Math.tan(cm.fov * Math.PI / 360) / H;
        const yaw = c.cur.yaw, r = new V3(-Math.cos(yaw), 0, Math.sin(yaw)), f = new V3(Math.sin(yaw), 0, Math.cos(yaw));
        const sp = Math.sin(c.cur.pitch);   // signed: from under the plane, screen-up points the other way
        c.goal.a.addScaledVector(r, -dx * k).addScaledVector(f, dy * k / ((sp < 0 ? -1 : 1) * Math.max(0.35, Math.abs(sp))));
        if (c === cam && cam.goal.a.length() > 9000) cam.goal.a.setLength(9000);
      } else {
        c.goal.yaw -= dx * 0.0055;
        c.goal.pitch = clamp(c.goal.pitch + dy * 0.0045, PITCH_MIN, PITCH_MAX);
        if (topView && c.goal.pitch < 1.45) { topView = false; ui.top.classList.remove('on'); }
      }
      return;
    }
    if (!pickPending) { pickPending = true; requestAnimationFrame(() => { pickPending = false; if (!drag) setHover(labelObjAt(document.elementFromPoint(mouse.x, mouse.y)) || pickAt(mouse.x, mouse.y)); positionHoverCard(); }); }
  });
  ui.stage.addEventListener('pointerup', e => {
    const d = drag; drag = null; ui.stage.classList.remove('drag');
    if (!d || d.moved || e.button !== 0) return;
    const o = d.lbl || pickAt(e.clientX, e.clientY);
    const now = performance.now();
    if (now - lastClick < 320) { dblObj(o); lastClick = 0; return; }
    lastClick = now;
    if (o && o.t === 'star' && o.crowded && !o.cycled && !d.lbl) { openChooser(o.list, e.clientX, e.clientY); return; }
    clickObj(o);
  });
  ui.stage.addEventListener('pointerleave', () => { mouse.in = false; if (!drag) setHover(null); });
  ui.stage.addEventListener('contextmenu', e => e.preventDefault());
  simpleEl.addEventListener('click', e => { const b = e.target.closest('.sbody'); if (b) selectSimpleBody(+b.dataset.b); });
  window.addEventListener('wheel', e => {
    const inScroll = e.target.closest && e.target.closest('.sys-scroll');
    if (e.target.closest && e.target.closest('#inspector, #top, #rail, #chips')) return;
    if (inScroll && Math.abs(e.deltaX) > Math.abs(e.deltaY)) return; // horizontal swipe scrolls the row of worlds
    e.preventDefault(); hideHint();
    const dy = e.deltaY * (e.deltaMode === 1 ? 33 : e.deltaMode === 2 ? 400 : 1);
    const dl = clamp(dy * (e.ctrlKey ? 0.01 : 0.0011), -0.45, 0.45);
    if (mode === 'system') {
      if (sysBlend < 0.5) return;
      if (sysView === 'simple') { simpleExit = dl > 0 ? simpleExit + dl : 0; if (simpleExit > 0.5) { simpleExit = 0; exitSystem(); } return; }
      stween = null; if (dl < 0) sysFollow = sysSel >= 0;
      scam.goal.ld = clamp(scam.goal.ld + dl, scam.min, scam.max + 0.5);
      if (scam.goal.ld > scam.max + 0.3) exitSystem();
      return;
    }
    tween = null;
    const before = cam.goal.ld;
    cam.goal.ld = clamp(cam.goal.ld + dl, LOG.min, LOG.max);
    const k = 1 - Math.pow(10, cam.goal.ld - before);
    if (dl < 0 && hov && hov.t === 'star' && cam.goal.ld < B_REG) cam.goal.a.lerp(starPos(hov.i), clamp(k * 1.15, 0, 1));
    else if (dl < 0 && hov && hov.t === 'void' && cam.goal.ld < B_REG) cam.goal.a.lerp(hov.p, clamp(k * 1.15, 0, 1));
    if (before <= LOG.min + 0.001 && dl < 0) afterZoom(dl); else if (dl < 0) afterZoom(dl * 0.5);
  }, { passive: false });
  window.addEventListener('keydown', e => {
    if (e.target === qEl) return;
    const k = e.key;
    if (chooser) {
      if (k === 'ArrowDown' || (k === 'Tab' && !e.shiftKey)) { e.preventDefault(); chooserMove(chooser.idx + 1); return; }
      if (k === 'ArrowUp' || (k === 'Tab' && e.shiftKey)) { e.preventDefault(); chooserMove(chooser.idx - 1); return; }
      if (k === 'Enter') { e.preventDefault(); chooserPick(chooser.idx); return; }
      if (k === 'Escape') { e.preventDefault(); closeChooser(); return; }
    }
    if (k === 'Tab' && mouse.in && !panelOpen && levelOf() === 2) { e.preventDefault(); cycleNext(e.shiftKey ? -1 : 1); return; }
    if (mode === 'system' && sysView === 'realistic' && !e.ctrlKey && !e.metaKey) {
      if (k === ' ') { e.preventDefault(); sim.playing = !sim.playing; renderTime(); return; }
      if (k === ',') { sim.speed = Math.max(0, sim.speed - 1); sim.playing = true; renderTime(); return; }
      if (k === '.') { sim.speed = Math.min(SPEEDS.length - 1, sim.speed + 1); sim.playing = true; renderTime(); return; }
      if (k === 'l' || k === 'L') { sim.trueScale = !sim.trueScale; renderTime(); return; }
    }
    if (k === '/' || (k.toLowerCase() === 'k' && (e.ctrlKey || e.metaKey))) { e.preventDefault(); openSearch(); return; }
    if (k === 'Escape') { e.preventDefault(); backOut(); return; }
    if (e.ctrlKey || e.metaKey || e.altKey) return;
    if (mode === 'system' && sysView === 'simple' && simple && (k === 'ArrowLeft' || k === 'ArrowRight')) {
      const o = simple.order, j = o.indexOf(simple.sel);
      selectSimpleBody(o[clamp(j + (k === 'ArrowRight' ? 1 : -1), 0, o.length - 1)]); return;
    }
    const c = mode === 'system' && sysBlend > 0.5 ? scam : cam;
    const cur = mode === 'system' && SY ? SY.i : sel && sel.t === 'star' ? sel.i : -1;
    const key = k.length === 1 ? k.toLowerCase() : k;
    if (key === 'ArrowLeft' || key === 'a') c.goal.yaw += 0.18;
    else if (key === 'ArrowRight' || key === 'd') c.goal.yaw -= 0.18;
    else if (key === 'ArrowUp' || key === 'w') c.goal.pitch = clamp(c.goal.pitch + 0.12, PITCH_MIN, PITCH_MAX);
    else if (key === 'ArrowDown' || key === 's') c.goal.pitch = clamp(c.goal.pitch - 0.12, PITCH_MIN, PITCH_MAX);
    else if (key === '+' || key === '=' || key === 'PageUp') zoomBy(-0.3);
    else if (key === '-' || key === '_' || key === 'PageDown') zoomBy(0.3);
    else if (key >= '1' && key <= '4') goLevel(+key - 1);
    else if (key === 't' && cur >= 0 && cur !== ship.sys) setTarget(cur);
    else if (key === 'v') { if (mode === 'system') setSysView(sysView === 'simple' ? 'realistic' : 'simple'); else toggleTop(); }
    else if (key === 'c' || key === 'h') centreOnMe();
    else if (key === 'g') goLevel(goalLevel() === 0 ? 2 : 0);
    else if (key === 'r' && cur >= 0 && !auto) { if (mode === 'system') exitSystem(true); planTo(cur); }
    else if (key === 'f' && cur >= 0) toggleFav(cur);
    else if (key === 'e') toggleDrones();
    else if ((key === 'Enter' || key === 'o') && sel && sel.t === 'star' && mode !== 'system') enterSystem(sel.i);
    else if (key === ' ' && routeCtx) { e.preventDefault(); if (auto) stopAutopilot(); else startAutopilot(); }
    else return;
    hideHint(); if (c === cam && /Arrow|^[wasd]$/.test(key)) tween = null;
  });
  function backOut() {
    if (targeting) { targeting = null; ui.stage.classList.remove('targeting'); toast(I.drone, 'Drone not sent'); return; }
    if (droneOpen) { toggleDrones(false); return; }
    if (chooser) { closeChooser(); return; }
    if (panelOpen) { closeSearch(); return; }
    if (auto) { stopAutopilot(); return; }
    if (mode === 'system' && sysView === 'realistic' && sysSel >= 0 && sysSel !== 0) { sysSel = 0; focusBody(-1); renderInspector(); renderSysmap(); return; }
    if (mode === 'system') { exitSystem(); return; }
    if (routeCtx && sel && sel.t === 'star' && sel.i === routeCtx.dest) { const d = routeCtx.dest; routeCtx = null; drawRoute(); select({ t: 'star', i: d }, { keepCam: true }); return; }
    if (sel && sel.t === 'star' && fromList && list) { sel = null; fromList = false; restyle(); renderInspector(true); return; }
    if (sel || list) { clearAll(); return; }
    const l = goalLevel(); if (l > 0) goLevel(l - 1);
  }
  window.addEventListener('resize', resize);
  let railStops = [0, 0, 0, 0];
  function resize() {
    W = window.innerWidth; H = window.innerHeight;
    renderer.setSize(W, H, false);
    camera.aspect = sysCam.aspect = W / H; camera.updateProjectionMatrix(); sysCam.updateProjectionMatrix();
    for (const l of routeLines) l.material.resolution.set(W, H);
    panelsDirty = true;
    const r = ui.rail.getBoundingClientRect();
    railStops = ui.stops.map(b => { const q = b.getBoundingClientRect(); return q.top + q.height / 2 - r.top; });
    renderSysMode();
  }

  // ---------- HUD (breadcrumb, rail, level caption, scale) ----------
  let crumbKey = '', capKey = '';
  function niceRound(v) { const p = Math.pow(10, Math.floor(log10(v))), m = v / p; return (m < 1.5 ? 1 : m < 3.5 ? 2 : m < 7.5 ? 5 : 10) * p; }
  function updateHUD() {
    const lvl = levelOf(), c = cam.cur;
    const a = c.a, sk = sectorOfPos(a.x, a.y, a.z);
    const parts = [{ n: 'Milky Way', l: 0 }];
    if (lvl >= 1) parts.push({ n: armOf(a), l: 1 });
    if (lvl >= 2) parts.push({ n: sk >= 0 ? SECTORS[sk][0] : 'Uncharted sector', l: 2 });
    if (lvl >= 3 && SY) parts.push({ n: SYS.name[SY.i], l: 3 });
    const key = parts.map(p => p.n).join('|');
    if (key !== crumbKey) {
      const prev = crumbKey.split('|');
      crumbKey = key;
      const sep = `<span class="crumb-sep">${I.chev}</span>`;
      ui.crumbs.innerHTML = parts.map((p, k) => (k ? sep : '') + `<button class="crumb${k === parts.length - 1 ? ' cur' : ''}${prev[k] !== p.n ? ' enter' : ''}" data-l="${p.l}">${k === 0 ? '<span class="lvl-dot"></span>' : ''}${esc(p.n)}</button>`).join('');
      panelsDirty = true;
    }
    const d = Math.pow(10, c.ld), across = 2 * d * Math.tan(camera.fov * Math.PI / 360) * (W / H);
    const names = ['Galaxy', 'Region', 'Neighbourhood', 'System'];
    let sub;
    if (lvl === 3 && SY) sub = SY.unknownWorlds ? 'worlds unknown' : `${SY.nodes.length - 1} bodies${SY.schematic ? ' · schematic' : ''}`;
    else if (lvl === 0) sub = `≈ 120 billion systems · you know ${fmt(K.knownList.length)}`;
    else sub = `${fmt(niceRound(across))} ly across`;
    if (lvl === 2) sub += ` · you know ${fmt(K.knownList.length)} of the ${fmt(N)} systems within 150 ly`;
    if (names[lvl] + sub !== capKey) { capKey = names[lvl] + sub; ui.levelname.textContent = names[lvl]; ui.levelsub.textContent = sub; }
    let u;
    if (c.ld >= LOG.galaxy) u = 0;
    else if (c.ld >= LOG.region) u = (LOG.galaxy - c.ld) / (LOG.galaxy - LOG.region);
    else if (c.ld >= LOG.nbr) u = 1 + (LOG.region - c.ld) / (LOG.region - LOG.nbr);
    else u = 2 + 0.5 * clamp((LOG.nbr - c.ld) / (LOG.nbr - LOG.min), 0, 1);
    u = lerp(u, 3, easeInOut(sysBlend));
    const i0 = Math.min(2, Math.floor(u)), f = u - i0, y = lerp(railStops[i0], railStops[i0 + 1], f);
    ui.thumb.style.transform = `translate3d(0,${(y - 8).toFixed(1)}px,0)`;
    ui.railFill.style.height = Math.max(0, y - railStops[0]) + 'px';
    ui.stops.forEach((b, k) => { b.classList.toggle('cur', k === lvl); b.classList.toggle('near', Math.abs(u - k) < 0.5 && k !== lvl); });
    const showChips = lvl === 2 && mode === 'map';
    if (ui.chips.classList.contains('hide') === showChips) { ui.chips.classList.toggle('hide', !showChips); panelsDirty = true; }
    if (lvl === 3) ui.scalebar.parentElement.style.opacity = 0;
    else {
      ui.scalebar.parentElement.style.opacity = 1;
      const lyPerPx = 2 * d * Math.tan(camera.fov * Math.PI / 360) / H;
      const nice = niceRound(lyPerPx * 120);
      ui.scalebar.style.width = (nice / lyPerPx).toFixed(0) + 'px'; ui.scaletxt.textContent = fmt(nice) + ' ly';
    }
  }
  ui.crumbs.addEventListener('click', e => { const b = e.target.closest('.crumb'); if (!b || b.classList.contains('cur')) return; goLevel(+b.dataset.l); });

  // ---------- Hash (deep links for review and screenshots) ----------
  let hashTimer = 0, initDone = false;
  function updateHash() {
    clearTimeout(hashTimer);
    hashTimer = setTimeout(() => {
      if (!initDone) return;
      const p = new URLSearchParams();
      p.set('level', ['galaxy', 'region', 'neighbourhood', 'system'][goalLevel()]);
      if (mode === 'system' && SY) { p.set('select', SYS.name[SY.i]); p.set('view', sysView); if (sysSel >= 0 && SY.raw[sysSel]) p.set('body', SY.raw[sysSel][0]); }
      else if (sel && sel.t === 'star') p.set('select', SYS.name[sel.i]);
      if (sel && sel.t === 'group') p.set('group', K.groups[sel.g].id);
      if (sel && sel.t === 'sector') p.set('sector', SECTORS[sel.k][0]);
      if (sel && sel.t === 'struct') p.set('struct', sel.id);
      if (list) p.set('intent', list.id);
      if (routeCtx) { p.set('route', SYS.name[routeCtx.dest]); if (routeCtx.active === routeCtx.safe) p.set('safe', '1'); }
      if (activeFilters.size) p.set('filters', [...activeFilters].join(','));
      if (topView) p.set('top', '1');
      if (mode === 'system' && sim.trueScale) p.set('scale', 'true');
      history.replaceState(null, '', '#' + p.toString().replace(/\+/g, '%20'));
    }, 400);
  }

  // ---------- Frame ----------
  let last = performance.now(), fpsAcc = 0, fpsN = 0, pinKey = '';
  window.__fps = 0;
  const lerpOp = (o, v, dt) => o + (v - o) * (1 - Math.exp(-dt * 8));
  const op = { structL: 1, sectL: 0, armL: 0, starL: 0, pin: 1 };
  const pinObj = { t: 'pin' };
  function frameBody(now) {
    const dt = clamp((now - last) / 1000, 0, 0.1); last = now; // the rAF time can precede performance.now() on the first frame
    fpsAcc += dt; fpsN++; if (fpsAcc > 0.5) { window.__fps = Math.round(fpsN / fpsAcc); fpsAcc = 0; fpsN = 0; }
    if (tween && stepTween(tween, now)) tween = null;
    if (stween && stepTween(stween, now)) stween = null;
    smoothCam(cam, dt, 9); smoothCam(scam, dt, 9);
    const shiftGoal = document.body.classList.contains('insp-open') && W > 900 ? (Math.min(368, W * 0.3) + 20) / 2 : 0;
    viewShift = viewShift + (shiftGoal - viewShift) * (1 - Math.exp(-dt * 7));
    if (Math.abs(viewShift - shiftGoal) < 0.3) viewShift = shiftGoal;
    const sp = clamp((now - sysT0) / 900, 0, 1);
    sysBlend = lerp(sysFrom, sysGoal, easeInOut(sp));
    tickAutopilot(dt);
    tickDrones(dt, now);
    // the system map's clock and scale (Realistic view)
    if (mode === 'system' && sim.playing) sim.days += dt * SPEEDS[sim.speed][0];
    sim.blend += ((sim.trueScale ? 1 : 0) - sim.blend) * (1 - Math.exp(-dt * 6));
    if (Math.abs(sim.blend - (sim.trueScale ? 1 : 0)) < 0.001) sim.blend = sim.trueScale ? 1 : 0;
    if (SY && mode === 'system') {
      layoutSystem(); pumpTextures(7);
      if (sysView === 'realistic') {
        if (sysFollow && sysSel >= 0 && !stween && SY.nodes[sysSel]) scam.goal.a.copy(SY.nodes[sysSel].pos).multiplyScalar(SY.root.scale.x);
        renderTime();
      }
    }
    placeCamera();
    const x = cam.cur.ld, main = 1 - sysBlend;
    const gal = smooth(2.95, 3.85, x) * lerp(0.42, 1, smooth(3.9, 4.6, x));
    const glow = smooth(2.9, 3.5, x) * lerp(0.16, 0.85, smooth(3.85, 4.6, x));   // concept Known: the galaxy reads as haze
    const hz = smooth(2.65, 3.2, x) * (1 - smooth(4.15, 4.6, x));
    const sect = smooth(2.95, 3.3, x) * (1 - smooth(4.0, 4.35, x));
    const arms = smooth(3.15, 3.55, x) * (1 - smooth(4.25, 4.65, x));
    const local = 1 - smooth(2.9, 3.45, x);
    const nbr = 1 - smooth(2.35, 2.75, x);
    op.structL = smooth(4.35, 4.6, x); op.sectL = smooth(3.15, 3.4, x) * (1 - smooth(3.95, 4.25, x)); op.armL = arms; op.starL = 1 - smooth(2.45, 2.8, x); op.pin = smooth(2.7, 3.0, x);
    galaxyPts.material.uniforms.uOpacity.value = gal * main; galaxyPts.visible = gal * main > 0.003;
    glowMat.uniforms.uOpacity.value = glow * main; glowPlane.visible = glow * main > 0.003;
    haze.material.uniforms.uOpacity.value = hz * main * 0.9; haze.visible = hz * main > 0.003;
    sectorMat.uniforms.uOpacity.value = sect * main; sectorPlane.visible = sect * main > 0.003;
    for (const a of ARMS) {
      a.hi = lerpOp(a.hi, hov && hov.t === 'struct' && STRUCTS.find(s => s.id === hov.id)?.path === a.id ? 1 : 0, dt);
      a.mat.uniforms.uOpacity.value = (arms * 0.22 + a.hi * 0.25) * main; a.mesh.visible = a.mat.uniforms.uOpacity.value > 0.003;
    }
    const d = Math.pow(10, x);
    field.update({ target, dist: d, opacity: local * main, focal: sysFocalMap(), now, haze: 1 });
    gridMat.uniforms.uOpacity.value = nbr * main; grid.visible = nbr * main > 0.003; grid.position.copy(ship.pos);
    bubble.position.copy(ship.pos); bubble.material.uniforms.uOpacity.value = nbr * main * (activeFilters.has('reach') ? 1.6 : 1); bubble.visible = nbr * main > 0.003;
    bubbleRing.position.copy(ship.pos); bubbleRing.material.opacity = nbr * main * 0.5; bubbleRing.visible = bubble.visible;
    for (const dm of dangerMeshes) { dm.m.material.uniforms.uOpacity.value = nbr * main * (1 + dm.boost * 1.2); dm.m.visible = nbr * main > 0.003; }
    if (routeGroup) {
      const ra = local * main, far = activeRoute() && activeRoute().far;
      routeGroup.visible = ra > 0.01 || !!far;
      for (const l of routeLines) l.material.opacity = l.userData.base * (l.userData.far ? main : ra);
      stopMat.uniforms.uOpacity.value = ra;
      if (comet) {
        comet.visible = !auto && ra > 0.01;
        if (comet.visible) { cometT = clamp((cometT + dt / 2.6) % 1, 0, 0.999); const p = routeCurve.getPoint(cometT); const a = comet.geometry.attributes.position; a.array[0] = p.x; a.array[1] = p.y; a.array[2] = p.z; a.needsUpdate = true; cometMat.uniforms.uA.value = ra * Math.sin(Math.PI * cometT); }
      }
    }
    const real = sysView === 'realistic';
    if (SY) {
      const sb = easeOut(sysBlend) * (real ? 1 : 0);
      SY.root.scale.setScalar(lerp(0.3, 1, easeOut(sysBlend)));
      SY.root.traverse(o => { if (o.material) { const base = o.userData.baseOp ?? 1; o.material.opacity = base * sb; } });
      SY.nodes.forEach(n => { if (n.orbitLine) { const s = n.k === sysSel ? 1 : n.k === sysHov ? 0.6 : 0; n.orbitLine.material.color.set(s > 0 ? 0x8cc4f8 : n.kind === 2 ? 0x3d6698 : 0x4a7fc0); n.orbitLine.material.opacity = (n.orbitLine.userData.baseOp + s * 0.3) * sb; } });
      sysScene.visible = sb > 0.002;
    }
    const showSimple = mode === 'system' && !real && sysBlend > 0.55;
    if (simpleEl.classList.contains('show') !== showSimple) simpleEl.classList.toggle('show', showSimple);
    setDrop(dropSel, sel && sel.t === 'star' ? sel.i : -1, nbr * main);
    setDrop(dropHov, hov && hov.t === 'star' && !(sel && sel.t === 'star' && sel.i === hov.i) ? hov.i : -1, nbr * main);

    renderer.clear();
    if (sysBlend < 0.999) renderer.render(scene, camera);
    if (sysBlend > 0.001) {
      placeSysCamera(); sysLight.position.set(0, 0, 0);
      veilMat.uniforms.uC.value.copy(real ? VEIL_NAVY : VEIL_DARK);
      veilMat.uniforms.uA.value = easeInOut(sysBlend) * (real ? 0.96 : 0.94); renderer.render(veilScene, veilCam);
      if (SY && sysScene.visible) renderer.render(sysScene, sysCam);
    }

    // labels and markers
    readPanels(now);
    prepVP(camera); prepVP(sysCam);
    frameNo++; placed = []; cands = [];
    const markerSize = p => [p.x - 9, p.y - 9, p.x + 9, p.y + 9];
    proj(camera, ship.pos.x, ship.pos.y, ship.pos.z, projV);
    if (projV.ok) { setMk(MK.ship, projV.x, projV.y, main); MK.ship.el.classList.toggle('gal', x > B_REG); if (main > 0.3) placed.push(markerSize(projV)); } else setMk(MK.ship, 0, 0, 0);
    const pinTxt = `Known space · ${fmt(K.knownList.length)} systems`;
    if (op.pin > 0.02 && projV.ok) addCand({ key: 'pin', pri: 5000, x: projV.x, y: projV.y, w: textW(pinTxt, ST.pin) + 34, h: 40, off: 14, mode: 'above', cls: 'lbl-pin', html: `<span class="pin">${pinTxt}</span><div class="pin-stem"></div>`, alpha: op.pin * main, obj: pinObj, wp: [ship.pos.x, ship.pos.y, ship.pos.z], cm: camera });
    // what you know, seen from far out: one small bright patch (about 70 ly across, never smaller than a fingertip)
    proj(camera, homeP.x, homeP.y, homeP.z, projV);
    if (projV.ok && op.pin > 0.02) { const sz = Math.max(30, 140 * sysFocalMap() / projV.w); MK.known.el.style.width = MK.known.el.style.height = sz.toFixed(0) + 'px'; MK.known.el.style.margin = (-sz / 2).toFixed(0) + 'px 0 0 ' + (-sz / 2).toFixed(0) + 'px'; setMk(MK.known, projV.x, projV.y, op.pin * main); } else setMk(MK.known, 0, 0, 0);
    const ringAt = (m, i, a) => { if (i < 0) { setMk(m, 0, 0, 0); return; } proj(camera, PX[i], PY[i], PZ[i], projV); if (projV.ok) { setMk(m, projV.x, projV.y, a); if (a > 0.3) placed.push(markerSize(projV)); } else setMk(m, 0, 0, 0); };
    ringAt(MK.sel, sel && sel.t === 'star' ? sel.i : -1, local * main);
    ringAt(MK.hov, hov && hov.t === 'star' ? hov.i : -1, local * main * 0.9);
    ringAt(MK.tgt, targetSys, local * main);
    if (hovInfo && hov && hov.t === 'star' && mouse.in && !drag && local * main > 0.3) {
      proj(camera, PX[hov.i], PY[hov.i], PZ[hov.i], projV);
      const dd = projV.ok ? Math.hypot(projV.x - mouse.x, projV.y - mouse.y) : 0;
      if (projV.ok && dd > 7 && dd < 60) { leader.setAttribute('x1', mouse.x); leader.setAttribute('y1', mouse.y); leader.setAttribute('x2', projV.x); leader.setAttribute('y2', projV.y); leaderSvg.style.opacity = '1'; }
      else leaderSvg.style.opacity = '0';
    } else leaderSvg.style.opacity = '0';

    if (main > 0.02) {
      if (op.structL > 0.02) for (const s of STRUCTS) {
        proj(camera, s.label[0], s.label[1], s.label[2], projV); if (!projV.ok) continue;
        const kind = s.kind, st = kind === 'spur' || kind === 'bar' ? ST.structs : ST.struct;
        const isSel = sel && sel.t === 'struct' && sel.id === s.id;
        addCand({ key: 'g' + s.id, pri: 3000 - (kind === 'spur' ? 50 : 0) + (isSel ? 100 : 0), x: projV.x, y: projV.y, w: textW(s.name, st) + 18, h: 25, mode: 'center', nudge: 16, cls: 'lbl-struct pill ' + kind + (isSel ? ' sel' : ''), html: esc(s.name), alpha: op.structL * main, obj: { t: 'struct', id: s.id }, wp: s.label, cm: camera });
      }
      if (op.sectL > 0.02) {
        const cs = sectorOfPos(cam.cur.a.x, 0, cam.cur.a.z);
        for (const o of regionSectors) {
          const s = o.s; proj(camera, s[4], SECTOR_Y, s[6], projV); if (!projV.ok) continue;
          const dist = Math.hypot(s[4] - target.x, s[6] - target.z), fade = 1 - smooth(2600, 3600, Math.hypot(s[4], s[6]));
          const isCur = o.k === 0, isHov = hov && hov.t === 'sector' && hov.k === o.k, isSel = sel && sel.t === 'sector' && sel.k === o.k;
          addCand({ key: 'c' + o.k, pri: 1500 - dist / 100 + (isCur ? 1200 : 0) + (isHov || isSel ? 1300 : 0) + (o.k === cs ? 50 : 0), x: projV.x, y: projV.y, w: textW(s[0], ST.sector), h: ST.sector.h, mode: 'center', nudge: isCur ? 14 : 0, cls: 'lbl-sector' + (isCur ? ' cur' : '') + (isHov || isSel ? ' hov' : ''), html: esc(s[0]), alpha: op.sectL * main * fade, obj: { t: 'sector', k: o.k }, wp: [s[4], SECTOR_Y, s[6]], cm: camera });
        }
      }
      if (op.armL > 0.02) for (const a of ARMS) {
        const spots = [];
        for (let j = 2; j < a.samples.length - 2; j++) {
          const s = a.samples[j]; if (s.w < 0.3) continue;
          proj(camera, s.p.x, s.p.y, s.p.z, projV); if (!projV.ok) continue;
          if (projV.x < 140 || projV.x > W - 140 || projV.y < 100 || projV.y > H - 100) continue;
          spots.push({ j, x: projV.x, y: projV.y, p: s.p, score: Math.abs(Math.hypot(projV.x - W * 0.5, projV.y - H * 0.5) - Math.min(W, H) * 0.3) });
        }
        if (!spots.length) continue;
        spots.sort((p, q) => p.score - q.score);
        const alts = [];
        for (const s0 of spots.slice(0, 24)) {
          if (alts.some(q => Math.hypot(q.x - s0.x, q.y - s0.y) < 90)) continue;
          const s2 = a.samples[Math.min(a.samples.length - 1, s0.j + 2)], s1 = a.samples[Math.max(0, s0.j - 2)];
          const p1 = proj(camera, s1.p.x, s1.p.y, s1.p.z, {}), p2 = proj(camera, s2.p.x, s2.p.y, s2.p.z, {});
          let ang = Math.atan2(p2.y - p1.y, p2.x - p1.x); if (ang > Math.PI / 2) ang -= Math.PI; if (ang < -Math.PI / 2) ang += Math.PI;
          alts.push({ x: s0.x, y: s0.y, ang, p: s0.p }); if (alts.length >= 5) break;
        }
        const name = armLabelName(a, alts[0].p);
        addCand({ key: 'a' + a.id + name, pri: 1800, x: alts[0].x, y: alts[0].y, alts, w: textW(name, ST.arm), h: ST.arm.h, mode: 'rot', ang: alts[0].ang, cls: 'lbl-arm', html: esc(name), alpha: op.armL * main * 0.95 });
      }
      field.project(camera, W, H);
      starLabels(op.starL * main);
      for (const gs of tiles.groupSpots()) {
        const g = gs.g; if (gs.n < 3 || op.starL < 0.02) continue;
        const t1 = g.name, t2 = g.summary() + (g.kind === 'survey' ? ' · ' + g.drone : g.when ? ' · ' + g.when : '');
        const w = Math.max(textW(t1, ST.terr), textW(t2, ST.terr2));
        const isSel = sel && sel.t === 'group' && sel.g === g.index;
        addCand({ key: 'G' + g.index, pri: 660 + (isSel ? 400 : 0) + (g.kind === 'survey' ? 30 : 0) - (g.kind === 'route' ? 200 : 0), x: gs.x, y: gs.y, w, h: 30, mode: 'center', nudge: 22, cls: 'lbl-terr grp k-' + g.kind + (isSel ? ' sel' : ''),
          html: `<div class="t1">${esc(t1)}</div><div class="t2">${esc(t2)}</div>`, alpha: op.starL * main, fc: g.kind === 'home' ? ACCENT : g.kind === 'survey' ? KCOL.scanned : KCOL.visited, obj: { t: 'group', g: g.index }, pad: 6 });
      }
      if (op.starL > 0.02) {
        const yaw = cam.cur.yaw;
        for (const r of [25, 50, 100]) {
          const px = ship.pos.x - Math.sin(yaw + 0.5) * r, pz = ship.pos.z - Math.cos(yaw + 0.5) * r;
          proj(camera, px, ship.pos.y, pz, projV); if (!projV.ok) continue;
          addCand({ key: 'r' + r, pri: 150, x: projV.x, y: projV.y, w: textW(r + ' ly', ST.ring), h: ST.ring.h, mode: 'center', cls: 'lbl-ring', html: r + ' ly', alpha: op.starL * main, wp: [px, ship.pos.y, pz], cm: camera });
        }
        const gp = [ship.pos.x, ship.pos.y, ship.pos.z + 112];
        proj(camera, gp[0], gp[1], gp[2], projV);
        if (projV.ok) addCand({ key: 'dirgc', pri: 160, x: projV.x, y: projV.y, w: textW('Galactic centre ↑', ST.dir), h: ST.dir.h, mode: 'center', cls: 'lbl-ring dir', html: 'Galactic centre ↑', alpha: op.starL * main * 0.9, wp: gp, cm: camera });
        for (const dm of dangerMeshes) {
          const c = dm.d.centre; proj(camera, c[0], c[1], c[2], projV); if (!projV.ok) continue;
          addCand({ key: 'd' + dm.d.id, pri: 700, x: projV.x, y: projV.y, w: textW(dm.d.name, ST.danger), h: ST.danger.h, mode: 'center', cls: 'lbl-danger' + (dm.d.kind === 'hazard' ? ' hazard' : ''), html: esc(dm.d.name), alpha: op.starL * main * 0.95, wp: c, cm: camera });
        }
      }
    }
    {   // tiles and drone effects (Neighbourhood); cleared further out and inside systems
      const rs = activeRoute(), tilesA = main > 0.02 ? (1 - smooth(2.62, 2.92, x)) * main : 0;
      const pk = [sel && sel.t === 'star' ? sel.i : -1, targetSys, rs ? rs.stops.length + ':' + rs.to : '', listSet ? listSet.size : 0, K.version].join('|');
      if (pk !== pinKey) { pinKey = pk; updatePinned(); tiles.dirty(); }
      tiles.frame({ camera, W, H, dist: d, alpha: tilesA, now, hover: hov && hov.t === 'star' && !(drag && drag.moved) ? hov.i : -1,
        sel: sel && sel.t === 'star' ? sel.i : -1, selGroup: sel && sel.t === 'group' ? sel.g : -1, route: rs ? rs.stops.map(q => q.sys) : null });
      drawFx(now, (1 - smooth(2.75, 3.1, x)) * main);
    }
    // Realistic view: Hollow's orange ring around the selected body, a thin one on hover
    {
      const ringBody = (m, k, a) => {
        if (!SY || k < 0 || !SY.nodes[k] || a < 0.02) { setMk(m, 0, 0, 0); return; }
        const n = SY.nodes[k], sc = SY.root.scale.x; proj(sysCam, n.pos.x * sc, 0, n.pos.z * sc, projV); if (!projV.ok) { setMk(m, 0, 0, 0); return; }
        const r = (n.mesh ? n.mesh.scale.x : 2) * sc * sysFocal() / projV.w, sz = Math.max(14, 2 * r + 12);
        m.el.style.width = m.el.style.height = sz.toFixed(1) + 'px'; m.el.style.margin = (-sz / 2).toFixed(1) + 'px 0 0 ' + (-sz / 2).toFixed(1) + 'px';
        setMk(m, projV.x, projV.y, a);
      };
      const ra = mode === 'system' && real ? smooth(0.55, 0.95, sysBlend) : 0;
      ringBody(MK.body, sysSel, ra); ringBody(MK.bhov, sysHov !== sysSel ? sysHov : -1, ra * 0.8);
    }
    if (SY && sysBlend > 0.4 && real) {
      const a = smooth(0.55, 0.95, sysBlend), dsys = Math.pow(10, scam.cur.ld), sc = SY.root.scale.x;
      for (const n of SY.nodes) {
        if (n.kind === 3) continue;
        proj(sysCam, n.pos.x * sc, 0, n.pos.z * sc, projV); if (!projV.ok) continue;
        const r = Math.max(3, n.size * sysFocal() / projV.w * (n.rings ? 2.2 : 1)) * (n === SY.prim ? 1.5 : 1);
        placed.push([projV.x - r, projV.y - r * (n.rings ? 0.6 : 1), projV.x + r, projV.y + r * (n.rings ? 0.6 : 1)]);
      }
      for (const n of SY.nodes) {
        if (n.kind === 3) continue;
        proj(sysCam, n.pos.x * sc, 0, n.pos.z * sc, projV); if (!projV.ok) continue;
        const isSel = n.k === sysSel, isHov = n.k === sysHov, par = SY.nodes[n.parent];
        if (n.kind === 2 && !isSel && !isHov && !(par && (par.k === sysSel || par.k === sysHov)) && dsys > 60) continue;
        const pxR = n.size * sysFocal() / projV.w * (n.rings ? 2.2 : 1);
        let pri = n.kind === 0 ? 900 : n.kind === 1 ? 600 + n.size * 10 : 200;
        if (isSel) pri += 2000; if (isHov) pri += 1500; if (n.home || n.find || n.player) pri += 200; if (n.land) pri += 20;
        // Hollow's labels: the short name under the body (orange when selected), a mark for your base or a find
        const cls = 'lbl-body hl' + (n.kind === 2 ? ' moon' : '') + (isSel ? ' sel' : '');
        const mark = n.home ? `<span class="ic ic-home">${I.home}</span>` : n.find ? `<span class="ic ic-find">${I[FIND_IC[n.find.kind]]}</span>` : n.player ? `<span class="ic ic-player">${I.flag}</span>` : '';
        const t1 = n.kind === 0 && n === SY.prim ? n.name : n.short;
        const st = n.kind === 2 ? ST.moon : ST.body, w = textW(t1, st) + (mark ? 18 : 0), h = n.kind === 2 ? 15 : 17;
        const html = `<div class="t1">${mark}${esc(t1)}</div>`;
        addCand({ key: 'b' + SY.i + ':' + n.k, pri, x: projV.x, y: projV.y + Math.max(6, pxR + 4) + h / 2, w, h, mode: 'center', cls, html, alpha: a, obj: { t: 'body', k: n.k }, wp: [n.pos.x * sc, 0, n.pos.z * sc], cm: sysCam });
      }
      for (const n of SY.nodes) {
        if (n.kind !== 3) continue;
        const rb = lerp(n.orbit, n.orbitTrue, sim.blend);
        const q = proj(sysCam, Math.cos(-0.6) * rb * sc, 0, Math.sin(-0.6) * rb * sc, {}); if (!q.ok) continue;
        addCand({ key: 'b' + SY.i + ':' + n.k, pri: 300, x: q.x, y: q.y, w: textW(n.short, ST.body), h: 15, mode: 'center', cls: 'lbl-body belt', html: `<div class="t1">${esc(n.short)}</div>`, alpha: a * 0.8 });
      }
    }
    flushLabels(now);
    for (const [k, rec] of LB) if (rec.seen === frameNo && k[0] === 's') labelledLast.add(+k.slice(1));
    if (mode === 'system' && sysView === 'realistic' && sysBlend > 0.5) { if (!document.body.classList.contains('sysreal')) renderSysmap(); }
    else if (document.body.classList.contains('sysreal')) { document.body.classList.remove('sysreal'); panelsDirty = true; }
    updateHUD();
    if (!initDone) { initDone = true; finishInit(); }
  }
  // one bad frame must never stop the map: report it once and keep drawing
  function frame(now) {
    try { frameBody(now); } catch (e) { if (!frame.failed) { frame.failed = true; console.error(e); } }
    requestAnimationFrame(frame);
  }

  // ---------- init ----------
  function finishInit() {
    $('#loading').classList.add('done');
    console.log(`[map] ready in ${(performance.now() - t0).toFixed(0)} ms · ${N} local systems · ${GS.count} galaxy points`);
    setTimeout(() => { window.__ready = true; }, 300);
    // deep link debug=1: after 6 s, write the frame rate, tile counts and the placed labels into a hidden <pre id="dbg">
    if (params.has('debug')) setTimeout(() => {
      const rows = []; for (const [k, r] of LB) if (r.seen === frameNo) rows.push(k + ' | ' + r.tr + ' | ' + r.op + ' | ' + r.el.textContent);
      const pre = document.createElement('pre'); pre.id = 'dbg'; pre.style.display = 'none';
      pre.textContent = JSON.stringify({ fps: window.__fps, tiles: tiles.count, cells: tiles.cells, buildAvgMs: tiles.bench(30), stats: tiles.stats, labels: rows }, null, 1);
      document.body.appendChild(pre);
    }, 6000);
  }
  function applyParams() {
    INSTANT_NOW = true;
    const g = k => params.get(k);
    if (g('filters')) { g('filters').split(',').forEach(f => { if (FILTERS.some(q => q.id === f)) activeFilters.add(f); }); applyFilters(); }
    if (g('top') === '1') { topView = true; ui.top.classList.add('on'); }
    if (g('view') === 'realistic' || g('view') === 'simple') sysView = g('view');
    const lvl = g('level');
    const s = findStar(g('select')), r = findStar(g('route'));
    if (lvl === 'galaxy') { cam.goal.ld = LOG.galaxy; cam.goal.pitch = 0.62; }
    else if (lvl === 'region') { cam.goal.ld = LOG.region; cam.goal.pitch = 0.86; }
    if (g('yaw')) cam.goal.yaw = +g('yaw') * Math.PI / 180;
    if (g('pitch')) cam.goal.pitch = clamp(+g('pitch') * Math.PI / 180, PITCH_MIN, PITCH_MAX);
    if (g('dist')) cam.goal.ld = clamp(log10(+g('dist')), LOG.min, LOG.max);
    if (topView) cam.goal.pitch = PITCH_MAX;
    const f = findStar(g('focus')); if (f >= 0) cam.goal.a.copy(starPos(f));
    if (g('sector')) { const k = SECTORS.findIndex(q => q[0].toLowerCase() === g('sector').toLowerCase()); if (k >= 0) { select({ t: 'sector', k }, { keepCam: true }); if (k) cam.goal.a.set(SECTORS[k][4], 0, SECTORS[k][6]); } }
    if (g('struct')) select({ t: 'struct', id: g('struct') }, { keepCam: true });
    if (g('group')) { const gr = K.groupById(g('group')); if (gr) { select({ t: 'group', g: gr.index }, { keepCam: true }); if (!g('dist')) { cam.goal.a.set(gr.centre[0], gr.centre[1], gr.centre[2]); cam.goal.ld = clamp(log10(Math.max(gr.radius, 8) * 3.4 + 12), log10(30), log10(420)); } } }
    if (g('scale') === 'true') { sim.trueScale = true; sim.blend = 1; }
    if (g('time')) sim.days = +g('time') || 0;
    if (s >= 0 && lvl !== 'system') select({ t: 'star', i: s }, { keepCam: true });
    if (g('intent')) runIntent(g('intent'));
    if (r >= 0) planTo(r, { safe: params.has('safe'), steps: params.has('steps') });
    if (lvl === 'system') {
      const i = s >= 0 ? s : r >= 0 ? r : ship.sys;
      let body; if (g('body')) { const k = bodiesOf(i).bodies.findIndex(b => b[0].toLowerCase() === g('body').toLowerCase()); if (k >= 0) body = k; }
      enterSystem(i, { body, view: sysView });
    }
    snapCam(cam); snapCam(scam);
    if (params.has('auto') && routeCtx) {
      startAutopilot();
      const frac = +g('auto') || 0.5, r2 = routeCtx.active;
      let t = ((r2.stops.length - 1) * (HOP_S + STOP_S) + r2.refuels * REFUEL_S) * frac;
      while (t > 0 && auto) { tickAutopilot(0.05); t -= 0.05; }
      snapCam(cam);
    }
    if (g('hover')) {
      const i = findStar(g('hover'));
      if (i >= 0) setTimeout(() => { placeCamera(); prepVP(camera); proj(camera, PX[i], PY[i], PZ[i], projV); mouse.x = projV.x; mouse.y = projV.y; setHover({ t: 'star', i }); }, 200);
    }
    if (params.has('search')) setTimeout(() => openSearch(g('search') || ''), 50);
    // drones: drone=<star> or drone=x,y,z sends the ready drone; phase=travel|scan|reveal|done and t=<s> fast-forward it
    if (g('drone')) {
      const v = g('drone'), st = findStar(v), xyz = v.split(',').map(Number);
      const tgt = st >= 0 ? { sys: st } : xyz.length === 3 && xyz.every(q => isFinite(q)) ? { p: new V3(xyz[0], xyz[1], xyz[2]) } : null;
      if (tgt) {
        sendDrone(tgt);
        const d = drones.find(q => q.state === 'outbound'), ph = g('phase') || 'travel', tt = +g('t') || 0;
        if (d) {
          let fast = ph === 'travel' ? Math.min(tt, d.legDur * 0.98) : d.legDur + 0.001;
          if (ph === 'scan' || ph === 'reveal' || ph === 'done') fast += ph === 'scan' ? Math.min(tt, 1.4) : 1.5;
          const t0f = performance.now(), step = 1 / 30;
          for (let q = 0; q < fast; q += step) tickDrones(step, t0f);
          if (ph === 'reveal' || ph === 'done') {
            const back = ph === 'done' ? 2.6 : tt;   // shift the reveal into the past so the wave shows its moment
            K.knownList.forEach(i => { if (K.since[i] > 0) K.since[i] -= back * 1000; }); reveals.forEach(r => { r.t0 -= back * 1000; });
            field.setKnowledge(K.level, K.since);
          }
          if (!g('focus') && !g('dist')) { cam.goal.a.copy(d.targetP).lerp(starPos(HOME), 0.35); cam.goal.ld = log10(Math.max(110, d.targetP.distanceTo(starPos(HOME)) * 2.1)); }
        }
      }
    }
    if (g('skip')) { const d = drones.find(q => q.id === g('skip')); if (d) skipDrone(d); }
    if (params.has('drones')) toggleDrones(true);
    if (g('mouse')) { const [mx, my] = g('mouse').split(',').map(Number); setTimeout(() => { mouse.x = mx; mouse.y = my; mouse.in = true; setHover(pickAt(mx, my)); positionHoverCard(); }, 900); }
    if (params.toString()) hideHint();
    INSTANT_NOW = false;
  }

  resize();
  field.setKnowledge(K.level, K.since);
  updatePinned();
  renderChips(); renderShip(); renderInspector(); renderSysMode(); renderDroneBtn();
  restyle();
  applyParams();
  window.__app = {
    cam, scam, ship, HD, K, tiles, drones, sendDrone, skipDrone, sim, enterSystem, exitSystem, select, goLevel, setHover, mouse, SYS, byName, planTo, planFar, openSearch, runIntent, setSysView, pickAt,
    get route() { return routeCtx; }, get level() { return levelOf(); }, get mode() { return mode; }, field, openChooser, get chooser() { return chooser; }, cycleNext, get starLabels() { let n = 0; for (const [k, r] of LB) if (r.seen === frameNo && k[0] === 's') n++; return n; },
    screenOf: n => { const i = byName.get(n.toLowerCase()); prepVP(camera); proj(camera, PX[i], PY[i], PZ[i], projV); return [Math.round(projV.x), Math.round(projV.y)]; },
  };
  const go = () => requestAnimationFrame(frame);
  if (document.fonts && document.fonts.load) Promise.race([document.fonts.load('500 12px Inter'), new Promise(r => setTimeout(r, 1500))]).then(() => { widthCache.clear(); go(); });
  else go();
}
})();
