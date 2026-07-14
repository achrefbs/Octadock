import { sampleArmSwimCycle, sampleJetCycle } from './HydrostatMotion.js?v=20';
import { ScrollSwimController } from './ScrollSwimController.js?v=21';

const TAU = Math.PI * 2;
const EPSILON = 1e-7;

export const ROAM_BEHAVIORS = Object.freeze({
  IDLE: 'idle',
  WANDER: 'wander',
  MEET: 'meet',
  INSPECT: 'inspect',
  FLEE: 'flee',
  RECOVER: 'recover',
});

function clamp(value, min = 0, max = 1) {
  return Math.min(max, Math.max(min, value));
}

function smootherstep(edge0, edge1, value) {
  const t = clamp((value - edge0) / Math.max(EPSILON, edge1 - edge0));
  return t * t * t * (t * (t * 6 - 15) + 10);
}

function fract(value) {
  return value - Math.floor(value);
}

function copyPoint(point = {}) {
  return {
    x: Number.isFinite(point.x) ? point.x : 0,
    y: Number.isFinite(point.y) ? point.y : 0,
  };
}

function normalize(vector, fallback = { x: 0, y: -1 }) {
  const length = Math.hypot(vector.x, vector.y);
  if (length < EPSILON) return copyPoint(fallback);
  return { x: vector.x / length, y: vector.y / length };
}

function dot(a, b) {
  return a.x * b.x + a.y * b.y;
}

function cross(a, b) {
  return a.x * b.y - a.y * b.x;
}

function finitePoint(point) {
  return Number.isFinite(point.x) && Number.isFinite(point.y);
}

/** Distance in vertical-NDC units, so X is corrected for viewport aspect. */
export function screenDistance(a, b, aspect = 1) {
  return Math.hypot((a.x - b.x) * Math.max(0.25, aspect), a.y - b.y);
}

/** Convert an NDC displacement into a unit direction with screen-correct slope. */
export function screenDirection(dx, dy, aspect = 1, fallback = { x: 0, y: -1 }) {
  const safeAspect = Math.max(0.25, aspect);
  const screenLength = Math.hypot(dx * safeAspect, dy);
  if (screenLength < EPSILON) return normalize(fallback);
  return normalize({ x: dx / screenLength, y: dy / screenLength }, fallback);
}

class SeededRandom {
  constructor(seed) {
    this.state = (Number(seed) >>> 0) || 0x6d2b79f5;
  }

  next() {
    let value = this.state;
    value ^= value << 13;
    value ^= value >>> 17;
    value ^= value << 5;
    this.state = value >>> 0;
    return this.state / 0x100000000;
  }

  range(min, max) {
    return min + (max - min) * this.next();
  }
}

const DEFAULT_SPECS = [
  {
    id: 'adult',
    position: { x: -0.46, y: 0.18 },
    heading: { x: 0.88, y: -0.32 },
    apparentSpan: 0.44,
    opacity: 0.86,
    collisionRadius: 0.21,
    phaseOffset: 0.13,
  },
  {
    id: 'juvenile',
    position: { x: 0.50, y: -0.24 },
    heading: { x: -0.78, y: 0.45 },
    apparentSpan: 0.35,
    opacity: 0.68,
    collisionRadius: 0.18,
    phaseOffset: 0.61,
  },
];

const ZERO_JET = Object.freeze({
  phase: 0,
  preload: 0,
  squeeze: 0,
  jet: 0,
  glide: 0,
  intake: 0,
});

