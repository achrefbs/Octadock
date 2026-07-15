import * as THREE from 'three';

export const EFFECT_TRIGGER_FLEE = 1;
export const EFFECT_TRIGGER_JET = 2;

const CURSOR_ACTIVE_CLASS = 'aquarium-cursor-active';
const CURSOR_NATIVE_CLASS = 'aquarium-cursor-native';
const INTERACTIVE_POINTER_SELECTOR = [
  'a',
  'button',
  'input',
  'select',
  'textarea',
  'label',
  '[contenteditable="true"]',
  '[role="button"]',
  '[role="link"]',
  '[role="textbox"]',
].join(',');

const PARTICLE_BUBBLE = 0;
const PARTICLE_WAKE = 1;
const PARTICLE_CURSOR = 2;
const POINTER_SLOT = 0;
const EPSILON = 1e-6;

function clamp(value, min = 0, max = 1) {
  return Math.min(max, Math.max(min, value));
}

function smootherstep(edge0, edge1, value) {
  const t = clamp((value - edge0) / Math.max(EPSILON, edge1 - edge0));
  return t * t * t * (t * (t * 6 - 15) + 10);
}

/** Fade decorative water effects completely before the projected screen is live. */
export function waterEffectGain(inside) {
  const value = Number.isFinite(inside) ? inside : 0;
  return 1 - smootherstep(0.42, 0.70, value);
}

/** A compact, allocation-free transition contract for render-only effects. */
export function effectTriggerMask(previousEpisode, previousStage, nextEpisode, nextStage) {
  let mask = 0;
  if (nextEpisode > previousEpisode) mask |= EFFECT_TRIGGER_FLEE;
  if (nextEpisode > 0 && nextStage === 'jet' && previousStage !== 'jet') {
    mask |= EFFECT_TRIGGER_JET;
  }
  return mask;
}

/** Fixed-step visual interpolation; simulation and hit testing remain untouched. */
export function interpolateRootPose(
  previousPosition,
  currentPosition,
  previousHeading,
  currentHeading,
  alpha,
  outPosition,
  outHeading,
) {
  const blend = clamp(Number(alpha) || 0);
  outPosition.x = previousPosition.x + (currentPosition.x - previousPosition.x) * blend;
  outPosition.y = previousPosition.y + (currentPosition.y - previousPosition.y) * blend;
  let headingX = previousHeading.x + (currentHeading.x - previousHeading.x) * blend;
  let headingY = previousHeading.y + (currentHeading.y - previousHeading.y) * blend;
  const length = Math.hypot(headingX, headingY);
  if (length < EPSILON) {
    headingX = currentHeading.x;
    headingY = currentHeading.y;
  } else {
    headingX /= length;
    headingY /= length;
  }
  outHeading.x = headingX;
  outHeading.y = headingY;
  return blend;
}

/**
 * Plan distance-sampled pointer wake emission. The returned carry is bounded
 * even when an input device reports one very large coalesced movement.
 */
export function pointerWakePlan(distancePixels, carryPixels = 0, {
  spacing = 20,
  maximumSamples = 4,
} = {}) {
  const distance = Math.max(0, Number(distancePixels) || 0);
  const safeSpacing = Math.max(1, Number(spacing) || 20);
  const carry = clamp(Number(carryPixels) || 0, 0, safeSpacing - EPSILON);
  const available = carry + distance;
  const uncappedCount = Math.floor(available / safeSpacing);
  const count = Math.min(Math.max(0, Math.floor(maximumSamples)), uncappedCount);
  return {
    count,
    firstFraction: distance > EPSILON
      ? clamp((safeSpacing - carry) / distance)
      : 1,
    stepFraction: distance > EPSILON ? safeSpacing / distance : 1,
    carry: available - Math.floor(available / safeSpacing) * safeSpacing,
  };
}

