/* Hollow Universe map, concept A "Search & Go": the interface.
   One search field drives everything: places and plain-language intents, a place card, an itinerary, a system view. */
(function () {
  'use strict';
  const H = window.HD, V = window.MapView;
  const $ = (s, el) => (el || document).querySelector(s);
  const esc = s => String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
  const fmt = new Intl.NumberFormat('en-GB');
  const DEG = Math.PI / 180;
  const App = window.App = {};
  const st = V.state;

  // ---------- icons ----------
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
    zoomin: '<svg viewBox="0 0 16 16"><circle cx="7" cy="7" r="4.6" fill="none" stroke="currentColor" stroke-width="1.5"/><path d="m10.4 10.4 3.4 3.4M7 5v4M5 7h4" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/></svg>'
  };
  const intentIcon = id => I[id] || I.star;

  // ---------- persistent bits (per viewer only; the page works without them) ----------
  const store = {
    get(k, d) { try { const v = localStorage.getItem('hollowA.' + k); return v ? JSON.parse(v) : d; } catch (e) { return d; } },
    set(k, v) { try { localStorage.setItem('hollowA.' + k, JSON.stringify(v)); } catch (e) { /* ignore */ } }
  };
  const byName = n => { const q = H.norm(n); for (let i = 0; i < H.N; i++) if (H.norm(H.NAME[i]) === q) return i; return -1; };
  // seeded history so the empty state shows what Recents and Favourites look like (noted as mock)
  st.recents = store.get('recents', [2, 7, 3, byName('Altair')]).filter(i => i >= 0 && i < H.N);
  st.favs = new Set(store.get('favs', [H.HOME, 0, byName('Vega')]).filter(i => i >= 0 && i < H.N));
  function addRecent(sys) { st.recents = [sys].concat(st.recents.filter(x => x !== sys)).slice(0, 8); store.set('recents', st.recents); }

  // ---------- elements ----------
  let qEl, topEl, panelEl, sheetEl, bodyEl, sysEl, toastEl, hintEl;
  let panelOpen = false, results = [], activeIdx = -1;
  let sheetMode = null, placeCtx = null, listCtx = null, routeCtx = null, autopilot = null, systemOpen = null, structCtx = null;

  // ---------- search panel ----------
  function highlight(name, q) {
    const n = name.toLowerCase(), k = q.trim().toLowerCase();
    const i = k ? n.indexOf(k) : -1;
    if (i < 0) return esc(name);
    return esc(name.slice(0, i)) + '<mark>' + esc(name.slice(i, i + k.length)) + '</mark>' + esc(name.slice(i + k.length));
  }
  function riHTML(it) {
    if (it.type === 'intent') return `<div class="ri intent${it.id === 'danger' ? ' danger' : ''}">${intentIcon(it.icon)}</div>`;
    if (it.type === 'system') return it.icon === 'home' ? `<div class="ri intent">${I.home}</div>` : it.icon === 'station' ? `<div class="ri">${I.station}</div>` : `<div class="ri"><span class="sd" style="--c:${H.COL[it.sys]}"></span></div>`;
    return `<div class="ri">${I[it.icon] || I.star}</div>`;
  }
  function jumpsLabel(it) {
    if (it.far) return 'beyond survey';
    if (it.jumps == null) return '';
    if (it.jumps === 0) return 'you are here';
    if (it.jumps < 0) return 'unreachable';
    return H.plural(it.jumps, 'jump');
  }
  function rowHTML(it, idx, q) {
    const tag = it.mock ? '<span class="tag">MOCK</span>' : '';
    return `<button class="row${idx === activeIdx ? ' active' : ''}" data-i="${idx}">${riHTML(it)}
      <div class="rt"><div class="rn">${highlight(it.name, q)}${tag}</div><div class="rs">${esc(it.sub || '')}</div></div>
      <div class="rd">${it.dist != null ? `<span class="dist">${H.ly(it.dist)}</span>` : ''}<span class="jumps">${jumpsLabel(it)}</span></div></button>`;
  }
  const CHIPS = [['fuel', 'Nearest fuel'], ['station', 'Nearest station'], ['home', 'Home'], ['earth', 'Earth-like worlds'], ['water', 'Water worlds'], ['ringed', 'Ringed within 50 ly'], ['landable', 'Landable nearby'], ['danger', 'Danger zones']];
  function renderPanel() {
    const q = qEl.value;
    topEl.classList.toggle('has-q', !!q);
    let html = '<div class="pnl-inner">';
    results = [];
    if (!q.trim()) {
      html += `<div class="sec-h">Ask the map</div><div class="chips">` +
        CHIPS.map(([id, t]) => `<button class="chip" data-intent="${id}">${intentIcon(H.intentById[id].icon)}${t}</button>`).join('') + '</div>';
      const favs = [...st.favs].slice(0, 4);
      if (favs.length) {
        html += `<div class="sec-h">Favourites</div><div class="favs">` + favs.map(s => {
          const isHome = s === H.HOME;
          return `<button class="fav" data-sys="${s}"><div class="fi">${isHome ? I.home : H.stationsBySys.has(s) ? I.station : I.favOn}</div><b>${esc(isHome ? 'Home base' : H.NAME[s])}</b><span>${isHome ? esc(H.NAME[s]) : H.ly(H.dShip(s))}</span></button>`;
        }).join('') + '</div>';
      }
      if (st.recents.length) {
        html += `<div class="sec-h">Recents</div>`;
        st.recents.slice(0, 4).forEach(s => {
          const it = H.describePlace({ type: 'system', name: H.NAME[s], sys: s });
          it.icon = 'clock'; results.push(it);
          html += rowHTML(Object.assign({}, it, { type: 'recent' }), results.length - 1, '');
        });
      }
    } else {
      const r = H.search(q);
      const groups = r.placesFirst ? [['Places', r.places], ['Ask the map', r.intents]] : [['Ask the map', r.intents], ['Places', r.places]];
      let any = false;
      for (const [title, items] of groups) {
        if (!items.length) continue;
        any = true;
        html += `<div class="sec-h">${title}</div>`;
        for (const it of items) { results.push(it); html += rowHTML(it, results.length - 1, q); }
      }
      if (!any) html += `<div class="empty-note">Nothing called “${esc(q)}” within the surveyed 150 ly. Try a star name, a sector, “Perseus Arm”, or ask for “nearest station”.</div>`;
      if (activeIdx < 0 && results.length) activeIdx = 0;
      html = html.replace(/class="row" data-i="0"/, 'class="row active" data-i="0"');
    }
    html += `<div class="panel-foot"><span><kbd>↑</kbd><kbd>↓</kbd>choose</span><span><kbd>↵</kbd>go</span><span><kbd>esc</kbd>back</span></div></div>`;
    panelEl.innerHTML = html;
  }
  function openPanel() { if (panelOpen) return; panelOpen = true; topEl.classList.add('open'); activeIdx = -1; renderPanel(); V.refreshUi(); }
  function closePanel() { if (!panelOpen) return; panelOpen = false; topEl.classList.remove('open'); V.refreshUi(); }
  function focusSearch() { qEl.focus(); qEl.select(); openPanel(); }
  function setActive(i) {
    if (!results.length) return;
    activeIdx = (i + results.length) % results.length;
    panelEl.querySelectorAll('.row').forEach(r => r.classList.toggle('active', +r.dataset.i === activeIdx));
    const el = panelEl.querySelector(`.row[data-i="${activeIdx}"]`); if (el) el.scrollIntoView({ block: 'nearest' });
  }
  function choose(it) {
    if (!it) return;
    closePanel(); qEl.blur();
    if (it.type === 'intent') { qEl.value = it.name; runIntent(it.id, it.radius); }
    else if (it.type === 'structure') { qEl.value = it.name; selectStructure(it); }
    else if (it.type === 'sector') { qEl.value = it.name; selectSector(it); }
    else if (it.type === 'station') { qEl.value = it.name; selectSystem(it.sys, { ctx: { icon: 'station', text: it.station.name + ' · ' + H.cap(it.station.type) + ' station' } }); }
    else if (it.type === 'base') { qEl.value = it.name; selectSystem(it.sys, { ctx: { icon: 'flag', text: it.player.name } }); }
    else if (it.sys != null) { qEl.value = H.NAME[it.sys]; selectSystem(it.sys); }
    topEl.classList.toggle('has-q', !!qEl.value);
  }

  // ---------- intents ----------
  function runIntent(id, radius) {
    const t = H.intentById[id];
    const rad = radius || t.radius || 150;
    const res = t.run(rad);
    if (res.single != null) {
      selectSystem(res.single, { ctx: { icon: t.icon, text: t.title + ' · ' + res.why } });
      return;
    }
    showList({ id, title: res.title || t.title, items: res.list, unit: res.unit, radius: rad, icon: t.icon, mock: t.mock, danger: res.danger });
  }
  function showList(ctx, opts) {
    listCtx = ctx; placeCtx = null; st.sel = null; routeOff();
    V.setHighlight(ctx.items); V.setDim(1); V.setFocus(null);
    const pts = [H.shipPos()].concat(ctx.items.slice(0, ctx.danger ? 2 : 12).map(H.pos));
    if (ctx.danger) H.M.danger.forEach(d => pts.push([d.centre[0] + d.radiusLy, d.centre[1], d.centre[2]], [d.centre[0] - d.radiusLy, d.centre[1], d.centre[2]]));
    V.setDangerBoost(ctx.danger ? 1 : 0);
    showSheet('list');
    if (!opts || !opts.keepCamera) frame(pts, 0.85);
  }

  // ---------- selection ----------
  function selectSystem(sys, opts) {
    opts = opts || {};
    if (sys == null || sys < 0) return;
    if (autopilot) return;
    st.sel = { type: 'system', sys }; structCtx = null;
    placeCtx = { sys, ctx: opts.ctx || null, fromList: !!(listCtx && opts.fromList) };
    if (!placeCtx.fromList) { listCtx = null; V.setHighlight(null); V.setDim(0); V.setDangerBoost(0); }
    if (!opts.keepRoute) routeOff();
    addRecent(sys);
    V.setFocus(sys, 16);
    showSheet('place');
    if (!opts.noFly) {
      const c = V.camera();
      V.flyTo({ target: H.pos(sys), dist: opts.dist || 26, pitch: c.galaxy > 0.3 ? 30 * DEG : Math.min(Math.max(c.pitch, 22 * DEG), 50 * DEG), instant: opts.instant });
    }
  }
  function selectStructure(s) {
    st.sel = { type: 'structure', ref: s }; structCtx = s; placeCtx = null; listCtx = null; routeOff();
    V.setHighlight(null); V.setDim(0); V.setFocus(null); V.setDangerBoost(0);
    const dist = s.kind === 'centre' ? 26000 : s.kind === 'bar' ? 42000 : s.kind === 'spur' ? 22000 : 52000;
    V.flyTo({ target: s.pos, dist, pitch: 52 * DEG, galaxy: true });
    showSheet('structure');
  }
  function selectSector(s) {
    st.sel = { type: 'sector', ref: s }; structCtx = s; placeCtx = null; listCtx = null; routeOff();
    V.setHighlight(null); V.setDim(0); V.setFocus(null);
    V.flyTo({ target: s.pos, dist: 3800, pitch: 40 * DEG, galaxy: true });
    showSheet('sector');
  }
  function frame(points, pad) {
    let mn = [1e9, 1e9, 1e9], mx = [-1e9, -1e9, -1e9];
    for (const p of points) for (let k = 0; k < 3; k++) { mn[k] = Math.min(mn[k], p[k]); mx[k] = Math.max(mx[k], p[k]); }
    const c = [(mn[0] + mx[0]) / 2, (mn[1] + mx[1]) / 2, (mn[2] + mx[2]) / 2];
    let r = 4; for (const p of points) r = Math.max(r, Math.hypot(p[0] - c[0], p[1] - c[1], p[2] - c[2]));
    const fh = Math.max(240, window.innerHeight - 2 * Math.abs(st.insetTop - st.insetBottom) - 90), fw = Math.max(240, window.innerWidth - st.insetLeft - 60);
    const dist = Math.min(420, Math.max(14, r / (Math.tan(24 * DEG) * Math.min(fh, fw) / window.innerHeight) * (pad || 1)));
    const cc = V.camera();
    V.flyTo({ target: c, dist, pitch: cc.galaxy > 0.3 ? 34 * DEG : Math.min(Math.max(cc.pitch, 26 * DEG), 48 * DEG) });
  }

  // ---------- route ----------
  function routeOff() {
    if (!routeCtx) return;
    routeCtx = null; V.setRoute(null);
    if (!listCtx) { V.setHighlight(null); V.setDim(0); }
    V.setDangerBoost(0);
  }
  function planRoute(dest, opts) {
    opts = opts || {};
    if (dest === H.ship.sys) { toast(I.check, 'You are already here'); return; }
    const t0 = performance.now();
    const fast = H.planRoute(H.ship.sys, dest, { fuel: H.ship.fuel });
    if (!fast || !fast.ok) { toast(I.warn, 'No route within jump range'); return; }
    let safe = null;
    if (fast.dangers.length) {
      const s = H.planRoute(H.ship.sys, dest, { fuel: H.ship.fuel, avoid: true });
      if (s && s.ok && s.dangers.length < fast.dangers.length) safe = s;
    }
    routeCtx = { dest, fast, safe, active: opts.safe && safe ? safe : fast, ms: performance.now() - t0, stepsOpen: !!opts.steps };
    st.target = dest; updateTargetPill();
    applyRoute(true);
  }
  function applyRoute(reframe) {
    const r = routeCtx.active;
    V.setRoute(r);
    V.setHighlight(r.stops.map(s => s.sys)); V.setDim(0.75); V.setFocus(null);
    V.setDangerBoost(r.dangers.length ? 1 : (routeCtx.safe ? 0.5 : 0));
    st.sel = { type: 'system', sys: routeCtx.dest };
    showSheet('route');
    if (reframe) frame(r.stops.map(s => H.pos(s.sys)), 1.08);
  }

  // ---------- autopilot (animates the ship along the route) ----------
  const HOP_S = 1.05, STOP_S = 0.32, REFUEL_S = 0.9;
  function startAutopilot() {
    if (!routeCtx || autopilot) return;
    const r = routeCtx.active;
    autopilot = { r, k: 0, phase: 'hop', t: 0, fuel: H.ship.fuel };
    V.cometOff = true;
    V.setShipPos(H.pos(r.stops[0].sys));
    sheetEl.classList.add('autopilot');
    updateAutopilotUI();
    renderRouteButtons();
    toast(I.play, 'Autopilot engaged · ' + H.plural(r.jumps, 'jump') + ' to ' + H.NAME[r.to]);
  }
  function stopAutopilot(msg) {
    if (!autopilot) return;
    const r = autopilot.r, k = autopilot.k;
    const at = r.stops[k].sys;
    autopilot = null; V.cometOff = false; V.setShipPos(null); sheetEl.classList.remove('autopilot');
    H.setShip(at, H.ship.fuel);
    updateShip();
    if (msg !== false) toast(I.stop, 'Autopilot stopped at ' + H.NAME[at]);
    if (at === r.to) return;
    planRoute(r.to, { steps: routeCtx && routeCtx.stepsOpen });
  }
  function tickAutopilot(dt) {
    const a = autopilot; if (!a) return;
    const r = a.r, n = r.stops.length;
    a.t += dt;
    if (a.phase === 'hop') {
      const u = Math.min(1, a.t / HOP_S), e = u < 0.5 ? 2 * u * u : 1 - Math.pow(-2 * u + 2, 2) / 2;
      const p = V.routePoint((a.k + e) / (n - 1));
      V.setShipPos(p); V.follow(p);
      if (u >= 1) {
        a.k++; a.t = 0;
        const s = r.stops[a.k];
        const cost = (H.stormy[s.sys] || H.stormy[r.stops[a.k - 1].sys]) ? 2 : 1;
        H.ship.fuel = Math.max(0, H.ship.fuel - cost); H.ship.sys = s.sys;
        a.phase = s.refuel ? 'refuel' : 'stop';
        V.setRouteProgress(a.k);
        updateShip(); updateAutopilotUI();
      }
    } else {
      const dur = a.phase === 'refuel' ? REFUEL_S : STOP_S;
      if (a.phase === 'refuel' && a.t > dur * 0.6 && H.ship.fuel < H.FUEL_MAX) { H.ship.fuel = H.FUEL_MAX; updateShip(); }
      if (a.t >= dur) {
        if (a.k >= n - 1) { arrive(); return; }
        a.phase = 'hop'; a.t = 0; updateAutopilotUI();
      }
    }
  }
  function arrive() {
    const r = autopilot.r;
    autopilot = null; V.cometOff = false; V.setShipPos(null);
    H.setShip(r.to, H.ship.fuel);
    routeCtx = null; V.setRoute(null);
    if (st.target === r.to) { st.target = null; updateTargetPill(); }
    updateShip();
    sheetEl.classList.remove('autopilot');
    toast(I.check, 'Arrived at ' + H.NAME[r.to]);
    selectSystem(r.to, { noFly: false, dist: 24 });
  }
  function updateAutopilotUI() {
    const a = autopilot; if (!a || sheetMode !== 'route') return;
    const r = a.r, n = r.stops.length;
    const prog = $('.prog', bodyEl); if (!prog) return;
    prog.classList.add('on');
    const k = a.k, next = Math.min(n - 1, k + (a.phase === 'hop' ? 1 : 0));
    const label = a.phase === 'refuel' ? `Refuelling at <b>${esc(H.NAME[r.stops[k].sys])}</b>` : a.phase === 'hop' ? `Jump ${k + 1} of ${n - 1} · <b>${esc(H.NAME[r.stops[next].sys])}</b>` : `Arrived at <b>${esc(H.NAME[r.stops[k].sys])}</b>`;
    $('.pt', prog).innerHTML = `<span>${label}</span><span>${Math.round(k / (n - 1) * 100)}%</span>`;
    $('.pbar i', prog).style.width = (k / (n - 1) * 100) + '%';
    bodyEl.querySelectorAll('.steps li').forEach((li, i) => { li.classList.toggle('done', i < k); li.classList.toggle('now', i === next); });
    const now = bodyEl.querySelector('.steps li.now'); if (now && $('.steps.open', bodyEl)) now.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
  }

  // ---------- sheet ----------
  function showSheet(mode) {
    sheetMode = mode;
    let html = '';
    if (mode === 'place') html = placeHTML(placeCtx.sys, placeCtx.ctx);
    else if (mode === 'list') html = listHTML();
    else if (mode === 'route') html = routeHTML();
    else if (mode === 'structure') html = structureHTML(structCtx);
    else if (mode === 'sector') html = sectorHTML(structCtx);
    bodyEl.innerHTML = html;
    bodyEl.classList.remove('swap'); void bodyEl.offsetWidth; bodyEl.classList.add('swap');
    bodyEl.scrollTop = 0;
    sheetEl.classList.add('show');
    syncInsets();
    if (mode === 'route') renderRouteButtons();
    requestAnimationFrame(syncInsets);
    setTimeout(syncInsets, 450);
  }
  function hideSheet() {
    sheetMode = null; sheetEl.classList.remove('show');
    syncInsets();
    setTimeout(() => V.refreshUi(), 500);
  }
  const docked = () => window.innerWidth > 900;
  function insetsFor(mode) {
    const top = 18 + 52 + 14;
    if (!mode) return [top, 0, 0];
    if (docked()) return [top * 0.5, 0, 420 + 18];
    return [top, sheetEl.offsetHeight + 14, 0];
  }
  function syncInsets() {
    const [t, b, l] = insetsFor(sheetMode);
    V.setInsets(t, b, l);
    V.refreshUi();
  }
  function closeAll() {
    if (autopilot) return;
    st.sel = null; placeCtx = null; listCtx = null; structCtx = null; routeOff();
    V.setHighlight(null); V.setDim(0); V.setFocus(null); V.setDangerBoost(0);
    qEl.value = ''; topEl.classList.remove('has-q');
    hideSheet();
  }

  // planet balls
  const PT = {
    'Metal-rich world': ['#d2c3b6', '#6c5a4f'], 'Rocky world': ['#d7b596', '#6e533f'], 'Earth-like world': ['#8cc8f0', '#2a6b4a'],
    'Water world': ['#7db4f2', '#1c3f84'], 'Icy world': ['#f2f7fb', '#8ea6ba'], 'Rocky ice world': ['#d4d9df', '#68717c'],
    'Gas giant': ['#ecd3a8', '#9b734c'], 'Ice giant': ['#b0e6ee', '#3a7f94'], 'Ammonia world': ['#e2cf93', '#7b6837']
  };
  function ballStyle(type, size, starCol) {
    if (starCol) return `width:${size}px;height:${size}px;background:radial-gradient(circle at 50% 50%, #fff 0, ${starCol} 45%, ${starCol} 70%);box-shadow:0 0 ${size * 0.6}px ${size * 0.15}px ${starCol}80`;
    const c = PT[type] || ['#cfcfcf', '#555'];
    let bg = `radial-gradient(circle at 32% 30%, ${c[0]} 0%, ${c[1]} 62%, #07090d 100%)`;
    if (type === 'Gas giant' || type === 'Ice giant') bg = `radial-gradient(circle at 32% 30%, transparent 0%, transparent 55%, rgba(4,6,10,.75) 100%), repeating-linear-gradient(170deg, ${c[0]} 0 ${Math.max(2, size / 9)}px, ${c[1]} ${Math.max(2, size / 9)}px ${Math.max(3, size / 5)}px), ${c[1]}`;
    if (type === 'Earth-like world') bg = `radial-gradient(circle at 32% 30%, rgba(255,255,255,.35) 0, transparent 38%), radial-gradient(circle at 60% 62%, #3f8a55 0 18%, transparent 30%), radial-gradient(circle at 32% 30%, ${c[0]} 0%, #2b5fa8 60%, #07090d 100%)`;
    return `width:${size}px;height:${size}px;background:${bg}`;
  }
  function planetsRow(sys, maxN) {
    const bodies = H.bodies[String(sys)];
    const items = [];
    if (bodies) {
      bodies.forEach((b, i) => {
        if (b[1] === 1 || b[1] === 3 || (b[1] === 0 && b[3] >= 0)) items.push({ b, i });
      });
      items.sort((a, b) => a.b[4] - b.b[4]);
      // keep planets orbiting companions after their star
      const html = items.slice(0, maxN).map(({ b }) => {
        if (b[1] === 3) return `<div class="pl" title="${esc(b[0])} · ${esc(b[2])}"><div class="pball" style="width:6px;height:18px;border-radius:3px;background-image:radial-gradient(circle, rgba(220,210,195,.8) 1px, transparent 1.4px);background-size:4px 5px"></div><span class="lm"></span></div>`;
        if (b[1] === 0) { const col = H.COL[sys]; return `<div class="pl" title="${esc(b[0])} · ${esc(b[2])} companion star"><div class="pball" style="${ballStyle(null, 16, col)}"></div><span class="lm"></span></div>`; }
        const sz = Math.round(Math.min(30, Math.max(6, 4 + Math.sqrt(b[5] / 1000) * 3.1)));
        return `<div class="pl" title="${esc(b[0])} · ${esc(b[2])}${b[9] ? ' · landable' : ''}"><div class="pball${b[10] ? ' ringed' : ''}" style="${ballStyle(b[2], sz)}"></div><span class="lm${b[9] ? ' on' : ''}"></span></div>`;
      }).join('');
      return { html: html + (items.length > maxN ? `<span class="more">+${items.length - maxN}</span>` : ''), real: true };
    }
    // long-range summary only: draw what it tells us
    const s = H.SUM[sys];
    const n = s[1], gg = s[4], ig = s[5], el = s[6], ww = s[7], rocky = Math.max(0, n - gg - ig - el - ww);
    const list = [];
    for (let k = 0; k < rocky; k++) list.push(['Rocky world', 7 + ((sys * 7 + k * 13) % 6)]);
    for (let k = 0; k < el; k++) list.push(['Earth-like world', 13]);
    for (let k = 0; k < ww; k++) list.push(['Water world', 14]);
    for (let k = 0; k < gg; k++) list.push(['Gas giant', 24 + (k % 2) * 4]);
    for (let k = 0; k < ig; k++) list.push(['Ice giant', 18]);
    let land = s[3];
    const html = list.slice(0, maxN).map(([t, sz], k) => {
      const l = (t === 'Rocky world' || t === 'Earth-like world' || t === 'Water world') && land-- > 0;
      return `<div class="pl" title="${esc(t)}"><div class="pball${k >= list.length - s[8] && s[8] ? ' ringed' : ''}" style="${ballStyle(t, sz)}"></div><span class="lm${l ? ' on' : ''}"></span></div>`;
    }).join('');
    return { html: (html || '<span class="more">No planets</span>') + (list.length > maxN ? `<span class="more">+${list.length - maxN}</span>` : ''), real: false };
  }

  function placeHTML(sys, ctx) {
    const here = sys === H.ship.sys, d = H.dShip(sys), s = H.SUM[sys];
    const name = H.NAME[sys], cls = H.CLS[sys];
    const sub = [cls, H.starKind(cls), here ? 'You are here' : H.ly(d), here ? null : H.jumpsText(sys)].filter(Boolean).join(' · ');
    const bodies = H.bodies[String(sys)];
    const fav = st.favs.has(sys);
    const back = placeCtx && placeCtx.fromList ? `<button class="icon-btn" data-act="back" title="Back to results (Esc)">${I.back}</button>` : '';
    let h = `<div class="ph">${back}<div class="orb" style="--c:${H.COL[sys]}"></div><div class="ttl"><h2>${esc(name)}</h2><p>${esc(sub)}</p></div>
      <button class="icon-btn${fav ? ' on' : ''}" data-act="fav" title="Favourite (F)">${fav ? I.favOn : I.fav}</button><button class="icon-btn" data-act="close" title="Close (Esc)">${I.close}</button></div>`;
    const badges = [];
    if (ctx) badges.push(`<span class="ctx">${intentIcon(ctx.icon)}${esc(ctx.text)}</span>`);
    if (sys === H.HOME) badges.push('<span class="badge acc">Home base</span>');
    if (H.canRefuel[sys]) badges.push(`<span class="badge">${H.stationsBySys.has(sys) && !H.SCOOP[sys] ? 'Station fuel' : 'Scoopable fuel'}</span>`);
    else badges.push('<span class="badge">No fuel</span>');
    if (H.dangerOf[sys] >= 0) badges.push(`<span class="badge dan">${esc(H.M.danger[H.dangerOf[sys]].name)}</span>`);
    if (st.target === sys) badges.push('<span class="badge acc">Target</span>');
    h += `<div class="badges">${badges.join('')}</div>`;
    h += `<div class="actions">
      <button class="btn primary" data-act="route" ${here ? 'disabled' : ''}>${I.route}Route</button>
      <button class="btn" data-act="target" ${here ? 'disabled' : ''}>${I.target}${st.target === sys ? 'Targeted' : 'Target'}</button>
      <button class="btn" data-act="open" ${bodies ? '' : 'disabled title="Full body data is surveyed within 40 ly of Sol in this prototype"'}>${I.open}Open system</button></div>`;
    const pr = planetsRow(sys, 16);
    h += `<div class="sec"><h4>Worlds<span>${H.plural(s[1], 'planet')} · ${H.plural(s[2], 'moon')} · ${s[3]} landable${s[0] > 1 ? ' · ' + H.plural(s[0], 'star') : ''}</span></h4>
      <div class="planets">${pr.html}</div>
      <div class="legend"><span><i></i>landable</span><span>${pr.real ? 'Full survey · open the system for details' : 'Long-range summary · full survey within 40 ly'}</span></div></div>`;
    if (H.dangerOf[sys] >= 0) {
      const dz = H.M.danger[H.dangerOf[sys]];
      h += `<div class="warn${dz.kind === 'hazard' ? ' haz' : ''}">${I.warn}<div><b>${esc(dz.name)}</b><span>${esc(dz.effect)}</span></div></div>`;
    }
    const sts = H.stationsBySys.get(sys) || [], pls = H.playersBySys.get(sys) || [];
    if (sts.length || pls.length || sys === H.HOME) {
      h += `<div class="sec"><h4>Stations &amp; bases<span>mock</span></h4>`;
      if (sys === H.HOME) h += `<div class="st-row"><div class="si" style="color:var(--accent)">${I.home}</div><div class="sn"><b>Your home base</b><span>On ${esc(H.M.home.body)} · ${esc(H.M.home.bodyType)}</span></div></div>`;
      for (const x of sts) {
        const where = x.type === 'depot' ? 'Deep-space depot' : x.type === 'outpost' ? 'Outpost on ' + x.body : 'Orbiting ' + x.body;
        h += `<div class="st-row"><div class="si">${I.station}</div><div class="sn"><b>${esc(x.name)}</b><span>${esc(where)}</span></div><div class="svc">${x.services.map(v => `<em>${v}</em>`).join('')}</div></div>`;
      }
      for (const p of pls) h += `<div class="st-row"><div class="si" style="color:${p.relation === 'hostile' ? 'var(--danger)' : p.relation === 'friendly' ? 'var(--accent)' : 'var(--text-2)'}">${I.flag}</div><div class="sn"><b>${esc(p.name)}</b><span>${H.cap(p.relation)} player · on ${esc(p.body)}</span></div></div>`;
      h += `</div>`;
    }
    const res = H.resNames(H.resMask[sys]);
    if (res.length) h += `<div class="sec"><h4>Resource hints<span>mock</span></h4><div class="res">${res.map(r => `<em>${esc(H.cap(r))}</em>`).join('')}</div></div>`;
    const solD = Math.hypot(H.X[sys], H.Y[sys], H.Z[sys]);
    const mag = H.MAG[sys] > 25 ? '—' : H.MAG[sys].toFixed(1);
    h += `<div class="sec"><h4>Star</h4><div class="facts">
      <div><b>${fmt.format(H.TEFF[sys])} K</b><span>Temperature</span></div>
      <div><b>${mag}</b><span>Abs. magnitude</span></div>
      <div><b>${H.ly(solD)}</b><span>From Sol</span></div>
      <div><b>${H.CAT[sys] ? 'Catalogue' : 'Generated'}</b><span>${H.CAT[sys] ? 'Real star (HYG)' : 'Procedural'}</span></div></div></div>`;
    return h;
  }

  function listHTML() {
    const c = listCtx;
    const n = c.items.length;
    let h = `<div class="ph"><div class="orb list${c.danger ? ' danger' : ''}">${intentIcon(c.icon)}</div><div class="ttl"><p class="eyebrow">${c.mock ? 'Mock resource hints' : 'Ask the map'}</p><h2>${esc(c.title)}</h2>
      <p>${n ? H.plural(n, 'system') + (c.danger ? ' · avoid or plan around them' : ' · nearest first') : 'Nothing found in range'}</p></div>
      <button class="icon-btn" data-act="close" title="Close (Esc)">${I.close}</button></div><div class="list">`;
    const rows = c.items.slice(0, 80);
    rows.forEach((sys, k) => {
      const it = { type: 'system', sys, name: H.NAME[sys], sub: (c.unit ? c.unit(sys) + ' · ' : '') + H.starKind(H.CLS[sys]), dist: H.dShip(sys), jumps: H.jumpsTo(sys), icon: 'star' };
      h += rowHTML(it, 1000 + k, '').replace('class="row', 'class="row lrow').replace(`data-i="${1000 + k}"`, `data-sys="${sys}"`);
    });
    if (n > rows.length) h += `<div class="note" style="padding:6px 12px">${n - rows.length} more · refine with “within 30 ly”</div>`;
    return h + '</div>';
  }

  function routeHTML() {
    const R = routeCtx, r = R.active, dest = R.dest;
    let h = `<div class="ph"><button class="icon-btn" data-act="back" title="Back (Esc)">${I.back}</button><div class="ttl"><p class="eyebrow">Route</p><h2>${esc(H.NAME[dest])}</h2>
      <p>From ${esc(H.NAME[r.from])}${r.from === H.HOME ? ' (home)' : ''} · ${H.starKind(H.CLS[dest])}</p></div>
      <button class="icon-btn" data-act="close" title="Close (Esc)">${I.close}</button></div>`;
    h += `<div class="rsum"><span class="big">${H.plural(r.jumps, 'jump')}</span><span class="sep">·</span><span class="mid">${r.refuels ? H.plural(r.refuels, 'refuel stop') : 'No refuelling'}</span><span class="sep">·</span><span class="mid">${H.minutes(r.seconds)}</span></div>
      <p class="rsub">${H.ly(r.lengthLy)} travelled · ${H.ly(r.straightLy)} straight line · ${H.RANGE} ly jumps · tank ${H.FUEL_MAX} jumps · planned in ${Math.max(1, Math.round(R.ms))} ms</p>`;
    if (R.safe) {
      const f = R.fast, s = R.safe;
      h += `<div class="alts">
        <button class="alt${r === f ? ' on' : ''}" data-act="alt-fast"><b>Fastest · ${H.plural(f.jumps, 'jump')}</b><span class="tagw">Through ${esc(f.dangers.map(d => d.region.name).join(', '))}</span></button>
        <button class="alt${r === s ? ' on' : ''}" data-act="alt-safe"><b>Safer · ${H.plural(s.jumps, 'jump')}</b><span class="tags">Avoids danger · +${s.jumps - f.jumps} jump${s.jumps - f.jumps === 1 ? '' : 's'}</span></button></div>`;
    }
    for (const d of r.dangers) {
      const z = d.region;
      h += `<div class="warn${z.kind === 'hazard' ? ' haz' : ''}">${I.warn}<div><b>Passes the ${esc(z.name)}</b><span>${esc(z.effect)} ${H.plural(d.count, 'stop')} inside.</span></div></div>`;
    }
    h += `<div class="go"></div><div class="prog"><div class="pt"></div><div class="pbar"><i></i></div></div>`;
    h += `<button class="steps-t${R.stepsOpen ? ' open' : ''}" data-act="steps">${I.chev}${H.plural(r.jumps, 'step')}</button><ol class="steps${R.stepsOpen ? ' open' : ''}">`;
    const n = r.stops.length;
    let fuel = H.ship.fuel;
    r.stops.forEach((s, k) => {
      const nm = esc(H.NAME[s.sys]);
      let kind = '', ico = '<i></i>', cls = '';
      if (k === 0) { cls = 'start'; kind = 'Start · ' + (s.sys === H.HOME ? 'home base' : H.starKind(H.CLS[s.sys])); }
      else {
        fuel = s.fuelIn;
        kind = H.ly(s.hop) + ' · ' + H.CLS[s.sys];
        if (s.danger >= 0) { cls = 'dan'; ico = `<i>${I.warn}</i>`; kind = H.M.danger[s.danger].name + ' · ' + H.ly(s.hop); }
        if (s.refuel) { cls = 'ref'; ico = `<i>${I.fuel}</i>`; kind = (H.stationsBySys.has(s.sys) ? 'Refuel at ' + H.stationsBySys.get(s.sys)[0].name : 'Refuel · scoop the ' + H.starKind(H.CLS[s.sys]).toLowerCase()) + ' · ' + H.ly(s.hop); }
        if (k === n - 1) { cls = 'end'; ico = `<i>${I.target}</i>`; kind = 'Arrive · ' + H.ly(s.hop); }
      }
      const fo = s.refuel ? s.fuelOut : s.fuelIn;
      const mf = '<span class="minifuel">' + Array.from({ length: H.FUEL_MAX }, (_, q) => `<i class="${q < fo ? '' : 'off'}"></i>`).join('') + '</span>';
      h += `<li data-sys="${s.sys}"><span class="sk ${cls}">${ico}</span><div class="sn"><b>${nm}</b><span>${esc(kind)}</span></div><div class="sr">${mf}</div></li>`;
    });
    return h + '</ol>';
  }
  function renderRouteButtons() {
    const go = $('.go', bodyEl); if (!go) return;
    go.innerHTML = autopilot ? `<button class="btn wide" data-act="autostop">${I.stop}Stop autopilot</button>` : `<button class="btn primary wide" data-act="auto">${I.play}Start autopilot</button>`;
    if (autopilot) updateAutopilotUI();
  }

  const STRUCT_TEXT = {
    'galactic-centre': 'The heart of the Milky Way. A black hole of about four million suns sits inside a dense disk of old stars. Everything you can see on this map orbits it once every 230 million years.',
    'bar': 'A long bar of old, reddish stars runs through the core, about 20,000 light-years end to end. The spiral arms begin near its tips.',
    'scutum-centaurus-arm': 'One of the two great arms, crowded with young blue stars and star-forming clouds on the far side of the core.',
    'sagittarius-carina-arm': 'The arm just inside ours, 4,200 ly toward the core. Its bright young clusters are the nearest big nurseries.',
    'perseus-arm': 'The next great arm outward, 4,600 ly beyond the Sun. Rich, young and mostly unexplored.',
    'norma-arm': 'The inner end of the arm that wraps all the way round to the Outer Arm, hugging the bar.',
    'outer-arm': 'The thin outer edge of the disk. Stars are sparse here and the dark between them is wide.',
    'orion-spur': 'Home. A short spur between the Sagittarius and Perseus arms. The Sun and every one of the 37,409 systems around you lie inside it.'
  };
  function structureHTML(s) {
    const d = Math.hypot(s.pos[0], s.pos[1], s.pos[2]);
    const kindWord = { centre: 'Galactic Centre', bar: 'Galactic bar', arm: 'Spiral arm', spur: 'Spur · you are here' }[s.kind];
    return `<div class="ph"><div class="orb ${s.kind === 'centre' || s.kind === 'bar' ? 'core' : 'arm'}"></div><div class="ttl"><p class="eyebrow">Galaxy</p><h2>${esc(s.name)}</h2><p>${kindWord} · ${H.ly(d)} from Sol</p></div>
      <button class="icon-btn" data-act="close" title="Close (Esc)">${I.close}</button></div>
      <p class="desc">${STRUCT_TEXT[s.id] || ''}</p>
      <div class="sec"><div class="facts">
        <div><b>${H.ly(d)}</b><span>From Sol</span></div>
        <div><b>≈ ${fmt.format(Math.ceil(d / H.RANGE))}</b><span>Jumps at ${H.RANGE} ly</span></div>
        <div><b>${fmt.format(Math.round(d))} yr</b><span>Light travel time</span></div>
        <div><b>${s.kind === 'centre' ? '4.1 M' : 'Billions'}</b><span>${s.kind === 'centre' ? 'Solar masses' : 'Systems'}</span></div></div></div>
      <div class="actions"><button class="btn primary" data-act="neigh">${I.ship}Back to my neighbourhood</button><button class="btn" data-act="route" disabled title="This prototype plots routes inside the surveyed 150 ly">${I.route}Route</button></div>`;
  }
  function sectorHTML(s) {
    const d = Math.hypot(s.pos[0], s.pos[1], s.pos[2]);
    return `<div class="ph"><div class="orb sector">${I.sector}</div><div class="ttl"><p class="eyebrow">Sector</p><h2>${esc(s.name)}</h2><p>1,024 ly cube · grid ${s.cube.join(', ')} · ${s.name === 'Nolcas Peris' ? 'contains Sol' : H.ly(d) + ' from Sol'}</p></div>
      <button class="icon-btn" data-act="close" title="Close (Esc)">${I.close}</button></div>
      <p class="desc">Every system in Hollow belongs to a sector; procedural names start with the sector name${s.name === 'Nolcas Peris' ? ', which is why so many stars around you are called “Nolcas Peris …”' : ''}.</p>
      <div class="actions"><button class="btn primary" data-act="neigh">${I.ship}Back to my neighbourhood</button></div>`;
  }

  // ---------- system view ----------
  function openSystem(sys) {
    const bodies = H.bodies[String(sys)];
    if (!bodies) { toast(I.warn, 'Full body data is surveyed within 40 ly in this prototype'); return; }
    systemOpen = { sys, bodies, sel: -1 };
    const name = H.NAME[sys];
    const short = n => n.indexOf(name + ' ') === 0 ? n.slice(name.length + 1) : n;
    // order: top-level bodies by orbit; planets of companions follow their star
    const top = [];
    bodies.forEach((b, i) => { if (i > 0 && b[1] !== 2) top.push(i); });
    const keyOf = i => { const b = bodies[i]; if (b[3] <= 0) return b[4]; const host = bodies[b[3]]; return host[4] + b[4] * 1e-3; };
    top.sort((a, c) => keyOf(a) - keyOf(c));
    const moonsOf = i => bodies.map((b, k) => [b, k]).filter(([b]) => b[1] === 2 && b[3] === i);
    const here = sys === H.ship.sys;
    let h = `<div class="sys-top"><button class="sys-back" data-sact="close">${I.back}Map</button>
      <div class="sys-title"><h1>${esc(name)}</h1><p>${esc(H.CLS[sys])} · ${H.starKind(H.CLS[sys])} · ${here ? 'You are here' : H.ly(H.dShip(sys)) + ' · ' + H.jumpsText(sys)} · ${H.plural(H.SUM[sys][1], 'planet')}, ${H.plural(H.SUM[sys][2], 'moon')}, ${H.SUM[sys][3]} landable</p></div>
      <div class="sys-acts">${here ? '' : `<button class="btn primary" data-sact="route">${I.route}Route here</button>`}<button class="btn" data-sact="fav">${st.favs.has(sys) ? I.favOn : I.fav}${st.favs.has(sys) ? 'Favourite' : 'Add favourite'}</button></div></div>
      <div class="sys-stage"><div class="sys-axis"></div><div class="sys-scroll"><div class="sys-track" style="--n:${top.length}"><div class="sys-star" style="--c:${H.COL[sys]}"></div>`;
    for (const i of top) {
      const b = bodies[i];
      if (b[1] === 3) { h += `<div class="sbody belt" data-b="${i}"><div class="slot"></div><div class="meta"><b>${esc(short(b[0]))}</b><span>${b[4]} AU</span></div></div>`; continue; }
      if (b[1] === 0) { const sz = 44; h += `<div class="sbody star2" data-b="${i}" style="--c:${H.COL[sys]}"><div class="slot"><div class="pball" style="${ballStyle(null, sz, '#ffd7a8')}"></div></div><div class="meta"><b>${esc(short(b[0]))}</b><span>${esc(b[2])} · ${b[4]} AU</span></div></div>`; continue; }
      const sz = Math.round(Math.min(64, Math.max(10, 7 + Math.sqrt(b[5] / 1000) * 6.2)));
      const ms = moonsOf(i);
      const moonHTML = ms.slice(0, 12).map(([m]) => `<i class="${m[9] ? 'l' : ''}" style="--s:${Math.round(Math.min(12, Math.max(4, 3 + Math.sqrt(m[5] / 1000) * 2.4)))}px" title="${esc(m[0])}"></i>`).join('') + (ms.length > 12 ? `<em>+${ms.length - 12} moons</em>` : '');
      const typeShort = b[2].replace(' world', '');
      h += `<div class="sbody" data-b="${i}"><div class="slot"><div class="pball${b[10] ? ' ringed' : ''}" style="${ballStyle(b[2], sz)}"></div></div>
        <div class="meta"><b>${esc(short(b[0]))}</b><span>${esc(typeShort)}</span>${b[9] ? '<span class="lb">Landable</span>' : ''}</div>
        <div class="moons">${moonHTML}</div></div>`;
    }
    h += `</div></div></div><div class="sys-detail"></div>`;
    sysEl.innerHTML = h;
    sysEl.classList.add('show'); sysEl.setAttribute('aria-hidden', 'false');
    // default: home body, else Earth-like, else first landable planet
    let pick = -1;
    if (sys === H.HOME) pick = H.M.home.bodyIndex;
    if (pick < 0) pick = bodies.findIndex(b => b[2] === 'Earth-like world');
    if (pick < 0) pick = bodies.findIndex(b => b[1] === 1 && b[9]);
    if (pick < 0) pick = top[0];
    selectBody(pick);
    V.state.hidden = true;
  }
  function selectBody(i) {
    if (!systemOpen) return;
    const { sys, bodies } = systemOpen;
    const b = bodies[i]; if (!b) return;
    systemOpen.sel = i;
    sysEl.querySelectorAll('.sbody').forEach(el => el.classList.toggle('on', +el.dataset.b === i));
    const el = sysEl.querySelector(`.sbody[data-b="${i}"]`); if (el) el.scrollIntoView({ inline: 'nearest', block: 'nearest', behavior: 'smooth' });
    const det = $('.sys-detail', sysEl);
    if (b[1] === 3) { det.innerHTML = `<div class="dt"><h3>${esc(b[0])}</h3><p>Asteroid belt · ${b[4]} AU from ${esc(bodies[Math.max(0, b[3])][0])}</p></div>`; return; }
    if (b[1] === 0) { det.innerHTML = `<div class="dball"><div class="pball" style="${ballStyle(null, 56, '#ffd7a8')}"></div></div><div class="dt"><h3>${esc(b[0])}</h3><p>${esc(b[2])} companion star · ${b[4]} AU · ${fmt.format(b[7])} K</p></div>`; return; }
    const sz = 64;
    const mask = H.resByBody.get(sys + ':' + i);
    const res = mask ? H.resNames(mask) : [];
    const tC = Math.round(b[7] - 273.15);
    const moons = bodies.filter(m => m[1] === 2 && m[3] === i).length;
    const parent = b[1] === 2 ? 'Moon of ' + bodies[b[3]][0] : b[4] + ' AU from ' + bodies[Math.max(0, b[3])][0];
    const isHome = sys === H.HOME && i === H.M.home.bodyIndex;
    det.innerHTML = `<div class="dball"><div class="pball${b[10] ? ' ringed' : ''}" style="${ballStyle(b[2], sz)}"></div></div>
      <div class="dt"><h3>${esc(b[0])}${isHome ? ' <span class="badge acc" style="vertical-align:3px">Home base</span>' : ''}</h3><p>${esc(b[2])} · ${esc(parent)}${moons ? ' · ' + H.plural(moons, 'moon') : ''}${b[10] ? ' · rings' : ''}</p>
      <div class="facts"><div><b>${b[9] ? 'Yes' : 'No'}</b><span>Landable</span></div><div><b>${b[6] < 0.1 ? b[6].toFixed(3) : b[6].toFixed(2)} g</b><span>Gravity</span></div>
      <div><b>${tC > 0 ? '+' : ''}${fmt.format(tC)} °C</b><span>Surface</span></div><div><b>${esc(H.cap(b[8]))}</b><span>Atmosphere</span></div>
      <div><b>${fmt.format(b[5])} km</b><span>Radius</span></div></div>
      ${res.length ? `<div class="res">${res.map(r => `<em>${esc(H.cap(r))}</em>`).join('')}<em style="opacity:.55">mock hints</em></div>` : ''}</div>`;
  }
  function closeSystem() {
    if (!systemOpen) return;
    systemOpen = null; sysEl.classList.remove('show'); sysEl.setAttribute('aria-hidden', 'true'); V.state.hidden = false;
  }

  // ---------- small UI ----------
  let toastT = 0;
  function toast(icon, text) {
    toastEl.innerHTML = icon + '<span></span>'; toastEl.querySelector('span').textContent = text;
    toastEl.classList.add('show'); clearTimeout(toastT); toastT = setTimeout(() => toastEl.classList.remove('show'), 2200);
  }
  function updateShip() {
    const s = H.ship.sys;
    $('#shipWhere').textContent = autopilot ? 'jumping · ' + H.NAME[s] : 'at ' + H.NAME[s] + (s === H.HOME ? ' · home' : '');
    const f = $('#fuel');
    f.innerHTML = Array.from({ length: H.FUEL_MAX }, (_, k) => `<i class="${k < H.ship.fuel ? '' : 'off'}"></i>`).join('');
    f.classList.toggle('low', H.ship.fuel <= 2);
    $('#fuelTxt').textContent = 'fuel ' + H.ship.fuel + '/' + H.FUEL_MAX;
  }
  function updateTargetPill() {
    const el = $('#targetPill');
    if (st.target == null) { el.classList.add('hidden'); return; }
    el.classList.remove('hidden');
    el.innerHTML = `${I.target}<span>Target</span><b>${esc(H.NAME[st.target])}</b><span>${H.jumpsText(st.target)}</span>`;
  }
  function toggleFav(sys) {
    if (st.favs.has(sys)) { st.favs.delete(sys); toast(I.fav, 'Removed from favourites'); }
    else { st.favs.add(sys); toast(I.favOn, 'Added to favourites · ' + H.NAME[sys]); }
    store.set('favs', [...st.favs]);
    if (sheetMode === 'place') showSheet('place');
  }
  function setTarget(sys) {
    st.target = st.target === sys ? null : sys;
    updateTargetPill();
    toast(I.target, st.target == null ? 'Target cleared' : 'Target set · ' + H.NAME[sys] + ' · ' + H.jumpsText(sys));
    if (sheetMode === 'place') showSheet('place');
  }
  function goLocal() {
    const sel = st.sel && st.sel.type === 'system' ? st.sel.sys : H.ship.sys;
    V.flyTo({ target: H.pos(sel), dist: 58, pitch: 30 * DEG });
    if (st.sel && st.sel.type !== 'system') closeAll();
  }
  function goGalaxy() {
    V.flyTo({ target: V.GAL_FRAME, dist: 92000, pitch: 68 * DEG, yaw: 0, keepAnchor: true });
  }
  function back() {
    if (systemOpen) return closeSystem();
    if (panelOpen) { if (qEl.value) { qEl.value = ''; activeIdx = -1; renderPanel(); return; } closePanel(); qEl.blur(); return; }
    if (autopilot) return stopAutopilot();
    if (sheetMode === 'route') { const d = routeCtx.dest; routeOff(); placeCtx = { sys: d, ctx: null }; st.sel = { type: 'system', sys: d }; V.setFocus(d, 16); showSheet('place'); V.flyTo({ target: H.pos(d), dist: 26 }); return; }
    if (sheetMode === 'place' && placeCtx && placeCtx.fromList && listCtx) { st.sel = null; placeCtx = null; showList(listCtx); return; }
    if (sheetMode) { closeAll(); return; }
    if (V.isGalaxy()) return goLocal();
  }

  // ---------- events ----------
  function bind() {
    qEl.addEventListener('focus', openPanel);
    qEl.addEventListener('input', () => { activeIdx = -1; openPanel(); renderPanel(); });
    qEl.addEventListener('keydown', e => {
      if (e.key === 'ArrowDown') { e.preventDefault(); setActive(activeIdx + 1); }
      else if (e.key === 'ArrowUp') { e.preventDefault(); setActive(activeIdx - 1); }
      else if (e.key === 'Enter') { e.preventDefault(); e.stopPropagation(); const it = results[Math.max(0, activeIdx)]; if (it) choose(it); }
    });
    $('#qclear').addEventListener('click', () => { qEl.value = ''; activeIdx = -1; qEl.focus(); renderPanel(); });
    panelEl.addEventListener('mousedown', e => e.preventDefault()); // keep focus in the field
    panelEl.addEventListener('click', e => {
      const chip = e.target.closest('[data-intent]'); if (chip) { const t = H.intentById[chip.dataset.intent]; closePanel(); qEl.blur(); qEl.value = t.title; topEl.classList.add('has-q'); runIntent(t.id); return; }
      const fav = e.target.closest('.fav'); if (fav) { choose({ type: 'system', sys: +fav.dataset.sys }); return; }
      const row = e.target.closest('.row'); if (row) choose(results[+row.dataset.i]);
    });
    document.addEventListener('pointerdown', e => { if (panelOpen && !topEl.contains(e.target)) { closePanel(); qEl.blur(); } }, true);
    sheetEl.addEventListener('click', e => {
      const lrow = e.target.closest('.lrow'); if (lrow) { selectSystem(+lrow.dataset.sys, { fromList: true, ctx: { icon: listCtx.icon, text: listCtx.title } }); return; }
      const li = e.target.closest('.steps li'); if (li && !autopilot) { V.flyTo({ target: H.pos(+li.dataset.sys), dist: 22 }); return; }
      const b = e.target.closest('[data-act]'); if (!b || b.disabled) return;
      const act = b.dataset.act, sys = placeCtx ? placeCtx.sys : routeCtx ? routeCtx.dest : null;
      if (act === 'close') { if (autopilot) stopAutopilot(); closeAll(); }
      else if (act === 'back') back();
      else if (act === 'route') planRoute(sys);
      else if (act === 'target') setTarget(sys);
      else if (act === 'fav') toggleFav(sys);
      else if (act === 'open') openSystem(sys);
      else if (act === 'auto') startAutopilot();
      else if (act === 'autostop') stopAutopilot();
      else if (act === 'alt-fast' || act === 'alt-safe') { if (autopilot) return; routeCtx.active = act === 'alt-safe' ? routeCtx.safe : routeCtx.fast; applyRoute(false); }
      else if (act === 'steps') { routeCtx.stepsOpen = !routeCtx.stepsOpen; b.classList.toggle('open', routeCtx.stepsOpen); $('.steps', bodyEl).classList.toggle('open', routeCtx.stepsOpen); syncInsets(); }
      else if (act === 'neigh') { closeAll(); V.flyTo({ target: H.shipPos(), dist: 58, pitch: 30 * DEG }); }
    });
    sysEl.addEventListener('click', e => {
      const b = e.target.closest('.sbody'); if (b) { selectBody(+b.dataset.b); return; }
      const a = e.target.closest('[data-sact]'); if (!a) return;
      if (a.dataset.sact === 'close') closeSystem();
      else if (a.dataset.sact === 'route') { const s = systemOpen.sys; closeSystem(); planRoute(s); }
      else if (a.dataset.sact === 'fav') { const s = systemOpen.sys; toggleFav(s); openSystem(s); }
    });
    $('#targetPill').addEventListener('click', () => { if (st.target != null) selectSystem(st.target); });
    $('#vLocal').addEventListener('click', goLocal);
    $('#vGal').addEventListener('click', goGalaxy);
    $('#recentre').addEventListener('click', () => { V.flyTo({ target: H.shipPos(), dist: 58, pitch: 30 * DEG }); });
    $('#zin').addEventListener('click', () => V.zoomBy(0.6));
    $('#zout').addEventListener('click', () => V.zoomBy(1.6));
    $('#ship').addEventListener('click', () => selectSystem(H.ship.sys));
    window.addEventListener('resize', () => requestAnimationFrame(syncInsets));

    document.addEventListener('keydown', e => {
      const typing = document.activeElement === qEl || e.target === qEl;
      if ((e.key === '/' && !typing) || ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k')) { e.preventDefault(); if (systemOpen) closeSystem(); focusSearch(); return; }
      if (e.key === 'Escape') { e.preventDefault(); back(); return; }
      if (typing || e.ctrlKey || e.metaKey || e.altKey) return;
      if (systemOpen) {
        const order = [...sysEl.querySelectorAll('.sbody')].map(x => +x.dataset.b), k = order.indexOf(systemOpen.sel);
        if (e.key === 'ArrowRight') selectBody(order[Math.min(order.length - 1, k + 1)]);
        if (e.key === 'ArrowLeft') selectBody(order[Math.max(0, k - 1)]);
        return;
      }
      const sel = st.sel && st.sel.type === 'system' ? st.sel.sys : null;
      const key = e.key.length === 1 ? e.key.toLowerCase() : e.key;
      hideHint();
      switch (key) {
        case 'r': if (sel != null && !autopilot) planRoute(sel); break;
        case 't': if (sel != null) setTarget(sel); break;
        case 'f': if (sel != null) toggleFav(sel); break;
        case 'o': case 'Enter': if (sel != null) openSystem(sel); break;
        case 'g': V.isGalaxy() ? goLocal() : goGalaxy(); break;
        case 'h': V.flyTo({ target: H.shipPos(), dist: 58, pitch: 30 * DEG }); break;
        case ' ': if (routeCtx) { e.preventDefault(); autopilot ? stopAutopilot() : startAutopilot(); } break;
        case 'ArrowLeft': case 'a': V.panBy(70, 0); break;
        case 'ArrowRight': case 'd': V.panBy(-70, 0); break;
        case 'ArrowUp': case 'w': V.panBy(0, 70); break;
        case 'ArrowDown': case 's': V.panBy(0, -70); break;
        case 'q': V.orbitBy(-0.12, 0); break;
        case 'e': V.orbitBy(0.12, 0); break;
        case '+': case '=': V.zoomBy(0.7); break;
        case '-': case '_': V.zoomBy(1.45); break;
      }
    });
  }
  function hideHint() { hintEl.classList.add('gone'); }

  // ---------- per-frame UI ----------
  let lastGal = null;
  function onFrame(dt) {
    tickAutopilot(dt);
    const c = V.camera();
    const gal = c.galaxy > 0.5;
    if (gal !== lastGal) {
      lastGal = gal;
      $('#vLocal').classList.toggle('on', !gal); $('#vGal').classList.toggle('on', gal);
      const on = gal ? $('#vGal') : $('#vLocal'), th = $('.seg-thumb');
      th.style.left = on.offsetLeft + 'px'; th.style.width = on.offsetWidth + 'px';
      qEl.placeholder = gal ? 'Search the galaxy: “Perseus Arm”, “Sagittarius A*”, a star…' : 'Search stars and places, or ask “nearest fuel”';
    }
    // scale bar
    const pxPerLy = window.innerHeight / (2 * c.dist * Math.tan(24 * DEG));
    const nice = [1, 2, 5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000, 25000, 50000];
    let L = nice[0]; for (const n of nice) { if (n * pxPerLy <= 130) L = n; }
    $('#scale .bar').style.width = Math.round(L * pxPerLy) + 'px';
    $('#scale .txt').textContent = fmt.format(L) + ' ly';
  }

  // ---------- URL states (for screenshots and links) ----------
  function applyHash() {
    const h = decodeURIComponent(location.hash.slice(1));
    if (!h) return false;
    const P = new URLSearchParams(h.replace(/\+/g, '%2B'));
    const has = k => P.has(k);
    const sysOf = n => { const i = byName(n); return i; };
    if (has('galaxy')) { V.flyTo({ target: V.GAL_FRAME, dist: 92000, pitch: 68 * DEG, yaw: 0, instant: true, keepAnchor: true }); }
    if (has('struct')) { const s = H.structures.find(x => H.norm(x.name) === H.norm(P.get('struct')) || x.aliases.includes(H.norm(P.get('struct')))); if (s) selectStructure(s); }
    if (has('sel')) { const i = sysOf(P.get('sel')); if (i >= 0) selectSystem(i, { instant: has('instant') }); }
    if (has('intent')) runIntent(P.get('intent'), P.has('r') ? +P.get('r') : null);
    if (has('route')) {
      const i = sysOf(P.get('route'));
      if (i >= 0) { selectSystem(i, { instant: true }); planRoute(i, { safe: has('safe'), steps: has('steps') }); }
      if (has('auto')) {
        startAutopilot();
        const frac = +P.get('auto') || 0.5;
        // fast-forward the autopilot
        const total = (autopilot.r.stops.length - 1) * (HOP_S + STOP_S) + autopilot.r.refuels * REFUEL_S;
        let t = total * frac; while (t > 0 && autopilot) { tickAutopilot(0.05); t -= 0.05; }
      }
    }
    if (has('system')) { const i = sysOf(P.get('system')); if (i >= 0) { openSystem(i); if (has('body')) { const bi = H.bodies[String(i)].findIndex(b => H.norm(b[0]) === H.norm(P.get('body'))); if (bi >= 0) selectBody(bi); } } }
    if (has('q')) { qEl.value = P.get('q'); focusSearch(); qEl.setSelectionRange(qEl.value.length, qEl.value.length); renderPanel(); }
    if (has('open')) focusSearch();
    if (has('zoom')) V.zoomBy(+P.get('zoom'));
    return true;
  }

  // ---------- start ----------
  App.start = function (THREE, addons) {
    qEl = $('#q'); topEl = $('#top'); panelEl = $('#panel'); sheetEl = $('#sheet'); bodyEl = $('#sheetBody'); sysEl = $('#system'); toastEl = $('#toast'); hintEl = $('#hint');
    V.init(THREE, addons);
    V.onFrame = onFrame;
    V.onPick = sys => { if (panelOpen) { closePanel(); qEl.blur(); } if (sys >= 0) { qEl.value = H.NAME[sys]; topEl.classList.add('has-q'); selectSystem(sys, { fromList: !!listCtx && st.highlight && st.highlight.includes(sys), ctx: listCtx && st.highlight && st.highlight.includes(sys) ? { icon: listCtx.icon, text: listCtx.title } : null }); } };
    V.onLabel = c => {
      if (c.sys != null) { qEl.value = H.NAME[c.sys]; topEl.classList.add('has-q'); selectSystem(c.sys, { fromList: !!listCtx && !!st.highlight && st.highlight.includes(c.sys), ctx: listCtx && st.highlight && st.highlight.includes(c.sys) ? { icon: listCtx.icon, text: listCtx.title } : null }); }
      else if (c.ref && c.ref.type === 'structure') { qEl.value = c.ref.name; selectStructure(c.ref); }
      else if (c.ref && c.ref.type === 'sector') { qEl.value = c.ref.name; selectSector(c.ref); }
      else if (c.kind === 'you') goLocal();
    };
    V.onUserMove = hideHint;
    bind(); updateShip(); updateTargetPill();
    syncInsets();
    const fromHash = applyHash();
    if (!fromHash) {
      // arrive gently: from a wider, higher view down into the neighbourhood
      V.flyTo({ target: H.shipPos(), dist: 150, pitch: 56 * DEG, yaw: -0.5, instant: true });
      setTimeout(() => V.flyTo({ target: H.shipPos(), dist: 58, pitch: 30 * DEG, yaw: 0, dur: 2.6 }), 250);
    }
    requestAnimationFrame(() => {
      document.body.classList.remove('booting');
      $('#boot').classList.add('gone');
      syncInsets();
    });
    setTimeout(hideHint, 12000);
    window.addEventListener('hashchange', () => { closeAll(); closeSystem(); applyHash(); });
    App.ready = true;
  };
  App.debug = { st, planRoute, runIntent, selectSystem, openSystem, back, startAutopilot, get route() { return routeCtx; } };
})();
