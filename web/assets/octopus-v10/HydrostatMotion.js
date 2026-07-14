import * as THREE from 'three';
import {
  TAU,
  clamp01,
  smootherstep,
} from './math.js';
import { buildOpenSwimDirections, buildTrailingDirections } from './SwimChainSolver.js?v=20';

const ARM_COUNT = 8;
const BONES_PER_ARM = 16;
const GOLDEN_PHASE = 2.399963229728653;
const LONG_AXIS = new THREE.Vector3(0, 1, 0);
const DIRECTION_INTEGRATION_STEP = 1 / 120;
const DIRECTION_EPSILON = 1e-7;

function fract(value) {
  return value - Math.floor(value);
}

function windowedPulse(value, riseStart, riseEnd, fallStart, fallEnd) {
  return smootherstep(riseStart, riseEnd, value)
    * (1 - smootherstep(fallStart, fallEnd, value));
}

function gaussian(value, center, width) {
  const normalized = (value - center) / Math.max(0.0001, width);
  return Math.exp(-0.5 * normalized * normalized);
}

/**
 * A jet contains a short high-power expulsion, a long low-drag glide and a
 * refill. The physics and the body deformation both sample this same cycle.
 */
export function sampleJetCycle(phase) {
  const p = fract(phase);
  const impulsePhase = clamp01((p - 0.10) / 0.18);
  return {
    phase: p,
    // A short hyper-inflation makes the beginning of a commanded burst
    // readable. The mantle then stays narrow through the low-drag glide and
    // refills only near the end of the cycle.
    preload: windowedPulse(p, 0.00, 0.055, 0.105, 0.19),
    // Deflation continues through almost the complete jet pulse. Ending the
    // shape change at the jet peak made the mantle appear to stop working while
    // thrust was still being generated, and discarded late added-mass recovery.
    squeeze: smootherstep(0.085, 0.275, p) * (1 - smootherstep(0.62, 0.94, p)),
    jet: p >= 0.10 && p <= 0.28 ? Math.sin(Math.PI * impulsePhase) ** 2 : 0,
    glide: windowedPulse(p, 0.245, 0.36, 0.72, 0.91),
    intake: windowedPulse(p, 0.68, 0.79, 0.94, 0.995),
  };
}

/**
 * Arm-powered swimming has a short closing power stroke and a recovery that is
 * approximately 2.5 times longer. Unlike a jet, the visible arm crown supplies
 * the stroke while the mantle only breathes.
 */
export function sampleArmSwimCycle(phase) {
  const p = fract(phase);
  let closure = 0;
  if (p < 0.20) closure = smootherstep(0, 0.20, p);
  else if (p < 0.34) closure = 1;
  else if (p < 0.84) closure = 1 - smootherstep(0.34, 0.84, p);
  return {
    phase: p,
    closure,
    openness: 1 - closure,
    power: windowedPulse(p, 0, 0.05, 0.20, 0.285),
    recovery: windowedPulse(p, 0.34, 0.41, 0.76, 0.865),
  };
}

/**
 * Phase a joint along the arm-swimming stroke. The biological power stroke is
 * a base-to-tip closing wave, not one rigid umbrella hinge. During the slower
 * recovery the tip begins to open first and the whole arm remains slightly
 * elongated before relaxing back to its authored length.
 */
export function sampleArmSwimJoint(phase, normalizedLength) {
  const p = fract(phase);
  const s = clamp01(normalizedLength);
  const powerDelay = s * 0.055;
  const recoveryDelay = (1 - s) * 0.045;
  let closure = 0;
  if (p < 0.34) {
    closure = smootherstep(powerDelay, 0.175 + powerDelay, p);
  } else if (p < 0.88) {
    closure = 1 - smootherstep(0.34 + recoveryDelay, 0.795 + recoveryDelay, p);
  }
  const elongation = windowedPulse(p, 0.02, 0.12, 0.70, 0.86);
  const recovery = windowedPulse(p, 0.34, 0.41, 0.76, 0.865);
  return {
    closure,
    elongation,
    recovery,
  };
}

/**
 * A single arm task expressed as curvature and constant-volume elongation.
 * The curvature maximum travels base-to-tip, matching the motion primitives
 * measured in reconstructed octopus arms instead of rotating every joint alike.
 */
