/* Hollow Universe map, concept Lens: the magnifier.
 *
 * A circular lens follows the cursor on a short leash: sweep and it travels with you, slow down and it settles, so
 * the stars inside hold still while you pick. Inside it the star cloud is spread apart by a smooth fisheye
 * (magnification m at the centre, easing to 1 at the rim, so everything under the glass stays visible) and drawn
 * one star at a time, with height stalks to the plane through your ship and each star's owner.
 *
 *   glass   (WebGL, a screen quad drawn after the context): dark glass, and on the plane through your ship the crisp
 *           territory (borders, contested hatching), the distance rings, the jump bubble and danger shells, all ray-cast
 *           through the inverse lens mapping so they line up with the stars;
 *   stars   (2D canvas, crisp at any DPR): stalks, star glyphs, owner rings, the route through the lens;
 *   ring    (HTML): the lens edge and its bezel readout (whose space you are looking at).
 *
 *   const lens = HollowLens.create({ THREE, scene, canvas, ring, bezel, sdf })
 *   lens.vis, lens.cx, lens.cy, lens.r, lens.m       read by stars.js and app.js
 *   lens.map(x, y, out) / lens.unmap(x, y, out)      screen point -> where the lens shows it (and back)
 *   lens.update(dt, opts); lens.draw(opts)           every frame (see app.js)
 */