/** Deterministic xorshift used by both tests and the bounded particle pool. */
export function createEffectsRandom(seed = 0x0e77ec75) {
  let state = (Number(seed) >>> 0) || 0x6d2b79f5;
  return () => {
    state ^= state << 13;
    state ^= state >>> 17;
    state ^= state << 5;
    return (state >>> 0) / 0x100000000;
  };
}

export function isInteractivePointerTarget(target) {
  return Boolean(target?.closest?.(INTERACTIVE_POINTER_SELECTOR));
}

export function shouldUsePressureCursor({
  active = false,
  pointerType = 'mouse',
  interactive = false,
  finePointer = true,
  coarsePointer = false,
  reducedMotion = false,
  forcedColors = false,
  inside = 0,
} = {}) {
  return Boolean(active
    && pointerType === 'mouse'
    && finePointer
    && !coarsePointer
    && !interactive
    && !reducedMotion
    && !forcedColors
    && waterEffectGain(inside) > 0.02);
}

function mediaMatches(query, fallback = false) {
  try {
    return typeof matchMedia === 'function' ? matchMedia(query).matches : fallback;
  } catch {
    return fallback;
  }
}

function makeMaterial() {
  return new THREE.ShaderMaterial({
    name: 'Aquarium bubbles, wakes, and bioluminescent cursor',
    transparent: true,
    depthTest: true,
    depthWrite: false,
    blending: THREE.NormalBlending,
    toneMapped: false,
    fog: true,
    uniforms: THREE.UniformsUtils.merge([
      THREE.UniformsLib.fog,
      {
        uTime: { value: 0 },
        uPixelRatio: { value: 1 },
        uWaterGain: { value: 1 },
      },
    ]),
    vertexShader: `
      #include <common>
      #include <fog_pars_vertex>
      attribute float aProgress;
      attribute float aSize;
      attribute float aKind;
      attribute float aSeed;
      attribute float aOpacity;
      varying float vProgress;
      varying float vKind;
      varying float vSeed;
      varying float vOpacity;
      uniform float uTime;
      uniform float uPixelRatio;

      void main() {
        vProgress = aProgress;
        vKind = aKind;
        vSeed = aSeed;
        vOpacity = aOpacity;
        vec4 mvPosition = modelViewMatrix * vec4(position, 1.0);
        bool cursor = aKind > 1.5;
        float perspective = cursor
          ? 1.0
          : clamp(7.0 / max(2.5, -mvPosition.z), 0.55, 1.4);
        float pulse = cursor ? 1.0 + sin(uTime * 2.35) * 0.026 : 1.0;
        gl_PointSize = max(1.0, aSize * uPixelRatio * perspective * pulse);
        gl_Position = projectionMatrix * mvPosition;
        #include <fog_vertex>
      }
    `,
    fragmentShader: `
      #include <common>
      #include <fog_pars_fragment>
      varying float vProgress;
      varying float vKind;
      varying float vSeed;
      varying float vOpacity;
      uniform float uTime;
      uniform float uWaterGain;

      void main() {
        vec2 point = gl_PointCoord * 2.0 - 1.0;
        float radius = length(point);
        bool cursor = vKind > 1.5;
        if (!cursor && radius > 1.0) discard;

        float life = smoothstep(0.0, 0.12, vProgress)
          * (1.0 - smoothstep(0.68, 1.0, vProgress));
        vec3 color;
        float shape;
        if (cursor) {
          // A compact pearl with caustic rays reads as light in water rather
          // than another UI ring. The faint lower-left tail gives it a little
          // direction without competing with nearby typography.
          float pearl = 1.0 - smoothstep(0.035, 0.29, radius);
          float aura = exp(-radius * 4.15) * (1.0 - smoothstep(0.68, 1.0, radius));
          float verticalRay = exp(-abs(point.x) * 20.0)
            * pow(max(0.0, 1.0 - abs(point.y)), 1.85);
          float horizontalRay = exp(-abs(point.y) * 23.0)
            * pow(max(0.0, 1.0 - abs(point.x)), 2.25);
          float shimmer = 0.94 + 0.06 * sin(uTime * 2.2 + vSeed * 6.28318);
          float caustic = (verticalRay * 0.86 + horizontalRay * 0.68) * shimmer;

          vec2 tailDirection = normalize(vec2(-0.82, 0.57));
          vec2 tailNormal = vec2(-tailDirection.y, tailDirection.x);
          float tailDistance = dot(point, tailDirection);
          float tailWidth = abs(dot(point, tailNormal));
          float tail = smoothstep(0.02, 0.14, tailDistance)
            * (1.0 - smoothstep(0.20, 0.94, tailDistance))
            * exp(-tailWidth * 19.0)
            * 0.27;

          float glint = exp(-length(point - vec2(-0.055, 0.065)) * 17.0);
          float colorWeight = max(0.001, pearl + caustic + aura + tail);
          color = (
            vec3(0.92, 1.0, 0.99) * (pearl + glint * 0.54)
            + vec3(0.36, 1.0, 0.94) * (caustic + aura * 0.50)
            + vec3(0.12, 0.67, 0.76) * tail
          ) / colorWeight;
          shape = clamp(
            pearl + glint * 0.58 + caustic * 0.62 + aura * 0.34 + tail,
            0.0,
            1.0
          );
          life = 1.0;
        } else if (vKind > 0.5) {
          shape = pow(max(0.0, 1.0 - radius), 2.35);
          color = vec3(0.16, 0.66, 0.61);
        } else {
          float outer = 1.0 - smoothstep(0.76, 1.0, radius);
          float inner = smoothstep(0.45, 0.70, radius);
          float glint = pow(max(0.0, 1.0 - length(point - vec2(-0.32, 0.34)) * 2.2), 5.0);
          shape = outer * inner + glint * 0.65;
          color = vec3(0.40, 0.88, 0.84);
        }

        float alpha = shape * life * vOpacity * uWaterGain;
        if (alpha < 0.002) discard;
        gl_FragColor = vec4(color, alpha);
        #include <fog_fragment>
      }
    `,
  });
}

