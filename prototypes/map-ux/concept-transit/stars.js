/* Hollow Universe map, concept AB: the star field.
 *
 * This file owns four things about the 37,409 local systems:
 *   1. DRAW       how a star looks on screen (size, colour, fog, halo);
 *   2. HIGHLIGHT  how a subset stands out (filters, search results) and the rest dims;
 *   3. PICK       which star is under the cursor;
 *   4. LABEL      which stars deserve a label, with a base priority.
 * Everything else lives in app.js: where labels go on screen (collision-free placement), panels, routes, camera.
 * To try another way of drawing the star field, fork this file and keep the interface; app.js will not notice.
 *
 * Interface
 *   const field = HollowStars.create({ THREE, scene, dpr, stars })
 *     stars: { n, x, y, z: Float32Array (ly, galactic frame), mag: Float32Array (absolute V),
 *              col: Float32Array (r, g, b in 0..1, three per star), cat: Uint8Array (1 = catalogue star) }
 *   field.update({ target, dist, opacity })   every frame before rendering: target = orbit centre (THREE.Vector3),
 *                                             dist = camera distance (ly), opacity = this layer's fade (0..1)
 *   field.setStyle(i, mode, rgb)              mode 0 normal, 1 highlighted (drawn larger in rgb = [r, g, b]), 2 dimmed;
 *   field.commit()                            call once after a batch of setStyle calls
 *   field.styleOf(i)                          the current mode of star i
 *   field.project(camera, w, h)               every frame after the camera moves; fills the read-only arrays below
 *   field.visible[i], field.sx[i], field.sy[i], field.app[i]
 *                                             on screen (1/0), screen position (CSS px), apparent magnitude from the camera
 *   field.labelCandidates(extra)              [i, priority, i, priority, ...] for visible stars worth a label;
 *                                             extra = Map(i -> bonus) of stars the app needs considered (ship, route, results)
 *   field.points                              the THREE.Points object (already added to the scene)
 *
 * Picking (forgiving; reuse it even if you replace the visuals)
 *   field.setImportance(w)                    Float32Array(n), about 0..2: how much each star matters when the cursor is
 *                                             near several (named, stations, bases, landable worlds, near you). App-owned.
 *   field.candidatesAt(x, y, r = 24)          visible stars within r px of the cursor, best first: [{ i, d, score }]
 *                                             (d = px from the cursor; lower score = better: near the cursor, important,
 *                                             highlighted by a filter, bright; dimmed stars rank last)
 *   field.pick(x, y)                          { i, list, crowd, crowded }: i = the best candidate (or -1), list =
 *                                             candidatesAt(), crowd = the likeliest few (within 16 px, at most 6, best
 *                                             first), crowded = two or more of them too close to call. The app then shows
 *                                             a chooser listing `crowd` instead of guessing; Tab / Shift+Tab cycle it.
 */
