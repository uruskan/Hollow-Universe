/* Hollow Universe map, concept Domains: the view is divided into domains, one per place.
 *
 * Every star that has earned a domain owns the part of the view nearest to it: a Voronoi partition of the projected
 * stars (d3-delaunay). So there is always exactly one star under the cursor, the hovered domain lights up, and the
 * domains of controlled systems take their owner's colour, so territories read as countries with borders.
 *
 * Who earns a domain (membership) is decided when the view settles, never while it moves:
 *   - rank: the app's tiers (rankInto), plus apparent brightness, plus a small bonus for keeping the domain it had;
 *   - zoom gate: far out only places qualify; each step in lets the next tier in;
 *   - spacing: a new domain's star must sit `sep` px or more from every other one. Ranks of 900 and up (you, your
 *     space, the selection, the route and every faction system) are exempt, so a country never loses a province;
 *   - budget: about 150 domains on a 1600 x 900 view from 100 ly, fewer further out, more closer in.
 * While the camera moves, the same stars keep their domains and only the geometry follows them: a Delaunay
 * triangulation of a few hundred points, rebuilt only on frames where the view changed (well under a millisecond).
 *
 * A domain reaches at most REACH_LY from its star, so lone places far out read as islands, not as continents.
 * A claim (a faction system, a contested or a frontier system) colours only the part of its domain within its claim
 * radius: two neighbouring claims meet on a straight border, and a claim facing the wild ends in an arc.
 *
 * Interface
 *   const domains = HollowDomains.create({ field, canvas, dpr, stars: { n, x, y, z }, rankInto, territory })
 *     field       the star field (stars.js): sx, sy, app, visible, near, styleOf, setSites
 *     rankInto(out)   fills out[i]: < 0 never a domain, >= 900 always (exempt from spacing); see app.js rankStars
 *     territory   [{ id, kind: 'faction' | 'contested' | 'frontier', color, colors?, systems: [[sys, radiusLy], ...] }]
 *   domains.frame({ camera, W, H, dist, alpha, mouse, hover, sel, route, boost, now })   every frame
 *     mouse = { x, y } or null; hover / sel = star index or -1; route = [star, ...] or null; boost = territory chip
 *   domains.dirty()            the app's state changed (selection, filters, route, ship): re-rank at the next settle
 *   domains.pick(x, y)         { i, k }: the star whose domain holds the point (k = its domain), or null outside them
 *   domains.members(k)         the other visible, undimmed stars inside domain k (for Tab), unsorted
 *   domains.domainOf(i)        the domain index of star i, or -1
 *   domains.isSite(i), domains.count, domains.stats
 */
