/* Hollow Universe galaxy map, Concept B: "Zoom levels".
   A classic script (works from file://). index.html imports three.js as a module and calls HollowMap.start().
   Four levels, each showing only what makes sense at its scale: Galaxy, Region, Neighbourhood, System. */
(function () {
'use strict';

const ICON = {
  route: '<path d="M5 15.5c0-3.2 3.2-3.4 5-5.4s5-2.2 5-5.6"/><circle cx="5" cy="15.5" r="1.9"/><circle cx="15" cy="4.5" r="1.9"/>',
  target: '<circle cx="10" cy="10" r="5.6"/><path d="M10 1.8v4.2M10 14v4.2M1.8 10H6M14 10h4.2"/>',
  star: '<path d="M10 2.6l2.25 4.7 5.05.62-3.72 3.47.98 5.05L10 13.9l-4.56 2.54.98-5.05L2.7 7.92l5.05-.62z"/>',
  system: '<circle cx="10" cy="10" r="2.3" class="fill"/><ellipse cx="10" cy="10" rx="7.6" ry="3.5" transform="rotate(-24 10 10)"/>',
  close: '<path d="M5.5 5.5l9 9M14.5 5.5l-9 9"/>',
  fuel: '<path d="M10 2.6s-4.9 5.4-4.9 8.8a4.9 4.9 0 0 0 9.8 0c0-3.4-4.9-8.8-4.9-8.8z"/>',
  station: '<path d="M10 2.6l6.4 3.7v7.4L10 17.4l-6.4-3.7V6.3z"/><circle cx="10" cy="10" r="2.1"/>',
  home: '<path d="M3.4 9.6L10 4l6.6 5.6"/><path d="M5.6 8.4V16h8.8V8.4"/>',
  flag: '<path d="M5.5 17.5V3"/><path d="M5.5 3.8h9l-2.1 3.1 2.1 3.1h-9"/>',
  land: '<path d="M10 3.6l3.4 5.9H6.6z"/><path d="M7.6 9.5l-2.2 4.2M12.4 9.5l2.2 4.2"/><path d="M2.8 16.2h14.4"/>',
  danger: '<path d="M10 3.2l7.4 13H2.6z"/><path d="M10 8.3v3.6"/><circle cx="10" cy="14.1" r=".6" class="fill"/>',
  play: '<path d="M6.6 4.4v11.2l9-5.6z" class="fill"/>',
  stop: '<rect x="5.5" y="5.5" width="9" height="9" rx="1.6" class="fill"/>',
  globe: '<circle cx="10" cy="10" r="6.6"/><path d="M3.4 10h13.2M10 3.4c2.3 2.2 2.3 11 0 13.2M10 3.4c-2.3 2.2-2.3 11 0 13.2"/>',
  sector: '<rect x="3" y="3" width="14" height="14" rx="3.4"/><path d="M10 3v14M3 10h14"/>',
  spiral: '<path d="M10 10.2c0-.9.9-1.4 1.6-1 1.2.7 1 2.7-.4 3.2-2 .7-3.8-1.1-3.4-3.1.5-2.7 3.6-3.9 6-2.6 3 1.7 3.2 6 .7 8.1"/><path d="M8.2 4.1C5.3 5 3.4 7.9 3.8 11"/>',
  chev: '<path d="M7.5 4.8L12.7 10l-5.2 5.2"/>',
  back: '<path d="M12.5 4.8L7.3 10l5.2 5.2"/>',
  ring: '<circle cx="10" cy="10" r="6.5"/>',
};
const ico = (n, c) => `<svg class="ico${c ? ' ' + c : ''}" viewBox="0 0 20 20">${ICON[n]}</svg>`;

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
  const $ = s => document.querySelector(s);
  const L = window.HOLLOW_LOCAL, G = window.HOLLOW_GALAXY, M = window.HOLLOW_MOCK;
  if (!L || !G || !M) throw new Error('data files missing (../data/*.js)');
  const SYS = L.systems, N = SYS.name.length;
  const V3 = THREE.Vector3;

  // ---------- small helpers ----------
  const clamp = (x, a, b) => x < a ? a : x > b ? b : x;
  const lerp = (a, b, t) => a + (b - a) * t;
  const smooth = (e0, e1, x) => { const t = clamp((x - e0) / (e1 - e0), 0, 1); return t * t * (3 - 2 * t); };
  const easeInOut = t => t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2;
  const easeOut = t => 1 - Math.pow(1 - t, 3);
  const log10 = Math.log10;
  const fmt = (v, d = 0) => v.toLocaleString('en-US', { minimumFractionDigits: d, maximumFractionDigits: d });
  const fmtLy = v => (v < 10 ? fmt(v, 2) : v < 100 ? fmt(v, 1) : fmt(v)) + ' ly';
  const esc = s => String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
  const hashStr = s => { let h = 2166136261; for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619); } return h >>> 0; };
  const mulberry = seed => () => { seed |= 0; seed = seed + 0x6D2B79F5 | 0; let t = Math.imul(seed ^ seed >>> 15, 1 | seed); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296; };
  const bytes = b64 => { const s = atob(b64); const u = new Uint8Array(s.length); for (let i = 0; i < s.length; i++) u[i] = s.charCodeAt(i); return u; };
  const hexRGB = h => { const c = parseInt(h.slice(1), 16); return [(c >> 16 & 255) / 255, (c >> 8 & 255) / 255, (c & 255) / 255]; };
  const angDiff = (a, b) => { let d = b - a; while (d > Math.PI) d -= 2 * Math.PI; while (d < -Math.PI) d += 2 * Math.PI; return d; };
  const params = new URLSearchParams((location.hash || '').replace(/^#/, '') || (location.search || '').replace(/^\?/, ''));
  const INSTANT = params.has('instant') || params.toString().length > 0;

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
  const stationsBy = new Map();
  for (const st of M.stations) { if (!stationsBy.has(st.sys)) stationsBy.set(st.sys, []); stationsBy.get(st.sys).push(st); FL[st.sys] |= F.STATION; }
  FL[M.home.sys] |= F.HOME;
  const playersBy = new Map();
  for (const p of M.players) { playersBy.set(p.sys, p); FL[p.sys] |= F.PLAYER; }
  const dangerOf = new Int8Array(N).fill(-1);
  M.danger.forEach((d, k) => {
    const r2 = d.radiusLy * d.radiusLy;
    for (let i = 0; i < N; i++) {
      const dx = PX[i] - d.centre[0], dy = PY[i] - d.centre[1], dz = PZ[i] - d.centre[2];
      if (dx * dx + dy * dy + dz * dz <= r2) { dangerOf[i] = k; FL[i] |= F.DANGER | (d.kind === 'hazard' ? F.STORM : F.RAID); }
    }
  });
  const byName = new Map(); for (let i = 0; i < N; i++) byName.set(SYS.name[i].toLowerCase(), i);
  const SECTORS = G.sectors.list; // [name, sx, sy, sz, cx, cy, cz]
  const sectorAt = new Map(); SECTORS.forEach((s, k) => sectorAt.set(s[1] + ',' + s[2] + ',' + s[3], k));
  const HOME_SECTOR = SECTORS[0][0];
  const sectorOfPos = (x, y, z) => {
    const sx = 64 + Math.floor((x + 512) / 1024), sy = 64 + Math.floor((y + 512) / 1024), sz = 38 + Math.floor((z + 512) / 1024);
    const k = sectorAt.get(sx + ',' + sy + ',' + sz);
    return k === undefined ? -1 : k;
  };
  const shortName = i => { const n = SYS.name[i]; return (!SYS.cat[i] && n.startsWith(HOME_SECTOR + ' ')) ? n.slice(HOME_SECTOR.length + 1) : n; };
  const resNames = M.resources.names;
  const resOfBody = new Map(); for (const r of M.resources.bodies) resOfBody.set(r[0] + ':' + r[1], r[2]);
  const maskNames = m => resNames.filter((_, k) => m & (1 << k));
  const GC = new V3(G.meta.galacticCentre[0], G.meta.galacticCentre[1], G.meta.galacticCentre[2]);

  const ship = {
    name: M.ship.name, sys: M.ship.sys, fuel: M.ship.fuel, max: M.ship.fuelJumps, range: M.ship.jumpRangeLy,
    pos: new V3(PX[M.ship.sys], PY[M.ship.sys], PZ[M.ship.sys]),
  };
  const starPos = i => new V3(PX[i], PY[i], PZ[i]);
  const distShip = i => Math.hypot(PX[i] - ship.pos.x, PY[i] - ship.pos.y, PZ[i] - ship.pos.z);

  // ---------- spatial grid (12.5 ly cells) ----------
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

  // ---------- route planner: A* over (system, fuel) ----------
  let NB_S = null, NB_L = null;
  function buildNeighbours() {
    if (NB_S) return;
    NB_S = new Int32Array(N + 1); const tmp = [];
    for (let i = 0; i < N; i++) { NB_S[i] = tmp.length; eachNear(PX[i], PY[i], PZ[i], ship.range, j => { if (j !== i) tmp.push(j); }); }
    NB_S[N] = tmp.length; NB_L = Int32Array.from(tmp);
  }
  const refuelAt = i => (FL[i] & F.STATION) || ((FL[i] & F.SCOOP) && !(FL[i] & F.STORM));
  function planRoute(from, to) {
    const tStart = performance.now();
    buildNeighbours();
    const R = ship.range, FMAX = ship.max, S8 = 8;
    const g = new Float64Array(N * S8).fill(Infinity), prev = new Int32Array(N * S8).fill(-1), closed = new Uint8Array(N * S8);
    const hk = [], hp = [];
    const push = (k, p) => { let i = hk.length; hk.push(k); hp.push(p); while (i > 0) { const j = (i - 1) >> 1; if (hp[j] <= p) break; hk[i] = hk[j]; hp[i] = hp[j]; i = j; } hk[i] = k; hp[i] = p; };
    const pop = () => { const top = hk[0], lk = hk.pop(), lp = hp.pop(), n = hk.length; if (n) { let i = 0; for (;;) { const l = 2 * i + 1; if (l >= n) break; const r = l + 1, m = (r < n && hp[r] < hp[l]) ? r : l; if (hp[m] >= lp) break; hk[i] = hk[m]; hp[i] = hp[m]; i = m; } hk[i] = lk; hp[i] = lp; } return top; };
    const tx = PX[to], ty = PY[to], tz = PZ[to];
    const h = i => { const d = Math.hypot(PX[i] - tx, PY[i] - ty, PZ[i] - tz); return Math.ceil(d / R - 1e-9) + d * 1e-4; };
    const s0 = from * S8 + clamp(ship.fuel, 0, FMAX);
    g[s0] = 0; push(s0, h(from));
    let goal = -1, iter = 0;
    while (hk.length) {
      const s = pop(); if (closed[s]) continue; closed[s] = 1;
      const u = (s / S8) | 0, f = s % S8;
      if (u === to) { goal = s; break; }
      if (++iter > 600000) break;
      const stormU = FL[u] & F.STORM;
      for (let k = NB_S[u]; k < NB_S[u + 1]; k++) {
        const v = NB_L[k];
        const cost = (stormU || (FL[v] & F.STORM)) ? 2 : 1;
        if (f < cost) continue;
        const nf = refuelAt(v) ? FMAX : f - cost;
        const d = Math.hypot(PX[v] - PX[u], PY[v] - PY[u], PZ[v] - PZ[u]);
        const c = 1 + d * 1e-4 + ((FL[v] & F.RAID) && v !== to ? 3 : 0);
        const ns = v * S8 + nf, ng = g[s] + c;
        if (ng < g[ns]) { g[ns] = ng; prev[ns] = s; push(ns, ng + h(v)); }
      }
    }
    if (goal < 0) return null;
    const path = []; for (let s = goal; s >= 0; s = prev[s]) path.push((s / S8) | 0); path.reverse();
    // Fuel plan along the path: refuel only where the tank would not reach the next refuel point.
    const stops = path.map((sys, j) => ({ sys, jump: j ? Math.hypot(PX[sys] - PX[path[j - 1]], PY[sys] - PY[path[j - 1]], PZ[sys] - PZ[path[j - 1]]) : 0, cost: 0, refuel: false, fuel: 0 }));
    for (let j = 1; j < stops.length; j++) stops[j].cost = ((FL[stops[j - 1].sys] & F.STORM) || (FL[stops[j].sys] & F.STORM)) ? 2 : 1;
    let fuel = ship.fuel; stops[0].fuel = fuel; let refuels = 0;
    for (let j = 1; j < stops.length; j++) {
      fuel -= stops[j].cost;
      if (j < stops.length - 1 && refuelAt(stops[j].sys)) {
        let need = 0;
        for (let q = j + 1; q < stops.length; q++) { need += stops[q].cost; if (refuelAt(stops[q].sys) || q === stops.length - 1) break; }
        if (fuel < need) { fuel = FMAX; stops[j].refuel = true; refuels++; }
      }
      stops[j].fuel = Math.max(0, fuel);
    }
    const total = stops.reduce((a, s) => a + s.jump, 0);
    const avoided = M.danger.filter(d => d.kind === 'hostile').filter(d => {
      const c = d.centre, dd = (i) => Math.hypot(PX[i] - c[0], PY[i] - c[1], PZ[i] - c[2]);
      const straight = segDist(PX[from], PY[from], PZ[from], tx, ty, tz, c[0], c[1], c[2]);
      return straight < d.radiusLy && !path.some(i => dd(i) <= d.radiusLy);
    });
    return { from, to, stops, jumps: stops.length - 1, total, refuels, avoided, ms: performance.now() - tStart, straight: Math.hypot(PX[from] - tx, PY[from] - ty, PZ[from] - tz) };
  }
  function segDist(ax, ay, az, bx, by, bz, px, py, pz) {
    const vx = bx - ax, vy = by - ay, vz = bz - az, wx = px - ax, wy = py - ay, wz = pz - az;
    const t = clamp((vx * wx + vy * wy + vz * wz) / Math.max(1e-9, vx * vx + vy * vy + vz * vz), 0, 1);
    return Math.hypot(ax + vx * t - px, ay + vy * t - py, az + vz * t - pz);
  }

  // ---------- filters ----------
  let reach = new Set();
  function updateReach() { reach = new Set(); eachNear(ship.pos.x, ship.pos.y, ship.pos.z, ship.range, i => { if (i !== ship.sys) reach.add(i); }); }
  updateReach();
  const FILTERS = [
    { id: 'reach', name: 'Reachable', color: '#6bccff', test: i => reach.has(i), rare: true, help: 'In jump range now' },
    { id: 'fuel', name: 'Fuel', color: '#ffbf6e', test: i => refuelAt(i), help: 'Scoopable star or station' },
    { id: 'stations', name: 'Stations', color: '#c6a3ff', test: i => FL[i] & F.STATION, rare: true, mock: true },
    { id: 'land', name: 'Landable', color: '#8fe39c', test: i => FL[i] & F.LAND },
    { id: 'earth', name: 'Earth-like', color: '#3fd8c6', test: i => FL[i] & F.EARTH, rare: true },
    { id: 'bases', name: 'Bases', color: '#ff9ccf', test: i => FL[i] & (F.HOME | F.PLAYER), rare: true, mock: true },
    { id: 'danger', name: 'Danger', color: '#ff6f6f', test: i => FL[i] & F.DANGER, rare: true, mock: true },
  ];
  const FILTER_PRIORITY = ['danger', 'bases', 'stations', 'earth', 'reach', 'land', 'fuel'];
  const activeFilters = new Set();
  const filterCount = id => { const f = FILTERS.find(q => q.id === id); let c = 0; for (let i = 0; i < N; i++) if (f.test(i)) c++; return c; };
  const counts = {}; for (const f of FILTERS) counts[f.id] = filterCount(f.id);
  const filterOf = new Int8Array(N).fill(-1); // index into FILTERS of the colour a star takes, -1 none, -2 dimmed
  let favs = new Set();
  try { const raw = localStorage.getItem('hollow.mapB.favs'); if (raw) favs = new Set(JSON.parse(raw).map(a => SYS.addr.indexOf(a)).filter(i => i >= 0)); } catch (e) { /* storage unavailable */ }
  const saveFavs = () => { try { localStorage.setItem('hollow.mapB.favs', JSON.stringify([...favs].map(i => SYS.addr[i]))); } catch (e) { /* ignore */ } };

  // ---------- renderer ----------
  const canvas = $('#gl');
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, powerPreference: 'high-performance' });
  const DPR = Math.min(window.devicePixelRatio || 1, 2);
  renderer.setPixelRatio(DPR);
  renderer.setClearColor(0x03050a, 1);
  renderer.autoClear = false;
  const scene = new THREE.Scene();
  const camera = new THREE.PerspectiveCamera(42, 1, 0.1, 1e6);
  let W = 1, H = 1;

  // Shared point shader for the galaxy sample and the regional star haze.
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
      vCol = mix(aColor, vec3(0.60, 0.76, 1.0), aYoung * 0.55);
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
  const pointMat = (u) => new THREE.ShaderMaterial({
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
  const galaxyPts = new THREE.Points(gGeo, pointMat({ size: 1.5, old: 0.16, young: 0.5, gamma: 5.0 }));
  galaxyPts.frustumCulled = false; galaxyPts.renderOrder = 1; scene.add(galaxyPts);

  // Face-on glow of the density model: the soft body of the disk.
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
    transparent: true, depthWrite: false, depthTest: false, blending: THREE.AdditiveBlending,
  });
  const glowPlane = new THREE.Mesh(new THREE.PlaneGeometry(GN * GCELL, GN * GCELL), glowMat);
  glowPlane.rotation.x = -Math.PI / 2;
  glowPlane.position.set(G.glow.x0 + GN * GCELL / 2, -45, G.glow.z0 + GN * GCELL / 2);
  glowPlane.renderOrder = 0; glowPlane.frustumCulled = false; scene.add(glowPlane);

  // Regional star haze around Sol: points drawn in proportion to the density model (a picture of density, not stars).
  const haze = (() => {
    const rnd = mulberry(0x48ab17), HALF = 9600, STEP = 120, n = Math.round(2 * HALF / STEP);
    const build = (arr, meta, pow) => {
      const w = new Float64Array(n * n); let tot = 0;
      for (let b = 0; b < n; b++) for (let a = 0; a < n; a++) {
        const x = -HALF + (a + 0.5) * STEP, z = -HALF + (b + 0.5) * STEP;
        const v = Math.pow(glowAt(arr, meta, x, z), pow); tot += v; w[b * n + a] = tot;
      }
      return { w, tot };
    };
    const A = build(glowAll, G.glow.all, 1.6), Y = build(glowYoung, G.glow.young, 1.35);
    const COUNT = 70000, YOUNG = 0.3;
    const pos = new Float32Array(COUNT * 3), col = new Uint8Array(COUNT * 3), br = new Uint8Array(COUNT), yg = new Uint8Array(COUNT);
    const gauss = () => { let u = 0, v = 0; while (u === 0) u = rnd(); v = rnd(); return Math.sqrt(-2 * Math.log(u)) * Math.cos(2 * Math.PI * v); };
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

  // Sectors: 1,024-ly cubes; the layer through Sol drawn as soft named areas on the midplane.
  const SECTOR_Y = -68;
  const sectorMat = new THREE.ShaderMaterial({
    uniforms: { uOpacity: { value: 0 }, uHover: { value: new THREE.Vector2(999, 999) }, uCur: { value: new THREE.Vector2(0, 0) }, uSel: { value: new THREE.Vector2(999, 999) } },
    vertexShader: 'varying vec2 vXZ; void main(){ vec4 w = modelMatrix * vec4(position,1.0); vXZ = w.xz; gl_Position = projectionMatrix * viewMatrix * w; }',
    fragmentShader: `
      uniform float uOpacity; uniform vec2 uHover, uCur, uSel; varying vec2 vXZ;
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
        float a = border * (0.16 + 0.5 * hi) + fill * (0.018 + 0.07 * hi);
        vec3 c = mix(vec3(0.72, 0.82, 1.0), vec3(0.42, 0.80, 1.0), hi);
        gl_FragColor = vec4(c, a * fade * uOpacity);
      }`,
    transparent: true, depthWrite: false, depthTest: false,
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
      uniforms: { uOpacity: { value: 0 }, uColor: { value: new THREE.Color(0.40, 0.62, 1.0) } },
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

  // Local stars: all 37,409 systems within 150 ly, as crisp round icons sized by apparent brightness.
  const lPos = new Float32Array(N * 3); for (let i = 0; i < N; i++) { lPos[3 * i] = PX[i]; lPos[3 * i + 1] = PY[i]; lPos[3 * i + 2] = PZ[i]; }
  const lState = new Float32Array(N * 4);
  const lGeo = new THREE.BufferGeometry();
  lGeo.setAttribute('position', new THREE.BufferAttribute(lPos, 3));
  lGeo.setAttribute('aColor', new THREE.BufferAttribute(COL, 3));
  lGeo.setAttribute('aMag', new THREE.BufferAttribute(MAG, 1));
  const lStateAttr = new THREE.BufferAttribute(lState, 4); lGeo.setAttribute('aState', lStateAttr);
  const localMat = new THREE.ShaderMaterial({
    uniforms: { uOpacity: { value: 1 }, uPx: { value: DPR }, uTarget: { value: new V3() }, uFogNear: { value: 100 }, uFogFar: { value: 200 }, uBoost: { value: 0 } },
    vertexShader: `
      attribute vec3 aColor; attribute float aMag; attribute vec4 aState;
      uniform float uOpacity, uPx, uFogNear, uFogFar, uBoost; uniform vec3 uTarget;
      varying vec3 vCol; varying float vA; varying float vS;
      void main() {
        vec4 mv = modelViewMatrix * vec4(position, 1.0);
        gl_Position = projectionMatrix * mv;
        float d = max(length(mv.xyz), 0.05);
        float app = aMag + 5.0 * log(d / 32.6) / log(10.0) - uBoost;
        float s = clamp(9.0 - app * 0.5, 2.6, 13.0);
        float a = clamp(1.3 - app * 0.07, 0.42, 1.0);
        float fog = 1.0 - smoothstep(uFogNear, uFogFar, distance(position, uTarget));
        vec3 c = aColor;
        if (aState.w > 1.5) { a *= 0.13; s = max(s * 0.7, 1.6); }
        else if (aState.w > 0.5) { c = aState.rgb; s = max(s, 5.0) + 1.5; a = 1.0; fog = max(fog, 0.6); }
        vCol = c; vA = a * fog * uOpacity; vS = s * uPx;
        gl_PointSize = vS;
      }`,
    fragmentShader: `
      varying vec3 vCol; varying float vA; varying float vS;
      void main() {
        float r = length(gl_PointCoord - 0.5) * 2.0;
        float aa = 2.2 / max(vS, 1.0);
        float core = 1.0 - smoothstep(0.40 - aa, 0.40 + aa, r);
        float halo = exp(-r * r * 6.5) * 0.42;
        float a = (core + halo) * vA;
        if (a < 0.004) discard;
        gl_FragColor = vec4(mix(vCol, vec3(1.0), core * 0.25), a);
      }`,
    transparent: true, depthWrite: false, depthTest: false, blending: THREE.AdditiveBlending,
  });
  const localPts = new THREE.Points(lGeo, localMat); localPts.frustumCulled = false; localPts.renderOrder = 4; scene.add(localPts);

  function applyFilters() {
    const act = FILTER_PRIORITY.filter(id => activeFilters.has(id)).map(id => FILTERS.find(f => f.id === id));
    for (let i = 0; i < N; i++) {
      let mode = 0, c = null, fi = -1;
      if (act.length) {
        mode = 2;
        for (const f of act) if (f.test(i)) { mode = 1; c = f.rgb || (f.rgb = hexRGB(f.color)); fi = FILTERS.indexOf(f); break; }
      }
      filterOf[i] = mode === 2 ? -2 : fi;
      lState[4 * i] = c ? c[0] : 0; lState[4 * i + 1] = c ? c[1] : 0; lState[4 * i + 2] = c ? c[2] : 0; lState[4 * i + 3] = mode;
    }
    lStateAttr.needsUpdate = true;
    dangerMeshes.forEach(d => { d.boost = activeFilters.has('danger') ? 1 : 0; });
    renderChips(); updateHash();
  }

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
        float a = (rings * 0.42 + axes * 0.3) * fade * uOpacity;
        gl_FragColor = vec4(0.66, 0.79, 1.0, a);
      }`,
    transparent: true, depthWrite: false, depthTest: false, side: THREE.DoubleSide,
  });
  const grid = new THREE.Mesh(new THREE.CircleGeometry(160, 160), gridMat);
  grid.rotation.x = -Math.PI / 2; grid.renderOrder = 2; grid.frustumCulled = false; scene.add(grid);

  // Fresnel shells: the jump-range bubble and the (mock) danger zones.
  const shellMat = (rgb, base, rim) => new THREE.ShaderMaterial({
    uniforms: { uColor: { value: new THREE.Color(rgb[0], rgb[1], rgb[2]) }, uOpacity: { value: 0 }, uBase: { value: base }, uRim: { value: rim } },
    vertexShader: 'varying vec3 vN; varying vec3 vV; void main(){ vec4 w = modelMatrix * vec4(position,1.0); vN = normalize(mat3(modelMatrix) * normal); vV = normalize(cameraPosition - w.xyz); gl_Position = projectionMatrix * viewMatrix * w; }',
    fragmentShader: 'uniform vec3 uColor; uniform float uOpacity, uBase, uRim; varying vec3 vN; varying vec3 vV; void main(){ float f = 1.0 - abs(dot(normalize(vN), normalize(vV))); float a = (pow(f, 3.0) * uRim + uBase) * uOpacity; gl_FragColor = vec4(uColor, a); }',
    transparent: true, depthWrite: false, depthTest: false, side: THREE.FrontSide, blending: THREE.AdditiveBlending,
  });
  const sphereGeo = new THREE.SphereGeometry(1, 64, 40);
  const bubble = new THREE.Mesh(sphereGeo, shellMat([0.42, 0.8, 1.0], 0.025, 0.5));
  bubble.scale.setScalar(ship.range); bubble.renderOrder = 3; scene.add(bubble);
  const circlePts = (r, n = 128) => { const a = []; for (let k = 0; k <= n; k++) { const t = k / n * Math.PI * 2; a.push(new V3(Math.cos(t) * r, 0, Math.sin(t) * r)); } return a; };
  const bubbleRing = new THREE.Line(new THREE.BufferGeometry().setFromPoints(circlePts(1)), new THREE.LineBasicMaterial({ color: 0x6bccff, transparent: true, opacity: 0, depthTest: false }));
  bubbleRing.scale.setScalar(ship.range); bubbleRing.renderOrder = 3; scene.add(bubbleRing);
  const dangerMeshes = M.danger.map(d => {
    const rgb = d.kind === 'hazard' ? [1.0, 0.68, 0.32] : [1.0, 0.36, 0.36];
    const m = new THREE.Mesh(sphereGeo, shellMat(rgb, 0.03, 0.42));
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
  const dropSel = mkDrop(0x6bccff, 0.55), dropHov = mkDrop(0xffffff, 0.35);
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

  // Route: a thick line with stop markers.
  let route = null, routeLine = null, routeStops = null;
  const stopMat = new THREE.ShaderMaterial({
    uniforms: { uPx: { value: DPR }, uOpacity: { value: 1 } },
    vertexShader: 'attribute float aKind; uniform float uPx; varying float vK; void main(){ vK = aKind; gl_Position = projectionMatrix * modelViewMatrix * vec4(position,1.0); gl_PointSize = (aKind > 1.5 && aKind < 2.5 ? 17.0 : 12.0) * uPx; }',
    fragmentShader: `
      uniform float uOpacity; varying float vK;
      void main() {
        float r = length(gl_PointCoord - 0.5) * 2.0;
        vec3 ring = (vK > 0.5 && vK < 1.5) ? vec3(1.0, 0.75, 0.43) : vec3(0.42, 0.80, 1.0);
        float outer = 1.0 - smoothstep(0.84, 0.98, r);
        float inner = 1.0 - smoothstep(0.50, 0.64, r);
        float dotc = 1.0 - smoothstep(0.22, 0.34, r);
        vec3 c = mix(ring, vec3(0.03, 0.07, 0.11), inner);
        c = mix(c, ring, dotc);
        if (vK > 1.5 && vK < 2.5) c = mix(ring, vec3(1.0), dotc);
        if (vK > 2.5) c = mix(ring * 0.5, vec3(0.03, 0.07, 0.11), inner);
        gl_FragColor = vec4(c, outer * uOpacity);
      }`,
    transparent: true, depthWrite: false, depthTest: false,
  });
  function drawRoute() {
    if (routeLine) { scene.remove(routeLine); routeLine.geometry.dispose(); routeLine.material.dispose(); routeLine = null; }
    if (routeStops) { scene.remove(routeStops); routeStops.geometry.dispose(); routeStops = null; }
    if (!route) return;
    const pts = []; for (const s of route.stops) pts.push(PX[s.sys], PY[s.sys], PZ[s.sys]);
    const geo = new LineGeometry(); geo.setPositions(pts);
    const mat = new LineMaterial({ color: 0x6bccff, linewidth: 2.6, transparent: true, opacity: 0.9, depthTest: false, depthWrite: false });
    mat.resolution.set(W, H);
    routeLine = new Line2(geo, mat); routeLine.computeLineDistances(); routeLine.renderOrder = 6; routeLine.frustumCulled = false; scene.add(routeLine);
    const sg = new THREE.BufferGeometry();
    sg.setAttribute('position', new THREE.Float32BufferAttribute(pts, 3));
    sg.setAttribute('aKind', new THREE.Float32BufferAttribute(route.stops.map((s, j) => j === route.stops.length - 1 ? 2 : s.refuel ? 1 : 0), 1));
    routeStops = new THREE.Points(sg, stopMat); routeStops.renderOrder = 7; routeStops.frustumCulled = false; scene.add(routeStops);
    refreshRouteKinds();
  }
  function refreshRouteKinds() {
    if (!routeStops || !route) return;
    const a = routeStops.geometry.attributes.aKind;
    route.stops.forEach((s, j) => { a.array[j] = j === route.stops.length - 1 ? 2 : (auto && j < auto.seg) || (route.done && j < route.stops.length - 1) ? 3 : s.refuel ? 1 : 0; });
    a.needsUpdate = true;
  }

  // ---------- System level: its own scene, units are display units with log-scaled orbits ----------
  const sysScene = new THREE.Scene();
  const sysCam = new THREE.PerspectiveCamera(36, 1, 0.1, 20000);
  sysScene.add(new THREE.AmbientLight(0xffffff, 0.16));
  const sysLight = new THREE.PointLight(0xffffff, 2.8, 0, 0); sysScene.add(sysLight);
  const veilScene = new THREE.Scene(), veilCam = new THREE.OrthographicCamera(-1, 1, 1, -1, 0, 1);
  const veilMat = new THREE.ShaderMaterial({ uniforms: { uA: { value: 0 } }, vertexShader: 'void main(){ gl_Position = vec4(position.xy, 0.0, 1.0); }', fragmentShader: 'uniform float uA; void main(){ gl_FragColor = vec4(0.012, 0.02, 0.04, uA); }', transparent: true, depthTest: false, depthWrite: false });
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
  const starKind = cls => {
    if (!cls) return 'Star';
    const c = cls[0], giant = /(^|[^I])III|II|Ia|Ib/.test(cls) && !/V/.test(cls.replace(/IV/, ''));
    if (c === 'D') return 'White dwarf'; if (c === 'N') return 'Neutron star'; if (c === 'L' || c === 'T' || c === 'Y') return 'Brown dwarf';
    if (c === 'W') return 'Wolf-Rayet star'; if (c === 'C') return 'Carbon star';
    const name = { O: 'Blue', B: 'Blue-white', A: 'White', F: 'Yellow-white', G: 'Yellow', K: 'Orange', M: 'Red' }[c] || '';
    return name + (giant ? ' giant' : c === 'O' || c === 'B' || c === 'A' ? ' star' : ' dwarf');
  };
  let SY = null;
  const sysFocal = () => H / (2 * Math.tan(sysCam.fov * Math.PI / 360));
  const sphere = new THREE.SphereGeometry(1, 40, 24);
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
  function buildSystem(i) {
    if (SY && SY.i === i) return SY;
    if (SY) { sysScene.remove(SY.root); SY.root.traverse(o => { if (o.material && o.material !== stopMat) o.material.dispose(); if (o.geometry && o.geometry !== sphere && o.geometry !== sphereGeo) o.geometry.dispose(); }); }
    let raw = L.bodies[String(i)], schematic = false;
    if (!raw) { raw = schematicBodies(i); schematic = true; }
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
    nodes.forEach(n => { n.theta = ang(n.name); });
    // satellites first, so we know how much room each orbiter needs
    nodes.forEach(n => {
      if (n === prim) return;
      const moons = n.kids.slice().sort((p, q) => p.a - q.a);
      moons.forEach((m, j) => { m.orbit = n.size + 1.7 + j * 1.3; });
      n.ext = moons.length ? moons[moons.length - 1].orbit + 0.8 : n.size + (n.rings ? n.size * 1.3 : 0);
    });
    const orb = prim.kids.slice().sort((p, q) => p.a - q.a);
    const as = orb.map(n => Math.max(n.a, 1e-4));
    const l0 = as.length ? log10(Math.min(...as)) : 0, l1 = as.length ? log10(Math.max(...as)) : 1;
    let prevR = prim.size + 8, prevExt = 0;
    orb.forEach((n, j) => {
      const u = l1 > l0 ? (log10(Math.max(n.a, 1e-4)) - l0) / (l1 - l0) : 0;
      let r = 20 + u * 112;
      if (n.kind === 3) n.ext = 3;
      r = Math.max(r, j === 0 ? prim.size + 10 + n.ext : prevR + prevExt + n.ext + 3.2);
      n.orbit = r; prevR = r; prevExt = n.ext;
    });
    const place = (n, cx, cz) => { n.pos.set(cx + Math.cos(n.theta) * n.orbit, 0, cz + Math.sin(n.theta) * n.orbit); n.kids.forEach(m => place(m, n.pos.x, n.pos.z)); };
    orb.forEach(n => place(n, 0, 0));
    const maxR = orb.length ? Math.max(...orb.map(n => n.orbit + n.ext)) : 30;
    const orbitLine = (r, cx, cz, op) => {
      const l = new THREE.LineLoop(new THREE.BufferGeometry().setFromPoints(circlePts(r, r < 12 ? 72 : 360).slice(0, -1)),
        new THREE.LineBasicMaterial({ color: 0xbfd6ff, transparent: true, opacity: op }));
      l.position.set(cx, 0, cz); l.userData.baseOp = op; root.add(l); return l;
    };
    const starCol = new THREE.Color(SYS.col[i]);
    nodes.forEach(n => {
      if (n.kind === 0) {
        const c = n === prim ? starCol : new THREE.Color(classCol(n.type));
        const core = new THREE.Mesh(sphere, new THREE.MeshBasicMaterial({ color: c.clone().lerp(new THREE.Color(1, 1, 1), 0.45), transparent: true }));
        core.scale.setScalar(n.size); core.position.copy(n.pos); root.add(core);
        const glow = new THREE.Sprite(new THREE.SpriteMaterial({ map: radialTex, color: c, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending }));
        glow.scale.setScalar(n.size * (n === prim ? 11 : 8)); glow.position.copy(n.pos); glow.userData.baseOp = 1; root.add(glow);
        n.mesh = core;
        if (n !== prim) n.orbitLine = orbitLine(n.orbit, 0, 0, 0.12);
      } else if (n.kind === 3) {
        const cnt = 900, p = new Float32Array(cnt * 3), rnd = mulberry(hashStr(n.name));
        for (let k = 0; k < cnt; k++) { const t = rnd() * Math.PI * 2, r = n.orbit + (rnd() - 0.5) * 4.2; p[3 * k] = Math.cos(t) * r; p[3 * k + 1] = (rnd() - 0.5) * 0.7; p[3 * k + 2] = Math.sin(t) * r; }
        const g = new THREE.BufferGeometry(); g.setAttribute('position', new THREE.BufferAttribute(p, 3));
        const pts = new THREE.Points(g, new THREE.PointsMaterial({ color: 0xa49a8e, size: 1.6, sizeAttenuation: false, transparent: true, opacity: 0.55, depthWrite: false }));
        pts.userData.baseOp = 0.55; root.add(pts); n.mesh = null;
      } else {
        const mat = new THREE.MeshStandardMaterial({ color: BODY_COL[n.type] || 0xa0a0a0, roughness: 0.88, metalness: 0, transparent: true });
        const m = new THREE.Mesh(sphere, mat); m.scale.setScalar(n.size); m.position.copy(n.pos); root.add(m); n.mesh = m;
        if (n.rings) {
          const rg = new THREE.Mesh(new THREE.RingGeometry(n.size * 1.45, n.size * 2.25, 72), new THREE.MeshBasicMaterial({ color: 0xd9ccb4, transparent: true, opacity: 0.42, side: THREE.DoubleSide, depthWrite: false }));
          rg.rotation.x = -Math.PI / 2 + 0.42; rg.position.copy(n.pos); rg.userData.baseOp = 0.42; root.add(rg);
        }
        const par = nodes[n.parent];
        n.orbitLine = orbitLine(n.orbit, par && par !== prim ? par.pos.x : 0, par && par !== prim ? par.pos.z : 0, n.kind === 2 ? 0.07 : 0.15);
      }
    });
    // markers: home base, stations, other players (mock)
    const homeK = M.home.sys === i ? M.home.bodyIndex : -1;
    const sts = stationsBy.get(i) || [], pl = playersBy.get(i);
    nodes.forEach(n => {
      n.home = n.k === homeK; n.station = sts.find(s => s.bodyIndex === n.k) || null; n.player = pl && pl.bodyIndex === n.k ? pl : null;
      n.res = resOfBody.get(i + ':' + n.k) || 0;
    });
    sysScene.add(root);
    SY = { i, nodes, root, maxR, schematic, prim };
    return SY;
  }

  // ---------- camera ----------
  const LOG = { galaxy: log10(98000), region: log10(5200), nbr: log10(100), min: log10(2.2), max: log10(180000) };
  const B_GAL = log10(20000), B_REG = log10(700);
  const cam = {
    cur: { yaw: 0.0, pitch: 0.62, ld: LOG.galaxy, a: ship.pos.clone() },
    goal: { yaw: 0.0, pitch: 0.62, ld: LOG.galaxy, a: ship.pos.clone() },
  };
  const scam = { cur: { yaw: 0, pitch: 0.55, ld: 2.4, a: new V3() }, goal: { yaw: 0, pitch: 0.55, ld: 2.4, a: new V3() }, min: 1.2, max: 3 };
  const target = new V3();
  let tween = null, stween = null, topView = false, savedPitch = 0.62;
  let mode = 'map', sysBlend = 0, sysGoal = 0, sysT0 = 0, sysFrom = 0;
  const PITCH_MIN = 0.16, PITCH_MAX = 1.5533;
  function placeCamera() {
    const c = cam.cur, d = Math.pow(10, c.ld), t = smooth(4.05, 4.78, c.ld);
    target.copy(c.a).lerp(GC, t);
    const cp = Math.cos(c.pitch), sp = Math.sin(c.pitch);
    camera.position.set(target.x - Math.sin(c.yaw) * cp * d, target.y + sp * d, target.z - Math.cos(c.yaw) * cp * d);
    camera.near = Math.max(0.02, d * 0.002); camera.far = d * 6 + 300000;
    applyOffset(camera); camera.lookAt(target); camera.updateMatrixWorld();
  }
  // When the inspector is open, the view's centre shifts into the free area (no distortion: the frustum widens instead).
  let viewShift = 0;
  function applyOffset(cm) {
    if (viewShift < 0.5) { cm.aspect = W / H; if (cm.view && cm.view.enabled) cm.clearViewOffset(); else cm.updateProjectionMatrix(); return; }
    cm.aspect = (W + 2 * viewShift) / H;
    cm.setViewOffset(W + 2 * viewShift, H, 2 * viewShift, 0, W, H);
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
  function flyTo(to, dur = 1.2, bump = 0, done) { tween = makeTween(cam, to, dur, bump, done); if (INSTANT_NOW) { stepTween(tween, Infinity); tween = null; snapCam(cam); } }
  function sysFlyTo(to, dur = 0.9, done) { stween = makeTween(scam, to, dur, 0, done); if (INSTANT_NOW) { stepTween(stween, Infinity); stween = null; snapCam(scam); } }
  function stepTween(tw, now) {
    const p = clamp((now - tw.t0) / tw.dur, 0, 1), e = easeInOut(p), c = tw.c.goal;
    c.yaw = tw.from.yaw + angDiff(tw.from.yaw, tw.to.yaw) * e;
    c.pitch = lerp(tw.from.pitch, tw.to.pitch, e);
    c.ld = lerp(tw.from.ld, tw.to.ld, e) + tw.bump * Math.sin(Math.PI * p);
    c.a.lerpVectors(tw.from.a, tw.to.a, e);
    if (p >= 1 && tw.done) { const d = tw.done; tw.done = null; d(); }
    return p >= 1;
  }
  let INSTANT_NOW = false;

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
    if (k === 2) flyTo({ ld: LOG.nbr, pitch: topView ? PITCH_MAX : Math.max(cam.goal.pitch, 0.5), a: (sel && sel.t === 'star' && cam.goal.ld > B_REG) ? cam.goal.a.clone() : a }, 1.3);
  }
  function enterSystem(i, opts = {}) {
    if (i < 0) return;
    hideHint();
    buildSystem(i);
    const was = mode; mode = 'system'; sysFrom = sysBlend; sysGoal = 1; sysT0 = performance.now();
    if (was !== 'system') {
      flyTo({ a: starPos(i), ld: log10(0.9) }, 1.0);
      scam.goal.yaw = scam.cur.yaw = cam.goal.yaw; scam.goal.pitch = scam.cur.pitch = 0.56;
    }
    sysSel = opts.body ?? -1;
    scam.min = log10(Math.max(14, SY.maxR * 0.18)); scam.max = log10(SY.maxR * 4.2);
    scam.goal.a.set(0, 0, 0); scam.goal.ld = log10(SY.maxR * 2.25); scam.cur.ld = scam.goal.ld + 0.35; scam.cur.a.set(0, 0, 0);
    if (sysSel >= 0) focusBody(sysSel, true);
    if (INSTANT_NOW) { sysBlend = 1; snapCam(scam); }
    select({ t: 'star', i }, { keepCam: true });
  }
  function exitSystem(silent) {
    if (mode !== 'system') return;
    mode = 'map'; sysFrom = sysBlend; sysGoal = 0; sysT0 = performance.now(); sysSel = -1;
    if (!silent) flyTo({ ld: log10(26), a: starPos(SY.i) }, 1.0);
    if (INSTANT_NOW) sysBlend = 0;
    renderInspector();
  }

  // ---------- selection & hover ----------
  let sel = null, hov = null, sysSel = -1, sysHov = -1, targetSys = -1;
  function select(obj, opts = {}) {
    sel = obj;
    if (obj && obj.t === 'sector') { const s = SECTORS[obj.k]; sectorMat.uniforms.uSel.value.set(s[1] - 64, s[3] - 38); }
    else sectorMat.uniforms.uSel.value.set(999, 999);
    renderInspector(true); updateHash();
    if (!obj || opts.keepCam) return;
    if (obj.t === 'sector' && cam.goal.ld > B_REG) { const s = SECTORS[obj.k]; flyTo({ a: new V3(s[4], 0, s[6]) }, 1.0); }
  }
  function flyToStar(i, ld) {
    hideHint();
    if (mode === 'system') exitSystem(true);
    const d = ld ?? Math.min(Math.max(cam.goal.ld, log10(18)), log10(60));
    flyTo({ a: starPos(i), ld: d, pitch: topView ? PITCH_MAX : clamp(cam.goal.pitch, 0.35, 1.2) }, cam.goal.ld > B_REG ? 1.6 : 1.1);
  }

  // ---------- UI: static pieces ----------
  const ui = {
    crumbs: $('#crumbs'), levelname: $('#levelname'), levelsub: $('#levelsub'), rail: $('#rail'), thumb: $('.rail-thumb'), railFill: $('.rail-fill'),
    stops: [...document.querySelectorAll('.rail-stop')], chips: $('#chips'), insp: $('#insp-scroll'), hc: $('#hovercard'), toast: $('#toast'),
    ship: $('#shipcard'), search: $('#searchinput'), results: $('#results'), hint: $('#hint'), scalebar: $('#scalebar'), scaletxt: $('#scaletxt'),
    top: $('#btn-top'), me: $('#btn-me'), zin: $('#btn-in'), zout: $('#btn-out'), labels: $('#labels'), markers: $('#markers'), stage: $('#stage'),
  };
  ui.stops.forEach(b => b.addEventListener('click', () => goLevel(+b.dataset.level)));
  ui.top.addEventListener('click', () => toggleTop());
  ui.me.addEventListener('click', () => centreOnMe());
  ui.ship.addEventListener('click', () => centreOnMe());
  ui.zin.addEventListener('click', () => zoomBy(-0.35));
  ui.zout.addEventListener('click', () => zoomBy(0.35));
  let toastTimer = 0;
  function toast(msg, ms = 2400) { ui.toast.innerHTML = msg; ui.toast.classList.add('show'); clearTimeout(toastTimer); toastTimer = setTimeout(() => ui.toast.classList.remove('show'), ms); }
  let hintHidden = false;
  function hideHint() { if (!hintHidden) { hintHidden = true; ui.hint.classList.add('hide'); } }
  setTimeout(hideHint, 14000);

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
    if (mode === 'system' && sysBlend > 0.5) { scam.goal.ld = clamp(scam.goal.ld + dl, scam.min, scam.max + 0.4); if (scam.goal.ld > scam.max + 0.3) exitSystem(); return; }
    tween = null;
    cam.goal.ld = clamp(cam.goal.ld + dl, LOG.min, LOG.max);
    afterZoom(dl);
  }
  function afterZoom(dl) {
    // Zooming in to the closest step over a star opens its system; zooming in from far away returns to your neighbourhood.
    if (dl < 0 && cam.goal.ld <= LOG.min + 0.001 && mode === 'map') {
      const i = nearestStar(cam.goal.a, 4);
      if (i >= 0) { enterSystem(i); return; }
    }
    if (dl < 0 && cam.goal.ld < B_REG + 0.25 && cam.goal.a.length() > 170) flyTo({ a: ship.pos.clone() }, 1.2);
  }

  // Chips
  function renderChips() {
    ui.chips.innerHTML = FILTERS.map((f, k) => (k === 1 || k === 5 ? '<span class="chip-sep"></span>' : '') +
      `<button class="chip${activeFilters.has(f.id) ? ' on' : ''}" data-f="${f.id}" style="--cc:${f.color}" title="${esc(f.help || f.name)}${f.mock ? ' (mock data)' : ''}"><i></i><span class="lbltxt">${f.name}</span><span class="cnt">${fmt(counts[f.id])}</span></button>`).join('');
  }
  ui.chips.addEventListener('click', e => {
    const b = e.target.closest('.chip'); if (!b) return;
    const id = b.dataset.f; if (activeFilters.has(id)) activeFilters.delete(id); else activeFilters.add(id);
    applyFilters();
    if (activeFilters.has(id)) { const n = counts[id]; toast(`<b>${fmt(n)}</b>&nbsp; ${FILTERS.find(f => f.id === id).name.toLowerCase()} system${n === 1 ? '' : 's'} highlighted`, 1600); }
  });

  // Ship card
  function renderShip() {
    const pips = Array.from({ length: ship.max }, (_, k) => `<i class="${k < ship.fuel ? 'on' : ''}"></i>`).join('');
    const tgt = targetSys >= 0 ? ` · <span style="color:var(--warm)">→ ${esc(shortName(targetSys))}</span>` : '';
    ui.ship.innerHTML = `<span class="sc-dot"></span><span class="sc-col"><span class="sc-name">${esc(ship.name)} <span style="color:var(--text-3);font-weight:500">· ${esc(shortName(ship.sys))}</span></span><span class="sc-sub">Jump ${fmt(ship.range, 1)} ly${tgt}</span></span><span class="fuel" title="Fuel: ${ship.fuel} of ${ship.max} jumps">${pips}</span>`;
  }

  // ---------- Inspector ----------
  const tag = (txt, cls, c) => `<span class="tag ${cls || ''}" ${c ? `style="--tc:${c}"` : ''}><i></i>${txt}</span>`;
  const fact = (k, v) => `<div><span>${k}</span><b>${v}</b></div>`;
  const mockChip = '<span class="mock-chip">mock</span>';
  const tempC = k => `${fmt(Math.round(k - 273.15))} °C`;
  function jumpsEstimate(i) {
    const d = Math.hypot(PX[i] - ship.pos.x, PY[i] - ship.pos.y, PZ[i] - ship.pos.z);
    if (i === ship.sys) return 'You are here';
    if (d <= ship.range) return '1 <small>in range</small>';
    return `≈ ${Math.ceil(d / ship.range)} <small>or more</small>`;
  }
  function starHTML(i) {
    const s = SYS.sum[i], d = distShip(i), st = stationsBy.get(i) || [], pl = playersBy.get(i), dz = dangerOf[i] >= 0 ? M.danger[dangerOf[i]] : null;
    const isHome = M.home.sys === i, isShip = ship.sys === i, fav = favs.has(i), inSys = mode === 'system' && SY && SY.i === i;
    const tags = [];
    if (isShip) tags.push(tag('You are here', 'acc'));
    if (isHome) tags.push(tag('Home base', '', '#ff9ccf'));
    if (reach.has(i)) tags.push(tag('In jump range', 'acc'));
    if (refuelAt(i)) tags.push(tag(FL[i] & F.STATION && !(FL[i] & F.SCOOP) ? 'Fuel at station' : 'Scoopable', '', '#ffbf6e'));
    if (st.length) tags.push(tag(st.length > 1 ? `${st.length} stations` : 'Station', '', '#c6a3ff'));
    if (s[6] > 0) tags.push(tag('Earth-like', '', '#3fd8c6'));
    if (dz) tags.push(tag(dz.name, 'warn'));
    if (SYS.cat[i]) tags.push(tag('Catalogue star', ''));
    const sector = SECTORS[sectorOfPos(PX[i], PY[i], PZ[i])];
    let h = `<div class="ih"><div class="ih-orb" style="--c:${SYS.col[i]}"></div><div class="ih-t"><div class="eyebrow">${inSys ? 'Star system' : 'Star'} · ${esc(sector ? sector[0] : 'Uncharted')}</div>
      <h2>${esc(SYS.name[i])}</h2><div class="sub">${esc(SYS.cls[i])} · ${starKind(SYS.cls[i])} · ${fmt(SYS.teff[i])} K</div></div>
      <button class="x" data-act="close" title="Close (Esc)">${ico('close')}</button></div>
      <div class="tags">${tags.join('')}</div>
      <div class="actions">
        <button class="act primary" data-act="route" title="Plot a route (R)">${ico('route')}Route</button>
        <button class="act ${targetSys === i ? 'tgt-on' : ''}" data-act="target" title="Set as target">${ico('target')}${targetSys === i ? 'Targeted' : 'Target'}</button>
        <button class="act ${fav ? 'on' : ''}" data-act="fav" title="Favourite (F)">${ico('star')}${fav ? 'Saved' : 'Favourite'}</button>
        ${inSys ? `<button class="act" data-act="closesys" title="Back to the neighbourhood (Esc)">${ico('back')}Map</button>` : `<button class="act" data-act="open" title="Open system (Enter)">${ico('system')}Open</button>`}
      </div>
      <div class="facts">
        ${fact('Distance', isShip ? '0 ly' : fmtLy(d))}${fact('Jumps', jumpsEstimate(i))}
        ${fact('Planets', s[1] ? `${s[1]} <small>· ${s[2]} moons</small>` : '—')}${fact('Landable', s[3] ? `${s[3]} <small>bodies</small>` : '—')}
        ${fact('Giants', s[4] + s[5] ? `${s[4]} gas · ${s[5]} ice` : '—')}${fact('Earth-like · water', s[6] + s[7] ? `${s[6]} · ${s[7]}` : '—')}
      </div>`;
    if (route && route.to === i) h += routeHTML();
    if (inSys) h += systemBodiesHTML();
    if (st.length || isHome || pl) {
      h += `<div class="sect"><h3>Here${mockChip}</h3>`;
      if (isHome) h += `<div class="rowcard" style="--rc:#ff9ccf"><div class="rc-ic">${ico('home')}</div><div class="rc-t"><b>${esc(M.home.name)}</b><span>On ${esc(M.home.body)} · ${esc(M.home.bodyType)}</span></div></div>`;
      for (const x of st) h += `<div class="rowcard" style="--rc:#c6a3ff"><div class="rc-ic">${ico('station')}</div><div class="rc-t"><b>${esc(x.name)}</b><span>${x.type[0].toUpperCase() + x.type.slice(1)} at ${esc(x.body)} · ${x.services.join(', ')}</span></div></div>`;
      if (pl) h += `<div class="rowcard" style="--rc:${pl.relation === 'hostile' ? '#ff6f6f' : pl.relation === 'friendly' ? '#8fe39c' : '#d6dde6'}"><div class="rc-ic">${ico('flag')}</div><div class="rc-t"><b>${esc(pl.name)}</b><span>${pl.relation[0].toUpperCase() + pl.relation.slice(1)} player · on ${esc(pl.body)}</span></div></div>`;
      h += '</div>';
    }
    if (dz) h += `<div class="sect"><h3>Danger${mockChip}</h3><div class="rowcard" style="--rc:${dz.kind === 'hazard' ? '#ffbf6e' : '#ff6f6f'}"><div class="rc-ic">${ico('danger')}</div><div class="rc-t"><b>${esc(dz.name)}</b><span>${esc(dz.effect)}</span></div></div></div>`;
    const rm = M.resources.systemMask[i];
    if (rm && !inSys) h += `<div class="sect"><h3>Resources on landable bodies${mockChip}</h3><div class="res-tags">${maskNames(rm).map(n => `<span>${n}</span>`).join('')}</div></div>`;
    return h;
  }
  function routeHTML() {
    const r = route, cur = auto ? auto.seg : r.done ? r.stops.length - 1 : 0;
    const steps = r.stops.map((s, j) => {
      const cls = ['step', j === r.stops.length - 1 ? 'end' : '', s.refuel ? 'fuel' : '', j < cur ? 'done' : '', j === cur ? 'here' : ''].join(' ');
      return `<li class="${cls}" data-sys="${s.sys}"><span class="st-dot">${j === 0 ? '•' : j}</span><span class="st-n">${esc(shortName(s.sys))}</span>${s.refuel ? `<span class="st-f">${ico('fuel')}Refuel</span>` : ''}<span class="st-d">${j ? fmtLy(s.jump) : 'start'}</span></li>`;
    }).join('');
    const sub = [`${r.refuels ? `${r.refuels} refuel stop${r.refuels > 1 ? 's' : ''}` : 'No refuelling needed'}`, ...r.avoided.map(d => `avoids ${d.name.toLowerCase()}`)].join(' · ');
    const btn = auto ? `<button class="btn wide" data-act="stopauto">${ico('stop')}Stop autopilot</button>`
      : r.done ? `<button class="btn wide ghost" data-act="clearroute">Clear route</button>`
        : `<div class="btnrow"><button class="btn primary" data-act="autopilot">${ico('play')}Start autopilot</button><button class="btn ghost" data-act="clearroute" style="flex:0 0 86px">Clear</button></div>`;
    return `<div class="route"><div class="route-h"><b>${r.jumps} jump${r.jumps === 1 ? '' : 's'}</b><span>${fmtLy(r.total)} · fuel ${ship.fuel}/${ship.max}</span></div>
      <div class="route-sub">${sub}</div><ol class="steps">${steps}</ol>${btn}</div>`;
  }
  function systemBodiesHTML() {
    if (!SY) return '';
    const nodes = SY.nodes, rows = [];
    const order = []; const walk = n => { order.push(n); n.kids.slice().sort((p, q) => p.a - q.a).forEach(walk); };
    walk(SY.prim);
    for (const n of order) {
      if (n === SY.prim) continue;
      const meta = n.kind === 3 ? '' : n.kind === 0 ? esc(n.type || 'Star') : SY.schematic ? '' : `${n.g.toFixed(2)} g · ${tempC(n.t)}`;
      const marks = (n.home ? ico('home') : '') + (n.station ? ico('station') : '');
      rows.push(`<div class="brow ${n.kind === 2 ? 'moon' : ''} ${sysSel === n.k ? 'sel' : ''}" data-body="${n.k}"><span class="bd" style="--c:#${new THREE.Color(n.kind === 0 ? classCol(n.type) : BODY_COL[n.type] || 0xa0a0a0).getHexString()}"></span>
        <span class="bn"><b>${esc(n.short)}</b><span>${esc(n.type)}${n.rings ? ' · ringed' : ''}</span></span><span class="bm">${meta}</span><span class="bl" title="${n.land ? 'Landable' : ''}">${n.land ? ico('land') : ''}</span>${marks ? `<span class="bl" style="color:#ff9ccf">${marks}</span>` : ''}</div>`);
    }
    let h = '';
    if (sysSel >= 0) h += bodyCardHTML(nodes[sysSel]);
    h += `<div class="sect"><h3>Bodies · ${nodes.length - 1}${SY.schematic ? '<span class="mock-chip">schematic</span>' : ''}</h3><div class="blist">${rows.join('')}</div>
      <div class="legend-note">${ico('land')}Landable${SY.schematic ? ' · Layout from the survey summary; full body lists exist within 40 ly in this prototype' : ' · Orbits drawn on a log scale'}</div></div>`;
    return h;
  }
  function bodyCardHTML(n) {
    if (!n) return '';
    const facts = n.kind === 0 ? [fact('Class', esc(n.type || '—')), fact('Temperature', n.t ? fmt(n.t) + ' K' : '—'), fact('Orbit', n.a ? fmt(n.a, 1) + ' AU' : 'Primary'), fact('Radius', n.rkm ? fmt(n.rkm) + ' km' : '—')]
      : SY.schematic ? [fact('Type', esc(n.type)), fact('Landable', n.land ? 'Yes' : 'No')]
        : [fact('Gravity', n.kind === 3 ? '—' : n.g.toFixed(2) + ' g'), fact('Temperature', n.kind === 3 ? '—' : tempC(n.t)), fact('Atmosphere', esc(n.atm || '—')), fact('Radius', n.rkm ? fmt(n.rkm) + ' km' : '—'), fact('Orbit', fmt(n.a, n.a < 0.1 ? 4 : 2) + ' AU'), fact('Landable', n.land ? 'Yes' : 'No')];
    const res = n.res ? `<div class="res-tags" style="margin-top:10px">${maskNames(n.res).map(x => `<span>${x}</span>`).join('')}${mockChip}</div>` : '';
    const marks = (n.home ? `<div class="rowcard" style="--rc:#ff9ccf;margin-top:10px"><div class="rc-ic">${ico('home')}</div><div class="rc-t"><b>${esc(M.home.name)}</b><span>Your base${mockChip}</span></div></div>` : '')
      + (n.station ? `<div class="rowcard" style="--rc:#c6a3ff;margin-top:10px"><div class="rc-ic">${ico('station')}</div><div class="rc-t"><b>${esc(n.station.name)}</b><span>${n.station.services.join(', ')}${mockChip}</span></div></div>` : '')
      + (n.player ? `<div class="rowcard" style="margin-top:10px"><div class="rc-ic">${ico('flag')}</div><div class="rc-t"><b>${esc(n.player.name)}</b><span>${n.player.relation}${mockChip}</span></div></div>` : '');
    return `<div class="sect"><h3>Selected</h3><div class="rowcard" style="background:var(--accent-soft)"><div class="rc-ic" style="--rc:var(--accent)">${ico(n.land ? 'land' : n.kind === 0 ? 'star' : 'ring')}</div><div class="rc-t"><b>${esc(n.name)}</b><span>${esc(n.type)}${n.land ? ' · landable' : ''}</span></div><button class="x" data-act="unbody" title="Deselect">${ico('close')}</button></div>
      <div class="facts" style="margin-top:8px">${facts.join('')}</div>${res}${marks}</div>`;
  }
  function sectorHTML(k) {
    const s = SECTORS[k], d = Math.hypot(s[4], s[5], s[6]);
    const est = glowAt(glowAll, G.glow.all, s[4], s[6]) * (1024 * 1024) / (GCELL * GCELL) * (s[2] === 64 ? 0.8 : 0.12);
    const n = est > 1e6 ? fmt(est / 1e6, est > 1e7 ? 0 : 1) + ' million' : fmt(Math.round(est / 1000) * 1000);
    const here = k === 0;
    return `<div class="ih"><div class="ih-orb sq">${ico('sector')}</div><div class="ih-t"><div class="eyebrow">Sector · ${esc(armOf(new V3(s[4], 0, s[6])))}</div><h2>${esc(s[0])}</h2>
      <div class="sub">${fmt(1024)} ly cube · ${here ? 'you are here' : fmtLy(d) + ' from Sol'}</div></div><button class="x" data-act="close">${ico('close')}</button></div>
      <div class="facts">${fact('Systems', '≈ ' + n)}${fact('Sector', `${s[1]} · ${s[2]} · ${s[3]}`)}${fact('Centre', `${fmt(s[4])}, ${fmt(s[5])}, ${fmt(s[6])}`)}${fact('Layer', s[2] === 64 ? 'Galactic plane' : s[2] > 64 ? 'Above the plane' : 'Below the plane')}</div>
      <div class="sect"><p>Every star in ${esc(s[0])} is named after it, like <b>${esc(s[0])} CG-HBW 7-1</b>.${here ? '' : ' Star-level detail in this prototype covers the 150 ly around Sol.'}</p></div>
      ${here ? `<div class="sect"><button class="btn primary wide" data-act="nbr">${ico('chev')}Zoom to your neighbourhood</button></div>` : ''}`;
  }
  function structHTML(id) {
    const s = STRUCTS.find(x => x.id === id); if (!s) return '';
    const R = s.radiusFromCentreLy ? fmt(s.radiusFromCentreLy) + ' ly' : '—';
    const d = Math.hypot(s.label[0], s.label[1], s.label[2]);
    return `<div class="ih"><div class="ih-orb sq">${ico('spiral')}</div><div class="ih-t"><div class="eyebrow">${s.kind === 'arm' ? 'Spiral arm' : s.kind === 'spur' ? 'Spur' : s.kind === 'bar' ? 'Bar' : 'Galactic centre'}</div><h2>${esc(s.name)}</h2>
      <div class="sub">${fmtLy(d)} from Sol</div></div><button class="x" data-act="close">${ico('close')}</button></div>
      <div class="facts">${fact('From the centre', R)}${fact('Arm strength', s.armStrength ? fmt(s.armStrength, 2) : '—')}</div>
      ${s.note ? `<div class="sect"><p>${esc(s.note)}</p></div>` : ''}
      ${s.id === 'orion-spur' ? `<div class="sect"><button class="btn primary wide" data-act="region">${ico('chev')}Zoom into the Orion Spur</button></div>` : ''}`;
  }
  let lastInspKey = '';
  function renderInspector(swap) {
    const open = !!sel;
    document.body.classList.toggle('insp-open', open);
    if (!open) { lastInspKey = ''; return; }
    let h = '';
    if (sel.t === 'star') h = starHTML(sel.i);
    else if (sel.t === 'sector') h = sectorHTML(sel.k);
    else if (sel.t === 'struct') h = structHTML(sel.id);
    const key = sel.t + (sel.i ?? sel.k ?? sel.id);
    const scroll = ui.insp.scrollTop;
    ui.insp.innerHTML = h;
    if (swap && key !== lastInspKey) { ui.insp.classList.remove('swap'); void ui.insp.offsetWidth; ui.insp.classList.add('swap'); ui.insp.scrollTop = 0; }
    else ui.insp.scrollTop = scroll;
    lastInspKey = key;
    panelsDirty = true;
  }
  ui.insp.addEventListener('click', e => {
    const b = e.target.closest('[data-act]'), step = e.target.closest('.step'), brow = e.target.closest('.brow');
    if (b) {
      const a = b.dataset.act;
      if (a === 'close') { select(null); return; }
      if (!sel) return;
      if (a === 'route' && sel.t === 'star') planTo(sel.i);
      if (a === 'target' && sel.t === 'star') { targetSys = targetSys === sel.i ? -1 : sel.i; renderShip(); renderInspector(); if (targetSys >= 0) toast(`Target set · ${esc(SYS.name[targetSys])} · ${fmtLy(distShip(targetSys))}`); }
      if (a === 'fav' && sel.t === 'star') { if (favs.has(sel.i)) favs.delete(sel.i); else favs.add(sel.i); saveFavs(); renderInspector(); }
      if (a === 'open' && sel.t === 'star') enterSystem(sel.i);
      if (a === 'closesys') exitSystem();
      if (a === 'autopilot') startAutopilot();
      if (a === 'stopauto') stopAutopilot();
      if (a === 'clearroute') { route = null; drawRoute(); renderInspector(); updateHash(); }
      if (a === 'unbody') { sysSel = -1; focusBody(-1); renderInspector(); }
      if (a === 'nbr') goLevel(2);
      if (a === 'region') { select(null); goLevel(1); }
      return;
    }
    if (step) { const i = +step.dataset.sys; if (mode !== 'system') { flyTo({ a: starPos(i), ld: Math.min(cam.goal.ld, log10(45)) }, 1.0); } return; }
    if (brow) { const k = +brow.dataset.body; sysSel = sysSel === k ? -1 : k; focusBody(sysSel); renderInspector(); }
  });
  function focusBody(k, instant) {
    if (!SY) return;
    const n = k >= 0 ? SY.nodes[k] : null;
    if (n) {
      const d = log10(clamp((n.ext || n.size) * 15 + 26, 42, SY.maxR * 1.3));
      sysFlyTo({ a: n.pos.clone(), ld: Math.min(scam.goal.ld, d) }, instant ? 0.001 : 0.9);
    } else sysFlyTo({ a: new V3(), ld: log10(SY.maxR * 2.25) }, 0.9);
  }
  function planTo(i) {
    if (i === ship.sys) { toast('You are already here'); return; }
    const r = planRoute(ship.sys, i);
    if (!r) { toast('No route: out of jump range for this ship'); return; }
    stopAutopilot(true);
    route = r; drawRoute(); renderInspector(); updateHash();
    toast(`Route plotted · <b>${r.jumps} jumps</b> · ${fmtLy(r.total)} <span style="color:var(--text-3)">· ${fmt(r.ms)} ms</span>`);
    if (mode === 'map' && cam.goal.ld < B_REG) {
      // frame the whole route
      const mid = new V3(); r.stops.forEach(s => mid.add(starPos(s.sys))); mid.multiplyScalar(1 / r.stops.length);
      let ext = 0; r.stops.forEach(s => { ext = Math.max(ext, starPos(s.sys).distanceTo(mid)); });
      flyTo({ a: mid, ld: clamp(log10(ext * 2.6 + 20), cam.goal.ld - 0.3, log10(400)) }, 1.1);
    }
  }

  // ---------- Autopilot (animates the ship marker along the route) ----------
  let auto = null;
  function startAutopilot() {
    if (!route || route.done) return;
    if (route.stops[0].sys !== ship.sys) { toast('Re-plot the route from where you are'); return; }
    if (mode === 'system') exitSystem(true);
    auto = { seg: 0, t: 0 };
    toast(`Autopilot engaged · ${route.jumps} jumps to ${esc(SYS.name[route.to])}`);
    flyTo({ a: ship.pos.clone(), ld: clamp(cam.goal.ld, log10(40), log10(110)) }, 0.8);
    renderInspector(); refreshRouteKinds();
  }
  function stopAutopilot(silent) { if (!auto) return; auto = null; if (!silent) toast('Autopilot off'); renderInspector(); refreshRouteKinds(); }
  function updateAutopilot(dt) {
    if (!auto || !route) return;
    const A = route.stops[auto.seg], B = route.stops[auto.seg + 1];
    auto.t += dt / 1.25;
    const e = easeInOut(clamp(auto.t / 0.78, 0, 1));
    ship.pos.set(lerp(PX[A.sys], PX[B.sys], e), lerp(PY[A.sys], PY[B.sys], e), lerp(PZ[A.sys], PZ[B.sys], e));
    if (!tween) cam.goal.a.copy(ship.pos);
    if (auto.t >= 1) {
      auto.seg++; auto.t = 0; ship.sys = B.sys; ship.fuel = B.fuel;
      updateReach(); counts.reach = reach.size; if (activeFilters.has('reach')) applyFilters(); else renderChips();
      renderShip(); renderInspector(); refreshRouteKinds();
      if (auto.seg >= route.stops.length - 1) {
        auto = null; route.done = true; refreshRouteKinds(); renderInspector();
        toast(`Arrived at <b>${esc(SYS.name[ship.sys])}</b>`, 3000);
      }
    }
  }

  // ---------- Search (secondary) ----------
  const searchIndex = [];
  for (let i = 0; i < N; i++) searchIndex.push({ n: SYS.name[i], l: SYS.name[i].toLowerCase(), t: 'star', i, cat: SYS.cat[i] });
  SECTORS.forEach((s, k) => searchIndex.push({ n: s[0], l: s[0].toLowerCase(), t: 'sector', k, cat: 1 }));
  STRUCTS.forEach(s => searchIndex.push({ n: s.name, l: s.name.toLowerCase(), t: 'struct', id: s.id, cat: 1 }));
  M.stations.forEach(s => searchIndex.push({ n: s.name, l: s.name.toLowerCase(), t: 'star', i: s.sys, cat: 1, via: 'Station in ' + s.system }));
  M.players.forEach(p => searchIndex.push({ n: p.name, l: p.name.toLowerCase(), t: 'star', i: p.sys, cat: 1, via: 'Player base' }));
  searchIndex.push({ n: 'Home base', l: 'home base', t: 'star', i: M.home.sys, cat: 1, via: M.home.system });
  let results = [], resAct = 0;
  function runSearch() {
    const q = ui.search.value.trim().toLowerCase();
    if (!q) { ui.results.classList.remove('open'); results = []; return; }
    const hits = [];
    for (const e of searchIndex) { const p = e.l.indexOf(q); if (p >= 0) hits.push({ e, s: (p === 0 ? 0 : 1) + (e.cat ? 0 : 2) + (e.t === 'star' ? distShip(e.i) / 1000 : 0) + e.l.length / 200 }); if (hits.length > 4000) break; }
    hits.sort((a, b) => a.s - b.s); results = hits.slice(0, 7).map(h => h.e); resAct = 0;
    ui.results.innerHTML = results.length ? results.map((e, k) => {
      const c = e.t === 'star' ? SYS.col[e.i] : e.t === 'sector' ? '#8fb4e8' : '#a6caff';
      const meta = e.via ? esc(e.via) : e.t === 'star' ? fmtLy(distShip(e.i)) : e.t === 'sector' ? 'Sector' : 'Galaxy';
      return `<button class="res${k === resAct ? ' act' : ''}" data-k="${k}"><span class="rdot" style="background:${c}"></span><span class="rname">${esc(e.n)}</span><span class="rmeta">${meta}</span></button>`;
    }).join('') : '<div class="res-empty">No matches</div>';
    ui.results.classList.add('open');
  }
  function pickResult(e) {
    if (!e) return;
    ui.search.value = ''; ui.results.classList.remove('open'); ui.search.blur(); hideHint();
    if (e.t === 'star') { select({ t: 'star', i: e.i }, { keepCam: true }); flyToStar(e.i, log10(34)); }
    if (e.t === 'sector') { if (mode === 'system') exitSystem(true); select({ t: 'sector', k: e.k }, { keepCam: true }); const s = SECTORS[e.k]; flyTo({ a: new V3(s[4], 0, s[6]), ld: LOG.region }, 1.5); }
    if (e.t === 'struct') { if (mode === 'system') exitSystem(true); select({ t: 'struct', id: e.id }, { keepCam: true }); flyTo({ ld: LOG.galaxy, pitch: 0.62 }, 1.6); }
  }
  ui.search.addEventListener('input', runSearch);
  ui.search.addEventListener('keydown', e => {
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') { e.preventDefault(); resAct = clamp(resAct + (e.key === 'ArrowDown' ? 1 : -1), 0, results.length - 1); [...ui.results.children].forEach((c, k) => c.classList.toggle('act', k === resAct)); }
    if (e.key === 'Enter') pickResult(results[resAct]);
    if (e.key === 'Escape') { ui.search.value = ''; ui.results.classList.remove('open'); ui.search.blur(); e.stopPropagation(); }
  });
  ui.results.addEventListener('mousedown', e => { const b = e.target.closest('.res'); if (b) { e.preventDefault(); pickResult(results[+b.dataset.k]); } });
  ui.search.addEventListener('blur', () => setTimeout(() => ui.results.classList.remove('open'), 120));

  // ---------- Labels: projected HTML, placed by priority, never overlapping each other or the panels ----------
  const measureCtx = document.createElement('canvas').getContext('2d');
  const widthCache = new Map();
  const ST = {
    star: { f: '500 12px Inter', h: 16 }, major: { f: '600 12.5px Inter', h: 16 }, dim: { f: '450 11px Inter', h: 15 }, sel: { f: '650 13px Inter', h: 17 },
    special: { f: '600 12px Inter', h: 16 }, sector: { f: '600 11px Inter', ls: 0.18, up: 1, h: 15 }, arm: { f: '500 12px Inter', ls: 0.34, up: 1, h: 16 },
    struct: { f: '500 13px Inter', ls: 0.26, up: 1, h: 17 }, structs: { f: '500 11.5px Inter', ls: 0.26, up: 1, h: 16 }, ring: { f: '500 10.5px Inter', ls: 0.04, h: 14 },
    dir: { f: '500 10px Inter', ls: 0.12, up: 1, h: 14 }, danger: { f: '600 11px Inter', ls: 0.08, up: 1, h: 15 }, pin: { f: '650 12px Inter', h: 40 },
    body: { f: '600 12.5px Inter', h: 30 }, body2: { f: '450 11px Inter', h: 30 }, moon: { f: '500 11px Inter', h: 15 },
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
  const LB = new Map();
  let placed = [], cands = [], frameNo = 0, panelRects = [], panelsDirty = true, lastPanelT = 0;
  function readPanels(now) {
    if (!panelsDirty && now - lastPanelT < 400) return;
    panelsDirty = false; lastPanelT = now; panelRects = [];
    for (const el of [ui.crumbs, $('#levelcap'), ui.rail, $('#topright'), ui.chips, $('#tools'), document.body.classList.contains('insp-open') ? $('#inspector') : $('#footer')]) {
      if (!el || el.classList.contains('hide')) continue;
      const r = el.getBoundingClientRect(); if (r.width > 0) panelRects.push([r.left - 6, r.top - 6, r.right + 6, r.bottom + 6]);
    }
  }
  const hits = (r, list) => { for (const q of list) if (r[0] < q[2] && r[2] > q[0] && r[1] < q[3] && r[3] > q[1]) return true; return false; };
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
    let n = 0;
    for (const c of cands) {
      if (n >= 64) break;
      const p = tryPlace(c); if (!p) continue;
      n++;
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
      if (rec.wp && rec.cm && rec.cm._vp) { // keep fading labels attached to their point
        proj(rec.cm, rec.wp[0], rec.wp[1], rec.wp[2], projV);
        if (projV.ok) { const tr = `translate3d(${Math.round(projV.x + rec.dx)}px,${Math.round(projV.y + rec.dy)}px,0)`; if (rec.tr !== tr) { rec.el.style.transform = tr; rec.tr = tr; } }
      }
    }
  }

  // Markers (ship, selection, hover, target)
  const mk = cls => { const el = document.createElement('div'); el.className = 'mk ' + cls; el.style.opacity = '0'; ui.markers.appendChild(el); return { el, op: -1, tr: '' }; };
  const MK = { ship: mk('mk-ship'), sel: mk('mk-ring mk-sel'), hov: mk('mk-ring mk-hov'), tgt: mk('mk-ring mk-target') };
  function setMk(m, x, y, alpha) {
    const op = alpha > 0.01 ? Math.min(1, alpha).toFixed(2) : '0';
    if (m.op !== op) { m.el.style.opacity = op; m.op = op; }
    if (alpha > 0.01) { const tr = `translate3d(${x.toFixed(1)}px,${y.toFixed(1)}px,0)`; if (m.tr !== tr) { m.el.style.transform = tr; m.tr = tr; } }
  }

  // Per-frame star projection, shared by labels and picking.
  const SXa = new Float32Array(N), SYa = new Float32Array(N), SAPP = new Float32Array(N), SOK = new Uint8Array(N);
  let starsProjected = false, appLimit = 9;
  const labelledLast = new Set();
  function projectStars(alpha) {
    starsProjected = alpha > 0.02;
    if (!starsProjected) return;
    const e = camera._vp, cx = camera.position.x, cy = camera.position.y, cz = camera.position.z;
    const fogFar = localMat.uniforms.uFogFar.value, t = localMat.uniforms.uTarget.value;
    for (let i = 0; i < N; i++) {
      const x = PX[i], y = PY[i], z = PZ[i];
      const cw = e[3] * x + e[7] * y + e[11] * z + e[15];
      if (cw <= 1e-6) { SOK[i] = 0; continue; }
      const sx = ((e[0] * x + e[4] * y + e[8] * z + e[12]) / cw * 0.5 + 0.5) * W, sy = (0.5 - (e[1] * x + e[5] * y + e[9] * z + e[13]) / cw * 0.5) * H;
      if (sx < -20 || sy < -20 || sx > W + 20 || sy > H + 20) { SOK[i] = 0; continue; }
      const fd = Math.hypot(x - t.x, y - t.y, z - t.z);
      if (fd > fogFar) { SOK[i] = 0; continue; }
      SXa[i] = sx; SYa[i] = sy; SOK[i] = 1;
      SAPP[i] = MAG[i] + 5 * log10(Math.max(0.05, Math.hypot(x - cx, y - cy, z - cz)) / 32.6);
    }
  }
  function starLabels(alpha) {
    if (alpha < 0.03 || !starsProjected) return;
    const special = new Map();
    const addS = (i, p) => { if (i >= 0) special.set(i, Math.max(special.get(i) || 0, p)); };
    addS(ship.sys, 800); addS(M.home.sys, 460); for (const s of M.stations) addS(s.sys, 300); for (const p of M.players) addS(p.sys, 280);
    for (const f of favs) addS(f, 250); addS(targetSys, 600);
    if (route) for (const s of route.stops) addS(s.sys, 420);
    if (sel && sel.t === 'star') addS(sel.i, 1000); if (hov && hov.t === 'star') addS(hov.i, 900);
    const anyF = activeFilters.size > 0;
    let count = 0;
    const cand = [];
    for (let i = 0; i < N; i++) {
      if (!SOK[i]) continue;
      const sp = special.get(i);
      const app = SAPP[i];
      if (!sp && app > appLimit) continue;
      let pri = 100 - app + (SYS.cat[i] ? 1.2 : 0) + (labelledLast.has(i) ? 1.0 : 0) + (sp || 0);
      if (anyF) { const fo = filterOf[i]; if (fo >= 0) pri += FILTERS[fo].rare ? 60 : 2.5; else if (!sp || (sp < 400 && i !== M.home.sys)) continue; }
      cand.push(i, pri); count++;
    }
    appLimit = clamp(appLimit + (count > 1400 ? -0.25 : count < 500 ? 0.25 : 0), 2, 22);
    labelledLast.clear();
    for (let k = 0; k < cand.length; k += 2) {
      const i = cand[k], pri = cand[k + 1], sp = special.get(i), fo = filterOf[i];
      let cls = 'lbl-star', st = ST.star, icon = '';
      const isSel = sel && sel.t === 'star' && sel.i === i;
      if (isSel) { cls += ' sel'; st = ST.sel; }
      else if (route && route.stops.some(s => s.sys === i)) { cls += ' rt'; st = ST.special; }
      else if (anyF && fo >= 0 && FILTERS[fo].rare) { cls += ' filter'; st = ST.special; }
      else if (sp) { cls += ' special'; st = ST.special; }
      else if (SAPP[i] < appLimit - 4 || SYS.cat[i]) { cls += ' major'; st = ST.major; }
      else { cls += ' dim'; st = ST.dim; }
      if (M.home.sys === i) icon = 'home'; else if (FL[i] & F.STATION) icon = 'station'; else if (FL[i] & F.PLAYER) icon = 'flag'; else if (favs.has(i)) icon = 'star';
      const text = shortName(i);
      const w = textW(text, st) + (icon ? 16 : 0);
      const iconC = icon === 'home' ? '#ff9ccf' : icon === 'station' ? '#c6a3ff' : icon === 'flag' ? (playersBy.get(i).relation === 'hostile' ? '#ff6f6f' : '#d6dde6') : '#ffbf6e';
      const html = (icon ? `<span class="ic" style="color:${iconC}">${ico(icon)}</span>` : '') + esc(text);
      const hasMk = i === ship.sys || i === targetSys || (sel && sel.t === 'star' && sel.i === i) || (hov && hov.t === 'star' && hov.i === i);
      cands.push({ key: 's' + i, pri, x: SXa[i], y: SYa[i], w, h: st.h, cls, html, alpha, off: hasMk ? (i === targetSys ? 18 : 14) : 8, pad: 4, obj: { t: 'star', i }, wp: [PX[i], PY[i], PZ[i]], cm: camera, fc: anyF && fo >= 0 ? FILTERS[fo].color : '' });
    }
  }

  // ---------- Input ----------
  const ray = new THREE.Raycaster(), ndc = new THREE.Vector2(), plane = new THREE.Plane(new V3(0, 1, 0), -SECTOR_Y), hitP = new V3();
  let mouse = { x: -1, y: -1, in: false }, drag = null, pickPending = false, lastClick = 0;
  function pickAt(mx, my, wantLabelObj) {
    if (wantLabelObj) return wantLabelObj;
    const lvl = levelOf();
    if (lvl === 3) {
      if (!SY) return null;
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
    if (lvl === 2 && starsProjected) {
      let best = -1, bs = 1e9;
      for (let i = 0; i < N; i++) {
        if (!SOK[i]) continue;
        const dx = SXa[i] - mx, dy = SYa[i] - my; if (dx > 16 || dx < -16 || dy > 16 || dy < -16) continue;
        const d = Math.hypot(dx, dy); if (d > 16) continue;
        const s = d + SAPP[i] * 0.6 + (filterOf[i] === -2 ? 12 : 0) - (LB.has('s' + i) && LB.get('s' + i).seen === frameNo ? 4 : 0);
        if (s < bs) { bs = s; best = i; }
      }
      return best >= 0 ? { t: 'star', i: best } : null;
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
  const labelObjAt = el => { const l = el && el.closest && el.closest('.lbl.hit'); if (!l) return null; for (const rec of LB.values()) if (rec.el === l) return rec.obj; return null; };
  function setHover(o) {
    const same = (a, b) => a === b || (a && b && a.t === b.t && a.i === b.i && a.k === b.k && a.id === b.id);
    if (o && o.t === 'body') { if (sysHov !== o.k) { sysHov = o.k; } hov = null; }
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
      const i = o.i, d = distShip(i), tags = [];
      if (i === ship.sys) tags.push('<span class="a">You are here</span>'); else if (d <= ship.range) tags.push('<span class="a">In range</span>'); else tags.push(`<span>≈ ${Math.ceil(d / ship.range)} jumps</span>`);
      if (refuelAt(i)) tags.push('<span class="w">Fuel</span>');
      if (FL[i] & F.STATION) tags.push('<span>Station</span>');
      if (SYS.sum[i][3]) tags.push(`<span>${SYS.sum[i][3]} landable</span>`);
      if (FL[i] & F.EARTH) tags.push('<span class="a">Earth-like</span>');
      if (FL[i] & F.DANGER) tags.push('<span class="d">Danger</span>');
      h = `<div class="hc-name"><i style="--c:${SYS.col[i]}"></i>${esc(SYS.name[i])}</div><div class="hc-sub">${esc(SYS.cls[i])} · ${starKind(SYS.cls[i])} · ${fmtLy(d)}</div><div class="hc-row">${tags.join('')}</div><div class="hc-hint">Click for details · double-click to fly there</div>`;
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
  function clickObj(o) {
    if (!o) { if (sel && mode !== 'system') select(null); return; }
    if (o.t === 'pin') { goLevel(cam.goal.ld > B_GAL ? 1 : 2); return; }
    if (o.t === 'body') { sysSel = sysSel === o.k ? -1 : o.k; focusBody(sysSel); renderInspector(); return; }
    if (o.t === 'struct') { select(o, { keepCam: true }); return; }
    select(o);
  }
  function dblObj(o) {
    if (!o) return;
    if (o.t === 'star') { select(o, { keepCam: true }); const near = cam.goal.ld < log10(40) && cam.goal.a.distanceTo(starPos(o.i)) < 3; if (near) enterSystem(o.i); else flyToStar(o.i, log10(26)); }
    if (o.t === 'sector') { const s = SECTORS[o.k]; if (o.k === 0) goLevel(2); else flyTo({ a: new V3(s[4], 0, s[6]), ld: log10(2600) }, 1.2); }
    if (o.t === 'body') { sysSel = o.k; focusBody(o.k); renderInspector(); }
    if (o.t === 'pin') goLevel(2);
  }
  ui.stage.addEventListener('pointerdown', e => {
    if (e.button === 2 || e.button === 1 || e.button === 0) {
      drag = { x: e.clientX, y: e.clientY, sx: e.clientX, sy: e.clientY, pan: e.button !== 0 || e.shiftKey, moved: false, lbl: labelObjAt(e.target) };
      ui.stage.setPointerCapture(e.pointerId);
    }
  });
  ui.stage.addEventListener('pointermove', e => {
    mouse.x = e.clientX; mouse.y = e.clientY; mouse.in = true;
    if (drag) {
      const dx = e.clientX - drag.x, dy = e.clientY - drag.y; drag.x = e.clientX; drag.y = e.clientY;
      if (!drag.moved && Math.hypot(e.clientX - drag.sx, e.clientY - drag.sy) > 4) { drag.moved = true; ui.stage.classList.add('drag'); ui.hc.classList.remove('show'); hideHint(); tween = null; stween = null; }
      if (!drag.moved) return;
      const c = mode === 'system' && sysBlend > 0.5 ? scam : cam;
      if (drag.pan) {
        if (c === scam) {
          const d = Math.pow(10, c.goal.ld), k = 2 * d * Math.tan(sysCam.fov * Math.PI / 360) / H;
          const yaw = c.cur.yaw, r = new V3(-Math.cos(yaw), 0, Math.sin(yaw)), f = new V3(Math.sin(yaw), 0, Math.cos(yaw));
          c.goal.a.addScaledVector(r, -dx * k).addScaledVector(f, dy * k / Math.max(0.35, Math.sin(c.cur.pitch)));
          return;
        }
        if (smooth(4.05, 4.78, cam.cur.ld) > 0.5) return;
        const d = Math.pow(10, cam.cur.ld), k = 2 * d * Math.tan(camera.fov * Math.PI / 360) / H;
        const yaw = cam.cur.yaw, r = new V3(-Math.cos(yaw), 0, Math.sin(yaw)), f = new V3(Math.sin(yaw), 0, Math.cos(yaw));
        cam.goal.a.addScaledVector(r, -dx * k).addScaledVector(f, dy * k / Math.max(0.35, Math.sin(cam.cur.pitch)));
        if (cam.goal.a.length() > 9000) cam.goal.a.setLength(9000);
      } else {
        c.goal.yaw -= dx * 0.0055;
        c.goal.pitch = clamp(c.goal.pitch + dy * 0.0045, PITCH_MIN, PITCH_MAX);
        if (topView && c.goal.pitch < 1.45) { topView = false; ui.top.classList.remove('on'); }
      }
      return;
    }
    if (!pickPending) { pickPending = true; requestAnimationFrame(() => { pickPending = false; if (!drag) setHover(pickAt(mouse.x, mouse.y, labelObjAt(document.elementFromPoint(mouse.x, mouse.y)))); positionHoverCard(); }); }
  });
  ui.stage.addEventListener('pointerup', e => {
    const d = drag; drag = null; ui.stage.classList.remove('drag');
    if (!d || d.moved) return;
    if (e.button !== 0) return;
    const o = d.lbl || pickAt(e.clientX, e.clientY);
    const now = performance.now();
    if (now - lastClick < 320) { dblObj(o); lastClick = 0; return; }
    lastClick = now; clickObj(o);
  });
  ui.stage.addEventListener('pointerleave', () => { mouse.in = false; if (!drag) setHover(null); });
  ui.stage.addEventListener('contextmenu', e => e.preventDefault());
  window.addEventListener('wheel', e => {
    if (e.target.closest && e.target.closest('#inspector, #results, #rail, #chips')) return;
    e.preventDefault(); hideHint();
    let dy = e.deltaY * (e.deltaMode === 1 ? 33 : e.deltaMode === 2 ? 400 : 1);
    const dl = clamp(dy * (e.ctrlKey ? 0.01 : 0.0011), -0.45, 0.45);
    if (mode === 'system' && sysBlend > 0.5) {
      stween = null;
      scam.goal.ld = clamp(scam.goal.ld + dl, scam.min, scam.max + 0.5);
      if (scam.goal.ld > scam.max + 0.3) exitSystem();
      return;
    }
    if (mode === 'system') return;
    tween = null;
    const before = cam.goal.ld;
    cam.goal.ld = clamp(cam.goal.ld + dl, LOG.min, LOG.max);
    const k = 1 - Math.pow(10, cam.goal.ld - before);
    // Zoom toward the star under the cursor at the Neighbourhood level.
    if (dl < 0 && hov && hov.t === 'star' && cam.goal.ld < B_REG) cam.goal.a.lerp(starPos(hov.i), clamp(k * 1.15, 0, 1));
    if (before <= LOG.min + 0.001 && dl < 0) afterZoom(dl); else if (dl < 0) afterZoom(dl * 0.5);
  }, { passive: false });
  window.addEventListener('keydown', e => {
    if (e.target === ui.search) return;
    const k = e.key;
    if (k === '/' || (k === 'k' && (e.ctrlKey || e.metaKey))) { e.preventDefault(); ui.search.focus(); return; }
    if (k === 'Escape') { backOut(); return; }
    const c = mode === 'system' && sysBlend > 0.5 ? scam : cam;
    if (k === 'ArrowLeft' || k === 'a') { c.goal.yaw += 0.18; }
    else if (k === 'ArrowRight' || k === 'd') { c.goal.yaw -= 0.18; }
    else if (k === 'ArrowUp' || k === 'w') { c.goal.pitch = clamp(c.goal.pitch + 0.12, PITCH_MIN, PITCH_MAX); }
    else if (k === 'ArrowDown' || k === 's') { c.goal.pitch = clamp(c.goal.pitch - 0.12, PITCH_MIN, PITCH_MAX); }
    else if (k === '+' || k === '=' || k === 'PageUp') zoomBy(-0.3);
    else if (k === '-' || k === '_' || k === 'PageDown') zoomBy(0.3);
    else if (k >= '1' && k <= '4') goLevel(+k - 1);
    else if (k === 't' || k === 'T') toggleTop();
    else if (k === 'c' || k === 'C') centreOnMe();
    else if ((k === 'r' || k === 'R') && sel && sel.t === 'star') planTo(sel.i);
    else if ((k === 'f' || k === 'F') && sel && sel.t === 'star') { if (favs.has(sel.i)) favs.delete(sel.i); else favs.add(sel.i); saveFavs(); renderInspector(); }
    else if (k === 'Enter' && sel && sel.t === 'star' && mode !== 'system') enterSystem(sel.i);
    else if (k === ' ' && route && !route.done) { e.preventDefault(); if (auto) stopAutopilot(); else startAutopilot(); }
    else return;
    hideHint(); tween = null;
  });
  function backOut() {
    if (ui.results.classList.contains('open')) { ui.results.classList.remove('open'); return; }
    if (mode === 'system' && sysSel >= 0) { sysSel = -1; focusBody(-1); renderInspector(); return; }
    if (mode === 'system') { exitSystem(); return; }
    if (sel) { select(null); return; }
    const l = goalLevel(); if (l > 0) goLevel(l - 1);
  }
  window.addEventListener('resize', resize);
  function resize() {
    W = window.innerWidth; H = window.innerHeight;
    renderer.setSize(W, H, false);
    camera.aspect = sysCam.aspect = W / H; camera.updateProjectionMatrix(); sysCam.updateProjectionMatrix();
    if (routeLine) routeLine.material.resolution.set(W, H);
    panelsDirty = true;
    const r = ui.rail.getBoundingClientRect();
    railStops = ui.stops.map(b => { const q = b.getBoundingClientRect(); return q.top + q.height / 2 - r.top; });
  }
  let railStops = [0, 0, 0, 0];

  // ---------- HUD (breadcrumb, rail, level caption, scale) ----------
  let crumbKey = '', capKey = '';
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
      const sep = '<svg class="crumb-sep ico" viewBox="0 0 20 20"><path d="M7.5 4.8L12.7 10l-5.2 5.2"/></svg>';
      ui.crumbs.innerHTML = parts.map((p, k) => (k ? sep : '') + `<button class="crumb${k === parts.length - 1 ? ' cur' : ''}${prev[k] !== p.n ? ' enter' : ''}" data-l="${p.l}">${k === 0 ? '<span class="lvl-dot"></span>' : ''}${esc(p.n)}</button>`).join('');
      panelsDirty = true;
    }
    // level caption
    const d = Math.pow(10, c.ld), across = 2 * d * Math.tan(camera.fov * Math.PI / 360) * (W / H);
    const names = ['Galaxy', 'Region', 'Neighbourhood', 'System'];
    let sub;
    if (lvl === 3 && SY) sub = `${SY.nodes.length - 1} bodies${SY.schematic ? ' · schematic' : ''}`;
    else if (lvl === 0) sub = '≈ 120 billion systems · 100,000 ly across';
    else sub = `${fmt(niceRound(across))} ly across`;
    if (lvl === 2) sub += ` · ${fmt(N)} stars within 150 ly`;
    const ck2 = names[lvl] + sub;
    if (ck2 !== capKey) { capKey = ck2; ui.levelname.textContent = names[lvl]; ui.levelsub.textContent = sub; }
    // rail thumb: a continuous position across the four stops
    let u;
    if (c.ld >= LOG.galaxy) u = 0;
    else if (c.ld >= LOG.region) u = (LOG.galaxy - c.ld) / (LOG.galaxy - LOG.region);
    else if (c.ld >= LOG.nbr) u = 1 + (LOG.region - c.ld) / (LOG.region - LOG.nbr);
    else u = 2 + 0.5 * clamp((LOG.nbr - c.ld) / (LOG.nbr - LOG.min), 0, 1);
    u = lerp(u, 3, easeInOut(sysBlend));
    const i0 = Math.min(2, Math.floor(u)), f = u - i0, y = lerp(railStops[i0], railStops[i0 + 1], f);
    ui.thumb.style.transform = `translate3d(0,${(y - 8).toFixed(1)}px,0)`;
    ui.railFill.style.height = Math.max(0, y - railStops[0]) + 'px';
    ui.railFill.style.top = '0px';
    ui.stops.forEach((b, k) => { b.classList.toggle('cur', k === lvl); b.classList.toggle('near', Math.abs(u - k) < 0.5 && k !== lvl); });
    // chips only at the Neighbourhood level
    const showChips = lvl === 2 && mode === 'map';
    if (ui.chips.classList.contains('hide') === showChips) { ui.chips.classList.toggle('hide', !showChips); panelsDirty = true; }
    // scale bar
    let lyPerPx, unit = 'ly';
    if (lvl === 3) { ui.scalebar.parentElement.style.opacity = 0; }
    else {
      ui.scalebar.parentElement.style.opacity = 1;
      lyPerPx = 2 * d * Math.tan(camera.fov * Math.PI / 360) / H;
      const nice = niceRound(lyPerPx * 120), px = nice / lyPerPx;
      ui.scalebar.style.width = px.toFixed(0) + 'px'; ui.scaletxt.textContent = fmt(nice) + ' ' + unit;
    }
  }
  function niceRound(v) { const p = Math.pow(10, Math.floor(log10(v))), m = v / p; return (m < 1.5 ? 1 : m < 3.5 ? 2 : m < 7.5 ? 5 : 10) * p; }
  ui.crumbs.addEventListener('click', e => { const b = e.target.closest('.crumb'); if (!b || b.classList.contains('cur')) return; goLevel(+b.dataset.l); });

  // ---------- Hash (deep links for review and screenshots) ----------
  let hashTimer = 0;
  function updateHash() {
    clearTimeout(hashTimer);
    hashTimer = setTimeout(() => {
      if (!initDone) return;
      const p = new URLSearchParams();
      p.set('level', ['galaxy', 'region', 'neighbourhood', 'system'][goalLevel()]);
      if (sel && sel.t === 'star') p.set('select', SYS.name[sel.i]);
      if (sel && sel.t === 'sector') p.set('sector', SECTORS[sel.k][0]);
      if (sel && sel.t === 'struct') p.set('struct', sel.id);
      if (route && !route.done) p.set('route', SYS.name[route.to]);
      if (activeFilters.size) p.set('filters', [...activeFilters].join(','));
      if (topView) p.set('top', '1');
      history.replaceState(null, '', '#' + p.toString().replace(/\+/g, '%20'));
    }, 400);
  }

  // ---------- Frame ----------
  let last = performance.now(), initDone = false, fpsAcc = 0, fpsN = 0;
  window.__fps = 0;
  const lerpOp = (o, v, dt) => o + (v - o) * (1 - Math.exp(-dt * 8));
  const op = { gal: 1, glow: 1, haze: 0, sect: 0, arms: 0, local: 0, nbr: 0, structL: 1, sectL: 0, armL: 0, starL: 0, pin: 1 };
  const pinObj = { t: 'pin' };
  function frame(now) {
    const dt = Math.min(0.1, (now - last) / 1000); last = now;
    fpsAcc += dt; fpsN++; if (fpsAcc > 0.5) { window.__fps = Math.round(fpsN / fpsAcc); fpsAcc = 0; fpsN = 0; }
    if (tween && stepTween(tween, now)) tween = null;
    if (stween && stepTween(stween, now)) stween = null;
    smoothCam(cam, dt, 9); smoothCam(scam, dt, 9);
    const shiftGoal = document.body.classList.contains('insp-open') && W > 900 ? (Math.min(368, W * 0.3) + 20) / 2 : 0;
    viewShift = INSTANT_NOW ? shiftGoal : viewShift + (shiftGoal - viewShift) * (1 - Math.exp(-dt * 7));
    if (Math.abs(viewShift - shiftGoal) < 0.3) viewShift = shiftGoal;
    // system blend
    const sp = clamp((now - sysT0) / 900, 0, 1);
    sysBlend = INSTANT_NOW ? sysGoal : lerp(sysFrom, sysGoal, easeInOut(sp));
    updateAutopilot(dt);
    placeCamera();
    const x = cam.cur.ld;
    // layer opacities as smooth functions of zoom: each level shows only what makes sense at its scale
    const main = 1 - sysBlend;
    const gal = smooth(2.95, 3.85, x) * lerp(0.42, 1, smooth(3.9, 4.6, x));
    const glow = smooth(2.9, 3.5, x) * lerp(0.16, 0.44, smooth(3.85, 4.6, x));
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
    for (const a of ARMS) { a.mat.uniforms.uOpacity.value = (arms * 0.22 + a.hi * 0.25) * main; a.mesh.visible = a.mat.uniforms.uOpacity.value > 0.003; a.hi = lerpOp(a.hi, hov && hov.t === 'struct' && STRUCTS.find(s => s.id === hov.id)?.path === a.id ? 1 : 0, dt); }
    localMat.uniforms.uOpacity.value = local * main; localPts.visible = local * main > 0.003;
    const d = Math.pow(10, x);
    localMat.uniforms.uTarget.value.copy(target);
    localMat.uniforms.uFogNear.value = Math.max(14, d * 0.95); localMat.uniforms.uFogFar.value = Math.max(30, d * 1.9);
    localMat.uniforms.uBoost.value = clamp((x - 2.3) * 2.2, 0, 4);
    gridMat.uniforms.uOpacity.value = nbr * main; grid.visible = nbr * main > 0.003; grid.position.copy(ship.pos);
    bubble.position.copy(ship.pos); bubble.material.uniforms.uOpacity.value = nbr * main * (activeFilters.has('reach') ? 1.6 : 1); bubble.visible = nbr * main > 0.003;
    bubbleRing.position.copy(ship.pos); bubbleRing.material.opacity = nbr * main * 0.55; bubbleRing.visible = bubble.visible;
    for (const dm of dangerMeshes) { dm.m.material.uniforms.uOpacity.value = nbr * main * (1 + dm.boost * 1.2); dm.m.visible = nbr * main > 0.003; }
    if (routeLine) { routeLine.material.opacity = 0.9 * local * main; routeLine.visible = local * main > 0.01; routeStops.material.uniforms.uOpacity.value = local * main; routeStops.visible = routeLine.visible; }
    // system scene fade
    if (SY) {
      const sb = easeOut(sysBlend);
      SY.root.scale.setScalar(lerp(0.3, 1, sb));
      SY.root.traverse(o => { if (o.material) { const base = o.userData.baseOp ?? 1; o.material.opacity = base * sb; } });
      SY.nodes.forEach(n => { if (n.orbitLine) { const s = n.k === sysSel ? 1 : n.k === sysHov ? 0.6 : 0; n.orbitLine.material.color.setHex(s > 0 ? 0x6bccff : 0xbfd6ff); n.orbitLine.material.opacity = (n.orbitLine.userData.baseOp + s * 0.35) * sb; } });
      sysScene.visible = sysBlend > 0.002;
    }
    // drop lines
    setDrop(dropSel, sel && sel.t === 'star' ? sel.i : -1, nbr * main);
    setDrop(dropHov, hov && hov.t === 'star' && !(sel && sel.t === 'star' && sel.i === hov.i) ? hov.i : -1, nbr * main);

    // render
    renderer.clear();
    if (sysBlend < 0.999) renderer.render(scene, camera);
    if (sysBlend > 0.001) {
      placeSysCamera(); sysLight.position.set(0, 0, 0);
      veilMat.uniforms.uA.value = easeInOut(sysBlend) * 0.94; renderer.render(veilScene, veilCam);
      renderer.render(sysScene, sysCam);
    }

    // labels and markers
    readPanels(now);
    prepVP(camera); prepVP(sysCam);
    frameNo++; placed = []; cands = [];
    const markerSize = (p) => [p.x - 8, p.y - 8, p.x + 8, p.y + 8];
    // ship marker
    proj(camera, ship.pos.x, ship.pos.y, ship.pos.z, projV);
    const shipA = main * (1 - smooth(4.4, 4.9, x) * 0.0);
    if (projV.ok) { setMk(MK.ship, projV.x, projV.y, shipA); if (shipA > 0.3) placed.push(markerSize(projV)); } else setMk(MK.ship, 0, 0, 0);
    if (op.pin > 0.02 && projV.ok) addCand({ key: 'pin', pri: 5000, x: projV.x, y: projV.y, w: textW('You are here', ST.pin) + 34, h: 40, off: 12, mode: 'above', cls: 'lbl-pin', html: '<span class="pin">You are here</span><div class="pin-stem"></div>', alpha: op.pin * main, obj: pinObj, wp: [ship.pos.x, ship.pos.y, ship.pos.z], cm: camera });
    // selection / hover / target rings
    const ringAt = (m, i, a) => { if (i < 0) { setMk(m, 0, 0, 0); return; } proj(camera, PX[i], PY[i], PZ[i], projV); if (projV.ok) { setMk(m, projV.x, projV.y, a); if (a > 0.3) placed.push(markerSize(projV)); } else setMk(m, 0, 0, 0); };
    ringAt(MK.sel, sel && sel.t === 'star' ? sel.i : -1, local * main);
    ringAt(MK.hov, hov && hov.t === 'star' ? hov.i : -1, local * main * 0.9);
    ringAt(MK.tgt, targetSys, local * main);

    if (main > 0.02) {
      // Galaxy: structure labels
      if (op.structL > 0.02) for (const s of STRUCTS) {
        proj(camera, s.label[0], s.label[1], s.label[2], projV); if (!projV.ok) continue;
        const kind = s.kind, st = kind === 'spur' || kind === 'bar' ? ST.structs : ST.struct;
        const pill = true;
        addCand({ key: 'g' + s.id, pri: 3000 - (kind === 'spur' ? 50 : 0), x: projV.x, y: projV.y, w: textW(s.name, st) + (pill ? 18 : 0), h: pill ? 25 : st.h, mode: 'center', nudge: 16, cls: 'lbl-struct ' + kind + (pill ? ' pill' : ''), html: esc(s.name), alpha: op.structL * main, obj: { t: 'struct', id: s.id }, wp: s.label, cm: camera });
      }
      // Region: sector names and arm names along their paths
      if (op.sectL > 0.02) {
        const cs = sectorOfPos(cam.cur.a.x, 0, cam.cur.a.z);
        for (const o of regionSectors) {
          const s = o.s; proj(camera, s[4], SECTOR_Y, s[6], projV); if (!projV.ok) continue;
          const dist = Math.hypot(s[4] - target.x, s[6] - target.z), fade = 1 - smooth(2600, 3600, Math.hypot(s[4], s[6]));
          const isCur = o.k === 0, isHov = hov && hov.t === 'sector' && hov.k === o.k;
          addCand({ key: 'c' + o.k, pri: 1500 - dist / 100 + (isCur ? 1200 : 0) + (isHov ? 1300 : 0) + (o.k === cs ? 50 : 0), x: projV.x, y: projV.y, w: textW(s[0], ST.sector), h: ST.sector.h, mode: 'center', nudge: isCur ? 14 : 0, cls: 'lbl-sector' + (isCur ? ' cur' : '') + (isHov ? ' hov' : ''), html: esc(s[0]), alpha: op.sectL * main * fade, obj: { t: 'sector', k: o.k }, wp: [s[4], SECTOR_Y, s[6]], cm: camera });
        }
      }
      if (op.armL > 0.02) for (const a of ARMS) {
        const spots = [];
        for (let j = 2; j < a.samples.length - 2; j++) {
          const s = a.samples[j]; if (s.w < 0.3) continue;
          proj(camera, s.p.x, s.p.y, s.p.z, projV); if (!projV.ok) continue;
          if (projV.x < 140 || projV.x > W - 140 || projV.y < 100 || projV.y > H - 100) continue;
          const dc = Math.hypot(projV.x - W * 0.5, projV.y - H * 0.5);
          spots.push({ j, x: projV.x, y: projV.y, p: s.p, score: Math.abs(dc - Math.min(W, H) * 0.3) });
        }
        if (!spots.length) continue;
        spots.sort((p, q) => p.score - q.score);
        const alts = [];
        for (const sp of spots.slice(0, 24)) {
          if (alts.some(q => Math.hypot(q.x - sp.x, q.y - sp.y) < 90)) continue;
          const s2 = a.samples[Math.min(a.samples.length - 1, sp.j + 2)], s1 = a.samples[Math.max(0, sp.j - 2)];
          const p1 = proj(camera, s1.p.x, s1.p.y, s1.p.z, {}), p2 = proj(camera, s2.p.x, s2.p.y, s2.p.z, {});
          let ang = Math.atan2(p2.y - p1.y, p2.x - p1.x); if (ang > Math.PI / 2) ang -= Math.PI; if (ang < -Math.PI / 2) ang += Math.PI;
          alts.push({ x: sp.x, y: sp.y, ang, p: sp.p }); if (alts.length >= 5) break;
        }
        const name = armLabelName(a, alts[0].p);
        addCand({ key: 'a' + a.id + name, pri: 1800, x: alts[0].x, y: alts[0].y, alts, w: textW(name, ST.arm), h: ST.arm.h, mode: 'rot', ang: alts[0].ang, cls: 'lbl-arm', html: esc(name), alpha: op.armL * main * 0.95 });
      }
      // Neighbourhood: stars, distance rings, danger zones
      projectStars(op.starL * main);
      starLabels(op.starL * main);
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
          const c = dm.d.centre; proj(camera, c[0], c[1] + dm.d.radiusLy * 0.0, c[2], projV); if (!projV.ok) continue;
          addCand({ key: 'd' + dm.d.id, pri: 700, x: projV.x, y: projV.y, w: textW(dm.d.name, ST.danger), h: ST.danger.h, mode: 'center', cls: 'lbl-danger' + (dm.d.kind === 'hazard' ? ' hazard' : ''), html: esc(dm.d.name), alpha: op.starL * main * 0.95, wp: c, cm: camera });
        }
      }
    } else starsProjected = false;
    // System: body labels
    if (SY && sysBlend > 0.4) {
      const a = smooth(0.55, 0.95, sysBlend), dsys = Math.pow(10, scam.cur.ld);
      for (const n of SY.nodes) {
        if (n.kind === 3) continue;
        proj(sysCam, n.pos.x * SY.root.scale.x, 0, n.pos.z * SY.root.scale.x, projV); if (!projV.ok) continue;
        const r = Math.max(3, n.size * sysFocal() / projV.w * (n.rings ? 2.2 : 1)) * (n === SY.prim ? 1.5 : 1);
        placed.push([projV.x - r, projV.y - r * (n.rings ? 0.6 : 1), projV.x + r, projV.y + r * (n.rings ? 0.6 : 1)]);
      }
      for (const n of SY.nodes) {
        proj(sysCam, n.pos.x * SY.root.scale.x, 0, n.pos.z * SY.root.scale.x, projV); if (!projV.ok) continue;
        const isSel = n.k === sysSel, isHov = n.k === sysHov, par = SY.nodes[n.parent];
        if (n.kind === 2 && !isSel && !isHov && !(par && (par.k === sysSel || par.k === sysHov)) && dsys > 60) continue;
        const pxR = n.size * sysFocal() / projV.w * (n.rings ? 2.2 : 1);
        let pri = n.kind === 0 ? 900 : n.kind === 1 ? 600 + n.size * 10 : n.kind === 3 ? 300 : 200;
        if (isSel) pri += 2000; if (isHov) pri += 1500; if (n.home || n.station) pri += 200; if (n.land) pri += 20;
        let html, w, h, cls = 'lbl-body' + (n.kind === 2 ? ' moon' : '') + (isSel ? ' sel' : '');
        const landIc = n.land ? `<span class="ic land">${ico('land')}</span>` : '';
        const mark = n.home ? `<span class="ic" style="color:#ff9ccf">${ico('home')}</span>` : n.station ? `<span class="ic" style="color:#c6a3ff">${ico('station')}</span>` : '';
        const t1 = n.kind === 0 && n === SY.prim ? n.name : n.short;
        if (n.kind === 2) { html = `<div class="t1">${mark}${landIc}${esc(t1)}</div>`; w = textW(t1, ST.moon) + (n.land ? 16 : 0) + (mark ? 16 : 0); h = 15; }
        else {
          const t2 = n.kind === 0 ? (n.type ? `${n.type} · ${starKind(n.type)}` : 'Companion star') : n.kind === 3 ? 'Asteroid belt' : SY.schematic ? n.type : `${n.type} · ${n.g.toFixed(2)} g · ${tempC(n.t)}`;
          html = `<div class="t1">${mark}${landIc}${esc(t1)}</div><div class="t2">${esc(t2)}</div>`;
          w = Math.max(textW(t1, ST.body) + (n.land ? 16 : 0) + (mark ? 16 : 0), textW(t2, ST.body2)); h = 30;
        }
        const bx = n.kind === 3 ? projV.x : projV.x, by = projV.y;
        if (n.kind === 3) { const q = proj(sysCam, Math.cos(-0.6) * n.orbit * SY.root.scale.x, 0, Math.sin(-0.6) * n.orbit * SY.root.scale.x, {}); if (!q.ok) continue; addCand({ key: 'b' + n.k, pri, x: q.x, y: q.y, w, h, mode: 'center', cls, html, alpha: a * 0.8 }); continue; }
        addCand({ key: 'b' + SY.i + ':' + n.k, pri, x: bx, y: by, w, h, off: Math.max(8, pxR + 5), cls, html, alpha: a, obj: { t: 'body', k: n.k }, wp: [n.pos.x * SY.root.scale.x, 0, n.pos.z * SY.root.scale.x], cm: sysCam });
      }
    }
    flushLabels(now);
    for (const [k, rec] of LB) if (rec.seen === frameNo && k[0] === 's') labelledLast.add(+k.slice(1));
    updateHUD();
    if (!initDone) { initDone = true; finishInit(); }
    requestAnimationFrame(frame);
  }

  // ---------- init ----------
  function finishInit() {
    $('#loading').classList.add('done');
    const lt = performance.now() - t0;
    console.log(`[map] ready in ${lt.toFixed(0)} ms · ${N} local systems · ${GS.count} galaxy points`);
    setTimeout(() => { window.__ready = true; }, 300);
  }
  function applyParams() {
    INSTANT_NOW = true;
    const g = k => params.get(k);
    const findStar = n => { if (!n) return -1; const i = byName.get(n.toLowerCase()); return i === undefined ? -1 : i; };
    if (g('filters')) { g('filters').split(',').forEach(f => { if (FILTERS.some(q => q.id === f)) activeFilters.add(f); }); applyFilters(); }
    if (g('top') === '1') { topView = true; ui.top.classList.add('on'); }
    const lvl = g('level');
    const s = findStar(g('select')), r = findStar(g('route'));
    if (lvl === 'region') { cam.goal.ld = LOG.region; cam.goal.pitch = 0.86; }
    else if (lvl === 'neighbourhood' || lvl === 'nbr') { cam.goal.ld = LOG.nbr; cam.goal.pitch = 0.62; }
    else if (lvl === 'system') { cam.goal.ld = LOG.nbr; }
    if (g('yaw')) cam.goal.yaw = +g('yaw') * Math.PI / 180;
    if (g('pitch')) cam.goal.pitch = clamp(+g('pitch') * Math.PI / 180, PITCH_MIN, PITCH_MAX);
    if (g('dist')) cam.goal.ld = clamp(log10(+g('dist')), LOG.min, LOG.max);
    if (topView) cam.goal.pitch = PITCH_MAX;
    const f = findStar(g('focus')); if (f >= 0) cam.goal.a.copy(starPos(f));
    if (g('sector')) { const k = SECTORS.findIndex(q => q[0].toLowerCase() === g('sector').toLowerCase()); if (k >= 0) { select({ t: 'sector', k }, { keepCam: true }); if (k) cam.goal.a.set(SECTORS[k][4], 0, SECTORS[k][6]); } }
    if (g('struct')) select({ t: 'struct', id: g('struct') }, { keepCam: true });
    if (s >= 0) select({ t: 'star', i: s }, { keepCam: true });
    if (r >= 0) { if (s < 0) select({ t: 'star', i: r }, { keepCam: true }); planTo(r); }
    if (lvl === 'system') {
      const i = s >= 0 ? s : r >= 0 ? r : ship.sys;
      let body; if (g('body') && L.bodies[String(i)]) { const k = L.bodies[String(i)].findIndex(b => b[0].toLowerCase() === g('body').toLowerCase()); if (k >= 0) body = k; }
      enterSystem(i, { body });
    }
    snapCam(cam); snapCam(scam);
    if (g('auto')) { startAutopilot(); }
    if (g('hover')) {
      const i = findStar(g('hover'));
      if (i >= 0) setTimeout(() => { placeCamera(); prepVP(camera); proj(camera, PX[i], PY[i], PZ[i], projV); mouse.x = projV.x; mouse.y = projV.y; setHover({ t: 'star', i }); }, 200);
    }
    if (g('hoversector')) { const k = SECTORS.findIndex(q => q[0].toLowerCase() === g('hoversector').toLowerCase()); if (k >= 0) setTimeout(() => { placeCamera(); prepVP(camera); proj(camera, SECTORS[k][4], SECTOR_Y, SECTORS[k][6], projV); mouse.x = projV.x; mouse.y = projV.y; setHover({ t: 'sector', k }); }, 200); }
    if (params.toString()) hideHint();
    INSTANT_NOW = false;
  }

  resize();
  renderChips(); renderShip(); renderInspector();
  applyFilters();
  applyParams();
  window.__app = { cam, scam, ship, planRoute, enterSystem, exitSystem, select, goLevel, setHover, mouse, SYS, byName, get route() { return route; }, get level() { return levelOf(); }, get mode() { return mode; }, flyTo, starPos, toggleTop,
    screenOf: n => { const i = byName.get(n.toLowerCase()); prepVP(camera); proj(camera, PX[i], PY[i], PZ[i], projV); return [Math.round(projV.x), Math.round(projV.y)]; } };
  const go = () => requestAnimationFrame(frame);
  if (document.fonts && document.fonts.load) Promise.race([document.fonts.load('500 12px Inter'), new Promise(r => setTimeout(r, 1500))]).then(() => { widthCache.clear(); go(); });
  else go();
}
})();