export class RoamingOctopusSystem {
  constructor({
    seed = 0x0c7ad0c,
    aspect = 16 / 9,
    initialAgents = DEFAULT_SPECS,
    bounds = {},
    meetingDelayRange = [8.0, 13.0],
  } = {}) {
    if (!Array.isArray(initialAgents) || initialAgents.length !== 2) {
      throw new TypeError('RoamingOctopusSystem requires exactly two agents.');
    }

    this.random = new SeededRandom(seed);
    this.aspect = Math.max(0.25, Number(aspect) || 1);
    this.bounds = {
      x: clamp(bounds.x ?? 0.92, 0.55, 1.20),
      y: clamp(bounds.y ?? 0.82, 0.48, 1.05),
      softX: clamp(bounds.softX ?? 0.66, 0.35, 0.95),
      softY: clamp(bounds.softY ?? 0.57, 0.30, 0.90),
    };
    this.meetingDelayRange = [
      Math.max(0, meetingDelayRange[0] ?? 8.0),
      Math.max(meetingDelayRange[0] ?? 8.0, meetingDelayRange[1] ?? 13.0),
    ];
    this.time = 0;
    this.fixedSteps = 0;
    this.meetingEpisode = 0;
    this.meetingActive = false;
    this.nextMeetingAt = this.random.range(...this.meetingDelayRange);
    this.transitionLog = [];
    this.lastInteraction = null;
    this.minimumDistance = Infinity;
    this.pointer = {
      x: 0,
      y: 0,
      active: false,
      pressed: false,
      eventId: 0,
      pointerType: 'mouse',
    };

    this.agents = initialAgents.map((source, index) => this.createAgent({
      ...DEFAULT_SPECS[index],
      ...source,
      position: { ...DEFAULT_SPECS[index].position, ...(source.position || {}) },
      heading: { ...DEFAULT_SPECS[index].heading, ...(source.heading || {}) },
    }, index));

    this.agents.forEach((agent) => {
      this.chooseWanderTarget(agent);
      agent.motionState = this.makeMotionState(agent);
    });
    this.resolveHardBounds();
  }

  createAgent(spec, index) {
    const heading = normalize(spec.heading);
    const controller = new ScrollSwimController({
      maxSpeed: 0.72,
      maxOffsetX: 99,
      maxOffsetY: 99,
      // Roaming is an unhurried whole-body redirect. A threat opts into the
      // much faster `turning` dynamics without ever snapping the root.
      cruiseNaturalFrequency: 1.55,
      cruiseDampingRatio: 1.10,
      maximumCruiseAcceleration: 2.5,
      maximumCruiseTurnRate: 0.92,
      turnNaturalFrequency: 11.0,
      turnDampingRatio: 1.02,
      maximumTurnAcceleration: 72,
      maximumTurnRate: 8.0,
    });
    controller.reset({ headingX: heading.x, headingY: heading.y });
    controller.offset.x = spec.position.x;
    controller.offset.y = spec.position.y;

    return {
      id: String(spec.id || `octopus-${index + 1}`),
      index,
      controller,
      behavior: ROAM_BEHAVIORS.WANDER,
      previousBehavior: null,
      stateAge: 0,
      stateDuration: this.random.range(4.8, 7.4),
      target: copyPoint(spec.position),
      targetHeading: copyPoint(heading),
      idleHeading: copyPoint(heading),
      partnerId: null,
      apparentSpan: clamp(spec.apparentSpan ?? 0.40, 0.24, 0.62),
      opacity: clamp(spec.opacity ?? 0.75, 0.30, 1),
      collisionRadius: clamp(spec.collisionRadius ?? 0.16, 0.10, 0.26),
      reactionRadius: clamp((spec.apparentSpan ?? 0.40) * 0.62 + 0.055, 0.22, 0.43),
      armPhase: fract(spec.phaseOffset ?? index * 0.47),
      armPeriod: this.random.range(3.3, 4.4),
      drive: 0,
      armCycle: sampleArmSwimCycle(spec.phaseOffset ?? index * 0.47),
      jetCycle: ZERO_JET,
      fleeStage: 'none',
      fleeDirection: copyPoint(heading),
      fleeStrokeTime: 0,
      fleeEpisode: 0,
      fleeStrokeIndex: 0,
      turnSide: index === 0 ? 1 : -1,
      touchCooldown: 0,
      pointerLatched: false,
      lastReactEvent: -1,
      motionState: null,
    };
  }

  setAspect(aspect) {
    if (Number.isFinite(aspect) && aspect > 0.1) this.aspect = aspect;
  }