export function sampleArmTask(task, s) {
  if (!task?.active) return { x: 0, z: 0, stretch: 1, push: 0 };
  const phase = clamp01(task.phase);
  const intensity = task.intensity ?? 1;
  const direction = task.direction ?? 1;
  const reach = smootherstep(0.01, 0.34, phase) * (1 - smootherstep(0.72, 0.96, phase));
  const plant = windowedPulse(phase, 0.30, 0.46, 0.62, 0.78);
  const push = windowedPulse(phase, 0.43, 0.56, 0.72, 0.86);
  const recover = smootherstep(0.72, 0.98, phase);
  const center = 0.16 + 0.70 * smootherstep(0.02, 0.58, phase);
  const bend = gaussian(s, center, 0.17 + phase * 0.030);
  const distalHook = gaussian(s, 0.87, 0.13);
  const proximal = gaussian(s, 0.30, 0.23);
  const rootFreedom = smootherstep(0.08, 0.34, s);

  return {
    x: rootFreedom * intensity * (
      bend * reach * 0.270
      - distalHook * plant * 0.130
      + distalHook * recover * 0.045
    ),
    z: rootFreedom * intensity * direction * (
      bend * reach * 0.650
      - distalHook * plant * 0.180
    ),
    stretch: 1 + proximal * push * 0.065 - distalHook * recover * 0.018,
    push,
  };
}

/**
 * Stateful, seeded arm selection. Explore keeps two to four arm programs alive
 * but deliberately has no gait period. New programs favor neighboring arms
 * without enforcing a sequence, preserving radial/ad-hoc recruitment.
 */
export class ArmRecruitment {
  constructor(seed = 0x6d2b79f5) {
    this.seed = seed >>> 0;
    this.tasks = Array.from({ length: ARM_COUNT }, () => ({
      active: false,
      phase: 1,
      duration: 1,
      intensity: 0,
      direction: 1,
      cooldown: 0,
      generation: 0,
    }));
    this.mode = null;
    this.recruitmentTimer = 0;
    this.lastRecruit = -1;
  }

  random() {
    let value = this.seed;
    value ^= value << 13;
    value ^= value >>> 17;
    value ^= value << 5;
    this.seed = value >>> 0;
    return this.seed / 4294967296;
  }

  enter(mode) {
    this.mode = mode;
    this.recruitmentTimer = mode === 'rest' ? 2.2 : 0;
    this.lastRecruit = -1;
    this.tasks.forEach((task) => {
      task.active = false;
      task.phase = 1;
      task.cooldown = this.random() * (mode === 'explore' ? 0.7 : 2.0);
    });
  }

  pickCandidate() {
    const available = this.tasks
      .map((task, index) => ({ task, index }))
      .filter(({ task }) => !task.active && task.cooldown <= 0);
    if (!available.length) return -1;

    if (this.lastRecruit >= 0 && this.random() < 0.44) {
      const neighborOrder = this.random() < 0.5 ? [-1, 1] : [1, -1];
      for (const offset of neighborOrder) {
        const neighbor = (this.lastRecruit + offset + ARM_COUNT) % ARM_COUNT;
        if (available.some(({ index }) => index === neighbor)) return neighbor;
      }
    }
    return available[Math.floor(this.random() * available.length)].index;
  }

  start(index, mode) {
    if (index < 0) return false;
    const task = this.tasks[index];
    task.active = true;
    task.phase = 0;
    task.duration = mode === 'explore'
      ? 2.8 + this.random() * 2.6
      : 4.8 + this.random() * 2.8;
    task.intensity = mode === 'explore'
      ? 0.76 + this.random() * 0.36
      : 0.18 + this.random() * 0.15;
    const side = this.random() < 0.5 ? -1 : 1;
    task.direction = side * (0.58 + this.random() * 0.42);
    task.generation += 1;
    this.lastRecruit = index;
    this.recruitmentTimer = mode === 'explore'
      ? 0.42 + this.random() * 1.35
      : 5.2 + this.random() * 5.8;
    return true;
  }

  update(deltaTime, mode) {
    if (mode !== this.mode) this.enter(mode);
    this.recruitmentTimer -= deltaTime;

    this.tasks.forEach((task) => {
      task.cooldown = Math.max(0, task.cooldown - deltaTime);
      if (!task.active) return;
      task.phase += deltaTime / task.duration;
      if (task.phase >= 1) {
        task.phase = 1;
        task.active = false;
        task.cooldown = mode === 'explore'
          ? 1.0 + this.random() * 2.8
          : 3.5 + this.random() * 4.5;
      }
    });

    let active = this.tasks.filter((task) => task.active).length;
    if (mode === 'explore') {
      while (active < 2) {
        const started = this.start(this.pickCandidate(), mode);
        if (!started) break;
        active += 1;
      }
      if (active < 4 && this.recruitmentTimer <= 0 && this.random() < 0.72) {
        if (this.start(this.pickCandidate(), mode)) active += 1;
        else this.recruitmentTimer = 0.35;
      }
    } else if (mode === 'rest' && active === 0 && this.recruitmentTimer <= 0) {
      this.start(this.pickCandidate(), mode);
    }
    return active;
  }
}

