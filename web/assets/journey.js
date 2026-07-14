/* Octadock — "The Descent". An underwater camera journey.
   ─────────────────────────────────────────────────────────────────────────
   This file IS the 3D system: hand-built for this page. No engine, no
   Three.js, no loaders — our own matrix/quaternion math, our own GLB parser
   and GPU skinning, our own materials (glass fresnel + wire), our own bloom.
   Plain classic script (no modules), WebGL2.

   Journey: surface light → the glass octopus (rigged GLB, curl-wave swim
   driven through our own joint pipeline) → a sunken workstation whose screen
   the camera dives into (the DOM demo takes over) → the violet thermocline
   ("cloud is opt-in") → the seabed close.

   Ground rules from the previous attempt's post-mortem:
   - ONE void black everywhere (CSS --void = clear = fog = env).
   - Native scroll only. No loading screen. Wireframe stays subtle.
   - ?freeze=<0..1> renders one still frame (same path reduced-motion takes).
   - window.__step(p) force-renders any point; hidden tabs advance on a timer.
   - If anything fails, the page must read perfectly without the 3D. */
(function () {
  'use strict';

  var canvas = document.getElementById('gl');
  var journeyEl = document.getElementById('journey');
  if (!canvas || !journeyEl) return;
  canvas.dataset.journeyStatus = 'booting';

  var q = new URLSearchParams(location.search);
  var FREEZE = q.has('freeze') ? Math.min(1, Math.max(0, parseFloat(q.get('freeze')) || 0)) : null;
  var reduce = matchMedia('(prefers-reduced-motion: reduce)').matches || FREEZE !== null;
  // The rigged animal shares this canvas and this frame clock. The journey is
  // the single source of truth for camera, composition, scroll, and gait.
  var EXTERNAL_OCTOPUS_V10 = location.protocol !== 'file:';
  window.__OCTOPUS_V10_EXTERNAL = EXTERNAL_OCTOPUS_V10;

  var gl = canvas.getContext('webgl2', {
    alpha: false, antialias: true, depth: true, stencil: false,
    powerPreference: 'high-performance', preserveDrawingBuffer: true
  });
  if (!gl) { document.body.classList.add('no-octo'); return; }
  canvas.dataset.journeyStatus = 'webgl';

  /* ══════════════════════════ tiny math library ═══════════════════════ */
  var M4 = {
    ident: function () { var m = new Float32Array(16); m[0] = m[5] = m[10] = m[15] = 1; return m; },
    mul: function (o, a, b) { // o = a × b
      for (var c = 0; c < 4; c++) {
        var b0 = b[c * 4], b1 = b[c * 4 + 1], b2 = b[c * 4 + 2], b3 = b[c * 4 + 3];
        o[c * 4]     = a[0] * b0 + a[4] * b1 + a[8]  * b2 + a[12] * b3;
        o[c * 4 + 1] = a[1] * b0 + a[5] * b1 + a[9]  * b2 + a[13] * b3;
        o[c * 4 + 2] = a[2] * b0 + a[6] * b1 + a[10] * b2 + a[14] * b3;
        o[c * 4 + 3] = a[3] * b0 + a[7] * b1 + a[11] * b2 + a[15] * b3;
      }
      return o;
    },
    compose: function (o, t, r, s) { // TRS from vec3 t, quat r, vec3 s
      var x = r[0], y = r[1], z = r[2], w = r[3];
      var x2 = x + x, y2 = y + y, z2 = z + z;
      var xx = x * x2, xy = x * y2, xz = x * z2;
      var yy = y * y2, yz = y * z2, zz = z * z2;
      var wx = w * x2, wy = w * y2, wz = w * z2;
      var sx = s[0], sy = s[1], sz = s[2];
      o[0] = (1 - (yy + zz)) * sx; o[1] = (xy + wz) * sx; o[2] = (xz - wy) * sx; o[3] = 0;
      o[4] = (xy - wz) * sy; o[5] = (1 - (xx + zz)) * sy; o[6] = (yz + wx) * sy; o[7] = 0;
      o[8] = (xz + wy) * sz; o[9] = (yz - wx) * sz; o[10] = (1 - (xx + yy)) * sz; o[11] = 0;
      o[12] = t[0]; o[13] = t[1]; o[14] = t[2]; o[15] = 1;
      return o;
    },
    invert: function (o, m) { // general 4x4 inverse
      var m00 = m[0], m01 = m[1], m02 = m[2], m03 = m[3],
          m10 = m[4], m11 = m[5], m12 = m[6], m13 = m[7],
          m20 = m[8], m21 = m[9], m22 = m[10], m23 = m[11],
          m30 = m[12], m31 = m[13], m32 = m[14], m33 = m[15];
      var b00 = m00 * m11 - m01 * m10, b01 = m00 * m12 - m02 * m10,
          b02 = m00 * m13 - m03 * m10, b03 = m01 * m12 - m02 * m11,
          b04 = m01 * m13 - m03 * m11, b05 = m02 * m13 - m03 * m12,
          b06 = m20 * m31 - m21 * m30, b07 = m20 * m32 - m22 * m30,
          b08 = m20 * m33 - m23 * m30, b09 = m21 * m32 - m22 * m31,
          b10 = m21 * m33 - m23 * m31, b11 = m22 * m33 - m23 * m32;
      var det = b00 * b11 - b01 * b10 + b02 * b09 + b03 * b08 - b04 * b07 + b05 * b06;
      if (!det) return o;
      det = 1.0 / det;
      o[0] = (m11 * b11 - m12 * b10 + m13 * b09) * det;
      o[1] = (m02 * b10 - m01 * b11 - m03 * b09) * det;
      o[2] = (m31 * b05 - m32 * b04 + m33 * b03) * det;
      o[3] = (m22 * b04 - m21 * b05 - m23 * b03) * det;
      o[4] = (m12 * b08 - m10 * b11 - m13 * b07) * det;
      o[5] = (m00 * b11 - m02 * b08 + m03 * b07) * det;
      o[6] = (m32 * b02 - m30 * b05 - m33 * b01) * det;
      o[7] = (m20 * b05 - m22 * b02 + m23 * b01) * det;
      o[8] = (m10 * b10 - m11 * b08 + m13 * b06) * det;
      o[9] = (m01 * b08 - m00 * b10 - m03 * b06) * det;
      o[10] = (m30 * b04 - m31 * b02 + m33 * b00) * det;
      o[11] = (m21 * b02 - m20 * b04 - m23 * b00) * det;
      o[12] = (m11 * b07 - m10 * b09 - m12 * b06) * det;
      o[13] = (m00 * b09 - m01 * b07 + m02 * b06) * det;
      o[14] = (m31 * b01 - m30 * b03 - m32 * b00) * det;
      o[15] = (m20 * b03 - m21 * b01 + m22 * b00) * det;
      return o;
    },
    perspective: function (o, fovy, aspect, near, far) {
      var f = 1 / Math.tan(fovy / 2), nf = 1 / (near - far);
      o.set([f / aspect, 0, 0, 0, 0, f, 0, 0, 0, 0, (far + near) * nf, -1, 0, 0, 2 * far * near * nf, 0]);
      return o;
    },
    lookAt: function (o, eye, c, up) { // view matrix
      var zx = eye[0] - c[0], zy = eye[1] - c[1], zz = eye[2] - c[2];
      var l = 1 / (Math.hypot(zx, zy, zz) || 1); zx *= l; zy *= l; zz *= l;
      var xx = up[1] * zz - up[2] * zy, xy = up[2] * zx - up[0] * zz, xz = up[0] * zy - up[1] * zx;
      l = 1 / (Math.hypot(xx, xy, xz) || 1); xx *= l; xy *= l; xz *= l;
      var yx = zy * xz - zz * xy, yy = zz * xx - zx * xz, yz = zx * xy - zy * xx;
      o.set([xx, yx, zx, 0, xy, yy, zy, 0, xz, yz, zz, 0,
        -(xx * eye[0] + xy * eye[1] + xz * eye[2]),
        -(yx * eye[0] + yy * eye[1] + yz * eye[2]),
        -(zx * eye[0] + zy * eye[1] + zz * eye[2]), 1]);
      return o;
    }
  };
  var Q = {
    mul: function (o, a, b) { // o = a ⊗ b
      var ax = a[0], ay = a[1], az = a[2], aw = a[3];
      var bx = b[0], by = b[1], bz = b[2], bw = b[3];
      o[0] = aw * bx + ax * bw + ay * bz - az * by;
      o[1] = aw * by - ax * bz + ay * bw + az * bx;
      o[2] = aw * bz + ax * by - ay * bx + az * bw;
      o[3] = aw * bw - ax * bx - ay * by - az * bz;
      return o;
    },
    rot: function (o, q, v) { // o = q · v (rotate vector)
      var qx = q[0], qy = q[1], qz = q[2], qw = q[3];
      var cx = qy * v[2] - qz * v[1] + qw * v[0];
      var cy = qz * v[0] - qx * v[2] + qw * v[1];
      var cz = qx * v[1] - qy * v[0] + qw * v[2];
      o[0] = v[0] + 2 * (qy * cz - qz * cy);
      o[1] = v[1] + 2 * (qz * cx - qx * cz);
      o[2] = v[2] + 2 * (qx * cy - qy * cx);
      return o;
    },
    rotInv: function (o, q, v) { // o = q⁻¹ · v
      var c = [-q[0], -q[1], -q[2], q[3]];
      return Q.rot(o, c, v);
    },
    mulAxisX: function (o, a, ang) { // o = a ⊗ rotX(ang)
      var h = ang / 2, bx = Math.sin(h), bw = Math.cos(h);
      o[0] = a[0] * bw + a[3] * bx;
      o[1] = a[1] * bw + a[2] * bx;
      o[2] = a[2] * bw - a[1] * bx;
      o[3] = a[3] * bw - a[0] * bx;
      return o;
    },
    fromTo: function (o, a, b) { // shortest rotation â → b̂ (unit inputs)
      var d = a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
      if (d > 0.99999) { o[0] = 0; o[1] = 0; o[2] = 0; o[3] = 1; return o; }
      if (d < -0.99999) { // 180°: any perpendicular axis
        var ax = Math.abs(a[0]) < 0.9 ? [1, 0, 0] : [0, 1, 0];
        var px = a[1] * ax[2] - a[2] * ax[1], py = a[2] * ax[0] - a[0] * ax[2], pz = a[0] * ax[1] - a[1] * ax[0];
        var pl = Math.hypot(px, py, pz) || 1;
        o[0] = px / pl; o[1] = py / pl; o[2] = pz / pl; o[3] = 0; return o;
      }
      var cx = a[1] * b[2] - a[2] * b[1], cy = a[2] * b[0] - a[0] * b[2], cz = a[0] * b[1] - a[1] * b[0];
      var w = 1 + d, l = Math.hypot(cx, cy, cz, w) || 1;
      o[0] = cx / l; o[1] = cy / l; o[2] = cz / l; o[3] = w / l;
      return o;
    }
  };
  function v3(x, y, z) { return [x, y, z]; }
  function lerp3(o, a, b, t) { o[0] = a[0] + (b[0] - a[0]) * t; o[1] = a[1] + (b[1] - a[1]) * t; o[2] = a[2] + (b[2] - a[2]) * t; return o; }
  function smoothstep(x, a, b) { var t = Math.min(1, Math.max(0, (x - a) / (b - a || 1))); return t * t * (3 - 2 * t); }

  /* ══════════════════════════ GL plumbing ═════════════════════════════ */
  function sh(type, src) {
    var s = gl.createShader(type);
    gl.shaderSource(s, src); gl.compileShader(s);
    if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(s) || 'shader');
    return s;
  }
  function prog(vs, fs) {
    var p = gl.createProgram();
    gl.attachShader(p, sh(gl.VERTEX_SHADER, '#version 300 es\n' + vs));
    gl.attachShader(p, sh(gl.FRAGMENT_SHADER, '#version 300 es\nprecision highp float;\n' + fs));
    gl.linkProgram(p);
    if (!gl.getProgramParameter(p, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(p) || 'link');
    var u = {}, n = gl.getProgramParameter(p, gl.ACTIVE_UNIFORMS);
    for (var i = 0; i < n; i++) {
      var info = gl.getActiveUniform(p, i);
      u[info.name.replace(/\[0\]$/, '')] = gl.getUniformLocation(p, info.name);
    }
    return { p: p, u: u };
  }
  function buf(target, data) {
    var b = gl.createBuffer();
    gl.bindBuffer(target, b);
    gl.bufferData(target, data, gl.STATIC_DRAW);
    return b;
  }
  function attr(program, name, buffer, comps, type, normalize, stride, offset) {
    var loc = gl.getAttribLocation(program, name);
    if (loc < 0) return;
    gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
    gl.enableVertexAttribArray(loc);
    gl.vertexAttribPointer(loc, comps, type, normalize || false, stride || 0, offset || 0);
  }

  var halfFloatOK = !!gl.getExtension('EXT_color_buffer_float');
  function makeTarget(w, h, depth) {
    var fb = gl.createFramebuffer(), tx = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, tx);
    gl.texImage2D(gl.TEXTURE_2D, 0, halfFloatOK ? gl.RGBA16F : gl.RGBA8, w, h, 0, gl.RGBA, halfFloatOK ? gl.HALF_FLOAT : gl.UNSIGNED_BYTE, null);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    gl.bindFramebuffer(gl.FRAMEBUFFER, fb);
    gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, tx, 0);
    var rb = null;
    if (depth) {
      rb = gl.createRenderbuffer();
      gl.bindRenderbuffer(gl.RENDERBUFFER, rb);
      gl.renderbufferStorage(gl.RENDERBUFFER, gl.DEPTH_COMPONENT24, w, h);
      gl.framebufferRenderbuffer(gl.FRAMEBUFFER, gl.DEPTH_ATTACHMENT, gl.RENDERBUFFER, rb);
    }
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    return { fb: fb, tx: tx, rb: rb, w: w, h: h };
  }
  function sizeTarget(t, w, h) {
    t.w = w; t.h = h;
    gl.bindTexture(gl.TEXTURE_2D, t.tx);
    gl.texImage2D(gl.TEXTURE_2D, 0, halfFloatOK ? gl.RGBA16F : gl.RGBA8, w, h, 0, gl.RGBA, halfFloatOK ? gl.HALF_FLOAT : gl.UNSIGNED_BYTE, null);
    if (t.rb) {
      gl.bindRenderbuffer(gl.RENDERBUFFER, t.rb);
      gl.renderbufferStorage(gl.RENDERBUFFER, gl.DEPTH_COMPONENT24, w, h);
    }
  }

  /* colours (linear-space) — --void is the single source of truth */
  var css = getComputedStyle(document.documentElement);
  function hex2lin(h) {
    h = h.trim().replace('#', '');
    var r = parseInt(h.slice(0, 2), 16) / 255, g = parseInt(h.slice(2, 4), 16) / 255, b = parseInt(h.slice(4, 6), 16) / 255;
    return [Math.pow(r, 2.2), Math.pow(g, 2.2), Math.pow(b, 2.2)];
  }
  var VOID = hex2lin(css.getPropertyValue('--void') || '#05080f');
  var TEAL = hex2lin('#2dd4bf'), CYAN = hex2lin('#38bdf8'), VIOLET = hex2lin('#a78bfa');

  window.__octoLive = true;
  var motionDebug = window.__octoMotion = {
    direction: 1, rawDeltaPx: 0, lastDeltaPx: 0, lastInputDirection: 1,
    lastEventAtMs: -1, inputMagnitudePx: 0, velocity: 0,
    effort: 0, mode: 0, inputActive: false, inputAgeMs: Infinity,
    state: 'idle', speed: 0, energy: 0, desired: 0,
    screenOff: 0, offX: 0, screenVelocity: 0,
    travelA: Math.PI / 2, yawSwing: 0, bank: 0, turning: false,
    strokeActive: false, strokePhase: 0, strokeBlend: 0, strokeClip: 'swim',
    anchorNdcX: 0, anchorNdcY: 0, ndcX: 0, ndcY: 0,
    yaw: 0, pitch: 0.08
  };
  document.body.classList.add('octo-live');

  /* ══════════════════════════ config / state ══════════════════════════ */
  var CFG = {
    scale: 1.5, drift: 1.0, amp: 40, glow: 0.55,
    thick: 1.2, rough: 0.1, bloom: 0.5, fog: 0.05, motes: 0.6, rays: 0.4,
    dirGain: 0.85           // viewport-normalized raw scroll -> NDC travel
  };
  var DPR = Math.min(devicePixelRatio || 1, 2);
  var degraded = false;

  var proj = M4.ident(), view = M4.ident(), viewProj = M4.ident();
  var camPos = v3(0, 12, 9);

  /* fullscreen triangle */
  var fsTriBuf = buf(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 3, -1, -1, 3]));
  function drawFsTri(pr) {
    attr(pr.p, 'aP', fsTriBuf, 2, gl.FLOAT);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
  }

  /* shared fog chunk (exp2, matches the old look) */
  var FOG = 'uniform vec3 uFogC; uniform float uFogD;' +
    'vec3 fogMix(vec3 c, float dist){ float f = 1.0 - exp(-uFogD*uFogD*dist*dist); return mix(c, uFogC, clamp(f,0.0,1.0)); }';

  /* ══════════════════════════ backdrop ════════════════════════════════ */
  var backdropP = prog(
    'in vec2 aP; out vec2 vUv; void main(){ vUv = aP*0.5+0.5; gl_Position = vec4(aP,0.9999,1.0); }',
    'in vec2 vUv; out vec4 O;' +
    'uniform float uSurface,uCloud,uFloor; uniform vec3 uVoid;' +
    'void main(){' +
    '  vec3 col = uVoid;' +
    '  float top = pow(clamp(vUv.y,0.0,1.0), 2.4);' +
    '  col += vec3(0.002,0.008,0.015) * uSurface * top;' +
    '  vec2 p = vUv - vec2(0.62,0.52);' +
    '  float d = length(p*vec2(1.1,1.4));' +
    '  col += vec3(0.002,0.007,0.007) * smoothstep(0.75,0.0,d) * (0.4 + 0.6*uSurface);' +
    '  col += vec3(0.010,0.006,0.024) * uCloud * smoothstep(0.9,0.25,abs(vUv.y-0.5)*1.6);' +
    '  col += vec3(0.005,0.022,0.019) * uFloor * pow(max(0.0,1.0-vUv.y),1.6) * 1.3;' +
    '  O = vec4(col,1.0);' +
    '}');

  /* ══════════════════════════ quads in world (rays/thermo/glow/screen) ═ */
  var quadBuf = buf(gl.ARRAY_BUFFER, new Float32Array([-0.5, -0.5, 0.5, -0.5, -0.5, 0.5, 0.5, 0.5]));
  var QUAD_VS =
    'in vec2 aP; out vec2 vUv;' +
    'uniform mat4 uVP; uniform vec3 uPos; uniform vec2 uSize; uniform vec3 uAxX; uniform vec3 uAxY;' +
    'out float vDist;' +
    'void main(){ vUv = aP+0.5;' +
    '  vec3 w = uPos + uAxX*(aP.x*uSize.x) + uAxY*(aP.y*uSize.y);' +
    '  vec4 cp = uVP*vec4(w,1.0); vDist = cp.w; gl_Position = cp; }';

  var rayP = prog(QUAD_VS,
    'in vec2 vUv; in float vDist; out vec4 O; uniform float uA;' +
    'void main(){' +
    '  float x = smoothstep(0.0,0.35,vUv.x)*smoothstep(1.0,0.65,vUv.x);' +
    '  float y = smoothstep(0.0,0.12,1.0-vUv.y)*pow(vUv.y,1.4);' +
    '  O = vec4(vec3(0.05,0.17,0.19), x*y*0.11*uA);' +
    '}');
  var thermoP = prog(QUAD_VS,
    'in vec2 vUv; in float vDist; out vec4 O; uniform float uA;' +
    'void main(){ float d = length(vUv-0.5)*2.0;' +
    '  O = vec4(0.25,0.16,0.78, smoothstep(1.0,0.1,d)*0.05*uA); }');
  var glowP = prog(QUAD_VS,
    'in vec2 vUv; in float vDist; out vec4 O; uniform float uA;' +
    'void main(){ float d = length((vUv-0.5)*vec2(1.25,1.6))*2.0;' +
    '  O = vec4(0.035,0.55,0.5, smoothstep(1.0,0.05,d)*0.16*uA); }');
  /* dark glass backing behind the projected DOM content: correct depth for the
     GL scene, plus a faint backlight so the display reads as a screen even
     from angles where the DOM quad is edge-on. The CONTENT is never drawn
     here — the #screenroom DOM is the one and only source of screen pixels. */
  var screenP = prog(QUAD_VS,
    'in vec2 vUv; in float vDist; out vec4 O;' + FOG +
    'void main(){' +
    '  float d = length((vUv-0.5)*vec2(1.6,1.2));' +
    '  vec3 c = vec3(0.004,0.008,0.013) + vec3(0.010,0.030,0.028)*smoothstep(0.75,0.1,d);' +
    '  O = vec4(fogMix(c, vDist), 1.0); }');

  var RAYS = [];
  for (var ri = 0; ri < 5; ri++) {
    RAYS.push({
      pos: v3(-5 + ri * 2.6 + (ri % 2), 7.5, -1.5 - (ri % 3) * 1.2),
      size: [1.4 + ri * 0.5, 16],
      rot: 0.18 - ri * 0.07, yaw: 0.3 * (ri % 2 ? 1 : -1)
    });
  }

  /* ══════════════════════════ floor grid ══════════════════════════════ */
  var floorP = prog(
    'in vec2 aP; out vec2 vP; out float vDist;' +
    'uniform mat4 uVP; uniform float uY; uniform float uS;' +
    'void main(){ vP = aP*uS; vec4 cp = uVP*vec4(aP.x*uS, uY, aP.y*uS, 1.0); vDist = cp.w; gl_Position = cp; }',
    'in vec2 vP; in float vDist; out vec4 O; uniform float uT,uA;' +
    'float ln(float x){ float g=abs(fract(x)-0.5); return smoothstep(0.46,0.5,g); }' +
    'void main(){' +
    '  float g = max(ln(vP.x*0.5), ln(vP.y*0.5));' +
    '  float caust = 0.75 + 0.25*sin(vP.x*0.7+uT*0.5)*sin(vP.y*0.8-uT*0.4);' +
    '  float f = smoothstep(26.0,2.0,length(vP));' +
    '  O = vec4(0.026,0.65,0.52, g*f*0.045*uA*caust);' +
    '}');

  /* ══════════════════════════ motes ═══════════════════════════════════ */
  var MOTES = 1500;
  var motePos = new Float32Array(MOTES * 3), moteSeed = new Float32Array(MOTES);
  for (var mi = 0; mi < MOTES; mi++) {
    motePos[mi * 3] = (Math.random() - 0.5) * 15;
    motePos[mi * 3 + 1] = -33 + Math.random() * 50;
    motePos[mi * 3 + 2] = -3 + Math.random() * 13;
    moteSeed[mi] = Math.random();
  }
  var motePosBuf = buf(gl.ARRAY_BUFFER, motePos);
  var moteSeedBuf = buf(gl.ARRAY_BUFFER, moteSeed);
  var moteP = prog(
    'in vec3 aP; in float aSeed; out float vA;' +
    'uniform mat4 uVP; uniform float uT,uA,uDpr;' +
    'void main(){' +
    '  vec3 p = aP;' +
    '  p.x += sin(uT*0.11 + aSeed*40.0)*0.35;' +
    '  p.y += sin(uT*0.07 + aSeed*29.0)*0.3;' +
    '  p.z += sin(uT*0.09 + aSeed*17.0)*0.25;' +
    '  vec4 cp = uVP*vec4(p,1.0);' +
    '  float dist = cp.w;' +
    '  vA = uA * (0.25 + 0.5*fract(aSeed*7.0)) * smoothstep(16.0,5.0,dist);' +
    '  gl_PointSize = (1.2 + fract(aSeed*13.0)*2.4) * uDpr * (6.0/max(2.0,dist));' +
    '  gl_Position = cp;' +
    '}',
    'in float vA; out vec4 O;' +
    'void main(){ float d = length(gl_PointCoord-0.5)*2.0; if(d>1.0) discard;' +
    '  O = vec4(0.25,0.68,0.68, pow(1.0-d,2.0)*vA); }');

  /* ══════════════════════════ sunken workstation ══════════════════════ */
  var SP = v3(0, -15.6, 2.2); // station position (shared with the track)
  /* The display quad matches the live viewport aspect, so the dive's hold
     point (camera on the screen axis at DIVE_D) makes the projected DOM
     content land EXACTLY on the full viewport — a true identity handoff. */
  var SCREEN_H = 1.98, SCREEN_W = 3.42, BEZEL = 0.11;
  var DIVE_D = (SCREEN_H / 2) / Math.tan(20 * Math.PI / 180); // fovy 40°
  function sizeScreen() {
    SCREEN_W = SCREEN_H * Math.min(2.6, Math.max(0.5, innerWidth / Math.max(1, innerHeight)));
    if (typeof buildDesk === 'function') buildDesk();
  }
  sizeScreen();

  // bezel box (unit cube scaled at draw)
  var cubeVerts = (function () {
    var f = [
      [0, 0, 1], [0, 0, -1], [0, 1, 0], [0, -1, 0], [1, 0, 0], [-1, 0, 0]
    ];
    var out = [];
    f.forEach(function (n) {
      var u = n[0] ? [0, 1, 0] : [1, 0, 0];
      var v = [n[1] * u[2] - n[2] * u[1], n[2] * u[0] - n[0] * u[2], n[0] * u[1] - n[1] * u[0]];
      var c = [[-1, -1], [1, -1], [-1, 1], [1, -1], [1, 1], [-1, 1]];
      c.forEach(function (s) {
        out.push(
          n[0] * 0.5 + (u[0] * s[0] + v[0] * s[1]) * 0.5,
          n[1] * 0.5 + (u[1] * s[0] + v[1] * s[1]) * 0.5,
          n[2] * 0.5 + (u[2] * s[0] + v[2] * s[1]) * 0.5,
          n[0], n[1], n[2]);
      });
    });
    return new Float32Array(out);
  })();
  var cubeBuf = buf(gl.ARRAY_BUFFER, cubeVerts);
  var bezelP = prog(
    'in vec3 aP; in vec3 aN; out vec3 vN; out vec3 vW; out float vDist;' +
    'uniform mat4 uVP; uniform vec3 uPos; uniform vec3 uScale;' +
    'void main(){ vec3 w = uPos + aP*uScale; vW = w; vN = aN;' +
    '  vec4 cp = uVP*vec4(w,1.0); vDist = cp.w; gl_Position = cp; }',
    'in vec3 vN; in vec3 vW; in float vDist; out vec4 O;' +
    'uniform vec3 uEye; uniform vec3 uBase; uniform float uFr;' + FOG +
    'void main(){' +
    '  vec3 N = normalize(vN); vec3 V = normalize(uEye - vW);' +
    '  float fr = pow(1.0 - abs(dot(N,V)), 3.0) * uFr;' +
    '  vec3 c = uBase + vec3(0.03,0.10,0.10)*fr + vec3(0.02,0.06,0.06)*uFr*pow(max(dot(N, normalize(vec3(0.4,0.7,0.5))),0.0),8.0);' +
    '  O = vec4(fogMix(c, vDist), 1.0);' +
    '}');

  /* the workstation is a NORMAL desk setup: monitor on a stand, on a desk with
     a keyboard and mouse, standing on a rock ledge — nothing dangling from
     above. Boxes are drawn with the bezel program; sized with the display. */
  var DESK = [];
  function buildDesk() {
    var dw = SCREEN_W + 1.9;                       // desk tracks the display width
    var lx = dw / 2 - 0.28;
    DESK = [
      // [pos offset from SP]        [scale]                [base colour]
      [[0, -1.18, -0.26],  [0.18, 0.24, 0.12],  [0.005, 0.010, 0.018]],  // stand neck
      [[0, -1.325, -0.18], [1.05, 0.06, 0.62],  [0.005, 0.010, 0.018]],  // stand foot
      [[0, -1.40, -0.35],  [dw, 0.12, 2.3],     [0.007, 0.013, 0.024]],  // desk top
      [[-lx, -2.04, -0.35],[0.16, 1.20, 1.95],  [0.004, 0.009, 0.016]],  // leg L
      [[ lx, -2.04, -0.35],[0.16, 1.20, 1.95],  [0.004, 0.009, 0.016]],  // leg R
      [[0.15, -1.31, 0.62],[1.75, 0.055, 0.52], [0.006, 0.014, 0.022]],  // keyboard
      [[1.45, -1.305, 0.66],[0.22, 0.05, 0.34], [0.006, 0.014, 0.022]],  // mouse
      [[0, -2.76, -0.5],   [dw + 3.6, 0.30, 4.8],[0.004, 0.008, 0.013]]  // rock ledge
    ];
  }
  buildDesk();

  /* ══════════════════════════ the creature: authored, not loaded ══════ */
  var creature = null; // built synchronously below
  var LAB = q.has('lab');
  var LABT = parseFloat(q.get('labt') || '4.2');
  var labAz = 0.9, labEl = 0.25, labDist = 7.5, labAuto = true;
  if (q.has('az')) { labAz = parseFloat(q.get('az')); labAuto = false; }
  if (q.has('el')) { labEl = parseFloat(q.get('el')); labAuto = false; }
  if (q.has('dist')) labDist = parseFloat(q.get('dist'));
  if (LAB) { // workbench: clear the stage, drag to orbit, wheel to zoom
    var labSt = document.createElement('style');
    labSt.textContent = '.journey,.records,.foot,.nav,.hud,.scrollcue,.vignette{visibility:hidden !important}' +
      'html,body{overflow:hidden !important;cursor:grab}body:active{cursor:grabbing}';
    document.head.appendChild(labSt);
    var dragging = false, lx = 0, ly = 0;
    addEventListener('pointerdown', function (e) { dragging = true; lx = e.clientX; ly = e.clientY; });
    addEventListener('pointermove', function (e) {
      if (!dragging) return;
      labAuto = false;
      labAz -= (e.clientX - lx) * 0.008;
      labEl = Math.max(-1.2, Math.min(1.2, labEl + (e.clientY - ly) * 0.006));
      lx = e.clientX; ly = e.clientY;
    });
    addEventListener('pointerup', function () { dragging = false; });
    addEventListener('wheel', function (e) {
      labDist = Math.max(2.2, Math.min(9, labDist + e.deltaY * 0.004));
      e.preventDefault();
    }, { passive: false });
  }

  /* One continuous Blender-authored creature: mantle, crown, web, eight arms,
     and sucker rows share one skinned surface and one rendering path. */
  var GLASS_FS =
    'in vec3 vN; in vec3 vW; in float vDist; in float vF; out vec4 O;' +
    'uniform vec3 uEye; uniform float uBack; uniform vec3 uTint;' + FOG +
    'vec3 env(vec3 d){' + // our tiny procedural studio: teal key, cyan fill, white rim
    '  vec3 c = mix(vec3(0.001,0.005,0.008), vec3(0.008,0.045,0.045), smoothstep(-1.0,1.0,d.y));' +
    '  c += vec3(0.026,0.55,0.45) * pow(max(dot(d, normalize(vec3(-0.55,0.42,0.55))),0.0), 5.0) * 0.4;' +
    '  c += vec3(0.03,0.35,0.75) * pow(max(dot(d, normalize(vec3(0.62,0.16,0.42))),0.0), 6.0) * 0.3;' +
    '  c += vec3(0.85) * pow(max(dot(d, normalize(vec3(0.0,0.92,-0.4))),0.0), 24.0) * 0.4;' +
    '  return c;' +
    '}' +
    'void main(){' +
    '  vec3 N = normalize(vN) * (uBack > 0.5 ? -1.0 : 1.0);' +
    '  vec3 V = normalize(uEye - vW);' +
    '  float ndv = max(dot(N,V), 0.0);' +
    '  float fr = pow(1.0 - ndv, 3.0);' +
    '  vec3 R = reflect(-V, N);' +
    '  vec3 c = uTint * (0.05 + 0.08*ndv);' +         // deep glass body
    '  c += env(R) * (0.10 + 0.5*fr);' +              // studio reflections
    '  c += uTint * 1.1 * fr;' +                      // rim light
    '  float a = uBack > 0.5 ? (0.055 + 0.13*fr) : (0.12 + 0.24*fr);' +
    '  O = vec4(fogMix(c, vDist), a * vF);' +
    '}';
  var WIRE_FS = 'out vec4 O; uniform vec3 uC; uniform float uA; void main(){ O = vec4(uC*uA, 1.0); }';

  /* The production Blender octopus is one continuous skinned surface. Its
     75-joint rig carries four Blender-authored clips; the page only owns
     screen-space direction, effort, and the short reversal envelope. */
  var SKIN_VS =
    'in vec3 aP; in vec3 aN; in vec4 aJ; in vec4 aW;' +
    'in vec3 aM0; in vec3 aM1; in vec3 aM2;' +
    'in vec3 aMN0; in vec3 aMN1; in vec3 aMN2;' +
    'uniform mat4 uVP; uniform mat4 uModel; uniform mat4 uJoints[128]; uniform vec3 uMorph;' +
    'out vec3 vN; out vec3 vW; out float vDist; out float vF;' +
    'void main(){' +
    '  vec3 p = aP + aM0*uMorph.x + aM1*uMorph.y + aM2*uMorph.z;' +
    '  vec3 n = normalize(aN + aMN0*uMorph.x + aMN1*uMorph.y + aMN2*uMorph.z);' +
    '  mat4 skin = aW.x*uJoints[int(aJ.x)] + aW.y*uJoints[int(aJ.y)] + aW.z*uJoints[int(aJ.z)] + aW.w*uJoints[int(aJ.w)];' +
    '  vec4 wp = uModel * skin * vec4(p,1.0);' +
    '  vW = wp.xyz;' +
    '  vN = normalize(mat3(uModel) * mat3(skin) * n);' +
    '  vF = 1.0;' +
    '  vec4 cp = uVP * wp; vDist = cp.w; gl_Position = cp;' +
    '}';
  var octoGlassP = prog(SKIN_VS, GLASS_FS);
  var octoWireP = prog(SKIN_VS, WIRE_FS);
  var eyePosBuf = buf(gl.ARRAY_BUFFER, new Float32Array([
    // v2.1 production rig: lateral orbital eyeballs, glTF Y-up mesh space
    -0.345, 0.381, 0.070,
     0.345, 0.381, 0.070
  ]));
  var eyeP = prog(
    'in vec3 aP; uniform mat4 uVP; uniform mat4 uModel; uniform float uSize;' +
    'void main(){ vec4 cp=uVP*uModel*vec4(aP,1.0); gl_PointSize=uSize/max(0.65,cp.w*0.12); gl_Position=cp; }',
    'out vec4 O; uniform vec3 uC;' +
    'void main(){ float d=length(gl_PointCoord-0.5)*2.0; if(d>1.0) discard;' +
    'float iris=smoothstep(1.0,0.22,d); float core=smoothstep(0.36,0.0,d);' +
    'O=vec4(mix(uC,vec3(0.88,1.0,0.98),core),0.18*iris+0.26*core); }');

  function parseGLB(ab) {
    var dv = new DataView(ab);
    if (dv.getUint32(0, true) !== 0x46546C67) throw new Error('not glb');
    var jsonLen = dv.getUint32(12, true);
    var json = JSON.parse(new TextDecoder().decode(new Uint8Array(ab, 20, jsonLen)));
    var off = 20 + jsonLen, bin = null;
    while (off < dv.byteLength) {
      var len = dv.getUint32(off, true), type = dv.getUint32(off + 4, true);
      if (type === 0x004E4942) { bin = new Uint8Array(ab, off + 8, len); break; }
      off += 8 + len;
    }
    var CT = { 5120: Int8Array, 5121: Uint8Array, 5122: Int16Array, 5123: Uint16Array, 5125: Uint32Array, 5126: Float32Array };
    var NC = { SCALAR: 1, VEC2: 2, VEC3: 3, VEC4: 4, MAT4: 16 };
    function acc(i) {
      var a = json.accessors[i];
      var Ctor = CT[a.componentType], comps = NC[a.type];
      var elemBytes = Ctor.BYTES_PER_ELEMENT * comps;
      var out;
      if (a.bufferView == null) {
        out = new Ctor(a.count * comps);
      } else {
        var bv = json.bufferViews[a.bufferView];
        var byteOff = (bv.byteOffset || 0) + (a.byteOffset || 0);
        var stride = bv.byteStride || 0;
        if (!stride || stride === elemBytes) {
          out = new Ctor(ab, bin.byteOffset + byteOff, a.count * comps);
        } else {
          out = new Ctor(a.count * comps);
          for (var e = 0; e < a.count; e++) {
            var src2 = new Ctor(ab, bin.byteOffset + byteOff + e * stride, comps);
            out.set(src2, e * comps);
          }
        }
      }
      if (a.sparse) {
        if (!(out.byteOffset === 0 && out.byteLength === out.buffer.byteLength)) {
          var dense = new Ctor(out.length); dense.set(out); out = dense;
        }
        var sparse = a.sparse;
        var ibv = json.bufferViews[sparse.indices.bufferView];
        var vbv = json.bufferViews[sparse.values.bufferView];
        var IndexCtor = CT[sparse.indices.componentType];
        var sparseIndices = new IndexCtor(ab, bin.byteOffset + (ibv.byteOffset || 0) + (sparse.indices.byteOffset || 0), sparse.count);
        var sparseValues = new Ctor(ab, bin.byteOffset + (vbv.byteOffset || 0) + (sparse.values.byteOffset || 0), sparse.count * comps);
        for (var si = 0; si < sparse.count; si++) {
          for (var sc = 0; sc < comps; sc++) out[sparseIndices[si] * comps + sc] = sparseValues[si * comps + sc];
        }
      }
      return out;
    }
    return { json: json, acc: acc };
  }

  function edgesOf(idx) {
    var eset = new Set(), edges = [];
    for (var i = 0; i < idx.length; i += 3) {
      var t3 = [idx[i], idx[i + 1], idx[i + 2]];
      for (var k = 0; k < 3; k++) {
        var a = t3[k], b = t3[(k + 1) % 3];
        var key = a < b ? a * 65536 + b : b * 65536 + a;
        if (!eset.has(key)) { eset.add(key); edges.push(a, b); }
      }
    }
    return new Uint16Array(edges);
  }
  var jit = function (i, k) { var x = Math.sin(i * 127.1 + k * 311.7) * 43758.5453; return x - Math.floor(x); };

  function inlineModelBuffer(encoded) {
    var raw = atob(encoded), bytes = new Uint8Array(raw.length);
    for (var bi = 0; bi < raw.length; bi++) bytes[bi] = raw.charCodeAt(bi);
    return bytes.buffer;
  }
  var modelSource = !EXTERNAL_OCTOPUS_V10 && (window.__OCTOPUS_GLB_BASE64
    ? Promise.resolve(inlineModelBuffer(window.__OCTOPUS_GLB_BASE64))
    : fetch('./assets/models/octopus-fable-web.glb?v=8')
        .then(function (r) { if (!r.ok) throw new Error('http ' + r.status); return r.arrayBuffer(); }));
  window.__OCTOPUS_GLB_BASE64 = null;
  if (modelSource) modelSource
    .then(function (ab) {
      var g = parseGLB(ab), j = g.json;
      var meshNode = -1;
      j.nodes.forEach(function (n, i) {
        if (n.mesh == null || meshNode >= 0) return;
        var candidate = j.meshes[n.mesh].primitives[0];
        if (candidate.attributes.JOINTS_0 != null) meshNode = i;
      });
      if (meshNode < 0) throw new Error('production octopus has no skinned mesh');
      var prim = j.meshes[j.nodes[meshNode].mesh].primitives[0];
      var pos = g.acc(prim.attributes.POSITION);
      var nrm = g.acc(prim.attributes.NORMAL);
      var jnt = g.acc(prim.attributes.JOINTS_0);
      var wgt = g.acc(prim.attributes.WEIGHTS_0);
      var idx = g.acc(prim.indices);
      // weights may arrive normalized-integer; the shader wants floats
      if (!(wgt instanceof Float32Array)) {
        var wScale = wgt instanceof Uint16Array ? 1 / 65535 : 1 / 255;
        var wf = new Float32Array(wgt.length);
        for (var wi = 0; wi < wgt.length; wi++) wf[wi] = wgt[wi] * wScale;
        wgt = wf;
      }
      // this rig ships no morph targets; bind harmless dummies and zero uMorph
      var targets = prim.targets || [];
      var hasMorphs = targets.length >= 3;
      var morphP = targets.map(function (target) { return g.acc(target.POSITION); });
      var morphN = targets.map(function (target) { return g.acc(target.NORMAL); });
      while (morphP.length < 3) { morphP.push(pos); morphN.push(nrm); }

      var mn = [1e9, 1e9, 1e9], mx = [-1e9, -1e9, -1e9], i, k;
      for (i = 0; i < pos.length; i += 3) {
        for (k = 0; k < 3; k++) {
          if (pos[i + k] < mn[k]) mn[k] = pos[i + k];
          if (pos[i + k] > mx[k]) mx[k] = pos[i + k];
        }
      }
      var ctr = [(mn[0] + mx[0]) / 2, (mn[1] + mx[1]) / 2, (mn[2] + mx[2]) / 2];
      var fit = 2.05 / Math.max(mx[0] - mn[0], mx[1] - mn[1], mx[2] - mn[2]);

      /* skeleton: rest pose, world transforms, and the arm chains */
      var skin = j.skins[0];
      var ibm = g.acc(skin.inverseBindMatrices);
      var parent = new Array(j.nodes.length).fill(-1);
      j.nodes.forEach(function (n, ni) { (n.children || []).forEach(function (c) { parent[c] = ni; }); });
      var order = [];
      function walk(ni) { order.push(ni); (j.nodes[ni].children || []).forEach(walk); }
      j.scenes[j.scene || 0].nodes.forEach(walk);

      var nodesRT = j.nodes.map(function (n) {
        return {
          t: n.translation ? n.translation.slice() : [0, 0, 0],
          r: n.rotation ? n.rotation.slice() : [0, 0, 0, 1],
          s: n.scale ? n.scale.slice() : [1, 1, 1],
          t0: n.translation ? n.translation.slice() : [0, 0, 0],
          s0: n.scale ? n.scale.slice() : [1, 1, 1],
          rest: n.rotation ? n.rotation.slice() : [0, 0, 0, 1],
          local: M4.ident(), world: M4.ident(),
          restWP: [0, 0, 0], restWQ: [0, 0, 0, 1], aq: [0, 0, 0, 1]
        };
      });
      order.forEach(function (ni) {
        var nd = nodesRT[ni];
        M4.compose(nd.local, nd.t, nd.rest, nd.s);
        if (parent[ni] >= 0) {
          M4.mul(nd.world, nodesRT[parent[ni]].world, nd.local);
          Q.mul(nd.restWQ, nodesRT[parent[ni]].restWQ, nd.rest);
        } else {
          nd.world.set(nd.local);
          nd.restWQ = nd.rest.slice();
        }
        nd.restWP = [nd.world[12], nd.world[13], nd.world[14]];
      });

      /* Blender-authored clips: sparse-keyed rotation/translation/scale
         channels per joint, sampled at runtime (idle, swim, swimB,
         turnDown, turnUp, flipLag). */
      var clips = {};
      (j.animations || []).forEach(function (anim) {
        var dur = 0, chans = [];
        (anim.channels || []).forEach(function (ch) {
          if (ch.target.node == null) return;
          var smp = anim.samplers[ch.sampler];
          var path = ch.target.path;
          if (path !== 'rotation' && path !== 'translation' && path !== 'scale') return;
          var times = g.acc(smp.input), vals = g.acc(smp.output);
          if (!times.length) return;
          dur = Math.max(dur, times[times.length - 1]);
          chans.push({
            node: ch.target.node, path: path, times: times, vals: vals,
            comps: path === 'rotation' ? 4 : 3
          });
        });
        clips[anim.name || ('clip' + Object.keys(clips).length)] = { dur: Math.max(dur, 0.001), chans: chans };
      });

      /* what the VIEWER calls "down", expressed in joint space: the mesh
         node may re-orient the whole scene (Z-up rigs), so push mesh -Y
         through its rest rotation instead of assuming scene -Y. Plus an
         orthonormal basis around it for arm bearings. */
      var MW = nodesRT[meshNode].world;
      var dwn = [-MW[4], -MW[5], -MW[6]];
      var dl0 = Math.hypot(dwn[0], dwn[1], dwn[2]) || 1;
      dwn[0] /= dl0; dwn[1] /= dl0; dwn[2] /= dl0;
      var uB0 = Math.abs(dwn[1]) < 0.9 ? [0, 1, 0] : [1, 0, 0];
      var uB = [uB0[1] * dwn[2] - uB0[2] * dwn[1], uB0[2] * dwn[0] - uB0[0] * dwn[2], uB0[0] * dwn[1] - uB0[1] * dwn[0]];
      var ul0 = Math.hypot(uB[0], uB[1], uB[2]) || 1;
      uB[0] /= ul0; uB[1] /= ul0; uB[2] /= ul0;
      var vB = [dwn[1] * uB[2] - dwn[2] * uB[1], dwn[2] * uB[0] - dwn[0] * uB[2], dwn[0] * uB[1] - dwn[1] * uB[0]];
      if (LAB) console.log('[octo] down(joint space): ' +
        dwn.map(function (x) { return x.toFixed(2); }).join(','));

      /* arm chains. This rig is not eight tidy runs off one root: there is
         a hub bone, arms fork partway, and a few one-bone stubs hang off
         the body. So arms are found as each leaf's PRIVATE path — climb
         from every fingertip toward the body and stop where the skeleton
         becomes shared (a joint with more than one leaf below it). No bone
         names involved; only the head is excluded by name (Tripo labels
         it) and gets a faint sway of its own instead of full arm motion. */
      var jset = new Set(skin.joints);
      var jkids = {};
      skin.joints.forEach(function (ni) { jkids[ni] = []; });
      skin.joints.forEach(function (ni) {
        var p2 = parent[ni];
        if (p2 >= 0 && jset.has(p2)) jkids[p2].push(ni);
      });
      var leafN = {};
      for (var oi = order.length - 1; oi >= 0; oi--) {
        var li = order[oi];
        if (!jset.has(li)) continue;
        var ks = jkids[li], sum = 0;
        for (var ki = 0; ki < ks.length; ki++) sum += leafN[ks[ki]];
        leafN[li] = ks.length ? sum : 1;
      }
      function isHeadJ(ni) { return /head|neck|eye/i.test(j.nodes[ni].name || ''); }
      // one full base-to-tip path per leaf (stop at the hub: 3+ leaves below)
      var paths = [];
      skin.joints.forEach(function (ni) {
        if (jkids[ni].length) return;
        var run = [ni], cur = ni;
        while (true) {
          var p2 = parent[cur];
          if (!(p2 >= 0 && jset.has(p2)) || leafN[p2] >= 3) break;
          cur = p2; run.unshift(cur);
        }
        paths.push(run);
      });
      // longest path claims a fork's shared trunk; the loser keeps its tip.
      // A path that crosses head-named joints donates them to the head sway
      // and its remainder (this rig parents one arm under the head) stays an arm.
      paths.sort(function (a, b) { return b.length - a.length; });
      if (LAB) console.log('[octo] paths: ' + paths.map(function (r) {
        return r.map(function (ni) { return (j.nodes[ni].name || '?').replace(/^tripo::0?_?(Left_)?/, ''); }).join('>');
      }).join(' | '));
      var used = new Set(), armsRaw = [], headRun = null;
      paths.forEach(function (run) {
        var lastH = -1, hi;
        for (hi = 0; hi < run.length; hi++) if (isHeadJ(run[hi])) lastH = hi;
        if (lastH >= 0) {
          if (!headRun) {
            headRun = run.slice(0, lastH + 1);
            headRun.forEach(function (ni) { used.add(ni); });
          }
          run = run.slice(lastH + 1);
        }
        var tail = run.filter(function (ni) { return !used.has(ni); });
        if (!tail.length) return;
        tail.forEach(function (ni) { used.add(ni); });
        armsRaw.push(tail);
      });
      // Accept both the production arm_0_00 convention and the CC0 Blender
      // rig's eight Tentakel.R/L.### chains. Require every joint in the run
      // to be arm-named so a body/eye leaf can never be mistaken for an arm.
      function isArmBoneName(name) {
        var clean = (name || '').replace(/^.*(?:::|\|)/, '').trim();
        return /^arm(?:[._-]?\d+){2}$/i.test(clean) ||
          /^tentakel(?:[._-][lr])?[._-]\d+$/i.test(clean);
      }
      armsRaw = armsRaw.filter(function (run) {
        return run.length && run.every(function (ni) {
          return isArmBoneName(j.nodes[ni].name);
        });
      });
      // the crown centre in JOINT space (ctr is mesh-space; the two differ
      // by the mesh node's transform — bearings must come from joint space)
      var jctr = [0, 0, 0];
      armsRaw.forEach(function (r) {
        var P0 = nodesRT[r[0]].restWP;
        jctr[0] += P0[0] / armsRaw.length; jctr[1] += P0[1] / armsRaw.length; jctr[2] += P0[2] / armsRaw.length;
      });
      function bear(P) { // arm bearing in the plane perpendicular to "down"
        var rx2 = P[0] - jctr[0], ry3 = P[1] - jctr[1], rz2 = P[2] - jctr[2];
        return Math.atan2(rx2 * vB[0] + ry3 * vB[1] + rz2 * vB[2],
          rx2 * uB[0] + ry3 * uB[1] + rz2 * uB[2]);
      }
      armsRaw.sort(function (a, b) {
        return bear(nodesRT[a[a.length - 1]].restWP) - bear(nodesRT[b[b.length - 1]].restWP);
      });
      if (LAB) {
        console.log('[octo] arms: ' + armsRaw.length + (headRun ? ' + head' : '') +
          ' | lens ' + armsRaw.map(function (r) { return r.length; }).join(','));
        var nm = function (ni) { return (j.nodes[ni].name || '?').replace(/^tripo::0?_?/, ''); };
        console.log('[octo] armNames: ' + armsRaw.map(function (r) {
          return nm(r[0]) + '..' + nm(r[r.length - 1]);
        }).join(' | ') + (headRun ? ' | HEAD ' + nm(headRun[0]) + '..' + nm(headRun[headRun.length - 1]) : ''));
      }
      function mkChain(run, k, ph) {
        if (run.length === 1) return { joints: run, s: [0.55], phase: ph, k: k * 0.6, cd: [null], dyn: [{ x: 0, v: 0 }] };
        var sArr = [0], L = 0, q2;
        for (q2 = 1; q2 < run.length; q2++) {
          var A2 = nodesRT[run[q2 - 1]].restWP, B2 = nodesRT[run[q2]].restWP;
          L += Math.hypot(B2[0] - A2[0], B2[1] - A2[1], B2[2] - A2[2]) || 1;
          sArr.push(L);
        }
        for (q2 = 1; q2 < run.length; q2++) sArr[q2] /= (L || 1);
        // each joint's bone direction in its OWN frame (= the next joint's
        // local translation): constant, used per frame to fold toward down.
        // The tip joint continues its incoming segment — it drives the
        // sculpted curl region, which must streamline too when jetting.
        var cd = [];
        for (q2 = 0; q2 < run.length; q2++) {
          if (q2 < run.length - 1) {
            var tv = nodesRT[run[q2 + 1]].t;
            var tl2 = Math.hypot(tv[0], tv[1], tv[2]) || 1;
            cd.push([tv[0] / tl2, tv[1] / tl2, tv[2] / tl2]);
          } else {
            var Pz = nodesRT[run[q2]].restWP, Py = nodesRT[run[q2 - 1]].restWP;
            var dz2 = [Pz[0] - Py[0], Pz[1] - Py[1], Pz[2] - Py[2]];
            var dl2 = Math.hypot(dz2[0], dz2[1], dz2[2]) || 1;
            dz2[0] /= dl2; dz2[1] /= dl2; dz2[2] /= dl2;
            cd.push(Q.rotInv([0, 0, 0], nodesRT[run[q2]].restWQ, dz2));
          }
        }
        // fallback fold axis: keeps the fold inside the arm's own radial
        // plane when a bone is near-vertical and the live axis degenerates.
        // Bearing comes from mid-arm — curled tips lie about the direction.
        // radial = u·cosφ + v·sinφ ; axis = radial × down = u·sinφ − v·cosφ
        var Pm = nodesRT[run[Math.round((run.length - 1) * 0.45)]].restWP;
        var ph2 = bear(Pm), sF = Math.sin(ph2), cF = Math.cos(ph2);
        // each arm aims a touch INSIDE straight-down, so the eight of them
        // converge into one bundle behind the body instead of an open umbrella
        var tg = [dwn[0] - 0.35 * (uB[0] * cF + vB[0] * sF),
                  dwn[1] - 0.35 * (uB[1] * cF + vB[1] * sF),
                  dwn[2] - 0.35 * (uB[2] * cF + vB[2] * sF)];
        var tl3 = Math.hypot(tg[0], tg[1], tg[2]) || 1;
        return {
          joints: run, s: sArr, phase: ph, k: k, cd: cd,
          tg: [tg[0] / tl3, tg[1] / tl3, tg[2] / tl3],
          fa: [uB[0] * sF - vB[0] * cF, uB[1] * sF - vB[1] * cF, uB[2] * sF - vB[2] * cF],
          dyn: run.map(function () { return { x: 0, v: 0 }; })
        };
      }
      var chains = armsRaw.map(function (run, ci) {
        // phase = the arm's bearing around the crown, so the stroke travels
        // arm-to-arm around the animal instead of firing all at once
        return mkChain(run, 0.85 + 0.3 * jit(ci, 2),
          bear(nodesRT[run[run.length - 1]].restWP));
      });
      if (headRun) {
        // the head carries the root of one arm (Tripo skinning bleeds), so
        // it must gather too — at reduced gain, so the mantle only leans
        // into the pose while the arm mesh riding its bones tucks under.
        var hc = mkChain(headRun, 0.22, 0);
        hc.gK = 0.65;
        chains.push(hc);
      }
      var jinfo = {};
      chains.forEach(function (c2) {
        c2.joints.forEach(function (ni, i2) { jinfo[ni] = { c: c2, i: i2 }; });
      });
      creature = {
        count: idx.length,
        posBuf: buf(gl.ARRAY_BUFFER, pos),
        nrmBuf: buf(gl.ARRAY_BUFFER, nrm),
        morphPBuf: morphP.map(function (data) { return buf(gl.ARRAY_BUFFER, data); }),
        morphNBuf: morphN.map(function (data) { return buf(gl.ARRAY_BUFFER, data); }),
        jntBuf: buf(gl.ARRAY_BUFFER, jnt),
        wgtBuf: buf(gl.ARRAY_BUFFER, new Float32Array(wgt)),
        idxBuf: buf(gl.ELEMENT_ARRAY_BUFFER, idx),
        nodesRT: nodesRT, order: order, parent: parent,
        joints: skin.joints, ibm: ibm, meshNode: meshNode,
        jointArr: new Float32Array(skin.joints.length * 16),
        chains: chains, jinfo: jinfo, dw: dwn,
        ctr: ctr, fit: fit,
        clips: clips, hasMorphs: hasMorphs,
        jntGLType: (jnt instanceof Uint8Array) ? gl.UNSIGNED_BYTE : gl.UNSIGNED_SHORT,
        idxGLType: (idx instanceof Uint32Array) ? gl.UNSIGNED_INT : gl.UNSIGNED_SHORT,
        model: M4.ident(),
        tmpA: M4.ident(), tmpB: M4.ident(), meshInv: M4.ident()
      };
      var edges = edgesOf(idx);
      creature.edgeCount = edges.length;
      creature.edgeBuf = buf(gl.ELEMENT_ARRAY_BUFFER, edges);
      if (LAB) { // measure what the fold actually does to every arm tip
        var svT = labTrail, nm2 = function (r) { return (j.nodes[r[0]].name || '?').replace(/^tripo::0?_?/, ''); };
        var tipW = function (c2) {
          var W = nodesRT[c2.joints[c2.joints.length - 1]].world;
          return W[12] * dwn[0] + W[13] * dwn[1] + W[14] * dwn[2];
        };
        labTrail = 0; boneWave(5);
        var y0 = chains.map(tipW);
        labTrail = 1; boneWave(5);
        console.log('[octo] fold (+ = tucked): ' + chains.map(function (c2, i2) {
          return nm2(c2.joints) + ' ' + (tipW(c2) - y0[i2]).toFixed(2);
        }).join(' | '));
        labTrail = svT;
      }
      if (reduce) frame(LABT * 1000);
    })
    .catch(function (e) { console.warn('octopus glb failed', e); });

  /* the swim from the start of this build, grown up: every joint of every
     arm rides a travelling two-frequency curl wave about its own bend axis
     (rest ⊗ rotX — the way this rig was authored to bend, so the mesh never
     tears), phase-staggered per arm, amplitude peaking mid-arm so the
     sculpted tip curls follow through. It swims harder while travelling. */
  var modeCur = 0, breathCur = 0;
  var inputVelocity = 0, scrollDirection = 1, motionPhase = 0;

  /* ══════════ locomotion state machine ══════════
     Scroll sets DESIRED VELOCITY and PROPULSION ENERGY; the creature swims
     through explicit phases:

         idle → anticipation → power stroke → glide → settle
                               ↓ (opposite input while moving)
                        brake → 3D turn → accelerate (anticipation again)

     Position INTEGRATES velocity — nothing eases toward a target, nothing
     drifts back to an old anchor. Repeated same-direction input tops up
     energy and CONTINUES the current phase (glide chains into the next
     anticipation at its natural end — no restart). Reversal brakes first
     (umbrella flare, speed dies), then turns through a curved yaw/bank arc
     in 3D (never a flat in-plane spin), then accelerates the new way. */
  var LS = {
    state: 'idle',
    t: 0,                 // seconds in state
    dir: 1,               // +1 travels down-screen, -1 up
    queuedDir: 0,         // set while braking/turning
    speed: 0,             // NDC/s, unsigned
    desired: 0,           // target speed from input rate
    energy: 0,            // propulsion reservoir 0..1
    clipPhase: 0,         // position on the swim clip 0..1
    blend: 0,             // swim-clip weight over idle
    alt: 0, strokes: 0,   // swim/swimB alternation
    travelA: -Math.PI / 2, // mantle-axis angle: +90° up, -90° down
    aFrom: -Math.PI / 2, aTo: -Math.PI / 2,
    yawSwing: 0, bank: 0, // 3D turn presentation
    side: 1,              // which way the turn arcs
    brakeEnv: 0,          // drives the flipLag arm-inertia overlay
    turnRecovery: 0,      // bridges the open turn crown into the next intake
    resumeHold: 0,        // cancellation preload; prevents an instant jet pop
    offX: 0, offY: 0      // persistent swim offsets from the track anchor
  };
  var TURN_MIN_S = 0.65, TURN_MAX_S = 2.20;
  var BRAKE_MIN_S = 0.22, BRAKE_MAX_S = 0.50;
  var directionTransition = window.OctadockDirectionTransition;
  if (!directionTransition || typeof directionTransition.resolveDirectionTransition !== 'function') {
    throw new Error('DirectionTransition must load before journey.js.');
  }
  var inRate = 0;         // smoothed |input| px/s
  var pendingDeltaPx = 0, pendingAbsPx = 0, pendingDir = 0;
  var directionIntentPx = 0, directionIntentThresholdPx = 10;
  var lastInputMs = -1e9, lastInputDir = 1;
  var rawDeltaPx = 0, rawMagnitudePx = 0, inputActive = false, inputAgeMs = Infinity;
  var INPUT_EPS_PX = 0.5;
  /* legacy aliases still read by the ?wave path and clip sampler */
  var dirSign = 1, screenOff = 0, screenVelocity = 0, effort = 0;
  var strokeAlt = 0, strokeBlend = 0, strokePhase = 0, strokeActive = false;
  var labTrail = q.has('trail') ? parseFloat(q.get('trail')) : null;
  var IDQ = [0, 0, 0, 1], bwV = [0, 0, 0], bwA = [0, 0, 0], bwQ = [0, 0, 0, 1];
  var physicsT = null;

  // One stable object shared with the external V10 renderer. Consumers may
  // retain references to its typed arrays; every frame mutates them in place.
  var V10_BRIDGE = {
    version: 1,
    schema: 'octadock.external-octopus.v10/1',
    enabled: EXTERNAL_OCTOPUS_V10,
    revision: 0,
    time: 0,
    deltaTime: 0,
    visible: false,
    inside: 0,
    pageProgress: 0,
    pageTarget: 0,
    viewport: {
      cssWidth: 0,
      cssHeight: 0,
      pixelWidth: 0,
      pixelHeight: 0,
      dpr: 1
    },
    camera: {
      projection: new Float32Array(16),
      view: new Float32Array(16),
      viewProjection: new Float32Array(16),
      position: new Float32Array(3),
      target: new Float32Array(3),
      up: new Float32Array([0, 1, 0]),
      fovRadians: 40 * Math.PI / 180,
      near: 0.1,
      far: 120,
      aspect: 1
    },
    environment: {
      fogColor: new Float32Array(3),
      density: 0
    },
    // The Three.js rig renders into a transparent target on the same WebGL2
    // context. Its raw texture is synchronously composed into rtScene below,
    // before marine snow, bloom and the shared ACES grade.
    composite: {
      supported: true,
      ready: false,
      texture: null,
      width: 0,
      height: 0
    },
    creature: {
      position: new Float32Array(3),
      rotation: new Float32Array(3),
      rotationOrder: 'YXZ',
      scale: 1,
      trackScale: 1,
      configScale: 1,
      anchorPosition: new Float32Array(3),
      anchorNdc: new Float32Array(2),
      ndc: new Float32Array(2),
      worldVelocity: new Float32Array(3),
      ambientGain: 1,
      motionHold: 0
    },
    locomotion: {
      state: LS.state,
      t: LS.t,
      dir: LS.dir,
      queuedDir: LS.queuedDir,
      speed: LS.speed,
      desired: LS.desired,
      energy: LS.energy,
      clipPhase: LS.clipPhase,
      blend: LS.blend,
      alt: LS.alt,
      strokes: LS.strokes,
      travelA: LS.travelA,
      aFrom: LS.aFrom,
      aTo: LS.aTo,
      yawSwing: LS.yawSwing,
      bank: LS.bank,
      side: LS.side,
      brakeEnv: LS.brakeEnv,
      turnRecovery: LS.turnRecovery,
      resumeHold: LS.resumeHold,
      offX: LS.offX,
      offY: LS.offY,
      physicalSpeed: 0,
      physicalHeadingX: 0,
      physicalHeadingY: -LS.dir,
      physicalAngularVelocity: 0,
      physicalTurnProgress: 0,
      physicalTurnEnvelope: 0,
      physicalTurnSettled: false,
      rawDeltaPx: 0,
      inputMagnitudePx: 0,
      inputActive: false,
      inputAgeMs: Infinity,
      inputDirection: 0,
      lastInputDirection: lastInputDir,
      effort: 0,
      mode: 0,
      scrollDirection: scrollDirection,
      screenVelocity: 0,
      strokeActive: false,
      strokePhase: 0,
      strokeBlend: 0,
      strokeClip: 'swim',
      active: false,
      jetActive: false,
      propulsionStyle: 'arm',
      travelSign: LS.dir,
      navigationSpeed: 0,
      thrust: 0,
      steer: 0,
      turning: false
    }
  };
  window.__OCTOPUS_JOURNEY__ = V10_BRIDGE;
  var journeyBeforePostEvent = new Event('octadock:journey-before-post');
  var journeyRenderedEvent = new Event('octadock:journey-rendered');
  canvas.dataset.journeyStatus = 'bridge';

  function boneWave(tt) {
    var o = creature, i;
    var pdt = physicsT === null ? 1 / 60 : tt - physicsT;
    var resetDynamics = FREEZE !== null || pdt < 0 || pdt > 0.22;
    pdt = Math.min(1 / 30, Math.max(1 / 240, pdt));
    physicsT = tt;
    if (resetDynamics && FREEZE !== null) motionPhase = tt * CFG.drift * scrollDirection;
    else motionPhase += pdt * CFG.drift * (0.28 + modeCur * 0.72) * scrollDirection;
    var T = motionPhase;
    var amp = (CFG.amp / 40) * 0.24 * (1 + modeCur * 0.4) * (1 + breathCur * 2);
    // the swim posture: arms folded together under the body — the jet pose.
    // It breathes with a slow pulse (contract → glide) and tightens to a
    // bullet while the creature actually travels.
    var trailT = labTrail !== null ? labTrail
      : (0.06 + 0.88 * modeCur) * (1 + 0.06 * Math.sin(T * 0.5));
    // one parents-first walk, tracking each node's ANIMATED world
    // orientation (aq), so every fold axis is measured against where the
    // arm actually is this frame — deep folds stay aimed at "under the
    // body" instead of corkscrewing off precomputed rest directions.
    for (i = 0; i < o.order.length; i++) {
      var ni = o.order[i], nd = o.nodesRT[ni];
      var pq = o.parent[ni] >= 0 ? o.nodesRT[o.parent[ni]].aq : IDQ;
      var ji = o.jinfo[ni];
      if (!ji) { Q.mul(nd.aq, pq, nd.rest); continue; }
      var c = ji.c, s = c.s[ji.i];
      var e = s * s * (3 - 2 * s);
      var env = 0.15 + 0.85 * e * (1 - 0.4 * Math.min(1, Math.max(0, (s - 0.65) / 0.35)));
      nd.r[0] = nd.rest[0]; nd.r[1] = nd.rest[1]; nd.r[2] = nd.rest[2]; nd.r[3] = nd.rest[3];
      if (c.cd[ji.i] && trailT > 1e-4) {
        // the fold lives at the SHOULDER: strong at the base (that is what
        // tucks the arm under the body), easing toward the tip so the
        // sculpted curls survive at rest — but a jetting octopus streamlines
        // its tips, so the taper relaxes as the trail pose deepens.
        var gs = Math.min(1, Math.max(0, (s - 0.5) / 0.5));
        var gEnv = 0.85 - (0.45 - 0.30 * Math.min(1, trailT)) * gs * gs * (3 - 2 * gs);
        Q.mul(nd.aq, pq, nd.rest);              // orientation before any delta
        Q.rot(bwV, nd.aq, c.cd[ji.i]);          // bone direction, world
        // fold as far as the bone is from hanging straight down — an arm
        // pointing up folds hardest, one already trailing folds not at all
        var dw = c.tg; // this arm's bundle direction (down + a bit inward)
        var dotd = bwV[0] * dw[0] + bwV[1] * dw[1] + bwV[2] * dw[2];
        var need = Math.min(1, Math.acos(Math.max(-1, Math.min(1, dotd))) / 1.05);
        var axx = bwV[1] * dw[2] - bwV[2] * dw[1] + c.fa[0] * 0.35; // bone × target
        var axy = bwV[2] * dw[0] - bwV[0] * dw[2] + c.fa[1] * 0.35;
        var axz = bwV[0] * dw[1] - bwV[1] * dw[0] + c.fa[2] * 0.35;
        var m2 = Math.hypot(axx, axy, axz);
        var GG = 0.9 * (c.gK || 1) * trailT * gEnv * need;
        if (GG > 1e-4 && m2 > 1e-5) {
          bwA[0] = axx / m2; bwA[1] = axy / m2; bwA[2] = axz / m2;
          Q.rotInv(bwV, nd.aq, bwA);            // fold axis in the joint frame
          // drop twist = the component along the BONE (rigs don't promise
          // bones lie on any particular local axis), keep the pure bend
          var cdL = c.cd[ji.i];
          var dp = bwV[0] * cdL[0] + bwV[1] * cdL[1] + bwV[2] * cdL[2];
          bwV[0] -= dp * cdL[0]; bwV[1] -= dp * cdL[1]; bwV[2] -= dp * cdL[2];
          var mL = Math.hypot(bwV[0], bwV[1], bwV[2]) || 1;
          var h2 = GG / 2, sh = Math.sin(h2) / mL;
          bwQ[0] = sh * bwV[0]; bwQ[1] = sh * bwV[1]; bwQ[2] = sh * bwV[2]; bwQ[3] = Math.cos(h2);
          Q.mul(nd.r, nd.rest, bwQ);
        }
      }
      var targetAng = amp * c.k * env * (
        Math.sin(T * 1.15 - s * (4.6 + 1.4 * trailT) + c.phase) +
        0.55 * Math.sin(T * 0.53 - s * 2.4 + c.phase * 0.7)
      );
      // Purpose-built secondary dynamics: each joint is a damped angular
      // spring, with softer tips. The parent wave is coupled forward so motion
      // travels down the arm and settles instead of snapping between poses.
      var dyn = c.dyn[ji.i];
      if (ji.i > 0) targetAng += c.dyn[ji.i - 1].x * (0.08 + 0.10 * s);
      if (resetDynamics) {
        dyn.x = targetAng; dyn.v = 0;
      } else {
        var stiffness = 24 - 11 * s;
        var damping = 7.8 - 2.6 * s;
        dyn.v += (targetAng - dyn.x) * stiffness * pdt;
        dyn.v *= Math.exp(-damping * pdt);
        dyn.x += dyn.v * pdt;
      }
      var ang = dyn.x;
      Q.mulAxisX(nd.r, nd.r, ang);
      Q.mul(nd.aq, pq, nd.r);
    }
    for (i = 0; i < o.order.length; i++) {
      var ni2 = o.order[i], n2 = o.nodesRT[ni2];
      M4.compose(n2.local, n2.t, n2.r, n2.s);
      if (o.parent[ni2] >= 0) M4.mul(n2.world, o.nodesRT[o.parent[ni2]].world, n2.local);
      else n2.world.set(n2.local);
    }
    M4.invert(o.meshInv, o.nodesRT[o.meshNode].world);
    for (i = 0; i < o.joints.length; i++) {
      M4.mul(o.tmpA, o.meshInv, o.nodesRT[o.joints[i]].world);
      M4.mul(o.tmpB, o.tmpA, o.ibm.subarray(i * 16, i * 16 + 16));
      o.jointArr.set(o.tmpB, i * 16);
    }
  }

  /* ══════════ Blender clip playback ══════════
     Authored Blender actions (idle / swim / swimB / flipLag) sampled at
     runtime instead of the procedural wave: idle↔swim crossfade follows
     scroll speed, and direction reversals add the flipLag arm-inertia
     overlay while the body flips (body-led turn). The wave stays available
     behind ?wave=1 for A/B comparison. */
  var idleClock = 0, lastPoseT = null;
  var turnEnvCur = 0;                     // set by the frame controller
  var csA = [0, 0, 0, 1], csB = [0, 0, 0, 1], csD = [0, 0, 0, 1];
  function qNlerpTo(o, b, t) {            // o = nlerp(o, b, t)
    var d = o[0] * b[0] + o[1] * b[1] + o[2] * b[2] + o[3] * b[3];
    var s = d < 0 ? -t : t, u = 1 - t;
    var x = o[0] * u + b[0] * s, y = o[1] * u + b[1] * s,
        z = o[2] * u + b[2] * s, w = o[3] * u + b[3] * s;
    var m = Math.hypot(x, y, z, w) || 1;
    o[0] = x / m; o[1] = y / m; o[2] = z / m; o[3] = w / m;
    return o;
  }
  function sampleChan(ch, t, out) {
    var times = ch.times, n = times.length, c;
    if (t <= times[0]) { for (c = 0; c < ch.comps; c++) out[c] = ch.vals[c]; return; }
    if (t >= times[n - 1]) {
      var last = (n - 1) * ch.comps;
      for (c = 0; c < ch.comps; c++) out[c] = ch.vals[last + c];
      return;
    }
    var lo = 0, hi = n - 1;
    while (hi - lo > 1) { var mid = (lo + hi) >> 1; if (times[mid] <= t) lo = mid; else hi = mid; }
    var f = (t - times[lo]) / ((times[hi] - times[lo]) || 1e-6);
    var A = lo * ch.comps, B = hi * ch.comps;
    if (ch.comps === 4) {
      var d = ch.vals[A] * ch.vals[B] + ch.vals[A + 1] * ch.vals[B + 1] +
              ch.vals[A + 2] * ch.vals[B + 2] + ch.vals[A + 3] * ch.vals[B + 3];
      var s = d < 0 ? -f : f, u = 1 - f;
      var x = ch.vals[A] * u + ch.vals[B] * s, y = ch.vals[A + 1] * u + ch.vals[B + 1] * s,
          z = ch.vals[A + 2] * u + ch.vals[B + 2] * s, w = ch.vals[A + 3] * u + ch.vals[B + 3] * s;
      var m = Math.hypot(x, y, z, w) || 1;
      out[0] = x / m; out[1] = y / m; out[2] = z / m; out[3] = w / m;
    } else {
      for (c = 0; c < 3; c++) out[c] = ch.vals[A + c] * (1 - f) + ch.vals[B + c] * f;
    }
  }
  function applyClip(o, clip, t, w) {     // blend a clip into current node pose
    for (var ci = 0; ci < clip.chans.length; ci++) {
      var ch = clip.chans[ci], nd = o.nodesRT[ch.node];
      if (!nd) continue;
      sampleChan(ch, t, csA);
      if (ch.path === 'rotation') {
        if (w >= 1) { nd.r[0] = csA[0]; nd.r[1] = csA[1]; nd.r[2] = csA[2]; nd.r[3] = csA[3]; }
        else qNlerpTo(nd.r, csA, w);
      } else if (ch.path === 'translation') {
        nd.t[0] += (csA[0] - nd.t[0]) * w; nd.t[1] += (csA[1] - nd.t[1]) * w; nd.t[2] += (csA[2] - nd.t[2]) * w;
      } else {
        nd.s[0] += (csA[0] - nd.s[0]) * w; nd.s[1] += (csA[1] - nd.s[1]) * w; nd.s[2] += (csA[2] - nd.s[2]) * w;
      }
    }
  }
  function applyClipAdditive(o, clip, t, w) {  // rest-relative rotation overlay
    if (w <= 1e-4) return;
    for (var ci = 0; ci < clip.chans.length; ci++) {
      var ch = clip.chans[ci];
      if (ch.path !== 'rotation') continue;
      var nd = o.nodesRT[ch.node];
      if (!nd) continue;
      sampleChan(ch, t, csA);
      // delta = conj(rest) ⊗ sample, scaled to weight, applied on current
      var r = nd.rest;
      csB[0] = -r[0]; csB[1] = -r[1]; csB[2] = -r[2]; csB[3] = r[3];
      Q.mul(csD, csB, csA);
      csB[0] = 0; csB[1] = 0; csB[2] = 0; csB[3] = 1;
      qNlerpTo(csB, csD, w);
      Q.mul(nd.r, nd.r.slice(), csB);
    }
  }
  function clipPose(tt) {
    var o = creature;
    var dt = lastPoseT === null ? 1 / 60 : Math.min(1 / 20, Math.max(0, tt - lastPoseT));
    lastPoseT = tt;
    if (FREEZE !== null) idleClock = LABT;
    else idleClock += dt;
    var i, ni, nd;
    // start from rest each frame
    for (i = 0; i < o.order.length; i++) {
      ni = o.order[i]; nd = o.nodesRT[ni];
      nd.r[0] = nd.rest[0]; nd.r[1] = nd.rest[1]; nd.r[2] = nd.rest[2]; nd.r[3] = nd.rest[3];
      nd.t[0] = nd.t0[0]; nd.t[1] = nd.t0[1]; nd.t[2] = nd.t0[2];
      nd.s[0] = nd.s0[0]; nd.s[1] = nd.s0[1]; nd.s[2] = nd.s0[2];
    }
    var idle = o.clips.idle;
    var swim = strokeAlt && o.clips.swimB ? o.clips.swimB : o.clips.swim;
    var lag = o.clips.flipLag;
    var wSwim = Math.min(1, Math.max(0, strokeBlend));
    if (idle) applyClip(o, idle, idleClock % idle.dur, 1);
    if (swim && wSwim > 1e-3) {
      var swimT = FREEZE !== null ? LABT % swim.dur : Math.min(0.999, strokePhase) * swim.dur;
      applyClip(o, swim, swimT, wSwim);
    }
    if (lag) applyClipAdditive(o, lag, Math.min(1, turnEnvCur) * lag.dur * 0.999, Math.sin(Math.PI * Math.min(1, turnEnvCur)));
    // compose world matrices + joint palette (same tail as boneWave)
    for (i = 0; i < o.order.length; i++) {
      ni = o.order[i]; nd = o.nodesRT[ni];
      M4.compose(nd.local, nd.t, nd.r, nd.s);
      if (o.parent[ni] >= 0) M4.mul(nd.world, o.nodesRT[o.parent[ni]].world, nd.local);
      else nd.world.set(nd.local);
    }
    M4.invert(o.meshInv, o.nodesRT[o.meshNode].world);
    for (i = 0; i < o.joints.length; i++) {
      M4.mul(o.tmpA, o.meshInv, o.nodesRT[o.joints[i]].world);
      M4.mul(o.tmpB, o.tmpA, o.ibm.subarray(i * 16, i * 16 + 16));
      o.jointArr.set(o.tmpB, i * 16);
    }
  }

  /* model matrix: T(pos) · Ry · Rx · Rz · S(fit·scale) · T(−ctr).
     Also keeps the rotation rows so world→creature-local is one transpose. */
  var creatureRot = [1, 0, 0, 0, 1, 0, 0, 0, 1];
  function creatureModel(px, py, pz, ry, rx, rz, s) {
    var o = creature, m = o.model;
    var cy = Math.cos(ry), sy = Math.sin(ry), cx = Math.cos(rx), sx = Math.sin(rx), cz = Math.cos(rz), sz = Math.sin(rz);
    // R = Ry·Rx·Rz (column-major)
    var r00 = cy * cz + sy * sx * sz, r01 = cx * sz, r02 = -sy * cz + cy * sx * sz;
    var r10 = -cy * sz + sy * sx * cz, r11 = cx * cz, r12 = sy * sz + cy * sx * cz;
    var r20 = sy * cx, r21 = -sx, r22 = cy * cx;
    var k = o.fit * s;
    var cxr = o.ctr[0], cyr = o.ctr[1], czr = o.ctr[2];
    m[0] = r00 * k; m[1] = r01 * k; m[2] = r02 * k; m[3] = 0;
    m[4] = r10 * k; m[5] = r11 * k; m[6] = r12 * k; m[7] = 0;
    m[8] = r20 * k; m[9] = r21 * k; m[10] = r22 * k; m[11] = 0;
    m[12] = px - k * (r00 * cxr + r10 * cyr + r20 * czr);
    m[13] = py - k * (r01 * cxr + r11 * cyr + r21 * czr);
    m[14] = pz - k * (r02 * cxr + r12 * cyr + r22 * czr);
    m[15] = 1;
    creatureRot[0] = r00; creatureRot[1] = r01; creatureRot[2] = r02;
    creatureRot[3] = r10; creatureRot[4] = r11; creatureRot[5] = r12;
    creatureRot[6] = r20; creatureRot[7] = r21; creatureRot[8] = r22;
    return m;
  }
  function worldToCreature(o, v) { // rotation transpose (orthonormal)
    o[0] = creatureRot[0] * v[0] + creatureRot[1] * v[1] + creatureRot[2] * v[2];
    o[1] = creatureRot[3] * v[0] + creatureRot[4] * v[1] + creatureRot[5] * v[2];
    o[2] = creatureRot[6] * v[0] + creatureRot[7] * v[1] + creatureRot[8] * v[2];
    return o;
  }

  var creaturePlacement = {
    position: [0, 0, 0],
    rotation: [0, 0, 0], // [rx, ry, rz], composed as Ry * Rx * Rz
    scale: 1,
    anchorNdc: [0, 0],
    ndc: [0, 0],
    ambientGain: 1,
    motionHold: 0
  };

  /* This is the one placement calculation used by both the legacy draw path
     and the external V10 bridge. Keeping it here prevents the overlay from
     developing a subtly different camera-relative swim path. */
  function updateCreaturePlacement(tt, dtt) {
    var ax = octoK[0], ay = octoK[1], az = octoK[2];
    var clipX = viewProj[0] * ax + viewProj[4] * ay + viewProj[8] * az + viewProj[12];
    var clipY = viewProj[1] * ax + viewProj[5] * ay + viewProj[9] * az + viewProj[13];
    var clipW = viewProj[3] * ax + viewProj[7] * ay + viewProj[11] * az + viewProj[15];
    var anchorNdcX = EXTERNAL_OCTOPUS_V10
      ? compositionNdc[0]
      : (clipW > 1e-6 ? clipX / clipW : 0);
    var anchorNdcY = EXTERNAL_OCTOPUS_V10
      ? compositionNdc[1]
      : (clipW > 1e-6 ? clipY / clipW : 0);
    var motionHold = Math.min(1, Math.max(inputActive ? 1 : 0, effort,
      strokeBlend * 0.85, LS.state === 'turn' || LS.state === 'brake' ? 1 : 0));
    var ambientGain = 1 - motionHold * 0.94;
    var ndcX = anchorNdcX + (EXTERNAL_OCTOPUS_V10 ? 0 : LS.offX)
      + Math.sin(tt * 0.2) * 0.012 * ambientGain * (EXTERNAL_OCTOPUS_V10 ? 0 : 1);
    var ndcY = anchorNdcY + (EXTERNAL_OCTOPUS_V10 ? 0 : LS.offY)
      + Math.sin(tt * 0.42) * 0.008 * ambientGain * (EXTERNAL_OCTOPUS_V10 ? 0 : 1);
    var wz = az + Math.cos(tt * 0.16) * 0.08 * ambientGain * (EXTERNAL_OCTOPUS_V10 ? 0 : 1);

    // Solve world X and Y together at the chosen depth. This is the exact
    // perspective solve historically used by the in-canvas animal.
    var a11 = viewProj[0] - ndcX * viewProj[3];
    var a12 = viewProj[4] - ndcX * viewProj[7];
    var b1 = ndcX * (viewProj[11] * wz + viewProj[15]) - (viewProj[8] * wz + viewProj[12]);
    var a21 = viewProj[1] - ndcY * viewProj[3];
    var a22 = viewProj[5] - ndcY * viewProj[7];
    var b2 = ndcY * (viewProj[11] * wz + viewProj[15]) - (viewProj[9] * wz + viewProj[13]);
    var detXY = a11 * a22 - a12 * a21;
    var wx = ax, wy = ay;
    if (Math.abs(detXY) > 1e-7) {
      wx = (b1 * a22 - a12 * b2) / detXY;
      wy = (a11 * b2 - b1 * a21) / detXY;
    }

    if (EXTERNAL_OCTOPUS_V10 || creature) {
      if (havePrev) {
        var velAlpha = 1 - Math.exp(-3.7 * dtt);
        velW[0] += ((wx - prevOcto[0]) / dtt - velW[0]) * velAlpha;
        velW[1] += ((wy - prevOcto[1]) / dtt - velW[1]) * velAlpha;
        velW[2] += ((wz - prevOcto[2]) / dtt - velW[2]) * velAlpha;
      }
      prevOcto[0] = wx; prevOcto[1] = wy; prevOcto[2] = wz;
      havePrev = true;
    }

    var ry = 0.20 + LS.yawSwing + Math.sin(tt * 0.13) * 0.05 * ambientGain + mx * 0.08 * (1 - effort * 0.7);
    var rx = 0.08 + Math.sin(tt * 0.1) * 0.025 * ambientGain;
    var rz = (Math.PI / 2 - LS.travelA) + LS.bank;
    // Whole-body scale must stay inertial. Mantle inflation belongs to the
    // hydrostat deformation, not a sinusoidal scale on the entire animal.
    var scale = CFG.scale * octoS * (EXTERNAL_OCTOPUS_V10
      ? 1
      : (1 + Math.sin(tt * 0.7) * 0.015));

    creaturePlacement.position[0] = wx;
    creaturePlacement.position[1] = wy;
    creaturePlacement.position[2] = wz;
    creaturePlacement.rotation[0] = rx;
    creaturePlacement.rotation[1] = ry;
    creaturePlacement.rotation[2] = rz;
    creaturePlacement.scale = scale;
    creaturePlacement.anchorNdc[0] = anchorNdcX;
    creaturePlacement.anchorNdc[1] = anchorNdcY;
    creaturePlacement.ndc[0] = ndcX;
    creaturePlacement.ndc[1] = ndcY;
    creaturePlacement.ambientGain = ambientGain;
    creaturePlacement.motionHold = motionHold;
    return creaturePlacement;
  }

  function publishV10Bridge(tt, dtt, inside, desiredDirection, placement) {
    var b = V10_BRIDGE;
    if (canvas.dataset.journeyStatus !== 'running') canvas.dataset.journeyStatus = 'running';
    b.revision++;
    b.time = tt;
    b.deltaTime = dtt;
    b.visible = EXTERNAL_OCTOPUS_V10 && inside < 0.985;
    b.inside = inside;
    b.pageProgress = p;
    b.pageTarget = target;

    b.viewport.cssWidth = innerWidth;
    b.viewport.cssHeight = innerHeight;
    b.viewport.pixelWidth = canvas.width;
    b.viewport.pixelHeight = canvas.height;
    b.viewport.dpr = DPR;

    b.camera.projection.set(proj);
    b.camera.view.set(view);
    b.camera.viewProjection.set(viewProj);
    b.camera.position.set(camPos);
    b.camera.target.set(lookK);
    b.camera.aspect = innerWidth / Math.max(1, innerHeight);

    b.environment.fogColor.set(fogC);
    b.environment.density = CFG.fog;

    b.creature.position.set(placement.position);
    b.creature.rotation.set(placement.rotation);
    b.creature.scale = placement.scale;
    b.creature.trackScale = octoS;
    b.creature.configScale = CFG.scale;
    b.creature.anchorPosition.set(octoK);
    b.creature.anchorNdc.set(placement.anchorNdc);
    b.creature.ndc.set(placement.ndc);
    b.creature.worldVelocity.set(velW);
    b.creature.ambientGain = placement.ambientGain;
    b.creature.motionHold = placement.motionHold;

    var l = b.locomotion;
    l.state = LS.state;
    l.t = LS.t;
    l.dir = LS.dir;
    l.queuedDir = LS.queuedDir;
    l.speed = LS.speed;
    l.desired = LS.desired;
    l.energy = LS.energy;
    l.clipPhase = LS.clipPhase;
    l.blend = LS.blend;
    l.alt = LS.alt;
    l.strokes = LS.strokes;
    l.travelA = LS.travelA;
    l.aFrom = LS.aFrom;
    l.aTo = LS.aTo;
    l.yawSwing = LS.yawSwing;
    l.bank = LS.bank;
    l.side = LS.side;
    l.brakeEnv = LS.brakeEnv;
    l.turnRecovery = LS.turnRecovery;
    l.resumeHold = LS.resumeHold;
    l.offX = LS.offX;
    l.offY = LS.offY;
    l.rawDeltaPx = rawDeltaPx;
    l.inputMagnitudePx = rawMagnitudePx;
    l.inputActive = inputActive;
    l.inputAgeMs = inputAgeMs;
    l.inputDirection = desiredDirection;
    l.lastInputDirection = lastInputDir;
    l.effort = effort;
    l.mode = modeCur;
    l.scrollDirection = scrollDirection;
    l.screenVelocity = screenVelocity;
    l.strokeActive = strokeActive;
    l.strokePhase = strokePhase;
    l.strokeBlend = strokeBlend;
    l.strokeClip = strokeAlt ? 'swimB' : 'swim';
    l.turning = LS.state === 'turn' || LS.state === 'brake';
    l.active = LS.state !== 'idle' && LS.state !== 'settle';
    l.jetActive = LS.state === 'anticipate' || LS.state === 'power' ||
      LS.state === 'glide' || LS.state === 'brake';
    l.propulsionStyle = l.jetActive ? 'jet' : 'arm';
    l.travelSign = LS.dir;
    l.navigationSpeed = Math.min(1, modeCur);
    l.thrust = l.jetActive ? LS.dir * Math.max(effort, LS.energy) : 0;
    l.steer = LS.yawSwing;
  }

  /* ══════════════════════════ bloom (ours) ════════════════════════════ */
  var rtScene = makeTarget(2, 2, true), rtA = makeTarget(2, 2, false), rtB = makeTarget(2, 2, false);
  var blurP = prog(
    'in vec2 aP; out vec2 vUv; void main(){ vUv = aP*0.5+0.5; gl_Position = vec4(aP,0.0,1.0); }',
    'in vec2 vUv; out vec4 O; uniform sampler2D uT; uniform vec2 uDir;' +
    'void main(){ vec4 s = vec4(0.0);' +
    '  s += texture(uT,vUv)*0.227;' +
    '  s += texture(uT,vUv+uDir)*0.194; s += texture(uT,vUv-uDir)*0.194;' +
    '  s += texture(uT,vUv+uDir*2.4)*0.121; s += texture(uT,vUv-uDir*2.4)*0.121;' +
    '  s += texture(uT,vUv+uDir*4.0)*0.054; s += texture(uT,vUv-uDir*4.0)*0.054;' +
    '  O = s; }');
  var octopusCompositeP = prog(
    'in vec2 aP; out vec2 vUv; void main(){ vUv=aP*0.5+0.5; gl_Position=vec4(aP,0.0,1.0); }',
    'in vec2 vUv; out vec4 O; uniform sampler2D uOctopus;' +
    'void main(){ O=texture(uOctopus,vUv); }');
  var compP = prog(
    'in vec2 aP; out vec2 vUv; void main(){ vUv = aP*0.5+0.5; gl_Position = vec4(aP,0.0,1.0); }',
    'in vec2 vUv; out vec4 O; uniform sampler2D uScene; uniform sampler2D uBloom; uniform float uStrength;' +
    'vec3 aces(vec3 x){ return clamp((x*(2.51*x+0.03))/(x*(2.43*x+0.59)+0.14), 0.0, 1.0); }' +
    'void main(){' +
    '  vec3 c = texture(uScene,vUv).rgb + texture(uBloom,vUv).rgb*uStrength;' +
    '  c = aces(c * 1.1);' +
    '  O = vec4(pow(c, vec3(1.0/2.2)), 1.0);' +
    '}');

  /* ══════════════════════════ camera track ════════════════════════════ */
  var KEYS = [];
  var COMPOSITION_KEYS = [];
  var compositionNdc = [0.48, 0.12];
  var screenWin = { s: 0.4, a: 0.45, b: 0.5, c: 0.6, d: 0.65 };
  function K(p, cam, look, oc, os) { return { p: p, cam: cam, look: look, octo: oc, os: os }; }
  function buildTrack() {
    var H = journeyEl.offsetHeight - innerHeight;
    var pOf = function (id, frac) {
      var el = document.getElementById(id) || journeyEl.querySelector('[data-beat="' + id + '"]');
      if (!el) return 0;
      return Math.min(1, Math.max(0, (el.offsetTop + frac * el.offsetHeight - innerHeight * frac) / (H || 1)));
    };
    KEYS = [
      K(0.0,                     v3(0, 12, 9),                 v3(1.4, 10.7, 0),      v3(2.8, 10.6, 2.5),   1.00),
      K(pOf('top', 0.9),         v3(0.3, 6.5, 8.7),            v3(1.5, 3.4, 0.5),     v3(2.6, 3.0, 2.2),    1.00),
      K(pOf('statement', 0.55),  v3(0.4, -1.8, 8.4),           v3(3.6, -7.2, 1.0),    v3(3.9, -7.5, 1.3),   1.01),
      K(pOf('tools', 0.35),      v3(0.2, -6.6, 7.8),           v3(1.5, -7.7, 1.4),    v3(4.1, -7.8, 1.5),   1.00),
      K(pOf('tools', 0.85),      v3(-0.4, -9.5, 8.1),          v3(1.2, -9.6, 1.2),    v3(3.8, -9.4, 1.3),   1.00),
      K(pOf('screen', 0.18),     v3(0.25, -14.75, SP[2] + 6.4), v3(0, -15.75, SP[2]), v3(3.6, -13.9, -0.6), 0.84),
      K(pOf('screen', 0.34),     v3(0.08, -15.30, SP[2] + 3.6), v3(0, -15.66, SP[2]), v3(4.0, -14.4, -1.6), 0.78),
      K(pOf('screen', 0.46),     v3(SP[0], SP[1], SP[2] + DIVE_D), v3(SP[0], SP[1], SP[2]), v3(4.4, -14.6, -2.2), 0.72),
      K(pOf('screen', 0.72),     v3(SP[0], SP[1], SP[2] + DIVE_D), v3(SP[0], SP[1], SP[2]), v3(4.4, -14.6, -2.2), 0.72),
      K(pOf('screen', 0.97),     v3(-0.3, -17.6, 7.4),         v3(-0.2, -20.5, 0.5),  v3(-1.4, -19.4, 0.8), 0.96),
      K(pOf('cloud', 0.3),       v3(-0.6, -21.4, 8.2),         v3(0, -23.4, 0),       v3(-3.8, -22.4, 0.6), 0.97),
      K(pOf('cloud', 0.85),      v3(-0.2, -24.2, 8.4),         v3(0.4, -25.6, 0),     v3(-3.2, -25.2, 0.9), 0.99),
      K(pOf('close', 0.4),       v3(0, -27.4, 8.6),            v3(-0.6, -29.3, 0.5),  v3(-4.1, -28.85, 0.7), 0.98),
      K(1.0,                     v3(0.2, -28.2, 8.2),          v3(-0.8, -29.6, 0.6),  v3(-4.0, -28.8, 0.7), 0.98)
    ];
    var comp = [
      [0.56, 0.06], [0.58, 0.00], [0.60, -0.05], [0.62, -0.10],
      [0.64, -0.14], [0.66, -0.18], [0.68, -0.20], [0.68, -0.22],
      [0.64, -0.20], [0.58, -0.12], [0.56, -0.08], [0.58, -0.12],
      [0.60, -0.16], [0.60, -0.18]
    ];
    COMPOSITION_KEYS = KEYS.map(function (key, index) {
      return { p: key.p, ndc: comp[index].slice() };
    });
    var trackAspect = innerWidth / innerHeight;
    if (innerWidth <= 760) {
      KEYS.forEach(function (k2) {
        k2.octo = k2.octo.slice(); k2.look = k2.look.slice();
        k2.octo[0] *= 0.42; k2.look[0] *= 0.42; k2.os *= 0.52;
      });
      // The giant wire specimen stays on one outer edge and moves in bounded
      // increments. The previous 1.46-NDC jump made even a smooth swim look
      // like a teleport between the first two phone beats.
      var mobileX = [0.58, 0.60, 0.60, 0.62, 0.62, 0.64, 0.64,
        0.64, 0.62, 0.58, 0.56, 0.58, 0.60, 0.60];
      var mobileY = [0.38, 0.24, 0.08, -0.08, -0.18, -0.20, -0.22,
        -0.24, -0.24, -0.18, -0.08, -0.12, -0.18, -0.22];
      COMPOSITION_KEYS.forEach(function (key, index) {
        key.ndc[0] = mobileX[index];
        key.ndc[1] = mobileY[index];
      });
    } else if (trackAspect < 1.35) {
      // Compact desktop/tablet windows have a narrow pocket below the nav and
      // beyond the headline's right edge. Tighten the silhouette enough to use
      // it without colliding with either layer.
      KEYS.forEach(function (k2) { k2.os *= 0.60; });
      COMPOSITION_KEYS.forEach(function (key) {
        key.ndc[0] = Math.max(-0.70, Math.min(0.70, key.ndc[0] * 0.94));
      });
      COMPOSITION_KEYS[0].ndc[0] = 0.60;
      COMPOSITION_KEYS[0].ndc[1] = 0.28;
      COMPOSITION_KEYS[1].ndc[0] = 0.62;
      COMPOSITION_KEYS[1].ndc[1] = 0.16;
    }
    for (var i = 1; i < KEYS.length; i++) if (KEYS[i].p <= KEYS[i - 1].p) KEYS[i].p = KEYS[i - 1].p + 0.002;
    screenWin = { s: pOf('screen', 0.02), a: pOf('screen', 0.34), b: pOf('screen', 0.46), c: pOf('screen', 0.72), d: pOf('screen', 0.82) };
  }
  var easeK = function (t) { return t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2; };
  var camK = v3(0, 12, 9), lookK = v3(0, 0, 0), octoK = v3(0, 0, 0), octoS = 1;
  function sampleComposition(pp) {
    var i = 0;
    while (i < COMPOSITION_KEYS.length - 2 && COMPOSITION_KEYS[i + 1].p < pp) i++;
    var a = COMPOSITION_KEYS[i], b = COMPOSITION_KEYS[i + 1];
    var t = easeK(Math.min(1, Math.max(0, (pp - a.p) / (b.p - a.p || 1))));
    compositionNdc[0] = a.ndc[0] + (b.ndc[0] - a.ndc[0]) * t;
    compositionNdc[1] = a.ndc[1] + (b.ndc[1] - a.ndc[1]) * t;
  }
  function sampleTrack(p) {
    var i = 0;
    while (i < KEYS.length - 2 && KEYS[i + 1].p < p) i++;
    var a = KEYS[i], b = KEYS[i + 1];
    var t = easeK(Math.min(1, Math.max(0, (p - a.p) / (b.p - a.p || 1))));
    lerp3(camK, a.cam, b.cam, t);
    lerp3(lookK, a.look, b.look, t);
    lerp3(octoK, a.octo, b.octo, t);
    octoS = a.os + (b.os - a.os) * t;
    sampleComposition(p);
  }

  /* ══════════════════════════ DOM hookups ═════════════════════════════ */
  var flashEl = document.getElementById('flash');
  var roomEl = document.getElementById('screenroom');
  var depthEl = document.getElementById('depth');
  var depthNav = document.getElementById('depthLabel');
  var hudEl = document.querySelector('.hud');
  var cueEl = document.querySelector('.scrollcue');

  var target = 0, p = 0, pVelocity = 0, mx = 0, my = 0;
  var prevOcto = [0, 0, 0], havePrev = false, velW = [0, 0, 0];
  var trailL = [0, 0.2, -1];
  var lastScrollY = window.scrollY != null ? window.scrollY : (document.documentElement.scrollTop || 0);
  function queueScrollDelta(dy, nowMs) {
    if (!Number.isFinite(dy) || Math.abs(dy) < INPUT_EPS_PX) return;
    var cap = Math.max(48, innerHeight * 0.35);
    pendingDeltaPx = Math.max(-cap, Math.min(cap, pendingDeltaPx + dy));
    // Treat all events delivered before one rendered frame as one signed
    // gesture. A tiny opposite tail must not steal the direction and inherit
    // the energy of the much larger event before it.
    pendingAbsPx = Math.abs(pendingDeltaPx);
    pendingDir = pendingDeltaPx > 0 ? 1 : pendingDeltaPx < 0 ? -1 : 0;
    lastInputMs = nowMs;
    if (pendingDir) lastInputDir = pendingDir;
    motionDebug.lastDeltaPx = dy;
    motionDebug.lastInputDirection = pendingDir;
    motionDebug.lastEventAtMs = nowMs;
  }
  /* state transitions (the only places LS.state is written) */
  function travelAngleForDirection(direction) {
    return direction > 0 ? -Math.PI / 2 : Math.PI / 2;
  }
  function externalPhysicalSpeed() {
    var value = V10_BRIDGE && V10_BRIDGE.locomotion
      ? Number(V10_BRIDGE.locomotion.physicalSpeed) : 0;
    return Number.isFinite(value) ? Math.max(0, value) : 0;
  }
  function lsEnter(state) {
    LS.state = state;
    LS.t = 0;
    if (state === 'anticipate') {
      LS.alt = LS.strokes & 1;
      LS.strokes++;
      // continue the clip from wherever it is if we are chaining out of a
      // glide/settle (phase wraps through neutral); a cold start snaps to 0
      if (LS.clipPhase > 0.9 || LS.clipPhase < 0.05) LS.clipPhase = 0;
    } else if (state === 'turn') {
      LS.aFrom = LS.travelA;
      LS.aTo = travelAngleForDirection(LS.queuedDir || LS.dir);
      // arc toward open water; dead centre alternates by stroke count
      LS.side = Math.abs(LS.offX) > 0.04 ? -Math.sign(LS.offX)
        : ((LS.strokes & 1) ? 1 : -1);
    }
  }
  function lsQueueDirection(nextDir) {
    var route = directionTransition.resolveDirectionTransition(LS, nextDir);
    if (route.action === 'none' || route.action === 'keep-redirect') return route;

    if (route.action === 'abort-brake') {
      // The latest gesture cancelled the queued flip before rotation began.
      // Restart from a real preload. Preserving the brake phase used to jump
      // straight into power and fire an unmistakable one-frame thrust pop.
      LS.queuedDir = 0;
      LS.clipPhase = 0;
      LS.resumeHold = 0.10;
      lsEnter('anticipate');
      return route;
    }

    if (route.action === 'retarget-turn') {
      // Retarget from the exact current presentation angle. The external
      // torque solver retains its angular velocity, so it decelerates and
      // returns physically instead of snapping to either cardinal heading.
      LS.queuedDir = route.direction;
      LS.aFrom = LS.travelA;
      LS.aTo = travelAngleForDirection(route.direction);
      LS.t = 0;
      return route;
    }

    LS.queuedDir = route.direction;
    if (route.action === 'start-brake') {
      // Do not mime a brake while stationary. Moving animals shed their actual
      // water-relative speed before the controller is allowed to rotate.
      if (Math.max(LS.speed, externalPhysicalSpeed()) < 0.025) lsEnter('turn');
      else lsEnter('brake');
    }
    return route;
  }
  /* RAW INPUT. Real wheel and touch deltas are the direction source of truth —
     they carry sign even when the page is pinned at its scroll extremes. Scroll
     events remain as fallback (scrollbar drag, PgUp/PgDn, space) but are
     suppressed while a wheel/touch gesture was seen, so nothing double-counts. */
  var rawInputUntil = -1e9;
  function onWheel(e) {
    if (FREEZE !== null) return;
    var dy = e.deltaY;
    if (e.deltaMode === 1) dy *= 16;
    else if (e.deltaMode === 2) dy *= innerHeight;
    rawInputUntil = performance.now() + 160;
    queueScrollDelta(dy, performance.now());
  }
  var touchLastY = null;
  function onTouchStart(e) { touchLastY = e.touches[0].clientY; }
  function onTouchMove(e) {
    var y = e.touches[0].clientY;
    if (touchLastY === null) { touchLastY = y; return; }
    var dy = touchLastY - y;              // finger up = content moves down = positive
    touchLastY = y;
    rawInputUntil = performance.now() + 160;
    if (FREEZE === null) queueScrollDelta(dy, performance.now());
  }
  function onTouchEnd() { touchLastY = null; }
  function onScroll() {
    var H = journeyEl.offsetHeight - innerHeight;
    var y = window.scrollY != null ? window.scrollY : (document.documentElement.scrollTop || 0);
    var dy = y - lastScrollY;
    lastScrollY = y;
    target = Math.min(1, Math.max(0, y / (H || 1)));
    if (FREEZE === null && performance.now() > rawInputUntil) {
      queueScrollDelta(dy, performance.now());
    }
  }
  if (!reduce) {
    addEventListener('scroll', onScroll, { passive: true });
    addEventListener('wheel', onWheel, { passive: true });
    addEventListener('touchstart', onTouchStart, { passive: true });
    addEventListener('touchmove', onTouchMove, { passive: true });
    addEventListener('touchend', onTouchEnd, { passive: true });
    addEventListener('pointermove', function (e) {
      mx = e.clientX / innerWidth - 0.5;
      my = e.clientY / innerHeight - 0.5;
    }, { passive: true });
  }

  var bw = 2, bh = 2;
  function resize() {
    DPR = Math.min(devicePixelRatio || 1, degraded ? 1.25 : 2);
    var w = Math.max(2, Math.round(innerWidth * DPR)), h = Math.max(2, Math.round(innerHeight * DPR));
    canvas.width = w; canvas.height = h;
    M4.perspective(proj, 40 * Math.PI / 180, innerWidth / innerHeight, 0.1, 120);
    sizeTarget(rtScene, w, h);
    bw = Math.max(2, Math.floor(w / 3)); bh = Math.max(2, Math.floor(h / 3));
    sizeTarget(rtA, bw, bh); sizeTarget(rtB, bw, bh);
    sizeScreen();
    buildTrack();
    if (reduce) frame(LABT * 1000);
  }
  addEventListener('resize', resize);

  /* ══════════ single-source screen projection ══════════
     The #screenroom DOM is the ONLY screen content in the journey. While the
     camera is out in the water it is perspective-mapped onto the monitor's
     display quad with the same view-projection matrix the WebGL scene uses,
     so it stays pixel-locked inside the bezel. As the camera reaches the dive
     hold point that mapping converges to the identity and the same element
     simply owns the viewport. One element. No swap, no fade, no second copy. */
  function homographyToQuad(w, h, q2) { // element rect (0,0,w,h) -> 4 screen pts
    var x0 = q2[0].x, y0 = q2[0].y, x1 = q2[1].x, y1 = q2[1].y;
    var x2 = q2[2].x, y2 = q2[2].y, x3 = q2[3].x, y3 = q2[3].y;
    var dx1 = x1 - x3, dx2 = x2 - x3, dy1 = y1 - y3, dy2 = y2 - y3;
    var sx = x0 - x1 - x2 + x3, sy = y0 - y1 - y2 + y3;
    var den = dx1 * dy2 - dx2 * dy1;
    if (Math.abs(den) < 1e-9) return null;
    var g = (sx * dy2 - sy * dx2) / den, hh = (sy * dx1 - sx * dy1) / den;
    var a = x1 - x0 + g * x1, b = x2 - x0 + hh * x2;
    var d = y1 - y0 + g * y1, e = y2 - y0 + hh * y2;
    return [a / w, d / w, 0, g / w,
            b / h, e / h, 0, hh / h,
            0, 0, 1, 0,
            x0, y0, 0, 1];
  }
  var roomVis = false, roomLive = false, roomWake = -1;
  function fmtM(x) {
    var v = +x.toFixed(9);
    return Math.abs(v) < 1e-9 ? '0' : String(v);
  }
  function projectScreenRoom(inside) {
    if (!roomEl) return;
    var vis = p > screenWin.s && p < screenWin.d + 0.08;
    if (vis !== roomVis) { roomVis = vis; roomEl.classList.toggle('vis', vis); }
    var live = inside > 0.75;
    if (live !== roomLive) {
      roomLive = live;
      roomEl.classList.toggle('live', live);
      roomEl.style.pointerEvents = live ? 'auto' : 'none';
    }
    if (!vis) return;
    // the monitor WAKES as the camera approaches: backlight and content are
    // staggered inside the same element via --wake (see journey.css) — the
    // screen is asleep in the distance, alive well before the dive.
    var wake = Math.max(smoothstep(p, screenWin.s, screenWin.a + 0.015), inside);
    if (Math.abs(wake - roomWake) > 0.004) {
      roomWake = wake;
      roomEl.style.setProperty('--wake', wake.toFixed(3));
    }
    if (inside >= 0.999) {      // exact handoff: the room IS the viewport
      roomEl.style.transform = 'none';
      roomEl.style.filter = 'none';
      return;
    }
    var hw = SCREEN_W / 2, hv = SCREEN_H / 2, cz = SP[2] + 0.052;
    var corners = [[-hw, hv], [hw, hv], [-hw, -hv], [hw, -hv]]; // TL TR BL BR
    var q2 = [];
    for (var i = 0; i < 4; i++) {
      var wx0 = SP[0] + corners[i][0], wy0 = SP[1] + corners[i][1];
      var cx = viewProj[0] * wx0 + viewProj[4] * wy0 + viewProj[8] * cz + viewProj[12];
      var cy = viewProj[1] * wx0 + viewProj[5] * wy0 + viewProj[9] * cz + viewProj[13];
      var cw = viewProj[3] * wx0 + viewProj[7] * wy0 + viewProj[11] * cz + viewProj[15];
      if (cw < 0.02) { roomEl.style.transform = 'none'; roomEl.style.filter = 'none'; return; }
      q2.push({ x: (cx / cw * 0.5 + 0.5) * innerWidth, y: (0.5 - cy / cw * 0.5) * innerHeight });
    }
    var m = homographyToQuad(innerWidth, innerHeight, q2);
    if (!m) return;
    var out = new Array(16);
    for (var mi = 0; mi < 16; mi++) out[mi] = fmtM(m[mi]);
    roomEl.style.transform = 'matrix3d(' + out.join(',') + ')';
    // fog parity with the GL scene, released to exactly 1.0 for the handoff
    var ddx = camPos[0] - SP[0], ddy = camPos[1] - SP[1], ddz = camPos[2] - cz;
    var dist = Math.sqrt(ddx * ddx + ddy * ddy + ddz * ddz);
    var fd = CFG.fog * dist;
    var fogf = 1 - Math.exp(-fd * fd);
    var dim = (1 - 0.8 * fogf) * (1 - inside) + inside;
    roomEl.style.filter = dim > 0.996 ? 'none' : 'brightness(' + dim.toFixed(3) + ')';
  }

  /* ══════════════════════════ the frame ═══════════════════════════════ */
  var UP = [0, 1, 0];
  var fogC = [0, 0, 0];
  var paused = false, ft = 0, fn = 0, lastFrameTT = null;
  window.__pause = function (v) { paused = v; if (!v && !reduce) requestAnimationFrame(loop); return 'paused=' + v; };

  function setCommon(u) {
    if (u.uVP) gl.uniformMatrix4fv(u.uVP, false, viewProj);
    if (u.uFogC) gl.uniform3fv(u.uFogC, fogC);
    if (u.uFogD) gl.uniform1f(u.uFogD, CFG.fog);
  }
  function quadAxes(u, yaw, rotZ) { // billboard-ish axes for world quads
    var cx = Math.cos(yaw), sx = Math.sin(yaw), cz = Math.cos(rotZ), sz = Math.sin(rotZ);
    gl.uniform3f(u.uAxX, cx * cz, sz, -sx * cz);
    gl.uniform3f(u.uAxY, -cx * sz, cz, sx * sz);
  }

  function frame(t) {
    var tt = t * 0.001;
    var frameDt = lastFrameTT === null ? 1 / 60 : Math.min(0.05, Math.max(0.001, tt - lastFrameTT));
    lastFrameTT = tt;
    // Three.js shares this WebGL2 context for the rigged animal after our
    // composite. Return to the default VAO before touching journey attributes;
    // this makes the two renderers deterministic without separate canvases or
    // animation clocks.
    gl.bindVertexArray(null);
    // Camera progress follows a critically damped second-order system. Position,
    // velocity and acceleration are continuous even when wheel deltas arrive in
    // bursts; input sign remains the locomotion controller's responsibility.
    var cameraOmega = 7.2;
    var pAcceleration = (target - p) * cameraOmega * cameraOmega
      - 2 * cameraOmega * pVelocity;
    pVelocity += pAcceleration * frameDt;
    pVelocity = Math.max(-0.82, Math.min(0.82, pVelocity));
    p = Math.min(1, Math.max(0, p + pVelocity * frameDt));
    if (Math.abs(target - p) < 1e-5 && Math.abs(pVelocity) < 1e-4) {
      p = target; pVelocity = 0;
    }

    rawDeltaPx = pendingDeltaPx;
    rawMagnitudePx = pendingAbsPx;
    var desiredDirection = pendingDir;
    pendingDeltaPx = 0; pendingAbsPx = 0; pendingDir = 0;
    var hasRawInput = rawMagnitudePx >= INPUT_EPS_PX;
    var viewportH = Math.max(1, innerHeight);
    inputAgeMs = FREEZE !== null ? Infinity : Math.max(0, performance.now() - lastInputMs);
    var redirectGesture = hasRawInput && desiredDirection && (
      desiredDirection !== LS.dir || LS.state === 'brake' || LS.state === 'turn'
    );
    var intentSample = directionTransition.sampleDirectionIntent({
      intentPx: directionIntentPx,
      deltaPx: redirectGesture ? rawDeltaPx : 0,
      deltaTime: frameDt,
      viewportHeight: viewportH
    });
    directionIntentPx = intentSample.intentPx;
    directionIntentThresholdPx = intentSample.thresholdPx;
    var acceptedDirection = 0;
    var acceptedMagnitudePx = 0;
    if (hasRawInput) {
      if (redirectGesture) {
        if (intentSample.accepted && intentSample.direction === desiredDirection) {
          acceptedDirection = desiredDirection;
          acceptedMagnitudePx = Math.min(Math.abs(directionIntentPx), viewportH * 0.35);
          directionIntentPx = 0;
        }
      } else {
        directionIntentPx = 0;
        acceptedDirection = desiredDirection;
        acceptedMagnitudePx = rawMagnitudePx;
      }
    }
    var hasAcceptedInput = acceptedMagnitudePx >= INPUT_EPS_PX;
    inputActive = inputAgeMs <= 140 && (!redirectGesture || hasAcceptedInput);

    /* ── input → desired velocity + propulsion energy ── */
    var rateNow = hasAcceptedInput ? acceptedMagnitudePx / frameDt : 0;
    inRate += (rateNow - inRate) * (1 - Math.exp(-(hasAcceptedInput ? 16 : 5) * frameDt));
    if (hasAcceptedInput) {
      LS.energy = Math.min(1, LS.energy + acceptedMagnitudePx / (viewportH * 0.78));
      // During brake/turn, matching LS.dir means "cancel the queued flip" and
      // must still reach the transition router. Outside a redirect it is the
      // ordinary same-direction top-up path.
      if (acceptedDirection && (
        acceptedDirection !== LS.dir || LS.state === 'brake' || LS.state === 'turn'
      )) lsQueueDirection(acceptedDirection);
      else if (LS.state === 'idle' || LS.state === 'settle') lsEnter('anticipate');
      // same-direction input during anticipate/power/glide just tops up energy:
      // the current phase carries on, and the glide chains the next stroke.
    }
    LS.desired = Math.min(1.35, (inRate / viewportH) * 1.05);
    if (!inputActive) LS.desired *= Math.exp(-2.2 * frameDt);
    LS.energy *= Math.exp(-((LS.state === 'idle' || LS.state === 'settle') ? 1.1 : 0.16) * frameDt);

    // The external controller publishes its water-relative solution back onto
    // the stable bridge object. This is the single authority for turn timing,
    // body attitude, arm load and brake completion.
    var physicalFeedback = V10_BRIDGE.locomotion;
    var physicalSpeed = Number(physicalFeedback.physicalSpeed) || 0;
    var physicalHeadingX = Number(physicalFeedback.physicalHeadingX) || 0;
    var physicalHeadingY = Number(physicalFeedback.physicalHeadingY);
    if (!Number.isFinite(physicalHeadingY)) physicalHeadingY = -LS.dir;
    var physicalAngularVelocity = Number(physicalFeedback.physicalAngularVelocity) || 0;
    var physicalTurnEnvelope = Math.max(0, Math.min(1,
      Number(physicalFeedback.physicalTurnEnvelope) || 0));
    var physicalTurnSettled = physicalFeedback.physicalTurnSettled === true;

    /* ── phase machine ── */
    LS.t += frameDt;
    var quick = 0.85 + 0.70 * LS.energy;          // energetic swims cycle faster
    switch (LS.state) {
      case 'idle':
        LS.blend += (0 - LS.blend) * (1 - Math.exp(-3.2 * frameDt));
        LS.speed *= Math.exp(-2.5 * frameDt);
        break;
      case 'anticipate': {                        // quick gather: clip 0 → 0.26
        LS.blend += ((0.62 + 0.38 * LS.energy) - LS.blend) * (1 - Math.exp(-18 * frameDt));
        LS.speed *= Math.exp(-0.9 * frameDt);
        if (LS.resumeHold > 0) {
          LS.resumeHold = Math.max(0, LS.resumeHold - frameDt);
          LS.clipPhase = 0;
          break;
        }
        LS.clipPhase = Math.min(0.26, LS.clipPhase + frameDt * quick * 1.9);
        if (LS.clipPhase >= 0.26 - 1e-4) lsEnter('power');
        break;
      }
      case 'power': {                             // jet: clip 0.26 → 0.50
        LS.clipPhase = Math.min(0.50, LS.clipPhase + frameDt * quick * 0.9);
        var cap = Math.max(LS.desired, 0.14 + 1.15 * LS.energy);
        LS.speed = Math.min(cap, LS.speed + (1.9 + 2.7 * LS.energy) * frameDt);
        LS.energy = Math.max(0, LS.energy - 0.95 * frameDt);   // burn fuel hard
        if (LS.clipPhase >= 0.50 - 1e-4) lsEnter('glide');
        break;
      }
      case 'glide': {                             // coast: clip 0.50 → 0.87
        LS.clipPhase = Math.min(0.87, LS.clipPhase + frameDt * quick * 0.42);
        LS.speed *= Math.exp(-1.5 * frameDt);
        // a fresh same-direction gesture in the LATE glide pumps the next jet
        // immediately; early-glide input only tops up the current momentum
        if (hasAcceptedInput && acceptedDirection === LS.dir && LS.clipPhase > 0.62) {
          LS.clipPhase = 0;
          lsEnter('anticipate');
        } else if (LS.clipPhase >= 0.87 - 1e-4) {
          if (inputActive || LS.desired > 0.05 || LS.energy > 0.5) {
            LS.clipPhase = 0;
            lsEnter('anticipate');
          } else lsEnter('settle');
        }
        break;
      }
      case 'settle': {                            // clip 0.87 → 1, weight down
        LS.clipPhase = Math.min(1, LS.clipPhase + frameDt * 0.5);
        LS.blend += (0 - LS.blend) * (1 - Math.exp(-2.6 * frameDt));
        LS.speed *= Math.exp(-2.4 * frameDt);
        if (hasAcceptedInput && acceptedDirection === LS.dir) lsEnter('anticipate');
        else if (LS.clipPhase >= 1 - 1e-4 && LS.blend < 0.05) { LS.clipPhase = 0; lsEnter('idle'); }
        break;
      }
      case 'brake': {                             // fast umbrella check
        LS.blend += (0.9 - LS.blend) * (1 - Math.exp(-16 * frameDt));
        LS.clipPhase += (0.26 - LS.clipPhase) * (1 - Math.exp(-13 * frameDt));
        LS.brakeEnv = Math.min(1, LS.brakeEnv + frameDt * 7.5);
        LS.speed *= Math.exp(-9.0 * frameDt);
        // Brake the actual water-relative velocity, not only the legacy scalar.
        if (LS.t >= BRAKE_MIN_S
          && (Math.max(LS.speed, physicalSpeed) < 0.04 || LS.t > BRAKE_MAX_S)) {
          lsEnter('turn');
        }
        break;
      }
      case 'turn': {
        LS.travelA = Math.atan2(physicalHeadingY, physicalHeadingX);
        var physicalTurnSign = Math.sign(physicalAngularVelocity) || LS.side;
        // Every visible redirect channel follows the same physical envelope.
        // A cancellation therefore passes through neutral instead of banking
        // one way while the mantle has already begun returning the other way.
        var presentationFollow = 1 - Math.exp(-18 * frameDt);
        LS.yawSwing += (physicalTurnEnvelope * 0.28 * physicalTurnSign - LS.yawSwing)
          * presentationFollow;
        LS.bank += (physicalTurnEnvelope * 0.14 * physicalTurnSign - LS.bank)
          * presentationFollow;
        LS.brakeEnv = Math.max(0, LS.brakeEnv - frameDt * 3.2);
        LS.speed *= Math.exp(-2.2 * frameDt);
        LS.offX += physicalTurnSign * 0.052 * physicalTurnEnvelope * frameDt;
        var turnTimedOut = LS.t >= TURN_MAX_S;
        if ((LS.t >= TURN_MIN_S && physicalTurnSettled) || turnTimedOut) {
          if (LS.queuedDir) LS.dir = LS.queuedDir;
          LS.queuedDir = 0;
          LS.clipPhase = 0;
          LS.turnRecovery = 1;
          LS.resumeHold = 0.12;
          // A safety timeout may mean the renderer was background-throttled.
          // Settle without thrust; the controller finishes aligning on resume.
          lsEnter(!turnTimedOut && (LS.energy > 0.06 || LS.desired > 0.04)
            ? 'anticipate' : 'settle');
        }
        break;
      }
    }
    if (LS.state !== 'turn') {
      LS.travelA = Math.atan2(physicalHeadingY, physicalHeadingX);
      LS.yawSwing *= Math.exp(-8 * frameDt);
      LS.bank *= Math.exp(-8 * frameDt);
      LS.brakeEnv = Math.max(0, LS.brakeEnv - frameDt * 2.6);
      LS.turnRecovery = Math.max(0, LS.turnRecovery - frameDt / 0.42);
    }

    /* ── integrate position; nothing ever pulls back toward the old anchor ── */
    // travelA rotates continuously during a reversal. Integrating the old
    // discrete sign made the animal slide backward while its mantle was
    // already halfway through the new arc.
    var offVel = Math.sin(LS.travelA) * LS.speed;
    LS.offY += offVel * frameDt;
    var EDGE = 0.34;                              // soft frame-edge spring only
    if (LS.offY > EDGE) LS.offY -= (LS.offY - EDGE) * (1 - Math.exp(-3.0 * frameDt));
    else if (LS.offY < -EDGE) LS.offY -= (LS.offY + EDGE) * (1 - Math.exp(-3.0 * frameDt));
    var EDGX = EDGE * 0.7;
    if (LS.offX > EDGX) LS.offX -= (LS.offX - EDGX) * (1 - Math.exp(-3.0 * frameDt));
    else if (LS.offX < -EDGX) LS.offX -= (LS.offX + EDGX) * (1 - Math.exp(-3.0 * frameDt));

    /* legacy aliases (wave path, clip sampler, debug) */
    dirSign = LS.dir;
    screenOff = LS.offY;
    screenVelocity = offVel;
    effort = Math.min(1, Math.max(LS.energy, LS.speed));
    strokeAlt = LS.alt;
    strokeBlend = LS.blend;
    strokePhase = LS.clipPhase;
    strokeActive = LS.state === 'anticipate' || LS.state === 'power' ||
      LS.state === 'glide' || LS.state === 'brake';
    modeCur = Math.min(1, Math.max(LS.speed * 1.4, LS.blend * 0.8));
    scrollDirection += (dirSign - scrollDirection) * (1 - Math.exp(-18 * frameDt));
    turnEnvCur = 0.5 * Math.max(LS.brakeEnv, physicalTurnEnvelope);

    if (FREEZE !== null) {
      p = FREEZE;
      pVelocity = 0;
      LS.dir = dirSign = q.get('dir') === 'down' ? 1 : -1;
      LS.travelA = LS.aFrom = LS.aTo = dirSign > 0 ? -Math.PI / 2 : Math.PI / 2;
      LS.yawSwing = LS.bank = LS.brakeEnv = turnEnvCur = 0;
      LS.turnRecovery = LS.resumeHold = directionIntentPx = 0;
      LS.speed = LS.energy = LS.desired = effort = 0;
      LS.offX = LS.offY = screenOff = screenVelocity = 0;
      inputActive = false;
      var freezeState = q.get('state') || (q.get('mode') === 'trail' ? 'glide' : 'idle');
      if (!/^(idle|anticipate|power|glide|settle|brake|turn)$/.test(freezeState)) freezeState = 'idle';
      var freezeActive = freezeState !== 'idle';
      var requestedPhase = q.has('phase') ? parseFloat(q.get('phase')) : (LABT / 4.9) % 1;
      LS.state = freezeState;
      LS.energy = freezeActive ? 0.82 : 0;
      LS.speed = freezeActive ? 0.22 : 0;
      strokeActive = freezeActive;
      LS.blend = strokeBlend = freezeActive ? 1 : 0;
      LS.clipPhase = strokePhase = freezeActive
        ? Math.min(0.999, Math.max(0, Number.isFinite(requestedPhase) ? requestedPhase : 0.38))
        : 0;
      modeCur = freezeActive ? 1 : 0;
    }
    motionDebug.state = LS.state;
    motionDebug.direction = dirSign;
    motionDebug.pageProgress = p;
    motionDebug.pageTarget = target;
    motionDebug.rawDeltaPx = rawDeltaPx;
    motionDebug.inputMagnitudePx = rawMagnitudePx;
    motionDebug.directionIntentPx = directionIntentPx;
    motionDebug.directionIntentThresholdPx = directionIntentThresholdPx;
    motionDebug.speed = LS.speed;
    motionDebug.desired = LS.desired;
    motionDebug.energy = LS.energy;
    motionDebug.effort = effort;
    motionDebug.mode = modeCur;
    motionDebug.inputActive = inputActive;
    motionDebug.inputAgeMs = inputAgeMs;
    motionDebug.screenOff = screenOff;
    motionDebug.offX = LS.offX;
    motionDebug.screenVelocity = screenVelocity;
    motionDebug.travelA = LS.travelA;
    motionDebug.yawSwing = LS.yawSwing;
    motionDebug.bank = LS.bank;
    motionDebug.physicalTurnEnvelope = physicalTurnEnvelope;
    motionDebug.physicalTurnSettled = physicalTurnSettled;
    motionDebug.turning = LS.state === 'turn' || LS.state === 'brake';
    motionDebug.strokeActive = strokeActive;
    motionDebug.strokePhase = strokePhase;
    motionDebug.strokeBlend = strokeBlend;
    motionDebug.strokeClip = strokeAlt ? 'swimB' : 'swim';
    sampleTrack(p);
    if (LAB) { // creature workbench: orbit camera (auto until you drag)
      var la = labAuto ? tt * 0.22 : labAz;
      var ce = Math.cos(labEl), se = Math.sin(labEl);
      camK[0] = Math.sin(la) * labDist * ce;
      camK[1] = -10.15 + se * labDist;
      camK[2] = Math.cos(la) * labDist * ce;
      lookK[0] = 0; lookK[1] = -10.15; lookK[2] = 0;
      octoK[0] = 0; octoK[1] = -10.4; octoK[2] = 0; octoS = 1;
      mx = 0; my = 0;
    }

    /* the dive into the screen: freehand camera motion (mouse parallax, bob)
       fades to zero through the approach, so at the hold point the camera sits
       EXACTLY on the display axis and the projected DOM equals the viewport. */
    var w = screenWin;
    var into = smoothstep(p, w.a, w.b);
    var outof = 1 - smoothstep(p, w.c, w.d);
    var inside = Math.min(into, outof);
    var steady = 1 - inside;
    camPos[0] = camK[0] + (mx * 0.35 + Math.sin(tt * 0.22) * 0.05) * steady;
    camPos[1] = camK[1] + (-my * 0.25 + Math.sin(tt * 0.31) * 0.04) * steady;
    camPos[2] = camK[2];
    M4.lookAt(view, camPos, lookK, UP);
    M4.mul(viewProj, proj, view);

    var y = camPos[1];
    var surf = smoothstep(y, -6, 10);
    var cloudBand = 1 - Math.min(1, Math.abs(y + 23) / 3.2);
    var floorN = smoothstep(-y, 24.5, 29);
    fogC[0] = VOID[0] + (0.055 - VOID[0]) * cloudBand * 0.22;
    fogC[1] = VOID[1] + (0.028 - VOID[1]) * cloudBand * 0.22;
    fogC[2] = VOID[2] + (0.16 - VOID[2]) * cloudBand * 0.22;

    // the same DOM content is on the monitor the whole time — project it now.
    // No flash, no crossfade: the entry is a pure continuous camera move.
    projectScreenRoom(inside);
    if (flashEl && flashEl.style.opacity !== '0') flashEl.style.opacity = '0';

    var meters = Math.max(0, Math.round((12 - y) * 88));
    var depthTxt = String(meters).padStart(4, '0');
    if (depthEl) depthEl.textContent = depthTxt;
    if (depthNav) depthNav.textContent = depthTxt;
    if (hudEl) hudEl.classList.toggle('gone', p > 0.965);
    if (cueEl) cueEl.classList.toggle('gone', p > 0.04);

    var placement = !EXTERNAL_OCTOPUS_V10 && inside >= 0.985
      ? creaturePlacement
      : updateCreaturePlacement(tt, frameDt);
    publishV10Bridge(tt, frameDt, inside, acceptedDirection, placement);
    if (inside >= 0.985) return; // the DOM demo owns the viewport

    /* ---- render scene into rtScene ---- */
    gl.bindFramebuffer(gl.FRAMEBUFFER, rtScene.fb);
    gl.viewport(0, 0, rtScene.w, rtScene.h);
    gl.clearColor(VOID[0], VOID[1], VOID[2], 1);
    gl.enable(gl.DEPTH_TEST);
    gl.depthMask(true);
    gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
    gl.disable(gl.BLEND);

    // backdrop (no depth)
    gl.disable(gl.DEPTH_TEST);
    gl.useProgram(backdropP.p);
    gl.uniform1f(backdropP.u.uSurface, surf);
    gl.uniform1f(backdropP.u.uCloud, cloudBand);
    gl.uniform1f(backdropP.u.uFloor, floorN);
    gl.uniform3fv(backdropP.u.uVoid, VOID);
    drawFsTri(backdropP);
    gl.enable(gl.DEPTH_TEST);

    // opaque: monitor bezel + desk furniture (fogged)
    gl.useProgram(bezelP.p);
    setCommon(bezelP.u);
    gl.uniform3fv(bezelP.u.uEye, camPos);
    attr(bezelP.p, 'aP', cubeBuf, 3, gl.FLOAT, false, 24, 0);
    attr(bezelP.p, 'aN', cubeBuf, 3, gl.FLOAT, false, 24, 12);
    gl.uniform3fv(bezelP.u.uPos, SP);
    gl.uniform3f(bezelP.u.uScale, SCREEN_W + BEZEL * 2, SCREEN_H + BEZEL * 2, 0.1);
    gl.uniform3f(bezelP.u.uBase, 0.006, 0.012, 0.022);
    gl.uniform1f(bezelP.u.uFr, 1.0);
    gl.drawArrays(gl.TRIANGLES, 0, 36);
    for (var di = 0; di < DESK.length; di++) {
      var D2 = DESK[di];
      gl.uniform3f(bezelP.u.uPos, SP[0] + D2[0][0], SP[1] + D2[0][1], SP[2] + D2[0][2]);
      gl.uniform3f(bezelP.u.uScale, D2[1][0], D2[1][1], D2[1][2]);
      gl.uniform3f(bezelP.u.uBase, D2[2][0], D2[2][1], D2[2][2]);
      gl.uniform1f(bezelP.u.uFr, di === DESK.length - 1 ? 0.16 : 0.34); // ledge dimmest
      gl.drawArrays(gl.TRIANGLES, 0, 36);
    }

    // the display backing (dark glass; the DOM room carries the content)
    gl.useProgram(screenP.p);
    setCommon(screenP.u);
    gl.uniform3f(screenP.u.uPos, SP[0], SP[1], SP[2] + 0.052);
    gl.uniform2f(screenP.u.uSize, SCREEN_W, SCREEN_H);
    quadAxes(screenP.u, 0, 0);
    attr(screenP.p, 'aP', quadBuf, 2, gl.FLOAT);
    gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);

    /* ---- translucent + additive ---- */
    gl.depthMask(false);
    gl.enable(gl.BLEND);

    // the creature: authored clips + screen-space direction control
    if (creature && !EXTERNAL_OCTOPUS_V10) {
      var wx = placement.position[0], wy = placement.position[1], wz = placement.position[2];
      var pitchNow = placement.rotation[0], yawNow = placement.rotation[1], rollNow = placement.rotation[2];
      var anchorNdcX = placement.anchorNdc[0], anchorNdcY = placement.anchorNdc[1];
      var ndcX = placement.ndc[0], ndcY = placement.ndc[1];
      var ambientGain = placement.ambientGain;
      creatureModel(
        wx, wy, wz,
        yawNow,
        pitchNow,
        rollNow,
        placement.scale
      );
      var vl = Math.hypot(velW[0], velW[1], velW[2]) || 1;
      worldToCreature(trailL, [-velW[0] / vl, -velW[1] / vl, -velW[2] / vl]);
      trailL[1] += 0.18;
      var tl = Math.hypot(trailL[0], trailL[1], trailL[2]) || 1;
      trailL[0] /= tl; trailL[1] /= tl; trailL[2] /= tl;

      var tintT = cloudBand > 0.45 ? [0.16, 0.10, 0.5] : [0.03, 0.33, 0.30];

      // pose the skeleton: authored clips by default, wave behind ?wave=1
      breathCur = 0.045 * Math.sin(tt * 1.35);
      var useClips = creature.clips && creature.clips.idle && !q.has('wave');
      if (useClips) clipPose(tt);
      else boneWave(tt);
      motionDebug.anchorNdcX = anchorNdcX;
      motionDebug.anchorNdcY = anchorNdcY;
      motionDebug.ndcX = ndcX;
      motionDebug.ndcY = ndcY;
      motionDebug.worldX = wx;
      motionDebug.worldY = wy;
      motionDebug.ambientGain = ambientGain;
      motionDebug.yaw = yawNow;
      motionDebug.pitch = pitchNow;
      var breathMorph = creature.hasMorphs ? 0.5 + 0.5 * Math.sin(tt * 1.35 - 1.5708) : 0;
      var jetMorph = creature.hasMorphs ? modeCur * (0.72 + 0.18 * Math.sin(tt * 2.1)) : 0;
      var webMorph = creature.hasMorphs ? 0.16 + 0.12 * Math.sin(tt * 1.35 + 0.4) : 0;

      function skinAttribs(pr) {
        attr(pr.p, 'aP', creature.posBuf, 3, gl.FLOAT);
        attr(pr.p, 'aN', creature.nrmBuf, 3, gl.FLOAT);
        attr(pr.p, 'aJ', creature.jntBuf, 4, creature.jntGLType);
        attr(pr.p, 'aW', creature.wgtBuf, 4, gl.FLOAT);
        attr(pr.p, 'aM0', creature.morphPBuf[0], 3, gl.FLOAT);
        attr(pr.p, 'aM1', creature.morphPBuf[1], 3, gl.FLOAT);
        attr(pr.p, 'aM2', creature.morphPBuf[2], 3, gl.FLOAT);
        attr(pr.p, 'aMN0', creature.morphNBuf[0], 3, gl.FLOAT);
        attr(pr.p, 'aMN1', creature.morphNBuf[1], 3, gl.FLOAT);
        attr(pr.p, 'aMN2', creature.morphNBuf[2], 3, gl.FLOAT);
      }

      gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
      gl.enable(gl.CULL_FACE);
      gl.useProgram(octoGlassP.p);
      setCommon(octoGlassP.u);
      gl.uniformMatrix4fv(octoGlassP.u.uModel, false, creature.model);
      gl.uniformMatrix4fv(octoGlassP.u.uJoints, false, creature.jointArr);
      gl.uniform3f(octoGlassP.u.uMorph, breathMorph, jetMorph, webMorph);
      gl.uniform3fv(octoGlassP.u.uEye, camPos);
      gl.uniform3fv(octoGlassP.u.uTint, tintT);
      skinAttribs(octoGlassP);
      gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, creature.idxBuf);
      gl.cullFace(gl.FRONT);
      gl.uniform1f(octoGlassP.u.uBack, 1);
      gl.drawElements(gl.TRIANGLES, creature.count, creature.idxGLType, 0);
      gl.cullFace(gl.BACK);
      gl.uniform1f(octoGlassP.u.uBack, 0);
      gl.drawElements(gl.TRIANGLES, creature.count, creature.idxGLType, 0);
      gl.disable(gl.CULL_FACE);

      // wireframe, additive, through-the-glass
      gl.disable(gl.DEPTH_TEST);
      gl.blendFunc(gl.SRC_ALPHA, gl.ONE);
      var breathW = 0.7 + 0.3 * (0.5 + 0.5 * Math.sin(tt * (6.2832 / 1.3)));
      var wireC = cloudBand > 0.45 ? VIOLET : TEAL;
      var wireA = 0.055 * CFG.glow * breathW;
      gl.useProgram(octoWireP.p);
      gl.uniformMatrix4fv(octoWireP.u.uVP, false, viewProj);
      gl.uniformMatrix4fv(octoWireP.u.uModel, false, creature.model);
      gl.uniformMatrix4fv(octoWireP.u.uJoints, false, creature.jointArr);
      gl.uniform3f(octoWireP.u.uMorph, breathMorph, jetMorph, webMorph);
      gl.uniform3fv(octoWireP.u.uC, wireC);
      gl.uniform1f(octoWireP.u.uA, wireA);
      skinAttribs(octoWireP);
      gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, creature.edgeBuf);
      gl.drawElements(gl.LINES, creature.edgeCount, gl.UNSIGNED_SHORT, 0);

      gl.useProgram(eyeP.p);
      gl.uniformMatrix4fv(eyeP.u.uVP, false, viewProj);
      gl.uniformMatrix4fv(eyeP.u.uModel, false, creature.model);
      gl.uniform1f(eyeP.u.uSize, 5 * DPR);
      gl.uniform3fv(eyeP.u.uC, wireC);
      attr(eyeP.p, 'aP', eyePosBuf, 3, gl.FLOAT);
      gl.drawArrays(gl.POINTS, 0, 2);
      gl.enable(gl.DEPTH_TEST);
    }

    // The rig is rendered synchronously into its transparent shared-context
    // target. Blend it into the linear scene now so fog particles, bloom and
    // the final color grade treat it as part of this world, not an overlay.
    canvas.dispatchEvent(journeyBeforePostEvent);
    var octoComposite = V10_BRIDGE.composite;
    if (octoComposite.ready && octoComposite.texture) {
      gl.bindVertexArray(null);
      gl.bindFramebuffer(gl.FRAMEBUFFER, rtScene.fb);
      gl.viewport(0, 0, rtScene.w, rtScene.h);
      gl.disable(gl.DEPTH_TEST);
      gl.depthMask(false);
      gl.enable(gl.BLEND);
      gl.blendEquation(gl.FUNC_ADD);
      gl.blendFuncSeparate(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA, gl.ONE, gl.ONE_MINUS_SRC_ALPHA);
      gl.useProgram(octopusCompositeP.p);
      gl.activeTexture(gl.TEXTURE0);
      gl.bindTexture(gl.TEXTURE_2D, octoComposite.texture);
      gl.uniform1i(octopusCompositeP.u.uOctopus, 0);
      drawFsTri(octopusCompositeP);
      gl.depthMask(true);
    }

    // additive atmosphere
    gl.enable(gl.BLEND);
    gl.blendFunc(gl.SRC_ALPHA, gl.ONE);

    // monitor halo
    gl.useProgram(glowP.p);
    gl.uniformMatrix4fv(glowP.u.uVP, false, viewProj);
    gl.uniform3f(glowP.u.uPos, SP[0], SP[1], SP[2] - 0.09);
    gl.uniform2f(glowP.u.uSize, SCREEN_W + 3.0, SCREEN_H + 2.2);
    quadAxes(glowP.u, 0, 0);
    gl.uniform1f(glowP.u.uA, 0.5);
    attr(glowP.p, 'aP', quadBuf, 2, gl.FLOAT);
    gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);

    // surface god rays
    if (surf > 0.02 && CFG.rays > 0.01) {
      gl.useProgram(rayP.p);
      gl.uniformMatrix4fv(rayP.u.uVP, false, viewProj);
      gl.uniform1f(rayP.u.uA, CFG.rays * surf);
      attr(rayP.p, 'aP', quadBuf, 2, gl.FLOAT);
      for (var r2 = 0; r2 < RAYS.length; r2++) {
        var R3 = RAYS[r2];
        gl.uniform3fv(rayP.u.uPos, R3.pos);
        gl.uniform2fv(rayP.u.uSize, R3.size);
        quadAxes(rayP.u, R3.yaw + Math.sin(tt * 0.05 + r2) * 0.06, R3.rot);
        gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
      }
    }

    // thermocline haze (flat, horizontal)
    if (cloudBand > 0.01) {
      gl.useProgram(thermoP.p);
      gl.uniformMatrix4fv(thermoP.u.uVP, false, viewProj);
      gl.uniform3f(thermoP.u.uPos, 0, -23, 0);
      gl.uniform2f(thermoP.u.uSize, 46, 46);
      gl.uniform3f(thermoP.u.uAxX, 1, 0, 0);
      gl.uniform3f(thermoP.u.uAxY, 0, 0, -1);
      gl.uniform1f(thermoP.u.uA, Math.min(1, cloudBand * 1.4));
      attr(thermoP.p, 'aP', quadBuf, 2, gl.FLOAT);
      gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
    }

    // seabed grid
    if (floorN > 0.01) {
      gl.useProgram(floorP.p);
      gl.uniformMatrix4fv(floorP.u.uVP, false, viewProj);
      gl.uniform1f(floorP.u.uY, -30);
      gl.uniform1f(floorP.u.uS, 40);
      gl.uniform1f(floorP.u.uT, tt);
      gl.uniform1f(floorP.u.uA, floorN);
      attr(floorP.p, 'aP', quadBuf, 2, gl.FLOAT);
      gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
    }

    // marine snow
    if (CFG.motes > 0.01) {
      gl.useProgram(moteP.p);
      gl.uniformMatrix4fv(moteP.u.uVP, false, viewProj);
      gl.uniform1f(moteP.u.uT, tt);
      gl.uniform1f(moteP.u.uA, CFG.motes);
      gl.uniform1f(moteP.u.uDpr, DPR);
      attr(moteP.p, 'aP', motePosBuf, 3, gl.FLOAT);
      attr(moteP.p, 'aSeed', moteSeedBuf, 1, gl.FLOAT);
      gl.drawArrays(gl.POINTS, 0, degraded ? MOTES >> 1 : MOTES);
    }

    gl.depthMask(true);
    gl.disable(gl.BLEND);

    /* ---- bloom + composite (ours) ---- */
    if (!degraded) {
      gl.disable(gl.DEPTH_TEST);
      gl.useProgram(blurP.p);
      gl.bindFramebuffer(gl.FRAMEBUFFER, rtA.fb);
      gl.viewport(0, 0, rtA.w, rtA.h);
      gl.activeTexture(gl.TEXTURE0);
      gl.bindTexture(gl.TEXTURE_2D, rtScene.tx);
      gl.uniform1i(blurP.u.uT, 0);
      gl.uniform2f(blurP.u.uDir, 1 / bw, 0);
      drawFsTri(blurP);
      gl.bindFramebuffer(gl.FRAMEBUFFER, rtB.fb);
      gl.bindTexture(gl.TEXTURE_2D, rtA.tx);
      gl.uniform2f(blurP.u.uDir, 0, 1 / bh);
      drawFsTri(blurP);
    }
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    gl.viewport(0, 0, canvas.width, canvas.height);
    gl.disable(gl.DEPTH_TEST);
    gl.useProgram(compP.p);
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, rtScene.tx);
    gl.uniform1i(compP.u.uScene, 0);
    gl.activeTexture(gl.TEXTURE1);
    gl.bindTexture(gl.TEXTURE_2D, degraded ? rtScene.tx : rtB.tx);
    gl.uniform1i(compP.u.uBloom, 1);
    gl.uniform1f(compP.u.uStrength, degraded ? 0 : CFG.bloom * (1 + floorN * 0.25));
    drawFsTri(compP);
    canvas.dispatchEvent(journeyRenderedEvent);
  }

  function loop(t) {
    if (paused || reduce) return;
    var t0 = performance.now();
    frame(t);
    if (!degraded) {
      ft += performance.now() - t0; fn++;
      if (fn === 140) {
        if (ft / fn > 26) { degraded = true; resize(); }
        ft = 0; fn = 0;
      }
    }
    requestAnimationFrame(loop); // rAF self-pauses while the tab is hidden
  }
  // keep the scene advancing in hidden tabs (screenshots, previews)
  setInterval(function () {
    if (document.hidden && !paused && !reduce) frame(performance.now());
  }, 50);
  // deterministic debug hook: force-render any journey point
  window.__step = function (pp) { target = p = Math.min(1, Math.max(0, pp)); frame(performance.now()); return p; };
  // test hook: exactly what the scroll handler does (sets target only, the
  // frame loop eases p toward it). Direction tests must also feed raw pixels.
  window.__setTarget = function (pp) { target = Math.min(1, Math.max(0, pp)); return target; };
  window.__feedScrollDelta = function (dy) {
    queueScrollDelta(Number(dy), performance.now());
    return { pendingDeltaPx: pendingDeltaPx, pendingDirection: pendingDir };
  };
  // The external rig may finish parsing after a deterministic/reduced-motion
  // journey has painted its single frame. Give that renderer one supported,
  // same-clock way to request a fresh composite for visual QA and snapshots.
  V10_BRIDGE.requestFrame = function () { frame(performance.now()); };

  canvas.addEventListener('webglcontextlost', function (e) {
    e.preventDefault();
    paused = true;
    document.body.classList.add('no-octo');
  });

  /* ══════════════════════════ boot ════════════════════════════════════ */
  var remeasure = function () { buildTrack(); if (reduce) frame(LABT * 1000); };
  resize();
  onScroll();
  setTimeout(remeasure, 400);
  if (document.fonts && document.fonts.ready) document.fonts.ready.then(remeasure);

  if (reduce) {
    p = target = FREEZE !== null ? FREEZE : 0.3;
    frame(LABT * 1000);
  } else {
    // Publish and paint once immediately. Background preview tabs may suspend
    // requestAnimationFrame entirely, but the external renderer still needs a
    // valid camera/locomotion frame before it can load.
    frame(performance.now());
    requestAnimationFrame(loop);
  }

  /* ══════════════════════════ tuner ═══════════════════════════════════ */
  addEventListener('keydown', function (e) {
    if (e.key === '`') {
      var el = document.getElementById('tune');
      if (el) el.classList.toggle('show');
    }
  });
  (function wireTuner() {
    var out = document.getElementById('tuneOut');
    var dump = function () { if (out) out.textContent = JSON.stringify(CFG); };
    ['scale', 'drift', 'amp', 'glow', 'thick', 'rough', 'bloom', 'fog', 'motes', 'rays'].forEach(function (k) {
      var el = document.getElementById('t_' + k);
      if (!el) return;
      el.value = CFG[k];
      el.addEventListener('input', function () { CFG[k] = parseFloat(el.value); dump(); if (reduce) frame(LABT * 1000); });
    });
    dump();
  })();
})();
