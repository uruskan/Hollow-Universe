/* Hollow Universe map, concept Floors: the star field, sliced into floors.
 *
 * The neighbourhood is cut into horizontal floors: slabs parallel to the galactic plane, h ly thick, with floor 0
 * centred on y0 (the home base). Every star is drawn on the mid-plane of its own floor, so each floor is a flat plan.
 *   - The ACTIVE floor's stars are crisp dots with a dark rim, sized by brightness. Only they are pickable and labelled.
 *   - Stars on the floors around it are GHOSTS: small ▲ (above) or ▼ (below) glyphs, faint, gone beyond two floors.
 *   - The floor position is continuous, so a floor change animates: dots cross-fade into ghosts and back while the
 *     camera rides between the planes like a lift.
 *
 * Interface (concept AB's, plus floors; app.js relies on all of it)
 *   const field = HollowStars.create({ THREE, scene, dpr, stars })
 *     stars: { n, x, y, z: Float32Array (ly, galactic frame), mag: Float32Array (absolute V),
 *              col: Float32Array (r, g, b in 0..1, three per star), cat: Uint8Array (1 = catalogue star) }
 *   field.setFloors({ y0, h })                slice the stars: fills field.floorOf[i] (Int16), field.dispY[i] (the height
 *                                             it is drawn at, ly), field.minFloor, field.maxFloor, field.floorCount(k)
 *   field.update({ target, dist, opacity, floor, goal, ghosts })
 *                                             every frame: target = orbit centre, dist = camera distance (ly), opacity =
 *                                             layer fade, floor = animated floor position (float), goal = the active floor
 *                                             (int; picking and labels use it), ghosts = 0..1
 *   field.setStyle(i, mode, rgb); field.commit(); field.styleOf(i)
 *                                             mode 0 normal, 1 highlighted in rgb, 2 dimmed (ghosts follow the same modes)
 *   field.project(camera, w, h)               fills visible[i] (on the active floor and on screen), shown[i] (on screen,
 *                                             any floor), sx[i], sy[i] (CSS px) and app[i] (apparent magnitude)
 *   field.labelCandidates(extra)              [i, priority, ...]: active-floor stars worth a label, plus stars of other
 *                                             floors that extra = Map(i -> bonus) insists on (bonus 400+: you, route, ...)
 *   field.setImportance(w), field.candidatesAt(x, y, r = 24), field.pick(x, y)
 *                                             forgiving picking, as in concept AB, over the active floor only
 */