  setPointer(x, y, { pressed = false, pointerType = 'mouse', eventId } = {}) {
    if (!Number.isFinite(x) || !Number.isFinite(y)) return;
    this.pointer.x = clamp(x, -1.35, 1.35);
    this.pointer.y = clamp(y, -1.35, 1.35);
    this.pointer.active = true;
    this.pointer.pressed = this.pointer.pressed || Boolean(pressed);
    this.pointer.pointerType = pointerType || 'mouse';
    this.pointer.eventId = Number.isFinite(eventId)
      ? Math.max(this.pointer.eventId + 1, eventId)
      : this.pointer.eventId + 1;
  }

  touch(point, options = {}) {
    this.setPointer(point.x, point.y, { ...options, pressed: true });
  }

  clearPointer() {
    this.pointer.active = false;
    this.pointer.pressed = false;
    this.agents.forEach((agent) => { agent.pointerLatched = false; });
  }

  scheduleNextMeeting() {
    this.nextMeetingAt = this.time + this.random.range(...this.meetingDelayRange);
  }

  chooseWanderTarget(agent) {
    const other = this.agents?.find((candidate) => candidate !== agent);
    let candidate = copyPoint(agent.controller.offset);
    for (let attempt = 0; attempt < 12; attempt += 1) {
      const radiusX = this.bounds.x - agent.collisionRadius / this.aspect - 0.08;
      const radiusY = this.bounds.y - agent.collisionRadius - 0.07;
      candidate = {
        x: this.random.range(-radiusX, radiusX),
        y: this.random.range(-radiusY, radiusY),
      };
      if (screenDistance(candidate, agent.controller.offset, this.aspect) < 0.40) continue;
      if (other && screenDistance(candidate, other.controller.offset, this.aspect) < 0.38) continue;
      break;
    }
    agent.target = candidate;
    agent.armPeriod = this.random.range(3.3, 4.5);
    return candidate;
  }

  enterBehavior(agent, behavior, duration, reason = '') {
    if (agent.behavior === behavior && behavior !== ROAM_BEHAVIORS.FLEE) return;
    const previous = agent.behavior;
    agent.previousBehavior = previous;
    agent.behavior = behavior;
    agent.stateAge = 0;
    agent.stateDuration = Math.max(0, duration || 0);
    this.transitionLog.push({
      time: this.time,
      agentId: agent.id,
      from: previous,
      to: behavior,
      reason,
    });
    if (this.transitionLog.length > 96) this.transitionLog.shift();

    if (behavior === ROAM_BEHAVIORS.IDLE) {
      agent.fleeStage = 'none';
      const angle = Math.atan2(agent.controller.heading.y, agent.controller.heading.x)
        + this.random.range(-0.42, 0.42);
      agent.idleHeading = { x: Math.cos(angle), y: Math.sin(angle) };
      agent.partnerId = null;
    } else if (behavior === ROAM_BEHAVIORS.WANDER) {
      agent.fleeStage = 'none';
      agent.partnerId = null;
      this.chooseWanderTarget(agent);
    } else if (behavior === ROAM_BEHAVIORS.RECOVER) {
      agent.partnerId = null;
      agent.fleeStage = 'glide';
    }
  }

  canMeet() {
    return this.agents.every((agent) => (
      agent.behavior === ROAM_BEHAVIORS.WANDER
      || agent.behavior === ROAM_BEHAVIORS.IDLE
    ) && agent.touchCooldown <= 0);
  }

  forceMeeting() {
    if (!this.canMeet()) return false;
    this.meetingEpisode += 1;
    this.meetingActive = true;
    const [first, second] = this.agents;
    this.enterBehavior(first, ROAM_BEHAVIORS.MEET, 8.0, 'social-rendezvous');
    this.enterBehavior(second, ROAM_BEHAVIORS.MEET, 8.0, 'social-rendezvous');
    first.partnerId = second.id;
    second.partnerId = first.id;
    this.nextMeetingAt = Infinity;
    return true;
  }