export class AquariumEffects {
  constructor({
    scene,
    camera,
    bridge,
    capacity = 320,
    seed = 0x0e77ec75,
    body = typeof document !== 'undefined' ? document.body : null,
    reducedMotion = mediaMatches('(prefers-reduced-motion: reduce)'),
    coarsePointer = mediaMatches('(pointer: coarse)'),
    finePointer = mediaMatches('(pointer: fine)', true),
    forcedColors = mediaMatches('(forced-colors: active)'),
  } = {}) {
    if (!scene?.add || !camera) throw new TypeError('AquariumEffects requires a scene and camera.');
    this.scene = scene;
    this.camera = camera;
    this.bridge = bridge || {};
    this.body = body;
    this.reducedMotion = Boolean(reducedMotion);
    this.coarsePointer = Boolean(coarsePointer);
    this.finePointer = Boolean(finePointer);
    this.forcedColors = Boolean(forcedColors);
    this.motionDisabled = this.reducedMotion || this.forcedColors;
    this.continuousEffects = !this.motionDisabled && !this.coarsePointer;
    this.capacity = Math.max(32, Math.min(1024, Math.floor(capacity) || 320));
    this.random = createEffectsRandom(seed);
    this.nextSlot = 1;
    this.staticDirty = true;
    this.ambientCarry = 0;
    this.pointerWakeCarry = 0;
    this.pointer = {
      active: false,
      pointerType: 'mouse',
      interactive: true,
      clientX: 0,
      clientY: 0,
      ndcX: 0,
      ndcY: 0,
      previousClientX: NaN,
      previousClientY: NaN,
      previousNdcX: 0,
      previousNdcY: 0,
    };
    this.agentEffects = new Map();

    const scalar = (length = this.capacity) => new Float32Array(length);
    this.positions = new Float32Array(this.capacity * 3);
    this.velocities = new Float32Array(this.capacity * 3);
    this.progress = scalar();
    this.sizes = scalar();
    this.kinds = scalar();
    this.seeds = scalar();
    this.opacities = scalar();
    this.ages = scalar();
    this.lifetimes = scalar();
    this.drags = scalar();
    this.buoyancies = scalar();
    this.active = new Uint8Array(this.capacity);

    this.geometry = new THREE.BufferGeometry();
    this.attributes = {
      position: new THREE.BufferAttribute(this.positions, 3),
      progress: new THREE.BufferAttribute(this.progress, 1),
      size: new THREE.BufferAttribute(this.sizes, 1),
      kind: new THREE.BufferAttribute(this.kinds, 1),
      seed: new THREE.BufferAttribute(this.seeds, 1),
      opacity: new THREE.BufferAttribute(this.opacities, 1),
    };
    Object.values(this.attributes).forEach((attribute) => attribute.setUsage(THREE.DynamicDrawUsage));
    this.geometry.setAttribute('position', this.attributes.position);
    this.geometry.setAttribute('aProgress', this.attributes.progress);
    this.geometry.setAttribute('aSize', this.attributes.size);
    this.geometry.setAttribute('aKind', this.attributes.kind);
    this.geometry.setAttribute('aSeed', this.attributes.seed);
    this.geometry.setAttribute('aOpacity', this.attributes.opacity);
    this.geometry.setDrawRange(0, this.capacity);

    this.material = makeMaterial();
    this.points = new THREE.Points(this.geometry, this.material);
    this.points.name = 'Shared aquarium bubble and wake pool';
    this.points.frustumCulled = false;
    this.points.renderOrder = 4;
    this.scene.add(this.points);

    this.cameraForward = new THREE.Vector3();
    this.cameraUp = new THREE.Vector3();
    this.cameraRight = new THREE.Vector3();
    this.anchor = new THREE.Vector3();
    this.rayPoint = new THREE.Vector3();
    this.rayDirection = new THREE.Vector3();
    this.planeOffset = new THREE.Vector3();
    this.emitter = new THREE.Vector3();
    this.exhaust = new THREE.Vector3();
    this.side = new THREE.Vector3();
    this.face = new THREE.Vector3();
    this.sampleWorld = new THREE.Vector3();

    this.stats = {
      activeCount: 0,
      ambientSpawned: 0,
      pointerWakeSpawned: 0,
      fleeBurstSpawned: 0,
      jetBubbleSpawned: 0,
      waterGain: 1,
      cursorMode: 'native',
      drawCalls: 1,
    };
    this._setCursorClass(false);
  }