(function () {
  'use strict';
  window.HollowStars = { create };

  // shared vertex code: where the star is drawn, how bright, and how far its floor is from the floor being shown
  const VS_HEAD = `
    attribute vec3 aColor; attribute float aMag; attribute vec4 aState; attribute float aFloor;
    uniform float uOpacity, uPx, uFogNear, uFogFar, uBoost, uFloor, uGhosts; uniform vec3 uTarget;
    varying vec3 vCol; varying float vA; varying float vS; varying float vUp;
    float appMag(vec4 mv) { float d = max(length(mv.xyz), 0.05); return aMag + 5.0 * log(d / 32.6) / log(10.0) - uBoost; }
    float fogOf() { return 1.0 - smoothstep(uFogNear, uFogFar, length(position.xz - uTarget.xz)); }`;

  const DOT_VS = VS_HEAD + `
    void main() {
      vec4 mv = modelViewMatrix * vec4(position, 1.0);
      gl_Position = projectionMatrix * mv;
      float df = abs(aFloor - uFloor);
      float on = 1.0 - smoothstep(0.0, 0.8, df);                         // 1 on the floor being shown
      if (on < 0.002) { vA = 0.0; vS = 0.0; vCol = vec3(0.0); vUp = 0.0; gl_PointSize = 0.0; return; }
      float app = appMag(mv);
      float s = clamp(10.0 - app * 0.5, 4.2, 14.0);                       // px
      float a = clamp(1.35 - app * 0.07, 0.62, 1.0);
      vec3 c = aColor;
      if (aState.w > 1.5) { a *= 0.2; s = max(s * 0.72, 3.0); }                                   // dimmed by a filter
      else if (aState.w > 0.5) { c = aState.rgb; s = max(s, 7.0) + 1.5; a = 1.0; }               // highlighted
      vCol = c; vA = a * on * fogOf() * uOpacity; vS = s * uPx; vUp = 0.0;
      gl_PointSize = vS;
    }`;
  // a crisp disc with a thin dark rim (reads on any fill) and a small glow; premultiplied alpha
  const DOT_FS = `
    varying vec3 vCol; varying float vA; varying float vS; varying float vUp;
    void main() {
      if (vA < 0.004) discard;
      float r = length(gl_PointCoord - 0.5) * 2.0;
      float aa = 2.0 / max(vS, 1.0);
      float core = 1.0 - smoothstep(0.46 - aa, 0.46 + aa, r);
      float rim = 1.0 - smoothstep(0.66 - aa, 0.66 + aa, r);
      float glow = exp(-r * r * 4.5) * 0.3 * (1.0 - core);
      vec3 c = mix(vCol, vec3(1.0), 0.28) * core + vCol * glow;
      float a = max(core, rim * 0.6);
      if (a + glow < 0.01) discard;
      gl_FragColor = vec4(c, a) * vA;
    }`;

  const GHOST_VS = VS_HEAD + `
    void main() {
      vec4 mv = modelViewMatrix * vec4(position, 1.0);
      gl_Position = projectionMatrix * mv;
      float sd = aFloor - uFloor, df = abs(sd);
      // fades in as the star leaves the floor being shown; one floor away it is clear, two floors away faint, then gone
      float g = smoothstep(0.2, 0.95, df) * mix(1.0, 0.42, smoothstep(1.2, 1.8, df)) * (1.0 - smoothstep(2.25, 2.85, df));
      bool hi = aState.w > 0.5 && aState.w < 1.5;
      float a = hi ? 0.78 : 0.3 * uGhosts;
      if (aState.w > 1.5) a *= 0.22;
      a *= g;
      if (a < 0.003) { vA = 0.0; vS = 0.0; vCol = vec3(0.0); vUp = 0.0; gl_PointSize = 0.0; return; }
      float s = (hi ? 9.0 : mix(7.5, 6.0, smoothstep(1.2, 1.8, df)));
      vCol = hi ? aState.rgb : mix(aColor, vec3(0.72, 0.78, 0.9), 0.55);
      vA = a * fogOf() * uOpacity; vS = s * uPx; vUp = sd > 0.0 ? 1.0 : 0.0;
      gl_PointSize = vS;
    }`;
  // ▲ for a star above the floor being shown, ▼ for one below (screen-aligned, like a lift's buttons)
  const GHOST_FS = `
    varying vec3 vCol; varying float vA; varying float vS; varying float vUp;
    float tri(vec2 p, float r) {
      const float k = 1.7320508;
      p.x = abs(p.x) - r; p.y = p.y + r / k;
      if (p.x + k * p.y > 0.0) p = vec2(p.x - k * p.y, -k * p.x - p.y) / 2.0;
      p.x -= clamp(p.x, -2.0 * r, 0.0);
      return -length(p) * sign(p.y);
    }
    void main() {
      if (vA < 0.003) discard;
      vec2 p = (gl_PointCoord - 0.5) * 2.0; p.y = -p.y;
      if (vUp < 0.5) p.y = -p.y;
      float d = tri(p + vec2(0.0, 0.2), 0.78);
      float aa = 2.6 / max(vS, 1.0);
      float a = 1.0 - smoothstep(-aa, aa, d);
      if (a < 0.01) discard;
      gl_FragColor = vec4(vCol, a * vA);
    }`;

  function create({ THREE, scene, dpr, stars }) {
    const { n, x, y, z, mag, col, cat } = stars;
    const pos = new Float32Array(n * 3), colour = new Float32Array(n * 3), state = new Float32Array(n * 4);
    const floorF = new Float32Array(n), floorOf = new Int16Array(n), dispY = new Float32Array(n);
    for (let i = 0; i < n; i++) {
      pos[3 * i] = x[i]; pos[3 * i + 1] = y[i]; pos[3 * i + 2] = z[i];
      // star colours are desaturated by about 40 % (concept A's calmer palette)
      const r = col[3 * i], g = col[3 * i + 1], b = col[3 * i + 2], l = 0.299 * r + 0.587 * g + 0.114 * b;
      colour[3 * i] = l + (r - l) * 0.62; colour[3 * i + 1] = l + (g - l) * 0.62; colour[3 * i + 2] = l + (b - l) * 0.62;
    }
    const geo = new THREE.BufferGeometry();
    const posAttr = new THREE.BufferAttribute(pos, 3);
    geo.setAttribute('position', posAttr);
    geo.setAttribute('aColor', new THREE.BufferAttribute(colour, 3));
    geo.setAttribute('aMag', new THREE.BufferAttribute(mag, 1));
    const floorAttr = new THREE.BufferAttribute(floorF, 1);
    geo.setAttribute('aFloor', floorAttr);
    const stateAttr = new THREE.BufferAttribute(state, 4); stateAttr.setUsage(THREE.DynamicDrawUsage);
    geo.setAttribute('aState', stateAttr);
    const uniforms = {
      uOpacity: { value: 1 }, uPx: { value: dpr }, uTarget: { value: new THREE.Vector3() },
      uFogNear: { value: 100 }, uFogFar: { value: 200 }, uBoost: { value: 0 }, uFloor: { value: 0 }, uGhosts: { value: 1 },
    };
    const ghostMat = new THREE.ShaderMaterial({ uniforms, vertexShader: GHOST_VS, fragmentShader: GHOST_FS,
      transparent: true, depthWrite: false, depthTest: false, blending: THREE.AdditiveBlending });
    const dotMat = new THREE.ShaderMaterial({ uniforms, vertexShader: DOT_VS, fragmentShader: DOT_FS,
      transparent: true, depthWrite: false, depthTest: false, blending: THREE.NormalBlending, premultipliedAlpha: true });
    const ghosts = new THREE.Points(geo, ghostMat), points = new THREE.Points(geo, dotMat);
    ghosts.frustumCulled = points.frustumCulled = false; ghosts.renderOrder = 3; points.renderOrder = 4;
    scene.add(ghosts); scene.add(points);

    const sx = new Float32Array(n), sy = new Float32Array(n), app = new Float32Array(n);
    const visible = new Uint8Array(n), shown = new Uint8Array(n);
    let projected = false, appLimit = 9, importance = new Float32Array(n), goal = 0, counts = new Int32Array(1), minFloor = 0;
    const PICK_R = 24;
    const clamp = (v, a, b) => v < a ? a : v > b ? b : v;

    const field = {
      points, ghosts, sx, sy, app, visible, shown, floorOf, dispY, minFloor: 0, maxFloor: 0, y0: 0, h: 10,
      setFloors({ y0, h }) {
        let lo = 1e9, hi = -1e9;
        for (let i = 0; i < n; i++) {
          const k = Math.floor((y[i] - y0) / h + 0.5);
          floorOf[i] = k; floorF[i] = k; dispY[i] = y0 + k * h; pos[3 * i + 1] = dispY[i];
          if (k < lo) lo = k; if (k > hi) hi = k;
        }
        minFloor = lo; counts = new Int32Array(hi - lo + 1);
        for (let i = 0; i < n; i++) counts[floorOf[i] - lo]++;
        Object.assign(field, { minFloor: lo, maxFloor: hi, y0, h });
        posAttr.needsUpdate = true; floorAttr.needsUpdate = true;
      },
      floorCount(k) { const j = k - minFloor; return j >= 0 && j < counts.length ? counts[j] : 0; },
      update({ target, dist, opacity, floor, goal: g, ghosts: gh }) {
        uniforms.uOpacity.value = opacity;
        points.visible = ghosts.visible = opacity > 0.003;
        uniforms.uTarget.value.copy(target);
        uniforms.uFogNear.value = Math.max(16, dist * 0.95);
        uniforms.uFogFar.value = Math.max(34, dist * 1.9);
        uniforms.uBoost.value = clamp((Math.log10(dist) - 2.3) * 2.2, 0, 4);
        uniforms.uFloor.value = floor; uniforms.uGhosts.value = gh;
        goal = g;
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
          visible[i] = 0; shown[i] = 0;
          const X = x[i], Y = dispY[i], Z = z[i];
          if (Math.hypot(X - t.x, Z - t.z) > fogFar) continue;
          const cw = e[3] * X + e[7] * Y + e[11] * Z + e[15];
          if (cw <= 1e-6) continue;
          const px = ((e[0] * X + e[4] * Y + e[8] * Z + e[12]) / cw * 0.5 + 0.5) * w;
          const py = (0.5 - (e[1] * X + e[5] * Y + e[9] * Z + e[13]) / cw * 0.5) * h;
          if (px < -20 || py < -20 || px > w + 20 || py > h + 20) continue;
          sx[i] = px; sy[i] = py; shown[i] = 1; visible[i] = floorOf[i] === goal ? 1 : 0;
          app[i] = mag[i] + 5 * Math.log10(Math.max(0.05, Math.hypot(X - cx, Y - cy, Z - cz)) / 32.6);
        }
      },
      isProjected() { return projected; },
      setImportance(wt) { importance = wt; },
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
          if (!shown[i]) continue;
          const bonus = extra.get(i);
          if (!visible[i]) { if (bonus !== undefined && bonus >= 400) out.push(i, 100 - app[i] + bonus * 0.9); continue; }   // another floor: only what the app insists on (bonus 400+)
          const mode = state[4 * i + 3];
          if (bonus === undefined && (app[i] > appLimit || mode > 1.5)) continue;   // too faint, or dimmed by a filter
          out.push(i, 100 - app[i] + (cat[i] ? 1.2 : 0) + (mode > 0.5 && mode < 1.5 ? 3 : 0) + (bonus || 0));
          count++;
        }
        // keep the candidate list near 300-900 stars whatever the zoom (one floor holds far fewer than the whole cloud)
        appLimit = clamp(appLimit + (count > 900 ? -0.25 : count < 300 ? 0.25 : 0), 2, 22);
        return out;
      },
    };
    return field;
  }
})();
