/* Hollow Universe map, concept Clusters: the star field.
 *
 * Like pins on a phone map. Stars that would crowd each other on screen merge into cluster bubbles with a count;
 * bubbles split into smaller bubbles and then single stars as you zoom in or approach. "Places" (your space,
 * stations, bases, filter matches, results, the selection, the route) are always individual glyphs. Every other star
 * is either inside a bubble, where it is drawn only as a faint density haze, or a single star you can pick.
 *
 * How the clusters are chosen
 *   - At start-up the stars are grouped into a fixed hierarchy, separately for each owner (unclaimed, contested, your
 *     space, each faction), so a cluster never mixes owners by construction. Each level doubles the cluster radius
 *     (0.35 ly ... 358 ly; vertical distances count 62 %). The hierarchy never changes, so bubbles are stable.
 *   - Every frame the hierarchy is cut by what the camera sees: a cluster opens (splits into its children) when its
 *     children would sit far enough apart on screen (bubbles ~200 px, single stars ~46 px), or when it would cover too
 *     much of the screen. Near clusters open before far ones. Hysteresis stops flicker at the threshold.
 *   - Then a screen-space pass, biggest first: a bubble absorbs whatever lies within ~132 px of it (clusters stacked along
 *     the view at other depths, a lone star too close to it), so bubbles never crowd or overlap.
 *   - Opening and closing animate: children fly out of their parent bubble (single stars too), and fly back in.
 *
 * Interface: concept AB's, plus the cluster calls
 *   const field = HollowStars.create({ THREE, scene, dpr, stars, layer, owners, tags })
 *     stars:  { n, x, y, z: Float32Array (ly), mag: Float32Array (absolute V), col: Float32Array (rgb 0..1 x3), cat: Uint8Array }
 *     layer:  HTML element for the bubble counts (all text is HTML, so it stays crisp)
 *     owners: { of: Int8Array(n) (-1 unclaimed, -2 contested, k = faction k), colors: ['#rrggbb' per faction], contested: '#rrggbb' }
 *     tags:   { fuel, station, base, land, earth, danger }: Uint8Array(n) each; summed per bubble for its summary
 *   field.update({ target, dist, opacity, clusterOpacity, time })   every frame; opacity fades the stars, clusterOpacity the bubbles
 *   field.setStyle(i, mode, rgb) / commit() / styleOf(i)            0 normal, 1 highlighted in rgb, 2 dimmed (as concept AB)
 *   field.setHighlight({ color } | null)        while a filter or list is on, bubbles count and ring their matches in this colour
 *   field.setPlaces(kind)                        Uint8Array(n): 0 none, 1 match/result dot (style rgb), 2 station, 3 home,
 *                                               4 your outpost, 5 another player's base, 6 contested, 7 plain place (no glyph)
 *   field.project(camera, w, h)                  every frame: cuts the hierarchy, animates bubbles, fills visible / sx / sy / app
 *                                               (visible = shown individually: single stars and places)
 *   field.labelCandidates(extra), setImportance(w), candidatesAt(x, y, r), pick(x, y)   as concept AB, over individual stars
 *   field.bubbles                                bubbles drawn now: [{ key, x, y, r, a, cnt, match, st, oc, dom }]
 *   field.clusterAt(x, y)                        the bubble under the cursor, or null
 *   field.setHoverCluster(key)                   ring highlight (-1 for none)
 *   field.members(key)                           star indices inside a bubble; field.boxOf(key): { c: [x, y, z], ext, cnt } in ly
 *   field.clusterOf(i)                           key of the bubble holding star i now, or -1
 */