(function () {
  'use strict';
  window.HollowStars = { create };

  function create({ THREE, scene, dpr, stars }) {
    const { n, x, y, z, mag, col, cat } = stars;
    const pos = new Float32Array(n * 3), colour = new Float32Array(n * 3), state = new Float32Array(n * 4);
    for (let i = 0; i < n; i++) {
      pos[3 * i] = x[i]; pos[3 * i + 1] = y[i]; pos[3 * i + 2] = z[i];
      // star colours are desaturated by about 40 % (concept A's calmer palette)
      const r = col[3 * i], g = col[3 * i + 1], b = col[3 * i + 2], l = 0.299 * r + 0.587 * g + 0.114 * b;
      colour[3 * i] = l + (r - l) * 0.62; colour[3 * i + 1] = l + (g - l) * 0.62; colour[3 * i + 2] = l + (b - l) * 0.62;
    }
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    geo.setAttribute('aColor', new THREE.BufferAttribute(colour, 3));
    geo.setAttribute('aMag', new THREE.BufferAttribute(mag, 1));
    const stateAttr = new THREE.BufferAttribute(state, 4); stateAttr.setUsage(THREE.DynamicDrawUsage);
    geo.setAttribute('aState', stateAttr);
    const uniforms = {
      uOpacity: { value: 1 }, uPx: { value: dpr }, uTarget: { value: new THREE.Vector3() },
      uFogNear: { value: 100 }, uFogFar: { value: 200 }, uBoost: { value: 0 },
    };
    const material = new THREE.ShaderMaterial({
      uniforms,
      vertexShader: `
        attribute vec3 aColor; attribute float aMag; attribute vec4 aState;
        uniform float uOpacity, uPx, uFogNear, uFogFar, uBoost; uniform vec3 uTarget;
        varying vec3 vCol; varying float vA; varying float vS;
        void main() {
          vec4 mv = modelViewMatrix * vec4(position, 1.0);
          gl_Position = projectionMatrix * mv;
          float d = max(length(mv.xyz), 0.05);
          float app = aMag + 5.0 * log(d / 32.6) / log(10.0) - uBoost;      // apparent magnitude seen from the camera
          float s = clamp(9.0 - app * 0.5, 2.6, 13.0);                         // px
          float a = clamp(1.3 - app * 0.07, 0.42, 1.0);
          float fog = 1.0 - smoothstep(uFogNear, uFogFar, distance(position, uTarget));
          vec3 c = aColor;
          if (aState.w > 1.5) { a *= 0.13; s = max(s * 0.7, 1.6); }                                     // dimmed
          else if (aState.w > 0.5) { c = aState.rgb; s = max(s, 5.0) + 1.5; a = 1.0; fog = max(fog, 0.6); } // highlighted
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
          gl_FragColor = vec4(mix(vCol, vec3(1.0), core * 0.3), a);
        }`,
      transparent: true, depthWrite: false, depthTest: false, blending: THREE.AdditiveBlending,
    });
    const points = new THREE.Points(geo, material);
    points.frustumCulled = false; points.renderOrder = 4;
    scene.add(points);

    const sx = new Float32Array(n), sy = new Float32Array(n), app = new Float32Array(n), visible = new Uint8Array(n);
    let projected = false, appLimit = 9, importance = new Float32Array(n);
    const PICK_R = 24;
    const clamp = (v, a, b) => v < a ? a : v > b ? b : v;

    const field = {
      points, sx, sy, app, visible,
      update({ target, dist, opacity }) {
        uniforms.uOpacity.value = opacity;
        points.visible = opacity > 0.003;
        uniforms.uTarget.value.copy(target);
        uniforms.uFogNear.value = Math.max(14, dist * 0.95);
        uniforms.uFogFar.value = Math.max(30, dist * 1.9);
        uniforms.uBoost.value = clamp((Math.log10(dist) - 2.3) * 2.2, 0, 4);
        if (opacity <= 0.02) projected = false;
      },
      setStyle(i, mode, rgb) {
        state[4 * i] = rgb ? rgb[0] : 0; state[4 * i + 1] = rgb ? rgb[1] : 0; state[4 * i + 2] = rgb ? rgb[2] : 0; state[4 * i + 3] = mode;
      },
      styleOf(i) { return state[4 * i + 3]; },
      commit() { stateAttr.needsUpdate = true; },
      project(camera, w, h) {
        if (!points.visible || uniforms.uOpacity.value <= 0.02) { projected = false; return; }
        projected = true;
        const m = new THREE.Matrix4().multiplyMatrices(camera.projectionMatrix, camera.matrixWorldInverse), e = m.elements;
        const cx = camera.position.x, cy = camera.position.y, cz = camera.position.z;
        const t = uniforms.uTarget.value, fogFar = uniforms.uFogFar.value;
        for (let i = 0; i < n; i++) {
          const X = x[i], Y = y[i], Z = z[i];
          const cw = e[3] * X + e[7] * Y + e[11] * Z + e[15];
          if (cw <= 1e-6) { visible[i] = 0; continue; }
          const px = ((e[0] * X + e[4] * Y + e[8] * Z + e[12]) / cw * 0.5 + 0.5) * w;
          const py = (0.5 - (e[1] * X + e[5] * Y + e[9] * Z + e[13]) / cw * 0.5) * h;
          if (px < -20 || py < -20 || px > w + 20 || py > h + 20) { visible[i] = 0; continue; }
          if (Math.hypot(X - t.x, Y - t.y, Z - t.z) > fogFar) { visible[i] = 0; continue; }
          sx[i] = px; sy[i] = py; visible[i] = 1;
          app[i] = mag[i] + 5 * Math.log10(Math.max(0.05, Math.hypot(X - cx, Y - cy, Z - cz)) / 32.6);
        }
      },
      isProjected() { return projected; },
      setImportance(w) { importance = w; },
      candidatesAt(px, py, r = PICK_R) {
        if (!projected) return [];
        const out = [];
        for (let i = 0; i < n; i++) {
          if (!visible[i]) continue;
          const dx = sx[i] - px, dy = sy[i] - py;
          if (dx > r || dx < -r || dy > r || dy < -r) continue;
          const d = Math.hypot(dx, dy); if (d > r) continue;
          const mode = state[4 * i + 3];
          const score = d / r - importance[i] * 0.45 - (mode > 0.5 && mode < 1.5 ? 0.5 : 0) + (mode > 1.5 ? 0.9 : 0) - clamp((8 - app[i]) * 0.025, -0.15, 0.25);
          out.push({ i, d, score });
        }
        out.sort((a, b) => a.score - b.score);
        return out.slice(0, 12);
      },
      pick(px, py) {
        const list = field.candidatesAt(px, py);
        if (!list.length) return { i: -1, list, crowd: [], crowded: false };
        const crowd = [list[0]].concat(list.slice(1).filter(c => c.d <= 16)).slice(0, 6);
        const a = crowd[0], b = crowd[1];
        // too close to call: a second candidate almost as good and almost as near, unless the cursor sits right on the first
        const crowded = !!b && b.score - a.score < 0.2 && !(a.d < 3 && b.d > 6);
        return { i: a.i, list, crowd, crowded };
      },
      labelCandidates(extra) {
        if (!projected) return [];
        const out = [];
        let count = 0;
        for (let i = 0; i < n; i++) {
          if (!visible[i]) continue;
          const bonus = extra.get(i);
          const mode = state[4 * i + 3];
          if (bonus === undefined && (app[i] > appLimit || mode > 1.5)) continue;   // too faint, or dimmed by a filter
          out.push(i, 100 - app[i] + (cat[i] ? 1.2 : 0) + (mode > 0.5 && mode < 1.5 ? 3 : 0) + (bonus || 0));
          count++;
        }
        // keep the candidate list near 500-1,400 stars whatever the zoom
        appLimit = clamp(appLimit + (count > 1400 ? -0.25 : count < 500 ? 0.25 : 0), 2, 22);
        return out;
      },
    };
    return field;
  }
})();