  cancelMeeting(fleeingAgent = null) {
    if (!this.meetingActive) return;
    this.meetingActive = false;
    this.agents.forEach((agent) => {
      if (agent === fleeingAgent) return;
      if (agent.behavior === ROAM_BEHAVIORS.MEET
        || agent.behavior === ROAM_BEHAVIORS.INSPECT) {
        this.enterBehavior(agent, ROAM_BEHAVIORS.WANDER, this.random.range(4.4, 7.2), 'partner-startled');
      }
    });
    this.scheduleNextMeeting();
  }

  beginInspect() {
    if (!this.meetingActive) return;
    const duration = this.random.range(2.2, 3.4);
    this.agents.forEach((agent) => {
      this.enterBehavior(agent, ROAM_BEHAVIORS.INSPECT, duration, 'close-contact');
    });
  }

  finishInspect() {
    if (!this.meetingActive) return;
    this.meetingActive = false;
    this.agents.forEach((agent) => {
      this.enterBehavior(agent, ROAM_BEHAVIORS.WANDER, this.random.range(4.8, 7.5), 'inspection-complete');
    });
    this.scheduleNextMeeting();
  }

  beginFlee(agent, pointerDistance) {
    const position = agent.controller.offset;
    let away = screenDirection(
      position.x - this.pointer.x,
      position.y - this.pointer.y,
      this.aspect,
      { x: -agent.controller.heading.x, y: -agent.controller.heading.y },
    );
    const tangent = { x: -away.y, y: away.x };
    away = normalize({
      x: away.x + tangent.x * this.random.range(-0.12, 0.12),
      y: away.y + tangent.y * this.random.range(-0.12, 0.12),
    }, away);

    this.cancelMeeting(agent);
    agent.previousBehavior = agent.behavior;
    this.enterBehavior(agent, ROAM_BEHAVIORS.FLEE, 2.2, 'pointer-contact');
    agent.fleeDirection = away;
    agent.targetHeading = copyPoint(away);
    agent.fleeStage = 'turn';
    agent.fleeStrokeTime = 0;
    agent.fleeStrokeIndex = 0;
    agent.fleeEpisode += 1;
    agent.touchCooldown = 2.0;
    agent.turnSide = Math.sign(cross(agent.controller.heading, away))
      || (this.random.next() < 0.5 ? -1 : 1);
    agent.lastReactEvent = this.pointer.eventId;
    this.lastInteraction = {
      time: this.time,
      agentId: agent.id,
      eventId: this.pointer.eventId,
      pointerType: this.pointer.pointerType,
      distance: pointerDistance,
      fleeEpisode: agent.fleeEpisode,
    };
  }

  processPointer() {
    if (!this.pointer.active) return;
    this.agents.forEach((agent) => {
      const distance = screenDistance(agent.controller.offset, this.pointer, this.aspect);
      const releaseRadius = agent.reactionRadius * 1.45;
      const inside = distance <= agent.reactionRadius;
      const pressedInside = this.pointer.pressed && distance <= agent.reactionRadius * 1.32;
      if (!inside && distance > releaseRadius) agent.pointerLatched = false;

      const newContact = inside && !agent.pointerLatched;
      const newPress = pressedInside && agent.lastReactEvent !== this.pointer.eventId;
      if ((newContact || newPress)
        && agent.touchCooldown <= 0
        && agent.behavior !== ROAM_BEHAVIORS.FLEE) {
        agent.pointerLatched = true;
        this.beginFlee(agent, distance);
      } else if (inside) {
        agent.pointerLatched = true;
      }
    });
    this.pointer.pressed = false;
  }

  boundaryAdjustedHeading(agent, desired, weight = 1) {
    const position = agent.controller.offset;
    const xLimit = Math.max(this.bounds.softX, this.bounds.x - agent.collisionRadius / this.aspect);
    const yLimit = Math.max(this.bounds.softY, this.bounds.y - agent.collisionRadius);
    const xPressure = smootherstep(this.bounds.softX, xLimit, Math.abs(position.x));
    const yPressure = smootherstep(this.bounds.softY, yLimit, Math.abs(position.y));
    return normalize({
      x: desired.x - Math.sign(position.x) * xPressure * 1.75 * weight,
      y: desired.y - Math.sign(position.y) * yPressure * 1.75 * weight,
    }, desired);
  }