(function () {
  'use strict';
  window.HollowStars = { create };

  const LEVELS = 11, R0 = 0.35, KY = 0.62;
  const GAP_BUBBLE = 196, GAP_STAR = 46, GAP_MIXED = 120, MAX_R = 240, HYST = 0.86;
  const ABSORB = 132, ABSORB_STAR = 26, ABSORB_PAIR = 22;   // on screen: bubbles keep ~132 px apart; a lone star needs 26 px of room
  const TAGS = ['fuel', 'station', 'base', 'land', 'earth', 'danger'], NT = TAGS.length;
  const MAXB = 1024;
  const bubbleD = c => Math.max(26, Math.min(64, 24 + 10.5 * Math.log10(c)));
  const fmtCount = c => c < 1000 ? String(c) : c < 9950 ? (c / 1000).toFixed(1).replace(/\.0$/, '') + 'k' : Math.round(c / 1000) + 'k';

  function create({ THREE, scene, dpr, stars, layer, owners, tags }) {
    const { n, x, y, z, mag, col, cat } = stars;
    const clamp = (v, a, b) => v < a ? a : v > b ? b : v;
    const smoothstep = (a, b, v) => { const t = clamp((v - a) / (b - a), 0, 1); return t * t * (3 - 2 * t); };
    const hexRGB = h => { const c = parseInt(h.slice(1), 16); return [(c >> 16 & 255) / 255, (c >> 8 & 255) / 255, (c & 255) / 255]; };

    // ---------- owners: class 0 unclaimed, 1 contested, 2 + k faction k ----------
    const ownOf = owners ? owners.of : new Int8Array(n).fill(-1);
    const NF = owners ? owners.colors.length : 0, NC = 2 + NF;
    const CLASS_RGB = [[0.70, 0.76, 0.84], hexRGB((owners && owners.contested) || '#ffb05e')].concat((owners ? owners.colors : []).map(hexRGB));
    const CLASS_CSS = ['', (owners && owners.contested) || '#ffb05e'].concat(owners ? owners.colors : []);
    const ocOf = i => ownOf[i] === -1 ? 0 : ownOf[i] === -2 ? 1 : 2 + ownOf[i];

    // ---------- the hierarchy (built once) ----------
    const CAP = 2 * n + 64;
    const CX = new Float32Array(CAP), CY = new Float32Array(CAP), CZ = new Float32Array(CAP), RAD = new Float32Array(CAP);
    const CNT = new Int32Array(CAP), PAR = new Int32Array(CAP).fill(-1), LV = new Int8Array(CAP).fill(-1), OC = new Uint8Array(CAP);
    const ST = new Int32Array(CAP * NT), MATCH = new Int32Array(CAP), SPLIT = new Uint8Array(CAP);
    const KIDS = new Array(CAP);
    let NN = n;
    const byClass = Array.from({ length: NC }, () => []);
    for (let i = 0; i < n; i++) {
      CX[i] = x[i]; CY[i] = y[i]; CZ[i] = z[i]; CNT[i] = 1; OC[i] = ocOf(i); byClass[OC[i]].push(i);
      for (let t = 0; t < NT; t++) { const a = tags && tags[TAGS[t]]; ST[i * NT + t] = a && a[i] ? 1 : 0; }
    }
    const mark = new Int32Array(CAP); let stamp = 0;
    const key3 = (a, b, c) => ((a + 4096) * 8192 + (b + 4096)) * 8192 + (c + 4096);
    function level(items, r, L, cls) {
      stamp++;
      const inv = 1 / r, r2 = r * r, grid = new Map();
      for (const id of items) {
        const k = key3(Math.floor(CX[id] * inv), Math.floor(CY[id] * KY * inv), Math.floor(CZ[id] * inv));
        let a = grid.get(k); if (!a) grid.set(k, a = []); a.push(id);
      }
      const order = items.slice().sort((a, b) => CNT[b] - CNT[a] || a - b);
      const out = [];
      for (const p of order) {
        if (mark[p] === stamp) continue;
        mark[p] = stamp;
        const px = CX[p], py = CY[p], pz = CZ[p];
        const gx = Math.floor(px * inv), gy = Math.floor(py * KY * inv), gz = Math.floor(pz * inv);
        const mem = [p];
        for (let a = -1; a <= 1; a++) for (let b = -1; b <= 1; b++) for (let c = -1; c <= 1; c++) {
          const cell = grid.get(key3(gx + a, gy + b, gz + c)); if (!cell) continue;
          for (const q of cell) {
            if (mark[q] === stamp) continue;
            const dx = CX[q] - px, dy = (CY[q] - py) * KY, dz = CZ[q] - pz;
            if (dx * dx + dy * dy + dz * dz <= r2) { mark[q] = stamp; mem.push(q); }
          }
        }
        if (mem.length === 1) { out.push(p); continue; }
        const id = NN++;
        let cn = 0, sx = 0, sy = 0, sz = 0;
        for (const q of mem) { const w = CNT[q]; cn += w; sx += CX[q] * w; sy += CY[q] * w; sz += CZ[q] * w; PAR[q] = id; for (let t = 0; t < NT; t++) ST[id * NT + t] += ST[q * NT + t]; }
        CX[id] = sx / cn; CY[id] = sy / cn; CZ[id] = sz / cn; CNT[id] = cn; LV[id] = L; OC[id] = cls;
        let rad = 0; for (const q of mem) rad = Math.max(rad, Math.hypot(CX[q] - CX[id], CY[q] - CY[id], CZ[q] - CZ[id]) + RAD[q]);
        RAD[id] = rad; KIDS[id] = Int32Array.from(mem);
        out.push(id);
      }
      return out;
    }
    const roots = [];
    for (let c = 0; c < NC; c++) {
      let items = byClass[c];
      for (let L = 0; L < LEVELS && items.length > 1; L++) items = level(items, R0 * Math.pow(2, L), L, c);
      roots.push(...items);
    }

    // ---------- stars: one geometry drawn twice (haze under the bubbles; single stars and place glyphs above) ----------
    const pos = new Float32Array(n * 3), colour = new Float32Array(n * 3), state = new Float32Array(n * 4), show = new Float32Array(n * 4), place = new Float32Array(n);
    for (let i = 0; i < n; i++) {
      pos[3 * i] = x[i]; pos[3 * i + 1] = y[i]; pos[3 * i + 2] = z[i];
      const r = col[3 * i], g = col[3 * i + 1], b = col[3 * i + 2], l = 0.299 * r + 0.587 * g + 0.114 * b;
      colour[3 * i] = l + (r - l) * 0.62; colour[3 * i + 1] = l + (g - l) * 0.62; colour[3 * i + 2] = l + (b - l) * 0.62;
      show[4 * i + 3] = -1e-4;   // haze since the start (age = time since this moment)
    }
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    geo.setAttribute('aColor', new THREE.BufferAttribute(colour, 3));
    geo.setAttribute('aMag', new THREE.BufferAttribute(mag, 1));
    const stateAttr = new THREE.BufferAttribute(state, 4).setUsage(THREE.DynamicDrawUsage); geo.setAttribute('aState', stateAttr);
    const showAttr = new THREE.BufferAttribute(show, 4).setUsage(THREE.DynamicDrawUsage); geo.setAttribute('aShow', showAttr);
    const placeAttr = new THREE.BufferAttribute(place, 1).setUsage(THREE.DynamicDrawUsage); geo.setAttribute('aPlace', placeAttr);
    const U = {
      uOpacity: { value: 1 }, uPx: { value: dpr }, uTarget: { value: new THREE.Vector3() }, uFogNear: { value: 100 }, uFogFar: { value: 200 },
      uBoost: { value: 0 }, uTime: { value: 0 }, uNear: { value: 10 }, uHaze: { value: 0.2 },
    };
    const VS = `
      attribute vec3 aColor; attribute float aMag; attribute vec4 aState; attribute vec4 aShow; attribute float aPlace;
      uniform float uOpacity, uPx, uFogNear, uFogFar, uBoost, uTime, uNear, uHaze, uPass; uniform vec3 uTarget;
      varying vec3 vCol; varying float vA; varying float vS; varying float vK;
      void main() {
        float age = uTime - abs(aShow.w);
        float ind = aShow.w > 0.0 ? smoothstep(0.0, 0.3, age) : 1.0 - smoothstep(0.0, 0.22, age);
        vec3 p = position;
        if (aShow.w > 0.0) { float f = clamp(age / 0.45, 0.0, 1.0); f = 1.0 - (1.0 - f) * (1.0 - f) * (1.0 - f); p = mix(aShow.xyz, position, f); }
        vec4 mv = modelViewMatrix * vec4(p, 1.0);
        gl_Position = projectionMatrix * mv;
        float d = max(length(mv.xyz), 0.05);
        float app = aMag + 5.0 * log(d / 32.6) / log(10.0) - uBoost;
        float fog = 1.0 - smoothstep(uFogNear, uFogFar, distance(position, uTarget));
        float nearF = smoothstep(uNear * 0.55, uNear, -mv.z);
        float mode = aState.w;
        vec3 c = aColor; float s; float a; vK = 0.0;
        if (uPass < 0.5) {
          s = 1.4 + clamp(3.4 - app * 0.24, 0.0, 1.6);
          a = uHaze * clamp(1.2 - app * 0.05, 0.32, 1.0) * (1.0 - ind);
          if (mode > 1.5) a *= 0.28; else if (mode > 0.5) { c = aState.rgb; a = min(1.0, a * 2.2); s += 0.4; }
        } else {
          s = clamp(10.0 - app * 0.5, 3.4, 12.5);
          a = clamp(1.35 - app * 0.07, 0.5, 1.0) * ind;
          if (mode > 1.5) a *= 0.3; else if (mode > 0.5) { c = aState.rgb; s = max(s, 6.5) + 1.0; a = ind; fog = max(fog, 0.6); }
          if (aPlace > 0.5 && aPlace < 6.5) { vK = aPlace; s = 19.0; a = ind * (mode > 1.5 ? 0.38 : 1.0); fog = max(fog, 0.7); }
        }
        vCol = c; vA = a * fog * nearF * uOpacity; vS = s * uPx;
        gl_PointSize = vA > 0.002 ? vS : 0.0;
      }`;
    const FS = `
      uniform float uPx, uPass;
      varying vec3 vCol; varying float vA; varying float vS; varying float vK;
      float band(float d, float w) { return 1.0 - smoothstep(w * 0.5 - 0.55, w * 0.5 + 0.55, abs(d)); }
      float fillc(float d) { return 1.0 - smoothstep(-0.6, 0.6, d); }
      float sdTri(vec2 p, float r) {
        const float k = 1.7320508; p.x = abs(p.x) - r; p.y = p.y + r / k;
        if (p.x + k * p.y > 0.0) p = vec2(p.x - k * p.y, -k * p.x - p.y) / 2.0;
        p.x -= clamp(p.x, -2.0 * r, 0.0); return -length(p) * sign(p.y);
      }
      void main() {
        vec2 q = (gl_PointCoord - 0.5) * vS;
        float r = length(q);
        if (vK < 0.5) {
          float rr = r / (vS * 0.5);
          if (uPass < 0.5) { float a = exp(-rr * rr * 3.2) * vA; if (a < 0.003) discard; gl_FragColor = vec4(vCol * a, 0.0); return; }
          float aa = 2.2 / max(vS, 1.0);
          float core = 1.0 - smoothstep(0.40 - aa, 0.40 + aa, rr);
          float halo = exp(-rr * rr * 6.5) * 0.42;
          float a = (core + halo) * vA;
          if (a < 0.004) discard;
          gl_FragColor = vec4(mix(vCol, vec3(1.0), core * 0.3) * a, core * vA * 0.5);
          return;
        }
        float P = uPx, k = vK, g = 0.0, back = 0.0; vec3 c = vCol;
        if (k < 1.5) { float R = 4.4 * P; g = fillc(r - R); back = fillc(r - R - 1.8 * P); }
        else if (k < 2.5) { c = vec3(0.77, 0.71, 0.99); float dd = (abs(q.x) + abs(q.y)) * 0.7071, R = 4.4 * P; g = max(band(dd - R, 1.8 * P), fillc(r - 1.4 * P)); back = fillc(dd - R - 2.2 * P); }
        else if (k < 3.5) { c = vec3(0.373, 0.890, 0.753); g = max(fillc(r - 3.0 * P), band(r - 6.2 * P, 1.7 * P)); back = fillc(r - 8.0 * P); }
        else if (k < 4.5) { c = vec3(0.373, 0.890, 0.753); float m = max(abs(q.x), abs(q.y)), R = 4.2 * P; g = max(band(m - R, 1.8 * P), fillc(m - 1.6 * P)); back = fillc(m - R - 2.2 * P); }
        else if (k < 5.5) { c = vec3(1.0, 0.61, 0.81); float t = sdTri(vec2(q.x, -q.y + 1.0 * P), 5.6 * P); g = band(t, 1.8 * P); back = fillc(t - 2.2 * P); }
        else { c = vec3(1.0, 0.69, 0.37); g = max(band(r - 5.0 * P, 1.8 * P), fillc(r - 1.5 * P)); back = fillc(r - 7.0 * P); }
        float A = max(g, back * 0.78) * vA;
        if (A < 0.004) discard;
        vec3 o = mix(vec3(0.02, 0.028, 0.045), c, g / max(max(g, back * 0.78), 1e-4));
        gl_FragColor = vec4(o * A, A);
      }`;
    const mkMat = pass => new THREE.ShaderMaterial({
      uniforms: Object.assign({}, U, { uPass: { value: pass } }), vertexShader: VS, fragmentShader: FS,
      transparent: true, depthWrite: false, depthTest: false,
      blending: THREE.CustomBlending, blendEquation: THREE.AddEquation, blendSrc: THREE.OneFactor, blendDst: THREE.OneMinusSrcAlphaFactor,
    });
    const points = new THREE.Points(geo, mkMat(0)); points.frustumCulled = false; points.renderOrder = 4; scene.add(points);
    const indPoints = new THREE.Points(geo, mkMat(1)); indPoints.frustumCulled = false; indPoints.renderOrder = 6; scene.add(indPoints);

    // ---------- bubbles: WebGL discs with a segmented ring; the counts are HTML ----------
    const bPos = new Float32Array(MAXB * 3), bD = new Float32Array(MAXB), bA = new Float32Array(MAXB), bSeg = new Float32Array(MAXB * 4);
    const bC0 = new Float32Array(MAXB * 3), bC1 = new Float32Array(MAXB * 3), bC2 = new Float32Array(MAXB * 3), bFill = new Float32Array(MAXB * 4);
    const bgeo = new THREE.BufferGeometry();
    const battr = (name, arr, k) => { const a = new THREE.BufferAttribute(arr, k).setUsage(THREE.DynamicDrawUsage); bgeo.setAttribute(name, a); return a; };
    const BA = [battr('position', bPos, 3), battr('aD', bD, 1), battr('aA', bA, 1), battr('aSeg', bSeg, 4), battr('aC0', bC0, 3), battr('aC1', bC1, 3), battr('aC2', bC2, 3), battr('aFill', bFill, 4)];
    bgeo.setDrawRange(0, 0);
    const bubbleMat = new THREE.ShaderMaterial({
      uniforms: { uPx: { value: dpr } },
      vertexShader: `
        attribute float aD; attribute float aA; attribute vec4 aSeg; attribute vec3 aC0; attribute vec3 aC1; attribute vec3 aC2; attribute vec4 aFill;
        uniform float uPx;
        varying float vD; varying float vA; varying vec4 vSeg; varying vec3 vC0; varying vec3 vC1; varying vec3 vC2; varying vec4 vFill;
        void main() {
          gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
          vD = aD * uPx; vA = aA; vSeg = aSeg; vC0 = aC0; vC1 = aC1; vC2 = aC2; vFill = aFill;
          gl_PointSize = aA > 0.003 ? vD + 14.0 * uPx : 0.0;
        }`,
      fragmentShader: `
        uniform float uPx;
        varying float vD; varying float vA; varying vec4 vSeg; varying vec3 vC0; varying vec3 vC1; varying vec3 vC2; varying vec4 vFill;
        void main() {
          float S = vD + 14.0 * uPx;
          vec2 q = (gl_PointCoord - 0.5) * S;
          float r = length(q), R = vD * 0.5, hov = vSeg.w;
          float rw = (hov > 0.5 ? 2.6 : 1.9) * uPx;
          float disc = 1.0 - smoothstep(R - 0.7, R + 0.5, r);
          float inner = 1.0 - smoothstep(R - rw - 0.7, R - rw + 0.5, r);
          float f = fract(atan(q.x, -q.y) / 6.2831853 + 1.0);
          vec3 rc = f < vSeg.x ? vC0 : f < vSeg.y ? vC1 : vC2;
          float gapA = 1.0;
          if (vSeg.x < 0.999) {
            float circ = 6.2831853 * R;
            float e = min(abs(f - vSeg.x), min(f, 1.0 - f));
            if (vSeg.y < 0.999) e = min(e, abs(f - vSeg.y));
            gapA = smoothstep(0.8 * uPx, 1.9 * uPx, e * circ);
          }
          if (hov > 0.5) rc = mix(rc, vec3(1.0), 0.28);
          float ringA = clamp(disc - inner, 0.0, 1.0) * gapA;
          float shadow = (1.0 - smoothstep(R, R + 6.0 * uPx, r)) * (1.0 - disc) * 0.5;
          vec3 fill = vFill.rgb + (hov > 0.5 ? 0.05 : 0.0);
          float sheen = (1.0 - smoothstep(0.0, R, length(q + vec2(0.0, R * 0.55)))) * 0.06;
          vec4 c = vec4(0.0, 0.0, 0.0, shadow);
          c = mix(c, vec4(fill + sheen, vFill.a), inner);
          c = mix(c, vec4(rc, 1.0), ringA);
          c.a *= vA;
          if (c.a < 0.003) discard;
          gl_FragColor = vec4(c.rgb * c.a, c.a);
        }`,
      transparent: true, depthWrite: false, depthTest: false,
      blending: THREE.CustomBlending, blendEquation: THREE.AddEquation, blendSrc: THREE.OneFactor, blendDst: THREE.OneMinusSrcAlphaFactor,
    });
    const bubblePts = new THREE.Points(bgeo, bubbleMat); bubblePts.frustumCulled = false; bubblePts.renderOrder = 5; scene.add(bubblePts);

    // ---------- state ----------
    const sx = new Float32Array(n), sy = new Float32Array(n), app = new Float32Array(n), visible = new Uint8Array(n);
    const PICK_R = 24;
    let projected = false, appLimit = 9, importance = new Float32Array(n), highlight = null, hoverKey = -1;
    let starOp = 1, clusOp = 1, now = 0, fogNear = 100, fogFar = 200, nearCut = 10, lastT = 0;
    const target = new THREE.Vector3();
    let placeKind = new Uint8Array(n), placeList = [];
    let indList = [];
    const indMark = new Uint8Array(n);
    const recs = new Map();                              // bubble key -> animated record
    let shownNow = new Map(), shownPrev = new Map();     // node -> key of the group showing it
    let groupsNow = [];
    const bubbles = [];

    // ---------- per-frame cut of the hierarchy ----------
    const stack = new Int32Array(CAP);
    const itemId = [], itemX = [], itemY = [], itemR = [], itemF = [], itemLeaf = [];
    const KMAX = 8192, kidX = new Float32Array(KMAX), kidY = new Float32Array(KMAX), kidR = new Float32Array(KMAX), kidL = new Uint8Array(KMAX);
    function cut(e, vm, W, H, focal) {
      itemId.length = itemX.length = itemY.length = itemR.length = itemF.length = itemLeaf.length = 0;
      let sp = 0; for (const r of roots) stack[sp++] = r;
      const tx = target.x, ty = target.y, tz = target.z;
      while (sp) {
        const id = stack[--sp];
        const X = CX[id], Y = CY[id], Z = CZ[id], R = RAD[id];
        const dt = Math.hypot(X - tx, Y - ty, Z - tz);
        if (dt - R > fogFar) continue;
        const depth = -(vm[2] * X + vm[6] * Y + vm[10] * Z + vm[14]);
        if (depth + R < nearCut) continue;
        const safe = depth - R > nearCut;
        let px = 0, py = 0;
        if (safe || id < n) {
          if (depth <= 1e-4) continue;
          const cw = e[3] * X + e[7] * Y + e[11] * Z + e[15];
          px = ((e[0] * X + e[4] * Y + e[8] * Z + e[12]) / cw * 0.5 + 0.5) * W;
          py = (0.5 - (e[1] * X + e[5] * Y + e[9] * Z + e[13]) / cw * 0.5) * H;
          const pr = R * focal / Math.max(depth - R, nearCut * 0.5);
          if (px + pr < -60 || px - pr > W + 60 || py + pr < -60 || py - pr > H + 60) continue;
        }
        const fade = (1 - smoothstep(fogNear, fogFar, dt)) * smoothstep(nearCut, nearCut * 1.6, depth);
        if (id < n) {
          if (dt > fogFar || depth < nearCut) continue;
          itemId.push(id); itemX.push(px); itemY.push(py); itemR.push(0); itemLeaf.push(1); itemF.push(fade);
          continue;
        }
        let open = !safe;
        if (!open) {
          const pr = R * focal / depth, h = SPLIT[id] === 1 ? HYST : 1;
          if (pr > MAX_R * h) open = true;
          else {
            // open when every pair of children would sit far enough apart on screen
            const kids = KIDS[id], k = Math.min(kids.length, KMAX);
            let ok = true;
            for (let a = 0; a < k && ok; a++) {
              const q = kids[a], dq = -(vm[2] * CX[q] + vm[6] * CY[q] + vm[10] * CZ[q] + vm[14]);
              if (dq <= nearCut * 0.5) { ok = false; break; }
              const cw = e[3] * CX[q] + e[7] * CY[q] + e[11] * CZ[q] + e[15];
              kidX[a] = ((e[0] * CX[q] + e[4] * CY[q] + e[8] * CZ[q] + e[12]) / cw * 0.5 + 0.5) * W;
              kidY[a] = (0.5 - (e[1] * CX[q] + e[5] * CY[q] + e[9] * CZ[q] + e[13]) / cw * 0.5) * H;
              kidL[a] = q < n ? 1 : 0; kidR[a] = q < n ? 0 : bubbleD(CNT[q]) / 2;
              for (let b = 0; b < a; b++) {
                const need = (kidL[a] && kidL[b] ? GAP_STAR : (kidL[a] || kidL[b]) ? Math.max(GAP_MIXED, kidR[a] + kidR[b] + 16) : Math.max(GAP_BUBBLE, kidR[a] + kidR[b] + 14)) * h;
                const dx = kidX[a] - kidX[b], dy = kidY[a] - kidY[b];
                if (dx * dx + dy * dy < need * need) { ok = false; break; }
              }
            }
            open = ok;
          }
        }
        if (open) { SPLIT[id] = 1; const kids = KIDS[id]; for (let a = 0; a < kids.length; a++) stack[sp++] = kids[a]; }
        else {
          SPLIT[id] = 0;
          if (dt > fogFar) continue;
          itemId.push(id); itemX.push(px); itemY.push(py); itemR.push(bubbleD(CNT[id]) / 2); itemLeaf.push(0); itemF.push(fade);
        }
      }
    }

    // ---------- absorb: nothing overlaps on screen ----------
    const gPool = [];
    function absorb() {
      const order = itemId.map((_, k) => k);
      order.sort((a, b) => (itemLeaf[a] - itemLeaf[b]) || (CNT[itemId[b]] - CNT[itemId[a]]) || (itemId[a] - itemId[b]));
      const groups = [], grid = new Map(), CELL = 140;
      const gk = (a, b) => (a + 512) * 4096 + (b + 512);
      for (const k of order) {
        const id = itemId[k], X = itemX[k], Y = itemY[k], R = itemR[k], leaf = itemLeaf[k];
        if (leaf && placeKind[id] > 0 && placeKind[id] < 7) continue;   // places draw themselves
        const cx = Math.floor(X / CELL), cy = Math.floor(Y / CELL);
        let hit = null, best = 1e9;
        for (let a = -1; a <= 1; a++) for (let b = -1; b <= 1; b++) {
          const cell = grid.get(gk(cx + a, cy + b)); if (!cell) continue;
          for (const g of cell) {
            const dx = g.x - X, dy = g.y - Y, d = Math.sqrt(dx * dx + dy * dy);
            const need = g.single ? (leaf ? ABSORB_PAIR : Math.max(R + 6, ABSORB * 0.6)) : leaf ? g.r + ABSORB_STAR : Math.max(g.r + R + 4, ABSORB);
            if (d < need && d < best) { best = d; hit = g; }
          }
        }
        if (hit) {
          hit.nodes.push(id); hit.cnt += CNT[id]; hit.match += MATCH[id]; hit.oc[OC[id]] += CNT[id];
          for (let t = 0; t < NT; t++) hit.st[t] += ST[id * NT + t];
          hit.single = false; hit.r = bubbleD(hit.cnt) / 2;
          continue;
        }
        const g = gPool[groups.length] || (gPool[groups.length] = { st: new Int32Array(NT), oc: new Int32Array(NC), nodes: [] });
        g.key = id; g.x = X; g.y = Y; g.r = leaf ? 4 : R; g.cnt = CNT[id]; g.match = MATCH[id]; g.single = !!leaf; g.f = itemF[k];
        g.nodes.length = 0; g.nodes.push(id); g.oc.fill(0); g.oc[OC[id]] = CNT[id];
        for (let t = 0; t < NT; t++) g.st[t] = ST[id * NT + t];
        groups.push(g);
        const key = gk(cx, cy); let cell = grid.get(key); if (!cell) grid.set(key, cell = []); cell.push(g);
      }
      return groups;
    }

    // ---------- bubble records: animation and drawing ----------
    function ancestorShown(map, id) { let p = id; for (let s = 0; s < 18 && p >= 0; s++) { if (map.has(p)) return map.get(p); p = PAR[p]; } return -1; }
    function centroidOf(g, out) {
      let w = 0, X = 0, Y = 0, Z = 0;
      for (const id of g.nodes) { const c = CNT[id]; w += c; X += CX[id] * c; Y += CY[id] * c; Z += CZ[id] * c; }
      const k = 0.25;   // stay on the lead cluster, pulled a little toward what it absorbed
      out[0] = CX[g.key] * (1 - k) + X / w * k; out[1] = CY[g.key] * (1 - k) + Y / w * k; out[2] = CZ[g.key] * (1 - k) + Z / w * k;
      return out;
    }
    function dominant(rec) {
      let best = 0, bw = -1;
      for (let c = 0; c < NC; c++) { const w = rec.oc[c] * (c === 0 ? 1 : 4); if (w > bw) { bw = w; best = c; } }
      return best;
    }
    function ringOf(rec) {
      // up to three segments: the matches (filter colour), or the owners (owned systems weigh 4x so a border shows)
      const segs = [];
      if (highlight) {
        const f = rec.match / Math.max(1, rec.cnt);
        if (rec.match > 0) segs.push([Math.max(0.06, f), highlight.rgb]);
        if (rec.match < rec.cnt) segs.push([1, [0.36, 0.40, 0.47]]);
      } else {
        const ws = [];
        for (let c = 0; c < NC; c++) if (rec.oc[c] > 0) ws.push([c, rec.oc[c] * (c === 0 ? 1 : 4)]);
        ws.sort((a, b) => b[1] - a[1]);
        const tot = ws.reduce((s, w) => s + w[1], 0), m = Math.min(ws.length, 3);
        let acc = 0;
        for (let k = 0; k < m; k++) { acc = k === m - 1 ? 1 : Math.min(0.92, acc + Math.max(0.08, ws[k][1] / tot)); segs.push([acc, CLASS_RGB[ws[k][0]]]); }
      }
      if (!segs.length) segs.push([1, CLASS_RGB[0]]);
      segs[segs.length - 1][0] = 1;
      return segs;
    }
    const tmp3 = [0, 0, 0];
    function syncRecords(groups, dt) {
      const seen = new Set();
      for (const g of groups) {
        if (g.single) continue;
        seen.add(g.key);
        let rec = recs.get(g.key);
        const c = centroidOf(g, tmp3);
        if (!rec) {
          rec = { key: g.key, w: [c[0], c[1], c[2]], goal: [0, 0, 0], a: 0, ag: 1, st: new Int32Array(NT), oc: new Int32Array(NC), el: null, txt: '', cls: '' };
          const from = ancestorShown(shownPrev, g.key), fr = from >= 0 && from !== g.key ? recs.get(from) : null;
          if (fr) { rec.w[0] = fr.w[0]; rec.w[1] = fr.w[1]; rec.w[2] = fr.w[2]; rec.a = Math.min(fr.a, 0.6); }   // grows out of its parent
          recs.set(g.key, rec);
        }
        rec.goal[0] = c[0]; rec.goal[1] = c[1]; rec.goal[2] = c[2]; rec.ag = 1; rec.f = g.f; rec.gone = 0;
        rec.cnt = g.cnt; rec.match = g.match; rec.st.set(g.st); rec.oc.set(g.oc);
      }
      for (const [key, rec] of recs) {
        if (seen.has(key)) continue;
        if (!rec.gone) {
          rec.gone = 1; rec.ag = 0;
          const into = ancestorShown(shownNow, key), ir = into >= 0 && into !== key ? recs.get(into) : null;
          if (ir) { rec.goal[0] = ir.goal[0]; rec.goal[1] = ir.goal[1]; rec.goal[2] = ir.goal[2]; }   // flies into the bubble that holds it now
        }
        if (rec.a < 0.01) { if (rec.el) rec.el.remove(); recs.delete(key); }
      }
      const kp = 1 - Math.exp(-dt * 11), ka = 1 - Math.exp(-dt * 9);
      for (const rec of recs.values()) {
        for (let q = 0; q < 3; q++) rec.w[q] += (rec.goal[q] - rec.w[q]) * kp;
        rec.a += (rec.ag - rec.a) * ka;
        if (rec.ag === 1 && rec.a > 0.995) rec.a = 1;
      }
    }
    function drawRecords(e, W, H) {
      bubbles.length = 0;
      const list = [...recs.values()].sort((a, b) => a.cnt - b.cnt);
      let k = 0;
      for (const rec of list) {
        const X = rec.w[0], Y = rec.w[1], Z = rec.w[2];
        const cw = e[3] * X + e[7] * Y + e[11] * Z + e[15];
        const alpha = rec.a * (rec.f ?? 1) * clusOp * (highlight && rec.match === 0 ? 0.3 : 1);
        if (cw <= 1e-6 || alpha < 0.01 || k >= MAXB) { rec.sx = -1e4; rec.sy = -1e4; if (rec.el && rec.op !== '0') { rec.el.style.opacity = '0'; rec.op = '0'; } continue; }
        const px = ((e[0] * X + e[4] * Y + e[8] * Z + e[12]) / cw * 0.5 + 0.5) * W;
        const py = (0.5 - (e[1] * X + e[5] * Y + e[9] * Z + e[13]) / cw * 0.5) * H;
        const D = bubbleD(rec.cnt) * (0.72 + 0.28 * rec.a);
        bPos[3 * k] = X; bPos[3 * k + 1] = Y; bPos[3 * k + 2] = Z; bD[k] = D; bA[k] = alpha;
        const segs = ringOf(rec);
        bSeg[4 * k] = segs[0][0]; bSeg[4 * k + 1] = segs.length > 1 ? segs[1][0] : 1; bSeg[4 * k + 2] = 1; bSeg[4 * k + 3] = rec.key === hoverKey ? 1 : 0;
        bC0.set(segs[0][1], 3 * k); bC1.set((segs[1] || segs[0])[1], 3 * k); bC2.set(segs[segs.length - 1][1], 3 * k);
        const dom = dominant(rec), tint = highlight ? (rec.match > 0 ? highlight.rgb : null) : dom > 0 ? CLASS_RGB[dom] : null;
        const tk = highlight ? 0.16 : 0.26, base = [0.040, 0.050, 0.072];
        for (let q = 0; q < 3; q++) bFill[4 * k + q] = tint ? base[q] + (tint[q] - base[q]) * tk : base[q];
        bFill[4 * k + 3] = 0.9;
        k++;
        if (!rec.el) { rec.el = document.createElement('div'); rec.el.className = 'cn'; layer.appendChild(rec.el); rec.tr = ''; rec.op = ''; }
        const txt = highlight ? (rec.match ? fmtCount(rec.match) : '') : fmtCount(rec.cnt);
        const cls = 'cn' + (D >= 40 ? ' big' : '') + (highlight && rec.match > 0 ? ' hi' : '') + (rec.key === hoverKey ? ' hov' : '') + (!highlight && dom > 0 ? ' own' : '');
        if (rec.txt !== txt) { rec.el.textContent = txt; rec.txt = txt; }
        if (rec.cls !== cls) { rec.el.className = cls; rec.cls = cls; }
        const hc = highlight && rec.match > 0 ? highlight.css : !highlight && dom > 0 ? CLASS_CSS[dom] : '';
        if (rec.hc !== hc) { rec.el.style.setProperty('--hc', hc || '#fff'); rec.hc = hc; }
        const tr = `translate3d(${Math.round(px)}px,${Math.round(py)}px,0)`;
        if (rec.tr !== tr) { rec.el.style.transform = tr; rec.tr = tr; }
        const op = Math.min(1, alpha * 1.15).toFixed(2);
        if (rec.op !== op) { rec.el.style.opacity = op; rec.op = op; }
        rec.sx = px; rec.sy = py; rec.r = D / 2;
        bubbles.push({ key: rec.key, x: px, y: py, r: D / 2, a: alpha, cnt: rec.cnt, match: rec.match, st: rec.st, oc: rec.oc, dom, gone: !!rec.gone });
      }
      bgeo.setDrawRange(0, k);
      for (const a of BA) a.needsUpdate = true;
    }
    function hideBubbles() {
      bgeo.setDrawRange(0, 0); bubbles.length = 0;
      for (const rec of recs.values()) if (rec.el) rec.el.remove();
      recs.clear(); shownNow = new Map(); shownPrev = new Map(); groupsNow = [];
    }

    // ---------- individual stars: the single stars of the cut, plus every place in view ----------
    function setIndividuals(groups, e, vm, W, H) {
      const next = [];
      for (const g of groups) if (g.single) { next.push(g.key); indMark[g.key] |= 2; }
      for (const i of placeList) {
        if (indMark[i] & 2) continue;
        const X = x[i], Y = y[i], Z = z[i];
        if (Math.hypot(X - target.x, Y - target.y, Z - target.z) > fogFar) continue;
        const depth = -(vm[2] * X + vm[6] * Y + vm[10] * Z + vm[14]);
        if (depth < nearCut * 0.8) continue;
        next.push(i); indMark[i] |= 2;
      }
      let changed = false;
      for (const i of indList) {
        visible[i] = 0;
        if (!(indMark[i] & 2)) { show[4 * i + 3] = -now; indMark[i] = 0; changed = true; }
      }
      for (const i of next) {
        if (!(indMark[i] & 1)) {
          const from = ancestorShown(shownPrev, i), fr = from >= 0 && from !== i ? recs.get(from) : null;
          if (fr) { show[4 * i] = fr.w[0]; show[4 * i + 1] = fr.w[1]; show[4 * i + 2] = fr.w[2]; }   // flies out of its bubble
          else { show[4 * i] = x[i]; show[4 * i + 1] = y[i]; show[4 * i + 2] = z[i]; }
          show[4 * i + 3] = now; changed = true;
        }
        indMark[i] = 1;
        const X = x[i], Y = y[i], Z = z[i];
        const cw = e[3] * X + e[7] * Y + e[11] * Z + e[15];
        if (cw <= 1e-6) continue;
        const px = ((e[0] * X + e[4] * Y + e[8] * Z + e[12]) / cw * 0.5 + 0.5) * W;
        const py = (0.5 - (e[1] * X + e[5] * Y + e[9] * Z + e[13]) / cw * 0.5) * H;
        if (px < -20 || py < -20 || px > W + 20 || py > H + 20) continue;
        sx[i] = px; sy[i] = py; visible[i] = 1;
        app[i] = mag[i] + 5 * Math.log10(Math.max(0.05, cw) / 32.6);
      }
      indList = next;
      if (changed) showAttr.needsUpdate = true;
    }
    function clearIndividuals() {
      if (!indList.length) return;
      for (const i of indList) { visible[i] = 0; indMark[i] = 0; show[4 * i + 3] = -now; }
      indList = []; showAttr.needsUpdate = true;
    }

    const field = {
      points, sx, sy, app, visible, bubbles,
      get nodeCount() { return NN; },
      update({ target: t, dist, opacity, clusterOpacity, time }) {
        starOp = opacity; clusOp = clusterOpacity ?? opacity; now = time ?? performance.now() / 1000;
        U.uOpacity.value = opacity; U.uTime.value = now;
        points.visible = indPoints.visible = opacity > 0.003;
        bubblePts.visible = clusOp > 0.003;
        target.copy(t); U.uTarget.value.copy(t);
        fogNear = Math.max(14, dist * 0.95); fogFar = Math.max(30, dist * 1.9);
        if (dist > 300) { fogNear = Math.max(fogNear, 400); fogFar = Math.max(fogFar, 900); }
        nearCut = Math.max(0.6, dist * 0.42);
        U.uFogNear.value = fogNear; U.uFogFar.value = fogFar; U.uNear.value = nearCut;
        U.uBoost.value = clamp((Math.log10(dist) - 2.3) * 2.2, 0, 4);
        U.uHaze.value = clamp(0.17 - (Math.log10(dist) - 1.4) * 0.04, 0.09, 0.19);
        if (opacity <= 0.02) projected = false;
      },
      setStyle(i, mode, rgb) {
        state[4 * i] = rgb ? rgb[0] : 0; state[4 * i + 1] = rgb ? rgb[1] : 0; state[4 * i + 2] = rgb ? rgb[2] : 0; state[4 * i + 3] = mode;
      },
      styleOf(i) { return state[4 * i + 3]; },
      commit() {
        stateAttr.needsUpdate = true;
        for (let i = 0; i < n; i++) MATCH[i] = state[4 * i + 3] > 0.5 && state[4 * i + 3] < 1.5 ? 1 : 0;
        for (let id = n; id < NN; id++) { let s = 0; const k = KIDS[id]; for (let a = 0; a < k.length; a++) s += MATCH[k[a]]; MATCH[id] = s; }
      },
      setHighlight(h) { highlight = h ? { rgb: hexRGB(h.color), css: h.color } : null; },
      setPlaces(kind) {
        placeKind = kind; placeList = [];
        for (let i = 0; i < n; i++) { place[i] = kind[i]; if (kind[i]) placeList.push(i); }
        placeAttr.needsUpdate = true;
      },
      setHoverCluster(key) { hoverKey = key; },
      project(camera, w, h) {
        const dt = clamp(now - lastT, 0, 0.1); lastT = now;
        if (clusOp <= 0.01 && starOp <= 0.02) { projected = false; if (recs.size) hideBubbles(); clearIndividuals(); return; }
        const m = new THREE.Matrix4().multiplyMatrices(camera.projectionMatrix, camera.matrixWorldInverse), e = m.elements, vm = camera.matrixWorldInverse.elements;
        const focal = h / (2 * Math.tan(camera.fov * Math.PI / 360));
        cut(e, vm, w, h, focal);
        const groups = absorb();
        shownPrev = shownNow; shownNow = new Map();
        for (const g of groups) for (const id of g.nodes) shownNow.set(id, g.key);
        groupsNow = groups;
        syncRecords(groups, dt);
        drawRecords(e, w, h);
        if (starOp > 0.02) { setIndividuals(groups, e, vm, w, h); projected = true; } else { clearIndividuals(); projected = false; }
        window.__dbg = { nodes: NN, roots: roots.length, items: itemId.length, groups: groups.length, bubbles: bubbles.length, singles: indList.length };
      },
      isProjected() { return projected; },
      clusterAt(px, py) {
        let best = null, bd = 1e9;
        for (const b of bubbles) { if (b.gone || b.a < 0.3) continue; const d = Math.hypot(b.x - px, b.y - py); if (d < b.r + 3 && d < bd) { bd = d; best = b; } }
        return best;
      },
      bubbleRects(pad = 3) { const out = []; for (const b of bubbles) if (b.a > 0.35) out.push([b.x - b.r - pad, b.y - b.r - pad, b.x + b.r + pad, b.y + b.r + pad]); return out; },
      members(key) {
        const g = groupsNow.find(q => q.key === key);
        const out = [], st = g ? g.nodes.slice() : key >= 0 ? [key] : [];
        while (st.length) { const id = st.pop(); if (id < n) out.push(id); else for (const q of KIDS[id]) st.push(q); }
        return out;
      },
      boxOf(key, leadOnly) {
        const g = groupsNow.find(q => q.key === key), nodes = g && !leadOnly ? g.nodes : [key];
        let w = 0, X = 0, Y = 0, Z = 0;
        for (const id of nodes) { const c = CNT[id]; w += c; X += CX[id] * c; Y += CY[id] * c; Z += CZ[id] * c; }
        X /= w; Y /= w; Z /= w;
        let ext = 0; for (const id of nodes) ext = Math.max(ext, Math.hypot(CX[id] - X, CY[id] - Y, CZ[id] - Z) + RAD[id]);
        return { c: [X, Y, Z], ext, cnt: w };
      },
      clusterOf(i) { const k = ancestorShown(shownNow, i); if (k < 0) return -1; const g = groupsNow.find(q => q.key === k); return g && !g.single ? k : -1; },
      setImportance(wt) { importance = wt; },
      candidatesAt(px, py, r = PICK_R) {
        if (!projected) return [];
        const out = [];
        for (const i of indList) {
          if (!visible[i]) continue;
          const dx = sx[i] - px, dy = sy[i] - py;
          if (dx > r || dx < -r || dy > r || dy < -r) continue;
          const d = Math.hypot(dx, dy); if (d > r) continue;
          const mode = state[4 * i + 3];
          const score = d / r - importance[i] * 0.45 - (mode > 0.5 && mode < 1.5 ? 0.5 : 0) + (mode > 1.5 ? 0.9 : 0) - clamp((8 - app[i]) * 0.025, -0.15, 0.25) - (placeKind[i] ? 0.15 : 0);
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
        for (const i of indList) {
          if (!visible[i]) continue;
          const bonus = extra.get(i);
          const mode = state[4 * i + 3];
          if (bonus === undefined && (app[i] > appLimit || mode > 1.5)) continue;
          out.push(i, 100 - app[i] + (cat[i] ? 1.2 : 0) + (mode > 0.5 && mode < 1.5 ? 3 : 0) + (bonus || 0));
          count++;
        }
        appLimit = clamp(appLimit + (count > 1400 ? -0.25 : count < 500 ? 0.25 : 0), 2, 22);
        return out;
      },
    };
    return field;
  }
})();