export function sampleArmCurvature({
  mode,
  armIndex,
  boneIndex,
  time,
  strokePhase = 0,
  speed = 0,
  steer = 0,
  thrust = 0,
  task = null,
}) {
  const s = boneIndex / (BONES_PER_ARM - 1);
  if (boneIndex === 0) return { x: 0, z: 0, stretch: 1, push: 0 };

  const rootFreedom = smootherstep(0.08, 0.34, s);
  const distal = s * s;
  const seed = armIndex * GOLDEN_PHASE;

  if (mode === 'explore') {
    const primitive = sampleArmTask(task, s);
    const supportBias = task?.active ? 0 : Math.sin(seed + 0.7) * distal * 0.009;
    return { ...primitive, z: primitive.z + rootFreedom * supportBias };
  }

  if (mode === 'swim') {
    const cycle = sampleJetCycle(strokePhase);
    const armCycle = sampleArmSwimCycle(strokePhase);
    const jetting = Math.abs(thrust) > 0.01 || speed > 0.075;
    // Runtime swimming uses the world-space chain solver below. Keep this pure
    // sampler for telemetry/backward compatibility, but do not reintroduce the
    // cumulative Euler sweep that made arms circle and cross around the body.
    return {
      x: 0,
      z: 0,
      stretch: 1 + rootFreedom * (jetting ? cycle.glide * speed * 0.018 : armCycle.closure * 0.012),
      push: jetting ? cycle.jet : armCycle.power,
    };
  }

  // At rest the mantle breathes continuously, but arms do not all perform a
  // choreographed wave. A single low-amplitude task occasionally probes while
  // the other tips only inherit tiny, non-commensurate tissue drift.
  const primitive = sampleArmTask(task, s);
  const tissueDrift = rootFreedom * distal * (
    Math.sin(time * (0.17 + armIndex * 0.007) + seed - s * 1.7) * 0.0045
  );
  return {
    x: primitive.x + tissueDrift,
    z: primitive.z + tissueDrift * 0.72,
    stretch: primitive.stretch,
    push: primitive.push,
  };
}

function captureTransform(object) {
  return {
    position: object.position.clone(),
    quaternion: object.quaternion.clone(),
    scale: object.scale.clone(),
  };
}

function dampScalar(current, target, response, deltaTime) {
  return current + (target - current) * (1 - Math.exp(-response * deltaTime));
}

/**
 * A muscular arm does not point directly at each newly sampled guide. Water
 * and tissue inertia make its body-space direction a second-order state. The
 * state deliberately contains no twist: authored bone roll remains responsible
 * for keeping the suckers on the oral face.
 */
export function createBodyDirectionDynamics(direction = LONG_AXIS) {
  const normalized = direction.clone().normalize();
  const fallbackAxis = Math.abs(normalized.x) < 0.8
    ? new THREE.Vector3(1, 0, 0)
    : new THREE.Vector3(0, 0, 1);
  fallbackAxis.addScaledVector(normalized, -fallbackAxis.dot(normalized)).normalize();
  return {
    direction: normalized,
    angularVelocity: new THREE.Vector3(),
    fallbackAxis,
    target: new THREE.Vector3(),
    error: new THREE.Vector3(),
    acceleration: new THREE.Vector3(),
    angularStep: new THREE.Vector3(),
    cross: new THREE.Vector3(),
    nextCross: new THREE.Vector3(),
    rotation: new THREE.Quaternion(),
  };
}

/**
 * Advance one bounded spherical spring. Integration is split at the same
 * 120 Hz step used by the animation engine, so a slow render frame cannot make
 * a distal joint explode. A damping ratio at or above one prevents the visible
 * ringing that a lightly damped tentacle chain produces.
 */
