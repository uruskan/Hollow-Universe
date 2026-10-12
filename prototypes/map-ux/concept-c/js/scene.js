/* Reach map (concept C): the WebGL layer. three.js draws stars, the galaxy, the ground plane (atlas grid,
   jump rings, the tank's reach) and the route. Every word on screen is HTML (see ui.js). */
'use strict';
window.RM = window.RM || {};

RM.createScene = function (THREE, host, model) {
  const M = model;
  const ACCENT = new THREE.Color(0.42, 0.80, 1.0);
  const renderer = new THREE.WebGLRenderer({ antialias: true, powerPreference: 'high-performance' });
  renderer.outputColorSpace = THREE.LinearSRGBColorSpace; // shaders write display values directly
  renderer.setClearColor(0x05070b, 1);
  host.appendChild(renderer.domElement);
  const scene = new THREE.Scene();
  const camera = new THREE.PerspectiveCamera(30, 1, 1, 1e8);
  camera.rotation.order = 'YXZ';

  let PR = Math.min(2, window.devicePixelRatio || 1);
  let VW = 1, VH = 1;
  const T = (x, y, z) => [x, y, -z]; // galactic (X, Y, Z) -> three.js (Z points to the Galactic Centre, up the screen)
  const premul = { transparent: true, depthTest: false, depthWrite: false, premultipliedAlpha: true, blending: THREE.NormalBlending };
  const additive = { transparent: true, depthTest: false, depthWrite: false, blending: THREE.AdditiveBlending };

  // ------------------------------------------------------------ background
  const bg = new THREE.Mesh(new THREE.PlaneGeometry(2, 2), new THREE.ShaderMaterial({
    depthTest: false, depthWrite: false,
    uniforms: { uRes: { value: new THREE.Vector2(1, 1) } },
    vertexShader: 'varying vec2 vUv; void main(){ vUv = uv; gl_Position = vec4(position.xy, 0.0, 1.0); }',
    fragmentShader: `
      precision highp float; varying vec2 vUv; uniform vec2 uRes;
      float hash(vec2 p){ return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }
      void main(){
        vec2 p = (vUv - 0.5) * vec2(uRes.x / uRes.y, 1.0);
        float r = length(p * vec2(0.85, 1.0));
        vec3 c = mix(vec3(0.045, 0.062, 0.092), vec3(0.016, 0.022, 0.034), smoothstep(0.0, 0.95, r));
        c += (hash(gl_FragCoord.xy) - 0.5) / 255.0;
        gl_FragColor = vec4(c, 1.0);
      }`,
  }));
  bg.frustumCulled = false; bg.renderOrder = -100;
  scene.add(bg);

  // ------------------------------------------------------------ galaxy glow (face-on density image)
  const glow = M.decodeGlow();
  const glowTex = (() => {
    const n = glow.size, data = new Uint8Array(n * n * 4);
    for (let k = 0; k < n * n; k++) { data[k * 4] = glow.all[k]; data[k * 4 + 1] = glow.young[k]; data[k * 4 + 3] = 255; }
    const t = new THREE.DataTexture(data, n, n, THREE.RGBAFormat, THREE.UnsignedByteType);
    t.magFilter = THREE.LinearFilter; t.minFilter = THREE.LinearFilter; t.needsUpdate = true;
    return t;
  })();
  const glowSpan = glow.size * glow.cellLy;
  const glowMat = new THREE.ShaderMaterial(Object.assign({}, additive, {
    uniforms: {
      uTex: { value: glowTex }, uFade: { value: 0 }, uY: { value: -60 },
      uOrigin: { value: new THREE.Vector2(glow.x0, glow.z0) }, uSpan: { value: glowSpan },
    },
    vertexShader: `
      uniform vec2 uOrigin; uniform float uSpan, uY; varying vec2 vUv;
      void main(){
        vUv = position.xy + 0.5;
        vec2 g = uOrigin + vUv * uSpan;
        gl_Position = projectionMatrix * viewMatrix * vec4(g.x, uY, -g.y, 1.0);
      }`,
    fragmentShader: `
      precision highp float; uniform sampler2D uTex; uniform float uFade; varying vec2 vUv;
      void main(){
        vec4 t = texture2D(uTex, vUv);
        float dens = t.r > 0.002 ? t.r : 0.0;
        float young = t.g > 0.002 ? t.g : 0.0;
        float body = pow(smoothstep(0.30, 1.0, dens), 2.4) * 0.55;
        float core = pow(smoothstep(0.80, 1.0, dens), 2.0) * 0.55;
        float arms = pow(smoothstep(0.22, 0.95, young), 1.7) * 0.42;
        vec3 c = vec3(0.95, 0.76, 0.52) * body + vec3(1.0, 0.90, 0.74) * core + vec3(0.42, 0.60, 1.0) * arms;
        c = 1.0 - exp(-c * 1.25);
        gl_FragColor = vec4(c * uFade, 1.0);
      }`,
  }));
  const glowMesh = new THREE.Mesh(new THREE.PlaneGeometry(1, 1), glowMat);
  glowMesh.frustumCulled = false; glowMesh.renderOrder = -90;
  scene.add(glowMesh);

  // ------------------------------------------------------------ galaxy sample points
  const gal = M.decodeGalaxy();
  const galGeo = new THREE.BufferGeometry();
  {
    const n = gal.count, pos = new Float32Array(n * 3), col = new Float32Array(n * 3), w = new Float32Array(n);
    for (let i = 0; i < n; i++) {
      pos[i * 3] = gal.pos[i * 3]; pos[i * 3 + 1] = gal.pos[i * 3 + 1]; pos[i * 3 + 2] = -gal.pos[i * 3 + 2];
      col[i * 3] = gal.col[i * 3] / 255; col[i * 3 + 1] = gal.col[i * 3 + 1] / 255; col[i * 3 + 2] = gal.col[i * 3 + 2] / 255;
      const b = gal.bright[i] / 255, young = gal.comp[i] === 0 || gal.lvl[i] <= 1;
      w[i] = young ? 0.07 + 0.30 * b : 0.010 + 0.045 * b * b;
    }
    galGeo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    galGeo.setAttribute('aColor', new THREE.BufferAttribute(col, 3));
    galGeo.setAttribute('aW', new THREE.BufferAttribute(w, 1));
  }
  const galMat = new THREE.ShaderMaterial(Object.assign({}, additive, {
    uniforms: { uFade: { value: 0 }, uPR: { value: PR }, uSize: { value: 1.6 } },
    vertexShader: `
      uniform float uPR, uSize; attribute vec3 aColor; attribute float aW; varying vec3 vC; varying float vA;
      void main(){
        gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
        gl_PointSize = uSize * uPR * (0.9 + aW * 0.8);
        vC = mix(aColor, vec3(1.0), 0.15); vA = aW;
      }`,
    fragmentShader: `
      precision highp float; uniform float uFade; varying vec3 vC; varying float vA;
      void main(){
        vec2 p = gl_PointCoord - 0.5; float d = length(p) * 2.0;
        float a = (1.0 - smoothstep(0.35, 1.0, d)) * vA * uFade;
        gl_FragColor = vec4(vC * a, 1.0);
      }`,
  }));
  const galPoints = new THREE.Points(galGeo, galMat);
  galPoints.frustumCulled = false; galPoints.renderOrder = -80;
  scene.add(galPoints);

  // ------------------------------------------------------------ sector grid (1,024 ly cubes)
  const sectorMat = new THREE.ShaderMaterial(Object.assign({}, premul, {
    extensions: {},
    uniforms: { uFade: { value: 0 }, uY: { value: 0 }, uSize: { value: M.SECTOR_SIZE }, uAccent: { value: ACCENT }, uPR: { value: PR } },
    vertexShader: `
      uniform float uY; varying vec2 vG;
      void main(){ vG = position.xy * 24000.0; gl_Position = projectionMatrix * viewMatrix * vec4(vG.x, uY, -vG.y, 1.0); }`,
    fragmentShader: `
      precision highp float; uniform float uFade, uSize, uPR; uniform vec3 uAccent; varying vec2 vG;
      void main(){
        vec2 q = (vG + uSize * 0.5) / uSize;
        vec2 m = abs(fract(q + 0.5) - 0.5) * uSize;
        vec2 lw = fwidth(vG) * uPR * 0.75;
        vec2 l = 1.0 - smoothstep(lw * 0.5, lw * 0.5 + fwidth(vG), m);
        float line = max(l.x, l.y);
        float edge = 1.0 - smoothstep(9000.0, 12000.0, length(vG));
        bool home = abs(vG.x) < uSize * 0.5 && abs(vG.y) < uSize * 0.5;
        float a = line * (home ? 0.32 : 0.11) * edge * uFade;
        vec3 c = home ? uAccent : vec3(0.75, 0.82, 0.92);
        float fill = home ? 0.035 * uFade : 0.0;
        float px = length(fwidth(vG)) * 0.7071;
        float bubble = (1.0 - smoothstep(0.6 * px, 1.6 * px, abs(length(vG) - 150.0))) * step(0.5, fract(atan(vG.y, vG.x) * 24.0)) * 0.5 * uFade;
        a += bubble; c = mix(c, vec3(0.85, 0.9, 1.0), bubble);
        gl_FragColor = vec4(c * (a + fill), a + fill);
      }`,
  }));
  const sectorMesh = new THREE.Mesh(new THREE.PlaneGeometry(1, 1), sectorMat);
  sectorMesh.frustumCulled = false; sectorMesh.renderOrder = -70;
  scene.add(sectorMesh);

  // ------------------------------------------------------------ local ground: atlas grid, rings, reach
  const F = M.FIELD;
  const fieldTex = new THREE.DataTexture(new Uint8Array(F.size * F.size * 4), F.size, F.size, THREE.RGBAFormat, THREE.UnsignedByteType);
  fieldTex.magFilter = THREE.LinearFilter; fieldTex.minFilter = THREE.LinearFilter; fieldTex.needsUpdate = true;
  const groundMat = new THREE.ShaderMaterial(Object.assign({}, premul, {
    uniforms: {
      uField: { value: fieldTex }, uFieldMin: { value: F.min }, uFieldSpan: { value: F.size * F.res }, uRange: { value: F.range },
      uFade: { value: 1 }, uGridFade: { value: 1 }, uY: { value: 0 }, uPR: { value: PR },
      uHalf: { value: M.HALF }, uCell: { value: M.CELL }, uShipCell: { value: new THREE.Vector2(-1, -1) },
      uHoverCell: { value: new THREE.Vector2(-1, -1) }, uAccent: { value: ACCENT }, uRings: { value: 1 },
    },
    vertexShader: `
      uniform float uY, uFieldMin, uFieldSpan; varying vec2 vG;
      void main(){ vG = (position.xy + 0.5) * uFieldSpan + uFieldMin; gl_Position = projectionMatrix * viewMatrix * vec4(vG.x, uY, -vG.y, 1.0); }`,
    fragmentShader: `
      precision highp float;
      uniform sampler2D uField; uniform float uFieldMin, uFieldSpan, uRange, uFade, uGridFade, uPR, uHalf, uCell, uRings;
      uniform vec2 uShipCell, uHoverCell; uniform vec3 uAccent; varying vec2 vG;
      vec4 acc = vec4(0.0);
      void over(vec3 c, float a){ acc = vec4(c * a, a) + acc * (1.0 - a); }
      float lineAt(float d, float px, float wpx){ return 1.0 - smoothstep(wpx * 0.5 - 0.5, wpx * 0.5 + 0.5, abs(d) / px); }
      void main(){
        float px = length(fwidth(vG)) * 0.7071 / uPR;       // ly per CSS pixel here
        vec4 f = (texture2D(uField, (vG - uFieldMin) / uFieldSpan) * 2.0 - 1.0) * uRange;
        bool chart = abs(vG.x) < uHalf && abs(vG.y) < uHalf;
        // outside the tank's reach: a quiet veil; inside: a faint lift and a glow along the edge
        float inside = 1.0 - smoothstep(-px, px, f.a);
        over(vec3(0.0), (1.0 - inside) * 0.22);
        over(uAccent, inside * (0.035 + 0.075 * exp(f.a / 2.2)));
        // atlas grid
        if (chart) {
          vec2 m = abs(fract((vG + uHalf) / uCell + 0.5) - 0.5) * uCell;
          float g = max(lineAt(m.x, px, 1.0), lineAt(m.y, px, 1.0));
          over(vec3(0.80, 0.86, 0.95), g * mix(0.045, 0.085, inside) * uGridFade);
          vec2 cell = floor(vec2(vG.x + uHalf, uHalf - vG.y) / uCell);
          if (cell == uHoverCell) over(vec3(1.0), 0.035 * uGridFade);
          if (cell == uShipCell) {
            over(uAccent, 0.07 * uGridFade);
            vec2 e = abs(vec2(vG.x + uHalf, uHalf - vG.y) - (cell + 0.5) * uCell);
            float edge = max(lineAt(e.x - uCell * 0.5, px, 1.4) * step(e.y, uCell * 0.5), lineAt(e.y - uCell * 0.5, px, 1.4) * step(e.x, uCell * 0.5));
            over(uAccent, edge * 0.55 * uGridFade);
          }
        }
        float border = max(lineAt(abs(vG.x) - uHalf, px, 1.0) * step(abs(vG.y), uHalf), lineAt(abs(vG.y) - uHalf, px, 1.0) * step(abs(vG.x), uHalf));
        over(vec3(0.80, 0.86, 0.95), border * 0.16 * uGridFade);
        // the tank's edge and the 1, 2, 3 jump rings
        over(uAccent, lineAt(f.a, px, 1.2) * 0.42);
        over(uAccent, lineAt(f.b, px, 1.3) * 0.40 * uRings);
        over(uAccent, lineAt(f.g, px, 1.3) * 0.55 * uRings);
        over(uAccent, lineAt(f.r, px, 1.5) * 0.80 * uRings);
        gl_FragColor = acc * uFade;
      }`,
  }));
  const groundMesh = new THREE.Mesh(new THREE.PlaneGeometry(1, 1), groundMat);
  groundMesh.frustumCulled = false; groundMesh.renderOrder = -60;
  scene.add(groundMesh);

  // ------------------------------------------------------------ danger spheres
  const dangerMeshes = M.DANGER.map((d, i) => {
    const hazard = i === M.STORM;
    const mat = new THREE.ShaderMaterial(Object.assign({}, premul, {
      uniforms: { uDepth: { value: 1 }, uFade: { value: 1 }, uPR: { value: PR }, uColor: { value: hazard ? new THREE.Color(1.0, 0.70, 0.32) : new THREE.Color(1.0, 0.36, 0.40) }, uHatch: { value: hazard ? 1 : 0 } },
      vertexShader: `
        varying vec3 vN; varying vec3 vV;
        void main(){ vec4 mv = modelViewMatrix * vec4(position, 1.0); vN = normalize(normalMatrix * normal); vV = normalize(-mv.xyz); gl_Position = projectionMatrix * mv; }`,
      fragmentShader: `
        precision highp float; uniform float uFade, uPR, uHatch, uDepth; uniform vec3 uColor; varying vec3 vN; varying vec3 vV;
        void main(){
          float ndv = abs(dot(normalize(vN), normalize(vV)));
          float rim = pow(1.0 - ndv, 3.0);
          float edge = smoothstep(0.82, 0.98, 1.0 - ndv);
          float hatch = step(0.62, fract((gl_FragCoord.x - gl_FragCoord.y) / (9.0 * uPR)));
          float a = (0.028 + 0.16 * rim + 0.5 * edge + uHatch * hatch * 0.05) * uFade * uDepth;
          gl_FragColor = vec4(uColor * a, a);
        }`,
    }));
    const mesh = new THREE.Mesh(new THREE.SphereGeometry(1, 64, 40), mat);
    mesh.position.set(...T(d.centre[0], d.centre[1], d.centre[2]));
    mesh.scale.setScalar(d.radiusLy);
    mesh.renderOrder = -50;
    scene.add(mesh);
    return mesh;
  });

  // ------------------------------------------------------------ local stars
  const N = M.N;
  const starGeo = new THREE.BufferGeometry();
  const aReach = new Float32Array(N), aHl = new Float32Array(N), aFlags = new Float32Array(N);
  {
    const pos = new Float32Array(N * 3), col = new Float32Array(N * 3), size = new Float32Array(N);
    const c = new THREE.Color();
    for (let s = 0; s < N; s++) {
      pos[s * 3] = M.X[s]; pos[s * 3 + 1] = M.Y[s]; pos[s * 3 + 2] = -M.Z[s];
      c.set(M.COL[s]);
      col[s * 3] = c.r; col[s * 3 + 1] = c.g; col[s * 3 + 2] = c.b;
      const mag = M.MAG[s] > 25 ? 11.5 : M.MAG[s];
      size[s] = Math.max(1.55, Math.min(3.9, 3.35 - 0.105 * mag));
    }
    starGeo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    starGeo.setAttribute('aColor', new THREE.BufferAttribute(col, 3));
    starGeo.setAttribute('aSize', new THREE.BufferAttribute(size, 1));
    starGeo.setAttribute('aFlags', new THREE.BufferAttribute(aFlags, 1));
    starGeo.setAttribute('aReach', new THREE.BufferAttribute(aReach, 1));
    starGeo.setAttribute('aHl', new THREE.BufferAttribute(aHl, 1));
  }
  const starMat = new THREE.ShaderMaterial(Object.assign({}, premul, {
    uniforms: {
      uPR: { value: PR }, uScale: { value: 1 }, uGlyph: { value: 1 }, uDetail: { value: 0 }, uRefY: { value: 0 },
      uFade: { value: 1 }, uFilter: { value: 0 }, uTilt: { value: 0 }, uCamDist: { value: 1 }, uAccent: { value: ACCENT },
    },
    vertexShader: `
      uniform float uPR, uScale, uGlyph, uDetail, uRefY, uFade, uFilter, uTilt, uCamDist;
      attribute vec3 aColor; attribute float aSize, aFlags, aReach, aHl;
      varying vec3 vColor; varying float vSize, vR, vAlpha, vFlags, vReach, vDy, vHl, vGlow;
      void main(){
        vec4 mv = modelViewMatrix * vec4(position, 1.0);
        gl_Position = projectionMatrix * mv;
        float dy = position.y - uRefY;
        bool inTank = aReach > -0.5;
        float persp = mix(1.0, clamp(uCamDist / max(-mv.z, 1.0), 0.45, 1.8), uTilt);
        float r = aSize * uScale * (inTank ? 1.0 : 0.72) * persp;
        int f = int(aFlags + 0.5);
        bool glyph = (f & 6) != 0 && uGlyph > 0.01;
        bool detail = uDetail > 0.01 && (inTank || (f & 8) != 0);
        float pad = (glyph || detail || (inTank && aReach < 3.5) || (uFilter > 0.5 && aHl > 0.5)) ? 10.0 : 2.0;
        vGlow = smoothstep(2.9, 3.8, aSize) * (inTank ? 1.0 : 0.4);
        pad = max(pad, r * 1.4 * vGlow);
        vR = r * uPR; vSize = 2.0 * (r + pad) * uPR; gl_PointSize = vSize;
        vColor = inTank ? aColor : mix(vec3(0.46, 0.51, 0.58), aColor, 0.22);
        float depth = mix(1.0, 0.38, smoothstep(16.0, 80.0, abs(dy)));
        float a = uFade * depth * (inTank ? 1.0 : 0.34);
        if (uFilter > 0.5) a *= aHl > 0.5 ? 1.15 : 0.16;
        vAlpha = a; vFlags = aFlags; vReach = aReach; vDy = dy; vHl = aHl;
      }`,
    fragmentShader: `
      precision highp float;
      uniform float uPR, uGlyph, uDetail, uFilter, uTilt; uniform vec3 uAccent;
      varying vec3 vColor; varying float vSize, vR, vAlpha, vFlags, vReach, vDy, vHl, vGlow;
      vec4 acc = vec4(0.0);
      void over(vec3 c, float a){ acc = vec4(c * a, a) + acc * (1.0 - a); }
      float disc(float d, float r){ return 1.0 - smoothstep(r - 0.7, r + 0.7, d); }
      float ring(float d, float r, float w){ return 1.0 - smoothstep(w * 0.5 - 0.6, w * 0.5 + 0.6, abs(d - r)); }
      void main(){
        vec2 p = (gl_PointCoord - 0.5) * vSize; p.y = -p.y;
        float d = length(p);
        int f = int(vFlags + 0.5);
        bool scoop = (f & 1) != 0, station = (f & 2) != 0, danger = (f & 4) != 0, land = (f & 8) != 0, hostile = (f & 16) != 0;
        bool inTank = vReach > -0.5;
        float r = vR, u = uPR;
        // soft halo for the bright ones
        if (vGlow > 0.0) over(vColor, exp(-d * d / (2.0 * pow(r * 1.7, 2.0))) * 0.42 * vGlow);
        // danger: a tinted disc with a thin ring
        if (danger && uGlyph > 0.01) {
          vec3 dc = hostile ? vec3(1.0, 0.36, 0.40) : vec3(1.0, 0.70, 0.32);
          over(dc, disc(d, r + 6.0 * u) * 0.16 * uGlyph);
          over(dc, ring(d, r + 6.0 * u, 1.4 * u) * 0.95 * uGlyph);
        }
        // height tick: up when the star is above your plane, down when below (flat view only)
        bool ticked = inTank && (vReach < 2.5 || uDetail > 0.5) && abs(vDy) > 1.5;
        if (ticked) {
          float s = sign(vDy);
          float len = clamp(1.4 + abs(vDy) * 0.17, 2.0, 5.5) * u;
          float y0 = r + 1.6 * u, y1 = y0 + len;
          float py = p.y * s;
          float dd = py < y0 ? length(vec2(p.x, py - y0)) : py > y1 ? length(vec2(p.x, py - y1)) : abs(p.x);
          over(mix(vColor, vec3(1.0), 0.35), (1.0 - smoothstep(0.55 * u - 0.5, 0.55 * u + 0.5, dd)) * 0.7 * (1.0 - uTilt));
        }
        // core: filled = you can scoop fuel here, hollow = no fuel
        if (scoop) over(vColor, disc(d, r));
        else if (inTank && (vReach < 3.5 || uDetail > 0.5)) { over(vColor, disc(d, r) * 0.16); over(vColor, ring(d, r - 0.7 * u, 1.35 * u)); }
        else over(vColor, disc(d, r) * 0.55);
        // station: a rounded square around the star
        if (station && uGlyph > 0.01) {
          float h = r + 3.6 * u, cr = 2.0 * u;
          vec2 q = abs(p) - vec2(h - cr);
          float sd = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - cr;
          over(uAccent, (1.0 - smoothstep(0.75 * u - 0.6, 0.75 * u + 0.6, abs(sd))) * uGlyph);
        }
        // landable worlds: a small moon at the lower right
        if (land && uDetail > 0.01) {
          vec2 c = vec2(1.0, -1.0) * (r + 2.4 * u) * 0.78;
          over(vec3(0.88, 0.93, 1.0), disc(length(p - c), 1.45 * u) * 0.9 * uDetail * (inTank ? 1.0 : 0.6));
        }
        // finder match
        vec4 o = acc * vAlpha;
        if (o.a < 0.004) discard;
        gl_FragColor = o;
      }`,
  }));
  const starPoints = new THREE.Points(starGeo, starMat);
  starPoints.frustumCulled = false; starPoints.renderOrder = -20;
  scene.add(starPoints);

  // ------------------------------------------------------------ drop lines (3D view only)
  const dropGeo = new THREE.BufferGeometry();
  {
    const pos = new Float32Array(N * 6), foot = new Float32Array(N * 2), reach = new Float32Array(N * 2);
    for (let s = 0; s < N; s++) {
      for (let k = 0; k < 2; k++) {
        pos[s * 6 + k * 3] = M.X[s]; pos[s * 6 + k * 3 + 1] = M.Y[s]; pos[s * 6 + k * 3 + 2] = -M.Z[s];
        foot[s * 2 + k] = k;
      }
    }
    dropGeo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    dropGeo.setAttribute('aFoot', new THREE.BufferAttribute(foot, 1));
    dropGeo.setAttribute('aReach', new THREE.BufferAttribute(reach, 1));
  }
  const dropMat = new THREE.ShaderMaterial(Object.assign({}, premul, {
    uniforms: { uRefY: { value: 0 }, uTilt: { value: 0 }, uFade: { value: 1 } },
    vertexShader: `
      uniform float uRefY, uTilt, uFade; attribute float aFoot, aReach; varying float vA;
      void main(){
        vec3 p = position; if (aFoot > 0.5) p.y = uRefY;
        gl_Position = projectionMatrix * modelViewMatrix * vec4(p, 1.0);
        vA = (aReach > -0.5 && aReach < 3.5 ? (aFoot > 0.5 ? 0.06 : 0.42) : 0.0) * uTilt * uFade;
      }`,
    fragmentShader: `precision highp float; varying float vA; void main(){ if (vA < 0.002) discard; gl_FragColor = vec4(vec3(0.75, 0.86, 1.0) * vA, vA); }`,
  }));
  const dropLines = new THREE.LineSegments(dropGeo, dropMat);
  dropLines.frustumCulled = false; dropLines.renderOrder = -30;
  scene.add(dropLines);

  // ------------------------------------------------------------ route ribbon (instanced screen-space quads)
  const ribbonMat = new THREE.ShaderMaterial(Object.assign({}, premul, {
    uniforms: { uViewport: { value: new THREE.Vector2(1, 1) }, uPR: { value: PR }, uProgress: { value: -1 }, uAccent: { value: ACCENT }, uFade: { value: 1 }, uWidth: { value: 3.2 } },
    vertexShader: `
      uniform vec2 uViewport; uniform float uPR, uWidth;
      attribute vec3 aA, aB; attribute float aDA, aDB;
      varying float vAcross, vDist, vHalf;
      void main(){
        vec4 ca = projectionMatrix * viewMatrix * vec4(aA, 1.0);
        vec4 cb = projectionMatrix * viewMatrix * vec4(aB, 1.0);
        vec2 sa = ca.xy / ca.w * 0.5 * uViewport, sb = cb.xy / cb.w * 0.5 * uViewport;
        vec2 dir = sb - sa; float len = length(dir); dir = len > 1e-4 ? dir / len : vec2(1.0, 0.0);
        vec2 nrm = vec2(-dir.y, dir.x);
        float hw = (uWidth * 0.5 + 2.2) * uPR;
        vec4 c = mix(ca, cb, position.x);
        vec2 s = mix(sa, sb, position.x) + nrm * position.y * (hw + 1.0);
        gl_Position = vec4(s / (0.5 * uViewport) * c.w, c.z, c.w);
        vAcross = position.y * (hw + 1.0); vHalf = hw; vDist = mix(aDA, aDB, position.x);
      }`,
    fragmentShader: `
      precision highp float; uniform float uPR, uProgress, uFade, uWidth; uniform vec3 uAccent;
      varying float vAcross, vDist, vHalf;
      void main(){
        float a = abs(vAcross);
        float core = 1.0 - smoothstep(uWidth * 0.5 * uPR - 0.6, uWidth * 0.5 * uPR + 0.6, a);
        float casing = 1.0 - smoothstep(vHalf - 0.8, vHalf + 0.4, a);
        bool done = vDist < uProgress;
        vec3 c = done ? vec3(0.93, 0.97, 1.0) : uAccent;
        vec4 o = vec4(vec3(0.0), casing * 0.55);
        o = vec4(c * core, core) + o * (1.0 - core);
        gl_FragColor = o * uFade;
      }`,
  }));
  let ribbon = null;
  function setRoute(route) {
    if (ribbon) { scene.remove(ribbon); ribbon.geometry.dispose(); ribbon = null; }
    if (!route) return;
    const n = route.path.length - 1;
    const g = new THREE.InstancedBufferGeometry();
    const corner = new Float32Array([0, -1, 0, 1, -1, 0, 0, 1, 0, 1, 1, 0]);
    g.setAttribute('position', new THREE.BufferAttribute(corner, 3));
    g.setIndex([0, 1, 2, 2, 1, 3]);
    const A = new Float32Array(n * 3), B = new Float32Array(n * 3), DA = new Float32Array(n), DB = new Float32Array(n);
    for (let i = 0; i < n; i++) {
      const a = route.path[i], b = route.path[i + 1];
      A.set(T(M.X[a], M.Y[a], M.Z[a]), i * 3); B.set(T(M.X[b], M.Y[b], M.Z[b]), i * 3);
      DA[i] = route.stops[i].total; DB[i] = route.stops[i + 1].total;
    }
    g.setAttribute('aA', new THREE.InstancedBufferAttribute(A, 3));
    g.setAttribute('aB', new THREE.InstancedBufferAttribute(B, 3));
    g.setAttribute('aDA', new THREE.InstancedBufferAttribute(DA, 1));
    g.setAttribute('aDB', new THREE.InstancedBufferAttribute(DB, 1));
    g.instanceCount = n;
    ribbon = new THREE.Mesh(g, ribbonMat);
    ribbon.frustumCulled = false; ribbon.renderOrder = -10;
    scene.add(ribbon);
  }

  // ------------------------------------------------------------ state uploads
  function setReach(reach, fieldBytes) {
    for (let s = 0; s < N; s++) aReach[s] = reach.cost[s] >= 0 ? reach.hops[s] : -1;
    starGeo.attributes.aReach.needsUpdate = true;
    const dr = dropGeo.attributes.aReach.array;
    for (let s = 0; s < N; s++) dr[s * 2] = dr[s * 2 + 1] = aReach[s];
    dropGeo.attributes.aReach.needsUpdate = true;
    fieldTex.image.data.set(fieldBytes);
    fieldTex.needsUpdate = true;
  }
  function setFlags(favourites) {
    for (let s = 0; s < N; s++) {
      let f = 0;
      if (M.canScoop(s)) f |= 1;
      if (M.stationsBySys.has(s)) f |= 2;
      if (M.dangerOf[s] >= 0) f |= 4;
      if (M.landable(s) > 0) f |= 8;
      if (M.dangerOf[s] >= 0 && M.dangerOf[s] !== M.STORM) f |= 16;
      aFlags[s] = f;
    }
    starGeo.attributes.aFlags.needsUpdate = true;
  }
  function setHighlight(matchSet) {
    if (matchSet) { for (let s = 0; s < N; s++) aHl[s] = matchSet.has(s) ? 1 : 0; }
    starMat.uniforms.uFilter.value = matchSet ? 1 : 0;
    starGeo.attributes.aHl.needsUpdate = true;
  }

  // ------------------------------------------------------------ camera
  // The view is a centre on the plane (galactic X, Z), a width in ly across the window, a tilt (0 = flat,
  // straight down; 1 = the 3D view) and a yaw. Flat uses a 0.3 degree lens from far away, which is
  // orthographic to within a percent, so the 3D view can open out of it without a jump.
  const view = { cx: 0, cz: 0, w: 150, tilt: 0, yaw: 0, refY: 0 };
  const pv = new Float64Array(16);
  const inv = new THREE.Matrix4();
  const tmpM = new THREE.Matrix4();
  let camDist = 1;
  function easeTilt(t) { return t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2; }
  function applyCamera() {
    const t = easeTilt(Math.max(0, Math.min(1, view.tilt)));
    const aspect = VW / VH;
    const fov = 0.3 + (40 - 0.3) * t;
    const viewH = view.w / aspect;
    const pitch = (90 - (90 - 50) * t) * Math.PI / 180;
    const D = (viewH / 2) / Math.tan((fov / 2) * Math.PI / 180) * (1 + 0.25 * t);
    camDist = D;
    camera.fov = fov; camera.aspect = aspect;
    camera.near = Math.max(0.05, D * (t > 0.01 ? 0.02 : 0.5));
    camera.far = D + Math.max(4000, view.w * 6);
    const target = new THREE.Vector3(view.cx, view.refY, -view.cz);
    const off = new THREE.Vector3(0, D * Math.sin(pitch), D * Math.cos(pitch)).applyAxisAngle(new THREE.Vector3(0, 1, 0), view.yaw);
    camera.position.copy(target).add(off);
    camera.rotation.set(-pitch, view.yaw, 0, 'YXZ');
    camera.updateProjectionMatrix();
    camera.updateMatrixWorld(true);
    tmpM.multiplyMatrices(camera.projectionMatrix, camera.matrixWorldInverse);
    pv.set(tmpM.elements);
    inv.copy(tmpM).invert();
    return t;
  }
  // galactic (x, y, z) -> CSS pixels; returns null behind the camera
  function project(x, y, z, out) {
    const tz = -z, e = pv;
    const w = e[3] * x + e[7] * y + e[11] * tz + e[15];
    if (w <= 1e-9) return null;
    const cx = (e[0] * x + e[4] * y + e[8] * tz + e[12]) / w, cy = (e[1] * x + e[5] * y + e[9] * tz + e[13]) / w;
    out = out || [0, 0];
    out[0] = (cx * 0.5 + 0.5) * VW; out[1] = (0.5 - cy * 0.5) * VH;
    return out;
  }
  // CSS pixel -> galactic (X, Z) on the plane y = refY (null if the ray misses)
  const v1 = new THREE.Vector3(), v2 = new THREE.Vector3();
  function groundAt(sx, sy, y) {
    const nx = (sx / VW) * 2 - 1, ny = 1 - (sy / VH) * 2;
    v1.set(nx, ny, -1).applyMatrix4(inv); v2.set(nx, ny, 1).applyMatrix4(inv);
    const py = y === undefined ? view.refY : y;
    const dy = v2.y - v1.y;
    if (Math.abs(dy) < 1e-12) return null;
    const k = (py - v1.y) / dy;
    if (k < 0) return null;
    return [v1.x + (v2.x - v1.x) * k, -(v1.z + (v2.z - v1.z) * k)];
  }

  function resize() {
    PR = Math.min(2, window.devicePixelRatio || 1);
    VW = host.clientWidth || window.innerWidth; VH = host.clientHeight || window.innerHeight;
    renderer.setPixelRatio(PR);
    renderer.setSize(VW, VH, false);
    renderer.domElement.style.width = VW + 'px'; renderer.domElement.style.height = VH + 'px';
    for (const m of [galMat, sectorMat, groundMat, starMat, ribbonMat, ...dangerMeshes.map(d => d.material)]) if (m.uniforms.uPR) m.uniforms.uPR.value = PR;
    bg.material.uniforms.uRes.value.set(VW, VH);
    ribbonMat.uniforms.uViewport.value.set(VW * PR, VH * PR);
  }

  // Layer opacities follow the zoom: the neighbourhood (grid, rings, glyphs) up close, sector names in the
  // middle, the galaxy far out.
  const smooth = (a, b, x) => { const t = Math.max(0, Math.min(1, (x - a) / (b - a))); return t * t * (3 - 2 * t); };
  function layerFades(w) {
    return {
      local: 1 - smooth(1400, 5200, w),
      grid: 1 - smooth(320, 620, w),
      rings: 1 - smooth(450, 900, w),
      glyph: 1 - smooth(260, 520, w),
      detail: 1 - smooth(70, 110, w),
      sector: smooth(500, 1300, w) * (1 - smooth(16000, 32000, w)),
      galaxy: smooth(1300, 16000, w),
      stars: 1 - smooth(2600, 9000, w),
    };
  }
  function render(state) {
    const t = applyCamera();
    const w = view.w, fades = layerFades(w);
    const scale = Math.max(0.5, Math.min(1.55, Math.pow(150 / w, 0.3)));
    const u = starMat.uniforms;
    u.uScale.value = scale; u.uGlyph.value = fades.glyph; u.uDetail.value = fades.detail; u.uRefY.value = view.refY;
    u.uFade.value = fades.stars; u.uTilt.value = t; u.uCamDist.value = camDist;
    groundMat.uniforms.uFade.value = fades.rings; groundMat.uniforms.uGridFade.value = fades.grid; groundMat.uniforms.uY.value = view.refY;
    groundMesh.visible = fades.rings > 0.001;
    sectorMat.uniforms.uFade.value = fades.sector; sectorMat.uniforms.uY.value = view.refY; sectorMesh.visible = fades.sector > 0.001;
    glowMat.uniforms.uFade.value = fades.galaxy; glowMesh.visible = fades.galaxy > 0.001;
    galMat.uniforms.uFade.value = fades.galaxy; galPoints.visible = fades.galaxy > 0.001;
    galMat.uniforms.uSize.value = 1.1 + 0.3 * smooth(20000, 120000, w);
    dropMat.uniforms.uRefY.value = view.refY; dropMat.uniforms.uTilt.value = t; dropMat.uniforms.uFade.value = fades.rings; dropLines.visible = t > 0.01;
    dangerMeshes.forEach((d, i) => { const dy = Math.abs(M.DANGER[i].centre[1] - view.refY); d.material.uniforms.uDepth.value = 1 - 0.45 * smooth(8, 50, dy); d.material.uniforms.uFade.value = fades.glyph; d.visible = fades.glyph > 0.001; });
    starPoints.visible = fades.stars > 0.001;
    ribbonMat.uniforms.uFade.value = Math.max(0.0, 1 - smooth(900, 2400, w));
    ribbonMat.uniforms.uProgress.value = state && state.routeProgress !== undefined ? state.routeProgress : -1;
    renderer.render(scene, camera);
    return fades;
  }
  function setShipCell(cell) { groundMat.uniforms.uShipCell.value.set(cell ? cell.i : -1, cell ? cell.j : -1); }
  function setHoverCell(cell) { groundMat.uniforms.uHoverCell.value.set(cell ? cell.i : -1, cell ? cell.j : -1); }

  resize();
  return {
    renderer, camera, view, resize, render, project, groundAt, layerFades, setReach, setFlags, setHighlight, setRoute,
    setShipCell, setHoverCell, get width() { return VW; }, get height() { return VH; }, get camDist() { return camDist; },
  };
};