  _randomRange(min, max) {
    return min + (max - min) * this.random();
  }

  _setCursorClass(active) {
    if (!this.body?.classList) return;
    this.body.classList.toggle(CURSOR_ACTIVE_CLASS, active);
    this.body.classList.toggle(CURSOR_NATIVE_CLASS, !active);
    this.stats.cursorMode = active ? 'bioluminescent-star' : 'native';
  }

  _updateBasis() {
    this.cameraForward.set(0, 0, -1).applyQuaternion(this.camera.quaternion).normalize();
    this.cameraUp.setFromMatrixColumn(this.camera.matrixWorld, 1).normalize();
    this.cameraRight.setFromMatrixColumn(this.camera.matrixWorld, 0).normalize();
  }

  _ndcToWorld(ndcX, ndcY, depthFactor, target) {
    this._updateBasis();
    if (this.bridge?.creature?.anchorPosition) {
      this.anchor.fromArray(this.bridge.creature.anchorPosition);
    } else {
      this.anchor.copy(this.camera.position).addScaledVector(this.cameraForward, 6);
    }
    const baseDepth = this.planeOffset.copy(this.anchor)
      .sub(this.camera.position)
      .dot(this.cameraForward);
    const planeDepth = Math.max(0.35, Math.abs(baseDepth) * clamp(depthFactor, 0.72, 1.25));
    this.rayPoint.set(ndcX, ndcY, 0).unproject(this.camera);
    this.rayDirection.copy(this.rayPoint).sub(this.camera.position).normalize();
    const denominator = this.rayDirection.dot(this.cameraForward);
    if (denominator <= EPSILON) return false;
    target.copy(this.camera.position).addScaledVector(
      this.rayDirection,
      planeDepth / denominator,
    );
    return true;
  }