export function stepBodyDirectionDynamics(
  dynamics,
  targetDirection,
  deltaTime,
  naturalFrequency,
  dampingRatio,
  maxAngularSpeed,
) {
  if (!(deltaTime > 0)) return dynamics.direction;
  const frequency = Math.max(0.01, naturalFrequency);
  const damping = Math.max(1, dampingRatio);
  const speedLimit = Math.max(0.01, maxAngularSpeed);
  const accelerationLimit = frequency * speedLimit * 2.25;
  const target = dynamics.target.copy(targetDirection);
  if (target.lengthSq() < DIRECTION_EPSILON) return dynamics.direction;
  target.normalize();

  let remaining = Math.min(deltaTime, 0.10);
  while (remaining > DIRECTION_EPSILON) {
    const step = Math.min(DIRECTION_INTEGRATION_STEP, remaining);
    remaining -= step;

    const direction = dynamics.direction.normalize();
    const velocity = dynamics.angularVelocity;
    // Spin around the arm's own long axis cannot change its direction and
    // would only corrupt the authored sucker roll, so discard it explicitly.
    velocity.addScaledVector(direction, -velocity.dot(direction));

    dynamics.cross.crossVectors(direction, target);
    const sine = dynamics.cross.length();
    const cosine = THREE.MathUtils.clamp(direction.dot(target), -1, 1);
    const angle = Math.atan2(sine, cosine);
    if (angle < DIRECTION_EPSILON) {
      dynamics.direction.copy(target);
      velocity.multiplyScalar(Math.exp(-2 * damping * frequency * step));
      continue;
    }

    if (sine > DIRECTION_EPSILON) {
      dynamics.fallbackAxis.copy(dynamics.cross).multiplyScalar(1 / sine);
    } else {
      // Antiparallel targets have no unique shortest-turn axis. Reuse the last
      // stable pole and reproject it so the chain cannot flip sides randomly.
      dynamics.fallbackAxis.addScaledVector(
        direction,
        -dynamics.fallbackAxis.dot(direction),
      );
      if (dynamics.fallbackAxis.lengthSq() < DIRECTION_EPSILON) {
        dynamics.fallbackAxis.set(
          Math.abs(direction.x) < 0.8 ? 1 : 0,
          0,
          Math.abs(direction.x) < 0.8 ? 0 : 1,
        );
        dynamics.fallbackAxis.addScaledVector(
          direction,
          -dynamics.fallbackAxis.dot(direction),
        );
      }
      dynamics.fallbackAxis.normalize();
    }

    dynamics.error.copy(dynamics.fallbackAxis).multiplyScalar(angle);
    dynamics.acceleration.copy(dynamics.error).multiplyScalar(frequency * frequency)
      .addScaledVector(velocity, -2 * damping * frequency);
    if (dynamics.acceleration.length() > accelerationLimit) {
      dynamics.acceleration.setLength(accelerationLimit);
    }
    velocity.addScaledVector(dynamics.acceleration, step);
    if (velocity.length() > speedLimit) velocity.setLength(speedLimit);

    dynamics.angularStep.copy(velocity).multiplyScalar(step);
    dynamics.angularStep.addScaledVector(
      direction,
      -dynamics.angularStep.dot(direction),
    );
    let stepAngle = dynamics.angularStep.length();
    if (stepAngle < DIRECTION_EPSILON) continue;
    if (stepAngle > speedLimit * step) {
      dynamics.angularStep.multiplyScalar((speedLimit * step) / stepAngle);
      stepAngle = speedLimit * step;
    }

    // Do not cross a stationary target along the active correction axis. This
    // retains lateral follow-through while removing the one-frame snap-back
    // that reads as vibration when a distal joint reaches its guide.
    const forwardStep = dynamics.angularStep.dot(dynamics.fallbackAxis);
    if (forwardStep > angle) {
      dynamics.angularStep.addScaledVector(
        dynamics.fallbackAxis,
        angle - forwardStep,
      );
      stepAngle = dynamics.angularStep.length();
    }
    if (stepAngle < DIRECTION_EPSILON) {
      dynamics.direction.copy(target);
      velocity.set(0, 0, 0);
      continue;
    }

    const stepAxis = dynamics.angularStep.multiplyScalar(1 / stepAngle);
    dynamics.rotation.setFromAxisAngle(stepAxis, stepAngle);
    dynamics.direction.applyQuaternion(dynamics.rotation).normalize();

    dynamics.nextCross.crossVectors(dynamics.direction, target);
    if (dynamics.cross.dot(dynamics.nextCross) < 0
      && velocity.dot(dynamics.fallbackAxis) > 0) {
      dynamics.direction.copy(target);
      velocity.addScaledVector(
        dynamics.fallbackAxis,
        -velocity.dot(dynamics.fallbackAxis),
      );
    }
    velocity.addScaledVector(
      dynamics.direction,
      -velocity.dot(dynamics.direction),
    );
  }

  return dynamics.direction;
}

