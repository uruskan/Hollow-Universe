/* Hollow Universe map, concept Known: the data layer (forked from concept A "Search & Go").
   Indexes, search, plain-language intents and the route planner. No rendering, no three.js.
   Changes from concept A, following the lore (nothing is still alive anywhere):
   - no stations: fuel stars (scoopable, HOLLOW_LOCAL scoop = 1) are the only refuelling points;
   - no raiders: the only hazard left is the natural ion storm;
   - intents and search answer from what you know (window.HOLLOW_KNOWLEDGE, read lazily: knowledge.js loads after this
     file because it uses the route planner to lay out your past trips). Names stay searchable: catalogue and generated
     names are public, so search finds any star, and the card says how little is known about it. */
(function () {
  'use strict';
  const L = window.HOLLOW_LOCAL, G = window.HOLLOW_GALAXY, M = window.HOLLOW_MOCK;
  const S = L.systems, N = S.name.length;
  const X = new Float32Array(S.x), Y = new Float32Array(S.y), Z = new Float32Array(S.z);
  const MAG = new Float32Array(S.mag), SUM = S.sum, CLS = S.cls, CAT = S.cat, SCOOP = S.scoop;
  const NAME = S.name;
  const RANGE = M.ship.jumpRangeLy, FUEL_MAX = M.ship.fuelJumps;
  const GC = G.meta.galacticCentre;

  // ---------- helpers ----------
  const fmt = new Intl.NumberFormat('en-GB');
  function plural(n, w, p) { return n + ' ' + (n === 1 ? w : (p || w + 's')); }
  function ly(d) {
    if (d < 0.05) return 'here';
    if (d < 10) return d.toFixed(1) + ' ly';
    if (d < 100000) return fmt.format(Math.round(d)) + ' ly';
    return fmt.format(Math.round(d / 1000)) + 'k ly';
  }
  function norm(s) {
    return s.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/[–—]/g, '-').replace(/[^a-z0-9*+\- ]/g, '').replace(/\s+/g, ' ').trim();
  }
  function compact(s) { return s.replace(/[^a-z0-9*]/g, ''); }
  function dist3(i, p) { const dx = X[i] - p[0], dy = Y[i] - p[1], dz = Z[i] - p[2]; return Math.sqrt(dx * dx + dy * dy + dz * dz); }
  function distIJ(i, j) { const dx = X[i] - X[j], dy = Y[i] - Y[j], dz = Z[i] - Z[j]; return Math.sqrt(dx * dx + dy * dy + dz * dz); }
  function pos(i) { return [X[i], Y[i], Z[i]]; }

  // ---------- star words ----------
  function starKind(cls) {
    if (cls === 'Black hole' || cls === 'Neutron star' || cls === 'Carbon star') return cls;
    if (cls.indexOf('Wolf') === 0) return 'Wolf–Rayet star';
    const c = cls[0];
    if (c === 'D') return 'White dwarf';
    if (c === 'L' || c === 'T' || c === 'Y') return 'Brown dwarf';
    const lum = (cls.match(/\s(I{1,3}|IV|V)$/) || [])[1] || 'V';
    const word = { O: 'Blue', B: 'Blue-white', A: 'White', F: 'Yellow-white', G: 'Yellow', K: 'Orange', M: 'Red' }[c] || 'Star';
    if (lum === 'I') return word + ' supergiant';
    if (lum === 'II' || lum === 'III') return word + ' giant';
    if (lum === 'IV') return word + ' subgiant';
    return { O: 'Blue star', B: 'Blue-white star', A: 'White star', F: 'Yellow-white star', G: 'Yellow dwarf', K: 'Orange dwarf', M: 'Red dwarf' }[c] || 'Star';
  }
  const isProper = new Uint8Array(N);
  for (let i = 0; i < N; i++) if (CAT[i] === 1 && !/\d/.test(NAME[i])) isProper[i] = 1;

  // ---------- mock layer indexes ----------
  // Knowledge (knowledge.js, MOCK): 0 unknown, 1 seen, 2 scanned by a drone, 3 visited, 4 your base. Read lazily.
  const KN = () => window.HOLLOW_KNOWLEDGE || null;
  const lvl = i => { const k = KN(); return k ? k.level[i] : 4; };
  const stationsBySys = new Map();   // concept Known: no stations anywhere (kept empty so callers need no special case)
  const playersBySys = new Map();
  for (const p of M.players) { if (!playersBySys.has(p.sys)) playersBySys.set(p.sys, []); playersBySys.get(p.sys).push(p); }
  const foundPlayer = i => { const k = KN(); return playersBySys.has(i) && (!k || k.playerFound(i)); };
  const HOME = M.home.sys;
  // Only natural hazards remain (the mock "raider" zone is gone: nobody is left to raid anyone).
  const DANGER = M.danger.filter(d => d.kind === 'hazard');
  const dangerOf = new Int8Array(N).fill(-1);
  const stormy = new Uint8Array(N);
  DANGER.forEach((d, k) => {
    for (let i = 0; i < N; i++) {
      if (dist3(i, d.centre) <= d.radiusLy) { dangerOf[i] = k; stormy[i] = 1; }
    }
  });
  // Fuel stars: a scoopable star outside the storm. They are the only refuelling points.
  const canRefuel = new Uint8Array(N);
  for (let i = 0; i < N; i++) canRefuel[i] = (SCOOP[i] === 1 && !stormy[i]) ? 1 : 0;
  const RES = M.resources.names;
  const resMask = M.resources.systemMask;
  const resByBody = new Map();
  for (const [sys, b, m] of M.resources.bodies) resByBody.set(sys + ':' + b, m);
  function resNames(mask) { const out = []; for (let k = 0; k < RES.length; k++) if (mask & (1 << k)) out.push(RES[k]); return out; }

  // ---------- spatial grid and jump graph ----------
  const OFF = 160, CELL = RANGE, DIM = Math.ceil((2 * OFF) / CELL) + 1;
  const cellIdx = (x, y, z) => ((Math.floor((x + OFF) / CELL) * DIM + Math.floor((y + OFF) / CELL)) * DIM + Math.floor((z + OFF) / CELL));
  const cellStart = new Int32Array(DIM * DIM * DIM + 1), cellItems = new Int32Array(N);
  {
    const c = new Int32Array(N);
    for (let i = 0; i < N; i++) { c[i] = cellIdx(X[i], Y[i], Z[i]); cellStart[c[i] + 1]++; }
    for (let k = 0; k < DIM * DIM * DIM; k++) cellStart[k + 1] += cellStart[k];
    const fill = cellStart.slice(0, DIM * DIM * DIM);
    for (let i = 0; i < N; i++) cellItems[fill[c[i]]++] = i;
  }
  function near(p, r, cb) { // calls cb(i, d) for systems within r of p
    const r2 = r * r;
    const x0 = Math.max(0, Math.floor((p[0] - r + OFF) / CELL)), x1 = Math.min(DIM - 1, Math.floor((p[0] + r + OFF) / CELL));
    const y0 = Math.max(0, Math.floor((p[1] - r + OFF) / CELL)), y1 = Math.min(DIM - 1, Math.floor((p[1] + r + OFF) / CELL));
    const z0 = Math.max(0, Math.floor((p[2] - r + OFF) / CELL)), z1 = Math.min(DIM - 1, Math.floor((p[2] + r + OFF) / CELL));
    for (let a = x0; a <= x1; a++) for (let b = y0; b <= y1; b++) for (let c = z0; c <= z1; c++) {
      const k = (a * DIM + b) * DIM + c;
      for (let q = cellStart[k]; q < cellStart[k + 1]; q++) {
        const i = cellItems[q], dx = X[i] - p[0], dy = Y[i] - p[1], dz = Z[i] - p[2], d2 = dx * dx + dy * dy + dz * dz;
        if (d2 <= r2) cb(i, Math.sqrt(d2));
      }
    }
  }
  // CSR adjacency: every pair within jump range
  const adjStart = new Int32Array(N + 1);
  let adj;
  {
    const tmp = [];
    const R2 = RANGE * RANGE + 1e-6;
    for (let i = 0; i < N; i++) {
      adjStart[i] = tmp.length;
      const ci = Math.floor((X[i] + OFF) / CELL), cj = Math.floor((Y[i] + OFF) / CELL), ck = Math.floor((Z[i] + OFF) / CELL);
      for (let a = Math.max(0, ci - 1); a <= Math.min(DIM - 1, ci + 1); a++)
        for (let b = Math.max(0, cj - 1); b <= Math.min(DIM - 1, cj + 1); b++)
          for (let c = Math.max(0, ck - 1); c <= Math.min(DIM - 1, ck + 1); c++) {
            const k = (a * DIM + b) * DIM + c;
            for (let q = cellStart[k]; q < cellStart[k + 1]; q++) {
              const j = cellItems[q];
              if (j === i) continue;
              const dx = X[i] - X[j], dy = Y[i] - Y[j], dz = Z[i] - Z[j];
              if (dx * dx + dy * dy + dz * dz <= R2) tmp.push(j);
            }
          }
    }
    adjStart[N] = tmp.length;
    adj = Int32Array.from(tmp);
  }

  function hopsFrom(src) {
    const h = new Int16Array(N).fill(-1), q = new Int32Array(N);
    let qh = 0, qt = 0; h[src] = 0; q[qt++] = src;
    while (qh < qt) {
      const u = q[qh++];
      for (let e = adjStart[u]; e < adjStart[u + 1]; e++) { const v = adj[e]; if (h[v] < 0) { h[v] = h[u] + 1; q[qt++] = v; } }
    }
    return h;
  }

  // ---------- route planner: A* over (system, fuel) ----------
  function Heap() { this.p = new Float64Array(1024); this.v = new Int32Array(1024); this.n = 0; }
  Heap.prototype.push = function (pr, val) {
    if (this.n === this.p.length) { const p = new Float64Array(this.n * 2); p.set(this.p); this.p = p; const v = new Int32Array(this.n * 2); v.set(this.v); this.v = v; }
    let i = this.n++; const P = this.p, V = this.v;
    while (i > 0) { const par = (i - 1) >> 1; if (P[par] <= pr) break; P[i] = P[par]; V[i] = V[par]; i = par; }
    P[i] = pr; V[i] = val;
  };
  Heap.prototype.pop = function () {
    const P = this.p, V = this.v, top = V[0], n = --this.n;
    if (n > 0) {
      const pr = P[n], val = V[n]; let i = 0;
      for (;;) { let c = 2 * i + 1; if (c >= n) break; if (c + 1 < n && P[c + 1] < P[c]) c++; if (P[c] >= pr) break; P[i] = P[c]; V[i] = V[c]; i = c; }
      P[i] = pr; V[i] = val;
    }
    return top;
  };

  const JUMP_S = 28, REFUEL_S = 40;
  function planRoute(from, to, opts) {
    opts = opts || {};
    const F = FUEL_MAX + 1, startFuel = opts.fuel == null ? FUEL_MAX : opts.fuel, avoid = !!opts.avoid;
    if (from === to) return null;
    const NS = N * F;
    const g = new Float64Array(NS).fill(Infinity), prev = new Int32Array(NS).fill(-1), act = new Uint8Array(NS), closed = new Uint8Array(NS);
    const tx = X[to], ty = Y[to], tz = Z[to];
    const h = u => { const dx = X[u] - tx, dy = Y[u] - ty, dz = Z[u] - tz; return Math.ceil(Math.sqrt(dx * dx + dy * dy + dz * dz) / RANGE - 1e-7) * 100; };
    const heap = new Heap();
    const s0 = from * F + startFuel; g[s0] = 0; heap.push(h(from), s0);
    let goal = -1, expanded = 0;
    while (heap.n) {
      const s = heap.pop();
      if (closed[s]) continue; closed[s] = 1; expanded++;
      const u = (s / F) | 0, f = s - u * F, gs = g[s];
      if (u === to) { goal = s; break; }
      if (f < FUEL_MAX && canRefuel[u]) {
        const t = u * F + FUEL_MAX, c = gs + 1 + (dangerOf[u] >= 0 ? 3 : 0);
        if (c < g[t]) { g[t] = c; prev[t] = s; act[t] = 1; heap.push(c + h(u) + h(u) * 1e-4, t); }
      }
      for (let e = adjStart[u]; e < adjStart[u + 1]; e++) {
        const v = adj[e], cost = (stormy[u] || stormy[v]) ? 2 : 1;
        if (f < cost) continue;
        const t = v * F + f - cost;
        if (closed[t]) continue;
        const c = gs + 100 + (dangerOf[v] >= 0 ? (avoid ? 5000 : 0.5) : 0) + distIJ(u, v) * 0.001;
        if (c < g[t]) { g[t] = c; prev[t] = s; act[t] = 0; const hv = h(v); heap.push(c + hv + hv * 1e-4, t); }
      }
    }
    if (goal < 0) return { ok: false, expanded };
    // rebuild
    const chain = [];
    for (let s = goal; s >= 0; s = prev[s]) chain.push(s);
    chain.reverse();
    const stops = [];
    for (let k = 0; k < chain.length; k++) {
      const s = chain[k], u = (s / F) | 0, f = s - u * F;
      if (act[s] === 1 && k > 0) { stops[stops.length - 1].refuel = true; stops[stops.length - 1].fuelOut = f; continue; }
      stops.push({ sys: u, fuelIn: f, fuelOut: f, refuel: false, danger: dangerOf[u] });
    }
    let total = 0;
    for (let k = 1; k < stops.length; k++) { stops[k].hop = distIJ(stops[k - 1].sys, stops[k].sys); total += stops[k].hop; }
    const jumps = stops.length - 1, refuels = stops.filter(s => s.refuel).length;
    const dangers = [];
    stops.forEach((s, k) => { if (k > 0 && s.danger >= 0) { const d = dangers.find(x => x.k === s.danger); if (d) d.count++; else dangers.push({ k: s.danger, region: DANGER[s.danger], count: 1 }); } });
    const seconds = jumps * JUMP_S + refuels * REFUEL_S;
    return { ok: true, from, to, stops, jumps, refuels, dangers, lengthLy: total, straightLy: distIJ(from, to), seconds, avoid, expanded };
  }
  function minutes(sec) { const m = Math.max(1, Math.round(sec / 60)); return m < 60 ? '~' + m + ' min' : '~' + Math.floor(m / 60) + ' h ' + (m % 60) + ' min'; }

  // ---------- the ship ----------
  const ship = { sys: M.ship.sys, fuel: M.ship.fuel, name: M.ship.name };
  let hops = hopsFrom(ship.sys);
  function setShip(sys, fuel) { ship.sys = sys; ship.fuel = fuel; hops = hopsFrom(sys); }
  function shipPos() { return pos(ship.sys); }
  function dShip(i) { return distIJ(i, ship.sys); }
  function jumpsTo(i) { return hops[i]; }
  function jumpsText(i) { const h = hops[i]; if (h < 0) return 'out of range'; if (h === 0) return 'you are here'; return plural(h, 'jump'); }

  // ---------- "what's there" ----------
  function worldsLine(i, short) {
    const s = SUM[i], parts = [];
    if (s[1] === 0) parts.push(s[9] ? 'Belts only' : 'No planets');
    else parts.push(plural(s[1], 'planet'));
    if (s[3]) parts.push(s[3] + ' landable');
    if (!short) {
      if (s[6]) parts.push(s[6] === 1 ? 'Earth-like world' : s[6] + ' Earth-like');
      else if (s[7]) parts.push(s[7] === 1 ? 'water world' : s[7] + ' water worlds');
      else if (s[8]) parts.push(s[8] === 1 ? 'ringed planet' : s[8] + ' ringed');
    }
    return parts.join(' · ');
  }
  const LEVEL_WORD = ['Unknown', 'Seen', 'Scanned', 'Visited', 'Yours'];
  function whatsThere(i) {
    const bits = [], k = KN(), l = lvl(i);
    if (i === HOME) bits.push('Your home base');
    else if (k && k.baseAt(i)) bits.push(k.baseAt(i).name);
    if (foundPlayer(i)) bits.push(playersBySys.get(i)[0].name);
    const f = k ? k.findsAt(i) : null; if (f && f.length) bits.push(f[0].title);
    if (dangerOf[i] >= 0) bits.push(DANGER[dangerOf[i]].name);
    if (l === 0) { bits.push(CAT[i] ? 'Unknown · catalogue star' : 'Unknown system'); return bits.slice(0, 3).join(' · '); }
    if (l === 1) { bits.push('Seen · ' + starKind(CLS[i])); return bits.slice(0, 3).join(' · '); }
    bits.push(starKind(CLS[i]));
    bits.push(worldsLine(i, bits.length > 2));
    return bits.slice(0, 3).join(' · ');
  }

  // ---------- search ----------
  const normNames = NAME.map(norm), compactNames = normNames.map(compact);
  const structures = G.structures.map(s => {
    const p = s.label;
    const sub = {
      centre: 'Supermassive black hole at the Galactic Centre',
      bar: 'The bar of old stars across the core',
      arm: 'Spiral arm', spur: 'Our local spur · the Sun lies in it'
    }[s.kind];
    const names = [s.name];
    if (s.kind === 'centre') names.unshift('Sagittarius A*'), names.push('Sgr A*', 'Galactic Center', 'Core', 'Centre');
    if (s.id === 'bar') names.push('Bar');
    return { type: 'structure', id: s.id, name: s.kind === 'centre' ? 'Sagittarius A*' : s.name, kind: s.kind, aliases: names.map(norm), pos: p, sub, note: s.note };
  });
  const sectors = G.sectors.list.map(r => ({ type: 'sector', name: r[0], norm: norm(r[0]), pos: [r[4], r[5], r[6]], cube: [r[1], r[2], r[3]] }));

  function scoreName(nq, cq, n, c) {
    if (!nq) return 0;
    if (n === nq) return 1000;
    if (n.startsWith(nq)) return 820 - n.length * 0.6;
    if (cq.length >= 2 && c.startsWith(cq)) return 760 - n.length * 0.6;
    const w = n.indexOf(' ' + nq);
    if (w >= 0) return 640 - w - n.length * 0.4;
    if (n.indexOf(nq) >= 0) return 420 - n.length * 0.4;
    if (cq.length >= 3 && c.indexOf(cq) >= 0) return 380 - n.length * 0.4;
    return 0;
  }

  // ---------- intents ----------
  function parseRadius(q, dflt) {
    const m = q.match(/(\d+(?:\.\d+)?)\s*(ly|light|lightyears|light-years|light years)?/);
    if (m && /within|under|less|<|ly|light|near/.test(q)) return Math.min(150, Math.max(5, parseFloat(m[1])));
    return dflt;
  }
  function nearestWhere(pred, exclude) {
    let best = -1, bd = Infinity; const sp = shipPos();
    // expanding radius search over the grid
    for (let r = 15; r <= 320 && best < 0; r *= 2) {
      near(sp, r, (i, d) => { if (i !== exclude && pred(i) && d < bd) { bd = d; best = i; } });
    }
    return best;
  }
  function listWhere(pred, radius, cap) {
    const out = [];
    near(shipPos(), radius, (i, d) => { if (pred(i)) out.push([i, d]); });
    out.sort((a, b) => a[1] - b[1]);
    return out.slice(0, cap || 400).map(a => a[0]);
  }
  const SUMF = { landable: 3, gas: 4, ice: 5, earth: 6, water: 7, ringed: 8, belts: 9 };
  function svc(st) { return st.map(s => s.services.filter(x => x !== 'refuel').join(', ')).join(' / '); }

  // Every intent answers from what you know: fuel by sight (a star's class shows whether it can be scooped), worlds only
  // where a drone or a visit has looked (level 2 and up).
  const seenUp = j => lvl(j) >= 1, scannedUp = j => lvl(j) >= 2;
  const INTENTS = [
    { id: 'fuel', icon: 'fuel', title: 'Nearest fuel star', keys: ['fuel', 'refuel', 'scoop', 'gas', 'tank', 'nearest fuel', 'fuel star'],
      run() { const i = nearestWhere(j => canRefuel[j] && seenUp(j), ship.sys); return { single: i, why: 'Scoopable ' + starKind(CLS[i]).toLowerCase() }; } },
    { id: 'home', icon: 'home', title: 'Home', keys: ['home', 'base', 'my base', 'go home'],
      run() { return { single: HOME, why: M.home.body + ' · ' + M.home.bodyType }; } },
    { id: 'finds', icon: 'ruin', title: 'Finds', keys: ['find', 'finds', 'monument', 'monuments', 'ruin', 'ruins', 'wreck', 'derelict', 'report', 'reports', 'discover'], radius: 150,
      run() { const k = KN(); const l = k ? [...new Set(k.finds.filter(f => !f.hidden).map(f => f.sys))].sort((a, b) => dShip(a) - dShip(b)) : []; return { list: l, title: 'What your drones found', unit: j => k.findsAt(j).map(f => f.title).join(', ') }; } },
    { id: 'landable', icon: 'land', title: 'Landable worlds nearby', keys: ['landable', 'land', 'landing', 'surface', 'planet', 'worlds'], radius: 25,
      run(r) { const l = listWhere(j => SUM[j][3] >= 3 && scannedUp(j), r).sort((a, b) => (SUM[b][3] - SUM[a][3]) || (dShip(a) - dShip(b))).slice(0, 60); return { list: l, title: 'Landable worlds nearby', unit: j => plural(SUM[j][3], 'landable world') }; } },
    { id: 'earth', icon: 'earth', title: 'Earth-like worlds', keys: ['earth', 'earth-like', 'earthlike', 'habitable', 'garden', 'breathable', 'terra'], radius: 150,
      run(r) { const l = listWhere(j => SUM[j][6] > 0 && j !== 0 && scannedUp(j), r); return { list: l, title: 'Earth-like worlds', unit: j => plural(SUM[j][6], 'Earth-like world') }; } },
    { id: 'water', icon: 'water', title: 'Water worlds', keys: ['water', 'ocean', 'sea', 'water world'], radius: 150,
      run(r) { const l = listWhere(j => SUM[j][7] > 0 && scannedUp(j), r); return { list: l, title: 'Water worlds', unit: j => plural(SUM[j][7], 'water world') }; } },
    { id: 'ringed', icon: 'ring', title: 'Ringed planets within 50 ly', keys: ['ring', 'rings', 'ringed'], radius: 50,
      run(r) { const l = listWhere(j => SUM[j][8] > 0 && scannedUp(j), r); return { list: l, title: 'Ringed planets within ' + r + ' ly', unit: j => plural(SUM[j][8], 'ringed planet') }; } },
    { id: 'gas', icon: 'giant', title: 'Gas giants nearby', keys: ['gas giant', 'giants', 'jupiter'], radius: 30,
      run(r) { const l = listWhere(j => SUM[j][4] > 0 && scannedUp(j), r); return { list: l, title: 'Gas giants within ' + r + ' ly', unit: j => plural(SUM[j][4], 'gas giant') }; } },
    { id: 'wd', icon: 'star', title: 'White dwarfs nearby', keys: ['white dwarf', 'dwarf', 'remnant'], radius: 50,
      run(r) { const l = listWhere(j => CLS[j][0] === 'D' && seenUp(j), r); return { list: l, title: 'White dwarfs within ' + r + ' ly', unit: j => CLS[j] }; } },
    { id: 'bright', icon: 'star', title: 'Brightest stars you know', keys: ['bright', 'brightest', 'giant star', 'big star', 'seen'], radius: 150,
      run(r) { const l = listWhere(j => MAG[j] < 2.5 && seenUp(j), r).sort((a, b) => MAG[a] - MAG[b]).slice(0, 40); return { list: l, title: 'Brightest stars you know', unit: j => CLS[j] + ' · ' + LEVEL_WORD[lvl(j)].toLowerCase() }; } },
    { id: 'players', icon: 'flag', title: 'Player bases', keys: ['player', 'players', 'friend', 'friends', 'bases', 'others', 'people'], radius: 150,
      run() { const l = M.players.map(p => p.sys).filter(foundPlayer).sort((a, b) => dShip(a) - dShip(b)); return { list: l, title: 'Player bases your drones found', unit: j => { const p = playersBySys.get(j)[0]; return p.handle + ' · ' + p.relation; } }; } },
    { id: 'danger', icon: 'warn', title: 'Hazards', keys: ['danger', 'dangerous', 'hazard', 'hazards', 'storm', 'ion', 'safe'], radius: 150,
      run() { const l = DANGER.map(d => d.sys); return { list: l, title: 'Hazards', unit: j => DANGER[dangerOf[j]].name, danger: true }; } }
  ];
  RES.forEach((r, k) => {
    INTENTS.push({ id: 'res-' + k, icon: 'gem', title: r[0].toUpperCase() + r.slice(1) + ' deposits nearby', keys: [r, r + ' deposit', 'mine ' + r, 'mining ' + r], radius: 30, mock: true,
      run(rad) { const l = listWhere(j => (resMask[j] & (1 << k)) && scannedUp(j), rad).slice(0, 80); return { list: l, title: r[0].toUpperCase() + r.slice(1) + ' within ' + rad + ' ly', unit: () => r + ' (mock)' }; } });
  });
  const intentById = Object.fromEntries(INTENTS.map(t => [t.id, t]));

  function intentPreview(t, radius) {
    const res = t.run(radius);
    if (res.single != null) {
      const i = res.single;
      return { res, sub: NAME[i] + ' · ' + res.why, dist: dShip(i), jumps: hops[i], sys: i };
    }
    const l = res.list;
    if (!l.length) return { res, sub: 'None within ' + radius + ' ly', dist: null };
    const first = l[0];
    const title = t.id === 'ringed' || t.id === 'gas' || t.id === 'wd' || t.id === 'bright' || t.id === 'finds' || t.mock ? '' : (radius < 150 ? ' within ' + radius + ' ly' : '');
    return { res, sub: plural(l.length, 'system') + title + ' · closest ' + NAME[first], dist: dShip(first), jumps: hops[first], sys: first };
  }

  function search(q) {
    const nq = norm(q), cq = compact(nq);
    const out = { intents: [], places: [] };
    if (!nq) return out;
    // intents
    const scored = [];
    for (const t of INTENTS) {
      let best = 0;
      const tn = norm(t.title);
      if (tn.startsWith(nq)) best = 900;
      else if (tn.indexOf(nq) >= 0 && nq.length >= 3) best = 700;
      for (const k of t.keys) {
        if (nq === k) best = Math.max(best, 950);
        else if (nq.indexOf(k) >= 0 && k.length >= 3) best = Math.max(best, 850 + k.length);
        else if (k.startsWith(nq) && nq.length >= 2) best = Math.max(best, 600 + nq.length * 20);
      }
      if (/^(near|close|closest)/.test(nq) && /^(fuel|finds)$/.test(t.id)) best = Math.max(best, 500);
      if (best) scored.push([best, t]);
    }
    scored.sort((a, b) => b[0] - a[0]);
    for (const [, t] of scored.slice(0, 3)) {
      const radius = parseRadius(nq, t.radius || 150);
      const pv = intentPreview(t, radius);
      const title = t.id === 'ringed' ? 'Ringed planets within ' + radius + ' ly' : t.title;
      out.intents.push({ type: 'intent', id: t.id, icon: t.icon, name: title, sub: pv.sub, dist: pv.dist, jumps: pv.jumps, radius, mock: t.mock });
    }
    // places
    const cand = [];
    for (const s of structures) {
      let sc = 0; for (const a of s.aliases) sc = Math.max(sc, scoreName(nq, cq, a, compact(a)));
      if (sc) cand.push([sc + 120, s]);
    }
    for (const p of M.players) {
      if (!foundPlayer(p.sys)) continue;   // other players' bases are known only once a drone has found them
      const sc = Math.max(scoreName(nq, cq, norm(p.handle), compact(norm(p.handle))), scoreName(nq, cq, norm(p.name), compact(norm(p.name))));
      if (sc) cand.push([sc + 80, { type: 'base', name: p.name, sys: p.sys, player: p }]);
    }
    const kn = KN();
    if (kn) {
      for (const g of kn.groups) {
        if (g.kind === 'route') continue;
        const sc = scoreName(nq, cq, norm(g.name), compact(norm(g.name)));
        if (sc) cand.push([sc + 70, { type: 'group', name: g.name, group: g }]);
      }
      for (const b of kn.bases) {
        if (b.sys === HOME) continue;
        const sc = scoreName(nq, cq, norm(b.name), compact(norm(b.name)));
        if (sc) cand.push([sc + 90, { type: 'outpost', name: b.name, sys: b.sys, base: b }]);
      }
      for (const f of kn.finds) {
        if (f.hidden) continue;
        const sc = scoreName(nq, cq, norm(f.title), compact(norm(f.title)));
        if (sc) cand.push([sc + 60, { type: 'find', name: f.title, sys: f.sys, find: f }]);
      }
    }
    let secHit = 0;
    for (const s of sectors) {
      const sc = scoreName(nq, cq, s.norm, compact(s.norm));
      if (sc && secHit < 3) { cand.push([sc + 60 - secHit * 30, s]); secHit++; }
    }
    if (nq.length >= 1) {
      const sp = shipPos();
      const sysHits = [];
      for (let i = 0; i < N; i++) {
        const n = normNames[i];
        if (n.charCodeAt(0) !== nq.charCodeAt(0) && n.indexOf(nq) < 0 && (cq.length < 3 || compactNames[i].indexOf(cq) < 0)) continue;
        let sc = scoreName(nq, cq, n, compactNames[i]);
        if (!sc) continue;
        sc += isProper[i] ? 70 : CAT[i] ? 30 : 0;
        sc += lvl(i) * 12;                       // what you know ranks first
        if (i === HOME) sc += 120;
        sc -= dist3(i, sp) * 0.35;
        sc -= (MAG[i] > 12 ? 10 : 0);
        sysHits.push([sc, i]);
      }
      sysHits.sort((a, b) => b[0] - a[0]);
      for (const [sc, i] of sysHits.slice(0, 8)) cand.push([sc, { type: 'system', name: NAME[i], sys: i }]);
    }
    cand.sort((a, b) => b[0] - a[0]);
    const seen = new Set();
    for (const [, c] of cand) {
      const key = c.type + ':' + (c.sys != null ? c.sys + c.name : c.name);
      if (seen.has(key)) continue; seen.add(key);
      out.places.push(describePlace(c));
      if (out.places.length >= 7) break;
    }
    if (out.places.length && out.intents.length && out.places[0].name && norm(out.places[0].name) === nq) out.placesFirst = true;
    return out;
  }

  function describePlace(c) {
    if (c.type === 'system') return Object.assign({ icon: c.sys === HOME ? 'home' : 'star', sub: whatsThere(c.sys), dist: dShip(c.sys), jumps: hops[c.sys], col: S.col[c.sys], level: lvl(c.sys) }, c);
    if (c.type === 'base') return Object.assign({ icon: 'flag', sub: cap(c.player.relation) + ' player base · ' + NAME[c.sys], dist: dShip(c.sys), jumps: hops[c.sys] }, c);
    if (c.type === 'outpost') return Object.assign({ icon: 'outpost', sub: 'Your outpost · ' + NAME[c.sys], dist: dShip(c.sys), jumps: hops[c.sys] }, c);
    if (c.type === 'find') return Object.assign({ icon: c.find.kind === 'monument' ? 'ruin' : c.find.kind === 'resource' ? 'gem' : c.find.kind === 'fuel' ? 'fuel' : 'flag', sub: c.find.sub + ' · ' + NAME[c.sys], dist: dShip(c.sys), jumps: hops[c.sys] }, c);
    if (c.type === 'group') { const g = c.group, d = Math.hypot(g.centre[0] - X[ship.sys], g.centre[1] - Y[ship.sys], g.centre[2] - Z[ship.sys]); return Object.assign({ icon: g.kind === 'home' ? 'home' : 'drone', sub: g.summary(), dist: d, jumps: null }, c); }
    if (c.type === 'structure') { const d = Math.hypot(c.pos[0], c.pos[1], c.pos[2]); return Object.assign({ icon: c.kind === 'centre' ? 'core' : 'arm', dist: d, jumps: null, far: true }, c); }
    if (c.type === 'sector') { const d = Math.hypot(c.pos[0], c.pos[1], c.pos[2]); return Object.assign({ icon: 'sector', sub: c.name === 'Nolcas Peris' ? 'Sector · you are in it · 1,024 ly cube' : 'Sector · 1,024 ly cube', dist: d, jumps: null, far: d > 160 }, c); }
    return c;
  }
  function cap(s) { return s[0].toUpperCase() + s.slice(1); }

  window.HD = {
    L, G, M, N, X, Y, Z, MAG, SUM, CLS, CAT, SCOOP, NAME, COL: S.col, TEFF: S.teff, LVL: S.lvl,
    RANGE, FUEL_MAX, GC, HOME, ship, setShip, shipPos, dShip, jumpsTo, jumpsText, hopsFrom,
    stationsBySys, playersBySys, foundPlayer, DANGER, dangerOf, stormy, canRefuel, isProper, RES, resMask, resByBody, resNames, LEVEL_WORD, lvl,
    starKind, worldsLine, whatsThere, plural, ly, norm, pos, dist3, distIJ, near, minutes,
    planRoute, search, describePlace, INTENTS, intentById, intentPreview, structures, sectors, svc, cap,
    bodies: L.bodies, bodyFields: L.meta.bodyFields
  };
})();