  _allocateSlot() {
    for (let attempt = 0; attempt < this.capacity - 1; attempt += 1) {
      const slot = this.nextSlot;
      this.nextSlot += 1;
      if (this.nextSlot >= this.capacity) this.nextSlot = 1;
      if (!this.active[slot]) return slot;
    }
    const slot = this.nextSlot;
    this.nextSlot += 1;
    if (this.nextSlot >= this.capacity) this.nextSlot = 1;
    return slot;
  }

  _spawn(x, y, z, velocityX, velocityY, velocityZ, {
    life,
    size,
    kind,
    opacity,
    drag,
    buoyancy,
  }) {
    const slot = this._allocateSlot();
    const offset = slot * 3;
    this.positions[offset] = x;
    this.positions[offset + 1] = y;
    this.positions[offset + 2] = z;
    this.velocities[offset] = velocityX;
    this.velocities[offset + 1] = velocityY;
    this.velocities[offset + 2] = velocityZ;
    this.ages[slot] = 0;
    this.lifetimes[slot] = Math.max(0.05, life);
    this.progress[slot] = 0;
    this.sizes[slot] = Math.max(1, size);
    this.kinds[slot] = kind;
    this.seeds[slot] = this.random();
    this.opacities[slot] = clamp(opacity);
    this.drags[slot] = Math.max(0, drag);
    this.buoyancies[slot] = buoyancy;
    this.active[slot] = 1;
    this.staticDirty = true;
    return slot;
  }

  _spawnAmbientBubble() {
    const ndcX = this._randomRange(-0.96, 0.96);
    const ndcY = this._randomRange(-1.08, -0.88);
    if (!this._ndcToWorld(ndcX, ndcY, this._randomRange(0.88, 1.15), this.sampleWorld)) return;
    const drift = this._randomRange(-0.028, 0.028);
    const rise = this._randomRange(0.055, 0.12);
    this._spawn(
      this.sampleWorld.x,
      this.sampleWorld.y,
      this.sampleWorld.z,
      this.cameraRight.x * drift + this.cameraUp.x * rise,
      this.cameraRight.y * drift + this.cameraUp.y * rise,
      this.cameraRight.z * drift + this.cameraUp.z * rise,
      {
        life: this._randomRange(5.0, 8.0),
        size: this._randomRange(3.0, 7.0),
        kind: PARTICLE_BUBBLE,
        opacity: this._randomRange(0.14, 0.28),
        drag: 0.22,
        buoyancy: this._randomRange(0.008, 0.016),
      },
    );
    this.stats.ambientSpawned += 1;
  }

  _spawnPointerWake(ndcX, ndcY, pathX, pathY) {
    if (!this._ndcToWorld(ndcX, ndcY, 0.88, this.sampleWorld)) return;
    const pathLength = Math.max(EPSILON, Math.hypot(pathX, pathY));
    const trailX = -pathX / pathLength;
    const trailY = pathY / pathLength;
    const wakeSpeed = this._randomRange(0.018, 0.042);
    const rise = this._randomRange(0.015, 0.040);
    this._spawn(
      this.sampleWorld.x,
      this.sampleWorld.y,
      this.sampleWorld.z,
      this.cameraRight.x * trailX * wakeSpeed + this.cameraUp.x * (trailY * wakeSpeed + rise),
      this.cameraRight.y * trailX * wakeSpeed + this.cameraUp.y * (trailY * wakeSpeed + rise),
      this.cameraRight.z * trailX * wakeSpeed + this.cameraUp.z * (trailY * wakeSpeed + rise),
      {
        life: this._randomRange(0.55, 1.05),
        size: this._randomRange(2.4, 5.2),
        kind: this.random() < 0.30 ? PARTICLE_BUBBLE : PARTICLE_WAKE,
        opacity: this._randomRange(0.10, 0.22),
        drag: 2.4,
        buoyancy: 0.018,
      },
    );
    this.stats.pointerWakeSpawned += 1;
  }

