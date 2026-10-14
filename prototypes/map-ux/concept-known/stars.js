/* Hollow Universe map, concept Known: the star field (forked from concept AB's stars.js).
 *
 * The map shows what you know. This file draws the 37,409 local systems in two ways:
 *   1. KNOWN stars (seen, scanned, visited, yours): crisp points, as in concept AB.
 *   2. UNKNOWN stars: never a point. They feed a quiet density haze: large, very faint, soft splats that add up to a
 *      cloud where stars are dense, so unknown space reads as "something is out there" without a single dot or label.
 * When a star becomes known (a drone survey, a visit) its splat fades out and its point fades in (the reveal).
 * It keeps concept AB's interface (draw, highlight, pick, label); picking in the Neighbourhood now goes through the
 * known stars' tiles (tiles.js), but candidatesAt / pick still work for any fork.
 *
 * Interface (as concept AB, plus)
 *   field.setKnowledge(level, since)   level: Uint8Array (0 unknown .. 4 yours), since: Float64Array (performance.now()
 *                                      ms when the star became known in this session; 0 = before). Call after changes.
 *   field.update({ target, dist, opacity, focal, now, haze })   focal = px per ly at depth 1; now = performance.now() ms;
 *                                      haze = the haze layer's opacity (0..1)
 *   field.labelCandidates(extra)       known stars only (plus everything in extra)
 */