  desiredHeading(agent, other) {
    const position = agent.controller.offset;
    let desired = agent.targetHeading;
    if (agent.behavior === ROAM_BEHAVIORS.WANDER) {
      desired = screenDirection(
        agent.target.x - position.x,
        agent.target.y - position.y,
        this.aspect,
        agent.controller.heading,
      );
    } else if (agent.behavior === ROAM_BEHAVIORS.IDLE) {
      desired = agent.idleHeading;
    } else if (agent.behavior === ROAM_BEHAVIORS.MEET
      || agent.behavior === ROAM_BEHAVIORS.INSPECT) {
      desired = screenDirection(
        other.controller.offset.x - position.x,
        other.controller.offset.y - position.y,
        this.aspect,
        agent.controller.heading,
      );
    } else if (agent.behavior === ROAM_BEHAVIORS.FLEE) {
      desired = agent.fleeDirection;
    } else if (agent.behavior === ROAM_BEHAVIORS.RECOVER) {
      desired = agent.controller.heading;
    }
    const boundaryWeight = agent.behavior === ROAM_BEHAVIORS.FLEE ? 1.55 : 1;
    agent.targetHeading = this.boundaryAdjustedHeading(agent, desired, boundaryWeight);
    return agent.targetHeading;
  }

  advanceBehavior(agent, other, deltaTime) {
    agent.stateAge += deltaTime;
    agent.touchCooldown = Math.max(0, agent.touchCooldown - deltaTime);
    agent.armPhase = fract(agent.armPhase + deltaTime / agent.armPeriod);

    if (agent.behavior === ROAM_BEHAVIORS.WANDER) {
      const targetDistance = screenDistance(agent.controller.offset, agent.target, this.aspect);
      if (targetDistance < 0.15 || agent.stateAge >= agent.stateDuration) {
        this.enterBehavior(agent, ROAM_BEHAVIORS.IDLE, this.random.range(1.4, 3.4), 'wander-pause');
      }
    } else if (agent.behavior === ROAM_BEHAVIORS.IDLE
      && agent.stateAge >= agent.stateDuration) {
      this.enterBehavior(agent, ROAM_BEHAVIORS.WANDER, this.random.range(4.8, 7.8), 'idle-complete');
    } else if (agent.behavior === ROAM_BEHAVIORS.MEET
      && agent.stateAge >= agent.stateDuration) {
      this.cancelMeeting();
    } else if (agent.behavior === ROAM_BEHAVIORS.INSPECT
      && agent.stateAge >= agent.stateDuration) {
      this.finishInspect();
    } else if (agent.behavior === ROAM_BEHAVIORS.RECOVER
      && agent.stateAge >= agent.stateDuration) {
      this.enterBehavior(agent, ROAM_BEHAVIORS.WANDER, this.random.range(4.8, 7.4), 'glide-complete');
    }

    const heading = this.desiredHeading(agent, other);
    const alignment = dot(agent.controller.heading, heading);
    agent.armCycle = sampleArmSwimCycle(agent.armPhase);
    agent.jetCycle = ZERO_JET;
    agent.drive = 0;
    let turning = false;
    let braking = false;
    let jet = 0;
    let squeeze = 0;
    let glide = 0;
    let intake = 0;
    let demand = 0;
    let thrustMultiplier = 1;

    if (agent.behavior === ROAM_BEHAVIORS.WANDER) {
      const alignmentGain = smootherstep(0.10, 0.88, alignment);
      agent.drive = agent.armCycle.power * 0.14 * alignmentGain;
      jet = agent.drive;
      glide = agent.armCycle.recovery;
      intake = agent.armCycle.recovery * 0.08;
      demand = 0.18;
    } else if (agent.behavior === ROAM_BEHAVIORS.MEET) {
      const separation = screenDistance(agent.controller.offset, other.controller.offset, this.aspect);
      const stopDistance = agent.collisionRadius + other.collisionRadius + 0.045;
      const approachGain = smootherstep(stopDistance, stopDistance + 0.12, separation);
      const alignmentGain = smootherstep(0.18, 0.88, alignment);
      agent.drive = agent.armCycle.power * 0.17 * approachGain * alignmentGain;
      jet = agent.drive;
      glide = agent.armCycle.recovery;
      intake = agent.armCycle.recovery * 0.10;
      demand = 0.17;
    } else if (agent.behavior === ROAM_BEHAVIORS.IDLE) {
      braking = agent.stateAge < 0.72;
      intake = braking ? 0.48 : 0.12;
    } else if (agent.behavior === ROAM_BEHAVIORS.INSPECT) {
      braking = true;
      intake = 0.34;
    } else if (agent.behavior === ROAM_BEHAVIORS.FLEE) {
      if (agent.fleeStage === 'turn') {
        turning = true;
        braking = true;
        intake = 0.42;
        const alignedEnough = alignment > 0.89 && agent.stateAge > 0.07;
        if (alignedEnough || agent.stateAge > 0.52) {
          agent.fleeStage = 'jet';
          agent.fleeStrokeTime = 0;
          turning = false;
          braking = false;
          intake = 0;
        }
      }
      if (agent.fleeStage === 'jet') {
        agent.fleeStrokeTime += deltaTime;
        const fleePeriod = 0.68;
        const normalizedStroke = agent.fleeStrokeTime / fleePeriod;
        agent.fleeStrokeIndex = Math.min(2, Math.floor(normalizedStroke) + 1);
        agent.jetCycle = sampleJetCycle(normalizedStroke);
        jet = agent.jetCycle.jet;
        squeeze = agent.jetCycle.squeeze;
        glide = agent.jetCycle.glide;
        intake = agent.jetCycle.intake;
        demand = 1;
        thrustMultiplier = 1.85;
        if (agent.fleeStrokeTime >= fleePeriod * 2) {
          this.enterBehavior(agent, ROAM_BEHAVIORS.RECOVER, 1.15, 'escape-glide');
        }
      }
    } else if (agent.behavior === ROAM_BEHAVIORS.RECOVER) {
      const recovery = clamp(agent.stateAge / Math.max(EPSILON, agent.stateDuration));
      glide = 1 - smootherstep(0.55, 1, recovery);
      intake = smootherstep(0.10, 0.78, recovery) * (1 - smootherstep(0.82, 1, recovery));
      braking = false;
    }

    agent.controller.update(deltaTime, {
      headingX: heading.x,
      headingY: heading.y,
      turnSide: agent.turnSide,
      turning,
      braking,
      demand,
      jet,
      squeeze,
      glide,
      intake,
      thrustMultiplier,
      active: agent.behavior !== ROAM_BEHAVIORS.IDLE,
    });
    agent.motionState = this.makeMotionState(agent);
  }