  _sampleRig(rig) {
    const siphon = rig?.octopus?.motion?.siphonOpening
      || rig?.octopus?.model?.getObjectByName?.('Siphon_opening')
      || rig?.octopus?.motion?.siphon;
    if (siphon?.getWorldPosition) siphon.getWorldPosition(this.emitter);
    else if (rig?.frame?.getWorldPosition) rig.frame.getWorldPosition(this.emitter);
    else return false;

    if (rig?.frame?.matrixWorld) {
      this.exhaust.set(0, -1, 0).transformDirection(rig.frame.matrixWorld);
      this.side.set(1, 0, 0).transformDirection(rig.frame.matrixWorld);
      this.face.set(0, 0, 1).transformDirection(rig.frame.matrixWorld);
    } else {
      this.exhaust.copy(this.cameraUp).multiplyScalar(-1);
      this.side.copy(this.cameraRight);
      this.face.copy(this.cameraForward).multiplyScalar(-1);
    }
    return true;
  }

  _spawnFleeBurst(rig) {
    if (!this._sampleRig(rig)) return;
    const effectScale = clamp(Number(rig?.agent?.effectScale) || 1, 0.35, 1);
    for (let index = 0; index < 5; index += 1) {
      const side = this._randomRange(-0.12, 0.12);
      const face = this._randomRange(-0.09, 0.09);
      const speed = this._randomRange(0.06, 0.15);
      this._spawn(
        this.emitter.x,
        this.emitter.y,
        this.emitter.z,
        this.exhaust.x * speed + this.side.x * side + this.face.x * face,
        this.exhaust.y * speed + this.side.y * side + this.face.y * face,
        this.exhaust.z * speed + this.side.z * side + this.face.z * face,
        {
          life: this._randomRange(0.8, 1.45),
          size: this._randomRange(3.0, 6.5) * effectScale,
          kind: PARTICLE_BUBBLE,
          opacity: this._randomRange(0.18, 0.34),
          drag: 1.5,
          buoyancy: 0.026,
        },
      );
      this.stats.fleeBurstSpawned += 1;
    }
  }

  _spawnJetBubble(rig, burst = false) {
    if (!this._sampleRig(rig)) return;
    const effectScale = clamp(Number(rig?.agent?.effectScale) || 1, 0.35, 1);
    const speed = this._randomRange(burst ? 0.25 : 0.18, burst ? 0.48 : 0.36);
    const side = this._randomRange(-0.08, 0.08);
    const face = this._randomRange(-0.055, 0.055);
    this._spawn(
      this.emitter.x,
      this.emitter.y,
      this.emitter.z,
      this.exhaust.x * speed + this.side.x * side + this.face.x * face,
      this.exhaust.y * speed + this.side.y * side + this.face.y * face,
      this.exhaust.z * speed + this.side.z * side + this.face.z * face,
      {
        life: this._randomRange(burst ? 1.2 : 0.9, burst ? 2.2 : 1.7),
        size: this._randomRange(burst ? 4.0 : 2.7, burst ? 9.0 : 6.5) * effectScale,
        kind: PARTICLE_BUBBLE,
        opacity: this._randomRange(0.20, burst ? 0.43 : 0.34),
        drag: this._randomRange(1.25, 1.9),
        buoyancy: this._randomRange(0.025, 0.052),
      },
    );
    this.stats.jetBubbleSpawned += 1;
  }

  _spawnJetBurst(rig) {
    for (let index = 0; index < 12; index += 1) this._spawnJetBubble(rig, true);
  }

