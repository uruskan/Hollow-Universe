/* Hollow Universe map, concept Known: every known star gets a shape.
 *
 * Known stars own cells: a Voronoi partition of the projected known stars (d3-delaunay 6.0.4), each cell bounded to a
 * few light-years from its star, so the shapes stop where knowledge stops. Unknown space has no cells at all.
 * Cells merge into tiles, so the screen never holds more than about 200 shapes:
 *   - the roots are what you know as wholes: Home, each drone survey, each past trip. A star known on its own (seen from
 *     home, a lone visit) is its own root;
 *   - places that matter keep a tile of their own at every zoom: your bases, visited systems, finds, the selection and
 *     the route;
 *   - any other tile splits in two (across its longer side on screen, at the median star) while it is wider than
 *     SPLIT_PX and the budget allows. Zooming in splits tiles down to single stars; zooming out merges them back;
 *   - a tile is the union of its stars' cells: one fill, with borders only where tiles meet and where knowledge ends.
 * Which star belongs to which tile is decided when the view settles (150 ms still) or when knowledge changes; while the
 * view moves, the stars keep their tiles and only the geometry follows, so the frame rate holds.
 *
 * Interface
 *   const tiles = HollowTiles.create({ canvas, dpr, stars: { x, y, z }, K, pinned })
 *     K        window.HOLLOW_KNOWLEDGE;  pinned(i) -> true for stars that keep a tile of their own (app state)
 *   tiles.frame({ camera, W, H, alpha, now, hover, sel, selGroup, route, dim })   every frame
 *     hover / sel = star or -1; selGroup = group index or -1; route = [star, ...] or null; dim(i) -> true to grey a cell
 *   tiles.dirty()            app state changed (selection, route, filters): re-deal at the next settle
 *   tiles.pick(x, y)         { i, tile } for the star whose cell holds the point, or null (unknown space)
 *   tiles.tileOf(i)          { id, root, group, members, kind, cls } or null
 *   tiles.groupSpots()       [{ g, x, y, ext, n }] on-screen centres of the root groups (for their names)
 *   tiles.count, tiles.cells, tiles.stats
 */