(function () {
  'use strict';
  window.HollowStars = { create };

  function create({ THREE, scene, dpr, stars }) {
    const { n, x, y, z, mag, col, cat } = stars;
    const pos = new Float32Array(n * 3), colour = new Float32Array(n * 3), state = new Float32Array(n * 4), know = new Float32Array(n * 2);
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
    const knowAttr = new THREE.BufferAttribute(know, 2); knowAttr.setUsage(THREE.DynamicDrawUsage);
    geo.setAttribute('aKnow', knowAttr);
    const uniforms = {
      uOpacity: { value: 1 }, uPx: { value: dpr }, uTarget: { value: new THREE.Vector3() },
      uFogNear: { value: 100 }, uFogFar: { value: 200 }, uBoost: { value: 0 }, uNow: { value: 0 },
    };
    const material = new THREE.ShaderMaterial({
      uniforms,
      vertexShader: `
        attribute vec3 aColor; attribute float aMag; attribute vec4 aState; attribute vec2 aKnow;
        uniform float uOpacity, uPx, uFogNear, uFogFar, uBoost, uNow; uniform vec3 uTarget;
        varying vec3 vCol; varying float vA; varying float vS;
        void main() {
          vec4 mv = modelViewMatrix * vec4(position, 1.0);
          gl_Position = projectionMatrix * mv;
          float lv = aKnow.x;
          if (lv < 0.5) { vA = 0.0; vS = 0.0; vCol = vec3(0.0); gl_PointSize = 0.0; return; }   // unknown: haze only
          float d = max(length(mv.xyz), 0.05);
          float app = aMag + 5.0 * log(d / 32.6) / log(10.0) - uBoost;      // apparent magnitude seen from the camera
          float s = clamp(9.0 - app * 0.5, 2.6, 13.0);                         // px
          float a = clamp(1.3 - app * 0.07, 0.42, 1.0);
          // known stars are crisp; seen-only stars a little smaller
          if (lv > 1.5) { s = max(s, 5.2); a = max(a, 0.92); } else { s = max(s * 0.9, 4.2); a = max(a, 0.8); }
          float fog = max(1.0 - smoothstep(uFogNear, uFogFar, distance(position, uTarget)), 0.5);
          float k = aKnow.y > 0.0 ? smoothstep(aKnow.y, aKnow.y + 0.7, uNow) : 1.0;            // the reveal
          float flash = aKnow.y > 0.0 && uNow > aKnow.y ? exp(-(uNow - aKnow.y) * 2.2) : 0.0;
          vec3 c = aColor;
          if (aState.w > 1.5) { a *= 0.13; s = max(s * 0.7, 1.6); }                                     // dimmed
          else if (aState.w > 0.5) { c = aState.rgb; s = max(s, 5.0) + 1.5; a = 1.0; fog = max(fog, 0.6); } // highlighted
          s += flash * 7.0;
          vCol = mix(c, vec3(1.0), flash * 0.6); vA = a * fog * uOpacity * k; vS = s * uPx;
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

    // The haze: every unknown star as a soft splat about 4 ly across, so faint that only density shows.
    const hazeUniforms = {
      uOpacity: { value: 1 }, uPx: { value: dpr }, uTarget: uniforms.uTarget, uFogNear: uniforms.uFogNear, uFogFar: uniforms.uFogFar,
      uNow: uniforms.uNow, uFocal: { value: 1000 }, uHaze: { value: 0.026 },
    };
    const hazeMat = new THREE.ShaderMaterial({
      uniforms: hazeUniforms,
      vertexShader: `
        attribute vec3 aColor; attribute vec2 aKnow;
        uniform float uOpacity, uPx, uFogNear, uFogFar, uNow, uFocal, uHaze; uniform vec3 uTarget;
        varying vec3 vCol; varying float vA;
        void main() {
          vec4 mv = modelViewMatrix * vec4(position, 1.0);
          gl_Position = projectionMatrix * mv;
          float k = aKnow.x < 0.5 ? 0.0 : (aKnow.y > 0.0 ? smoothstep(aKnow.y - 0.2, aKnow.y + 0.9, uNow) : 1.0);
          float depth = max(-mv.z, 0.1);
          float px = clamp(4.4 * uFocal / depth, 12.0, 76.0);
          float fog = 1.0 - smoothstep(uFogNear, uFogFar, distance(position, uTarget));
          vCol = mix(aColor, vec3(0.60, 0.68, 0.84), 0.6);
          vA = uHaze * (1.0 - k) * fog * uOpacity;
          gl_PointSize = vA < 0.0005 ? 0.0 : px * uPx;
        }`,
      fragmentShader: `
        varying vec3 vCol; varying float vA;
        void main() {
          vec2 q = gl_PointCoord - 0.5;
          float a = exp(-dot(q, q) * 14.0) * vA;
          if (a < 0.0015) discard;
          gl_FragColor = vec4(vCol, a);
        }`,
      transparent: true, depthWrite: false, depthTest: false, blending: THREE.AdditiveBlending,
    });
    const haze = new THREE.Points(geo, hazeMat);
    haze.frustumCulled = false; haze.renderOrder = 3;
    scene.add(haze);

    const sx = new Float32Array(n), sy = new Float32Array(n), app = new Float32Array(n), visible = new Uint8Array(n);
    let projected = false, appLimit = 9, importance = new Float32Array(n);
    const PICK_R = 24;
    const clamp = (v, a, b) => v < a ? a : v > b ? b : v;

    const field = {
      points, haze, sx, sy, app, visible,
      update({ target, dist, opacity, focal, now, haze: hz }) {
        uniforms.uOpacity.value = opacity;
        points.visible = opacity > 0.003;
        uniforms.uTarget.value.copy(target);
        uniforms.uFogNear.value = Math.max(14, dist * 0.95);
        uniforms.uFogFar.value = Math.max(30, dist * 1.9);
        uniforms.uBoost.value = clamp((Math.log10(dist) - 2.3) * 2.2, 0, 4);
        uniforms.uNow.value = (now || performance.now()) / 1000;
        hazeUniforms.uOpacity.value = opacity * (hz == null ? 1 : hz);
        hazeUniforms.uFocal.value = focal || 1000;
        haze.visible = hazeUniforms.uOpacity.value > 0.003;
        if (opacity <= 0.02) projected = false;
      },
      setKnowledge(level, since) {
        for (let i = 0; i < n; i++) { know[2 * i] = level[i]; know[2 * i + 1] = since[i] > 0 ? since[i] / 1000 : 0; }
        knowAttr.needsUpdate = true;
      },
      known: i => know[2 * i] > 0.5,
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
        for (let i = 0; i < n; i++) {
          const X = x[i], Y = y[i], Z = z[i];
          const cw = e[3] * X + e[7] * Y + e[11] * Z + e[15];
          if (cw <= 1e-6) { visible[i] = 0; continue; }
          const px = ((e[0] * X + e[4] * Y + e[8] * Z + e[12]) / cw * 0.5 + 0.5) * w;
          const py = (0.5 - (e[1] * X + e[5] * Y + e[9] * Z + e[13]) / cw * 0.5) * h;
          if (px < -20 || py < -20 || px > w + 20 || py > h + 20) { visible[i] = 0; continue; }
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
          if (!visible[i] || know[2 * i] < 0.5) continue;
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
          if (bonus === undefined && (know[2 * i] < 0.5 || app[i] > appLimit || mode > 1.5)) continue;   // unknown, too faint or dimmed
          out.push(i, 100 - app[i] + (cat[i] ? 1.2 : 0) + (mode > 0.5 && mode < 1.5 ? 3 : 0) + know[2 * i] * 0.8 + (bonus || 0));
          count++;
        }
        appLimit = clamp(appLimit + (count > 700 ? -0.25 : count < 250 ? 0.25 : 0), 2, 22);
        return out;
      },
    };
    return field;
  }
})();