(function () {
  'use strict';
  window.HollowLens = { create };

  function create({ THREE, scene, canvas, ring, bezel, sdf, colors, dpr }) {
    const clamp = (v, a, b) => v < a ? a : v > b ? b : v;
    const L = {
      vis: false, a: 0, cx: -999, cy: -999, r: 120, m: 2.4, locked: false, lockI: -1, leash: 0.5,
      map, unmap, update, draw, resize, setRadius, inside,
    };
    // fisheye: f(x) = m x / sqrt(1 + (m^2 - 1) x^2) on x = distance / radius; f(0)' = m, f(1) = 1
    const f = (x, m) => m * x / Math.sqrt(1 + (m * m - 1) * x * x);
    const finv = (y, m) => y / Math.sqrt(m * m - y * y * (m * m - 1));
    function map(px, py, out) {
      const dx = px - L.cx, dy = py - L.cy, d = Math.hypot(dx, dy);
      if (d >= L.r || d < 1e-6) { out.x = px; out.y = py; return out; }
      const s = L.r * f(d / L.r, L.m) / d; out.x = L.cx + dx * s; out.y = L.cy + dy * s; return out;
    }
    function unmap(px, py, out) {
      const dx = px - L.cx, dy = py - L.cy, d = Math.hypot(dx, dy);
      if (d >= L.r || d < 1e-6) { out.x = px; out.y = py; return out; }
      const s = L.r * finv(d / L.r, L.m) / d; out.x = L.cx + dx * s; out.y = L.cy + dy * s; return out;
    }
    function inside(px, py, pad = 0) { return L.vis && Math.hypot(px - L.cx, py - L.cy) < L.r - pad; }
    function setRadius(r) { L.r = clamp(r, 70, 260); }

    // ---------- glass (WebGL) ----------
    const MARGIN = 26;
    const u = {
      uC: { value: new THREE.Vector2() }, uR: { value: 120 }, uM: { value: 2 }, uA: { value: 0 }, uMargin: { value: MARGIN },
      uView: { value: new THREE.Vector2(1, 1) }, uCam: { value: new THREE.Vector3() }, uD0: { value: new THREE.Vector3() },
      uDx: { value: new THREE.Vector3() }, uDy: { value: new THREE.Vector3() }, uPlaneY: { value: 0 },
      uSdf: { value: sdf ? sdf.tex : null }, uHalf: { value: sdf ? sdf.half : 160 }, uHasSdf: { value: sdf ? 1 : 0 },
      uC0: { value: new THREE.Color(colors[0] || '#000') }, uC1: { value: new THREE.Color(colors[1] || '#000') },
      uC2: { value: new THREE.Color(colors[2] || '#000') }, uC3: { value: new THREE.Color(colors[3] || '#000') },
      uShip: { value: new THREE.Vector3() }, uRange: { value: 12.5 }, uAcc: { value: new THREE.Color('#5fe3c0') },
      uDz0: { value: new THREE.Vector4(0, 0, 0, 0) }, uDz1: { value: new THREE.Vector4(0, 0, 0, 0) },
      uDc0: { value: new THREE.Color('#ff6b5e') }, uDc1: { value: new THREE.Color('#ffb05e') },
    };
    const mat = new THREE.ShaderMaterial({
      uniforms: u,
      vertexShader: `
        uniform vec2 uC, uView; uniform float uR, uMargin; varying vec2 vP;
        void main() {
          float e = uR + uMargin;
          vP = vec2(position.x, -position.y) * e;                       // CSS px from the lens centre, y down
          vec2 p = uC + vP;
          gl_Position = vec4(p.x / uView.x * 2.0 - 1.0, 1.0 - p.y / uView.y * 2.0, 0.0, 1.0);
        }`,
      fragmentShader: `
        uniform vec2 uC, uView; uniform float uR, uM, uA, uMargin, uPlaneY, uHalf, uHasSdf, uRange;
        uniform vec3 uCam, uD0, uDx, uDy, uShip, uC0, uC1, uC2, uC3, uAcc, uDc0, uDc1;
        uniform vec4 uDz0, uDz1; uniform sampler2D uSdf;
        varying vec2 vP;
        float finv(float y, float m) { return y / sqrt(m * m - y * y * (m * m - 1.0)); }
        float inside(float d) { float w = fwidth(d); return 1.0 - smoothstep(-w, w, d); }
        float edge(float d, float px) { float w = fwidth(d) * px; return 1.0 - smoothstep(0.0, w, abs(d)); }
        vec4 layer(float d, vec3 c) {
          float i = inside(d), rim = exp(min(d, 0.0) / 1.8);
          float a = i * (0.10 + 0.20 * rim) + edge(d, 1.5) * 0.95;
          return vec4(c * a, a);
        }
        vec3 shell(vec3 dir, vec4 s, vec3 c) {
          if (s.w <= 0.0) return vec3(0.0);
          vec3 oc = s.xyz - uCam; float tca = dot(oc, dir); float d2 = dot(oc, oc) - tca * tca; float r2 = s.w * s.w;
          if (tca <= 0.0 || d2 >= r2) return vec3(0.0);
          float q = sqrt(d2 / r2), fr = 1.0 - sqrt(max(0.0, 1.0 - q * q));
          return c * (pow(fr, 3.0) * 0.42 + 0.035);
        }
        void main() {
          float d = length(vP);
          if (d > uR + uMargin) discard;
          if (d > uR) { float s = 1.0 - smoothstep(uR, uR + uMargin, d); gl_FragColor = vec4(0.0, 0.0, 0.0, 0.5 * s * s * uA); return; }
          float x = d / uR, xo = x > 1e-4 ? finv(x, uM) : 0.0;
          vec2 q = uC + (x > 1e-4 ? vP * (xo / x) : vec2(0.0));        // the screen point the lens shows here
          vec2 ndc = vec2(q.x / uView.x * 2.0 - 1.0, 1.0 - q.y / uView.y * 2.0);
          vec3 dir = normalize(uD0 + ndc.x * uDx + ndc.y * uDy);
          vec3 col = vec3(0.032, 0.046, 0.068) + vec3(0.012, 0.018, 0.026) * (1.0 - x * x);
          float t = (uPlaneY - uCam.y) / dir.y;
          if (t > 0.0) {
            vec3 h = uCam + dir * t;
            // territory on the plane through your ship: crisp borders, every owner in its colour
            if (uHasSdf > 0.5) {
              vec2 uv = vec2((h.x + uHalf) / (2.0 * uHalf), (uHalf - h.z) / (2.0 * uHalf));
              float fade = 1.0 - smoothstep(118.0, 158.0, length(h.xz));
              if (fade > 0.0 && uv.x > 0.0 && uv.x < 1.0 && uv.y > 0.0 && uv.y < 1.0) {
                vec4 s = texture2D(uSdf, uv);
                vec4 acc = layer(s.r, uC0) * 1.15 + layer(s.g, uC1) + layer(s.b, uC2) + layer(s.a, uC3);
                float both = inside(s.r) * inside(s.g);
                float hatch = step(0.5, fract((h.x + h.z) * 0.32 * uM));
                acc.rgb += both * mix(uC0, uC1, hatch) * 0.28;
                col += acc.rgb * fade;
              }
            }
            // distance rings around your ship
            float r = length(h.xz - uShip.xz);
            float rings = max(max(edge(r - 10.0, 1.1), edge(r - 25.0, 1.1)), max(edge(r - 50.0, 1.1), edge(r - 100.0, 1.1)));
            col += vec3(0.80, 0.85, 0.92) * rings * 0.16 * (1.0 - smoothstep(105.0, 158.0, r));
          }
          col += shell(dir, vec4(uShip, uRange), uAcc);
          col += shell(dir, uDz0, uDc0) + shell(dir, uDz1, uDc1);
          col *= 1.0 - 0.35 * smoothstep(0.7, 1.0, x);                   // the compressed rim reads as the glass's edge
          gl_FragColor = vec4(col, 0.95 * uA);
        }`,
      transparent: true, depthTest: false, depthWrite: false,
    });
    const quad = new THREE.Mesh(new THREE.PlaneGeometry(2, 2), mat);
    quad.frustumCulled = false; quad.renderOrder = 90; quad.visible = false; scene.add(quad);
    const v0 = new THREE.Vector3(), v1 = new THREE.Vector3(), v2 = new THREE.Vector3();

    // ---------- 2D canvas for the stars inside ----------
    const ctx = canvas.getContext('2d');
    let W = 1, H = 1, DPR = dpr || 1, dirty = null;
    function resize(w, h, d) { W = w; H = h; DPR = d; canvas.width = Math.round(w * d); canvas.height = Math.round(h * d); canvas.style.width = w + 'px'; canvas.style.height = h + 'px'; dirty = null; }
    const sprites = new Map();
    function halo(r, g, b) {
      const key = ((r * 15) | 0) + ',' + ((g * 15) | 0) + ',' + ((b * 15) | 0);
      let s = sprites.get(key);
      if (!s) {
        s = document.createElement('canvas'); s.width = s.height = 64;
        const x = s.getContext('2d'), gr = x.createRadialGradient(32, 32, 0, 32, 32, 32), c = `${Math.round(r * 255)},${Math.round(g * 255)},${Math.round(b * 255)}`;
        gr.addColorStop(0, `rgba(${c},0.9)`); gr.addColorStop(0.22, `rgba(${c},0.32)`); gr.addColorStop(0.55, `rgba(${c},0.07)`); gr.addColorStop(1, `rgba(${c},0)`);
        x.fillStyle = gr; x.fillRect(0, 0, 64, 64); sprites.set(key, s);
      }
      return s;
    }
    const rgbStr = (c, a) => `rgba(${Math.round(c[0] * 255)},${Math.round(c[1] * 255)},${Math.round(c[2] * 255)},${a})`;

    // ---------- follow, lock, fade, magnification ----------
    function update(dt, o) {
      const want = o.allowed && (L.locked ? !!o.lockXY : o.mouseIn);
      if (L.locked && o.lockXY) { L.cx = o.lockXY[0]; L.cy = o.lockXY[1]; }
      else if (!L.locked && o.mouseIn && o.allowed) {
        if (L.a < 0.05 || L.cx < -500) { L.cx = o.mx; L.cy = o.my; }
        else if (!o.frozen) {
          // the leash: inside it the lens holds still (so you can aim); past it the lens is pulled along
          const dx = o.mx - L.cx, dy = o.my - L.cy, d = Math.hypot(dx, dy), lim = L.leash * L.r;
          if (d > lim) { L.cx += dx * (1 - lim / d); L.cy += dy * (1 - lim / d); }
        }
      }
      L.a += ((want ? 1 : 0) - L.a) * (1 - Math.exp(-dt * 14));
      if (!want && L.a < 0.01) L.a = 0;
      L.vis = L.a > 0.02;
      // magnification: spread the patch so its stars sit about SPACING px apart, whatever the zoom or density
      const dens = (o.count || 0) / (Math.PI * L.r * L.r);
      const goal = clamp(36 * Math.sqrt(dens), 1.6, 5);
      L.m += (goal - L.m) * (1 - Math.exp(-dt * (L.a < 0.05 ? 50 : 4)));
      // glass uniforms
      quad.visible = L.vis;
      if (!L.vis) return;
      const cam = o.camera;
      u.uC.value.set(L.cx, L.cy); u.uR.value = L.r; u.uM.value = L.m; u.uA.value = L.a * o.alpha; u.uView.value.set(W, H);
      u.uCam.value.copy(cam.position);
      v0.set(0, 0, 0).unproject(cam).sub(cam.position); v1.set(1, 0, 0).unproject(cam).sub(cam.position); v2.set(0, 1, 0).unproject(cam).sub(cam.position);
      u.uD0.value.copy(v0); u.uDx.value.copy(v1).sub(v0); u.uDy.value.copy(v2).sub(v0);
      u.uPlaneY.value = o.planeY; u.uShip.value.copy(o.ship); u.uRange.value = o.range;
      if (o.danger) { const d0 = o.danger[0], d1 = o.danger[1]; if (d0) u.uDz0.value.set(d0[0], d0[1], d0[2], d0[3]); if (d1) u.uDz1.value.set(d1[0], d1[1], d1[2], d1[3]); }
      ring.style.width = ring.style.height = (2 * L.r) + 'px';
    }

    // ---------- draw the stars inside (after field.project) ----------
    const mp = { x: 0, y: 0 }, mq = { x: 0, y: 0 };
    let order = [];
    function draw(o) {
      ctx.setTransform(DPR, 0, 0, DPR, 0, 0);
      if (dirty) ctx.clearRect(dirty[0], dirty[1], dirty[2], dirty[3]);
      dirty = null;
      const alpha = L.a * o.alpha;
      ring.style.opacity = bezel.style.opacity = alpha > 0.01 ? Math.min(1, alpha).toFixed(2) : '0';
      if (alpha < 0.01) return;
      const R = L.r, cx = L.cx, cy = L.cy;
      ring.style.transform = `translate3d(${(cx - R).toFixed(1)}px,${(cy - R).toFixed(1)}px,0)`;
      ring.classList.toggle('locked', L.locked);
      bezel.style.transform = `translate3d(${cx.toFixed(1)}px,${(cy + R).toFixed(1)}px,0) translate(-50%,-50%)`;
      dirty = [cx - R - 4, cy - R - 4, 2 * R + 8, 2 * R + 8];
      const F = o.field, n = F.lensCount, list = F.lensList;
      ctx.save();
      ctx.beginPath(); ctx.arc(cx, cy, R - 0.5, 0, Math.PI * 2); ctx.clip();
      ctx.globalAlpha = alpha;
      // route through the lens
      if (o.route && o.route.length > 1) {
        const pts = [];
        for (const p of o.route) { const s = o.proj(p.x, p.y, p.z); if (!s) { pts.push(null); continue; } map(s.x, s.y, mp); pts.push([mp.x, mp.y]); }
        const path = () => { ctx.beginPath(); let pen = false; for (const p of pts) { if (!p) { pen = false; continue; } if (!pen) { ctx.moveTo(p[0], p[1]); pen = true; } else ctx.lineTo(p[0], p[1]); } };
        ctx.lineCap = ctx.lineJoin = 'round';
        path(); ctx.strokeStyle = 'rgba(95,227,192,0.14)'; ctx.lineWidth = 9; ctx.stroke();
        path(); ctx.strokeStyle = 'rgba(95,227,192,0.95)'; ctx.lineWidth = 2.4; ctx.stroke();
      }
      // far to near, so nearer stars sit on top
      order.length = 0;
      for (let k = 0; k < n; k++) order.push(list[k]);
      order.sort((a, b) => F.dep[b] - F.dep[a]);
      const P = o.pos, planeY = o.planeY;
      // height stalks: from each star to the plane through your ship, bent by the lens like everything else
      ctx.lineWidth = 1;
      // only the patch's most visible stars get a stalk (plus the hovered, the selected and owned ones), so they stay readable
      const stalked = order.filter(i => F.lr[i] < 0.74 && F.fog[i] > 0.3).sort((a, b) => F.app[a] - F.app[b]).slice(0, 34);
      for (const i of order) if ((i === o.hov || i === o.sel || o.ownerRGB(i)) && F.lr[i] < 0.86 && !stalked.includes(i)) stalked.push(i);
      for (const i of stalked) {
        const fa = F.fog[i];
        const foot = o.proj(P.x[i], planeY, P.z[i]); if (!foot) continue;
        const sx = F.sx[i], sy = F.sy[i], above = P.y[i] >= planeY;
        const len = Math.hypot(foot.x - sx, foot.y - sy), steps = len < 12 ? 1 : 8;
        const own = o.ownerRGB(i), strong = i === o.hov || i === o.sel;
        const a = (strong ? 0.85 : own ? 0.5 : 0.2) * (0.4 + 0.6 * fa) * (above ? 1 : 0.75);
        ctx.strokeStyle = own ? rgbStr(own, a) : `rgba(205,218,238,${a})`;
        ctx.setLineDash(above ? [] : [3, 3]);
        ctx.beginPath();
        for (let s = 0; s <= steps; s++) {
          const t = s / steps; map(sx + (foot.x - sx) * t, sy + (foot.y - sy) * t, mp);
          if (s === 0) ctx.moveTo(mp.x, mp.y); else ctx.lineTo(mp.x, mp.y);
        }
        ctx.stroke();
        map(foot.x, foot.y, mq);
        if (Math.hypot(mq.x - cx, mq.y - cy) < R) { ctx.fillStyle = own ? rgbStr(own, a + 0.15) : `rgba(205,218,238,${a + 0.1})`; ctx.fillRect(mq.x - 1.5, mq.y - 0.75, 3, 1.5); }
      }
      ctx.setLineDash([]);
      // the stars
      const C = F.colour;
      for (const i of order) {
        const X = F.lx[i], Y = F.ly[i], rr = F.lr[i], fa = F.fog[i], mode = o.styleOf(i);
        if (rr > 0.86) {                                                   // the compressed rim: context only
          ctx.fillStyle = `rgba(200,212,232,${0.16 * fa})`; ctx.fillRect(X - 0.6, Y - 0.6, 1.2, 1.2); continue;
        }
        const ap = F.app[i];
        let rc = clamp(4.4 - 0.22 * ap, 1.3, 4.8) * (mode > 1.5 ? 0.7 : 1);
        const c = mode > 0.5 && mode < 1.5 ? o.styleRGB(i) : [C[3 * i], C[3 * i + 1], C[3 * i + 2]];
        const bright = clamp(1.25 - ap * 0.07, 0.35, 1) * (0.35 + 0.65 * fa) * (mode > 1.5 ? 0.35 : 1);
        ctx.globalAlpha = alpha * bright * 0.9;
        const hs = rc * 7; ctx.drawImage(halo(c[0], c[1], c[2]), X - hs / 2, Y - hs / 2, hs, hs);
        ctx.globalAlpha = alpha * Math.min(1, bright + 0.2);
        ctx.fillStyle = rgbStr([0.35 + c[0] * 0.65, 0.35 + c[1] * 0.65, 0.35 + c[2] * 0.65], 1);
        ctx.beginPath(); ctx.arc(X, Y, rc, 0, Math.PI * 2); ctx.fill();
        // whose it is: a ring in the owner's colour (amber dashes where contested, faint dashes on the free frontier)
        const own = o.ownerRGB(i), kind = o.keyOf(i);
        if (own) { ctx.strokeStyle = rgbStr(own, 0.95); ctx.lineWidth = 1.4; ctx.setLineDash(o.contested(i) ? [3, 2] : []); ctx.beginPath(); ctx.arc(X, Y, rc + 3, 0, Math.PI * 2); ctx.stroke(); ctx.setLineDash([]); }
        else if (o.frontier(i)) { ctx.strokeStyle = 'rgba(226,233,246,0.38)'; ctx.lineWidth = 1; ctx.setLineDash([2, 2.5]); ctx.beginPath(); ctx.arc(X, Y, rc + 3, 0, Math.PI * 2); ctx.stroke(); ctx.setLineDash([]); }
        if (kind === 2) { ctx.strokeStyle = 'rgba(196,181,253,0.95)'; ctx.lineWidth = 1.3; const k = rc + 5.5; ctx.beginPath(); ctx.moveTo(X, Y - k); ctx.lineTo(X + k, Y); ctx.lineTo(X, Y + k); ctx.lineTo(X - k, Y); ctx.closePath(); ctx.stroke(); }
        if (mode > 0.5 && mode < 1.5 && !own) { ctx.strokeStyle = rgbStr(c, 0.9); ctx.lineWidth = 1.2; ctx.beginPath(); ctx.arc(X, Y, rc + 3, 0, Math.PI * 2); ctx.stroke(); }
      }
      ctx.restore();
    }
    return L;
  }
})();
