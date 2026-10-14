/* Hollow Universe map, concept Transit: the network behind the diagram.
 *
 * Draws nothing. From the real star data (37,409 systems, real 12.5 ly jumps) and the mock layers it builds:
 *   stations  the systems that matter: home, outposts, contested systems, faction capitals and a few members,
 *             (mock) stations and player bases near you, plus fuel stops that split long lanes;
 *   lanes     a sparse network between stations, each lane a real chain of jumps (every hop <= 12.5 ly);
 *   layout    an octilinear diagram: each lane keeps its real direction snapped to 0/45/90 degrees and a length
 *             that grows with its jumps; lanes that close a loop get one bend;
 *   lines     lanes paired into straight runs of one territory colour (Y = your space, K/T/O = factions,
 *             L = open space), with codes like a metro's line numbers;
 *   zones     fare-zone areas: an octagon around each territory's stations, the frontier ring, the contested
 *             overlap, danger areas;
 *   plan()    a journey planner that rides the network (walk to a station, ride lines, change, walk off) and
 *             returns a route in the same shape as data.js's planner, so the itinerary and autopilot work as is.
 * Engine-free: no DOM, no three.js. Deterministic: same data, same diagram.
 *
 * Diagram coordinates are light-years on the plane y = Y[home], with home at the origin: p = [x, z] means
 * world (X[home] + x, Y[home], Z[home] + z).
 */
