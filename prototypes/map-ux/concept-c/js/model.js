/* Reach map (concept C): the data model. Grid names, reach, routes and search. Nothing here draws. */
'use strict';
window.RM = window.RM || {};

(function () {
  const L = window.HOLLOW_LOCAL, G = window.HOLLOW_GALAXY, MK = window.HOLLOW_MOCK;
  const S = L.systems;
  const N = S.x.length;
  const X = new Float64Array(S.x), Y = new Float64Array(S.y), Z = new Float64Array(S.z);
  const NAME = S.name, CLS = S.cls, COL = S.col, MAG = S.mag, CAT = S.cat, SCOOP = S.scoop, SUM = S.sum;
  const JUMP = MK.ship.jumpRangeLy;
  const TANK = MK.ship.fuelJumps;
  const RES_NAMES = MK.resources.names;
  const RES_MASK = MK.resources.systemMask;

  // ---------------------------------------------------------------- star descriptions
  const HUE = { O: 'Blue', B: 'Blue-white', A: 'White', F: 'Yellow-white', G: 'Yellow', K: 'Orange', M: 'Red' };
  function starKind(cls) {
    if (/^Black hole/.test(cls)) return 'Black hole';
    if (/^Neutron/.test(cls)) return 'Neutron star';
    if (/^Wolf/.test(cls)) return 'Wolf–Rayet star';
    if (/^Carbon/.test(cls)) return 'Carbon star';
    if (/^T Tauri/.test(cls)) return 'Young T Tauri star';
    if (/^D/.test(cls)) return 'White dwarf';
    if (/^[LTY]\d/.test(cls)) return 'Brown dwarf';
    const hue = HUE[cls[0]] || '';
    const lum = (/\s(Ia|Ib|III|II|IV|I|V)$/.exec(cls) || [])[1];
    if (lum === 'V') return cls[0] === 'M' || cls[0] === 'K' ? hue + ' dwarf' : hue + ' star';
    if (lum === 'IV') return hue + ' subgiant';
    if (lum === 'III') return hue + ' giant';
    if (lum) return hue + ' supergiant';
    return 'Star';
  }

  // ---------------------------------------------------------------- the atlas grid
  // 25 x 25 squares of 12 ly, lettered A-Y left to right and numbered 1-25 from the Galactic-Centre side.
  // Sol sits in the middle of M13, so the chart covers the whole 150 ly neighbourhood.
  const CELL = 12, COLS = 25, HALF = CELL * COLS / 2;
  const LETTERS = 'ABCDEFGHIJKLMNOPQRSTUVWXY';
  function cellIndex(x, z) {
    const i = Math.floor((x + HALF) / CELL), j = Math.floor((HALF - z) / CELL);
    if (i < 0 || i >= COLS || j < 0 || j >= COLS) return null;
    return { i, j, name: LETTERS[i] + (j + 1) };
  }
  function cellName(x, z) { const c = cellIndex(x, z); return c ? c.name : null; }
  function cellCentre(i, j) { return [-HALF + (i + 0.5) * CELL, HALF - (j + 0.5) * CELL]; }
  function parseCell(text) {
    const m = /^\s*([A-Ya-y])\s*-?\s*(\d{1,2})\s*$/.exec(text);
    if (!m) return null;
    const i = LETTERS.indexOf(m[1].toUpperCase()), j = parseInt(m[2], 10) - 1;
    if (i < 0 || j < 0 || j >= COLS) return null;
    return { i, j, name: LETTERS[i] + (j + 1) };
  }

  // ---------------------------------------------------------------- mock layer, indexed by system
  const stationsBySys = new Map();
  for (const st of MK.stations) {
    if (!stationsBySys.has(st.sys)) stationsBySys.set(st.sys, []);
    stationsBySys.get(st.sys).push(st);
  }
  const basesBySys = new Map();
  const bases = [];
  {
    const h = MK.home;
    bases.push({ kind: 'home', relation: 'home', label: 'Your base', handle: 'You', sys: h.sys, body: h.body, bodyType: h.bodyType });
    for (const p of MK.players) {
      bases.push({ kind: 'player', relation: p.relation, label: p.handle + '’s base', handle: p.handle, sys: p.sys, body: p.body, bodyType: p.bodyType });
    }
    for (const b of bases) {
      if (!basesBySys.has(b.sys)) basesBySys.set(b.sys, []);
      basesBySys.get(b.sys).push(b);
    }
  }
  const DANGER = MK.danger;
  const STORM = DANGER.findIndex(d => d.kind === 'hazard');
  const dangerOf = new Int8Array(N).fill(-1);
  for (let s = 0; s < N; s++) {
    for (let d = 0; d < DANGER.length; d++) {
      const c = DANGER[d].centre;
      const dx = X[s] - c[0], dy = Y[s] - c[1], dz = Z[s] - c[2];
      if (dx * dx + dy * dy + dz * dz <= DANGER[d].radiusLy * DANGER[d].radiusLy) { dangerOf[s] = d; break; }
    }
  }
  const inStorm = s => dangerOf[s] === STORM && STORM >= 0;
  const canScoop = s => SCOOP[s] === 1 && !inStorm(s);
  const refuels = s => canScoop(s) || stationsBySys.has(s);
  const jumpCost = s => (inStorm(s) ? 2 : 1);
  const landable = s => SUM[s][3];

  // Resource groups for pips and finders (bits from HOLLOW_MOCK.resources.names).
  const bit = name => 1 << RES_NAMES.indexOf(name);
  const RES_GROUPS = [
    { id: 'water', label: 'Water & ice', mask: bit('water') | bit('ice'), colour: '#bfe6ff' },
    { id: 'metals', label: 'Metals', mask: bit('iron') | bit('nickel'), colour: '#d8a27a' },
    { id: 'organics', label: 'Organics', mask: bit('organics'), colour: '#93d88b' },
    { id: 'rare', label: 'Rare metals', mask: bit('rare metals'), colour: '#efcf6a' },
    { id: 'volatiles', label: 'Volatiles', mask: bit('volatiles'), colour: '#c3a6ff' },
    { id: 'silicates', label: 'Silicates', mask: bit('silicates'), colour: '#b7b0a6' },
  ];
  function resourceGroups(s) {
    const m = RES_MASK[s];
    return RES_GROUPS.filter(g => m & g.mask);
  }

  // ---------------------------------------------------------------- spatial index (cells of one jump)
  const GS = JUMP, GO = 162.5, GD = Math.ceil((2 * GO) / GS);
  const gi = v => Math.min(GD - 1, Math.max(0, Math.floor((v + GO) / GS)));
  const cellStart = new Int32Array(GD * GD * GD + 1);
  const cellItems = new Int32Array(N);
  {
    const cellOf = new Int32Array(N);
    for (let s = 0; s < N; s++) { cellOf[s] = (gi(X[s]) * GD + gi(Y[s])) * GD + gi(Z[s]); cellStart[cellOf[s] + 1]++; }
    for (let c = 0; c < GD * GD * GD; c++) cellStart[c + 1] += cellStart[c];
    const fill = cellStart.slice(0, GD * GD * GD);
    for (let s = 0; s < N; s++) cellItems[fill[cellOf[s]]++] = s;
  }
  const nbCache = new Array(N);
  const R2 = JUMP * JUMP;
  function neighbours(s) {
    let out = nbCache[s];
    if (out) return out;
    const tmp = [];
    const ci = gi(X[s]), cj = gi(Y[s]), ck = gi(Z[s]);
    for (let a = Math.max(0, ci - 1); a <= Math.min(GD - 1, ci + 1); a++)
      for (let b = Math.max(0, cj - 1); b <= Math.min(GD - 1, cj + 1); b++)
        for (let c = Math.max(0, ck - 1); c <= Math.min(GD - 1, ck + 1); c++) {
          const cell = (a * GD + b) * GD + c;
          for (let k = cellStart[cell]; k < cellStart[cell + 1]; k++) {
            const t = cellItems[k];
            if (t === s) continue;
            const dx = X[t] - X[s], dy = Y[t] - Y[s], dz = Z[t] - Z[s];
            if (dx * dx + dy * dy + dz * dz <= R2) tmp.push(t);
          }
        }
    out = nbCache[s] = Int32Array.from(tmp);
    return out;
  }
  const dist = (a, b) => Math.hypot(X[a] - X[b], Y[a] - Y[b], Z[a] - Z[b]);

  // ---------------------------------------------------------------- reach from a system
  // hops: fewest jumps to every system (graph of 12.5 ly hops). cost: fuel needed without refuelling,
  // up to the fuel in the tank (a jump into the ion storm costs two). -1 means "not on this tank".
  function computeReach(start, fuel) {
    const hops = new Int16Array(N).fill(-1);
    const queue = new Int32Array(N);
    let head = 0, tail = 0;
    hops[start] = 0; queue[tail++] = start;
    while (head < tail) {
      const s = queue[head++], h = hops[s] + 1;
      const nb = neighbours(s);
      for (let k = 0; k < nb.length; k++) { const t = nb[k]; if (hops[t] < 0) { hops[t] = h; queue[tail++] = t; } }
    }
    const cost = new Int8Array(N).fill(-1);
    const buckets = [];
    for (let c = 0; c <= fuel; c++) buckets.push([]);
    cost[start] = 0; buckets[0].push(start);
    for (let c = 0; c <= fuel; c++) {
      for (let k = 0; k < buckets[c].length; k++) {
        const s = buckets[c][k];
        if (cost[s] !== c) continue;
        const nb = neighbours(s);
        for (let m = 0; m < nb.length; m++) {
          const t = nb[m], ct = c + jumpCost(t);
          if (ct > fuel) continue;
          if (cost[t] < 0 || ct < cost[t]) { cost[t] = ct; buckets[ct].push(t); }
        }
      }
    }
    let inTank = 0;
    for (let s = 0; s < N; s++) if (cost[s] >= 0) inTank++;
    return { start, fuel, hops, cost, inTank };
  }

  // Distance fields for the rings (1, 2, 3 jumps) and the tank's reach, as an RGBA byte texture.
  // Each channel is the signed distance (ly) to the union of 12.5 ly discs around the systems that can make
  // the last jump, clamped to +-FIELD.range. The map draws the zero contour, so the rings follow real stars.
  const FIELD = { min: -172, res: 0.5, size: 688, range: 8 };
  function rasterReach(reach) {
    const n = FIELD.size, cells = n * n, range = FIELD.range, res = FIELD.res, min = FIELD.min;
    const ch = [0, 1, 2, 3].map(() => new Float32Array(cells).fill(range));
    const rad = JUMP, reachR = rad + range;
    for (let s = 0; s < N; s++) {
      const h = reach.hops[s], c = reach.cost[s];
      const f0 = h === 0, f1 = h >= 0 && h <= 1, f2 = h >= 0 && h <= 2, f3 = c >= 0 && c <= reach.fuel - 1;
      if (!(f0 || f1 || f2 || f3)) continue;
      const cx = X[s], cz = Z[s];
      const ix0 = Math.max(0, Math.floor((cx - reachR - min) / res)), ix1 = Math.min(n - 1, Math.ceil((cx + reachR - min) / res));
      const iz0 = Math.max(0, Math.floor((cz - reachR - min) / res)), iz1 = Math.min(n - 1, Math.ceil((cz + reachR - min) / res));
      for (let iz = iz0; iz <= iz1; iz++) {
        const dz = min + (iz + 0.5) * res - cz, dz2 = dz * dz, row = iz * n;
        for (let ix = ix0; ix <= ix1; ix++) {
          const dx = min + (ix + 0.5) * res - cx;
          const d2 = dx * dx + dz2;
          if (d2 > reachR * reachR) continue;
          const d = Math.sqrt(d2) - rad, k = row + ix;
          if (f0 && d < ch[0][k]) ch[0][k] = d;
          if (f1 && d < ch[1][k]) ch[1][k] = d;
          if (f2 && d < ch[2][k]) ch[2][k] = d;
          if (f3 && d < ch[3][k]) ch[3][k] = d;
        }
      }
    }
    // soften the scallops of the outer rings a little (two box passes of 1.5 ly)
    const tmp = new Float32Array(cells), rad2 = 3;
    for (const c of [1, 2, 3]) {
      const src = ch[c];
      for (let pass = 0; pass < 2; pass++) {
        for (let iz = 0; iz < n; iz++) {
          const row = iz * n;
          for (let ix = 0; ix < n; ix++) {
            let acc = 0, cnt = 0;
            for (let k = -rad2; k <= rad2; k++) { const x = ix + k; if (x >= 0 && x < n) { acc += src[row + x]; cnt++; } }
            tmp[row + ix] = acc / cnt;
          }
        }
        for (let iz = 0; iz < n; iz++) {
          for (let ix = 0; ix < n; ix++) {
            let acc = 0, cnt = 0;
            for (let k = -rad2; k <= rad2; k++) { const z = iz + k; if (z >= 0 && z < n) { acc += tmp[z * n + ix]; cnt++; } }
            src[iz * n + ix] = acc / cnt;
          }
        }
      }
    }
    const data = new Uint8Array(cells * 4);
    const scale = 255 / (2 * range);
    for (let k = 0; k < cells; k++) {
      for (let c = 0; c < 4; c++) {
        const v = (ch[c][k] + range) * scale;
        data[k * 4 + c] = v < 0 ? 0 : v > 255 ? 255 : v + 0.5;
      }
    }
    return { data, ch };
  }
  // Where a ring crosses the line from (x, z) toward +Z (up the screen): used to place ring labels.
  function fieldCrossing(ch, x, z, dirX, dirZ) {
    const n = FIELD.size;
    let inside = false;
    for (let t = 0; t < 200; t += 0.25) {
      const px = x + dirX * t, pz = z + dirZ * t;
      const ix = Math.floor((px - FIELD.min) / FIELD.res), iz = Math.floor((pz - FIELD.min) / FIELD.res);
      if (ix < 0 || iz < 0 || ix >= n || iz >= n) return null;
      const v = ch[iz * n + ix];
      if (v < 0) inside = true;
      else if (inside) return [px, pz];
    }
    return null;
  }

  // ---------------------------------------------------------------- routes
  // A* over (system, fuel) states: fewest jumps first, then shortest distance; avoids hostile space when it
  // can; the tank refills at scoopable stars (not inside the ion storm) and at stations.
  function Heap() { this.k = []; this.v = []; }
  Heap.prototype.push = function (key, val) {
    const k = this.k, v = this.v; let i = k.length; k.push(key); v.push(val);
    while (i > 0) { const p = (i - 1) >> 1; if (k[p] <= key) break; k[i] = k[p]; v[i] = v[p]; i = p; }
    k[i] = key; v[i] = val;
  };
  Heap.prototype.pop = function () {
    const k = this.k, v = this.v, top = v[0], lk = k.pop(), lv = v.pop(), n = k.length;
    if (n > 0) {
      let i = 0;
      for (;;) {
        let c = 2 * i + 1; if (c >= n) break;
        if (c + 1 < n && k[c + 1] < k[c]) c++;
        if (k[c] >= lk) break;
        k[i] = k[c]; v[i] = v[c]; i = c;
      }
      k[i] = lk; v[i] = lv;
    }
    return top;
  };
  Object.defineProperty(Heap.prototype, 'size', { get() { return this.k.length; } });

  function planRoute(start, goal, fuel0) {
    if (start === goal || start < 0 || goal < 0) return null;
    const TK = TANK + 1;
    const g = new Float64Array(N * TK).fill(Infinity);
    const prev = new Int32Array(N * TK).fill(-1);
    const closed = new Uint8Array(N * TK);
    const heap = new Heap();
    const h = s => Math.max(0, Math.ceil(dist(s, goal) / JUMP - 1e-9)) * 1.0;
    const s0 = start * TK + fuel0;
    g[s0] = 0; heap.push(h(start), s0);
    let found = -1, guard = 0;
    while (heap.size && guard++ < 400000) {
      const st = heap.pop();
      if (closed[st]) continue;
      closed[st] = 1;
      const s = (st / TK) | 0, f = st % TK;
      if (s === goal) { found = st; break; }
      const nb = neighbours(s);
      for (let k = 0; k < nb.length; k++) {
        const t = nb[k];
        let ft = f - jumpCost(t);
        if (ft < 0) continue;
        if (refuels(t)) ft = TANK;
        const d = dist(s, t);
        let c = 1 + 0.004 * d;
        if (t !== goal && dangerOf[t] >= 0) c += dangerOf[t] === STORM ? 0.6 : 4;
        const nt = t * TK + ft, ng = g[st] + c;
        if (ng < g[nt]) { g[nt] = ng; prev[nt] = st; heap.push(ng + h(t), nt); }
      }
    }
    if (found < 0) return null;
    const path = [];
    for (let st = found; st >= 0; st = prev[st]) path.push((st / TK) | 0);
    path.reverse();
    return describeRoute(path, fuel0);
  }

  // Turns a path into stops: legs, planned refuels (only where the next refuel point is out of reach),
  // stations, danger and an ETA (mock timings: 40 s per jump, 50 s to scoop or refuel).
  function describeRoute(path, fuel0) {
    const stops = [];
    let fuel = fuel0, total = 0, refuelCount = 0;
    for (let i = 0; i < path.length; i++) {
      const s = path[i];
      const leg = i ? dist(path[i - 1], s) : 0;
      total += leg;
      if (i) fuel -= jumpCost(s);
      let refuel = false;
      if (i > 0 && i < path.length - 1 && refuels(s)) {
        let need = 0, ok = false;
        for (let j = i + 1; j < path.length; j++) {
          need += jumpCost(path[j]);
          if (need > fuel) break;
          if (j === path.length - 1 || refuels(path[j])) { ok = true; break; }
        }
        if (!ok) { refuel = true; fuel = TANK; refuelCount++; }
      }
      // the autopilot tops up where the trip ends, if it can
      if (i === path.length - 1 && i > 0 && refuels(s)) fuel = TANK;
      stops.push({ s, leg, total, fuel, refuel, station: stationsBySys.get(s) || null, bases: basesBySys.get(s) || null, danger: dangerOf[s] >= 0 ? DANGER[dangerOf[s]] : null });
    }
    const jumps = path.length - 1;
    const seconds = jumps * 40 + refuelCount * 50;
    return { path, stops, jumps, ly: total, refuels: refuelCount, seconds, from: path[0], to: path[path.length - 1] };
  }

  // ---------------------------------------------------------------- sectors and arms
  const SECTORS = G.sectors.list.map(r => ({ name: r[0], sx: r[1], sy: r[2], sz: r[3], x: r[4], y: r[5], z: r[6] }));
  const SECTOR_SIZE = G.sectors.sizeLy;
  const sectorByKey = new Map(SECTORS.map(s => [s.sx + ',' + s.sy + ',' + s.sz, s]));
  const SOL_SECTOR = SECTORS[0];
  function sectorAt(x, y, z) {
    const sx = SOL_SECTOR.sx + Math.round(x / SECTOR_SIZE), sy = SOL_SECTOR.sy + Math.round(y / SECTOR_SIZE), sz = SOL_SECTOR.sz + Math.round(z / SECTOR_SIZE);
    return sectorByKey.get(sx + ',' + sy + ',' + sz) || null;
  }
  const ARMS = G.armPaths.map(a => ({ id: a.id, name: a.name, line: a.line }));
  const STRUCTURES = G.structures;
  const GC = G.meta.galacticCentre;
  function regionAt(x, z) {
    const r = Math.hypot(x - GC[0], z - GC[2]);
    if (r < 1500) return 'Galactic Centre';
    if (r < 9000) return 'Galactic Bar';
    let best = null, bestD = 2600;
    for (const a of ARMS) {
      const ln = a.line;
      for (let k = 0; k + 1 < ln.length; k++) {
        if (ln[k][3] < 0.3) continue;
        const ax = ln[k][0], az = ln[k][2], bx = ln[k + 1][0], bz = ln[k + 1][2];
        const vx = bx - ax, vz = bz - az, l2 = vx * vx + vz * vz;
        const t = Math.max(0, Math.min(1, ((x - ax) * vx + (z - az) * vz) / l2));
        const d = Math.hypot(x - ax - vx * t, z - az - vz * t);
        if (d < bestD) { bestD = d; best = a.name; }
      }
    }
    if (best) return best.replace('Norma–Outer Arm', r < 25000 ? 'Norma Arm' : 'Outer Arm');
    return r > 52000 ? 'Beyond the disk' : 'Inter-arm gap';
  }

  // ---------------------------------------------------------------- labels: who deserves a name first
  const DESIGNATION = /^(HD|HIP|HR|GJ|Gliese|Gl|LHS|LP|LTT|BD|CD|CPD|NLTT|L|G|2MASS|WISE|SCR|TYC|UGPS|DENIS|LSPM|PM|UCAC|APMPM|LEHPM|Wolf|Ross|Luyten|Stephenson|SO|SIPS|ULAS|CFBDS|PSO|VVV)\b/;
  const tier = new Int8Array(N);
  for (let s = 0; s < N; s++) tier[s] = CAT[s] ? (DESIGNATION.test(NAME[s]) ? 1 : 2) : 0;
  function labelOrder(reach) {
    const score = new Float32Array(N);
    for (let s = 0; s < N; s++) {
      const m = Math.min(MAG[s], 20);
      let v = tier[s] === 2 ? 600 - m * 12 : tier[s] === 1 ? 260 - m * 6 : 40 - m * 1.5;
      const h = reach.hops[s];
      if (reach.cost[s] >= 0) v += 120;
      if (h >= 0 && h <= 3) v += 140 - h * 25;
      if (stationsBySys.has(s)) v += 1500;
      if (basesBySys.has(s)) v += 1700;
      score[s] = v;
    }
    const order = new Int32Array(N);
    for (let s = 0; s < N; s++) order[s] = s;
    order.sort((a, b) => score[b] - score[a]);
    return order;
  }

  // ---------------------------------------------------------------- search
  const lowerNames = NAME.map(n => n.toLowerCase());
  function search(query, ship, limit) {
    limit = limit || 8;
    const q = query.trim().toLowerCase();
    if (!q) return [];
    const out = [];
    const cell = parseCell(q);
    if (cell) out.push({ kind: 'cell', cell, rank: -1 });
    for (const st of MK.stations) {
      const n = st.name.toLowerCase();
      if (n.includes(q)) out.push({ kind: 'station', station: st, s: st.sys, rank: n.startsWith(q) ? 0 : 2 });
    }
    for (const b of bases) {
      const n = (b.label + ' ' + b.handle).toLowerCase();
      if (n.includes(q) || (q === 'home' && b.kind === 'home')) out.push({ kind: 'base', base: b, s: b.sys, rank: 0 });
    }
    for (const sec of SECTORS) {
      if (sec.sy !== SOL_SECTOR.sy) continue;
      const n = sec.name.toLowerCase();
      if (n.startsWith(q) && q.length >= 2) out.push({ kind: 'sector', sector: sec, rank: 1 });
    }
    for (const st of STRUCTURES) {
      if (st.name.toLowerCase().includes(q) && q.length >= 3) out.push({ kind: 'structure', structure: st, rank: 1 });
    }
    const sys = [];
    for (let s = 0; s < N; s++) {
      const n = lowerNames[s];
      const at = n.indexOf(q);
      if (at < 0) continue;
      const rank = n === q ? 0 : at === 0 ? 1 : n[at - 1] === ' ' ? 2 : 3;
      sys.push({ kind: 'system', s, rank, d: ship >= 0 ? dist(ship, s) : 0 });
    }
    sys.sort((a, b) => a.rank - b.rank || tier[b.s] - tier[a.s] || a.d - b.d);
    for (let k = 0; k < Math.min(sys.length, limit); k++) out.push(sys[k]);
    out.sort((a, b) => a.rank - b.rank);
    return out.slice(0, limit);
  }

  // Finders: nearest systems (by jumps) that match a category.
  const CATEGORIES = [
    { id: 'stations', label: 'Stations', test: s => stationsBySys.has(s) },
    { id: 'landable', label: 'Landable worlds', test: s => landable(s) > 0 },
    { id: 'water', label: 'Water & ice', test: s => (RES_MASK[s] & RES_GROUPS[0].mask) !== 0 },
    { id: 'metals', label: 'Metals', test: s => (RES_MASK[s] & RES_GROUPS[1].mask) !== 0 },
    { id: 'organics', label: 'Organics', test: s => (RES_MASK[s] & RES_GROUPS[2].mask) !== 0 },
    { id: 'rare', label: 'Rare metals', test: s => (RES_MASK[s] & RES_GROUPS[3].mask) !== 0 },
  ];
  function nearest(categoryId, reach, limit) {
    const cat = CATEGORIES.find(c => c.id === categoryId);
    if (!cat) return [];
    const list = [];
    for (let s = 0; s < N; s++) {
      if (!cat.test(s)) continue;
      const h = reach.hops[s];
      if (h < 0) continue;
      list.push({ s, h, d: dist(reach.start, s) });
    }
    list.sort((a, b) => a.h - b.h || a.d - b.d);
    let inTank = 0;
    for (const r of list) if (reach.cost[r.s] >= 0) inTank++;
    return { cat, count: list.length, inTank, list: list.slice(0, limit || 6), matches: list };
  }

  // ---------------------------------------------------------------- galaxy sample
  function decodeGalaxy() {
    const bytes = b64 => Uint8Array.from(atob(b64), c => c.charCodeAt(0));
    const St = G.stars;
    return {
      count: St.count,
      pos: new Float32Array(bytes(St.pos).buffer),
      col: bytes(St.col), lvl: bytes(St.lvl), comp: bytes(St.comp), bright: bytes(St.bright),
    };
  }
  function decodeGlow() {
    const bytes = b64 => Uint8Array.from(atob(b64), c => c.charCodeAt(0));
    const g = G.glow;
    return { size: g.size, cellLy: g.cellLy, x0: g.x0, z0: g.z0, all: bytes(g.all.data), young: bytes(g.young.data) };
  }

  RM.model = {
    N, X, Y, Z, NAME, CLS, COL, MAG, CAT, SCOOP, SUM, BODIES: L.bodies, BODY_FIELDS: L.meta.bodyFields,
    JUMP, TANK, MOCK: MK, GALAXY: G, SHIP: MK.ship, HOME: MK.home,
    starKind, tier,
    CELL, COLS, HALF, LETTERS, cellIndex, cellName, cellCentre, parseCell,
    stationsBySys, basesBySys, bases, DANGER, STORM, dangerOf, inStorm, canScoop, refuels, jumpCost, landable,
    RES_GROUPS, RES_MASK, resourceGroups,
    neighbours, dist, computeReach, FIELD, rasterReach, fieldCrossing,
    planRoute, describeRoute,
    SECTORS, SECTOR_SIZE, SOL_SECTOR, sectorAt, ARMS, STRUCTURES, GC, regionAt,
    labelOrder, search, CATEGORIES, nearest, decodeGalaxy, decodeGlow,
  };
})();