export class HydrostatMotion {
  constructor(model) {
    this.model = model;
    this.body = model.getObjectByName('Body');
    this.mantle = model.getObjectByName('Mantle');
    this.siphon = model.getObjectByName('Siphon');
    this.siphonOpening = model.getObjectByName('Siphon_opening');
    this.arms = Array.from({ length: ARM_COUNT }, (_, armIndex) => (
      Array.from({ length: BONES_PER_ARM }, (_, boneIndex) => (
        model.getObjectByName(`Arm_${String(armIndex + 1).padStart(2, '0')}_${String(boneIndex).padStart(2, '0')}`)
      ))
    ));

    if (!this.body || !this.mantle || this.arms.some((arm) => arm.some((bone) => !bone))) {
      throw new Error('The octopus GLB does not contain the expected 130-bone hydrostat rig.');
    }

    this.bodyRest = captureTransform(this.body);
    this.mantleRest = captureTransform(this.mantle);
    this.siphonRest = this.siphon ? captureTransform(this.siphon) : null;
    this.siphonOpeningRest = this.siphonOpening ? captureTransform(this.siphonOpening) : null;
    this.armRest = this.arms.map((arm) => arm.map(captureTransform));
    this.deltaEuler = new THREE.Euler(0, 0, 0, 'XYZ');
    this.deltaQuaternion = new THREE.Quaternion();
    this.targetQuaternion = new THREE.Quaternion();
    this.directionDelta = new THREE.Quaternion();
    this.rollQuaternion = new THREE.Quaternion();
    this.bodyWorldQuaternion = new THREE.Quaternion();
    this.parentWorldQuaternion = new THREE.Quaternion();
    this.targetBodyDirection = new THREE.Vector3();
    this.targetWorldDirection = new THREE.Vector3();
    this.targetParentDirection = new THREE.Vector3();
    this.swimGuides = this.initializeSwimGuides();
    this.swimDirectionDynamics = this.swimGuides.map((guide) => (
      guide.restDirections.map((direction) => createBodyDirectionDynamics(direction))
    ));
    this.swimModeActive = false;
    this.inverseBodyQuaternion = new THREE.Quaternion();
    this.syncWorldQuaternion = new THREE.Quaternion();
    this.recruitment = new ArmRecruitment();
    this.motionTime = 0;
  }

  initializeSwimGuides() {
    this.model.updateMatrixWorld(true);
    const bodyInverse = this.body.matrixWorld.clone().invert();
    const inverseBodyQuaternion = this.body.getWorldQuaternion(new THREE.Quaternion()).invert();

    return this.arms.map((arm, armIndex) => {
      const heads = arm.map((bone) => (
        bone.getWorldPosition(new THREE.Vector3()).applyMatrix4(bodyInverse)
      ));
      const restDirections = arm.map((bone) => (
        LONG_AXIS.clone()
          .applyQuaternion(bone.getWorldQuaternion(new THREE.Quaternion()))
          .applyQuaternion(inverseBodyQuaternion)
          .normalize()
      ));
      const restLocalDirections = arm.map((boneIndexBone, boneIndex) => (
        LONG_AXIS.clone().applyQuaternion(this.armRest[armIndex][boneIndex].quaternion).normalize()
      ));
      const segmentLengths = arm.map((bone, boneIndex) => (
        boneIndex < BONES_PER_ARM - 1
          ? heads[boneIndex + 1].distanceTo(heads[boneIndex])
          : Math.max(0.05, Math.abs(bone.position.y))
      ));

      const solve = (trailingSign, tightness) => buildTrailingDirections({
        armIndex,
        root: heads[0],
        rootDirection: restDirections[0],
        restDirections,
        segmentLengths,
        trailingSign,
        tightness,
      }).directions;
      const open = buildOpenSwimDirections({
        armIndex,
        root: heads[0],
        restDirections,
      });

      return {
        restDirections,
        restLocalDirections,
        open,
        scullForward: solve(1, 0.68),
        scullReverse: solve(-1, 0.68),
        jetForward: solve(1, 1),
        jetReverse: solve(-1, 1),
      };
    });
  }

  applyDelta(bone, rest, target, response, deltaTime) {
    this.deltaEuler.set(target.x, target.y ?? 0, target.z);
    this.deltaQuaternion.setFromEuler(this.deltaEuler);
    this.targetQuaternion.copy(rest.quaternion).multiply(this.deltaQuaternion);
    const blend = 1 - Math.exp(-response * deltaTime);
    bone.quaternion.slerp(this.targetQuaternion, blend);

    // Bone local Y is its long axis. Reciprocal radial scaling approximates the
    // constant-volume constraint of a muscular hydrostat during elongation.
    const stretch = THREE.MathUtils.clamp(target.stretch ?? 1, 0.94, 1.08);
    const radial = 1 / Math.sqrt(stretch);
    bone.scale.x = dampScalar(bone.scale.x, rest.scale.x * radial, response, deltaTime);
    bone.scale.y = dampScalar(bone.scale.y, rest.scale.y * stretch, response, deltaTime);
    bone.scale.z = dampScalar(bone.scale.z, rest.scale.z * radial, response, deltaTime);
  }

  syncSwimDirectionDynamics() {
    this.inverseBodyQuaternion.copy(this.bodyWorldQuaternion).invert();
    for (let armIndex = 0; armIndex < ARM_COUNT; armIndex += 1) {
      for (let boneIndex = 0; boneIndex < BONES_PER_ARM; boneIndex += 1) {
        const dynamics = this.swimDirectionDynamics[armIndex][boneIndex];
        this.arms[armIndex][boneIndex].getWorldQuaternion(this.syncWorldQuaternion);
        dynamics.direction.copy(LONG_AXIS)
          .applyQuaternion(this.syncWorldQuaternion)
          .applyQuaternion(this.inverseBodyQuaternion)
          .normalize();
        dynamics.angularVelocity.set(0, 0, 0);
      }
    }
  }