(function () {
  'use strict';
  window.HollowTransit = { build };

  const S2 = Math.SQRT1_2;
  const DIRS = [[1, 0], [S2, S2], [0, 1], [-S2, S2], [-1, 0], [-S2, -S2], [0, -1], [S2, -S2]];
  const ZONE_ORDER = ['you', 'karth', 'tessaly', 'orrin'];
  const CODE = { you: 'Y', karth: 'K', tessaly: 'T', orrin: 'O', open: 'L' };

  function build({ HD, M, TD }) {
    const t0 = (typeof performance !== 'undefined' ? performance : Date).now();
    const X = HD.X, Y = HD.Y, Z = HD.Z, NAME = HD.NAME, RANGE = HD.RANGE, FMAX = HD.FUEL_MAX;
    const HOME = M.home.sys;
    const d3 = (i, j) => Math.hypot(X[i] - X[j], Y[i] - Y[j], Z[i] - Z[j]);
    const NET_R = 84;                       // stations and bases this close to home join the network

    // ---------- territory lookups (mock) ----------
    const factions = TD ? TD.factions : [];
    const owner = new Map();
    factions.forEach(f => f.systems.forEach(s => owner.set(s.sys, f.id)));
    const claim = new Map();
    if (TD) TD.contested.forEach(c => claim.set(c.sys, c.claimants));
    const zoneOfSys = sys => claim.has(sys) ? 'contested' : owner.get(sys) || 'open';

    // ---------- 1. stations ----------
    const stations = [], bySys = new Map();
    function add(sys, kind) {
      let s = bySys.get(sys);
      if (!s) { s = { id: stations.length, sys, kinds: [], zone: zoneOfSys(sys), p: null, edges: [] }; bySys.set(sys, s); stations.push(s); }
      if (!s.kinds.includes(kind)) s.kinds.push(kind);
      return s;
    }
    add(HOME, 'home');
    for (const f of factions) if (f.id === 'you') for (const o of f.systems) if (o.role === 'outpost') add(o.sys, 'outpost');
    if (TD) TD.contested.forEach(c => add(c.sys, 'contested'));
    for (const f of factions) {
      if (f.id === 'you') continue;
      add(f.capital, 'capital');
      const picked = [f.capital];   // three more members, spread out, so each zone has a shape and a line
      for (let k = 0; k < 3; k++) {
        let best = -1, bd = -1;
        for (const o of f.systems) {
          if (picked.includes(o.sys)) continue;
          let m = Infinity; for (const p of picked) m = Math.min(m, d3(o.sys, p));
          if (m > bd) { bd = m; best = o.sys; }
        }
        if (best >= 0) { picked.push(best); add(best, 'member'); }
      }
    }
    for (const st of M.stations) if (d3(st.sys, HOME) <= NET_R) add(st.sys, 'station');
    for (const p of M.players) if (d3(p.sys, HOME) <= NET_R + 12) add(p.sys, 'base');
    const nBFS = stations.length;

    // ---------- 2. jumps between stations (breadth-first over the real jump graph) ----------
    const hops = stations.map(s => HD.hopsFrom(s.sys));
    const hopsOf = (a, b) => hops[a][stations[b].sys];
    const W = stations.map((sa, a) => stations.map((sb, b) => {
      if (a === b) return 0;
      const h = hopsOf(a, b);
      return h < 0 ? Infinity : h + 0.04 * d3(sa.sys, sb.sys) / RANGE;   // jumps first, then distance
    }));

    // ---------- 3. lanes: a relative-neighbourhood graph in jumps ----------
    const MAXH = 9;
    let cand = [];
    for (let a = 0; a < nBFS; a++) for (let b = a + 1; b < nBFS; b++) {
      const wab = W[a][b]; if (!(wab < MAXH + 1)) continue;
      let keep = true;
      for (let c = 0; c < nBFS && keep; c++) if (c !== a && c !== b && Math.max(W[a][c], W[c][b]) < wab - 1e-9) keep = false;
      if (keep) cand.push({ a, b, h: hopsOf(a, b), w: wab });
    }
    // keep the network in one piece: join components through their cheapest pair
    {
      const comp = stations.map((s, k) => k);
      const find = k => { while (comp[k] !== k) k = comp[k] = comp[comp[k]]; return k; };
      cand.forEach(e => { comp[find(e.a)] = find(e.b); });
      for (;;) {
        const roots = new Set(stations.slice(0, nBFS).map(s => find(s.id)));
        if (roots.size <= 1) break;
        let best = null;
        for (let a = 0; a < nBFS; a++) for (let b = 0; b < nBFS; b++) if (find(a) !== find(b) && W[a][b] < Infinity && (!best || W[a][b] < best.w)) best = { a, b, h: hopsOf(a, b), w: W[a][b] };
        if (!best) break;
        cand.push(best); comp[find(best.a)] = find(best.b);
      }
    }

    // each lane follows a real chain of jumps: step to a neighbour one jump nearer the far end, close to the
    // straight line, preferring stars you can refuel at and avoiding danger
    function segDist(v, p0, p1) {
      const ax = p1[0] - p0[0], ay = p1[1] - p0[1], az = p1[2] - p0[2], l2 = ax * ax + ay * ay + az * az || 1;
      const t = Math.max(0, Math.min(1, ((X[v] - p0[0]) * ax + (Y[v] - p0[1]) * ay + (Z[v] - p0[2]) * az) / l2));
      return Math.hypot(X[v] - p0[0] - ax * t, Y[v] - p0[1] - ay * t, Z[v] - p0[2] - az * t);
    }
    function chain(from, to, hTo) {
      if (hTo[from] < 0) return null;
      const out = [from], p0 = [X[from], Y[from], Z[from]], p1 = [X[to], Y[to], Z[to]];
      let u = from, guard = 0;
      while (u !== to && guard++ < 80) {
        const hu = hTo[u]; let best = -1, bs = Infinity;
        HD.near([X[u], Y[u], Z[u]], RANGE, v => {
          if (hTo[v] !== hu - 1) return;
          const s = segDist(v, p0, p1) + (HD.canRefuel[v] ? 0 : 4) + (HD.dangerOf[v] >= 0 ? 8 : 0);
          if (s < bs || (s === bs && v < best)) { bs = s; best = v; }
        });
        if (best < 0) return null;
        out.push(best); u = best;
      }
      return u === to ? out : null;
    }
    let lanes = [];
    for (const e of cand) {
      const c = chain(stations[e.a].sys, stations[e.b].sys, hops[e.b]);
      if (c) lanes.push({ a: e.a, b: e.b, h: c.length - 1, w: e.w, chain: c });
    }

    // fuel stops: long lanes get a station half-way (the tank holds 6 jumps; ion storms double the cost)
    const split = [];
    for (const e of lanes) {
      if (e.h < 5) { split.push(e); continue; }
      const mid = Math.round(e.h / 2);
      let m = mid, bs = Infinity;
      for (let k = Math.max(1, mid - 1); k <= Math.min(e.h - 1, mid + 1); k++) {
        const v = e.chain[k], s = Math.abs(k - mid) + (HD.canRefuel[v] ? 0 : 5) + (HD.dangerOf[v] >= 0 ? 5 : 0) - (HD.isProper[v] ? 0.6 : HD.CAT[v] ? 0.3 : 0);
        if (s < bs) { bs = s; m = k; }
      }
      const fs = add(e.chain[m], bySys.has(e.chain[m]) ? 'station' : 'fuel');
      split.push({ a: e.a, b: fs.id, h: m, w: m, chain: e.chain.slice(0, m + 1) });
      split.push({ a: fs.id, b: e.b, h: e.h - m, w: e.h - m, chain: e.chain.slice(m) });
    }
    lanes = split;
    // the same pair twice (two long lanes sharing a fuel stop): keep the shorter
    {
      const seen = new Map();
      lanes = lanes.filter(e => {
        const k = Math.min(e.a, e.b) + ':' + Math.max(e.a, e.b), o = seen.get(k);
        if (o && o.h <= e.h) return false;
        seen.set(k, e); return true;
      });
      lanes = [...new Set(seen.values())];
    }
    lanes.forEach((e, k) => { e.id = k; e.danger = e.chain.some(v => HD.dangerOf[v] >= 0); e.storm = e.chain.some(v => HD.stormy[v]); });

    // ---------- 4. the octilinear layout ----------
    const L_OF = h => Math.max(10, 7 * h);
    const DMIN = 8.6, DEDGE = 4;
    const segs = [];                                   // placed segments: [x1, z1, x2, z2, laneId]
    const ports = stations.map(() => new Uint8Array(8));
    const root = bySys.get(HOME);
    root.p = [0, 0];
    const adjL = stations.map(() => []);
    lanes.forEach(e => { adjL[e.a].push(e); adjL[e.b].push(e); });
    const realDir = (a, b) => [X[stations[b].sys] - X[stations[a].sys], Z[stations[b].sys] - Z[stations[a].sys]];
    const angDist = (x, y) => { let d = Math.abs(x - y) % (2 * Math.PI); return d > Math.PI ? 2 * Math.PI - d : d; };
    function segSeg(p, q, r, s) {   // proper crossing of segments pq and rs
      const o = (a, b, c) => (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]);
      const d1 = o(p, q, r), d2 = o(p, q, s), d3_ = o(r, s, p), d4 = o(r, s, q);
      return ((d1 > 1e-9 && d2 < -1e-9) || (d1 < -1e-9 && d2 > 1e-9)) && ((d3_ > 1e-9 && d4 < -1e-9) || (d3_ < -1e-9 && d4 > 1e-9));
    }
    function ptSeg(p, a, b) {
      const ax = b[0] - a[0], az = b[1] - a[1], l2 = ax * ax + az * az || 1;
      const t = Math.max(0, Math.min(1, ((p[0] - a[0]) * ax + (p[1] - a[1]) * az) / l2));
      return Math.hypot(p[0] - a[0] - ax * t, p[1] - a[1] - az * t);
    }
    function pathCost(pts, ends) {   // collisions of a candidate polyline with what is already placed
      let bad = 0;
      for (let k = 0; k + 1 < pts.length; k++) {
        const p = pts[k], q = pts[k + 1];
        for (const s of segs) {
          const r = [s[0], s[1]], t = [s[2], s[3]];
          if (segSeg(p, q, r, t)) bad += 1;
        }
        for (const st of stations) if (st.p && !ends.includes(st.id) && ptSeg(st.p, p, q) < DEDGE) bad += 1;
      }
      return bad;
    }
    function nearStation(q, skip) { let m = Infinity; for (const st of stations) if (st.p && st.id !== skip) m = Math.min(m, Math.hypot(st.p[0] - q[0], st.p[1] - q[1])); return m; }
    function nearSeg(q) { let m = Infinity; for (const s of segs) m = Math.min(m, ptSeg(q, [s[0], s[1]], [s[2], s[3]])); return m; }
    const dirIndex = (dx, dz) => ((Math.round(Math.atan2(dz, dx) / (Math.PI / 4)) % 8) + 8) % 8;
    function placeSeg(e, pts) {
      for (let k = 0; k + 1 < pts.length; k++) segs.push([pts[k][0], pts[k][1], pts[k + 1][0], pts[k + 1][1], e.id]);
      const sa = stations[e.a].p === pts[0] ? e.a : e.b, sb = sa === e.a ? e.b : e.a;
      ports[sa][dirIndex(pts[1][0] - pts[0][0], pts[1][1] - pts[0][1])] = 1;
      const n = pts.length;
      ports[sb][dirIndex(pts[n - 2][0] - pts[n - 1][0], pts[n - 2][1] - pts[n - 1][1])] = 1;
    }
    // grow a tree from home, cheapest lane first (Prim), placing each new station along the snapped real direction
    const inTree = new Set();
    const placedLane = new Set();
    {
      const frontier = [];
      const pushFrom = a => adjL[a].forEach(e => { const b = e.a === a ? e.b : e.a; if (!stations[b].p) frontier.push(e); });
      pushFrom(root.id);
      while (frontier.length) {
        frontier.sort((p, q) => p.w - q.w || p.id - q.id);
        const e = frontier.shift();
        const a = stations[e.a].p ? e.a : e.b, b = a === e.a ? e.b : e.a;
        if (stations[b].p) continue;
        const pa = stations[a].p, [rx, rz] = realDir(a, b), rl = Math.hypot(rx, rz), ang = Math.atan2(rz, rx);
        let best = null;
        for (const mult of [1, 1.3, 1.65, 2.1, 2.7]) for (let k = 0; k < 8; k++) {
          const diff = rl < 1.5 ? 0.4 : angDist(ang, k * Math.PI / 4);
          if (diff > 1.75) continue;
          const L = L_OF(e.h) * mult, q = [pa[0] + DIRS[k][0] * L, pa[1] + DIRS[k][1] * L];
          let score = diff * 7 + (mult - 1) * 9 + (ports[a][k] ? 9 : 0);
          const ns = nearStation(q, -1), nl = nearSeg(q), bad = pathCost([pa, q], [a]);
          if (ns < DMIN) score += 60 + (DMIN - ns) * 8;
          if (nl < DEDGE * 1.4) score += 30;
          score += bad * 40;
          if (!best || score < best.score) best = { score, q };
        }
        stations[b].p = best.q;
        placeSeg(e, [pa, best.q]);
        e.poly = [pa.slice(), best.q.slice()]; if (a !== e.a) e.poly.reverse();
        inTree.add(e.id); placedLane.add(e.id);
        pushFrom(b);
      }
    }
    // stations the tree never reached (should not happen) sit beside home, unconnected
    stations.forEach(s => { if (!s.p) s.p = [0, 0]; });
    // lanes that close a loop: one bend (a diagonal and a straight run), kept only if they fit cleanly
    const rest = lanes.filter(e => !inTree.has(e.id)).sort((p, q) => p.w - q.w || p.id - q.id);
    for (const e of rest) {
      const pa = stations[e.a].p, pb = stations[e.b].p, dx = pb[0] - pa[0], dz = pb[1] - pa[1];
      const adx = Math.abs(dx), adz = Math.abs(dz), sx = Math.sign(dx), sz = Math.sign(dz);
      let opts;
      const diagStraight = Math.min(adx, adz) < 0.6 || Math.abs(adx - adz) < 0.6;
      if (diagStraight) opts = [[pa, pb]];
      else if (adx > adz) opts = [[pa, [pa[0] + sx * adz, pa[1] + sz * adz], pb], [pa, [pb[0] - sx * adz, pa[1]], pb]];
      else opts = [[pa, [pa[0] + sx * adx, pa[1] + sz * adx], pb], [pa, [pa[0], pb[1] - sz * adx], pb]];
      let best = null;
      for (const pts of opts) {
        let len = 0; for (let k = 0; k + 1 < pts.length; k++) len += Math.hypot(pts[k + 1][0] - pts[k][0], pts[k + 1][1] - pts[k][1]);
        const bad = pathCost(pts, [e.a, e.b]);
        let portBad = 0;
        if (ports[e.a][dirIndex(pts[1][0] - pts[0][0], pts[1][1] - pts[0][1])]) portBad++;
        const n = pts.length;
        if (ports[e.b][dirIndex(pts[n - 2][0] - pts[n - 1][0], pts[n - 2][1] - pts[n - 1][1])]) portBad++;
        const sc = bad * 10 + portBad * 3 + len / L_OF(e.h);
        if (bad === 0 && portBad === 0 && len <= 2.3 * L_OF(e.h) && (!best || sc < best.sc)) best = { sc, pts };
      }
      if (best) { placeSeg(e, best.pts); e.poly = best.pts.map(p => p.slice()); placedLane.add(e.id); }
    }
    lanes = lanes.filter(e => placedLane.has(e.id));
    lanes.forEach((e, k) => { e.id = k; });
    stations.forEach(s => { s.edges = []; });
    lanes.forEach(e => { stations[e.a].edges.push(e); stations[e.b].edges.push(e); });
    // lengths and helpers on each lane's diagram polyline
    for (const e of lanes) {
      e.len = []; let t = 0;
      for (let k = 0; k + 1 < e.poly.length; k++) { t += Math.hypot(e.poly[k + 1][0] - e.poly[k][0], e.poly[k + 1][1] - e.poly[k][1]); e.len.push(t); }
      e.total = t;
    }
    // point at a fraction of a lane's diagram length (from end a)
    function laneAt(e, f) {
      const d = f * e.total; let prev = 0;
      for (let k = 0; k < e.len.length; k++) {
        if (d <= e.len[k] + 1e-9 || k === e.len.length - 1) {
          const seg = e.len[k] - prev, u = seg > 0 ? (d - prev) / seg : 0, p = e.poly[k], q = e.poly[k + 1];
          return [p[0] + (q[0] - p[0]) * u, p[1] + (q[1] - p[1]) * u];
        }
        prev = e.len[k];
      }
      return e.poly[e.poly.length - 1].slice();
    }
    // the outgoing direction of a lane at one of its stations
    function dirAt(e, sid) {
      const pts = sid === e.a ? e.poly : e.poly.slice().reverse();
      const dx = pts[1][0] - pts[0][0], dz = pts[1][1] - pts[0][1], l = Math.hypot(dx, dz) || 1;
      return [dx / l, dz / l];
    }

    // ---------- 5. lines: pair lanes at each station into straight runs of one colour ----------
    const inZone = (s, z) => s.zone === z || (s.zone === 'contested' && (claim.get(s.sys) || []).includes(z));
    lanes.forEach(e => { const A = stations[e.a], B = stations[e.b]; e.zone = ZONE_ORDER.find(z => inZone(A, z) && inZone(B, z)) || 'open'; });
    const pairAt = new Map();   // key "lane:station" -> the lane it continues into
    for (const s of stations) {
      const E = s.edges, pairs = [];
      for (let i = 0; i < E.length; i++) for (let j = i + 1; j < E.length; j++) {
        if (E[i].zone !== E[j].zone) continue;
        const d1 = dirAt(E[i], s.id), d2 = dirAt(E[j], s.id), dot = d1[0] * d2[0] + d1[1] * d2[1];
        if (dot > 0.05) continue;     // no turns sharper than 90 degrees
        pairs.push([dot, E[i], E[j]]);
      }
      pairs.sort((p, q) => p[0] - q[0] || p[1].id - q[1].id || p[2].id - q[2].id);
      const used = new Set();
      for (const [, e1, e2] of pairs) {
        if (used.has(e1.id) || used.has(e2.id)) continue;
        used.add(e1.id); used.add(e2.id);
        pairAt.set(e1.id + ':' + s.id, e2); pairAt.set(e2.id + ':' + s.id, e1);
      }
    }
    const lines = [], lineOfLane = new Int32Array(lanes.length).fill(-1);
    function walkLine(startLane, startStation) {
      const run = [];
      let e = startLane, from = startStation, guard = 0;
      while (e && lineOfLane[e.id] < 0 && guard++ < 500) {
        const to = e.a === from ? e.b : e.a;
        run.push({ lane: e, from, to });
        lineOfLane[e.id] = lines.length;
        e = pairAt.get(e.id + ':' + to); from = to;
      }
      return run;
    }
    // start at line ends (a lane end with no partner), longest first; then whatever is left (loops)
    const ends = [];
    for (const e of lanes) for (const sid of [e.a, e.b]) if (!pairAt.has(e.id + ':' + sid)) ends.push([e, sid]);
    ends.sort((p, q) => p[0].id - q[0].id || p[1] - q[1]);
    for (const [e, sid] of ends) {
      if (lineOfLane[e.id] >= 0) continue;
      const run = walkLine(e, sid);
      if (run.length) lines.push({ id: lines.length, zone: e.zone, run });
    }
    for (const e of lanes) if (lineOfLane[e.id] < 0) { const run = walkLine(e, e.a); if (run.length) lines.push({ id: lines.length, zone: e.zone, run }); }
    for (const ln of lines) {
      ln.hops = ln.run.reduce((a, r) => a + r.lane.h, 0);
      ln.stations = [ln.run[0].from].concat(ln.run.map(r => r.to));
    }
    // codes like a metro's line numbers: Y1 is the longest line in your space, L1 the longest open line
    for (const z of ZONE_ORDER.concat(['open'])) {
      lines.filter(l => l.zone === z).sort((p, q) => q.hops - p.hops || p.id - q.id).forEach((l, k) => { l.code = CODE[z] + (k + 1); });
    }
    lanes.forEach(e => { e.line = lineOfLane[e.id]; });
    stations.forEach(s => {
      s.lines = [...new Set(s.edges.map(e => e.line))].sort((a, b) => a - b);
      s.interchange = s.lines.length >= 2 || s.zone === 'contested';
    });

    // where each real system of a lane sits on the diagram (the ticks)
    const onLane = new Map();   // sys -> { lane, k }
    for (const e of lanes) for (let k = 1; k < e.h; k++) if (!onLane.has(e.chain[k]) && !bySys.has(e.chain[k])) onLane.set(e.chain[k], { lane: e, k });
    const tickPos = (e, k) => laneAt(e, k / e.h);

    // ---------- 6. zones (fare-zone style) ----------
    function clipPoly(poly, nx, nz, c) {
      const out = [];
      for (let i = 0; i < poly.length; i++) {
        const p = poly[i], q = poly[(i + 1) % poly.length];
        const dp = nx * p[0] + nz * p[1] - c, dq = nx * q[0] + nz * q[1] - c;
        if (dp <= 0) out.push(p);
        if ((dp < 0) !== (dq < 0) && Math.abs(dp - dq) > 1e-12) { const t = dp / (dp - dq); out.push([p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t]); }
      }
      return out;
    }
    const NORMALS = [[1, 0], [-1, 0], [0, 1], [0, -1], [S2, S2], [-S2, -S2], [S2, -S2], [-S2, S2]];
    function octagon(pts, m) {
      let poly = [[-1e5, -1e5], [1e5, -1e5], [1e5, 1e5], [-1e5, 1e5]];
      for (const [nx, nz] of NORMALS) {
        let mx = -Infinity; for (const p of pts) mx = Math.max(mx, nx * p[0] + nz * p[1]);
        poly = clipPoly(poly, nx, nz, mx + m);
      }
      return poly.filter((p, k) => { const q = poly[(k + 1) % poly.length]; return Math.hypot(p[0] - q[0], p[1] - q[1]) > 0.05; });
    }
    function intersect(P, Q) {   // convex polygons
      let out = P;
      for (let i = 0; i < Q.length && out.length; i++) {
        const a = Q[i], b = Q[(i + 1) % Q.length], ex = b[0] - a[0], ez = b[1] - a[1];
        // keep the side of the edge the polygon's centre is on
        const cx = Q.reduce((s, p) => s + p[0], 0) / Q.length, cz = Q.reduce((s, p) => s + p[1], 0) / Q.length;
        let nx = ez, nz = -ex; if (nx * (cx - a[0]) + nz * (cz - a[1]) > 0) { nx = -nx; nz = -nz; }
        out = clipPoly(out, nx, nz, nx * a[0] + nz * a[1]);
      }
      return out;
    }
    const zones = [];
    const ZM = 6.5;
    const youF = factions.find(f => f.id === 'you');
    for (const z of ZONE_ORDER) {
      const f = factions.find(q => q.id === z); if (!f) continue;
      const pts = stations.filter(s => inZone(s, z)).map(s => s.p);
      if (!pts.length) continue;
      zones.push({ id: z, code: z === 'you' ? '1' : CODE[z], name: f.name, relation: f.relation, color: f.color, poly: octagon(pts, ZM), count: f.systems.length, shown: pts.length });
    }
    const zYou = zones.find(z => z.id === 'you');
    if (zYou && TD) {
      const pts = stations.filter(s => inZone(s, 'you')).map(s => s.p);
      zones.push({ id: 'frontier', code: '2', name: 'Frontier', relation: 'unclaimed', color: '#cfd8e6', poly: octagon(pts, ZM + 9), count: TD.frontier.systems.length, ring: true });
    }
    const zK = zones.find(z => z.relation === 'hostile');
    const contestedArea = zYou && zK ? intersect(zYou.poly, zK.poly) : [];
    // danger on the network: an area around the lane points and stations inside each zone
    const dangers = M.danger.map((d, k) => {
      const pts = [];
      for (const s of stations) if (HD.dangerOf[s.sys] === k) pts.push(s.p);
      for (const e of lanes) for (let j = 0; j <= e.h; j++) if (HD.dangerOf[e.chain[j]] === k) pts.push(laneAt(e, j / e.h));
      return pts.length ? { k, region: d, poly: octagon(pts, 4.5) } : null;
    }).filter(Boolean);

    // diagram bounds
    let bx0 = Infinity, bz0 = Infinity, bx1 = -Infinity, bz1 = -Infinity;
    for (const s of stations) { bx0 = Math.min(bx0, s.p[0]); bx1 = Math.max(bx1, s.p[0]); bz0 = Math.min(bz0, s.p[1]); bz1 = Math.max(bz1, s.p[1]); }

    // ---------- 7. journeys over the network ----------
    const WALK = 60, CHANGE = 35, HOP_OFF = 140, NL = lines.length;
    function plan(A, B, opts) {
      opts = opts || {};
      const avoid = !!opts.avoid, startFuel = opts.fuel == null ? FMAX : opts.fuel;
      if (A === B) return null;
      const hA = opts.hA || HD.hopsFrom(A), hB = HD.hopsFrom(B);
      if (hA[B] < 0) return null;
      const direct = hA[B], sA = bySys.get(A), sB = bySys.get(B);
      const nS = stations.length, SZ = nS * (NL + 1);
      const dist = new Float64Array(SZ).fill(Infinity), prev = new Int32Array(SZ).fill(-1), via = new Int32Array(SZ).fill(-1), done = new Uint8Array(SZ);
      const st = (sid, line) => sid * (NL + 1) + (line < 0 ? NL : line);
      const heap = [];
      const push = (d, s) => { heap.push([d, s]); let i = heap.length - 1; while (i > 0) { const p = (i - 1) >> 1; if (heap[p][0] <= heap[i][0]) break; [heap[p], heap[i]] = [heap[i], heap[p]]; i = p; } };
      const pop = () => { const top = heap[0], last = heap.pop(); if (heap.length) { heap[0] = last; let i = 0; for (;;) { const l = 2 * i + 1, r = l + 1; let m = i; if (l < heap.length && heap[l][0] < heap[m][0]) m = l; if (r < heap.length && heap[r][0] < heap[m][0]) m = r; if (m === i) break; [heap[m], heap[i]] = [heap[i], heap[m]]; i = m; } } return top; };
      for (const s of stations) {
        const h = s.sys === A ? 0 : hA[s.sys];
        if (h < 0 || h > FMAX || (sA && s !== sA)) continue;   // at a station you board there
        const k = st(s.id, -1), d = h * 100 + (h ? WALK : 0) + (avoid && h && HD.dangerOf[s.sys] >= 0 ? 5000 : 0);
        if (d < dist[k]) { dist[k] = d; push(d, k); }
      }
      while (heap.length) {
        const [d, k] = pop(); if (done[k]) continue; done[k] = 1;
        const sid = Math.floor(k / (NL + 1)), line = k % (NL + 1);
        for (const e of stations[sid].edges) {
          const o = e.a === sid ? e.b : e.a, nl = e.line;
          const c = e.h * 100 + (e.danger ? (avoid ? 5000 : 0.5) : 0) + (line !== NL && line !== nl ? CHANGE : 0);
          const t = st(o, nl);
          if (d + c < dist[t]) { dist[t] = d + c; prev[t] = k; via[t] = e.id; push(d + c, t); }
        }
        // a short hop off the network to another station (up to 4 jumps), when the lines go the long way round
        if (sid < nBFS) for (let o = 0; o < nBFS; o++) {
          const h = hops[o][stations[sid].sys];
          if (o === sid || h < 1 || h > 4) continue;
          const c = h * 100 + HOP_OFF + (avoid && HD.dangerOf[stations[o].sys] >= 0 ? 5000 : 0);
          const t = st(o, -1);
          if (d + c < dist[t]) { dist[t] = d + c; prev[t] = k; via[t] = -2; push(d + c, t); }
        }
      }
      let best = -1, bd = Infinity;
      for (let k = 0; k < SZ; k++) {
        if (dist[k] === Infinity) continue;
        const s = stations[Math.floor(k / (NL + 1))], h = s.sys === B ? 0 : hB[s.sys];
        if (h < 0 || h > FMAX || (sB && s !== sB)) continue;   // a station destination is reached by the network
        const tot = dist[k] + h * 100 + (h ? WALK : 0);
        if (tot < bd) { bd = tot; best = k; }
      }
      // the states back to the boarding station
      const path = [];
      for (let k = best; k >= 0; k = prev[k]) path.push(k);
      path.reverse();
      let seq = null, legs = [];
      if (best >= 0) {
        const board = stations[Math.floor(path[0] / (NL + 1))], alight = stations[Math.floor(best / (NL + 1))];
        seq = [];
        if (board.sys !== A) {
          const c = chain(board.sys, A, hA); if (!c) seq = null; else { c.reverse(); seq.push(...c); legs.push({ kind: 'walk', from: A, to: board.sys, hops: c.length - 1 }); }
        } else seq.push(A);
        for (let j = 1; seq && j < path.length; j++) {
          const fromId = Math.floor(path[j - 1] / (NL + 1));
          if (via[path[j]] === -2) {   // off the network between two stations
            const toId = Math.floor(path[j] / (NL + 1)), c = chain(stations[fromId].sys, stations[toId].sys, hops[toId]);
            if (!c) { seq = null; break; }
            seq.push(...c.slice(1));
            legs.push({ kind: 'walk', from: c[0], to: c[c.length - 1], hops: c.length - 1 });
            continue;
          }
          const e = lanes[via[path[j]]];
          const c = e.a === fromId ? e.chain : e.chain.slice().reverse();
          seq.push(...c.slice(1));
          const last = legs[legs.length - 1];
          if (last && last.kind === 'ride' && last.line === e.line) { last.to = c[c.length - 1]; last.hops += e.h; last.lanes.push(e.id); last.via.push(c[c.length - 1]); }
          else legs.push({ kind: 'ride', line: e.line, from: c[0], to: c[c.length - 1], hops: e.h, lanes: [e.id], via: [c[c.length - 1]] });
        }
        if (seq && alight.sys !== B) {
          const c = chain(alight.sys, B, hB); if (!c) seq = null; else { seq.push(...c.slice(1)); legs.push({ kind: 'walk', from: alight.sys, to: B, hops: c.length - 1 }); }
        }
      }
      const netJumps = seq ? seq.length - 1 : Infinity;
      // when the network is a detour, go direct (concept A's planner) and say so
      if (!seq || (!opts.force && netJumps > direct + Math.max(3, Math.ceil(direct * 0.4)))) {
        const r = HD.planRoute(A, B, { fuel: startFuel, avoid });
        if (!r || !r.ok) return r;
        r.transit = { direct: true, legs: [{ kind: 'walk', from: A, to: B, hops: r.jumps }], changes: 0, directJumps: direct };
        return r;
      }
      return finish(A, B, seq, legs, startFuel, avoid, direct);
    }
    // fuel along a known sequence of systems: refuel as late as possible
    function finish(A, B, seq, legs, startFuel, avoid, direct) {
      const n = seq.length, cost = k => (HD.stormy[seq[k]] || HD.stormy[seq[k + 1]]) ? 2 : 1;
      let fuel = startFuel;
      const stops = [];
      for (let k = 0; k < n; k++) {
        const stop = { sys: seq[k], fuelIn: fuel, fuelOut: fuel, refuel: false, danger: HD.dangerOf[seq[k]] };
        if (k < n - 1) {
          let need = 0, j = k;
          do { need += cost(j); j++; } while (j < n - 1 && !HD.canRefuel[seq[j]]);
          if (fuel < need && HD.canRefuel[seq[k]] && fuel < FMAX) { fuel = FMAX; stop.refuel = true; stop.fuelOut = fuel; }
          if (fuel < cost(k)) return HD.planRoute(A, B, { fuel: startFuel, avoid });
          fuel -= cost(k);
        }
        if (k > 0) stop.hop = d3(seq[k - 1], seq[k]);
        stops.push(stop);
      }
      let total = 0; for (let k = 1; k < n; k++) total += stops[k].hop;
      const jumps = n - 1, refuels = stops.filter(s => s.refuel).length, dangers = [];
      stops.forEach((s, k) => { if (k > 0 && s.danger >= 0) { const d = dangers.find(x => x.k === s.danger); if (d) d.count++; else dangers.push({ k: s.danger, region: M.danger[s.danger], count: 1 }); } });
      const rides = legs.filter(l => l.kind === 'ride').length;
      return {
        ok: true, from: A, to: B, stops, jumps, refuels, dangers, lengthLy: total, straightLy: d3(A, B), seconds: jumps * 28 + refuels * 40, avoid, expanded: 0,
        transit: { direct: false, legs, changes: Math.max(0, rides - 1), directJumps: direct },
      };
    }

    const ms = (typeof performance !== 'undefined' ? performance : Date).now() - t0;
    return {
      stations, lanes, lines, zones, dangers, contestedArea, bySys, onLane, laneAt, tickPos, dirAt, plan,
      bounds: [bx0, bz0, bx1, bz1], home: HOME, ms,
    };
  }
})();
