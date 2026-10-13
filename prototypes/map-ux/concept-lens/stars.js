/* Hollow Universe map, concept Lens: the star field.
 *
 * Focus plus context. Outside the lens the 37,409 local systems are not drawn one by one: they become a calm,
 * low-contrast density glow (where stars are thick or thin), plus a few key places the app marks (your space,
 * stations, bases, the selection, filter matches). Inside the lens (lens.js) every star is drawn, spread apart,
 * with its height stalk and owner. This file owns the data side of both:
 *   1. GLOW      every star splatted into a small float target, then drawn full screen as a soft haze;
 *   2. KEYS      the few stars drawn crisply outside the lens;
 *   3. LENS      which stars sit under the lens glass and where the lens shows them (mapped positions);
 *   4. PICK      the star under the cursor: inside the lens among the spread-apart stars, outside among key places;
 *   5. LABEL     which key stars deserve a name outside the lens (app.js names the stars inside it).
 *
 * Interface (concept AB's, unchanged, plus the lens additions marked +)
 *   const field = HollowStars.create({ THREE, scene, dpr, stars })
 *   field.update({ target, dist, opacity })   every frame before rendering
 *   field.setStyle(i, mode, rgb); field.commit()   mode 0 normal, 1 highlighted in rgb, 2 dimmed
 *   field.project(camera, w, h)               every frame; fills visible / sx / sy / app (+ dep, fog, lens arrays)
 *   field.labelCandidates(extra)              [i, priority, ...] for key stars OUTSIDE the lens (extra = Map(i -> bonus))
 *   field.setImportance(w); field.candidatesAt(x, y, r); field.pick(x, y)   as in concept AB, lens-aware
 * + field.renderGlow(renderer, camera)        before the main render: splats every star into the glow target
 * + field.setKey(i, rgb, kind); field.commitKeys()   kind 0 none, 1 dot, 2 diamond (station), 3 ring (your space), 4 big dot
 * + field.setLens(lens)                       lens = { vis, cx, cy, r, map(x, y, out) } (lens.js)
 * + field.lensList, field.lensCount           stars under the glass this frame (indices), and how many
 * + field.lx[i], field.ly[i], field.lr[i]     where the lens shows star i (CSS px) and its radius there (0..1 of the lens)
 * + field.inLens[i]                           1 if star i is under the glass (drawn by the lens, not outside)
 */
