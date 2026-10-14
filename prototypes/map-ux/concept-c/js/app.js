/* Reach map (concept C): state, input, camera motion, autopilot and the URL states used for screenshots. */
'use strict';
window.RM = window.RM || {};

RM.start = function (THREE) {
  RM.enter = el => { if (document.body.classList.contains('snap')) return; el.classList.remove('enter'); void el.offsetWidth; el.classList.add('enter'); setTimeout(() => el.classList.remove('enter'), 450); };
  const M = RM.model;
  const S = RM.createScene(THREE, document.getElementById('gl'), M);
  const view = S.view;
  const pos = s => [M.X[s], M.Y[s], M.Z[s]];

  let favs = [];
  try { favs = JSON.parse(localStorage.getItem('reachmap.favourites') || '[]'); } catch (e) { favs = []; }
  const bodyRes = new Map();
  for (const b of M.MOCK.resources.bodies) { if (!bodyRes.has(b[0])) bodyRes.set(b[0], []); bodyRes.get(b[0]).push(b); }

  const state = {
    ship: { sys: M.SHIP.sys, fuel: M.SHIP.fuel }, shipPos: pos(M.SHIP.sys),
    reach: null, labelOrder: null, ringAnchors: null, categoryCounts: {},
    selected: -1, target: -1, favourites: new Set(favs.filter(s => s >= 0 && s < M.N)),
    route: null, routeProgress: undefined, routeExpanded: false, autopilot: null,
    finder: null, finderSet: null, finderResult: null,
    hover: -1, hoverCell: null, dragging: false, panelsDirty: true, bodyRes, pinnedBody: -1,
  };
  const goal = { cx: state.shipPos[0], cz: state.shipPos[2], w: 150, tilt: 0, yaw: 0 };
  view.cx = goal.cx; view.cz = goal.cz; view.w = goal.w; view.refY = state.shipPos[1];
  let flight = null, anchor = null, follow = false, dirty = true;
  const vel = { x: 0, z: 0 };
  const keys = new Set();

  // ------------------------------------------------------------ reach
  function computeReach() {
    const sys = state.ship.sys;
    state.reach = M.computeReach(sys, state.ship.fuel);
    const field = M.rasterReach(state.reach);
    S.setReach(state.reach, field.data);
    state.labelOrder = M.labelOrder(state.reach);
    const x = M.X[sys], z = M.Z[sys];
    const ring = [0.5, 0.866];
    state.ringAnchors = [
      M.fieldCrossing(field.ch[0], x, z, ring[0], ring[1]),
      M.fieldCrossing(field.ch[1], x, z, ring[0], ring[1]),
      M.fieldCrossing(field.ch[2], x, z, ring[0], ring[1]),
      M.fieldCrossing(field.ch[3], x, z, -1, -0.12),
    ];
    for (const c of M.CATEGORIES) {
      let n = 0;
      for (let s = 0; s < M.N; s++) if (state.reach.cost[s] >= 0 && c.test(s)) n++;
      state.categoryCounts[c.id] = n;
    }
    S.setShipCell(M.cellIndex(x, z));
    if (state.finder) setFinder(state.finder, true);
    dirty = true;
  }

  // ------------------------------------------------------------ actions
  const act = {
    select(s, fly) {
      state.selected = s;
      ui.renderCard();
      if (s >= 0 && fly) {
        const w = view.w > 260 ? 110 : view.w;
        const [x, , z] = pos(s);
        const free = freeCentreOffset();
        flyTo(x - free[0] * w / S.width, z + free[1] * w / S.width, w);
      }
      dirty = true;
    },
    route(s) {
      if (s === state.ship.sys) return;
      stopAutopilot();
      const R = M.planRoute(state.ship.sys, s, state.ship.fuel);
      if (!R) { ui.toast('No route: that system is out of reach'); return; }
      state.route = R; state.routeProgress = undefined; state.routeExpanded = false; state.autopilot = null;
      S.setRoute(R);
      ui.renderRoute();
      fitRoute(R);
      ui.toast(`Route planned · ${R.jumps} jumps${R.refuels ? ' · ' + R.refuels + ' refuel' + (R.refuels > 1 ? 's' : '') : ''}`);
      dirty = true;
    },
    clearRoute() {
      stopAutopilot();
      state.route = null; state.autopilot = null; state.routeProgress = undefined;
      S.setRoute(null); ui.renderRoute(); ui.renderStatus(); dirty = true;
    },
    target(s) { state.target = s; ui.renderCard(); ui.renderStatus(); dirty = true; if (s >= 0) ui.toast('Target set · ' + M.NAME[s]); },
    favourite(s) {
      if (state.favourites.has(s)) state.favourites.delete(s); else state.favourites.add(s);
      try { localStorage.setItem('reachmap.favourites', JSON.stringify([...state.favourites])); } catch (e) { /* private mode */ }
      ui.renderCard();
      ui.toast(state.favourites.has(s) ? 'Saved to your places' : 'Removed from your places');
    },
    autopilot(on) { if (on) startAutopilot(); else { stopAutopilot(); ui.renderRoute(); ui.renderStatus(); } },
    finder(id, keepText) { setFinder(id, false, keepText); },
    preview(s) { state.hover = s; dirty = true; },
    goCell(cell) { const c = M.cellCentre(cell.i, cell.j); flyTo(c[0], c[1], 72); S.setHoverCell(cell); state.hoverCell = cell; setTimeout(() => { state.hoverCell = null; S.setHoverCell(null); dirty = true; }, 2500); },
    goSector(sec) { flyTo(sec.x, sec.z, 2600); },
    goStructure(st) { flyTo(st.label[0], st.label[2], st.kind === 'centre' || st.kind === 'bar' ? 40000 : 52000); },
    zoomToLevel(it) {
      if (it.level === 'galaxy') flyTo(M.GC[0], M.GC[2] - 4500, 178000);
      else if (it.level === 'arm') flyTo(view.cx, view.cz, 26000);
      else if (it.level === 'sector') flyTo(it.sector.x, it.sector.z, 2600);
      else if (it.level === 'cell') { const c = M.cellCentre(it.cell.i, it.cell.j); flyTo(c[0], c[1], 72); }
    },
  };
  const ui = RM.createUI({ M, S, state, act });

  function setFinder(id, quiet, keepText) {
    if (!id) {
      state.finder = null; state.finderSet = null; state.finderResult = null; state.finderTop = null;
      S.setHighlight(null);
    } else {
      const r = M.nearest(id, state.reach, 6);
      state.finder = id; state.finderResult = r;
      state.finderSet = new Set(r.matches.map(m => m.s));
      state.finderTop = r.list.map(m => m.s);
      S.setHighlight(state.finderSet);
      if (!quiet) {
        ui.searchInput.value = '';
        ui.openSearch();
      }
    }
    if (!keepText && !quiet && !id) ui.searchInput.value = ui.searchInput.value;
    ui.renderResults(); ui.syncToken();
    if (!id && document.activeElement !== ui.searchInput) ui.closeSearch();
    dirty = true;
  }

  // ------------------------------------------------------------ camera motion
  function freeCentreOffset() {
    // keep the selected thing clear of the left card and the right route panel
    const left = !document.getElementById('card').classList.contains('hidden') || state.selected >= 0 ? 400 : 0;
    const right = state.route ? 380 : 0;
    return [(left - right) / 2, 0];
  }
  function flyTo(cx, cz, w, dur) {
    anchor = null; vel.x = vel.z = 0; follow = false;
    const d = Math.hypot(cx - view.cx, cz - view.cz);
    const zoomSpan = Math.abs(Math.log(w / view.w));
    flight = { c0: [view.cx, view.cz], w0: view.w, c1: [cx, cz], w1: w, t: 0, dur: dur || Math.min(1.6, 0.55 + 0.12 * zoomSpan + 0.25 * Math.log1p(d / Math.max(w, view.w))) };
    goal.cx = cx; goal.cz = cz; goal.w = w;
  }
  function fitRoute(R) {
    let x0 = Infinity, x1 = -Infinity, z0 = Infinity, z1 = -Infinity;
    for (const st of R.stops) { const s = st.s; x0 = Math.min(x0, M.X[s]); x1 = Math.max(x1, M.X[s]); z0 = Math.min(z0, M.Z[s]); z1 = Math.max(z1, M.Z[s]); }
    const freeW = S.width - (state.selected >= 0 ? 400 : 0) - 380 - 80, freeH = S.height - 200;
    const w = Math.max(60, Math.max((x1 - x0) / freeW, (z1 - z0) / freeH) * S.width * 1.08);
    const off = freeCentreOffset();
    flyTo((x0 + x1) / 2 - off[0] * w / S.width, (z0 + z1) / 2, Math.min(w, 1200));
  }
  const easeInOut = t => (t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2);
  function update(dt) {
    const before = view.cx + ',' + view.cz + ',' + view.w + ',' + view.tilt + ',' + view.yaw + ',' + view.refY;
    // keyboard panning
    let kx = 0, kz = 0;
    if (keys.has('ArrowLeft') || keys.has('a')) kx -= 1;
    if (keys.has('ArrowRight') || keys.has('d')) kx += 1;
    if (keys.has('ArrowUp') || keys.has('w')) kz += 1;
    if (keys.has('ArrowDown') || keys.has('s')) kz -= 1;
    if (kx || kz) {
      flight = null; anchor = null; follow = false;
      const c = Math.cos(view.yaw), sn = Math.sin(view.yaw);
      const sp = view.w * 0.75 * dt;
      goal.cx += (kx * c - kz * sn) * sp; goal.cz += (kz * c + kx * sn) * sp;
    }
    if (flight) {
      flight.t = Math.min(1, flight.t + dt / flight.dur);
      const e = easeInOut(flight.t);
      const d = Math.hypot(flight.c1[0] - flight.c0[0], flight.c1[1] - flight.c0[1]);
      const wMax = Math.max(flight.w0, flight.w1);
      const bump = d > wMax * 0.9 ? Math.log(d / wMax * 1.1) : 0;
      view.w = Math.exp(Math.log(flight.w0) + (Math.log(flight.w1) - Math.log(flight.w0)) * e + bump * Math.sin(Math.PI * e));
      view.cx = flight.c0[0] + (flight.c1[0] - flight.c0[0]) * e;
      view.cz = flight.c0[1] + (flight.c1[1] - flight.c0[1]) * e;
      if (flight.t >= 1) flight = null;
    } else {
      const k = 1 - Math.exp(-dt * 13);
      view.w = Math.exp(Math.log(view.w) + (Math.log(goal.w) - Math.log(view.w)) * k);
      if (Math.abs(view.w / goal.w - 1) < 1e-4) view.w = goal.w;
      if (anchor && view.tilt < 0.01) {
        view.cx = anchor.gx - anchor.ox * view.w / S.width;
        view.cz = anchor.gz + anchor.oy * view.w / S.width;
        if (view.w === goal.w) anchor = null;
      } else {
        if (vel.x || vel.z) {
          goal.cx += vel.x * dt; goal.cz += vel.z * dt;
          const decay = Math.exp(-dt * 5);
          vel.x *= decay; vel.z *= decay;
          if (Math.hypot(vel.x, vel.z) < view.w * 0.002) vel.x = vel.z = 0;
        }
        view.cx += (goal.cx - view.cx) * k; view.cz += (goal.cz - view.cz) * k;
        if (Math.abs(goal.cx - view.cx) < view.w * 1e-5) view.cx = goal.cx;
        if (Math.abs(goal.cz - view.cz) < view.w * 1e-5) view.cz = goal.cz;
      }
    }
    if (view.tilt !== goal.tilt) {
      const step = dt / 0.85;
      view.tilt = goal.tilt > view.tilt ? Math.min(goal.tilt, view.tilt + step) : Math.max(goal.tilt, view.tilt - step);
    }
    view.yaw += (goal.yaw - view.yaw) * (1 - Math.exp(-dt * 10));
    if (Math.abs(goal.yaw - view.yaw) < 1e-4) view.yaw = goal.yaw;
    const ry = state.shipPos[1];
    view.refY += (ry - view.refY) * (1 - Math.exp(-dt * 8));
    if (Math.abs(ry - view.refY) < 1e-3) view.refY = ry;
    const after = view.cx + ',' + view.cz + ',' + view.w + ',' + view.tilt + ',' + view.yaw + ',' + view.refY;
    if (after !== before) dirty = true;
  }

  // ------------------------------------------------------------ autopilot
  function startAutopilot() {
    if (!state.route) return;
    if (state.route.from !== state.ship.sys) { act.route(state.route.to); if (!state.route) return; }
    state.autopilot = { running: true, leg: 0, t: 0, pause: 0.45, arrived: false };
    state.routeProgress = 0;
    follow = true; flight = null;
    goal.w = Math.min(view.w, 120);
    ui.renderRoute(); ui.renderStatus();
    ui.toast('Autopilot engaged · ' + state.route.jumps + ' jumps to ' + M.NAME[state.route.to]);
  }
  function stopAutopilot() {
    const ap = state.autopilot;
    if (!ap || !ap.running) return;
    ap.running = false;
    if (ap.t > 0) { // finish the jump in progress so the ship is never between stars
      arrive(ap);
    }
    follow = false;
    ui.toast('Autopilot off · you have the stick');
  }
  function arrive(ap) {
    const R = state.route;
    const to = R.stops[ap.leg + 1];
    ap.leg++; ap.t = 0;
    state.ship.sys = to.s; state.ship.fuel = to.fuel;
    state.shipPos = pos(to.s);
    state.routeProgress = to.total;
    computeReach();
    if (ap.leg >= R.jumps) {
      ap.running = false; ap.arrived = true; follow = false;
      ui.toast('Arrived · ' + (to.station ? to.station[0].name : M.NAME[to.s]));
    } else if (to.refuel) {
      ap.pause = 1.0; ui.toast('Scooping fuel at ' + M.NAME[to.s], true);
    } else ap.pause = 0.28;
    ui.renderRoute(); ui.renderStatus();
    if (state.selected >= 0) ui.renderCard();
  }
  function stepAutopilot(dt) {
    const ap = state.autopilot;
    if (!ap || !ap.running) return;
    if (ap.pause > 0) { ap.pause -= dt; return; }
    const R = state.route;
    const a = R.stops[ap.leg], b = R.stops[ap.leg + 1];
    const legDur = 0.6 + Math.min(0.5, b.leg / 25);
    if (ap.t === 0) { ui.renderRoute(); }
    ap.t = Math.min(1, ap.t + dt / legDur);
    const e = easeInOut(ap.t);
    const pa = pos(a.s), pb = pos(b.s);
    state.shipPos = [pa[0] + (pb[0] - pa[0]) * e, pa[1] + (pb[1] - pa[1]) * e, pa[2] + (pb[2] - pa[2]) * e];
    state.routeProgress = a.total + (b.total - a.total) * e;
    if (follow) { goal.cx = state.shipPos[0] - freeCentreOffset()[0] * view.w / S.width; goal.cz = state.shipPos[2]; }
    dirty = true;
    if (ap.t >= 1) arrive(ap);
  }
  function seekAutopilot(frac) {
    const R = state.route;
    if (!R) return;
    const target = frac * R.ly;
    let i = 0;
    while (i < R.jumps - 1 && R.stops[i + 1].total <= target) i++;
    const st = R.stops[i];
    state.ship.sys = st.s; state.ship.fuel = st.fuel;
    computeReach();
    const b = R.stops[i + 1];
    const t = Math.max(0, Math.min(0.999, (target - st.total) / Math.max(1e-6, b.total - st.total)));
    state.autopilot = { running: true, leg: i, t, pause: 0, arrived: false };
    const pa = pos(st.s), pb = pos(b.s), e = easeInOut(t);
    state.shipPos = [pa[0] + (pb[0] - pa[0]) * e, pa[1] + (pb[1] - pa[1]) * e, pa[2] + (pb[2] - pa[2]) * e];
    state.routeProgress = st.total + (b.total - st.total) * e;
    follow = true;
    ui.renderRoute(); ui.renderStatus();
  }

  // ------------------------------------------------------------ picking
  const P = [0, 0];
  function pick(sx, sy, radius) {
    const fades = S.layerFades(view.w);
    if (fades.stars < 0.3) return -1;
    let best = -1, bestD = radius * radius;
    for (let s = 0; s < M.N; s++) {
      if (!S.project(M.X[s], M.Y[s], M.Z[s], P)) continue;
      const dx = P[0] - sx, dy = P[1] - sy;
      let d2 = dx * dx + dy * dy;
      if (d2 > bestD * 2.5) continue;
      if (state.reach.cost[s] < 0) d2 *= 1.6;
      if (M.stationsBySys.has(s) || M.basesBySys.has(s)) d2 *= 0.5;
      if (d2 < bestD) { bestD = d2; best = s; }
    }
    return best;
  }

  // ------------------------------------------------------------ input
  const stage = document.getElementById('stage');
  let drag = null;
  stage.addEventListener('contextmenu', e => e.preventDefault());
  stage.addEventListener('pointerdown', e => {
    if (document.activeElement === ui.searchInput) ui.searchInput.blur();
    try { stage.setPointerCapture(e.pointerId); } catch (err) { /* synthetic pointer */ }
    const g = S.groundAt(e.clientX, e.clientY);
    drag = { x: e.clientX, y: e.clientY, lx: e.clientX, ly: e.clientY, t: performance.now(), moved: false, g, rotate: e.button === 2 || e.ctrlKey, label: e.target.closest('[data-s]') };
    flight = null; anchor = null; vel.x = vel.z = 0;
  });
  stage.addEventListener('pointermove', e => {
    if (drag) {
      const dx = e.clientX - drag.x, dy = e.clientY - drag.y;
      if (!drag.moved && Math.hypot(dx, dy) > 4) { drag.moved = true; state.dragging = true; stage.classList.add('grab'); follow = false; }
      if (drag.moved) {
        if (drag.rotate) {
          goal.yaw += (e.clientX - drag.lx) * 0.006;
          if (goal.tilt === 0 && Math.abs(goal.yaw) > 0.02) { goal.tilt = 1; syncTiltButton(); }
        } else if (drag.g) {
          const g = S.groundAt(e.clientX, e.clientY);
          if (g) {
            const ddx = drag.g[0] - g[0], ddz = drag.g[1] - g[1];
            view.cx += ddx; view.cz += ddz; goal.cx = view.cx; goal.cz = view.cz;
            const now = performance.now(), dtm = Math.max(1, now - drag.t) / 1000;
            vel.x = vel.x * 0.6 + (ddx / dtm) * 0.4; vel.z = vel.z * 0.6 + (ddz / dtm) * 0.4;
            drag.t = now;
            dirty = true;
          }
        }
        drag.lx = e.clientX; drag.ly = e.clientY;
      }
      return;
    }
    hoverAt(e.clientX, e.clientY, e.target);
  });
  function hoverAt(x, y, target) {
    const overLabel = target && target.closest && target.closest('[data-s]');
    const s = overLabel ? +overLabel.dataset.s : pick(x, y, 12);
    if (s !== state.hover) { state.hover = s; dirty = true; }
    stage.classList.toggle('point', s >= 0);
    const g = S.groundAt(x, y);
    const cell = g && S.layerFades(view.w).grid > 0.2 ? M.cellIndex(g[0], g[1]) : null;
    const key = cell ? cell.name : '';
    if (key !== (state.hoverCell ? state.hoverCell.name : '')) { state.hoverCell = cell; S.setHoverCell(cell); dirty = true; }
  }
  stage.addEventListener('pointerup', e => {
    if (!drag) return;
    const d = drag; drag = null;
    stage.classList.remove('grab');
    state.dragging = false;
    if (!d.moved) {
      vel.x = vel.z = 0;
      const s = d.label ? +d.label.dataset.s : pick(e.clientX, e.clientY, 14);
      act.select(s, false);
      if (s < 0 && state.finder) {/* keep the finder */}
    } else if (performance.now() - d.t > 80) { vel.x = vel.z = 0; }
    dirty = true;
  });
  stage.addEventListener('pointerleave', () => { if (!drag && state.hover >= 0) { state.hover = -1; dirty = true; } });
  stage.addEventListener('wheel', e => {
    e.preventDefault();
    flight = null; vel.x = vel.z = 0; follow = false;
    const trackpadPan = !e.ctrlKey && e.deltaMode === 0 && (Math.abs(e.deltaX) > 0.5 || (Math.abs(e.deltaY) < 40 && !Number.isInteger(e.deltaY)));
    if (trackpadPan) {
      const k = view.w / S.width;
      const c = Math.cos(view.yaw), sn = Math.sin(view.yaw);
      goal.cx += (e.deltaX * c + e.deltaY * sn * 0) * k; goal.cz -= e.deltaY * k;
      view.cx = goal.cx; view.cz = goal.cz; anchor = null; dirty = true;
      return;
    }
    const lines = e.deltaMode === 1 ? e.deltaY * 33 : e.deltaMode === 2 ? e.deltaY * 400 : e.deltaY;
    const factor = Math.exp(Math.max(-1.2, Math.min(1.2, lines * (e.ctrlKey ? 0.012 : 0.0022))));
    zoomAt(e.clientX, e.clientY, factor);
  }, { passive: false });
  function zoomAt(sx, sy, factor) {
    const w = Math.max(6, Math.min(170000, goal.w * factor));
    if (view.tilt < 0.01) {
      const g = S.groundAt(sx, sy);
      if (g) {
        // keep the point under the cursor fixed while the zoom eases in
        const ox = sx - S.width / 2, oy = sy - S.height / 2;
        anchor = { gx: g[0], gz: g[1], ox, oy };
        goal.cx = g[0] - ox * w / S.width; goal.cz = g[1] + oy * w / S.width;
      }
    }
    goal.w = w; dirty = true;
  }
  window.addEventListener('pointerup', () => { if (drag) { drag = null; stage.classList.remove('grab'); state.dragging = false; } });

  function syncTiltButton() { document.getElementById('b-tilt').classList.toggle('on', goal.tilt > 0.5); }
  function toggleTilt() {
    goal.tilt = goal.tilt > 0.5 ? 0 : 1;
    if (goal.tilt === 0) goal.yaw = 0;
    anchor = null;
    syncTiltButton(); dirty = true;
  }
  function toggleLegend(force) {
    const l = document.getElementById('legend');
    const open = force !== undefined ? force : l.classList.contains('hidden');
    l.classList.toggle('hidden', !open);
    if (open) RM.enter(l);
    l.classList.toggle('beside', !!state.route);
    document.getElementById('b-legend').classList.toggle('on', open);
    state.panelsDirty = true; dirty = true;
  }
  function recentre() {
    const off = freeCentreOffset();
    const w = view.w > 400 || view.w < 60 ? 150 : view.w;
    flyTo(state.shipPos[0] - off[0] * w / S.width, state.shipPos[2], w);
  }
  document.getElementById('b-in').onclick = () => zoomAt(S.width / 2, S.height / 2, 1 / 1.8);
  document.getElementById('b-out').onclick = () => zoomAt(S.width / 2, S.height / 2, 1.8);
  document.getElementById('b-tilt').onclick = toggleTilt;
  document.getElementById('b-home').onclick = recentre;
  document.getElementById('b-legend').onclick = () => toggleLegend();
  document.getElementById('status').onclick = () => { recentre(); act.select(state.ship.sys, false); };

  document.addEventListener('keydown', e => {
    if (document.activeElement === ui.searchInput) return;
    const k = e.key.length === 1 ? e.key.toLowerCase() : e.key;
    if (e.metaKey || e.ctrlKey || e.altKey) return;
    if (k === '/') { e.preventDefault(); ui.focusSearch(); return; }
    if (k === 'Escape') { backOut(); return; }
    if (['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'w', 'a', 's', 'd'].includes(k)) { keys.add(k); e.preventDefault(); return; }
    if (k === '+' || k === '=') zoomAt(S.width / 2, S.height / 2, 1 / 1.6);
    else if (k === '-' || k === '_') zoomAt(S.width / 2, S.height / 2, 1.6);
    else if (k === 't' || k === '3') toggleTilt();
    else if (k === 'l') toggleLegend();
    else if (k === ' ' || k === 'h') { e.preventDefault(); recentre(); }
    else if (k === 'r' || k === 'Enter') { if (state.selected >= 0 && state.selected !== state.ship.sys) act.route(state.selected); }
    else if (k === 'f') { if (state.selected >= 0) act.favourite(state.selected); }
    else if (k === 'g') { if (state.selected >= 0) act.target(state.target === state.selected ? -1 : state.selected); }
    else if (k === 'p') { if (state.route) act.autopilot(!(state.autopilot && state.autopilot.running)); }
    else if (k === 'q') goal.yaw -= 0.35;
    else if (k === 'e') goal.yaw += 0.35;
  });
  document.addEventListener('keyup', e => { keys.delete(e.key.length === 1 ? e.key.toLowerCase() : e.key); });
  window.addEventListener('blur', () => keys.clear());
  function backOut() {
    const legend = document.getElementById('legend');
    if (!legend.classList.contains('hidden')) return toggleLegend(false);
    if (state.finder) return setFinder(null);
    if (state.selected >= 0) return act.select(-1);
    if (state.route && !(state.autopilot && state.autopilot.running)) return act.clearRoute();
    if (goal.tilt > 0.5) return toggleTilt();
    if (view.w > 400 || Math.hypot(view.cx - state.shipPos[0], view.cz - state.shipPos[2]) > view.w * 0.3) return recentre();
  }
  window.addEventListener('resize', () => { S.resize(); state.panelsDirty = true; dirty = true; });

  // ------------------------------------------------------------ URL states (#select=Sirius&tilt=1 ...)
  function findSystem(name) {
    if (!name) return -1;
    if (/^\d+$/.test(name)) return +name;
    const n = name.toLowerCase();
    const st = M.MOCK.stations.find(x => x.name.toLowerCase() === n);
    if (st) return st.sys;
    const b = M.bases.find(x => x.handle.toLowerCase() === n);
    if (b) return b.sys;
    const i = M.NAME.findIndex(x => x.toLowerCase() === n);
    if (i >= 0) return i;
    const r = M.search(name, state.ship.sys, 1)[0];
    return r && r.s !== undefined ? r.s : -1;
  }
  function applyHash() {
    const h = new URLSearchParams(location.hash.slice(1));
    if (![...h.keys()].length) return false;
    const num = (k, d) => (h.has(k) ? parseFloat(h.get(k)) : d);
    if (h.has('route')) {
      const s = findSystem(h.get('route'));
      if (s >= 0) act.route(s);
      if (h.has('expand')) { state.routeExpanded = true; ui.renderRoute(); }
    }
    if (h.has('autopilot')) seekAutopilot(num('autopilot', 0.5));
    if (h.has('w')) { goal.w = view.w = num('w', 150); }
    if (h.has('select')) { const s = findSystem(h.get('select')); if (s >= 0) act.select(s, true); }
    if (h.has('target')) { const s = findSystem(h.get('target')); if (s >= 0) act.target(s); }
    if (h.has('finder')) setFinder(h.get('finder'));
    if (h.has('search')) { ui.focusSearch(); ui.searchInput.value = h.get('search'); ui.renderResults(); ui.syncToken(); }
    if (h.has('legend')) toggleLegend(true);
    if (h.has('zoom')) {
      const z = h.get('zoom');
      if (z === 'galaxy') act.zoomToLevel({ level: 'galaxy' });
      else if (z === 'arm') act.zoomToLevel({ level: 'arm' });
      else if (z === 'sector') act.zoomToLevel({ level: 'sector', sector: M.SOL_SECTOR });
    }
    if (h.has('at')) { const [x, z] = h.get('at').split(',').map(parseFloat); goal.cx = x; goal.cz = z; flight = null; }
    if (h.has('w')) { goal.w = num('w', 150); if (flight) { flight.w1 = goal.w; } }
    if (h.has('tilt')) { goal.tilt = num('tilt', 1); syncTiltButton(); }
    if (h.has('yaw')) goal.yaw = num('yaw', 0) * Math.PI / 180;
    if (h.has('hover')) { const s = findSystem(h.get('hover')); state.hover = s; }
    // snap: screenshots should not wait for animations
    if (flight) { goal.cx = flight.c1[0]; goal.cz = flight.c1[1]; goal.w = flight.w1; flight = null; }
    view.cx = goal.cx; view.cz = goal.cz; view.w = goal.w; view.tilt = goal.tilt; view.yaw = goal.yaw; anchor = null;
    view.refY = state.shipPos[1];
    dirty = true;
    return true;
  }
  window.addEventListener('hashchange', applyHash);

  // ------------------------------------------------------------ go
  document.body.classList.add('snap');
  if (!/shot/.test(location.hash)) setTimeout(() => document.body.classList.remove('snap'), 2500);
  computeReach();
  ui.renderStatus();
  ui.syncToken();
  applyHash();
  function fadeLoader() {
    const l = document.getElementById('loading');
    l.classList.add('done');
    if (document.body.classList.contains('snap') && location.hash.length > 1) { l.style.display = 'none'; return; }
    const t0 = performance.now();
    (function step(now) {
      const k = Math.min(1, (now - t0) / 450);
      l.style.opacity = String(1 - k);
      if (k < 1) requestAnimationFrame(step); else l.style.display = 'none';
    })(t0);
  }
  let last = performance.now(), firstFrame = true;
  function frame(now) {
    const dt = Math.min(0.05, Math.max(0.001, (now - last) / 1000));
    last = now;
    stepAutopilot(dt);
    update(dt);
    if (dirty || state.panelsDirty) {
      const fades = S.render(state);
      ui.updateOverlay(fades, view.tilt);
      ui.updateCrumbs();
      ui.updateScale();
      dirty = false;
      if (firstFrame) { firstFrame = false; fadeLoader(); }
    }
    requestAnimationFrame(frame);
  }
  requestAnimationFrame(frame);
  RM.debug = { state, view, goal, S, M, ui, act };
  if (/selftest/.test(location.hash)) selfTest();
  function selfTest() {
    const log = (...a) => console.log('TEST', ...a);
    window.addEventListener('error', e => console.log('TEST ERROR', e.message, e.filename, e.lineno));
    const canvas = S.renderer.domElement;
    const pe = (type, x, y, extra) => canvas.dispatchEvent(new PointerEvent(type, Object.assign({ clientX: x, clientY: y, pointerId: 7, button: 0, buttons: type === 'pointerup' ? 0 : 1, bubbles: true }, extra || {})));
    const key = k => document.dispatchEvent(new KeyboardEvent('keydown', { key: k, bubbles: true }));
    const wheel = (x, y, dy) => canvas.dispatchEvent(new WheelEvent('wheel', { clientX: x, clientY: y, deltaY: dy, deltaMode: 0, bubbles: true, cancelable: true }));
    const sirius = M.NAME.indexOf('Sirius');
    const steps = [
      () => { wheel(800, 450, -300); log('wheel in, goal.w', goal.w.toFixed(1)); },
      () => { pe('pointerdown', 800, 450); pe('pointermove', 760, 430); pe('pointermove', 700, 400); pe('pointerup', 700, 400); log('dragged, centre', view.cx.toFixed(1), view.cz.toFixed(1)); },
      () => { key(' '); log('recentre'); },
      () => { const p = S.project(M.X[sirius], M.Y[sirius], M.Z[sirius], [0, 0]); pe('pointerdown', p[0], p[1]); pe('pointerup', p[0], p[1]); log('clicked Sirius at', p.map(v => v.toFixed(0)).join(','), 'selected', M.NAME[state.selected]); },
      () => { key('r'); log('route', state.route && state.route.jumps, 'jumps'); },
      () => { key('p'); log('autopilot running', !!(state.autopilot && state.autopilot.running)); for (let k = 0; k < 300; k++) { stepAutopilot(0.05); update(0.05); } },
      () => { log('after autopilot: arrived', !!(state.autopilot && state.autopilot.arrived), 'ship at', M.NAME[state.ship.sys], 'fuel', state.ship.fuel); },
      () => { key('Escape'); key('Escape'); log('esc x2, selected', state.selected, 'route', !!state.route); },
      () => { key('t'); log('tilt on'); },
      () => { pe('pointerdown', 800, 450, { button: 2 }); pe('pointermove', 900, 450, { button: 2 }); pe('pointerup', 900, 450, { button: 2 }); log('rotated yaw', goal.yaw.toFixed(2)); },
      () => { key('t'); log('tilt off'); },
      () => { for (let i = 0; i < 12; i++) wheel(800, 450, 400); for (let k = 0; k < 100; k++) update(0.05); S.render(state); ui.updateCrumbs(); log('zoomed out to', goal.w.toFixed(0), 'view', view.w.toFixed(0)); },
      () => { log('crumbs', document.getElementById('crumbs').textContent); key('Escape'); for (let k = 0; k < 100; k++) update(0.05); log('esc -> w', view.w.toFixed(0)); },
      () => { ui.focusSearch(); ui.searchInput.value = 'barn'; ui.searchInput.dispatchEvent(new Event('input')); ui.searchInput.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true })); log('search barn ->', M.NAME[state.selected]); },
      () => { act.finder('rare'); log('finder rare', state.finderResult.count, 'matches; top', M.NAME[state.finderTop[0]]); },
      () => { ui.searchInput.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true })); log('finder cleared', state.finder === null); },
      () => { key('l'); key('Escape'); key('g'); key('f'); log('legend/target/fav ok', state.target, state.favourites.size); },
      () => { log('PASS', 'w', view.w.toFixed(0)); },
    ];
    let i = 0;
    (function next() {
      if (i >= steps.length) return;
      try { if (steps[i]) steps[i](); } catch (err) { console.log('TEST ERROR step', i, err.message, err.stack); }
      i++; setTimeout(next, 900);
    })();
  }
  if (/debug/.test(location.hash)) {
    setTimeout(() => {
      const lbls = [...document.querySelectorAll('#overlay > div')].filter(e => e.style.display !== 'none');
      console.log('DBG overlay', lbls.length, lbls.slice(0, 12).map(e => e.className + ':' + getComputedStyle(e).opacity + ':' + e.textContent.slice(0, 18)).join(' | '));
      console.log('DBG anchors', JSON.stringify(state.ringAnchors), 'inTank', state.reach.inTank, 'w', view.w, 'refY', view.refY);
    }, 4000);
  }
};