  resolveSeparation(deltaTime) {
    const [first, second] = this.agents;
    const dx = second.controller.offset.x - first.controller.offset.x;
    const dy = second.controller.offset.y - first.controller.offset.y;
    const distance = Math.max(EPSILON, Math.hypot(dx * this.aspect, dy));
    const minimum = first.collisionRadius + second.collisionRadius;
    this.minimumDistance = distance;
    if (distance >= minimum) return;

    const screenNormalX = dx * this.aspect / distance;
    const screenNormalY = dy / distance;
    const penetration = minimum - distance;
    const correctionX = screenNormalX / this.aspect * penetration * 0.5;
    const correctionY = screenNormalY * penetration * 0.5;
    first.controller.offset.x -= correctionX;
    first.controller.offset.y -= correctionY;
    second.controller.offset.x += correctionX;
    second.controller.offset.y += correctionY;

    // Remove only closing normal velocity. This is an inelastic soft-body
    // contact, not a hidden spring that makes the animals bounce apart.
    const relativeScreenX = (second.controller.velocity.x - first.controller.velocity.x)
      * this.aspect;
    const relativeScreenY = second.controller.velocity.y - first.controller.velocity.y;
    const closingSpeed = relativeScreenX * screenNormalX + relativeScreenY * screenNormalY;
    if (closingSpeed < 0) {
      const velocityCorrectionX = screenNormalX / this.aspect * closingSpeed * 0.5;
      const velocityCorrectionY = screenNormalY * closingSpeed * 0.5;
      first.controller.velocity.x += velocityCorrectionX;
      first.controller.velocity.y += velocityCorrectionY;
      second.controller.velocity.x -= velocityCorrectionX;
      second.controller.velocity.y -= velocityCorrectionY;
    }
  }