  notePointerSample(event, ndcX, ndcY) {
    const pointer = this.pointer;
    const clientX = Number(event?.clientX);
    const clientY = Number(event?.clientY);
    const nextNdcX = Number(ndcX);
    const nextNdcY = Number(ndcY);
    if (![clientX, clientY, nextNdcX, nextNdcY].every(Number.isFinite)) return;

    pointer.active = true;
    pointer.pointerType = event?.pointerType || 'mouse';
    pointer.interactive = isInteractivePointerTarget(event?.target);
    pointer.clientX = clientX;
    pointer.clientY = clientY;
    pointer.ndcX = nextNdcX;
    pointer.ndcY = nextNdcY;

    const eligible = this.continuousEffects
      && pointer.pointerType === 'mouse'
      && !pointer.interactive
      && waterEffectGain(this.bridge?.inside) > 0.02;
    if (eligible && Number.isFinite(pointer.previousClientX)) {
      const dx = clientX - pointer.previousClientX;
      const dy = clientY - pointer.previousClientY;
      const distance = Math.hypot(dx, dy);
      const plan = pointerWakePlan(distance, this.pointerWakeCarry);
      this.pointerWakeCarry = plan.carry;
      for (let index = 0; index < plan.count; index += 1) {
        const fraction = clamp(plan.firstFraction + plan.stepFraction * index);
        this._spawnPointerWake(
          pointer.previousNdcX + (nextNdcX - pointer.previousNdcX) * fraction,
          pointer.previousNdcY + (nextNdcY - pointer.previousNdcY) * fraction,
          dx,
          dy,
        );
      }
    } else {
      this.pointerWakeCarry = 0;
    }

    pointer.previousClientX = clientX;
    pointer.previousClientY = clientY;
    pointer.previousNdcX = nextNdcX;
    pointer.previousNdcY = nextNdcY;
    this._syncCursor();
  }

  clearPointer() {
    this.pointer.active = false;
    this.pointer.previousClientX = NaN;
    this.pointer.previousClientY = NaN;
    this.pointerWakeCarry = 0;
    this.active[POINTER_SLOT] = 0;
    this.opacities[POINTER_SLOT] = 0;
    this._setCursorClass(false);
  }

  _syncCursor() {
    const active = shouldUsePressureCursor({
      active: this.pointer.active,
      pointerType: this.pointer.pointerType,
      interactive: this.pointer.interactive,
      finePointer: this.finePointer,
      coarsePointer: this.coarsePointer,
      reducedMotion: this.reducedMotion,
      forcedColors: this.forcedColors,
      inside: this.bridge?.inside,
    });
    this._setCursorClass(active);
    if (!active || !this._ndcToWorld(
      this.pointer.ndcX,
      this.pointer.ndcY,
      0.76,
      this.sampleWorld,
    )) {
      this.active[POINTER_SLOT] = 0;
      this.opacities[POINTER_SLOT] = 0;
      return;
    }
    this.active[POINTER_SLOT] = 1;
    this.positions[0] = this.sampleWorld.x;
    this.positions[1] = this.sampleWorld.y;
    this.positions[2] = this.sampleWorld.z;
    this.progress[POINTER_SLOT] = 0.5;
    this.sizes[POINTER_SLOT] = 29;
    this.kinds[POINTER_SLOT] = PARTICLE_CURSOR;
    this.seeds[POINTER_SLOT] = 0.375;
    this.opacities[POINTER_SLOT] = 0.94;
    this.staticDirty = true;
  }

