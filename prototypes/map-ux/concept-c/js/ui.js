/* Reach map (concept C): everything you read. HTML labels placed by projection without overlaps, the
   search, the star card, the route's subway line, the status, the legend. */
'use strict';
window.RM = window.RM || {};

RM.createUI = function (ctx) {
  const { M, S, state, act } = ctx;
  const $ = id => document.getElementById(id);
  const overlay = $('overlay');
  const esc = t => String(t).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

  // ------------------------------------------------------------ icons (drawn for this prototype)
  const svg = (d, extra) => `<svg class="ico" viewBox="0 0 20 20"${extra || ''}>${d}</svg>`;
  const ICON = {
    fuel: svg('<path d="M10 3.2c2.6 3.3 4.4 5.7 4.4 8a4.4 4.4 0 0 1-8.8 0c0-2.3 1.8-4.7 4.4-8z"/>'),
    nofuel: svg('<path d="M10 3.2c2.6 3.3 4.4 5.7 4.4 8a4.4 4.4 0 0 1-8.8 0c0-2.3 1.8-4.7 4.4-8z"/><path d="M4 16L16 4"/>'),
    station: svg('<rect x="5.2" y="5.2" width="9.6" height="9.6" rx="2" transform="rotate(45 10 10)"/>'),
    pin: svg('<path d="M10 17.5s5-5.1 5-9a5 5 0 0 0-10 0c0 3.9 5 9 5 9z"/><circle cx="10" cy="8.5" r="1.8"/>'),
    home: svg('<path d="M3.5 9.5L10 4l6.5 5.5"/><path d="M5.5 8.5V16h9V8.5"/>'),
    warn: svg('<path d="M10 3.5l7 12.5H3z"/><path d="M10 8.5v3.2"/><circle cx="10" cy="13.9" r="0.4" class="fill"/>'),
    storm: svg('<path d="M11 2.8L5.5 11h4l-1 6.2L14.5 9h-4z"/>'),
    star: svg('<path d="M10 2.8l2.2 4.6 5 .6-3.7 3.4 1 5-4.5-2.5-4.5 2.5 1-5L2.8 8l5-.6z"/>'),
    target: svg('<circle cx="10" cy="10" r="5.5"/><path d="M10 2v3.5M10 14.5V18M2 10h3.5M14.5 10H18"/>'),
    route: svg('<circle cx="5" cy="15" r="1.8"/><circle cx="15" cy="5" r="1.8"/><path d="M6.6 13.8C9 12 7 9 10 8s3.6-.8 3.6-1.7"/>'),
    grid: svg('<rect x="3.5" y="3.5" width="13" height="13" rx="2"/><path d="M10 3.5v13M3.5 10h13"/>'),
    sector: svg('<path d="M10 2.8l6.2 3.6v7.2L10 17.2l-6.2-3.6V6.4z"/>'),
    spiral: svg('<path d="M10 10.2a1.3 1.3 0 1 1 1.3-1.3c0 2-2.4 3.2-4.2 2.3-2.6-1.3-2.2-5.2.3-6.2 3.4-1.4 7 1 7 4.7 0 4.2-4 6.8-8 5.6"/>'),
    planet: svg('<circle cx="10" cy="10" r="4.2"/><path d="M3 12.6c-1.3 1.6 5 .5 9.4-2s6.7-4.8 4.6-4.9"/>'),
    water: svg('<path d="M10 3.5c2.4 3 4 5.1 4 7.2a4 4 0 0 1-8 0c0-2.1 1.6-4.2 4-7.2z"/><path d="M8 11.5a2 2 0 0 0 2 2"/>'),
    metal: svg('<path d="M4 13l3-7h6l3 7z"/><path d="M4 13h12"/>'),
    leaf: svg('<path d="M5 15c0-6 4-10 11-10 0 7-4 11-10 11"/><path d="M5 15l6-6"/>'),
    gem: svg('<path d="M6 4h8l3 4-7 8.5L3 8z"/><path d="M3 8h14M8 4l2 12.5L12 4"/>'),
    play: '<svg viewBox="0 0 20 20"><path d="M7 5l8 5-8 5z" fill="currentColor" stroke="none"/></svg>',
    stop: '<svg viewBox="0 0 20 20"><rect x="5.5" y="5.5" width="9" height="9" rx="1.5" fill="currentColor" stroke="none"/></svg>',
    close: '<svg viewBox="0 0 20 20"><path d="M6 6l8 8M14 6l-8 8"/></svg>',
    chev: '<svg class="chev" viewBox="0 0 10 10"><path d="M3.5 2l3 3-3 3"/></svg>',
  };
  const PIN_COLOURS = { home: '#ffffff', friendly: '#6fd69a', neutral: '#b9c3cf', hostile: '#ff5c66' };
  function pinSvg(rel) {
    const c = PIN_COLOURS[rel] || '#fff';
    const glyph = rel === 'home'
      ? '<path d="M5.6 9.2L9 6.4l3.4 2.8V12.4H5.6z" fill="#0b1018"/>'
      : '<circle cx="9" cy="9" r="2.6" fill="#0b1018"/>';
    return `<svg viewBox="0 0 18 24"><path d="M9 23.2s7.6-7.4 7.6-13.6A7.6 7.6 0 0 0 1.4 9.6C1.4 15.8 9 23.2 9 23.2z" fill="${c}" stroke="rgba(0,0,0,.45)" stroke-width="1"/>${glyph}</svg>`;
  }

  // ------------------------------------------------------------ formatting
  const nf = new Intl.NumberFormat('en-GB');
  function fmtLy(d) {
    if (d < 0.05) return '0 ly';
    if (d < 10) return d.toFixed(1) + ' ly';
    if (d < 100000) return nf.format(Math.round(d)) + ' ly';
    return nf.format(Math.round(d / 1000)) + 'k ly';
  }
  const jumpsText = h => (h === 0 ? 'Here' : h === 1 ? '1 jump' : h + ' jumps');
  function fmtTime(sec) {
    if (sec < 60) return Math.round(sec) + ' s';
    const m = Math.round(sec / 60);
    return m < 60 ? m + ' min' : Math.floor(m / 60) + ' h ' + (m % 60) + ' min';
  }
  const shipSys = () => state.ship.sys;
  const hopsTo = s => state.reach.hops[s];
  const inTank = s => state.reach.cost[s] >= 0;

  // ------------------------------------------------------------ text measurement
  const mctx = document.createElement('canvas').getContext('2d');
  const wcache = new Map();
  function textW(text, font, spacing) {
    const key = font + '|' + (spacing || 0) + '|' + text;
    let w = wcache.get(key);
    if (w === undefined) {
      mctx.font = font;
      w = mctx.measureText(text).width + (spacing || 0) * text.length;
      wcache.set(key, w);
    }
    return w;
  }

  // ------------------------------------------------------------ overlay pool
  const pool = new Map();
  let frameNo = 0;
  function el(key, cls, html) {
    let p = pool.get(key);
    if (!p) {
      const e = document.createElement('div');
      overlay.appendChild(e);
      p = { el: e, html: null, cls: null, used: -1, shown: false };
      pool.set(key, p);
    }
    if (p.cls !== cls) { p.el.className = cls + (p.shown ? ' on' : ''); p.cls = cls; }
    if (p.html !== html) { p.el.innerHTML = html; p.html = html; }
    p.used = frameNo;
    return p;
  }
  function put(p, x, y, extra) {
    p.el.style.transform = `translate3d(${Math.round(x)}px, ${Math.round(y)}px, 0)` + (extra || '');
    if (p.el.style.display === 'none') p.el.style.display = '';
    if (!p.shown) {
      p.shown = true;
      requestAnimationFrame(() => { if (p.shown) p.el.classList.add('on'); });
    }
  }
  function sweep() {
    for (const p of pool.values()) {
      if (p.used !== frameNo && p.shown) {
        p.shown = false; p.el.classList.remove('on'); p.el.style.display = 'none';
      }
    }
  }

  // ------------------------------------------------------------ collision-free placement
  const rects = [];
  function hit(x, y, w, h) {
    for (let k = 0; k < rects.length; k++) {
      const r = rects[k];
      if (x < r[0] + r[2] && x + w > r[0] && y < r[1] + r[3] && y + h > r[1]) return true;
    }
    return false;
  }
  const add = (x, y, w, h) => rects.push([x, y, w, h]);
  function panelRects() {
    for (const id of ['searchbox', 'card', 'route', 'status', 'controls', 'legend', 'crumbs', 'mocknote', 'scale']) {
      const e = $(id);
      if (!e || e.classList.contains('hidden')) continue;
      const r = e.getBoundingClientRect();
      if (r.width > 0) add(r.left - 8, r.top - 8, r.width + 16, r.height + 16);
    }
  }
  let panelCache = null, panelCacheFrame = -10;
  const lastSlot = new Map();
  // Tries right, left, above, below the anchor; keeps the slot it had last time if it still fits.
  function placeLabel(key, ax, ay, w, h, r, vw, vh) {
    const slots = [
      [ax + r + 6, ay - h / 2], [ax - r - 6 - w, ay - h / 2],
      [ax - w / 2, ay - r - 4 - h], [ax - w / 2, ay + r + 4],
    ];
    const prev = lastSlot.get(key);
    const order = prev !== undefined ? [prev, 0, 1, 2, 3] : [0, 1, 2, 3];
    for (const k of order) {
      const [x, y] = slots[k];
      if (x < 30 || y < 28 || x + w > vw - 4 || y + h > vh - 4) continue;
      if (hit(x - 2, y - 1, w + 4, h + 2)) continue;
      add(x - 2, y - 1, w + 4, h + 2);
      lastSlot.set(key, k);
      return [x, y];
    }
    return null;
  }

  // ------------------------------------------------------------ the overlay, every frame the view moves
  const P = [0, 0];
  const LABEL_FONT = '500 12px Inter';
  function starRadius(s, scale) {
    const mag = M.MAG[s] > 25 ? 11.5 : M.MAG[s];
    return Math.max(1.55, Math.min(3.9, 3.35 - 0.105 * mag)) * scale;
  }
  function updateOverlay(fades, tilt) {
    frameNo++;
    rects.length = 0;
    const vw = S.width, vh = S.height, view = S.view;
    const W = view.w;
    if (frameNo - panelCacheFrame > 6 || state.panelsDirty) {
      const save = rects.length; panelRects(); panelCache = rects.slice(save); rects.length = save;
      panelCacheFrame = frameNo; state.panelsDirty = false;
    }
    for (const r of panelCache) rects.push(r);
    add(0, 0, vw, 26 * (1 - tilt)); add(0, 0, 28 * (1 - tilt), vh);
    const scale = Math.max(0.5, Math.min(1.55, Math.pow(150 / W, 0.3)));
    const localOn = fades.stars > 0.05;
    const refY = view.refY;

    // ship
    const sp = state.shipPos;
    if (S.project(sp[0], sp[1], sp[2], P)) {
      const p = el('ship', 'mk mk-ship', '<i></i>' + (W < 9000 ? '<b>You</b>' : '<b>You are here</b>'));
      put(p, P[0], P[1]);
      add(P[0] - 14, P[1] - 14, 28, 28);
      add(P[0] + 11, P[1] - 12, W < 9000 ? 40 : 100, 20);
    }
    const labelled = new Set();
    // selection and target
    if (state.selected >= 0 && localOn) {
      const s = state.selected;
      if (S.project(M.X[s], M.Y[s], M.Z[s], P)) {
        const p = el('sel', 'mk selring', ''); if (p.selFor !== s) { p.selFor = s; if (RM.enter) RM.enter(p.el); } put(p, P[0], P[1]); add(P[0] - 14, P[1] - 14, 28, 28);
        const name = M.NAME[s], w = textW(name, '650 13px Inter') + 4;
        const at = placeLabel('L' + s, P[0], P[1], w, 17, 13, vw, vh);
        if (at) { const q = el('L' + s, 'lbl big sel', `<span class="nm">${esc(name)}</span>`); q.el.dataset.s = s; put(q, at[0], at[1]); labelled.add(s); }
      }
    }
    if (state.target >= 0 && localOn) {
      const s = state.target;
      if (S.project(M.X[s], M.Y[s], M.Z[s], P)) { put(el('tgt', 'mk target', ''), P[0], P[1]); add(P[0] - 18, P[1] - 18, 36, 36); }
    }
    // route stops
    if (state.route && fades.stars > 0.05) {
      const R = state.route, ap = state.autopilot;
      const doneUpTo = ap ? ap.leg : -1;
      R.stops.forEach((st, i) => {
        if (i === 0) return;
        const s = st.s;
        if (!S.project(M.X[s], M.Y[s], M.Z[s], P)) return;
        let cls = 'mk stop';
        if (i === R.stops.length - 1) cls += ' end';
        else if (st.refuel) cls += ' refuel';
        else if (st.station) cls += ' station';
        if (st.danger) cls += ' danger';
        if (i <= doneUpTo) cls += ' done';
        put(el('stop' + i, cls, '<i></i>'), P[0], P[1]);
        add(P[0] - 8, P[1] - 8, 16, 16);
      });
      // destination name always
      const last = R.stops[R.stops.length - 1].s;
      if (S.project(M.X[last], M.Y[last], M.Z[last], P)) {
        const stn = M.stationsBySys.get(last);
        const name = stn ? stn[0].name : M.NAME[last];
        const w = textW(name, '650 13px Inter') + 4;
        const at = placeLabel('L' + last, P[0], P[1], w, 17, 10, vw, vh);
        if (at) { put(el('L' + last, 'lbl big sel', `<span class="nm">${esc(name)}</span>`), at[0], at[1]); labelled.add(last); }
      }
    }
    // finder: the nearest matches, numbered like the list
    if (state.finderTop && localOn) {
      state.finderTop.forEach((s, i) => {
        if (!S.project(M.X[s], M.Y[s], M.Z[s], P)) return;
        put(el('fd' + i, 'mk fnum', '<i><b>' + (i + 1) + '</b></i>'), P[0], P[1]);
        add(P[0] - 11, P[1] - 30, 22, 30);
      });
    }
    // base pins
    if (localOn && W < 700) {
      for (const b of M.bases) {
        const s = b.sys;
        if (!S.project(M.X[s], M.Y[s], M.Z[s], P)) continue;
        if (P[0] < -40 || P[0] > vw + 40 || P[1] < -40 || P[1] > vh + 40) continue;
        const name = b.kind === 'home' ? 'Home' : b.handle;
        const sub = b.kind === 'home' ? '' : `<small>${b.relation}</small>`;
        const p = el('pin' + b.handle, 'mk pin ' + b.relation, pinSvg(b.relation) + `<b>${esc(name)}${sub}</b>`);
        put(p, P[0], P[1]);
        add(P[0] - 10, P[1] - 27, 20, 26);
        add(P[0] + 10, P[1] - 28, textW(name, '600 11.5px Inter') + 6, b.kind === 'home' ? 16 : 30);
        labelled.add(s);
      }
    }
    // ring labels: "1 jump", "2 jumps", "3 jumps" and the tank's edge
    if (fades.rings > 0.15 && state.ringAnchors) {
      state.ringAnchors.forEach((a, k) => {
        if (!a) return;
        if (!S.project(a[0], refY, a[1], P)) return;
        const text = k === 3 ? 'This tank · ' + state.ship.fuel + ' jumps' : k === 0 ? '1 jump' : (k + 1) + ' jumps';
        const w = textW(text.toUpperCase(), '650 10px Inter', 0.8) + 16, h = 18;
        const x = P[0] - w / 2, y = P[1] - h / 2;
        if (x < 30 || y < 28 || x + w > vw || y + h > vh || hit(x, y, w, h)) return;
        add(x, y, w, h);
        put(el('ring' + k, 'ringlbl' + (k === 3 ? ' tank' : ''), esc(text)), x, y);
      });
    }
    // danger zones
    if (fades.glyph > 0.1) {
      M.DANGER.forEach((d, i) => {
        if (!S.project(d.centre[0], d.centre[1], d.centre[2], P)) return;
        const dy = d.centre[1] - refY;
        const rel = Math.abs(dy) < 3 ? 'at your height' : fmtLy(Math.abs(dy)) + (dy > 0 ? ' above you' : ' below you');
        const hostile = i !== M.STORM;
        const html = (hostile ? ICON.warn : ICON.storm) + `<span>${esc(d.name)}</span><small>${rel}</small>`;
        const w = 13 + 6 + textW(d.name, '600 11.5px Inter') + 6 + textW(rel, '500 11px Inter') + 22, h = 24;
        const x = P[0] - w / 2, y = P[1] - h / 2;
        if (x < 30 || y < 28 || x + w > vw || y + h > vh || hit(x, y, w, h)) return;
        add(x, y, w, h);
        put(el('zone' + i, 'zone ' + (hostile ? 'hostile' : 'hazard'), html), x, y);
      });
    }
    // star names, most important first
    if (localOn && W < 1400) {
      const budget = W < 45 ? 80 : W < 90 ? 60 : W < 200 ? 44 : W < 500 ? 28 : 14;
      const detail = fades.detail > 0.5;
      const order = state.labelOrder;
      let placed = 0;
      const lim = Math.min(order.length, W < 60 ? 37409 : 9000);
      for (let k = 0; k < lim && placed < budget; k++) {
        const s = order[k];
        if (labelled.has(s)) continue;
        if (M.tier[s] === 0 && W > 70 && !M.stationsBySys.has(s) && s !== state.selected && s !== state.target) continue;
        if (!S.project(M.X[s], M.Y[s], M.Z[s], P)) continue;
        if (P[0] < 0 || P[0] > vw || P[1] < 0 || P[1] > vh) continue;
        if (state.finder && !state.finderSet.has(s) && s !== state.selected) continue;
        const tank = inTank(s);
        const st = M.stationsBySys.has(s);
        const name = M.NAME[s];
        let html = '', w = textW(name, LABEL_FONT);
        if (st) { html += '<i class="g g-st"></i>'; w += 15; }
        html += `<span class="nm">${esc(name)}</span>`;
        if (detail && tank) {
          const land = M.landable(s);
          if (land > 0) { html += `<i class="g g-land">${land}</i>`; w += 12 + textW(String(land), '600 10.5px Inter'); }
          const rg = M.resourceGroups(s).slice(0, 3);
          if (rg.length) { html += rg.map(g => `<i class="g g-res" style="--c:${g.colour}"></i>`).join(''); w += 5 + 6 + (rg.length - 1) * 3; }
        }
        const r = starRadius(s, scale) + 2;
        const at = placeLabel('L' + s, P[0], P[1], w, 16, r, vw, vh);
        if (!at) continue;
        const cls = 'lbl' + (tank || state.finder ? '' : ' dim') + (s === state.selected ? ' sel' : '');
        const p = el('L' + s, cls, html);
        p.el.dataset.s = s;
        put(p, at[0], at[1]);
        placed++;
      }
    }
    // sectors (the 1,024 ly cubes at your height)
    if (fades.sector > 0.05) {
      const shipSector = M.sectorAt(sp[0], sp[1], sp[2]);
      for (const sec of M.SECTORS) {
        if (sec.sy !== M.SOL_SECTOR.sy) continue;
        if (!S.project(sec.x, refY, sec.z, P)) continue;
        if (P[0] < -100 || P[0] > vw + 100 || P[1] < -40 || P[1] > vh + 40) continue;
        const text = sec.name;
        const w = textW(text.toUpperCase(), '650 11px Inter', 2.2) + 2, h = 15;
        // inside the home sector the local stars sit on the centre: put its name above them
        const home = shipSector && sec.name === shipSector.name;
        let x = P[0] - w / 2, y = P[1] - h / 2;
        if (home) { const top = S.project(sec.x, refY, sec.z + 300, [0, 0]); if (top) y = Math.min(y - 70, top[1] - h); }
        if (x < 30 || y < 28 || x + w > vw - 4 || y + h > vh - 4 || hit(x, y, w, h)) continue;
        add(x, y, w, h);
        put(el('sec' + sec.name, 'sector' + (home ? ' home' : ''), esc(text)), x, y);
      }
    }
    // where the prototype's star data ends
    if (fades.sector > 0.3 && W < 9000) {
      if (S.project(0, refY, -150, P)) {
        const text = 'Charted here: every system within 150 ly';
        const w = textW(text, '500 11px Inter') + 4;
        const x = P[0] - w / 2, y = P[1] + 8;
        if (!hit(x, y, w, 16)) { add(x, y, w, 16); put(el('edge', 'edge-note', esc(text)), x, y); }
      }
    }
    // the galaxy's structures: arm names follow their arm and slide along it until they fit
    if (fades.galaxy > 0.12) {
      for (const st of M.STRUCTURES) {
        const L = st.label;
        const arm = st.path ? M.ARMS.find(a => a.id === st.path) : null;
        const text = st.name;
        const centre = st.kind === 'centre' || st.kind === 'bar';
        const font = centre ? '600 12px Inter' : '600 13px Inter';
        const w = textW(text.toUpperCase(), font, centre ? 2.9 : 4.2) + (centre ? 20 : 0), h = centre ? 20 : 16;
        const cands = [[L[0], L[1], L[2], -1]];
        let best = -1;
        if (arm) {
          let bd = Infinity;
          for (let k = 0; k < arm.line.length; k++) {
            const d = Math.hypot(arm.line[k][0] - L[0], arm.line[k][2] - L[2]);
            if (d < bd) { bd = d; best = k; }
          }
          for (const off of [5, -5, 10, -10, 15, -15, 20, -20]) {
            const k = best + off;
            if (k > 0 && k < arm.line.length - 1 && arm.line[k][3] > 0.35) cands.push([arm.line[k][0], arm.line[k][1], arm.line[k][2], k]);
          }
        }
        for (const c of cands) {
          if (!S.project(c[0], c[1], c[2], P)) continue;
          let angle = 0;
          if (arm && tilt < 0.5) {
            let k = c[3] >= 0 ? c[3] : best;
            k = Math.max(1, Math.min(arm.line.length - 2, k));
            const a = arm.line[k - 1], b = arm.line[k + 1];
            const pa = S.project(a[0], a[1], a[2], [0, 0]), pb = S.project(b[0], b[1], b[2], [0, 0]);
            if (pa && pb) {
              angle = Math.atan2(pb[1] - pa[1], pb[0] - pa[0]);
              if (angle > Math.PI / 2) angle -= Math.PI; else if (angle < -Math.PI / 2) angle += Math.PI;
            }
          }
          // an oriented label is tested as a row of small squares along its baseline
          const cos = Math.cos(angle), sin = Math.sin(angle);
          const n = Math.max(1, Math.ceil(w / h));
          const boxes = [];
          let ok = true;
          for (let i = 0; i < n && ok; i++) {
            const t = n === 1 ? 0 : -w / 2 + h / 2 + (i * (w - h)) / (n - 1);
            const cx = P[0] + cos * t, cy = P[1] + sin * t;
            const bx = cx - h / 2 - 1, by = cy - h / 2 - 1;
            if (bx < 30 || by < 28 || bx + h > vw - 4 || by + h > vh - 4 || hit(bx, by, h + 2, h + 2)) ok = false;
            boxes.push([bx, by]);
          }
          if (!ok) continue;
          for (const b of boxes) add(b[0], b[1], h + 2, h + 2);
          const p = el('arm' + st.id, 'arm' + (centre ? ' centre' : ''), esc(text));
          put(p, P[0], P[1], ` rotate(${angle}rad) translate(${-w / 2}px, ${-h / 2}px)`);
          break;
        }
      }
    }
    // hover tip
    if (state.hover >= 0 && !state.dragging && localOn) {
      const s = state.hover;
      if (S.project(M.X[s], M.Y[s], M.Z[s], P)) {
        const h = hopsTo(s);
        const meta = s === shipSys() ? 'you are here' : (h >= 0 ? jumpsText(h) : '') + (inTank(s) ? '' : ' · refuel on the way');
        put(el('hover', 'hover-tip', `${esc(M.NAME[s])}<span>${meta}</span>`), P[0] + 12, P[1] + 10);
      }
    }
    sweep();
    updateRulers(fades, tilt);
  }

  // ------------------------------------------------------------ atlas rulers
  const rx = $('ruler-x'), ry = $('ruler-y');
  const rulerX = [], rulerY = [];
  for (let i = 0; i < M.COLS; i++) {
    const a = document.createElement('span'); a.textContent = M.LETTERS[i]; rx.appendChild(a); rulerX.push(a);
    const b = document.createElement('span'); b.textContent = String(i + 1); ry.appendChild(b); rulerY.push(b);
  }
  let lastRulerKey = '';
  function updateRulers(fades, tilt) {
    const o = fades.grid * Math.max(0, 1 - tilt * 3);
    $('rulers').style.opacity = o.toFixed(3);
    if (o < 0.01) return;
    const v = S.view, vw = S.width, vh = S.height;
    const ship = M.cellIndex(state.shipPos[0], state.shipPos[2]);
    const hov = state.hoverCell;
    for (let i = 0; i < M.COLS; i++) {
      const c = M.cellCentre(i, 0);
      const p = S.project(c[0], v.refY, v.cz, [0, 0]);
      const e = rulerX[i];
      const show = p && p[0] > 40 && p[0] < vw - 10;
      e.style.display = show ? '' : 'none';
      if (show) e.style.left = p[0].toFixed(1) + 'px';
      e.className = ship && ship.i === i ? 'you' : hov && hov.i === i ? 'hov' : '';
    }
    for (let j = 0; j < M.COLS; j++) {
      const c = M.cellCentre(0, j);
      const p = S.project(v.cx, v.refY, c[1], [0, 0]);
      const e = rulerY[j];
      const show = p && p[1] > 34 && p[1] < vh - 10;
      e.style.display = show ? '' : 'none';
      if (show) e.style.top = p[1].toFixed(1) + 'px';
      e.className = ship && ship.j === j ? 'you' : hov && hov.j === j ? 'hov' : '';
    }
  }

  // ------------------------------------------------------------ breadcrumbs and scale
  let crumbKey = '';
  function updateCrumbs() {
    const v = S.view;
    const items = [{ label: 'Milky Way', level: 'galaxy' }];
    if (v.w < 60000) items.push({ label: M.regionAt(v.cx, v.cz), level: 'arm' });
    const sec = M.sectorAt(v.cx, state.shipPos[1], v.cz);
    if (v.w < 9000 && sec) items.push({ label: sec.name, level: 'sector', sector: sec });
    const cell = M.cellIndex(v.cx, v.cz);
    if (v.w < 500 && cell) items.push({ label: cell.name, level: 'cell', cell });
    const key = items.map(i => i.label).join('|');
    if (key === crumbKey) return;
    crumbKey = key;
    const nav = $('crumbs');
    nav.innerHTML = items.map((it, k) => (k ? ICON.chev : '') + `<button data-k="${k}" class="${k === items.length - 1 ? 'cur' : ''}${it.level === 'cell' ? ' cell' : ''}">${esc(it.label)}</button>`).join('');
    nav.querySelectorAll('button').forEach(b => {
      b.onclick = () => { const it = items[+b.dataset.k]; if (+b.dataset.k < items.length - 1) act.zoomToLevel(it); };
    });
    state.panelsDirty = true;
  }
  let scaleKey = '';
  function updateScale() {
    const lyPerPx = S.view.w / S.width;
    const target = 110 * lyPerPx;
    const p10 = Math.pow(10, Math.floor(Math.log10(target)));
    let len = p10;
    for (const m of [1, 2, 5]) if (m * p10 <= target) len = m * p10;
    const px = len / lyPerPx;
    const key = len + '|' + px.toFixed(0);
    if (key === scaleKey) return;
    scaleKey = key;
    $('scale-label').textContent = len >= 1000 ? nf.format(len) + ' ly' : len + ' ly';
    $('scale-bar').style.width = px.toFixed(0) + 'px';
    $('scale').style.opacity = S.view.tilt > 0.3 ? 0 : 1;
  }

  // ------------------------------------------------------------ status (bottom left)
  function renderStatus() {
    const s = state.ship.sys;
    const cell = M.cellName(state.shipPos[0], state.shipPos[2]) || '—';
    const ap = state.autopilot;
    const fuel = state.ship.fuel;
    const dst = state.route ? (M.stationsBySys.get(state.route.to) || [{ name: M.NAME[state.route.to] }])[0].name : '';
    const where = ap && ap.running ? 'Autopilot · to ' + esc(dst) : esc(M.NAME[s]) + (M.basesBySys.has(s) && M.basesBySys.get(s).some(b => b.kind === 'home') ? ' · home' : '');
    let cells = '';
    for (let k = 0; k < M.TANK; k++) cells += `<i class="${k < fuel ? '' : 'off'}"></i>`;
    const target = state.target >= 0 ? `<span>Target · ${esc(M.NAME[state.target])} · ${jumpsText(Math.max(0, hopsTo(state.target)))}</span>` : '';
    $('status').innerHTML = `
      <div class="you"><small>You</small><b>${cell}</b></div>
      <div class="ship"><b>${esc(M.SHIP.name)}</b><span>${where}</span>${target}
        <div class="gauge"><div class="cells">${cells}</div><span><b>${fuel}</b> of ${M.TANK} jumps · ${M.JUMP} ly each</span></div>
      </div>`;
    state.panelsDirty = true;
  }

  // ------------------------------------------------------------ star card
  const TYPE_COLOUR = {
    'Rocky world': '#b39b84', 'Metal-rich world': '#c9a477', 'Earth-like world': '#5fb59a', 'Water world': '#4f8fe0',
    'Icy world': '#d3e4f0', 'Rocky ice world': '#9fb4c4', 'Ammonia world': '#c8b27a', 'Gas giant': '#d8b48e', 'Ice giant': '#8fc3dd',
  };
  function bodyResources(s, bi) {
    const list = state.bodyRes.get(s);
    if (!list) return [];
    const e = list.find(b => b[1] === bi);
    if (!e) return [];
    return M.RES_GROUPS.filter(g => e[2] & g.mask);
  }
  function orreryHtml(s) {
    const bodies = M.BODIES[String(s)];
    if (!bodies) return null;
    const slots = [];
    bodies.forEach((b, i) => {
      if (i === 0) return;
      const kind = b[1], parent = b[3];
      if (kind === 1 || kind === 3 || (kind === 0 && parent <= 0)) slots.push(i);
    });
    const width = 322, x0 = 44, x1 = width - 14;
    const step = slots.length > 1 ? (x1 - x0) / (slots.length - 1) : 0;
    const moonsOf = new Map();
    bodies.forEach((b, i) => { if (b[1] === 2) { if (!moonsOf.has(b[3])) moonsOf.set(b[3], []); moonsOf.get(b[3]).push(i); } });
    const starCol = M.COL[s];
    let html = `<div class="orrery"><div class="line"></div><div class="sun" style="background:radial-gradient(circle at 70% 50%, ${starCol}, ${starCol}cc 55%, ${starCol}00 72%)"></div>`;
    const home = M.HOME.sys === s ? M.HOME.bodyIndex : -1;
    slots.forEach((i, k) => {
      const b = bodies[i];
      const x = slots.length > 1 ? x0 + k * step : (x0 + x1) / 2;
      if (b[1] === 3) { html += `<div class="belt" style="left:${x}px" data-b="${i}"></div>`; return; }
      let d = b[1] === 0 ? 14 : Math.max(5, Math.min(22, 4 + Math.sqrt(b[5] / 1000) * 3.1));
      if (slots.length > 10) d = Math.min(d, 18);
      const col = b[1] === 0 ? b[2] && M.COL[s] : TYPE_COLOUR[b[2]] || '#aab';
      const bg = b[1] === 0 ? `radial-gradient(circle, #fff, ${col})` : `radial-gradient(circle at 35% 35%, ${col}, ${col}99 70%, ${col}66)`;
      const land = b[9] ? ' land' : '';
      const ring = b[10] ? `<div class="ringd" style="width:${d * 1.9}px;height:${d * 1.9}px"></div>` : '';
      const shadow = b[9] ? 'box-shadow:0 0 0 2px #0b1017, 0 0 0 3.2px rgba(107,204,255,.85);' : '';
      html += `<div class="pl${land}" data-b="${i}" style="left:${x}px;width:${d}px;height:${d}px;background:${bg};${shadow}">${ring}</div>`;
      const moons = (moonsOf.get(i) || []).slice(0, 4);
      moons.forEach((m, mk) => { html += `<div class="moon" style="left:${x}px;top:${30 + d / 2 + 6 + mk * 5}px"></div>`; });
      if (i === home) html += `<div class="moon" style="left:${x}px;top:${30 - d / 2 - 12}px;width:7px;height:7px;background:#fff;box-shadow:0 0 0 2px #0b1017"></div>`;
    });
    html += '</div>';
    return html;
  }
  function bodyNote(s, i) {
    const b = M.BODIES[String(s)][i];
    if (!b) return '';
    if (b[1] === 3) return `<b>${esc(b[0])}</b> · asteroid belt at ${b[4]} AU`;
    const bits = [esc(b[2])];
    if (b[1] !== 0) {
      bits.push(b[6] < 0.1 ? b[6].toFixed(3) + ' g' : b[6].toFixed(2) + ' g');
      bits.push(Math.round(b[7]) + ' K');
      if (b[8] !== 'none') bits.push(b[8] + ' atmosphere');
      bits.push(b[9] ? '<span style="color:var(--accent)">landable</span>' : 'not landable');
    }
    const res = bodyResources(s, i).map(g => g.label.toLowerCase());
    const moons = M.BODIES[String(s)].filter(m => m[1] === 2 && m[3] === i).length;
    if (moons) bits.push(moons + (moons === 1 ? ' moon' : ' moons'));
    let base = '';
    if (M.HOME.sys === s && M.HOME.bodyIndex === i) base = ' · <b>your base</b>';
    return `<b>${esc(b[0])}</b> · ${bits.join(' · ')}${res.length ? ' · ' + res.join(', ') : ''}${base}`;
  }
  function renderCard() {
    const card = $('card');
    const s = state.selected;
    if (s < 0) { card.classList.add('hidden'); state.panelsDirty = true; return; }
    const here = s === shipSys();
    const d = M.dist(shipSys(), s);
    const h = hopsTo(s);
    const tank = inTank(s);
    const dy = M.Y[s] - state.shipPos[1];
    const cell = M.cellName(M.X[s], M.Z[s]);
    const kind = M.starKind(M.CLS[s]);
    const sum = M.SUM[s];
    const rows = [];
    if (M.canScoop(s)) rows.push(`<div class="row fuel"><div class="ic">${ICON.fuel}</div><div><b>Fuel</b><p>Scoopable ${kind.toLowerCase()}: the tank refills here</p></div></div>`);
    else rows.push(`<div class="row nofuel"><div class="ic">${ICON.nofuel}</div><div><b>No fuel</b><p>${M.inStorm(s) ? 'The ion storm makes scooping impossible' : 'A ' + kind.toLowerCase() + ' cannot be scooped'}</p></div></div>`);
    for (const st of M.stationsBySys.get(s) || []) {
      rows.push(`<div class="row station"><div class="ic">${ICON.station}</div><div><b>${esc(st.name)}</b><p>${st.type === 'outpost' ? 'Surface outpost on ' : st.type === 'depot' ? 'Deep-space depot near ' : 'Orbital station at '}${esc(st.body || st.system)}</p><div class="svc">${st.services.map(x => `<span>${esc(x[0].toUpperCase() + x.slice(1))}</span>`).join('')}</div></div></div>`);
    }
    for (const b of M.basesBySys.get(s) || []) {
      rows.push(`<div class="row base ${b.relation}"><div class="ic">${b.kind === 'home' ? ICON.home : ICON.pin}</div><div><b>${esc(b.label)}</b><p>${b.kind === 'home' ? '' : b.relation[0].toUpperCase() + b.relation.slice(1) + ' · '}on ${esc(b.body)}, ${esc(b.bodyType.toLowerCase())}</p></div></div>`);
    }
    if (M.dangerOf[s] >= 0) {
      const z = M.DANGER[M.dangerOf[s]];
      const hz = M.dangerOf[s] === M.STORM;
      rows.push(`<div class="row ${hz ? 'hazard' : 'danger'}"><div class="ic">${hz ? ICON.storm : ICON.warn}</div><div><b>${esc(z.name)}</b><p>${esc(z.effect)}</p></div></div>`);
    }
    const orrery = orreryHtml(s);
    let worlds = '';
    const counts = `${sum[1]} planet${sum[1] === 1 ? '' : 's'} · ${sum[3]} landable`;
    if (orrery) {
      worlds = `<div class="sect">Worlds <span>${counts}</span></div>${orrery}<div class="orbit-note" id="onote">Point at a world for details. Ringed outline: landable.</div>`;
    } else {
      const pills = [];
      if (sum[0] > 1) pills.push(`<span><b>${sum[0]}</b>stars</span>`);
      pills.push(`<span><b>${sum[1]}</b>planets</span>`);
      if (sum[2]) pills.push(`<span><b>${sum[2]}</b>moons</span>`);
      pills.push(`<span><b>${sum[3]}</b>landable</span>`);
      if (sum[4]) pills.push(`<span><b>${sum[4]}</b>gas giant${sum[4] > 1 ? 's' : ''}</span>`);
      if (sum[5]) pills.push(`<span><b>${sum[5]}</b>ice giant${sum[5] > 1 ? 's' : ''}</span>`);
      if (sum[6]) pills.push(`<span><b>${sum[6]}</b>earth-like</span>`);
      if (sum[7]) pills.push(`<span><b>${sum[7]}</b>water world${sum[7] > 1 ? 's' : ''}</span>`);
      if (sum[9]) pills.push(`<span><b>${sum[9]}</b>belt${sum[9] > 1 ? 's' : ''}</span>`);
      worlds = `<div class="sect">Worlds <span>summary</span></div><div class="summary">${pills.join('')}</div>`;
    }
    const rg = M.resourceGroups(s);
    const res = rg.length ? `<div class="sect">Resources <span>on landable worlds</span></div><div class="reslist">${rg.map(g => `<span style="--c:${g.colour}"><i></i>${g.label}</span>`).join('')}</div>` : '';
    const jumpFact = here
      ? '<div class="in"><b>Here</b><span>you are here</span></div>'
      : tank ? `<div class="in"><b>${jumpsText(h)}</b><span>on this tank</span></div>` : `<div class="warm"><b>${jumpsText(h)}</b><span>refuel on the way</span></div>`;
    const heightFact = `<div><b>${Math.abs(dy) < 0.5 ? 'Level' : fmtLy(Math.abs(dy)) + (dy > 0 ? ' ↑' : ' ↓')}</b><span>${Math.abs(dy) < 0.5 ? 'with you' : dy > 0 ? 'above you' : 'below you'}</span></div>`;
    const fav = state.favourites.has(s);
    card.innerHTML = `
      <div class="p-head">
        <div class="overline">${here ? 'You are here' : 'Star system'} · <span class="cellref">${cell || 'off chart'}</span></div>
        <h2>${esc(M.NAME[s])}</h2>
        <div class="sub">${esc(M.CLS[s])} · ${esc(kind)}${M.CAT[s] ? ' · catalogue star' : ''}</div>
        <button class="iconbtn x" data-a="close" title="Close (Esc)">${ICON.close}</button>
      </div>
      <div class="p-body">
        <div class="facts"><div><b>${here ? '0 ly' : fmtLy(d)}</b><span>from you</span></div>${jumpFact}${heightFact}</div>
        <div class="rows">${rows.join('')}</div>
        ${worlds}${res}
      </div>
      <div class="actions">
        <button class="btn primary" data-a="route" ${here ? 'disabled style="opacity:.45;pointer-events:none"' : ''}>${ICON.route.replace('class="ico"', '')}Route</button>
        <button class="btn${state.target === s ? ' on' : ''}" data-a="target">${ICON.target.replace('class="ico"', '')}${state.target === s ? 'Targeted' : 'Target'}</button>
        <button class="btn square fav${fav ? ' on' : ''}" data-a="fav" title="Favourite">${ICON.star.replace('class="ico"', '')}</button>
      </div>`;
    if (card.classList.contains('hidden') && RM.enter) RM.enter(card);
    card.classList.remove('hidden');
    card.querySelectorAll('[data-a]').forEach(b => {
      b.onclick = () => {
        const a = b.dataset.a;
        if (a === 'close') act.select(-1);
        else if (a === 'route') act.route(s);
        else if (a === 'target') act.target(state.target === s ? -1 : s);
        else if (a === 'fav') act.favourite(s);
      };
    });
    const note = card.querySelector('#onote');
    if (note) {
      const def = note.innerHTML;
      card.querySelectorAll('.orrery [data-b]').forEach(p => {
        p.onmouseenter = () => { note.innerHTML = bodyNote(s, +p.dataset.b); };
        p.onmouseleave = () => { note.innerHTML = state.pinnedBody >= 0 ? bodyNote(s, state.pinnedBody) : def; };
        p.onclick = () => {
          card.querySelectorAll('.orrery .act').forEach(x => x.classList.remove('act'));
          state.pinnedBody = +p.dataset.b; p.classList.add('act'); note.innerHTML = bodyNote(s, state.pinnedBody);
        };
      });
      state.pinnedBody = -1;
      if (M.HOME.sys === s) { note.innerHTML = bodyNote(s, M.HOME.bodyIndex); }
    }
    state.panelsDirty = true;
  }

  // ------------------------------------------------------------ route: the subway line
  function renderRoute() {
    const box = $('route');
    const R = state.route;
    document.getElementById('legend').classList.toggle('beside', !!R);
    if (!R) { box.classList.add('hidden'); state.panelsDirty = true; updateScalePos(); return; }
    const ap = state.autopilot;
    const to = R.to;
    const st = M.stationsBySys.get(to);
    const dest = st ? st[0].name : M.NAME[to];
    const sub = st ? M.NAME[to] + ' · ' + (st[0].type === 'outpost' ? 'surface outpost' : st[0].type === 'depot' ? 'depot' : 'orbital station') : M.starKind(M.CLS[to]) + ' · ' + (M.cellName(M.X[to], M.Z[to]) || '');
    const n = R.stops.length;
    const notable = i => i === 0 || i === n - 1 || R.stops[i].refuel || R.stops[i].station || R.stops[i].danger || R.stops[i].bases || (ap && (i === ap.leg || i === ap.leg + 1));
    const rows = [];
    let i = 0;
    while (i < n) {
      if (!state.routeExpanded && n > 9 && !notable(i)) {
        let j = i;
        while (j < n && !notable(j)) j++;
        if (j - i >= 2) {
          const ly = R.stops[j - 1].total - R.stops[i - 1].total;
          const done = ap && ap.leg >= j - 1 ? ' done' : '';
          rows.push(`<div class="st more${done}" data-more="1"><div class="rail"></div><div class="tx"><b>··· ${j - i} more stops</b></div><span class="leg">${fmtLy(ly)}</span></div>`);
          i = j;
          continue;
        }
      }
      const s = R.stops[i];
      const cls = ['st'];
      if (i === 0) cls.push('start');
      if (i === n - 1) cls.push('end');
      if (s.refuel) cls.push('refuel');
      if (s.station) cls.push('station');
      if (s.danger) cls.push('danger');
      if (ap) { if (i <= ap.leg) cls.push('done'); if (i === ap.leg && ap.t > 0 && i < n - 1) cls.push('half'); if (i === ap.leg && !ap.t) cls.push('cur'); }
      const tags = [];
      if (ap && ap.running && i === ap.leg + 1) tags.push('<span class="tag station">Next</span>');
      if (s.refuel) tags.push(`<span class="tag fuel">${ICON.fuel.replace('class="ico"', 'class="ico" style="width:10px;height:10px"')}Refuel</span>`);
      if (s.station) tags.push(`<span class="tag station">${esc(s.station[0].name)}</span>`);
      if (s.danger) tags.push(`<span class="tag ${s.danger.kind === 'hazard' ? 'hazard' : 'danger'}">${esc(s.danger.name)}</span>`);
      if (s.bases) for (const b of s.bases) tags.push(`<span class="tag base">${esc(b.kind === 'home' ? 'Your base' : b.label)}</span>`);
      let meta = '';
      if (i === 0) meta = ap && ap.leg > 0 ? 'Departed' : 'You are here';
      else if (i === n - 1) meta = M.starKind(M.CLS[s.s]) + ' · destination';
      else meta = M.starKind(M.CLS[s.s]) + (M.canScoop(s.s) ? '' : ' · no fuel');
      const dot = s.refuel ? `<i>${ICON.fuel.replace('class="ico"', '')}</i>` : '<i></i>';
      rows.push(`<div class="${cls.join(' ')}" data-s="${s.s}"><div class="rail">${dot}</div><div class="tx"><b>${esc(M.NAME[s.s])}</b><span>${meta}</span>${tags.length ? `<div class="tags">${tags.join('')}</div>` : ''}</div>${i ? `<span class="leg">${fmtLy(s.leg)}</span>` : ''}</div>`);
      i++;
    }
    const running = ap && ap.running, arrived = ap && ap.arrived;
    const pct = ap ? Math.round(100 * (state.routeProgress || 0) / R.ly) : 0;
    const legsLeft = ap ? Math.max(0, R.jumps - ap.leg - (ap.t > 0 ? 1 : 0)) : R.jumps;
    const foot = arrived
      ? `<button class="btn primary" data-a="done">Arrived · Done</button><div class="note">${esc(dest)} reached in ${R.jumps} jumps.</div>`
      : running
        ? `<div class="progress"><i style="width:${pct}%"></i></div><button class="btn" data-a="stop">${ICON.stop}Stop autopilot</button><div class="note">Jump ${Math.min(R.jumps, ap.leg + 1)} of ${R.jumps} · ${legsLeft} to go</div>`
        : `<button class="btn primary" data-a="go">${ICON.play}Start autopilot</button><div class="note">Flies every jump and scoops fuel on the way. You can take the stick at any stop.</div>`;
    box.innerHTML = `
      <div class="p-head">
        <div class="overline">Route · <span class="cellref">${esc(M.cellName(M.X[to], M.Z[to]) || 'off chart')}</span></div>
        <h2>${esc(dest)}</h2>
        <div class="sub">${esc(sub)}</div>
        <button class="iconbtn x" data-a="close" title="Close route (Esc)">${ICON.close}</button>
      </div>
      <div class="r-stats">
        <div><b>${R.jumps}</b><span>jumps</span></div>
        <div><b>${fmtLy(R.ly)}</b><span>flown</span></div>
        <div><b>${fmtTime(R.seconds)}</b><span>ETA</span></div>
        <div class="${R.refuels ? 'warm' : ''}"><b>${R.refuels}</b><span>refuel${R.refuels === 1 ? '' : 's'}</span></div>
      </div>
      <div class="r-line">${rows.join('')}</div>
      <div class="r-foot">${foot}</div>`;
    if (box.classList.contains('hidden') && RM.enter) RM.enter(box);
    box.classList.remove('hidden');
    box.querySelectorAll('[data-a]').forEach(b => {
      b.onclick = () => {
        const a = b.dataset.a;
        if (a === 'close') act.clearRoute();
        else if (a === 'go') act.autopilot(true);
        else if (a === 'stop') act.autopilot(false);
        else if (a === 'done') act.clearRoute();
      };
    });
    box.querySelectorAll('.st[data-s]').forEach(r => { r.onclick = () => act.select(+r.dataset.s, true); });
    box.querySelectorAll('.st[data-more]').forEach(r => { r.onclick = () => { state.routeExpanded = true; renderRoute(); }; });
    const cur = box.querySelector('.st.cur, .st.half');
    if (cur && running) cur.scrollIntoView({ block: 'nearest' });
    state.panelsDirty = true;
    updateScalePos();
  }
  function updateScalePos() { $('scale').style.right = 'calc(var(--edge) + ' + ($('controls').offsetWidth + 18) + 'px)'; }

  // ------------------------------------------------------------ legend
  function renderLegend() {
    const sw = inner => `<span class="sw"><svg viewBox="0 0 30 20">${inner}</svg></span>`;
    const A = '#6bccff';
    $('legend').innerHTML = `
      <h3>Reach</h3>
      <div class="lg">${sw(`<path d="M2 16 A13 13 0 0 1 28 16" fill="none" stroke="${A}" stroke-width="1.6"/>`)}<span><b>Rings</b> · where 1, 2 and 3 jumps take you</span></div>
      <div class="lg">${sw(`<rect x="1" y="3" width="28" height="14" rx="4" fill="rgba(107,204,255,.16)" stroke="rgba(107,204,255,.5)"/>`)}<span><b>Lit area</b> · reachable on this tank</span></div>
      <div class="lg">${sw(`<circle cx="15" cy="10" r="3" fill="#7c8592" opacity=".7"/>`)}<span><b>Grey</b> · needs a refuel stop first</span></div>
      <h3>Stars</h3>
      <div class="lg-grid">
        <div class="lg">${sw(`<circle cx="15" cy="10" r="3.6" fill="#ffd9a8"/>`)}<span>Fuel: scoopable</span></div>
        <div class="lg">${sw(`<circle cx="15" cy="10" r="3" fill="none" stroke="#e6edf6" stroke-width="1.4"/>`)}<span>No fuel</span></div>
        <div class="lg">${sw(`<circle cx="15" cy="10" r="2.6" fill="#ffd9a8"/><rect x="9.3" y="4.3" width="11.4" height="11.4" rx="2" fill="none" stroke="${A}" stroke-width="1.4"/>`)}<span>Station</span></div>
        <div class="lg">${sw(`<circle cx="15" cy="10" r="2.6" fill="#ffd9a8"/><circle cx="19.6" cy="14.6" r="1.6" fill="#e2eaf5"/>`)}<span>Landable worlds</span></div>
        <div class="lg">${sw(`<circle cx="15" cy="11" r="2.6" fill="#ffd9a8"/><path d="M15 7v-5" stroke="#ffe7c8" stroke-width="1.3" stroke-linecap="round"/>`)}<span>Above you</span></div>
        <div class="lg">${sw(`<circle cx="15" cy="9" r="2.6" fill="#ffd9a8"/><path d="M15 13v5" stroke="#ffe7c8" stroke-width="1.3" stroke-linecap="round"/>`)}<span>Below you</span></div>
        <div class="lg">${sw(`<circle cx="15" cy="10" r="8" fill="rgba(255,92,102,.16)" stroke="#ff5c66" stroke-width="1.4"/><circle cx="15" cy="10" r="2.6" fill="#ffd9a8"/>`)}<span>Hostile space</span></div>
        <div class="lg">${sw(`<circle cx="15" cy="10" r="8" fill="rgba(255,179,82,.16)" stroke="#ffb352" stroke-width="1.4"/><circle cx="15" cy="10" r="2.6" fill="#ffd9a8"/>`)}<span>Ion storm</span></div>
      </div>
      <h3>Bases</h3>
      <div class="lg-grid">
        <div class="lg"><span class="sw">${pinSvg('home').replace('<svg', '<svg style="width:14px;height:19px"')}</span><span>Your base</span></div>
        <div class="lg"><span class="sw">${pinSvg('friendly').replace('<svg', '<svg style="width:14px;height:19px"')}</span><span>Friendly</span></div>
        <div class="lg"><span class="sw">${pinSvg('neutral').replace('<svg', '<svg style="width:14px;height:19px"')}</span><span>Neutral</span></div>
        <div class="lg"><span class="sw">${pinSvg('hostile').replace('<svg', '<svg style="width:14px;height:19px"')}</span><span>Hostile</span></div>
      </div>
      <h3>Resources</h3>
      <div class="lg-grid">${M.RES_GROUPS.map(g => `<div class="lg">${sw(`<circle cx="15" cy="10" r="3.4" fill="${g.colour}"/>`)}<span>${g.label}</span></div>`).join('')}</div>
      <div class="foot">Squares are ${M.CELL} ly, about one jump: “meet at G7”. Drag to pan, scroll or pinch to zoom, <kbd>T</kbd> 3D, <kbd>/</kbd> search, <kbd>Esc</kbd> back.<br>Stations, bases, players, danger, resources and the ship are mock data.</div>`;
  }

  // ------------------------------------------------------------ search
  const q = $('q'), results = $('results'), box = $('searchbox');
  let resultItems = [], active = 0;
  const CAT_ICON = { stations: ICON.station, landable: ICON.planet, water: ICON.water, metals: ICON.metal, organics: ICON.leaf, rare: ICON.gem };
  function hl(name, query) {
    const i = name.toLowerCase().indexOf(query.toLowerCase());
    if (i < 0 || !query) return esc(name);
    return esc(name.slice(0, i)) + '<em>' + esc(name.slice(i, i + query.length)) + '</em>' + esc(name.slice(i + query.length));
  }
  function sysMeta(s) {
    if (s === shipSys()) return { text: 'Here', cls: 'in' };
    const h = hopsTo(s);
    return { text: jumpsText(h), cls: inTank(s) ? 'in' : '' };
  }
  function sysRow(s, title, sub, icon) {
    const m = sysMeta(s);
    return { s, html: `<div class="ic">${icon || `<i class="dot" style="background:${M.COL[s]}"></i>`}</div><div class="tx"><b>${title}</b><span>${sub}</span></div><div class="meta ${m.cls}">${m.text}</div>` };
  }
  function renderResults() {
    const text = q.value;
    resultItems = [];
    let html = '';
    if (state.finder) {
      const f = state.finderResult;
      html += `<div class="res-h">Nearest by jumps<span>${nf.format(f.inTank)} on this tank · ${nf.format(f.count)} charted</span></div>`;
      for (const r of f.list) {
        const s = r.s;
        const st = M.stationsBySys.get(s);
        const title = st ? esc(st[0].name) : esc(M.NAME[s]);
        const sub = (st ? esc(M.NAME[s]) + ' · ' : '') + fmtLy(r.d) + ' · ' + (M.cellName(M.X[s], M.Z[s]) || '');
        resultItems.push(sysRow(s, title, sub, st ? ICON.station : null));
      }
    } else if (!text.trim()) {
      html += '<div class="res-h">Find nearby</div><div class="cats">' + M.CATEGORIES.map(c => {
        const n = state.categoryCounts[c.id];
        return `<button class="cat" data-cat="${c.id}"><span class="ic">${CAT_ICON[c.id]}</span><b>${c.label}</b><span>${n} in reach</span></button>`;
      }).join('') + '</div>';
      const places = [];
      for (const b of M.bases) places.push(sysRow(b.sys, esc(b.kind === 'home' ? 'Your base' : b.label), esc(M.NAME[b.sys]) + ' · ' + b.relation, pinSvg(b.relation).replace('<svg', '<svg style="width:12px;height:16px"')));
      for (const s of state.favourites) places.push(sysRow(s, esc(M.NAME[s]), 'Favourite · ' + fmtLy(M.dist(shipSys(), s)), ICON.star.replace('class="ico"', 'class="ico" style="color:#ffd66e"')));
      if (places.length) html += '<div class="res-h">Your places</div>';
      resultItems = places;
    } else {
      for (const r of M.search(text, shipSys(), 8)) {
        if (r.kind === 'system') {
          const s = r.s;
          resultItems.push(sysRow(s, hl(M.NAME[s], text), esc(M.CLS[s]) + ' · ' + fmtLy(M.dist(shipSys(), s)) + ' · ' + (M.cellName(M.X[s], M.Z[s]) || 'off chart')));
        } else if (r.kind === 'station') {
          const s = r.s, st = r.station;
          resultItems.push(sysRow(s, hl(st.name, text), 'Station · ' + esc(st.system) + ' · ' + fmtLy(M.dist(shipSys(), s)), ICON.station));
        } else if (r.kind === 'base') {
          resultItems.push(sysRow(r.s, hl(r.base.kind === 'home' ? 'Your base' : r.base.label, text), esc(M.NAME[r.s]) + ' · ' + r.base.relation, pinSvg(r.base.relation).replace('<svg', '<svg style="width:12px;height:16px"')));
        } else if (r.kind === 'cell') {
          const c = r.cell;
          resultItems.push({ cell: c, html: `<div class="ic">${ICON.grid}</div><div class="tx"><b>Square ${c.name}</b><span>${M.CELL} × ${M.CELL} ly on the Nolcas Peris chart</span></div><div class="meta"></div>` });
        } else if (r.kind === 'sector') {
          resultItems.push({ sector: r.sector, html: `<div class="ic">${ICON.sector}</div><div class="tx"><b>${hl(r.sector.name, text)}</b><span>Sector · 1,024 ly cube</span></div><div class="meta">${fmtLy(Math.hypot(r.sector.x, r.sector.z))}</div>` });
        } else if (r.kind === 'structure') {
          resultItems.push({ structure: r.structure, html: `<div class="ic">${ICON.spiral}</div><div class="tx"><b>${hl(r.structure.name, text)}</b><span>${r.structure.kind === 'arm' ? 'Spiral arm' : r.structure.kind === 'spur' ? 'Spur' : 'Galactic structure'}</span></div><div class="meta"></div>` });
        }
      }
      if (!resultItems.length) html += `<div class="res-h">No matches<span>try a star, station, base or square like G7</span></div>`;
    }
    active = Math.min(active, Math.max(0, resultItems.length - 1));
    html += resultItems.map((r, k) => `<div class="res${k === active && (text.trim() || state.finder) ? ' act' : ''}" data-k="${k}">${r.html}</div>`).join('');
    results.innerHTML = html;
    results.querySelectorAll('.res').forEach(e => {
      e.onmousedown = ev => { ev.preventDefault(); choose(+e.dataset.k); };
      e.onmouseenter = () => { if (state.finder) act.preview(resultItems[+e.dataset.k].s); };
    });
    results.querySelectorAll('.cat').forEach(e => { e.onmousedown = ev => { ev.preventDefault(); act.finder(e.dataset.cat); }; });
    state.panelsDirty = true;
  }
  function choose(k) {
    const r = resultItems[k];
    if (!r) return;
    if (r.cell) act.goCell(r.cell);
    else if (r.sector) act.goSector(r.sector);
    else if (r.structure) act.goStructure(r.structure);
    else act.select(r.s, true);
    if (!state.finder) { q.blur(); closeSearch(); }
  }
  function openSearch() { box.classList.add('open'); renderResults(); }
  function closeSearch() {
    box.classList.remove('open');
    if (!state.finder) results.innerHTML = '';
    state.panelsDirty = true;
  }
  function syncToken() {
    const tok = $('token');
    if (state.finder) {
      tok.innerHTML = esc(state.finderResult.cat.label) + ` <span style="opacity:.7;cursor:pointer" id="tokx">×</span>`;
      tok.classList.remove('hidden');
      q.placeholder = '';
      $('tokx').onmousedown = e => { e.preventDefault(); act.finder(null); };
    } else {
      tok.classList.add('hidden');
      q.placeholder = 'Search stars, stations, squares';
    }
    $('q-clear').classList.toggle('hidden', !(q.value || state.finder));
    $('q-kbd').classList.toggle('hidden', !!(q.value || state.finder || document.activeElement === q));
  }
  q.addEventListener('focus', () => { openSearch(); syncToken(); });
  q.addEventListener('blur', () => { setTimeout(() => { if (document.activeElement !== q && !state.finder) closeSearch(); syncToken(); }, 120); });
  q.addEventListener('input', () => { active = 0; if (state.finder && q.value) act.finder(null, true); renderResults(); syncToken(); });
  q.addEventListener('keydown', e => {
    if (e.key === 'ArrowDown') { active = Math.min(resultItems.length - 1, active + 1); renderResults(); e.preventDefault(); }
    else if (e.key === 'ArrowUp') { active = Math.max(0, active - 1); renderResults(); e.preventDefault(); }
    else if (e.key === 'Enter') { choose(active); e.preventDefault(); }
    else if (e.key === 'Escape') {
      e.preventDefault(); e.stopPropagation();
      if (q.value) { q.value = ''; renderResults(); }
      else if (state.finder) act.finder(null);
      else { q.blur(); closeSearch(); }
      syncToken();
    } else if (e.key === 'Backspace' && !q.value && state.finder) { act.finder(null); }
  });
  $('q-clear').onclick = () => { q.value = ''; if (state.finder) act.finder(null); renderResults(); syncToken(); q.focus(); };

  // ------------------------------------------------------------ toast
  let toastTimer = 0;
  function toast(msg, warm) {
    const t = $('toast');
    t.innerHTML = `<i></i>${esc(msg)}`;
    t.classList.toggle('warm', !!warm);
    t.classList.add('on');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => t.classList.remove('on'), 2600);
  }

  renderLegend();
  return {
    updateOverlay, updateCrumbs, updateScale, renderStatus, renderCard, renderRoute, renderResults, openSearch, closeSearch, syncToken,
    toast, focusSearch: () => q.focus(), searchInput: q, fmtLy, jumpsText, updateScalePos,
  };
};