(function () {
  'use strict';
  window.HollowStars = { create };

  function create({ THREE, scene, dpr, stars }) {
    const { n, x, y, z, mag, col, cat } = stars;
    const pos = new Float32Array(n * 3), colour = new Float32Array(n * 3), state = new Float32Array(n * 4), keyA = new Float32Array(n * 4);
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
    const keyAttr = new THREE.BufferAttribute(keyA, 4); keyAttr.setUsage(THREE.DynamicDrawUsage);
    geo.setAttribute('aKey', keyAttr);

    // ---------- 1. Glow: a screen-space density estimate, one soft splat per star ----------
    const GLOW_DIV = 6;                       // the glow target is a sixth of the screen in CSS px
    const rt = new THREE.WebGLRenderTarget(4, 4, { type: THREE.HalfFloatType, format: THREE.RGBAFormat, depthBuffer: false, stencilBuffer: false });
    rt.texture.minFilter = rt.texture.magFilter = THREE.LinearFilter; rt.texture.generateMipmaps = false;
    const glowScene = new THREE.Scene();
    const splatMat = new THREE.ShaderMaterial({
      uniforms: { uSplat: { value: 16 }, uTarget: { value: new THREE.Vector3() }, uFogNear: { value: 100 }, uFogFar: { value: 200 } },
      vertexShader: `
        attribute vec3 aColor; attribute vec4 aState;
        uniform float uSplat, uFogNear, uFogFar; uniform vec3 uTarget;
        varying vec3 vCol; varying float vW;
        void main() {
          gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
          float fog = 1.0 - smoothstep(uFogNear, uFogFar, distance(position, uTarget));
          vW = fog * (aState.w > 1.5 ? 0.4 : 1.0);
          vCol = aColor;
          gl_PointSize = uSplat;
        }`,
      fragmentShader: `
        varying vec3 vCol; varying float vW;
        void main() {
          vec2 q = gl_PointCoord - 0.5;
          float g = exp(-dot(q, q) * 13.0) * vW;
          if (g < 0.003) discard;
          gl_FragColor = vec4(vCol * g, g);
        }`,
      transparent: true, depthTest: false, depthWrite: false,
      blending: THREE.CustomBlending, blendEquation: THREE.AddEquation, blendSrc: THREE.OneFactor, blendDst: THREE.OneFactor,
      blendSrcAlpha: THREE.OneFactor, blendDstAlpha: THREE.OneFactor,
    });
    const splats = new THREE.Points(geo, splatMat); splats.frustumCulled = false; glowScene.add(splats);
    const hazeMat = new THREE.ShaderMaterial({
      uniforms: { uTex: { value: rt.texture }, uTexel: { value: new THREE.Vector2(0.01, 0.01) }, uOpacity: { value: 0 }, uGain: { value: 1 } },
      vertexShader: 'varying vec2 vUv; void main(){ vUv = uv; gl_Position = vec4(position.xy, 0.0, 1.0); }',
      fragmentShader: `
        uniform sampler2D uTex; uniform vec2 uTexel; uniform float uOpacity, uGain; varying vec2 vUv;
        vec4 tap(vec2 o) { return texture2D(uTex, vUv + o * uTexel); }
        void main() {
          // a 3x3 tent over the (bilinear) target smooths the splats into one haze
          vec4 s = tap(vec2(0.0)) * 0.25
                 + (tap(vec2(1.5, 0.0)) + tap(vec2(-1.5, 0.0)) + tap(vec2(0.0, 1.5)) + tap(vec2(0.0, -1.5))) * 0.125
                 + (tap(vec2(1.5, 1.5)) + tap(vec2(-1.5, 1.5)) + tap(vec2(1.5, -1.5)) + tap(vec2(-1.5, -1.5))) * 0.0625;
          float d = s.a;
          if (d < 1e-4) discard;
          vec3 tint = s.rgb / d;                                         // the average star colour here
          float v = 1.0 - exp(-d * uGain);                               // soft saturation: thick never blows out
          vec3 base = mix(vec3(0.20, 0.27, 0.42), vec3(0.70, 0.76, 0.88), v);
          vec3 c = mix(base, tint, 0.2) * pow(v, 1.7) * 0.3;
          gl_FragColor = vec4(c * uOpacity, 1.0);
        }`,
      transparent: true, depthTest: false, depthWrite: false, blending: THREE.AdditiveBlending,
    });
    const haze = new THREE.Mesh(new THREE.PlaneGeometry(2, 2), hazeMat);
    haze.frustumCulled = false; haze.renderOrder = 1.5; scene.add(haze);

    // ---------- 2. Keys: the few stars drawn crisply outside the lens ----------
    const uniforms = { uOpacity: { value: 1 }, uPx: { value: dpr }, uTarget: { value: new THREE.Vector3() }, uFogFar: { value: 200 } };
    const keyMat = new THREE.ShaderMaterial({
      uniforms,
      vertexShader: `
        attribute vec4 aState; attribute vec4 aKey;
        uniform float uOpacity, uPx, uFogFar; uniform vec3 uTarget;
        varying vec3 vCol; varying float vA; varying float vS; varying float vKind;
        void main() {
          float kind = aKey.w; vec3 c = aKey.rgb;
          if (kind < 0.5 && aState.w > 0.5 && aState.w < 1.5) { kind = 1.0; c = aState.rgb; }   // a filter or search match
          if (kind < 0.5) { gl_Position = vec4(2.0, 2.0, 2.0, 1.0); gl_PointSize = 0.0; vA = 0.0; vS = 1.0; vKind = 0.0; vCol = c; return; }
          gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
          float far = smoothstep(uFogFar * 0.6, uFogFar * 1.1, distance(position, uTarget));
          float s = kind > 3.5 ? 8.0 : kind > 2.5 ? 13.0 : kind > 1.5 ? 11.0 : 6.5;
          vCol = c; vKind = kind; vA = uOpacity * (1.0 - far * 0.55);
          vS = s * uPx; gl_PointSize = vS;
        }`,
      fragmentShader: `
        varying vec3 vCol; varying float vA; varying float vS; varying float vKind;
        void main() {
          vec2 q = (gl_PointCoord - 0.5) * 2.0;
          float r = length(q), aa = 2.4 / max(vS, 1.0), a;
          if (vKind > 3.5 || vKind < 1.5) {                                 // dot
            a = 1.0 - smoothstep(0.55 - aa, 0.55 + aa, r);
            a += exp(-r * r * 5.0) * 0.35;
          } else if (vKind < 2.5) {                                         // diamond outline and a centre dot
            float dm = abs(q.x) + abs(q.y);
            a = (1.0 - smoothstep(0.13 - aa, 0.13 + aa, abs(dm - 0.72))) + (1.0 - smoothstep(0.24 - aa, 0.24 + aa, r));
          } else {                                                          // ring and a centre dot
            a = (1.0 - smoothstep(0.1 - aa, 0.1 + aa, abs(r - 0.74))) + (1.0 - smoothstep(0.3 - aa, 0.3 + aa, r));
          }
          a = min(a, 1.0) * vA;
          if (a < 0.01) discard;
          gl_FragColor = vec4(mix(vCol, vec3(1.0), 0.18), a);
        }`,
      transparent: true, depthWrite: false, depthTest: false,
    });
    const points = new THREE.Points(geo, keyMat);
    points.frustumCulled = false; points.renderOrder = 4;
    scene.add(points);

    // ---------- 3-5. Projection, lens, picking, labels ----------
    const sx = new Float32Array(n), sy = new Float32Array(n), app = new Float32Array(n), visible = new Uint8Array(n);
    const dep = new Float32Array(n), fog = new Float32Array(n);
    const lx = new Float32Array(n), ly = new Float32Array(n), lr = new Float32Array(n), inLens = new Uint8Array(n);
    const lensList = new Int32Array(n);
    let lensCount = 0, lensWeight = 0;
    let projected = false, importance = new Float32Array(n), lens = null, keyList = [], keysDirty = true;
    const keyKind = new Uint8Array(n);
    const PICK_R = 24, LENS_PICK_R = 30;
    const clamp = (v, a, b) => v < a ? a : v > b ? b : v;
    let fogNear = 100, fogFar = 200, glowW = 0, glowH = 0;
    const mp = { x: 0, y: 0 }, tmpSize = new THREE.Vector2(), tmpCol = new THREE.Color();

    const field = {
      points, sx, sy, app, visible, dep, fog, lx, ly, lr, inLens, colour, x, y, z,
      get lensList() { return lensList; }, get lensCount() { return lensCount; }, get lensWeight() { return lensWeight; },
      update({ target, dist, opacity }) {
        uniforms.uOpacity.value = opacity;
        points.visible = opacity > 0.003;
        uniforms.uTarget.value.copy(target); splatMat.uniforms.uTarget.value.copy(target);
        fogNear = Math.max(14, dist * 0.95); fogFar = Math.max(30, dist * 1.9);
        splatMat.uniforms.uFogNear.value = fogNear; splatMat.uniforms.uFogFar.value = fogFar; uniforms.uFogFar.value = fogFar;
        // zoomed out, the same stars crowd fewer pixels: keep the haze about level with zoom
        hazeMat.uniforms.uGain.value = 0.06 / Math.pow(clamp(dist / 100, 0.15, 4), 1.1);
        hazeMat.uniforms.uOpacity.value = opacity;
        haze.visible = opacity > 0.003;
        if (opacity <= 0.02) projected = false;
      },
      renderGlow(renderer, camera) {
        if (!haze.visible) return;
        renderer.getSize(tmpSize);
        const w = Math.max(16, Math.ceil(tmpSize.x / GLOW_DIV)), h = Math.max(16, Math.ceil(tmpSize.y / GLOW_DIV));
        if (w !== glowW || h !== glowH) { rt.setSize(w, h); glowW = w; glowH = h; hazeMat.uniforms.uTexel.value.set(1 / w, 1 / h); }
        const prevT = renderer.getRenderTarget(); renderer.getClearColor(tmpCol); const ca = renderer.getClearAlpha();
        renderer.setRenderTarget(rt); renderer.setClearColor(0x000000, 0); renderer.clear(true, false, false);
        renderer.render(glowScene, camera);
        renderer.setRenderTarget(prevT); renderer.setClearColor(tmpCol, ca);
      },
      setStyle(i, mode, rgb) {
        state[4 * i] = rgb ? rgb[0] : 0; state[4 * i + 1] = rgb ? rgb[1] : 0; state[4 * i + 2] = rgb ? rgb[2] : 0; state[4 * i + 3] = mode;
      },
      styleOf(i) { return state[4 * i + 3]; },
      styleRGB(i) { return [state[4 * i], state[4 * i + 1], state[4 * i + 2]]; },
      commit() { stateAttr.needsUpdate = true; keysDirty = true; },
      setKey(i, rgb, kind) {
        keyA[4 * i] = rgb ? rgb[0] : 0; keyA[4 * i + 1] = rgb ? rgb[1] : 0; keyA[4 * i + 2] = rgb ? rgb[2] : 0; keyA[4 * i + 3] = kind || 0;
        keyKind[i] = kind || 0;
      },
      keyOf(i) { return keyKind[i]; },
      clearKeys() { keyA.fill(0); keyKind.fill(0); },
      commitKeys() { keyAttr.needsUpdate = true; keysDirty = true; },
      setLens(l) { lens = l; },
      project(camera, w, h) {
        if (!points.visible || uniforms.uOpacity.value <= 0.02) { projected = false; lensCount = 0; return; }
        projected = true;
        if (keysDirty) { keysDirty = false; keyList = []; for (let i = 0; i < n; i++) if (keyKind[i] || (state[4 * i + 3] > 0.5 && state[4 * i + 3] < 1.5)) keyList.push(i); }
        const m = new THREE.Matrix4().multiplyMatrices(camera.projectionMatrix, camera.matrixWorldInverse), e = m.elements;
        const cx = camera.position.x, cy = camera.position.y, cz = camera.position.z;
        const t = uniforms.uTarget.value;
        const L = lens && lens.vis ? lens : null, Lx = L ? L.cx : 0, Ly = L ? L.cy : 0, Lr = L ? L.r : 0, Lr2 = Lr * Lr;
        lensCount = 0; lensWeight = 0;
        for (let i = 0; i < n; i++) {
          inLens[i] = 0;
          const X = x[i], Y = y[i], Z = z[i];
          const cw = e[3] * X + e[7] * Y + e[11] * Z + e[15];
          if (cw <= 1e-6) { visible[i] = 0; continue; }
          const px = ((e[0] * X + e[4] * Y + e[8] * Z + e[12]) / cw * 0.5 + 0.5) * w;
          const py = (0.5 - (e[1] * X + e[5] * Y + e[9] * Z + e[13]) / cw * 0.5) * h;
          if (px < -20 || py < -20 || px > w + 20 || py > h + 20) { visible[i] = 0; continue; }
          const dt = Math.hypot(X - t.x, Y - t.y, Z - t.z);
          if (dt > fogFar) { visible[i] = 0; continue; }
          sx[i] = px; sy[i] = py; visible[i] = 1; dep[i] = cw;
          const fa = dt <= fogNear ? 1 : 1 - (dt - fogNear) / (fogFar - fogNear);
          fog[i] = fa * fa * (3 - 2 * fa);
          app[i] = mag[i] + 5 * Math.log10(Math.max(0.05, Math.hypot(X - cx, Y - cy, Z - cz)) / 32.6);
          if (L) {
            const dx = px - Lx, dy = py - Ly;
            if (dx * dx + dy * dy < Lr2) {
              inLens[i] = 1; lensList[lensCount++] = i; lensWeight += fog[i];
              L.map(px, py, mp); lx[i] = mp.x; ly[i] = mp.y; lr[i] = Math.hypot(mp.x - Lx, mp.y - Ly) / Lr;
            }
          }
        }
      },
      isProjected() { return projected; },
      setImportance(wt) { importance = wt; },
      candidatesAt(px, py, r = PICK_R) {
        if (!projected) return [];
        const out = [];
        const L = lens && lens.vis ? lens : null;
        if (L && Math.hypot(px - L.cx, py - L.cy) < L.r) {
          // inside the lens: the spread-apart stars, at the places the lens shows them, with generous targets
          const rr = Math.max(r, LENS_PICK_R);
          for (let k = 0; k < lensCount; k++) {
            const i = lensList[k];
            if (lr[i] > 0.9) continue;                                      // the compressed rim is context, not a target
            const dx = lx[i] - px, dy = ly[i] - py;
            if (dx > rr || dx < -rr || dy > rr || dy < -rr) continue;
            const d = Math.hypot(dx, dy); if (d > rr) continue;
            const mode = state[4 * i + 3];
            const score = d / rr - importance[i] * 0.16 - (mode > 0.5 && mode < 1.5 ? 0.3 : 0) + (mode > 1.5 ? 0.8 : 0) - clamp((8 - app[i]) * 0.012, -0.08, 0.12) + (1 - fog[i]) * 0.3;
            out.push({ i, d, score });
          }
        } else {
          // outside the lens only key places are drawn, so only they can be picked
          for (const i of keyList) {
            if (!visible[i] || inLens[i]) continue;
            const dx = sx[i] - px, dy = sy[i] - py;
            if (dx > r || dx < -r || dy > r || dy < -r) continue;
            const d = Math.hypot(dx, dy); if (d > r) continue;
            out.push({ i, d, score: d / r - importance[i] * 0.3 });
          }
        }
        out.sort((a, b) => a.score - b.score);
        return out.slice(0, 12);
      },
      pick(px, py) {
        const list = field.candidatesAt(px, py);
        if (!list.length) return { i: -1, list, crowd: [], crowded: false };
        const crowd = [list[0]].concat(list.slice(1).filter(c => c.d <= 10)).slice(0, 6);
        const a = crowd[0], b = crowd[1];
        // still too close to call after the lens spread them (stars on almost the same line of sight)
        const crowded = !!b && b.score - a.score < 0.12 && Math.abs(b.d - a.d) < 6 && !(a.d < 4 && b.d > 6);
        return { i: a.i, list, crowd, crowded };
      },
      // key stars outside the lens worth a name: only what the app asks for (extra), never the glow's stars
      labelCandidates(extra) {
        if (!projected) return [];
        const out = [];
        for (const [i, bonus] of extra) {
          if (!visible[i] || inLens[i]) continue;
          out.push(i, 100 - app[i] + (cat[i] ? 1.2 : 0) + bonus);
        }
        return out;
      },
    };
    return field;
  }
})();