  resolveHardBounds() {
    this.agents.forEach((agent) => {
      const xLimit = Math.max(0.2, this.bounds.x - agent.collisionRadius / this.aspect);
      const yLimit = Math.max(0.2, this.bounds.y - agent.collisionRadius);
      if (agent.controller.offset.x < -xLimit || agent.controller.offset.x > xLimit) {
        agent.controller.offset.x = clamp(agent.controller.offset.x, -xLimit, xLimit);
        if (agent.controller.velocity.x * agent.controller.offset.x > 0) {
          agent.controller.velocity.x = 0;
        }
      }
      if (agent.controller.offset.y < -yLimit || agent.controller.offset.y > yLimit) {
        agent.controller.offset.y = clamp(agent.controller.offset.y, -yLimit, yLimit);
        if (agent.controller.velocity.y * agent.controller.offset.y > 0) {
          agent.controller.velocity.y = 0;
        }
      }
    });
  }

  makeMotionState(agent) {
    const speed = Math.hypot(agent.controller.velocity.x, agent.controller.velocity.y);
    const navigationSpeed = clamp(speed / Math.max(EPSILON, agent.controller.maxSpeed));
    const turnLoad = clamp(
      agent.controller.angularVelocity / agent.controller.maximumTurnRate,
      -1,
      1,
    ) * (agent.behavior === ROAM_BEHAVIORS.FLEE ? 0.42 : 0.24);
    const resting = agent.behavior === ROAM_BEHAVIORS.IDLE;
    const inspecting = agent.behavior === ROAM_BEHAVIORS.INSPECT;
    const fleeing = agent.behavior === ROAM_BEHAVIORS.FLEE;
    const recovering = agent.behavior === ROAM_BEHAVIORS.RECOVER;

    if (fleeing) {
      const turning = agent.fleeStage === 'turn';
      const turnGather = 0.12 + smootherstep(0, 0.45, agent.stateAge) * 0.38;
      const cycle = turning ? { ...ZERO_JET, phase: 0.055, preload: 0.7 } : agent.jetCycle;
      return {
        mode: 'swim',
        navigationSpeed,
        thrust: cycle.jet,
        steer: 0,
        turnLoad,
        rollLoad: turnLoad * 0.18,
        strokePhase: cycle.phase,
        jetPhase: cycle.phase,
        armPhase: cycle.phase,
        // Phase .055 is a real hyper-inflation preload. It deforms the mantle
        // while the controller still blocks thrust during the redirect.
        jetActive: true,
        propulsionStyle: 'jet',
        bundleAmount: turning ? turnGather : undefined,
        travelSign: 1,
        jet: cycle.jet,
        squeeze: cycle.squeeze,
        glide: cycle.glide,
        intake: cycle.intake,
        motionScale: 1.22,
        externalAttitude: true,
      };
    }

    if (recovering) {
      const recovery = clamp(agent.stateAge / Math.max(EPSILON, agent.stateDuration));
      return {
        mode: 'swim',
        navigationSpeed,
        thrust: 0,
        steer: 0,
        turnLoad,
        rollLoad: 0,
        strokePhase: 0.76 + recovery * 0.20,
        jetPhase: 0.76 + recovery * 0.20,
        armPhase: 0.76 + recovery * 0.20,
        // The recovery phase contains no expulsion, but HydrostatMotion needs
        // the active intake flag to show the mantle visibly refilling.
        jetActive: recovery < 0.98,
        propulsionStyle: 'jet',
        bundleAmount: (1 - smootherstep(0.05, 0.92, recovery)) * 0.42,
        travelSign: 1,
        jet: 0,
        squeeze: 0,
        glide: 1 - recovery,
        intake: smootherstep(0.15, 0.80, recovery),
        motionScale: 0.92,
        externalAttitude: true,
      };
    }

    return {
      mode: resting ? 'rest' : inspecting ? 'explore' : 'swim',
      navigationSpeed,
      thrust: agent.drive,
      steer: 0,
      turnLoad,
      rollLoad: turnLoad * 0.08,
      strokePhase: agent.armPhase,
      jetPhase: 0.995,
      armPhase: agent.armPhase,
      jetActive: false,
      propulsionStyle: 'arm',
      travelSign: 1,
      jet: 0,
      squeeze: 0,
      glide: agent.armCycle?.recovery || 0,
      intake: 0,
      motionScale: resting ? 0.64 : inspecting ? 0.72 : 0.82,
      externalAttitude: true,
    };
  }