  applyResolvedDirection(
    bone,
    rest,
    restLocalDirection,
    resolvedBodyDirection,
    deltaTime,
    stretch = 1,
    roll = 0,
  ) {
    this.targetWorldDirection.copy(resolvedBodyDirection)
      .applyQuaternion(this.bodyWorldQuaternion)
      .normalize();
    bone.parent.getWorldQuaternion(this.parentWorldQuaternion);
    this.parentWorldQuaternion.invert();
    this.targetParentDirection.copy(this.targetWorldDirection)
      .applyQuaternion(this.parentWorldQuaternion)
      .normalize();

    this.directionDelta.setFromUnitVectors(restLocalDirection, this.targetParentDirection);
    this.targetQuaternion.copy(this.directionDelta).multiply(rest.quaternion);
    if (Math.abs(roll) > 0.00001) {
      this.rollQuaternion.setFromAxisAngle(LONG_AXIS, roll);
      this.targetQuaternion.multiply(this.rollQuaternion);
    }
    // Direction inertia has already been integrated in body space. Applying a
    // second first-order quaternion filter here would erase distal follow-through.
    bone.quaternion.copy(this.targetQuaternion);

    const response = 8;
    const axial = THREE.MathUtils.clamp(stretch, 0.94, 1.08);
    const radial = 1 / Math.sqrt(axial);
    bone.scale.x = dampScalar(bone.scale.x, rest.scale.x * radial, response, deltaTime);
    bone.scale.y = dampScalar(bone.scale.y, rest.scale.y * axial, response, deltaTime);
    bone.scale.z = dampScalar(bone.scale.z, rest.scale.z * radial, response, deltaTime);
    bone.updateWorldMatrix(false, false);
  }