(function () {
  'use strict';
  window.HollowTiles = { create };

  const SPLIT_PX = 150;          // a tile wider than this on screen splits in two
  const BUDGET = 200;            // never more tiles than this on a 1600 x 900 view (scaled with the window area)
  const SETTLE_MS = 150, FADE_MS = 300, REVEAL_MS = 1500;
  const REACH = [0, 4.5, 6.5, 7, 8];   // ly a cell reaches from its star, by knowledge level
  // colours by what you know (A's palette: one mint accent for yours; lilac for finds, pink for other players)
  const CLS = {
    base:    { rgb: [95, 227, 192],  fill: 0.17, edge: 0.9 },
    home:    { rgb: [95, 227, 192],  fill: 0.07, edge: 0.55 },
    visited: { rgb: [226, 234, 246], fill: 0.08, edge: 0.5 },
    scanned: { rgb: [128, 172, 255], fill: 0.07, edge: 0.55 },
    seen:    { rgb: [200, 208, 222], fill: 0.0,  edge: 0.34 },
    find:    { rgb: [196, 181, 253], fill: 0.16, edge: 0.8 },
    player:  { rgb: [255, 156, 207], fill: 0.13, edge: 0.75 },
  };

  function create({ canvas, dpr, stars, K, pinned }) {
    const { x, y, z } = stars;
    const D = window.d3 && window.d3.Delaunay;
    if (!D) throw new Error('d3-delaunay did not load');
    const ctx = canvas.getContext('2d');
    let W = 0, H = 0;
    const vp = new Float64Array(16);
    let projScale = 1;

    // ---- membership: star -> tile (dealt on settle) ----
    let tiles = [];                         // [{ id, root, group, members, kind, cls, fallback }]
    const tileId = new Map();               // star -> tile index
    let dealVersion = -1, dealT = -1e9, memDirty = true, lastMoveT = 0, memCam = null;
    const camNow = new Float64Array(20), camPrev = new Float64Array(20);
    let camPrevOk = false;
    // ---- geometry (rebuilt when the view or the membership changes) ----
    let geo = null, oldBorders = null, oldBordersT = 0, drawKey = '', lastFind = 0;
    const stats = { known: 0, cells: 0, tiles: 0, dealMs: 0, buildMs: 0, drawMs: 0 };

    function camSnapshot(camera, out) {
      const m = camera.matrixWorld.elements, p = camera.projectionMatrix.elements;
      for (let k = 0; k < 16; k++) out[k] = m[k];
      out[16] = p[0]; out[17] = p[5]; out[18] = W; out[19] = H;
    }
    function camMoved(a, b, dist) {
      if (a[18] !== b[18] || a[19] !== b[19]) return 1e9;
      const pos = Math.hypot(a[12] - b[12], a[13] - b[13], a[14] - b[14]) / Math.max(dist, 1e-6);
      let rot = 0; for (let k = 0; k < 11; k++) if (k !== 3 && k !== 7) rot = Math.max(rot, Math.abs(a[k] - b[k]));
      const zoom = Math.abs(a[17] - b[17]) / Math.max(a[17], 1e-6);
      return (pos + rot + zoom) * b[19];
    }
    function setVP(camera) {
      const p = camera.projectionMatrix.elements, m = camera.matrixWorldInverse.elements;
      for (let r = 0; r < 4; r++) for (let c = 0; c < 4; c++) {
        let s = 0; for (let k = 0; k < 4; k++) s += p[k * 4 + r] * m[c * 4 + k];
        vp[c * 4 + r] = s;
      }
      projScale = p[5] * H / 2;
    }
    const tmp = [0, 0, 0];
    function proj(X, Y, Z, out) {
      const e = vp, cw = e[3] * X + e[7] * Y + e[11] * Z + e[15];
      if (cw <= 1e-6) return false;
      out[0] = ((e[0] * X + e[4] * Y + e[8] * Z + e[12]) / cw * 0.5 + 0.5) * W;
      out[1] = (0.5 - (e[1] * X + e[5] * Y + e[9] * Z + e[13]) / cw * 0.5) * H;
      out[2] = cw;
      return true;
    }
    const rootOf = i => K.groupOf[i] >= 0 ? K.groupOf[i] : -1 - i;
    function classOf(members, root) {
      let base = false, player = false, find = false, top = 0;
      for (const i of members) {
        const l = K.level[i]; if (l > top) top = l;
        if (l === 4) base = true;
        const f = K.findsAt(i); if (f.length) { if (f.some(q => q.kind === 'player')) player = true; else find = true; }
      }
      if (base) return 'base';
      if (members.length === 1) return player ? 'player' : find ? 'find' : top >= 3 ? 'visited' : top === 2 ? 'scanned' : 'seen';
      const g = root >= 0 ? K.groups[root] : null;
      return g ? (g.kind === 'home' ? 'home' : g.kind === 'survey' ? 'scanned' : 'visited') : top >= 3 ? 'visited' : top === 2 ? 'scanned' : 'seen';
    }
    function makeTile(members, root, fallback) {
      const t = { id: tiles.length, root, group: root >= 0 ? K.groups[root] : null, members, fallback: !!fallback };
      t.kind = t.group ? t.group.kind : 'single';
      t.cls = classOf(members, root);
      tiles.push(t);
      for (const i of members) tileId.set(i, t.id);
      return t;
    }

    // ---- deal: roots, pinned singles, then splits by screen size under the budget ----
    function deal() {
      const t0 = performance.now();
      tiles = []; tileId.clear();
      const list = K.knownList, M = Math.max(W, H) * 0.25;
      const roots = new Map(), sx = new Map(), sy = new Map();
      for (const i of list) {
        if (!proj(x[i], y[i], z[i], tmp)) continue;
        if (tmp[0] < -M || tmp[1] < -M || tmp[0] > W + M || tmp[1] > H + M) continue;
        sx.set(i, tmp[0]); sy.set(i, tmp[1]);
        const r = rootOf(i);
        if (pinned(i) && r < 0) { makeTile([i], r); continue; }
        if (pinned(i)) { makeTile([i], r); continue; }
        let b = roots.get(r); if (!b) roots.set(r, b = []); b.push(i);
      }
      const budget = Math.round(BUDGET * Math.min(1.6, Math.max(0.6, W * H / (1600 * 900))));
      const node = (members, root) => {
        let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
        for (const i of members) { const a = sx.get(i), b = sy.get(i); if (a < x0) x0 = a; if (a > x1) x1 = a; if (b < y0) y0 = b; if (b > y1) y1 = b; }
        const onScreen = x1 > 0 && y1 > 0 && x0 < W && y0 < H;
        return { members, root, x0, y0, x1, y1, ext: onScreen ? Math.max(x1 - x0, y1 - y0) : 0 };
      };
      const heap = [];
      for (const [r, ms] of roots) heap.push(node(ms, r));
      let count = tiles.length + heap.length;
      heap.sort((a, b) => a.ext - b.ext);
      while (heap.length) {
        const nd = heap.pop();
        if (nd.ext <= SPLIT_PX || nd.members.length < 2 || count >= budget) { makeTile(nd.members, nd.root); continue; }
        const byX = nd.x1 - nd.x0 >= nd.y1 - nd.y0, key = byX ? sx : sy;
        const ms = nd.members.slice().sort((a, b) => key.get(a) - key.get(b));
        const mid = ms.length >> 1;
        const a = node(ms.slice(0, mid), nd.root), b = node(ms.slice(mid), nd.root);
        count++;
        // keep the heap sorted (it is small)
        for (const c of [a, b]) { let k = heap.length; heap.push(c); while (k > 0 && heap[k - 1].ext > c.ext) { heap[k] = heap[k - 1]; k--; } heap[k] = c; }
      }
      dealVersion = K.version; dealT = performance.now();
      stats.tiles = tiles.length; stats.dealMs = performance.now() - t0;
    }
    // stars that were off screen at the deal still get a tile: their root's fallback
    const fallbackOf = new Map();
    function tileFor(i) {
      const t = tileId.get(i); if (t !== undefined) return t;
      const r = rootOf(i);
      let f = fallbackOf.get(r);
      if (f === undefined || !tiles[f] || tiles[f].root !== r) { f = makeTile([], r, true).id; fallbackOf.set(r, f); }
      tiles[f].members.push(i); tileId.set(i, f);
      return f;
    }

    // ---- geometry ----
    function circlePoly(cx, cy, r, seg) { const out = []; for (let k = 0; k < seg; k++) { const t = k / seg * Math.PI * 2; out.push([cx + Math.cos(t) * r, cy + Math.sin(t) * r]); } return out; }
    function build() {
      const t0 = performance.now();
      const M = Math.max(W, H) * 0.3, list = K.knownList;
      const idx = [], pts = [], depth = [];
      for (const i of list) {
        if (!proj(x[i], y[i], z[i], tmp)) continue;
        if (tmp[0] < -M || tmp[1] < -M || tmp[0] > W + M || tmp[1] > H + M) continue;
        idx.push(i); pts.push(tmp[0], tmp[1]); depth.push(tmp[2]);
      }
      const g = { idx, pts, cells: [], edges: [], tileOfCell: [], cellOf: new Map(), del: null, byTile: new Map(), borders: new Path2D(), groupBorders: new Path2D(), outer: {} };
      for (const key in CLS) g.outer[key] = new Path2D();
      if (idx.length) {
        g.del = new D(Float64Array.from(pts));
        const vor = g.del.voronoi([-M, -M, W + M, H + M]);
        for (let k = 0; k < idx.length; k++) {
          const i = idx[k]; g.cellOf.set(i, k);
          const t = tileFor(i); g.tileOfCell.push(t);
          let b = g.byTile.get(t); if (!b) g.byTile.set(t, b = []); b.push(k);
          let poly = vor.cellPolygon(k);
          if (!poly || poly.length < 4) { g.cells.push(null); continue; }
          poly.pop();
          const px = pts[2 * k], py = pts[2 * k + 1];
          const reach = Math.min(170, Math.max(11, REACH[K.level[i]] * projScale / depth[k]));
          let far = 0; for (const p of poly) far = Math.max(far, Math.hypot(p[0] - px, p[1] - py));
          if (far > reach) poly = clipConvex(poly, circlePoly(px, py, reach, 30));
          g.cells.push(poly.length >= 3 ? poly : null);
        }
        // every cell edge: shared with a neighbour (the bisector) or the edge of knowledge (the reach arc)
        const borders = new Path2D(), groupBorders = new Path2D(), outer = {};
        for (const key in CLS) outer[key] = new Path2D();
        const nb = [];
        for (let k = 0; k < idx.length; k++) {
          const poly = g.cells[k]; const E = []; g.edges.push(E);
          if (!poly) continue;
          // cells wholly off screen (kept only so the ones on screen are right) need no edges
          let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
          for (const q of poly) { if (q[0] < x0) x0 = q[0]; if (q[0] > x1) x1 = q[0]; if (q[1] < y0) y0 = q[1]; if (q[1] > y1) y1 = q[1]; }
          if (x1 < -4 || y1 < -4 || x0 > W + 4 || y0 > H + 4) continue;
          const px = pts[2 * k], py = pts[2 * k + 1];
          nb.length = 0; for (const q of g.del.neighbors(k)) nb.push(q);
          const tk = g.tileOfCell[k], rk = tiles[tk].root;
          for (let e = 0; e < poly.length; e++) {
            const a = poly[e], b = poly[(e + 1) % poly.length], mx = (a[0] + b[0]) / 2, my = (a[1] + b[1]) / 2;
            const dk = Math.sqrt((mx - px) * (mx - px) + (my - py) * (my - py));
            let j = -1, best = Math.max(0.75, dk * 0.004);
            for (let u = 0; u < nb.length; u++) { const q = nb[u], ex = mx - pts[2 * q], ey = my - pts[2 * q + 1], dq = Math.abs(Math.sqrt(ex * ex + ey * ey) - dk); if (dq < best) { best = dq; j = q; } }
            E.push([a[0], a[1], b[0], b[1], j]);
            if (j < 0) { const p = outer[tiles[tk].cls]; p.moveTo(a[0], a[1]); p.lineTo(b[0], b[1]); continue; }
            if (j < k) continue;                                     // shared edges are drawn once
            const tj = g.tileOfCell[j]; if (tj === tk) continue;      // inside a tile: no border
            const p = tiles[tj].root === rk ? borders : groupBorders;
            p.moveTo(a[0], a[1]); p.lineTo(b[0], b[1]);
          }
        }
        g.borders = borders; g.groupBorders = groupBorders; g.outer = outer;
      }
      geo = g;
      stats.known = list.length; stats.cells = idx.length; stats.buildMs = performance.now() - t0;
    }

    // ---- drawing ----
    function rgba(c, a) { return `rgba(${c[0]}, ${c[1]}, ${c[2]}, ${Math.max(0, Math.min(1, a)).toFixed(3)})`; }
    function tilePath(t, extraFilter) {
      const p = new Path2D(), ks = geo.byTile.get(t) || [];
      for (const k of ks) { const c = geo.cells[k]; if (c && (!extraFilter || extraFilter(k))) addPoly(p, c); }
      return p;
    }
    function tileOutline(t) {
      const p = new Path2D(), ks = geo.byTile.get(t) || [];
      for (const k of ks) for (const e of geo.edges[k] || []) if (e[4] < 0 || geo.tileOfCell[e[4]] !== t) { p.moveTo(e[0], e[1]); p.lineTo(e[2], e[3]); }
      return p;
    }
    function cellOutline(k) { const p = new Path2D(); const c = geo.cells[k]; if (c) addPoly(p, c); return p; }
    function draw(st) {
      const t0 = performance.now();
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      ctx.clearRect(0, 0, W, H);
      if (!geo || st.alpha < 0.01) return;
      const A = st.alpha, now = st.now;
      ctx.lineJoin = 'round'; ctx.lineCap = 'round';
      // 1. your space is a feeling: a soft glow around your bases (no borders, no claim)
      for (const b of K.bases) {
        if (!proj(x[b.sys], y[b.sys], z[b.sys], tmp)) continue;
        const r = Math.min(260, Math.max(26, (b.role === 'home' ? 11 : 6.5) * projScale / tmp[2]));
        const gr = ctx.createRadialGradient(tmp[0], tmp[1], 0, tmp[0], tmp[1], r);
        gr.addColorStop(0, `rgba(95, 227, 192, ${(0.2 * A).toFixed(3)})`); gr.addColorStop(0.45, `rgba(95, 227, 192, ${(0.08 * A).toFixed(3)})`); gr.addColorStop(1, 'rgba(95, 227, 192, 0)');
        ctx.fillStyle = gr; ctx.beginPath(); ctx.arc(tmp[0], tmp[1], r, 0, Math.PI * 2); ctx.fill();
      }
      const revealing = k => { const s = K.since[geo.idx[k]]; return s > 0 && now - s < REVEAL_MS; };
      const anyReveal = geo.idx.some((i, k) => revealing(k));
      // 2. tile fills: one fill per tile (the union of its cells), stars mid-reveal drawn on their own below
      for (const [t, ks] of geo.byTile) {
        const c = CLS[tiles[t].cls]; if (!c.fill) continue;
        const p = new Path2D();
        for (const k of ks) { const poly = geo.cells[k]; if (poly && !(anyReveal && revealing(k)) && !(st.dim && st.dim(geo.idx[k]))) addPoly(p, poly); }
        ctx.fillStyle = rgba(c.rgb, c.fill * A); ctx.fill(p);
      }
      if (anyReveal) {
        for (let k = 0; k < geo.idx.length; k++) {
          if (!revealing(k) || !geo.cells[k]) continue;
          const u = Math.max(0, Math.min(1, (now - K.since[geo.idx[k]]) / REVEAL_MS));
          if (now < K.since[geo.idx[k]]) continue;
          const c = CLS[tiles[geo.tileOfCell[k]].cls], p = cellOutline(k);
          ctx.fillStyle = rgba(c.rgb, (c.fill + 0.22 * (1 - u)) * A * Math.min(1, u * 3)); ctx.fill(p);
          ctx.lineWidth = 1; ctx.strokeStyle = rgba(c.rgb, 0.5 * (1 - u) * A); ctx.stroke(p);
        }
      }
      // 3. route corridor
      if (st.route && st.route.length) {
        const p = new Path2D();
        for (const s of st.route) { const k = geo.cellOf.get(s); if (k !== undefined && geo.cells[k]) addPoly(p, geo.cells[k]); }
        ctx.fillStyle = `rgba(95, 227, 192, ${(0.06 * A).toFixed(3)})`; ctx.fill(p);
      }
      // 4. borders: between groups, between tiles of one group (fading in after a deal), and the edge of knowledge
      const fin = Math.min(1, (now - dealT) / FADE_MS);
      if (oldBorders) {
        const f = Math.min(1, (now - oldBordersT) / FADE_MS);
        if (f >= 1) oldBorders = null; else { ctx.lineWidth = 1; ctx.strokeStyle = `rgba(214, 224, 240, ${(0.15 * (1 - f) * A).toFixed(3)})`; ctx.stroke(oldBorders); }
      }
      ctx.lineWidth = 1;
      ctx.strokeStyle = `rgba(214, 224, 240, ${(0.15 * fin * A).toFixed(3)})`; ctx.stroke(geo.borders);
      ctx.strokeStyle = `rgba(214, 224, 240, ${(0.3 * A).toFixed(3)})`; ctx.stroke(geo.groupBorders);
      for (const key in CLS) {
        const c = CLS[key];
        ctx.lineWidth = key === 'base' || key === 'find' || key === 'player' ? 1.6 : 1.25;
        if (key === 'seen') ctx.setLineDash([3, 4]);
        ctx.strokeStyle = rgba(c.rgb, c.edge * A); ctx.stroke(geo.outer[key]);
        if (key === 'seen') ctx.setLineDash([]);
      }
      // 5. the selected group: a soft mint wash over its cells, and the edge of knowledge around it in mint
      if (st.selGroup >= 0) {
        const f = new Path2D(), p = new Path2D();
        for (const [t, ks] of geo.byTile) if (tiles[t].root === st.selGroup) for (const k of ks) {
          if (geo.cells[k]) addPoly(f, geo.cells[k]);
          for (const e of geo.edges[k] || []) if (e[4] < 0) { p.moveTo(e[0], e[1]); p.lineTo(e[2], e[3]); }
        }
        ctx.fillStyle = `rgba(95, 227, 192, ${(0.06 * A).toFixed(3)})`; ctx.fill(f);
        ctx.lineWidth = 2; ctx.strokeStyle = `rgba(95, 227, 192, ${(0.9 * A).toFixed(3)})`; ctx.stroke(p);
      }
      // 6. the selected star's cell
      const sk = st.sel >= 0 ? geo.cellOf.get(st.sel) : undefined;
      if (sk !== undefined && geo.cells[sk]) {
        const p = roundedPath(geo.cells[sk], 4);
        ctx.fillStyle = `rgba(95, 227, 192, ${(0.07 * A).toFixed(3)})`; ctx.fill(p);
        ctx.lineWidth = 1.8; ctx.strokeStyle = `rgba(95, 227, 192, ${(0.95 * A).toFixed(3)})`; ctx.stroke(p);
      }
      // 7. the hovered tile (soft light) and the hovered star's own cell inside it
      const hk = st.hover >= 0 ? geo.cellOf.get(st.hover) : undefined;
      if (hk !== undefined && geo.cells[hk]) {
        const t = geo.tileOfCell[hk], ks = geo.byTile.get(t) || [];
        const tp = tilePath(t);
        ctx.save(); ctx.shadowColor = 'rgba(160, 240, 220, 0.4)'; ctx.shadowBlur = 14;
        ctx.fillStyle = `rgba(170, 236, 220, ${(0.1 * A).toFixed(3)})`; ctx.fill(tp); ctx.restore();
        ctx.lineWidth = 1.5; ctx.strokeStyle = `rgba(190, 246, 230, ${(0.75 * A).toFixed(3)})`; ctx.stroke(tileOutline(t));
        if (ks.length > 1) { ctx.lineWidth = 1; ctx.setLineDash([2, 3]); ctx.strokeStyle = `rgba(255, 255, 255, ${(0.55 * A).toFixed(3)})`; ctx.stroke(cellOutline(hk)); ctx.setLineDash([]); }
      }
      ctx.globalAlpha = 1;
      stats.drawMs = performance.now() - t0;
    }

    const api = {
      stats,
      get count() { return tiles.length; },
      get cells() { return geo ? geo.idx.length : 0; },
      dirty() { memDirty = true; },
      resize(w, h) { W = w; H = h; canvas.width = Math.round(W * dpr); canvas.height = Math.round(H * dpr); drawKey = ''; camPrevOk = false; memDirty = true; },
      frame(st) {
        if (st.W !== W || st.H !== H) api.resize(st.W, st.H);
        if (st.alpha < 0.01) {
          if (geo || drawKey !== 'off') { ctx.setTransform(1, 0, 0, 1, 0, 0); ctx.clearRect(0, 0, canvas.width, canvas.height); geo = null; drawKey = 'off'; }
          memDirty = true; camPrevOk = false;
          return;
        }
        const now = st.now;
        camSnapshot(st.camera, camNow);
        const dist = st.dist || 100;
        const moved = !camPrevOk || camMoved(camPrev, camNow, dist) > 0.05;
        if (moved) { lastMoveT = now; camPrev.set(camNow); camPrevOk = true; }
        setVP(st.camera);
        const settled = now - lastMoveT > SETTLE_MS;
        const viewChanged = !memCam || camMoved(memCam, camNow, dist) > 0.5;
        let rebuilt = false;
        if (dealVersion !== K.version || (settled && (memDirty || viewChanged))) {
          if (geo && geo.borders) { oldBorders = geo.borders; oldBordersT = now; }
          deal(); memDirty = false; memCam = Float64Array.from(camNow); fallbackOf.clear();
          build(); rebuilt = true;
        }
        if (!rebuilt && (!geo || moved)) build();
        const revealOn = K.knownList.some(i => K.since[i] > 0 && now - K.since[i] < REVEAL_MS + 200);
        const key = [stats.buildMs, st.hover, st.sel, st.selGroup, st.route ? st.route.length : 0, st.alpha.toFixed(3), st.dimKey || '', now - dealT < FADE_MS + 50 || oldBorders || revealOn ? now : 0, moved ? now : 0].join('|');
        if (key !== drawKey) { drawKey = key; draw(st); }
      },
      pick(px, py) {
        if (!geo || !geo.del) return null;
        const k = geo.del.find(px, py, lastFind < geo.idx.length ? lastFind : 0); lastFind = k;
        const poly = geo.cells[k];
        if (!poly || !inside(poly, px, py)) return null;
        return { i: geo.idx[k], tile: tiles[geo.tileOfCell[k]] };
      },
      tileOf(i) { const t = tileId.get(i); return t === undefined ? null : tiles[t]; },
      // where each root group sits on screen now (the centre of its stars) and how wide it is
      groupSpots() {
        if (!geo) return [];
        const acc = new Map();
        for (let k = 0; k < geo.idx.length; k++) {
          const r = tiles[geo.tileOfCell[k]].root; if (r < 0) continue;
          let a = acc.get(r); if (!a) acc.set(r, a = { g: K.groups[r], sx: 0, sy: 0, n: 0, x0: Infinity, x1: -Infinity, y0: Infinity, y1: -Infinity });
          const X = geo.pts[2 * k], Y = geo.pts[2 * k + 1];
          a.sx += X; a.sy += Y; a.n++; a.x0 = Math.min(a.x0, X); a.x1 = Math.max(a.x1, X); a.y0 = Math.min(a.y0, Y); a.y1 = Math.max(a.y1, Y);
        }
        return [...acc.values()].map(a => ({ g: a.g, x: a.sx / a.n, y: a.sy / a.n, ext: Math.max(a.x1 - a.x0, a.y1 - a.y0), n: a.n }));
      },
      // the stars of a tile, for Tab (the app sorts them)
      membersOf(i) { const t = api.tileOf(i); return t ? t.members.slice() : [i]; },
      bench(n = 20) { const t0 = performance.now(); for (let k = 0; k < n; k++) build(); return (performance.now() - t0) / n; },
    };
    return api;
  }

  // ---------- geometry helpers (as concept Domains) ----------
  function signedArea(p) { let a = 0; for (let j = 0, m = p.length; j < m; j++) { const q = p[j], r = p[(j + 1) % m]; a += q[0] * r[1] - r[0] * q[1]; } return a / 2; }
  function clipConvex(subject, clip) {
    const sgn = signedArea(clip) >= 0 ? 1 : -1;
    let out = subject;
    for (let j = 0, m = clip.length; j < m && out.length; j++) {
      const a = clip[j], b = clip[(j + 1) % m], ex = b[0] - a[0], ey = b[1] - a[1];
      const side = p => sgn * (ex * (p[1] - a[1]) - ey * (p[0] - a[0]));
      const inp = out; out = [];
      let prev = inp[inp.length - 1], sp = side(prev);
      for (const cur of inp) {
        const sc = side(cur);
        if (sc >= 0) { if (sp < 0) out.push(cut(prev, cur, sp, sc)); out.push(cur); }
        else if (sp >= 0) out.push(cut(prev, cur, sp, sc));
        prev = cur; sp = sc;
      }
    }
    return out;
  }
  function cut(p, q, sp, sq) { const t = sp / (sp - sq); return [p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t]; }
  function inside(p, X, Y) {
    let pos = 0, neg = 0;
    for (let j = 0, m = p.length; j < m; j++) {
      const a = p[j], b = p[(j + 1) % m], c = (b[0] - a[0]) * (Y - a[1]) - (b[1] - a[1]) * (X - a[0]);
      if (c > 0) pos++; else if (c < 0) neg++;
      if (pos && neg) return false;
    }
    return true;
  }
  function addPoly(path, p) { path.moveTo(p[0][0], p[0][1]); for (let j = 1; j < p.length; j++) path.lineTo(p[j][0], p[j][1]); path.closePath(); }
  function roundedPath(p, r) {
    const path = new Path2D(), m = p.length;
    if (m < 3) return path;
    const L = (a, b) => Math.hypot(b[0] - a[0], b[1] - a[1]);
    const mid = (a, b) => [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2];
    const s = mid(p[m - 1], p[0]); path.moveTo(s[0], s[1]);
    for (let j = 0; j < m; j++) {
      const prev = p[(j + m - 1) % m], a = p[j], next = p[(j + 1) % m], e = mid(a, next);
      const l1 = L(prev, a), l2 = L(a, next);
      if (l1 < 1e-6 || l2 < 1e-6) { path.lineTo(e[0], e[1]); continue; }
      const cos = ((prev[0] - a[0]) * (next[0] - a[0]) + (prev[1] - a[1]) * (next[1] - a[1])) / (l1 * l2);
      const half = Math.acos(Math.max(-1, Math.min(1, cos))) / 2, th = Math.tan(half);
      const rr = Math.max(0, Math.min(r, 0.45 * Math.min(l1, l2) * th));
      if (rr < 0.3) path.lineTo(a[0], a[1]); else path.arcTo(a[0], a[1], e[0], e[1], rr);
    }
    path.closePath();
    return path;
  }
})();