(function () {
  'use strict';
  window.HollowDomains = { create };

  const REACH_LY = 26;        // a domain never reaches further than this from its star
  const SETTLE_MS = 150;      // the view must hold still this long before domains are re-dealt
  const FADE_MS = 320;        // old and new meshes cross-fade when the partition is re-dealt
  const LENS_PX = 260;        // the mesh sharpens within this radius of the cursor
  const MINT = [95, 227, 192];

  function create({ field, canvas, dpr, stars, rankInto, territory }) {
    const { n, x, y, z } = stars;
    const D = window.d3 && window.d3.Delaunay;
    if (!D) throw new Error('d3-delaunay did not load');
    const ctx = canvas.getContext('2d');
    let W = 0, H = 0;

    // ---- territory lookup: star -> its claim (group, radius) ----
    const claimOf = new Map();
    territory.forEach((g, gi) => g.systems.forEach(([s, r]) => { if (!claimOf.has(s) || g.kind !== 'frontier') claimOf.set(s, { g: gi, r }); }));
    const hatch = makeHatch(territory.find(g => g.kind === 'contested'));

    // ---- membership ----
    const rank = new Float32Array(n), score = new Float32Array(n), isSite = new Uint8Array(n);
    let sites = [];
    let memDirty = true, memVersion = 0, lastMoveT = 0, memCam = null;
    const camNow = new Float64Array(20), camPrev = new Float64Array(20);
    let camPrevOk = false;

    // ---- geometry (rebuilt when the view or the membership changes) ----
    let geo = null, geoCam = null, geoVersion = -1, geoW = 0, geoH = 0;
    const vp = new Float64Array(16);
    let projScale = 1;  // proj[5] * H / 2: px per ly at depth 1
    let lastFind = 0, fadeOld = null, fadeT0 = 0;
    let drawKey = '';
    const stats = { sites: 0, buildMs: 0, memberMs: 0, drawMs: 0 };

    function camSnapshot(camera, out) {
      const m = camera.matrixWorld.elements, p = camera.projectionMatrix.elements;
      for (let k = 0; k < 16; k++) out[k] = m[k];
      out[16] = p[0]; out[17] = p[5]; out[18] = W; out[19] = H;
    }
    // how far the view moved between two snapshots, in px on screen (roughly)
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

    // far out only places earn a domain; each step in lets the next tier in
    const minRankFor = d => d >= 240 ? 760 : d >= 125 ? 500 : d >= 62 ? 300 : 0;
    const sepFor = d => d >= 240 ? 46 : d >= 125 ? 40 : 34;
    const budgetFor = d => Math.round(Math.min(380, Math.max(40, 150 * Math.pow(100 / d, 0.85))) * Math.min(2, Math.max(0.5, W * H / (1600 * 900))));

    function deal(dist) {
      const t0 = performance.now();
      rankInto(rank);
      const sx = field.sx, sy = field.sy, app = field.app, near = field.near, vis = field.visible;
      const minR = minRankFor(dist), sep = sepFor(dist), budget = budgetFor(dist);
      const cand = [];
      for (let i = 0; i < n; i++) {
        if (!near[i]) continue;
        const r = rank[i];
        if (r < 0 || (r < minR && r < 900)) continue;
        score[i] = r + Math.min(24, Math.max(-12, (8 - app[i]) * 3)) + (isSite[i] ? 20 : 0);
        cand.push(i);
      }
      cand.sort((a, b) => score[b] - score[a]);
      // spacing on a hash grid of `sep` px cells
      const grid = new Map(), sep2 = sep * sep, out = [];
      const key = (gx, gy) => gx * 100003 + gy;
      const put = i => { const k = key(Math.floor(sx[i] / sep), Math.floor(sy[i] / sep)); let b = grid.get(k); if (!b) grid.set(k, b = []); b.push(i); };
      const clear = i => {
        const gx = Math.floor(sx[i] / sep), gy = Math.floor(sy[i] / sep);
        for (let a = gx - 1; a <= gx + 1; a++) for (let b = gy - 1; b <= gy + 1; b++) {
          const bucket = grid.get(key(a, b)); if (!bucket) continue;
          for (const j of bucket) { const dx = sx[j] - sx[i], dy = sy[j] - sy[i]; if (dx * dx + dy * dy < sep2) return false; }
        }
        return true;
      };
      let onScreen = 0;
      for (const i of cand) if (rank[i] >= 900) { out.push(i); put(i); if (vis[i]) onScreen++; }
      for (const i of cand) {
        if (rank[i] >= 900) continue;
        if (onScreen >= budget) break;
        if (!clear(i)) continue;
        out.push(i); put(i); if (vis[i]) onScreen++;
      }
      for (const i of sites) isSite[i] = 0;
      for (const i of out) isSite[i] = 1;
      sites = out; memVersion++;
      field.setSites(isSite);
      stats.sites = out.length; stats.onScreen = onScreen; stats.memberMs = performance.now() - t0;
    }

    // ---- geometry ----
    function circlePoly(i, rLy, seg) {
      const out = [], X = x[i], Y = y[i], Z = z[i];
      for (let k = 0; k < seg; k++) {
        const t = k / seg * Math.PI * 2;
        if (!proj(X + Math.cos(t) * rLy, Y, Z + Math.sin(t) * rLy, tmp)) return null;
        out.push([tmp[0], tmp[1]]);
      }
      return out;
    }
    function build() {
      const t0 = performance.now();
      const M = Math.max(W, H) * 0.25;
      const idx = [], pts = [], depth = [];
      for (const i of sites) {
        if (!proj(x[i], y[i], z[i], tmp)) continue;
        if (tmp[0] < -M || tmp[1] < -M || tmp[0] > W + M || tmp[1] > H + M) continue;
        idx.push(i); pts.push(tmp[0], tmp[1]); depth.push(tmp[2]);
      }
      const g = { idx, pts, polys: [], area: [], cx: [], cy: [], del: null, mesh: null, terr: [], domainOf: new Map() };
      idx.forEach((i, k) => g.domainOf.set(i, k));
      if (idx.length) {
        g.del = new D(Float64Array.from(pts));
        const vor = g.del.voronoi([-M, -M, W + M, H + M]);
        const mesh = new Path2D();
        for (let k = 0; k < idx.length; k++) {
          let poly = vor.cellPolygon(k);
          if (!poly || poly.length < 4) { g.polys.push(null); g.area.push(0); continue; }
          poly.pop();
          // bound the domain: REACH_LY from its star (only when the cell actually reaches that far)
          const sxk = pts[2 * k], syk = pts[2 * k + 1], reachPx = REACH_LY * projScale / depth[k];
          let far = 0; for (const p of poly) far = Math.max(far, Math.hypot(p[0] - sxk, p[1] - syk));
          if (far > reachPx * 0.97) { const c = circlePoly(idx[k], REACH_LY, 40); if (c) poly = clipConvex(poly, c); }
          if (poly.length < 3) { g.polys.push(null); g.area.push(0); continue; }
          g.polys.push(poly); g.area.push(Math.abs(signedArea(poly)));
          addPoly(mesh, poly);
        }
        g.mesh = mesh;
        // claims: the part of each claimed domain within its claim radius, one path per group
        territory.forEach((grp, gi) => {
          const path = new Path2D(), pieces = [];
          for (const [s, rLy] of grp.systems) {
            const k = g.domainOf.get(s); if (k === undefined || !g.polys[k]) continue;
            const c = circlePoly(s, rLy, 36); if (!c) continue;
            const piece = clipConvex(g.polys[k], c);
            if (piece.length < 3) continue;
            if (signedArea(piece) < 0) piece.reverse();
            addPoly(path, piece); pieces.push(piece);
          }
          if (!pieces.length) return;
          const outside = new Path2D(); outside.rect(-M - 10, -M - 10, W + 2 * M + 20, H + 2 * M + 20); outside.addPath(path);
          let ax = 0, ay = 0, aa = 0;
          for (const p of pieces) { const a = Math.abs(signedArea(p)), c = centroid(p); ax += c[0] * a; ay += c[1] * a; aa += a; }
          g.terr.push({ grp, gi, path, outside, pieces, cx: aa ? ax / aa : 0, cy: aa ? ay / aa : 0, area: aa });
        });
        // the label bonus: bigger domains get their names first
        const bonus = field.siteBonus;
        for (let k = 0; k < idx.length; k++) bonus[idx[k]] = g.area[k] > 0 ? Math.max(0, Math.min(6, Math.log2(g.area[k] / 900))) : 0;
      }
      geo = g; geoVersion = memVersion; geoW = W; geoH = H;
      stats.buildMs = performance.now() - t0;
    }

    // ---- drawing ----
    function draw(st) {
      const t0 = performance.now();
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      ctx.clearRect(0, 0, W, H);
      if (!geo || !geo.mesh || st.alpha < 0.01) return;
      const A = st.alpha, now = st.now;
      ctx.lineJoin = 'round'; ctx.lineCap = 'round';
      // 1. the mesh: faint everywhere, sharper around the cursor; the old mesh fades out after a re-deal
      let fin = 1;
      if (fadeOld) {
        const f = Math.min(1, (now - fadeT0) / FADE_MS); fin = f;
        if (f >= 1) fadeOld = null;
        else { ctx.globalAlpha = A * (1 - f); ctx.lineWidth = 1; ctx.strokeStyle = 'rgba(178, 196, 222, 0.10)'; ctx.stroke(fadeOld); }
      }
      ctx.globalAlpha = A * fin;
      ctx.lineWidth = 1;
      ctx.strokeStyle = `rgba(178, 196, 222, ${(0.13 * (1 - 0.45 * st.boost)).toFixed(3)})`;
      ctx.stroke(geo.mesh);
      if (st.mouse) {
        const g = ctx.createRadialGradient(st.mouse.x, st.mouse.y, 0, st.mouse.x, st.mouse.y, LENS_PX);
        g.addColorStop(0, 'rgba(200, 218, 242, 0.30)'); g.addColorStop(0.45, 'rgba(200, 218, 242, 0.12)'); g.addColorStop(1, 'rgba(200, 218, 242, 0)');
        ctx.strokeStyle = g; ctx.stroke(geo.mesh);
      }
      ctx.globalAlpha = A;
      // 2. the route: a corridor of domains
      if (st.route && st.route.length) {
        const p = new Path2D();
        for (const s of st.route) { const k = geo.domainOf.get(s); if (k !== undefined && geo.polys[k]) addPoly(p, geo.polys[k]); }
        ctx.fillStyle = 'rgba(95, 227, 192, 0.05)'; ctx.fill(p);
      }
      // 3. territory: countries (fill, faint provinces, a crisp border just outside), the contested strip, the frontier
      const boost = st.boost;
      for (const t of geo.terr) {
        const kind = t.grp.kind, c = hexRGB(t.grp.color);
        if (kind === 'faction') {
          ctx.fillStyle = rgba(c, (t.grp.self ? 0.15 : 0.11) * (1 + 0.8 * boost)); ctx.fill(t.path);
          ctx.save(); ctx.clip(t.path); ctx.lineWidth = 1; ctx.strokeStyle = rgba(c, 0.20 + 0.1 * boost); ctx.stroke(t.path); ctx.restore();
          ctx.save(); ctx.clip(t.outside, 'evenodd'); ctx.lineWidth = 3.2; ctx.strokeStyle = rgba(c, 0.85); ctx.stroke(t.path); ctx.restore();
        } else if (kind === 'contested') {
          if (hatch) {
            const a = proj(x[t.grp.systems[0][0]], y[t.grp.systems[0][0]], z[t.grp.systems[0][0]], tmp) ? tmp : [0, 0];
            hatch.setTransform(new DOMMatrix([1 / dpr, 0, 0, 1 / dpr, a[0], a[1]]));
            ctx.globalAlpha = A * (0.62 + 0.3 * boost); ctx.fillStyle = hatch; ctx.fill(t.path); ctx.globalAlpha = A;
          }
          ctx.save(); ctx.clip(t.outside, 'evenodd'); ctx.setLineDash([5, 4]); ctx.lineWidth = 3.2; ctx.strokeStyle = rgba(c, 0.95); ctx.stroke(t.path); ctx.restore();
        } else {
          ctx.fillStyle = rgba(c, 0.028 + 0.03 * boost); ctx.fill(t.path);
          ctx.save(); ctx.clip(t.outside, 'evenodd'); ctx.setLineDash([2.5, 4.5]); ctx.lineWidth = 2.6; ctx.strokeStyle = rgba(c, 0.5 + 0.2 * boost); ctx.stroke(t.path); ctx.restore();
        }
      }
      // 4. the selected domain
      const selK = st.sel >= 0 ? geo.domainOf.get(st.sel) : undefined;
      if (selK !== undefined && geo.polys[selK]) {
        const p = roundedPath(geo.polys[selK], 4);
        ctx.fillStyle = 'rgba(95, 227, 192, 0.05)'; ctx.fill(p);
        ctx.lineWidth = 1.8; ctx.strokeStyle = 'rgba(95, 227, 192, 0.9)'; ctx.stroke(p);
      }
      // 5. the hovered domain: a soft lit polygon
      const hovK = st.hover >= 0 ? geo.domainOf.get(st.hover) : undefined;
      if (hovK !== undefined && geo.polys[hovK]) {
        const poly = geo.polys[hovK], p = roundedPath(poly, 6), px = geo.pts[2 * hovK], py = geo.pts[2 * hovK + 1];
        let far = 1; for (const q of poly) far = Math.max(far, Math.hypot(q[0] - px, q[1] - py));
        const g = ctx.createRadialGradient(px, py, 0, px, py, far);
        g.addColorStop(0, 'rgba(150, 240, 214, 0.20)'); g.addColorStop(1, 'rgba(150, 240, 214, 0.06)');
        ctx.save();
        ctx.shadowColor = 'rgba(95, 227, 192, 0.55)'; ctx.shadowBlur = 16;
        ctx.fillStyle = g; ctx.fill(p);
        ctx.restore();
        ctx.lineWidth = 1.5; ctx.strokeStyle = 'rgba(160, 244, 220, 0.78)'; ctx.stroke(p);
      }
      ctx.globalAlpha = 1;
      stats.drawMs = performance.now() - t0;
    }

    const domains = {
      stats,
      get count() { return sites.length; },
      isSite: i => isSite[i] === 1,
      dirty() { memDirty = true; },
      resize(w, h, d) { W = w; H = h; if (d) dpr = d; canvas.width = Math.round(W * dpr); canvas.height = Math.round(H * dpr); drawKey = ''; camPrevOk = false; },
      frame(st) {
        if (st.W !== W || st.H !== H) domains.resize(st.W, st.H);
        if (st.alpha < 0.01 || !field.isProjected()) {
          if (geo || drawKey !== 'off') { ctx.setTransform(1, 0, 0, 1, 0, 0); ctx.clearRect(0, 0, canvas.width, canvas.height); geo = null; drawKey = 'off'; }
          field.setSiteBlend(0);
          memDirty = true; camPrevOk = false;
          return;
        }
        field.setSiteBlend(st.alpha);
        const now = st.now;
        camSnapshot(st.camera, camNow);
        const moved = !camPrevOk || camMoved(camPrev, camNow, st.dist) > 0.05;
        if (moved) { lastMoveT = now; camPrev.set(camNow); camPrevOk = true; }
        const settled = now - lastMoveT > SETTLE_MS;
        const viewChangedSinceDeal = !memCam || camMoved(memCam, camNow, st.dist) > 0.5;
        if (settled && (memDirty || viewChangedSinceDeal || !sites.length && field.isProjected())) {
          const oldMesh = geo && geo.mesh && !viewChangedSinceDeal ? geo.mesh : null;
          deal(st.dist);
          memDirty = false; memCam = Float64Array.from(camNow);
          if (oldMesh) { fadeOld = oldMesh; fadeT0 = now; } else { fadeOld = null; fadeT0 = now - FADE_MS * 0.4; }
        }
        setVP(st.camera);
        if (!geo || moved || geoVersion !== memVersion || geoW !== W || geoH !== H) { if (moved) fadeOld = null; build(); }
        const m = st.mouse;
        const key = [geo && geoVersion, stats.buildMs, m ? Math.round(m.x) + ',' + Math.round(m.y) : '-', st.hover, st.sel, st.route ? st.route.length : 0, st.alpha.toFixed(3), st.boost, fadeOld ? now : 0, moved ? now : 0].join('|');
        if (key !== drawKey) { drawKey = key; draw(st); }
      },
      pick(px, py) {
        if (!geo || !geo.del) return null;
        const k = geo.del.find(px, py, lastFind < geo.idx.length ? lastFind : 0); lastFind = k;
        const poly = geo.polys[k];
        if (!poly || !inside(poly, px, py)) return null;
        return { i: geo.idx[k], k };
      },
      domainOf(i) { if (!geo) return -1; const k = geo.domainOf.get(i); return k === undefined ? -1 : k; },
      members(k) {
        if (!geo || !geo.polys[k]) return [];
        const poly = geo.polys[k], site = geo.idx[k];
        let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
        for (const p of poly) { if (p[0] < x0) x0 = p[0]; if (p[0] > x1) x1 = p[0]; if (p[1] < y0) y0 = p[1]; if (p[1] > y1) y1 = p[1]; }
        const out = [], sx = field.sx, sy = field.sy, vis = field.visible;
        for (let i = 0; i < n; i++) {
          if (!vis[i] || i === site) continue;
          const X = sx[i], Y = sy[i];
          if (X < x0 || X > x1 || Y < y0 || Y > y1) continue;
          if (field.styleOf(i) > 1.5 || !inside(poly, X, Y)) continue;
          out.push(i);
        }
        return out;
      },
      // the screen centre of a claim group's coloured area (for its name), or null when none of it is on screen
      claimCentre(id) { if (!geo) return null; const t = geo.terr.find(q => q.grp.id === id); return t && t.area > 400 ? [t.cx, t.cy, t.area] : null; },
    };
    return domains;
  }

  // ---------- geometry helpers ----------
  function signedArea(p) { let a = 0; for (let j = 0, m = p.length; j < m; j++) { const q = p[j], r = p[(j + 1) % m]; a += q[0] * r[1] - r[0] * q[1]; } return a / 2; }
  function centroid(p) {
    let cx = 0, cy = 0, a = 0;
    for (let j = 0, m = p.length; j < m; j++) { const q = p[j], r = p[(j + 1) % m], c = q[0] * r[1] - r[0] * q[1]; cx += (q[0] + r[0]) * c; cy += (q[1] + r[1]) * c; a += c; }
    if (Math.abs(a) < 1e-9) return p[0];
    return [cx / (3 * a), cy / (3 * a)];
  }
  // Sutherland-Hodgman: the part of `subject` inside the convex polygon `clip` (either orientation)
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
  // a polygon with softly rounded corners
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
  function hexRGB(h) { if (Array.isArray(h)) return h; const c = parseInt(h.slice(1), 16); return [c >> 16 & 255, c >> 8 & 255, c & 255]; }
  function rgba(c, a) { return `rgba(${c[0]}, ${c[1]}, ${c[2]}, ${Math.max(0, Math.min(1, a)).toFixed(3)})`; }
  // diagonal stripes in both claimants' colours, for contested domains
  function makeHatch(grp) {
    if (!grp) return null;
    const cols = (grp.colors && grp.colors.length >= 2 ? grp.colors : [grp.color, grp.color]).map(hexRGB);
    const S = 8, k = 2;   // stripe period (CSS px) and supersampling
    const c = document.createElement('canvas'); c.width = c.height = S * 2 * k;
    const g = c.getContext('2d'); g.scale(k, k);
    const stripe = (col, off) => {
      g.fillStyle = rgba(col, 0.42);
      g.beginPath();
      for (let o = -2 * S; o <= 4 * S; o += 2 * S) { g.moveTo(o + off, 0); g.lineTo(o + off + 3, 0); g.lineTo(o + off + 3 - 2 * S, 2 * S); g.lineTo(o + off - 2 * S, 2 * S); g.closePath(); }
      g.fill();
    };
    stripe(cols[0], 0); stripe(cols[1], S);
    return g.createPattern(c, 'repeat');
  }
  void MINT;
})();