  _advanceParticles(deltaTime) {
    const dt = clamp(Number(deltaTime) || 0, 0, 0.05);
    let activeCount = this.active[POINTER_SLOT] ? 1 : 0;
    for (let slot = 1; slot < this.capacity; slot += 1) {
      if (!this.active[slot]) continue;
      this.ages[slot] += dt;
      if (this.ages[slot] >= this.lifetimes[slot]) {
        this.active[slot] = 0;
        this.opacities[slot] = 0;
        this.progress[slot] = 1;
        continue;
      }
      const offset = slot * 3;
      const drag = Math.exp(-this.drags[slot] * dt);
      this.velocities[offset] = this.velocities[offset] * drag
        + this.cameraUp.x * this.buoyancies[slot] * dt;
      this.velocities[offset + 1] = this.velocities[offset + 1] * drag
        + this.cameraUp.y * this.buoyancies[slot] * dt;
      this.velocities[offset + 2] = this.velocities[offset + 2] * drag
        + this.cameraUp.z * this.buoyancies[slot] * dt;
      this.positions[offset] += this.velocities[offset] * dt;
      this.positions[offset + 1] += this.velocities[offset + 1] * dt;
      this.positions[offset + 2] += this.velocities[offset + 2] * dt;
      this.progress[slot] = this.ages[slot] / this.lifetimes[slot];
      activeCount += 1;
    }
    this.stats.activeCount = activeCount;
  }

  _upload() {
    this.attributes.position.needsUpdate = true;
    this.attributes.progress.needsUpdate = true;
    this.attributes.opacity.needsUpdate = true;
    if (this.staticDirty) {
      this.attributes.size.needsUpdate = true;
      this.attributes.kind.needsUpdate = true;
      this.attributes.seed.needsUpdate = true;
      this.staticDirty = false;
    }
  }

  update(deltaTime, time, rigs = []) {
    const dt = clamp(Number(deltaTime) || 0, 0, 0.05);
    const gain = waterEffectGain(this.bridge?.inside);
    this.stats.waterGain = gain;
    this.material.uniforms.uTime.value = Number(time) || 0;
    this.material.uniforms.uPixelRatio.value = clamp(
      this.bridge?.viewport?.dpr || 1,
      1,
      2,
    );
    this.material.uniforms.uWaterGain.value = gain;
    this.points.visible = gain > 0.001 && !this.motionDisabled;
    this._updateBasis();
    this._syncCursor();

    const canEmit = !this.motionDisabled && gain > 0.01;
    if (canEmit) {
      if (this.continuousEffects) {
        this.ambientCarry += dt * 1.55 * gain;
        const ambientCount = Math.min(2, Math.floor(this.ambientCarry));
        this.ambientCarry -= ambientCount;
        for (let index = 0; index < ambientCount; index += 1) this._spawnAmbientBubble();
      } else {
        this.ambientCarry = 0;
      }
    } else {
      this.ambientCarry = 0;
    }

    // Synchronize trigger edges even while the portal hides water. Otherwise
    // reversing out of the screen could replay a stale flee burst.
    rigs.forEach((rig) => {
      const agent = rig?.agent;
      if (!agent) return;
      let state = this.agentEffects.get(agent.id);
      if (!state) {
        state = {
          episode: Number(agent.fleeEpisode) || 0,
          stage: agent.fleeStage || 'none',
          trailCarry: 0,
        };
        this.agentEffects.set(agent.id, state);
        return;
      }
      const episode = Number(agent.fleeEpisode) || 0;
      const stage = agent.fleeStage || 'none';
      const trigger = effectTriggerMask(state.episode, state.stage, episode, stage);
      if (canEmit) {
        if (trigger & EFFECT_TRIGGER_FLEE) this._spawnFleeBurst(rig);
        if (trigger & EFFECT_TRIGGER_JET) this._spawnJetBurst(rig);
      }

      if (canEmit && this.continuousEffects && stage === 'jet') {
        const jet = clamp(agent.jetCycle?.jet || agent.motionState?.jet || 0);
        state.trailCarry += dt * (8 + jet * 24) * gain;
        const trailCount = Math.min(4, Math.floor(state.trailCarry));
        state.trailCarry -= trailCount;
        for (let index = 0; index < trailCount; index += 1) {
          this._spawnJetBubble(rig, false);
        }
      } else {
        state.trailCarry = 0;
      }
      state.episode = episode;
      state.stage = stage;
    });

    this._advanceParticles(dt);
    this._upload();
    return this.stats;
  }

  dispose() {
    this.clearPointer();
    this.scene.remove(this.points);
    this.geometry.dispose();
    this.material.dispose();
  }
}