  update(deltaTime, state) {
    const muscleDrive = Math.max(0.18, state.motionScale ?? 1);
    this.motionTime += deltaTime * muscleDrive;
    const mode = state.mode === 'swim' ? 'swim' : state.mode === 'explore' ? 'explore' : 'rest';
    const speed = clamp01(state.navigationSpeed ?? 0);
    const steer = THREE.MathUtils.clamp(state.steer ?? 0, -1, 1);
    const jetPhase = state.jetPhase ?? state.strokePhase ?? 0;
    const armPhase = state.armPhase ?? state.strokePhase ?? 0;
    const cycle = sampleJetCycle(jetPhase);
    const armCycle = sampleArmSwimCycle(armPhase);
    const jetBundle = smootherstep(0.025, 0.22, cycle.phase)
      * (1 - smootherstep(0.70, 0.985, cycle.phase));
    const propulsionStyle = state.propulsionStyle
      ?? ((Math.abs(state.thrust ?? 0) > 0.01 || speed > 0.075) ? 'jet' : 'arm');
    const jetting = mode === 'swim' && propulsionStyle === 'jet';
    const jetActive = jetting && (state.jetActive ?? Math.abs(state.thrust ?? 0) > 0.01);
    this.recruitment.update(deltaTime * muscleDrive, mode);

    const breath = Math.sin(this.motionTime * 0.69)
      + Math.sin(this.motionTime * 0.27 + 1.2) * 0.18;
    const breathAmount = mode === 'swim'
      ? jetting ? 0.004 : 0.014
      : mode === 'explore' ? 0.011 : 0.022;
    const squeezeStrength = jetActive ? cycle.squeeze * (0.88 + speed * 0.12) : 0;
    const intakeStrength = jetActive ? cycle.intake * (0.76 + speed * 0.16) : 0;
    const preloadStrength = jetActive ? cycle.preload : 0;
    const inflationStrength = Math.max(preloadStrength, intakeStrength * 0.92);
    const radialScale = 1 + breath * breathAmount + inflationStrength * 0.095 - squeezeStrength * 0.180;
    const axialScale = 1 - breath * breathAmount * 0.52 - inflationStrength * 0.040 + squeezeStrength * 0.140;
    const mantleResponse = jetActive ? 14.0 : 2.4;
    this.mantle.scale.x = dampScalar(this.mantle.scale.x, this.mantleRest.scale.x * radialScale, mantleResponse, deltaTime);
    // Blender/Three bones use local Y as the bone's long axis. Mantle length is
    // therefore Y; X/Z are the circular cross-section.
    this.mantle.scale.y = dampScalar(this.mantle.scale.y, this.mantleRest.scale.y * axialScale, mantleResponse, deltaTime);
    this.mantle.scale.z = dampScalar(this.mantle.scale.z, this.mantleRest.scale.z * radialScale, mantleResponse, deltaTime);

    // The funnel is the nozzle that makes the jet directional. Opening it in
    // sync with expulsion is small geometrically but crucial perceptually: the
    // viewer can see where the water pulse comes from.
    if (this.siphon && this.siphonRest) {
      const siphonPulse = jetActive ? cycle.jet : 0;
      const siphonScale = 1 + siphonPulse * 0.105;
      this.siphon.scale.x = dampScalar(this.siphon.scale.x, this.siphonRest.scale.x * siphonScale, 12, deltaTime);
      this.siphon.scale.y = dampScalar(this.siphon.scale.y, this.siphonRest.scale.y * (1 + siphonPulse * 0.075), 12, deltaTime);
      this.siphon.scale.z = dampScalar(this.siphon.scale.z, this.siphonRest.scale.z * siphonScale, 12, deltaTime);
    }
    if (this.siphonOpening && this.siphonOpeningRest) {
      const aperture = 1 + (jetActive ? cycle.jet * 0.24 : 0);
      this.siphonOpening.scale.x = dampScalar(this.siphonOpening.scale.x, this.siphonOpeningRest.scale.x * aperture, 15, deltaTime);
      this.siphonOpening.scale.y = dampScalar(this.siphonOpening.scale.y, this.siphonOpeningRest.scale.y * aperture, 15, deltaTime);
      this.siphonOpening.scale.z = dampScalar(this.siphonOpening.scale.z, this.siphonOpeningRest.scale.z * aperture, 15, deltaTime);
    }

    const activeTasks = this.recruitment.tasks.filter((task) => task.active);
    const explorePush = activeTasks.length
      ? activeTasks.reduce((sum, task) => sum + sampleArmTask(task, 0.30).push, 0) / activeTasks.length
      : 0;
    const bodyPitch = mode === 'swim'
      ? jetting ? -0.040 * squeezeStrength - speed * 0.018 : -armCycle.power * 0.014
      : explorePush * -0.012;
    const externalAttitude = state.externalAttitude === true;
    const bodyYaw = mode === 'explore'
      ? Math.sin(this.motionTime * 0.23) * 0.018
      : externalAttitude ? 0 : steer * speed * -0.050;
    const bodyRoll = mode === 'rest'
      ? Math.sin(this.motionTime * 0.19 + 0.4) * 0.006
      : externalAttitude ? 0 : steer * speed * -0.040;
    this.applyDelta(this.body, this.bodyRest, {
      x: bodyPitch,
      y: bodyYaw,
      z: bodyRoll,
      stretch: 1,
    }, 3.2, deltaTime);

    this.model.updateMatrixWorld(true);
    this.body.getWorldQuaternion(this.bodyWorldQuaternion);
    if (mode === 'swim' && !this.swimModeActive) {
      this.syncSwimDirectionDynamics();
      this.swimModeActive = true;
    } else if (mode !== 'swim') {
      this.swimModeActive = false;
    }
    const trailingSign = (state.travelSign ?? 1) < 0 ? -1 : 1;
    const guideName = jetting
      ? trailingSign > 0 ? 'jetForward' : 'jetReverse'
      : trailingSign > 0 ? 'scullForward' : 'scullReverse';

    for (let armIndex = 0; armIndex < ARM_COUNT; armIndex += 1) {
      for (let boneIndex = 0; boneIndex < BONES_PER_ARM; boneIndex += 1) {
        if (mode === 'swim') {
          const guide = this.swimGuides[armIndex];
          const s = boneIndex / (BONES_PER_ARM - 1);
          const jointCycle = sampleArmSwimJoint(armPhase, s);
          // Website reversals can prescribe a single bounded gather amount.
          // This lets the crown flare for braking, streamline through the
          // fastest part of the turn, then reopen before the next intake. The
          // same second-order joint dynamics still supply all continuity and
          // distal follow-through; this is a target, never a pose snap.
          const directionAmount = Number.isFinite(state.bundleAmount)
            ? clamp01(state.bundleAmount)
            : jetting ? jetBundle : jointCycle.closure;
          if (jetting && directionAmount > 0.995) {
            this.targetBodyDirection.copy(guide[guideName][boneIndex]);
          } else {
            const openDirection = guide.open[boneIndex];
            const closedDirection = guide[guideName][boneIndex];
            const directionAngle = openDirection.angleTo(closedDirection);
            const opposition = THREE.MathUtils.clamp(
              (-trailingSign * openDirection.y + 0.05) / 1.05,
              0,
              1,
            );
            const fold = Math.sin(Math.PI * directionAmount)
              * Math.sin(directionAngle * 0.5) ** 2
              * (0.65 + opposition * 0.55);
            // A normalized vector blend plus an explicit under-body pole is
            // stable even when open and trailing directions are antiparallel.
            // It also makes the travelling close wave bend like an arm instead
            // of flipping through an arbitrary shortest-quaternion axis.
            this.targetBodyDirection.copy(openDirection)
              .lerp(closedDirection, directionAmount)
              .addScaledVector(LONG_AXIS, -fold)
              .normalize();
          }

          const rootFreedom = smootherstep(0.08, 0.34, s);
          // Distributed water load bends distal joints more than the attached
          // crown. This is the passive arm lag visible during a real turn.
          const turnLoad = THREE.MathUtils.clamp(state.turnLoad ?? steer, -1, 1);
          this.targetBodyDirection.x -= turnLoad * rootFreedom * s
            * (jetting ? 0.135 : 0.085);
          this.targetBodyDirection.z -= (state.rollLoad ?? 0) * rootFreedom * s * 0.055;
          this.targetBodyDirection.normalize();

          const stretch = jetting
            ? 1 + rootFreedom * cycle.glide * speed * 0.018
            : 1 + rootFreedom * jointCycle.elongation * 0.034;
          const recoveryRoll = jetting
            ? 0
            : (armIndex % 2 === 0 ? 1 : -1)
              * rootFreedom
              * Math.sin(Math.PI * s)
              * jointCycle.recovery
              * 0.115;
          const directionDynamics = this.swimDirectionDynamics[armIndex][boneIndex];
          const inertia = s ** 0.82;
          const naturalFrequency = THREE.MathUtils.lerp(16.0, 5.2, inertia);
          const dampingRatio = THREE.MathUtils.lerp(1.04, 1.20, inertia);
          const maxAngularSpeed = THREE.MathUtils.lerp(5.1, 2.15, inertia);
          const resolvedDirection = stepBodyDirectionDynamics(
            directionDynamics,
            this.targetBodyDirection,
            deltaTime,
            naturalFrequency,
            dampingRatio,
            maxAngularSpeed,
          );
          this.applyResolvedDirection(
            this.arms[armIndex][boneIndex],
            this.armRest[armIndex][boneIndex],
            guide.restLocalDirections[boneIndex],
            resolvedDirection,
            deltaTime,
            stretch,
            recoveryRoll,
          );
          continue;
        }

        const target = sampleArmCurvature({
          mode,
          armIndex,
          boneIndex,
          time: this.motionTime,
          strokePhase: armPhase,
          speed,
          steer,
          thrust: state.thrust ?? 0,
          task: this.recruitment.tasks[armIndex],
        });
        const response = 7.0 - boneIndex * 0.43;
        this.applyDelta(
          this.arms[armIndex][boneIndex],
          this.armRest[armIndex][boneIndex],
          target,
          response,
          deltaTime,
        );
      }
    }

    this.telemetry = {
      jetting,
      jetActive,
      propulsionStyle,
      jet: cycle.jet,
      squeeze: cycle.squeeze,
      glide: cycle.glide,
      armClosure: armCycle.closure,
      armPower: armCycle.power,
      armRecovery: armCycle.recovery,
    };
  }

