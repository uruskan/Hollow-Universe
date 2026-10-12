/* Hollow Universe map, concept A "Search & Go": the 3D map.
   Stars, galaxy, route, camera, picking and collision-free HTML labels. Started by index.html once three.js is loaded. */
(function () {
  'use strict';
  const H = window.HD;
  const V = window.MapView = {};
  let THREE, Line2, LineMaterial, LineGeometry;
  let renderer, scene, camera, canvas, W = 1, Hh = 1, DPR = 1;
  const ACCENT_HEX = '#5fe3c0';
  const DANGER_HEX = '#ff6b5e';

  // ---------- state shared with the UI ----------
  V.state = { sel: null, target: null, route: null, list: null, highlight: null, favs: new Set(), recents: [], insetTop: 84, insetBottom: 0, insetLeft: 0, hidden: false };
  V.onPick = null; V.onHover = null; V.onLabel = null; V.onUserMove = null;

  // ---------- camera ----------
  // The camera looks at (tx,ty,tz). Zooming walks a path between a local anchor and a galaxy framing point,
  // so scrolling out from the neighbourhood brings the whole galaxy into view and scrolling back returns.
  const DEG = Math.PI / 180;
  const cam = { tx: 0, ty: 0, tz: 0, dist: 60, yaw: 0, pitch: 28 * DEG };
  const goal = { tx: 0, ty: 0, tz: 0, dist: 60, yaw: 0, pitch: 28 * DEG };
  const GAL_FRAME = [-20, -60, 12500], GAL_PITCH = 64 * DEG;
  const anchor = { x: 0, y: 0, z: 0, pitch: 28 * DEG }, frameP = { x: GAL_FRAME[0], y: GAL_FRAME[1], z: GAL_FRAME[2], pitch: GAL_PITCH };
  let tween = null, viewShift = 0, viewShiftGoal = 0, viewShiftX = 0, viewShiftXGoal = 0;
  const easeInOut = t => t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2;
  const smooth = (a, b, x) => { const t = Math.min(1, Math.max(0, (x - a) / (b - a))); return t * t * (3 - 2 * t); };
  function galW(d) { return smooth(Math.log(1500), Math.log(45000), Math.log(d)); }

  V.flyTo = function (o) {
    const from = { tx: cam.tx, ty: cam.ty, tz: cam.tz, dist: cam.dist, yaw: cam.yaw, pitch: cam.pitch };
    const to = {
      tx: o.target ? o.target[0] : from.tx, ty: o.target ? o.target[1] : from.ty, tz: o.target ? o.target[2] : from.tz,
      dist: o.dist || from.dist, yaw: o.yaw == null ? cam.yaw : o.yaw, pitch: o.pitch == null ? cam.pitch : o.pitch
    };
    if (!o.keepAnchor) {
      anchor.x = to.tx; anchor.y = to.ty; anchor.z = to.tz; anchor.pitch = galW(to.dist) > 0.5 ? 30 * DEG : to.pitch;
      if (o.galaxy) { frameP.x = to.tx; frameP.y = to.ty; frameP.z = to.tz; frameP.pitch = to.pitch; }
      else { frameP.x = GAL_FRAME[0]; frameP.y = GAL_FRAME[1]; frameP.z = GAL_FRAME[2]; frameP.pitch = GAL_PITCH; }
    }
    let dy = to.yaw - from.yaw; while (dy > Math.PI) dy -= 2 * Math.PI; while (dy < -Math.PI) dy += 2 * Math.PI; to.yaw = from.yaw + dy;
    const travel = Math.hypot(to.tx - from.tx, to.ty - from.ty, to.tz - from.tz);
    const arc = o.arc != null ? o.arc : Math.min(2.0, Math.max(0, Math.log(1 + travel / Math.max(6, Math.min(from.dist, to.dist))) * 0.5));
    const dur = o.instant ? 0 : (o.dur || Math.min(2.6, 1.0 + arc * 0.45 + Math.abs(Math.log(to.dist / from.dist)) * 0.1));
    Object.assign(goal, to);
    if (dur <= 0) { Object.assign(cam, to); tween = null; if (o.done) o.done(); return; }
    tween = { from, to, t: 0, dur, arc, cb: o.done };
  };
  V.camera = () => ({ target: [cam.tx, cam.ty, cam.tz], dist: cam.dist, yaw: cam.yaw, pitch: cam.pitch, galaxy: galW(cam.dist) });
  V.isGalaxy = () => galW(cam.dist) > 0.5;
  V.isTweening = () => !!tween;
  V.GAL_FRAME = GAL_FRAME; V.GAL_PITCH = GAL_PITCH;
  V.setInsets = function (top, bottom, left) { V.state.insetTop = top; V.state.insetBottom = bottom; V.state.insetLeft = left || 0; viewShiftGoal = (top - bottom) / 2; viewShiftXGoal = (left || 0) / 2; uiFrame = 0; };
  function effTarget() { return [cam.tx, cam.ty, cam.tz]; }

  function updateCamera(dt) {
    if (tween) {
      tween.t += dt / tween.dur;
      const t = Math.min(1, tween.t), e = easeInOut(t), f = tween.from, g = tween.to;
      cam.tx = f.tx + (g.tx - f.tx) * e; cam.ty = f.ty + (g.ty - f.ty) * e; cam.tz = f.tz + (g.tz - f.tz) * e;
      cam.yaw = f.yaw + (g.yaw - f.yaw) * e; cam.pitch = f.pitch + (g.pitch - f.pitch) * e;
      cam.dist = Math.exp(Math.log(f.dist) + (Math.log(g.dist) - Math.log(f.dist)) * e + tween.arc * Math.sin(Math.PI * e));
      if (t >= 1) { const cb = tween.cb; tween = null; Object.assign(goal, g); if (cb) cb(); }
    } else {
      const k = 1 - Math.exp(-dt * 9);
      cam.tx += (goal.tx - cam.tx) * k; cam.ty += (goal.ty - cam.ty) * k; cam.tz += (goal.tz - cam.tz) * k;
      cam.yaw += (goal.yaw - cam.yaw) * k; cam.pitch += (goal.pitch - cam.pitch) * k;
      cam.dist = Math.exp(Math.log(cam.dist) + (Math.log(goal.dist) - Math.log(cam.dist)) * k);
    }
    const ks = 1 - Math.exp(-dt * 7);
    viewShift += (viewShiftGoal - viewShift) * ks; viewShiftX += (viewShiftXGoal - viewShiftX) * ks;
    const p = cam.pitch, cp = Math.cos(p), sp = Math.sin(p);
    camera.position.set(cam.tx - Math.sin(cam.yaw) * cp * cam.dist, cam.ty + sp * cam.dist, cam.tz - Math.cos(cam.yaw) * cp * cam.dist);
    camera.up.set(0, 1, 0);
    camera.lookAt(cam.tx, cam.ty, cam.tz);
    camera.near = Math.max(0.02, cam.dist * 0.002); camera.far = Math.max(4e5, cam.dist * 40);
    camera.setViewOffset(W, Hh, -viewShiftX, -viewShift, W, Hh);
    camera.updateProjectionMatrix();
    camera.updateMatrixWorld();
  }

  // ---------- input ----------
  let drag = null, mouse = { x: -1, y: -1, inside: false }, lastMoveT = 0, hoverSys = -1, pickFrame = 0;
  V.hoverSys = -1;
  function userMoved() { if (tween) { Object.assign(goal, { tx: cam.tx, ty: cam.ty, tz: cam.tz, dist: cam.dist, yaw: cam.yaw, pitch: cam.pitch }); tween = null; } if (V.onUserMove) V.onUserMove(); }
  function shiftAll(mx, my, mz) { goal.tx += mx; goal.ty += my; goal.tz += mz; anchor.x += mx; anchor.y += my; anchor.z += mz; frameP.x += mx; frameP.y += my; frameP.z += mz; }
  function panBy(dxPx, dyPx) {
    const s = cam.dist * 0.9 / Hh / Math.max(0.35, Math.sin(cam.pitch) * 0.6 + 0.4);
    const fx = Math.sin(cam.yaw), fz = Math.cos(cam.yaw), rx = -fz, rz = fx;
    shiftAll((-rx * dxPx + fx * dyPx) * s, 0, (-rz * dxPx + fz * dyPx) * s);
  }
  function zoomTo(nd) {
    nd = Math.min(260000, Math.max(2.5, nd));
    const w0 = galW(goal.dist), w1 = galW(nd);
    if (w1 > w0 && w0 < 1) {
      const f = (w1 - w0) / (1 - w0);
      goal.tx += (frameP.x - goal.tx) * f; goal.ty += (frameP.y - goal.ty) * f; goal.tz += (frameP.z - goal.tz) * f; goal.pitch += (frameP.pitch - goal.pitch) * f;
    } else if (w1 < w0 && w0 > 0) {
      const f = (w0 - w1) / w0;
      goal.tx += (anchor.x - goal.tx) * f; goal.ty += (anchor.y - goal.ty) * f; goal.tz += (anchor.z - goal.tz) * f; goal.pitch += (anchor.pitch - goal.pitch) * f;
    }
    goal.dist = nd;
  }
  V.zoomBy = (f) => { userMoved(); zoomTo(goal.dist * f); };
  V.panBy = (x, y) => { userMoved(); panBy(x, y); };
  function orbit(dyaw, dpitch) {
    goal.yaw += dyaw;
    const np = Math.min(88 * DEG, Math.max(4 * DEG, goal.pitch + dpitch)), d = np - goal.pitch;
    goal.pitch = np; anchor.pitch = Math.min(88 * DEG, Math.max(4 * DEG, anchor.pitch + d)); frameP.pitch = Math.min(88 * DEG, Math.max(4 * DEG, frameP.pitch + d));
  }
  V.orbitBy = (dyaw, dpitch) => { userMoved(); orbit(dyaw, dpitch); };
  V.follow = function (p) { if (tween) return; goal.tx = p[0]; goal.ty = p[1]; goal.tz = p[2]; anchor.x = p[0]; anchor.y = p[1]; anchor.z = p[2]; };

  function bindInput() {
    canvas.addEventListener('contextmenu', e => e.preventDefault());
    canvas.addEventListener('pointerdown', e => {
      canvas.setPointerCapture(e.pointerId);
      drag = { x: e.clientX, y: e.clientY, pan: e.button === 2 || e.button === 1 || e.shiftKey, moved: 0, button: e.button };
    });
    canvas.addEventListener('pointermove', e => {
      mouse.x = e.clientX; mouse.y = e.clientY; mouse.inside = true; lastMoveT = performance.now();
      if (!drag) return;
      const dx = e.clientX - drag.x, dy = e.clientY - drag.y; drag.x = e.clientX; drag.y = e.clientY; drag.moved += Math.abs(dx) + Math.abs(dy);
      if (drag.moved < 4) return;
      userMoved();
      if (drag.pan) panBy(dx, dy);
      else orbit(dx * 0.005, dy * 0.004);
      document.body.classList.add('dragging');
    });
    const up = e => {
      if (drag && drag.moved < 4 && drag.button === 0) { const hit = pickAt(e.clientX, e.clientY); if (V.onPick) V.onPick(hit); }
      drag = null; document.body.classList.remove('dragging');
    };
    canvas.addEventListener('pointerup', up);
    canvas.addEventListener('pointercancel', () => { drag = null; document.body.classList.remove('dragging'); });
    canvas.addEventListener('pointerleave', () => { mouse.inside = false; });
    canvas.addEventListener('wheel', e => {
      e.preventDefault(); userMoved();
      const dy = e.deltaMode === 1 ? e.deltaY * 33 : e.deltaY;
      zoomTo(goal.dist * Math.exp(Math.max(-1, Math.min(1, dy * (e.ctrlKey ? 0.012 : 0.0016)))));
    }, { passive: false });
  }

  // ---------- picking ----------
  const VP = new Float32Array(16);
  function refreshVP() {
    const m = new THREE.Matrix4().multiplyMatrices(camera.projectionMatrix, camera.matrixWorldInverse);
    VP.set(m.elements);
  }
  function project(x, y, z, out) {
    const e = VP;
    const cw = e[3] * x + e[7] * y + e[11] * z + e[15];
    if (cw <= 1e-6) { out.ok = false; return out; }
    const cx = e[0] * x + e[4] * y + e[8] * z + e[12], cy = e[1] * x + e[5] * y + e[9] * z + e[13];
    out.x = (cx / cw * 0.5 + 0.5) * W; out.y = (-cy / cw * 0.5 + 0.5) * Hh; out.w = cw; out.ok = true;
    return out;
  }
  V.project = (p) => project(p[0], p[1], p[2], {});
  const _p = {};
  function pickAt(px, py, radius) {
    if (galW(cam.dist) > 0.6 || localAlpha < 0.15) return -1;
    radius = radius || 14;
    let best = -1, bs = radius * radius;
    const X = H.X, Y = H.Y, Z = H.Z, n = H.N;
    for (let i = 0; i < n; i++) {
      const p = project(X[i], Y[i], Z[i], _p);
      if (!p.ok) continue;
      const dx = p.x - px, dy = p.y - py, d2 = dx * dx + dy * dy;
      if (d2 > radius * radius) continue;
      const score = d2 - starSize[i] * 9 - (H.isProper[i] ? 30 : 0);
      if (score < bs) { bs = score; best = i; }
    }
    return best;
  }
  V.pickAt = pickAt;

  // ---------- local stars ----------
  let localPts, localMat, starSize, hiAttr, localAlpha = 1;
  function desat(hex, k) {
    const c = new THREE.Color(hex);
    const l = c.r * 0.299 + c.g * 0.587 + c.b * 0.114;
    return [l + (c.r - l) * k, l + (c.g - l) * k, l + (c.b - l) * k];
  }
  function buildLocal() {
    const n = H.N, posA = new Float32Array(n * 3), colA = new Float32Array(n * 3), sizeA = new Float32Array(n), alphaA = new Float32Array(n);
    starSize = sizeA;
    for (let i = 0; i < n; i++) {
      posA[i * 3] = H.X[i]; posA[i * 3 + 1] = H.Y[i]; posA[i * 3 + 2] = H.Z[i];
      let m = H.MAG[i]; if (m > 25) m = 11.5;
      const lum = Math.pow(10, -0.4 * (m - 4.83));
      sizeA[i] = Math.min(8.5, 1.35 + 2.0 * Math.pow(lum, 0.2));
      alphaA[i] = Math.min(1, Math.max(0.16, 0.3 + 0.2 * (Math.log10(lum) + 3.2)));
      const c = desat(H.COL[i], 0.62);
      colA[i * 3] = c[0]; colA[i * 3 + 1] = c[1]; colA[i * 3 + 2] = c[2];
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(posA, 3));
    g.setAttribute('aColor', new THREE.BufferAttribute(colA, 3));
    g.setAttribute('aSize', new THREE.BufferAttribute(sizeA, 1));
    g.setAttribute('aAlpha', new THREE.BufferAttribute(alphaA, 1));
    hiAttr = new THREE.BufferAttribute(new Float32Array(n), 1); hiAttr.setUsage(THREE.DynamicDrawUsage);
    g.setAttribute('aHi', hiAttr);
    localMat = new THREE.ShaderMaterial({
      uniforms: {
        uPx: { value: DPR }, uRef: { value: 60 }, uFade: { value: 1 }, uDim: { value: 0 }, uFocus: { value: new THREE.Vector4(0, 0, 0, 1e9) }, uFocusAmt: { value: 0 },
        uHiCol: { value: new THREE.Color(ACCENT_HEX) }
      },
      vertexShader: `
        attribute vec3 aColor; attribute float aSize; attribute float aAlpha; attribute float aHi;
        uniform float uPx, uRef, uFade, uDim, uFocusAmt; uniform vec4 uFocus;
        varying vec3 vCol; varying float vA; varying float vHi;
        void main(){
          vec4 mv = modelViewMatrix * vec4(position, 1.0);
          gl_Position = projectionMatrix * mv;
          float d = max(0.01, -mv.z);
          float persp = clamp(pow(uRef / d, 0.55), 0.55, 2.6);
          float hi = aHi * uDim;
          float s = aSize * persp * (1.0 + 0.55 * hi) * (1.0 - 0.3 * uDim * (1.0 - aHi));
          float a = aAlpha * mix(1.0, 0.45, smoothstep(uRef * 1.2, uRef * 3.6, d));
          a *= 1.0 - 0.86 * uDim * (1.0 - aHi);
          float fd = distance(position, uFocus.xyz);
          a *= 1.0 - 0.72 * uFocusAmt * smoothstep(uFocus.w, uFocus.w * 2.6, fd);
          a = max(a, hi * 0.95);
          vA = a * uFade; vHi = hi;
          vCol = aColor;
          gl_PointSize = max(1.0, s * 2.4 * uPx);
          if (vA < 0.004) gl_Position = vec4(2.0, 2.0, 2.0, 1.0);
        }`,
      fragmentShader: `
        uniform vec3 uHiCol;
        varying vec3 vCol; varying float vA; varying float vHi;
        void main(){
          float r = length(gl_PointCoord - 0.5) * 2.0;
          if (r > 1.0) discard;
          float core = smoothstep(0.5, 0.18, r);
          float halo = pow(1.0 - r, 2.2) * 0.42;
          vec3 col = mix(vCol, vec3(1.0), core * 0.35);
          col = mix(col, mix(col, uHiCol, 0.35), vHi * (1.0 - core));
          gl_FragColor = vec4(col, (core + halo) * vA);
        }`,
      transparent: true, depthTest: false, depthWrite: false, blending: THREE.AdditiveBlending
    });
    localPts = new THREE.Points(g, localMat);
    localPts.frustumCulled = false; localPts.renderOrder = 2;
    scene.add(localPts);
  }
  V.setHighlight = function (list) {
    const a = hiAttr.array; a.fill(0);
    if (list) for (const i of list) a[i] = 1;
    hiAttr.needsUpdate = true;
    V.state.highlight = list && list.length ? list : null;
  };

  // ---------- galaxy ----------
  let galPts, galMat, glowMesh, glowMat;
  function b64(s) { const bin = atob(s), u = new Uint8Array(bin.length); for (let i = 0; i < bin.length; i++) u[i] = bin.charCodeAt(i); return u; }
  function buildGalaxy() {
    const S = H.G.stars;
    const pos = new Float32Array(b64(S.pos).buffer), col = b64(S.col), bright = b64(S.bright), comp = b64(S.comp), lvl = b64(S.lvl);
    const n = S.count, colA = new Float32Array(n * 3), bA = new Float32Array(n), yA = new Float32Array(n), kA = new Float32Array(n);
    const KW = [1, 0.55, 0.22, 0.26, 0.3, 0.08];
    for (let i = 0; i < n; i++) {
      const young = comp[i] === 0;
      yA[i] = young ? 1 : 0; kA[i] = KW[comp[i]] || 0.5;
      let r = col[i * 3] / 255, g = col[i * 3 + 1] / 255, b = col[i * 3 + 2] / 255;
      const l = r * 0.3 + g * 0.59 + b * 0.11;
      r = l + (r - l) * 0.55; g = l + (g - l) * 0.55; b = l + (b - l) * 0.55;
      if (young) { r *= 0.86; g *= 0.95; b = Math.min(1, b * 1.12); }
      else if (comp[i] === 3 || comp[i] === 4) { g *= 0.86; b *= 0.66; }
      colA[i * 3] = r; colA[i * 3 + 1] = g; colA[i * 3 + 2] = b;
      bA[i] = bright[i] / 255 * (young ? 1.18 : 0.92);
    }
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    geo.setAttribute('aColor', new THREE.BufferAttribute(colA, 3));
    geo.setAttribute('aB', new THREE.BufferAttribute(bA, 1));
    geo.setAttribute('aY', new THREE.BufferAttribute(yA, 1));
    geo.setAttribute('aK', new THREE.BufferAttribute(kA, 1));
    galMat = new THREE.ShaderMaterial({
      uniforms: { uPx: { value: DPR }, uFade: { value: 0 }, uSky: { value: 0.5 }, uNear: { value: 3000 }, uRef: { value: 60000 } },
      vertexShader: `
        attribute vec3 aColor; attribute float aB; attribute float aY; attribute float aK;
        uniform float uPx, uFade, uSky, uNear, uRef;
        varying vec3 vCol; varying float vA;
        void main(){
          vec4 mv = modelViewMatrix * vec4(position, 1.0);
          gl_Position = projectionMatrix * mv;
          float d = max(1.0, -mv.z);
          float gal = uFade;
          float near = smoothstep(uNear * 0.6, uNear, d);
          float persp = clamp(pow(uRef / d, 0.5), 0.6, 3.0);
          float sGal = (1.0 + 1.5 * pow(aB, 2.0) + aY * 0.7) * persp;
          float sSky = 3.2 + 2.0 * pow(aB, 3.0);
          float s = mix(sSky, sGal, gal);
          float aG = (0.016 + 0.09 * pow(aB, 2.4)) * aK + aY * (0.03 + 0.17 * pow(aB, 1.6));
          float a = mix(uSky * (0.006 + 0.07 * pow(aB, 2.0)) * aK * near, aG, gal);
          a *= mix(1.0, clamp(d / uRef * 1.6, 0.35, 1.0), gal);
          vA = a; vCol = aColor;
          gl_PointSize = max(1.0, s * 2.0 * uPx);
          if (vA < 0.002) gl_Position = vec4(2.0, 2.0, 2.0, 1.0);
        }`,
      fragmentShader: `
        varying vec3 vCol; varying float vA;
        void main(){
          float r = length(gl_PointCoord - 0.5) * 2.0;
          if (r > 1.0) discard;
          float a = pow(1.0 - r, 1.8);
          gl_FragColor = vec4(vCol, a * vA);
        }`,
      transparent: true, depthTest: false, depthWrite: false, blending: THREE.AdditiveBlending
    });
    galPts = new THREE.Points(geo, galMat); galPts.frustumCulled = false; galPts.renderOrder = 1;
    scene.add(galPts);

    // face-on glow image
    const gw = H.G.glow, size = gw.size, all = b64(gw.all.data), young = b64(gw.young.data);
    const tex = new Uint8Array(size * size * 4);
    const fA = new Float32Array(size * size), fY = new Float32Array(size * size);
    for (let i = 0; i < size * size; i++) {
      fA[i] = all[i] ? (all[i] - 1) / 254 : 0;
      fY[i] = young[i] ? (young[i] - 1) / 254 : 0;
    }
    const blur = (src) => { // two box passes
      let a = src;
      for (let pass = 0; pass < 2; pass++) {
        const o = new Float32Array(size * size);
        for (let z = 0; z < size; z++) for (let x = 0; x < size; x++) {
          let s = 0, c = 0;
          for (let dz = -1; dz <= 1; dz++) for (let dx = -1; dx <= 1; dx++) { const xx = x + dx, zz = z + dz; if (xx < 0 || zz < 0 || xx >= size || zz >= size) continue; s += a[zz * size + xx]; c++; }
          o[z * size + x] = s / c;
        }
        a = o;
      }
      return a;
    };
    const bA2 = blur(fA), bY2 = blur(fY);
    for (let i = 0; i < size * size; i++) {
      const a = Math.pow(Math.max(0, bA2[i] - 0.22) / 0.78, 2.4), y = Math.pow(Math.max(0, bY2[i] - 0.3) / 0.7, 2.0);
      const r = a * 255 * 1.0 + y * 120, g = a * 205 + y * 160, b = a * 150 + y * 220;
      tex[i * 4] = Math.min(255, r); tex[i * 4 + 1] = Math.min(255, g); tex[i * 4 + 2] = Math.min(255, b); tex[i * 4 + 3] = 255;
    }
    const dt = new THREE.DataTexture(tex, size, size, THREE.RGBAFormat);
    dt.magFilter = THREE.LinearFilter; dt.minFilter = THREE.LinearFilter; dt.needsUpdate = true;
    const x0 = gw.x0, z0 = gw.z0, x1 = x0 + size * gw.cellLy, z1 = z0 + size * gw.cellLy, yy = -68;
    const geo2 = new THREE.BufferGeometry();
    geo2.setAttribute('position', new THREE.BufferAttribute(new Float32Array([x0, yy, z0, x1, yy, z0, x1, yy, z1, x0, yy, z1]), 3));
    geo2.setAttribute('uv', new THREE.BufferAttribute(new Float32Array([0, 0, 1, 0, 1, 1, 0, 1]), 2));
    geo2.setIndex([0, 2, 1, 0, 3, 2]);
    glowMat = new THREE.ShaderMaterial({
      uniforms: { uTex: { value: dt }, uA: { value: 0 } },
      vertexShader: `varying vec2 vUv; void main(){ vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position,1.0); }`,
      fragmentShader: `uniform sampler2D uTex; uniform float uA; varying vec2 vUv; void main(){ vec3 c = texture2D(uTex, vUv).rgb; gl_FragColor = vec4(c * uA * 0.42, 1.0); }`,
      transparent: true, depthTest: false, depthWrite: false, blending: THREE.AdditiveBlending, side: THREE.DoubleSide
    });
    glowMesh = new THREE.Mesh(geo2, glowMat); glowMesh.frustumCulled = false; glowMesh.renderOrder = 0;
    scene.add(glowMesh);
  }

  // ---------- reference rings around the ship, range ring, danger ----------
  let rings, rangeRing, gcLine, dangerMeshes = [], dropLines, dropPos;
  function circlePts(r, seg) { const a = []; for (let k = 0; k <= seg; k++) { const t = k / seg * Math.PI * 2; a.push(Math.cos(t) * r, 0, Math.sin(t) * r); } return a; }
  function buildRefs() {
    rings = new THREE.Group();
    const radii = [25, 50, 100, 150];
    for (const r of radii) {
      const g = new THREE.BufferGeometry(); g.setAttribute('position', new THREE.Float32BufferAttribute(circlePts(r, 192), 3));
      const m = new THREE.LineBasicMaterial({ color: 0xffffff, transparent: true, opacity: 0.075, depthTest: false, depthWrite: false });
      const l = new THREE.Line(g, m); l.renderOrder = 1; rings.add(l);
    }
    { // spoke toward the Galactic Centre
      const g = new THREE.BufferGeometry(); g.setAttribute('position', new THREE.Float32BufferAttribute([0, 0, 0, 0, 0, 150], 3));
      gcLine = new THREE.Line(g, new THREE.LineDashedMaterial({ color: 0xffffff, transparent: true, opacity: 0.12, dashSize: 2, gapSize: 2.5, depthTest: false }));
      gcLine.computeLineDistances(); rings.add(gcLine);
    }
    scene.add(rings);
    const g = new THREE.BufferGeometry(); g.setAttribute('position', new THREE.Float32BufferAttribute(circlePts(H.RANGE, 128), 3));
    rangeRing = new THREE.Line(g, new THREE.LineDashedMaterial({ color: new THREE.Color(ACCENT_HEX), transparent: true, opacity: 0.55, dashSize: 0.9, gapSize: 0.7, depthTest: false, depthWrite: false }));
    rangeRing.computeLineDistances(); rangeRing.renderOrder = 3; scene.add(rangeRing);
    // danger spheres
    for (const d of H.M.danger) {
      const geo = new THREE.SphereGeometry(d.radiusLy, 64, 40);
      const mat = new THREE.ShaderMaterial({
        uniforms: { uCol: { value: new THREE.Color(d.kind === 'hazard' ? '#ffb05e' : DANGER_HEX) }, uA: { value: 0.5 } },
        vertexShader: `varying vec3 vN; varying vec3 vV; void main(){ vec4 mv = modelViewMatrix * vec4(position,1.0); vN = normalize(normalMatrix * normal); vV = normalize(-mv.xyz); gl_Position = projectionMatrix * mv; }`,
        fragmentShader: `uniform vec3 uCol; uniform float uA; varying vec3 vN; varying vec3 vV; void main(){ float f = 1.0 - abs(dot(normalize(vN), normalize(vV))); float a = (pow(f, 3.2) * 0.32 + 0.022) * uA; gl_FragColor = vec4(uCol, a); }`,
        transparent: true, depthTest: false, depthWrite: false, blending: THREE.AdditiveBlending
      });
      const mesh = new THREE.Mesh(geo, mat); mesh.position.set(d.centre[0], d.centre[1], d.centre[2]); mesh.renderOrder = 1;
      mesh.userData = d; dangerMeshes.push(mesh); scene.add(mesh);
    }
    // drop lines for labelled stars
    dropPos = new Float32Array(64 * 6);
    const dg = new THREE.BufferGeometry(); dg.setAttribute('position', new THREE.BufferAttribute(dropPos, 3).setUsage(THREE.DynamicDrawUsage));
    dropLines = new THREE.LineSegments(dg, new THREE.LineBasicMaterial({ color: 0xffffff, transparent: true, opacity: 0.16, depthTest: false, depthWrite: false }));
    dropLines.frustumCulled = false; dropLines.renderOrder = 1; scene.add(dropLines);
  }

  // ---------- route ----------
  let routeGroup = null, routeCurve = null, routeSamples = null, comet, cometMat, stopPts, stopMat, routeT = 0;
  V.setRoute = function (route) {
    if (routeGroup) { scene.remove(routeGroup); routeGroup.traverse(o => { if (o.geometry) o.geometry.dispose(); }); routeGroup = null; }
    routeCurve = null; routeSamples = null;
    V.state.route = route || null;
    if (!route) return;
    routeGroup = new THREE.Group();
    const pts = [];
    // gently curved path through the stops (Catmull-Rom, low tension) for a calm line
    const raw = route.stops.map(s => new THREE.Vector3(H.X[s.sys], H.Y[s.sys], H.Z[s.sys]));
    routeCurve = raw.length > 2 ? new THREE.CatmullRomCurve3(raw, false, 'centripetal', 0.2) : new THREE.LineCurve3(raw[0], raw[1]);
    const samples = Math.max(24, raw.length * 14);
    for (let k = 0; k <= samples; k++) { const p = routeCurve.getPoint(k / samples); pts.push(p.x, p.y, p.z); }
    routeSamples = pts;
    const mk = (w, op) => {
      const g = new LineGeometry(); g.setPositions(pts);
      const m = new LineMaterial({ color: new THREE.Color(ACCENT_HEX), linewidth: w, transparent: true, opacity: op, depthTest: false, depthWrite: false, blending: THREE.AdditiveBlending });
      const l = new Line2(g, m); l.renderOrder = 5; l.frustumCulled = false; l.userData.base = op; return l;
    };
    routeGroup.add(mk(16, 0.07), mk(7, 0.16), mk(2.6, 0.95));
    // stops
    const n = route.stops.length, sp = new Float32Array(n * 3), sk = new Float32Array(n);
    route.stops.forEach((s, k) => {
      sp[k * 3] = H.X[s.sys]; sp[k * 3 + 1] = H.Y[s.sys]; sp[k * 3 + 2] = H.Z[s.sys];
      sk[k] = k === 0 ? 3 : k === n - 1 ? 2 : s.danger >= 0 ? 4 : s.refuel ? 1 : 0;
    });
    const sg = new THREE.BufferGeometry(); sg.setAttribute('position', new THREE.BufferAttribute(sp, 3)); sg.setAttribute('aKind', new THREE.BufferAttribute(sk, 1));
    sg.setAttribute('aDone', new THREE.BufferAttribute(new Float32Array(n), 1).setUsage(THREE.DynamicDrawUsage));
    stopMat = new THREE.ShaderMaterial({
      uniforms: { uPx: { value: DPR }, uAcc: { value: new THREE.Color(ACCENT_HEX) }, uDan: { value: new THREE.Color(DANGER_HEX) } },
      vertexShader: `attribute float aKind; attribute float aDone; uniform float uPx; varying float vK; varying float vD;
        void main(){ vK = aKind; vD = aDone; gl_Position = projectionMatrix * modelViewMatrix * vec4(position,1.0);
          float s = aKind == 0.0 ? 9.0 : aKind == 2.0 ? 17.0 : 13.0; gl_PointSize = s * uPx; }`,
      fragmentShader: `uniform vec3 uAcc; uniform vec3 uDan; varying float vK; varying float vD;
        void main(){ float r = length(gl_PointCoord - 0.5) * 2.0; if (r > 1.0) discard;
          vec3 ring = vK == 4.0 ? uDan : uAcc;
          float edge = smoothstep(1.0, 0.82, r);
          float inner = smoothstep(0.62, 0.5, r);
          vec3 fill = vK == 0.0 ? vec3(0.03,0.05,0.07) : (vK == 1.0 ? vec3(1.0) : vK == 2.0 ? uAcc : vec3(0.03,0.05,0.07));
          if (vK == 3.0) fill = vec3(1.0);
          vec3 c = mix(ring, fill, inner);
          if (vD > 0.5) c = mix(c, vec3(0.35), 0.6);
          gl_FragColor = vec4(c, edge); }`,
      transparent: true, depthTest: false, depthWrite: false
    });
    stopPts = new THREE.Points(sg, stopMat); stopPts.renderOrder = 6; stopPts.frustumCulled = false; routeGroup.add(stopPts);
    // comet
    const cg = new THREE.BufferGeometry(); cg.setAttribute('position', new THREE.BufferAttribute(new Float32Array(3), 3));
    cometMat = new THREE.ShaderMaterial({
      uniforms: { uPx: { value: DPR }, uAcc: { value: new THREE.Color(ACCENT_HEX) }, uA: { value: 1 } },
      vertexShader: `uniform float uPx; void main(){ gl_Position = projectionMatrix * modelViewMatrix * vec4(position,1.0); gl_PointSize = 22.0 * uPx; }`,
      fragmentShader: `uniform vec3 uAcc; uniform float uA; void main(){ float r = length(gl_PointCoord - 0.5) * 2.0; if (r > 1.0) discard; float a = pow(1.0 - r, 3.0); gl_FragColor = vec4(mix(uAcc, vec3(1.0), a * 0.7), a * uA); }`,
      transparent: true, depthTest: false, depthWrite: false, blending: THREE.AdditiveBlending
    });
    comet = new THREE.Points(cg, cometMat); comet.renderOrder = 7; comet.frustumCulled = false; routeGroup.add(comet);
    routeT = 0;
    scene.add(routeGroup);
  };
  V.setRouteProgress = function (k) { // stops < k are done
    if (!stopPts) return;
    const a = stopPts.geometry.getAttribute('aDone');
    for (let i = 0; i < a.count; i++) a.array[i] = i < k ? 1 : 0;
    a.needsUpdate = true;
  };
  V.routePoint = function (t) { if (!routeCurve) return null; const p = routeCurve.getPoint(Math.min(1, Math.max(0, t))); return [p.x, p.y, p.z]; };
  V.routeParamOfStop = function (k) { // curve param of stop k (centripetal CR passes through points at uniform u per segment)
    const n = V.state.route.stops.length; return routeCurve.getUtoTmapping ? k / (n - 1) : k / (n - 1);
  };

  // ---------- ship position (moves during autopilot) ----------
  let shipPosNow = null;
  V.setShipPos = function (p) { shipPosNow = p; };
  V.cometOff = false;

  // ---------- labels ----------
  const layer = document.getElementById('labels');
  const markLayer = document.getElementById('markers');
  const labelEls = new Map();
  let measureCtx = null;
  const widthCache = new Map();
  function textW(t, font) {
    const k = font + '|' + t; let w = widthCache.get(k);
    if (w == null) { if (!measureCtx) measureCtx = document.createElement('canvas').getContext('2d'); measureCtx.font = font; w = measureCtx.measureText(t).width; widthCache.set(k, w); }
    return w;
  }
  const FONT_L = '500 12.5px Inter, system-ui, sans-serif', FONT_B = '600 13.5px Inter, system-ui, sans-serif', FONT_S = '600 11.5px Inter, system-ui, sans-serif';
  const ICONW = 16;
  const LOCAL_SECTOR = 'Nolcas Peris ';
  function shortName(i) { const n = H.NAME[i]; return !H.CAT[i] && n.indexOf(LOCAL_SECTOR) === 0 ? n.slice(LOCAL_SECTOR.length) : n; }
  V.shortName = shortName;
  // which named stars can be labelled
  let namedList = null;
  function buildNamed() {
    namedList = [];
    for (let i = 0; i < H.N; i++) {
      const d = Math.hypot(H.X[i], H.Y[i], H.Z[i]);
      if (H.isProper[i] || (H.CAT[i] && d < 16) || H.stationsBySys.has(i)) namedList.push(i);
    }
  }
  function uiRects() {
    const out = [];
    document.querySelectorAll('.ui-block').forEach(el => {
      if (el.offsetParent === null && getComputedStyle(el).position !== 'fixed') return;
      const r = el.getBoundingClientRect();
      if (r.width && r.height && getComputedStyle(el).visibility !== 'hidden' && +getComputedStyle(el).opacity > 0.05) out.push([r.left - 6, r.top - 6, r.right + 6, r.bottom + 6]);
    });
    return out;
  }
  let uiCache = [], uiFrame = 0;
  function overlaps(r, list) { for (const q of list) if (r[0] < q[2] && r[2] > q[0] && r[1] < q[3] && r[3] > q[1]) return true; return false; }

  function gatherCandidates() {
    const st = V.state, C = [];
    const w = galW(cam.dist);
    const sp = shipPosNow || H.shipPos();
    if (w > 0.5) {
      C.push({ key: 'g-you', pos: sp, text: 'You are here', kind: 'you', pri: 1000 });
      for (const s of H.structures) C.push({ key: 'st-' + s.id, pos: s.pos, text: s.kind === 'centre' ? 'Galactic Centre' : s.name, kind: 'arm', pri: 600 - (s.kind === 'spur' ? 30 : 0), ref: s });
      if (cam.dist < 14000) {
        const t = effTarget();
        const secs = H.sectors.map(s => [Math.hypot(s.pos[0] - t[0], s.pos[1] - t[1], s.pos[2] - t[2]), s]).sort((a, b) => a[0] - b[0]).slice(0, 12);
        for (const [d, s] of secs) if (s.name !== 'Nolcas Peris') C.push({ key: 'sec-' + s.name, pos: s.pos, text: s.name, kind: 'sector', pri: 300 - d * 0.01, ref: s });
      }
      if (st.sel && st.sel.type === 'structure') C.push({ key: 'st-' + st.sel.ref.id, pos: st.sel.ref.pos, text: st.sel.ref.kind === 'centre' ? 'Galactic Centre' : st.sel.ref.name, kind: 'arm sel', pri: 990, ref: st.sel.ref });
      return C;
    }
    const seen = new Set();
    const add = (sys, kind, pri, text) => {
      if (sys == null || sys < 0 || seen.has(sys)) return;
      seen.add(sys);
      C.push({ key: 's' + sys, sys, pos: [H.X[sys], H.Y[sys], H.Z[sys]], text: text || shortName(sys), kind, pri });
    };
    // distance rings and the way to the core, on the ship's plane, at the edge nearest the camera
    if (!V.state.hidden && cam.dist < 300) {
      const fx = -Math.sin(cam.yaw), fz = -Math.cos(cam.yaw);
      for (const r of [25, 50, 100, 150]) if (r < cam.dist * 2.2 && r > cam.dist * 0.18) C.push({ key: 'ring' + r, pos: [sp[0] + fx * r, sp[1], sp[2] + fz * r], text: r + ' ly', kind: 'ring', pri: 120 });
      C.push({ key: 'gc', pos: [sp[0], sp[1], sp[2] + 150], text: 'Galactic Centre \u2192 26,700 ly', kind: 'gcdir', pri: 130 });
    }
    if (shipPosNow) C.push({ key: 'ship', pos: shipPosNow, text: H.ship.name, kind: 'ship', pri: 1001 });
    if (cam.dist > 520) C.push({ key: 'bubble', pos: [sp[0], sp[1] - 150, sp[2]], text: 'Surveyed bubble \u00b7 37,409 systems within 150 ly', kind: 'gcdir', pri: 1200 });
    if (!shipPosNow) add(H.ship.sys, H.ship.sys === H.HOME ? 'home ship' : 'ship', 1000);
    add(H.HOME, 'home', 995);
    if (st.sel && st.sel.type === 'system') add(st.sel.sys, 'sel', 990);
    if (st.target != null) add(st.target, 'target', 985);
    if (st.route) {
      const n = st.route.stops.length;
      add(st.route.to, 'dest', 980);
      st.route.stops.forEach((s, k) => { if (k > 0 && k < n - 1 && s.danger >= 0) add(s.sys, 'danger', 900 - k); });
      st.route.stops.forEach((s, k) => { if (k > 0 && k < n - 1 && s.refuel) add(s.sys, 'refuel', 890 - k); });
      st.route.stops.forEach((s, k) => { if (k > 0 && k < n - 1) add(s.sys, 'stop', 640 - k); });
    }
    if (st.highlight) st.highlight.slice(0, 14).forEach((i, k) => add(i, 'result', 760 - k * 4));
    const t = effTarget(), R = cam.dist * 1.5;
    for (const sys of st.recents.slice(0, 5)) add(sys, 'recent', 470);
    for (const k of st.favs) if (typeof k === 'number') add(k, 'fav', 480);
    const scored = [];
    for (const i of namedList) {
      if (seen.has(i)) continue;
      const d = Math.hypot(H.X[i] - t[0], H.Y[i] - t[1], H.Z[i] - t[2]);
      if (d > R) continue;
      let sc = 400 - d / R * 260 - Math.max(-2, Math.min(16, H.MAG[i])) * 4 + (H.isProper[i] ? 40 : 0);
      if (H.stationsBySys.has(i)) sc += 90;
      if (H.playersBySys.has(i)) sc += 60;
      scored.push([sc, i]);
    }
    scored.sort((a, b) => b[0] - a[0]);
    for (const [sc, i] of scored.slice(0, st.route || st.highlight ? 5 : 40)) add(i, H.stationsBySys.has(i) ? 'station' : 'star', sc);
    for (const p of H.M.players) if (!seen.has(p.sys)) { const d = Math.hypot(H.X[p.sys] - t[0], H.Y[p.sys] - t[1], H.Z[p.sys] - t[2]); if (d < R) add(p.sys, 'base', 430 - d); }
    return C;
  }

  const ICONS = {
    home: '<svg viewBox="0 0 16 16"><path d="M2.5 7.4 8 3l5.5 4.4V13a.5.5 0 0 1-.5.5H9.6V10H6.4v3.5H3a.5.5 0 0 1-.5-.5Z" fill="currentColor"/></svg>',
    station: '<svg viewBox="0 0 16 16"><rect x="4.2" y="4.2" width="7.6" height="7.6" rx="1.2" transform="rotate(45 8 8)" fill="none" stroke="currentColor" stroke-width="1.6"/></svg>',
    refuel: '<svg viewBox="0 0 16 16"><path d="M8 2.2c2.3 3 3.6 5 3.6 6.8a3.6 3.6 0 0 1-7.2 0C4.4 7.2 5.7 5.2 8 2.2Z" fill="currentColor"/></svg>',
    danger: '<svg viewBox="0 0 16 16"><path d="M8 2.4 14 13H2Z" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linejoin="round"/><path d="M8 6.6v3M8 11.2v.3" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"/></svg>',
    target: '<svg viewBox="0 0 16 16"><circle cx="8" cy="8" r="5" fill="none" stroke="currentColor" stroke-width="1.6"/><circle cx="8" cy="8" r="1.6" fill="currentColor"/></svg>',
    base: '<svg viewBox="0 0 16 16"><path d="M4 14V2.6M4 3h7.5L10 5.6l1.5 2.6H4" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round"/></svg>',
    fav: '<svg viewBox="0 0 16 16"><path d="m8 2.2 1.7 3.6 3.9.5-2.9 2.7.8 3.9L8 11l-3.5 1.9.8-3.9-2.9-2.7 3.9-.5Z" fill="currentColor"/></svg>',
    ship: '<svg viewBox="0 0 16 16"><path d="M8 1.8 12.6 14 8 11.2 3.4 14Z" fill="currentColor"/></svg>'
  };
  V.ICONS = ICONS;
  function labelHTML(c) {
    let icon = '';
    const k = c.kind;
    if (k.indexOf('home') >= 0) icon = ICONS.home;
    else if (k === 'station') icon = ICONS.station;
    else if (k === 'refuel') icon = ICONS.refuel;
    else if (k === 'danger') icon = ICONS.danger;
    else if (k === 'target') icon = ICONS.target;
    else if (k === 'base') icon = ICONS.base;
    else if (k === 'fav') icon = ICONS.fav;
    else if (k === 'ship' || k === 'you') icon = ICONS.ship;
    const sub = c.sub ? `<span class="ls">${c.sub}</span>` : '';
    return (icon ? `<i>${icon}</i>` : '') + `<span class="lt"></span>` + sub;
  }
  function getLabelEl(c) {
    let e = labelEls.get(c.key);
    if (!e) {
      const el = document.createElement('div');
      el.className = 'lbl';
      el.addEventListener('click', ev => { ev.stopPropagation(); if (V.onLabel) V.onLabel(el._c); });
      el.addEventListener('pointerenter', () => { if (el._c && el._c.sys != null) V.hoverSys = el._c.sys; });
      el.addEventListener('pointerleave', () => { V.hoverSys = -1; });
      layer.appendChild(el);
      e = { el, kind: null, text: null, side: 0, on: false, unused: 0 };
      labelEls.set(c.key, e);
    }
    if (e.kind !== c.kind || e.text !== c.text || e.sub !== c.sub) {
      e.el.className = 'lbl k-' + c.kind.split(' ').join(' k-');
      e.el.innerHTML = labelHTML(c);
      e.el.querySelector('.lt').textContent = c.text;
      e.kind = c.kind; e.text = c.text; e.sub = c.sub;
    }
    e.el._c = c;
    return e;
  }
  let cands = [], candFrame = 0;
  const placedDrops = [];
  function layoutLabels() {
    if (++candFrame % 6 === 1 || !cands.length) cands = gatherCandidates();
    if (++uiFrame % 10 === 1) uiCache = uiRects();
    const placed = uiCache.slice();
    const pad = 3, maxN = 15, gal = galW(cam.dist) > 0.5;
    const lineObs = [];
    if (routeCurve && routeSamples && !gal) { // the route line is an obstacle too
      const q = {};
      for (let k = 0; k < routeSamples.length; k += 3) {
        project(routeSamples[k], routeSamples[k + 1], routeSamples[k + 2], q);
        if (q.ok) lineObs.push([q.x - 4, q.y - 4, q.x + 4, q.y + 4, q.x, q.y]);
      }
    }
    let count = 0; placedDrops.length = 0;
    cands.sort((a, b) => b.pri - a.pri);
    const used = new Set();
    const p = {};
    for (const c of cands) {
      if (count >= maxN && c.kind !== 'ring' && c.kind !== 'gcdir') continue;
      project(c.pos[0], c.pos[1], c.pos[2], p);
      if (!p.ok || p.x < -40 || p.y < -40 || p.x > W + 40 || p.y > Hh + 40) continue;
      const annot = c.kind === 'ring' || c.kind === 'gcdir';
      if (c.kind === 'stop' && count >= 12) continue;
      const big = c.kind.indexOf('sel') >= 0 || c.kind === 'dest' || c.kind.indexOf('home') >= 0 || c.kind === 'ship' || c.kind === 'you';
      const font = c.kind.indexOf('arm') >= 0 ? FONT_S : big ? FONT_B : FONT_L;
      let tw = textW(c.kind.indexOf('arm') >= 0 ? c.text.toUpperCase() : c.text, font) + (c.kind.indexOf('arm') >= 0 ? c.text.length * 1.4 : 0);
      const hasIcon = /home|station|refuel|danger|target|base|fav|ship|you/.test(c.kind);
      if (c.sub) tw += textW(c.sub, FONT_L) + 7;
      const wpx = tw + (hasIcon ? ICONW + 4 : 0) + 14, hpx = big ? 24 : 22;
      const r0 = c.sys != null ? Math.max(6, starSize[c.sys] * 1.6) : 8;
      const e = labelEls.get(c.key);
      const sides = c.kind.indexOf('arm') >= 0 || c.kind === 'sector' || c.kind === 'ring' || c.kind === 'gcdir' ? [4] : [0, 1, 2, 3];
      if (e && e.on && sides.length > 1) { const s = sides.indexOf(e.side); if (s > 0) { sides.splice(s, 1); sides.unshift(e.side); } }
      let ok = false, rect, side = 0;
      for (const sd of sides) {
        if (sd === 0) rect = [p.x + r0, p.y - hpx / 2, p.x + r0 + wpx, p.y + hpx / 2];
        else if (sd === 1) rect = [p.x - r0 - wpx, p.y - hpx / 2, p.x - r0, p.y + hpx / 2];
        else if (sd === 2) rect = [p.x - wpx / 2, p.y - r0 - hpx, p.x + wpx / 2, p.y - r0];
        else if (sd === 3) rect = [p.x - wpx / 2, p.y + r0, p.x + wpx / 2, p.y + r0 + hpx];
        else rect = [p.x - wpx / 2, p.y - hpx / 2, p.x + wpx / 2, p.y + hpx / 2];
        if (rect[0] < 4 || rect[1] < 4 || rect[2] > W - 4 || rect[3] > Hh - 4) continue;
        const padded = [rect[0] - pad, rect[1] - pad, rect[2] + pad, rect[3] + pad];
        if (overlaps(padded, placed)) continue;
        let hitLine = false;
        for (const o of lineObs) {
          if (padded[0] < o[2] && padded[2] > o[0] && padded[1] < o[3] && padded[3] > o[1]) {
            const dx = o[4] - p.x, dy = o[5] - p.y;
            if (dx * dx + dy * dy > 26 * 26) { hitLine = true; break; }
          }
        }
        if (!hitLine) { ok = true; side = sd; break; }
      }
      if (!ok) continue;
      placed.push(rect);
      const le = getLabelEl(c);
      le.side = side; le.unused = 0; used.add(c.key);
      le.el.style.transform = `translate3d(${Math.round(rect[0])}px, ${Math.round(rect[1])}px, 0)`;
      le.el.dataset.side = side;
      if (!le.on) { le.on = true; le.el.classList.add('on'); }
      if (!annot) count++;
      if (c.sys != null && !gal && /sel|dest|home|target/.test(c.kind)) placedDrops.push(c.pos);
      // a placed star also blocks its own dot
      placed.push([p.x - r0 + 2, p.y - r0 + 2, p.x + r0 - 2, p.y + r0 - 2]);
    }
    for (const [k, e] of labelEls) {
      if (used.has(k)) continue;
      if (e.on) { e.on = false; e.el.classList.remove('on'); }
      if (++e.unused > 240) { e.el.remove(); labelEls.delete(k); }
    }
  }

  // ---------- markers: ship, selection, hover, stations ----------
  const mk = {};
  function marker(cls, html) { const el = document.createElement('div'); el.className = 'mk ' + cls; el.innerHTML = html || ''; markLayer.appendChild(el); return el; }
  function placeMarker(el, pos, show) {
    if (!show || !pos) { el.style.opacity = 0; return null; }
    const p = project(pos[0], pos[1], pos[2], {});
    if (!p.ok) { el.style.opacity = 0; return null; }
    el.style.opacity = ''; el.style.transform = `translate3d(${p.x.toFixed(1)}px, ${p.y.toFixed(1)}px, 0)`;
    return p;
  }
  function buildMarkers() {
    mk.sel = marker('m-sel');
    mk.hover = marker('m-hover');
    mk.target = marker('m-target');
    mk.ship = marker('m-ship', '<div class="pulse"></div><div class="chev">' + ICONS.ship + '</div>');
    mk.stations = H.M.stations.map(s => { const el = marker('m-station', ''); el._sys = s.sys; return el; });
    mk.bases = H.M.players.map(p => { const el = marker('m-base r-' + p.relation, ''); el._sys = p.sys; return el; });
    mk.home = marker('m-home', ICONS.home);
    mk.tip = document.getElementById('tip');
  }
  function updateMarkers() {
    const st = V.state, local = galW(cam.dist) < 0.5 && localAlpha > 0.3;
    const sp = shipPosNow || H.shipPos();
    placeMarker(mk.ship, sp, true);
    mk.ship.classList.toggle('gal', !local);
    placeMarker(mk.home, [H.X[H.HOME], H.Y[H.HOME], H.Z[H.HOME]], local && (shipPosNow || H.ship.sys !== H.HOME));
    placeMarker(mk.sel, st.sel && st.sel.type === 'system' ? H.pos(st.sel.sys) : null, local);
    placeMarker(mk.target, st.target != null ? H.pos(st.target) : null, local && (!st.sel || st.sel.sys !== st.target));
    for (const el of mk.stations) { const le = labelEls.get('s' + el._sys); placeMarker(el, H.pos(el._sys), local && cam.dist < 200 && !(le && le.on)); }
    for (const el of mk.bases) placeMarker(el, H.pos(el._sys), local && cam.dist < 260);
    // hover
    let hs = -1;
    if (V.hoverSys >= 0) hs = V.hoverSys;
    else if (mouse.inside && !drag && local && performance.now() - lastMoveT < 4000) hs = (++pickFrame % 3 === 0 || hoverSys < 0) ? pickAt(mouse.x, mouse.y, 12) : hoverSys;
    if (hs !== hoverSys) { hoverSys = hs; if (V.onHover) V.onHover(hs); canvas.style.cursor = hs >= 0 ? 'pointer' : ''; }
    const hp = placeMarker(mk.hover, hs >= 0 ? H.pos(hs) : null, hs >= 0 && local && !(st.sel && st.sel.sys === hs));
    if (hp && hs >= 0) {
      mk.tip.style.opacity = 1;
      if (mk.tip._sys !== hs) {
        mk.tip._sys = hs;
        mk.tip.innerHTML = `<b></b><span></span>`;
        mk.tip.querySelector('b').textContent = H.NAME[hs];
        mk.tip.querySelector('span').textContent = H.starKind(H.CLS[hs]) + ' · ' + H.ly(H.dShip(hs)) + (H.jumpsTo(hs) > 0 ? ' · ' + H.jumpsText(hs) : '');
      }
      const tw = mk.tip.offsetWidth;
      let x = hp.x + 14, y = hp.y + 14;
      if (x + tw > W - 10) x = hp.x - tw - 14;
      mk.tip.style.transform = `translate3d(${x.toFixed(0)}px, ${y.toFixed(0)}px, 0)`;
    } else mk.tip.style.opacity = 0;
  }

  // ---------- frame ----------
  let last = performance.now(), fpsAcc = 0, fpsN = 0;
  V.fps = 0;
  const focusU = { amt: 0, goal: 0, dim: 0, dimGoal: 0 };
  V.setFocus = function (sys, radius) {
    if (sys == null) { focusU.goal = 0; return; }
    const p = Array.isArray(sys) ? sys : H.pos(sys);
    localMat.uniforms.uFocus.value.set(p[0], p[1], p[2], radius || 18); focusU.goal = 1;
  };
  V.setDim = function (v) { focusU.dimGoal = v; };
  let dangerBoost = 0;
  V.setDangerBoost = v => { dangerBoost = v; };

  function frame(now) {
    requestAnimationFrame(frame);
    const dt = Math.min(0.05, (now - last) / 1000); last = now;
    fpsAcc += dt; fpsN++; if (fpsAcc > 0.5) { V.fps = Math.round(fpsN / fpsAcc); fpsAcc = 0; fpsN = 0; }
    updateCamera(dt);
    const w = galW(cam.dist);
    // cross-fade
    localAlpha = 1 - smooth(260, 2600, cam.dist);
    const k = 1 - Math.exp(-dt * 5);
    focusU.amt += (focusU.goal - focusU.amt) * k; focusU.dim += (focusU.dimGoal - focusU.dim) * k;
    localMat.uniforms.uFade.value = localAlpha;
    localMat.uniforms.uRef.value = cam.dist;
    localMat.uniforms.uFocusAmt.value = focusU.amt;
    localMat.uniforms.uDim.value = focusU.dim;
    localPts.visible = localAlpha > 0.003;
    const gf = smooth(500, 7000, cam.dist);
    galMat.uniforms.uFade.value = gf;
    galMat.uniforms.uNear.value = 2600 * (1 - gf) + 1;
    galMat.uniforms.uSky.value = 0.55;
    galMat.uniforms.uRef.value = Math.max(20000, cam.dist);
    glowMat.uniforms.uA.value = smooth(2200, 30000, cam.dist) * 1.15;
    const sp = shipPosNow || H.shipPos();
    rings.position.set(sp[0], sp[1], sp[2]);
    rings.children.forEach(l => { l.material.opacity = (l === gcLine ? 0.16 : 0.075) * localAlpha * (1 - smooth(300, 900, cam.dist)) * (V.state.hidden ? 0 : 1); });
    rangeRing.position.set(sp[0], sp[1], sp[2]);
    rangeRing.material.opacity = 0.5 * localAlpha * (1 - smooth(120, 400, cam.dist));
    for (const m of dangerMeshes) m.material.uniforms.uA.value = localAlpha * (0.5 + dangerBoost * 0.7) * (1 - smooth(60, 300, cam.dist) * 0.5);
    if (routeGroup) {
      routeGroup.visible = localAlpha > 0.02;
      routeGroup.children.forEach(o => { if (o.material && o.material.isLineMaterial) o.material.opacity = o.userData.base * localAlpha; });
      routeT = (routeT + dt / Math.max(2.5, V.state.route.jumps * 0.35)) % 1;
      const cp = routeCurve.getPoint(routeT);
      comet.geometry.attributes.position.array.set([cp.x, cp.y, cp.z]); comet.geometry.attributes.position.needsUpdate = true;
      cometMat.uniforms.uA.value = V.cometOff ? 0 : Math.sin(routeT * Math.PI) * localAlpha;
    }
    refreshVP();
    layoutLabels();
    // drop lines from labelled stars to the ship's plane
    const n = Math.min(64, placedDrops.length);
    for (let i = 0; i < n; i++) {
      const p = placedDrops[i];
      dropPos[i * 6] = p[0]; dropPos[i * 6 + 1] = p[1]; dropPos[i * 6 + 2] = p[2];
      dropPos[i * 6 + 3] = p[0]; dropPos[i * 6 + 4] = sp[1]; dropPos[i * 6 + 5] = p[2];
    }
    dropLines.geometry.setDrawRange(0, n * 2); dropLines.geometry.attributes.position.needsUpdate = true;
    dropLines.material.opacity = 0.15 * localAlpha * (1 - smooth(200, 500, cam.dist));
    updateMarkers();
    if (V.onFrame) V.onFrame(dt);
    renderer.render(scene, camera);
  }

  function resize() {
    W = window.innerWidth; Hh = window.innerHeight; DPR = Math.min(window.devicePixelRatio || 1, 2);
    renderer.setPixelRatio(DPR); renderer.setSize(W, Hh, false);
    camera.aspect = W / Hh; camera.updateProjectionMatrix();
    if (localMat) localMat.uniforms.uPx.value = DPR;
    if (galMat) galMat.uniforms.uPx.value = DPR;
    if (stopMat) stopMat.uniforms.uPx.value = DPR;
    if (cometMat) cometMat.uniforms.uPx.value = DPR;
    uiFrame = 0;
  }
  V.refreshUi = () => { uiFrame = 0; candFrame = 0; };

  V.init = function (three, addons) {
    THREE = three; Line2 = addons.Line2; LineMaterial = addons.LineMaterial; LineGeometry = addons.LineGeometry;
    canvas = document.getElementById('gl');
    renderer = new THREE.WebGLRenderer({ canvas, antialias: true, powerPreference: 'high-performance' });
    renderer.setClearColor(0x04060a, 1);
    scene = new THREE.Scene();
    camera = new THREE.PerspectiveCamera(48, 1, 0.05, 1e6);
    W = window.innerWidth; Hh = window.innerHeight;
    resize();
    buildGalaxy(); buildLocal(); buildRefs(); buildNamed(); buildMarkers();
    const sp = H.shipPos();
    Object.assign(cam, { tx: sp[0], ty: sp[1], tz: sp[2] }); Object.assign(goal, cam);
    bindInput();
    window.addEventListener('resize', resize);
    requestAnimationFrame(frame);
  };
})();