  update(deltaTime) {
    const dt = clamp(Number.isFinite(deltaTime) ? deltaTime : 1 / 120, 1 / 1000, 1 / 30);
    this.time += dt;
    this.fixedSteps += 1;

    this.processPointer();
    if (!this.meetingActive && this.time >= this.nextMeetingAt && this.canMeet()) {
      this.forceMeeting();
    }

    const [first, second] = this.agents;
    this.advanceBehavior(first, second, dt);
    this.advanceBehavior(second, first, dt);
    this.resolveSeparation(dt);
    this.resolveHardBounds();

    if (this.meetingActive
      && first.behavior === ROAM_BEHAVIORS.MEET
      && second.behavior === ROAM_BEHAVIORS.MEET) {
      const separation = screenDistance(first.controller.offset, second.controller.offset, this.aspect);
      const relativeSpeed = Math.hypot(
        (first.controller.velocity.x - second.controller.velocity.x) * this.aspect,
        first.controller.velocity.y - second.controller.velocity.y,
      );
      const contactDistance = first.collisionRadius + second.collisionRadius + 0.055;
      if (separation <= contactDistance && relativeSpeed < 0.055) this.beginInspect();
    }

    this.agents.forEach((agent) => {
      agent.motionState = this.makeMotionState(agent);
      if (!finitePoint(agent.controller.offset)
        || !finitePoint(agent.controller.velocity)
        || !finitePoint(agent.controller.heading)) {
        throw new Error(`Non-finite roaming state for ${agent.id}.`);
      }
    });
    return this;
  }

  snapshot() {
    return {
      time: this.time,
      fixedSteps: this.fixedSteps,
      meetingEpisode: this.meetingEpisode,
      meetingActive: this.meetingActive,
      nextMeetingAt: this.nextMeetingAt,
      minimumDistance: this.minimumDistance,
      lastInteraction: this.lastInteraction ? { ...this.lastInteraction } : null,
      agents: this.agents.map((agent) => ({
        id: agent.id,
        behavior: agent.behavior,
        stateAge: agent.stateAge,
        stateDuration: agent.stateDuration,
        position: copyPoint(agent.controller.offset),
        velocity: copyPoint(agent.controller.velocity),
        heading: copyPoint(agent.controller.heading),
        targetHeading: copyPoint(agent.targetHeading),
        target: copyPoint(agent.target),
        speed: Math.hypot(agent.controller.velocity.x, agent.controller.velocity.y),
        angularVelocity: agent.controller.angularVelocity,
        partnerId: agent.partnerId,
        collisionRadius: agent.collisionRadius,
        reactionRadius: agent.reactionRadius,
        fleeStage: agent.fleeStage,
        fleeEpisode: agent.fleeEpisode,
        fleeStrokeIndex: agent.fleeStrokeIndex,
        jet: agent.jetCycle?.jet || 0,
        armPhase: agent.armPhase,
      })),
    };
  }
}