  reset() {
    this.motionTime = 0;
    this.swimModeActive = false;
    this.recruitment.enter('rest');
    this.body.position.copy(this.bodyRest.position);
    this.body.quaternion.copy(this.bodyRest.quaternion);
    this.body.scale.copy(this.bodyRest.scale);
    this.mantle.position.copy(this.mantleRest.position);
    this.mantle.quaternion.copy(this.mantleRest.quaternion);
    this.mantle.scale.copy(this.mantleRest.scale);
    if (this.siphon && this.siphonRest) {
      this.siphon.position.copy(this.siphonRest.position);
      this.siphon.quaternion.copy(this.siphonRest.quaternion);
      this.siphon.scale.copy(this.siphonRest.scale);
    }
    if (this.siphonOpening && this.siphonOpeningRest) {
      this.siphonOpening.position.copy(this.siphonOpeningRest.position);
      this.siphonOpening.quaternion.copy(this.siphonOpeningRest.quaternion);
      this.siphonOpening.scale.copy(this.siphonOpeningRest.scale);
    }
    this.arms.forEach((arm, armIndex) => {
      arm.forEach((bone, boneIndex) => {
        const rest = this.armRest[armIndex][boneIndex];
        bone.position.copy(rest.position);
        bone.quaternion.copy(rest.quaternion);
        bone.scale.copy(rest.scale);
        const dynamics = this.swimDirectionDynamics[armIndex][boneIndex];
        dynamics.direction.copy(this.swimGuides[armIndex].restDirections[boneIndex]);
        dynamics.angularVelocity.set(0, 0, 0);
      });
    });
  }
}
