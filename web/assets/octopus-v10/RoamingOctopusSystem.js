import { sampleArmSwimCycle, sampleJetCycle } from './HydrostatMotion.js?v=21';
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

function wrapAngle(angle) {
  let wrapped = angle;
  while (wrapped > Math.PI) wrapped -= TAU;
  while (wrapped < -Math.PI) wrapped += TAU;
  return wrapped;
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

/** A displacement whose magnitude is measured in aspect-correct screen units. */
function screenDisplacement(direction, distance, aspect = 1) {
  const unit = normalize(direction);
  const screenScale = Math.max(EPSILON, Math.hypot(unit.x * Math.max(0.25, aspect), unit.y));
  return {
    x: unit.x * distance / screenScale,
    y: unit.y * distance / screenScale,
  };
}

function quadraticBezier(start, control, end, t) {
  const inverse = 1 - t;
  return {
    x: inverse * inverse * start.x + 2 * inverse * t * control.x + t * t * end.x,
    y: inverse * inverse * start.y + 2 * inverse * t * control.y + t * t * end.y,
  };
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

export const DEFAULT_CITY_SPECS = Object.freeze([
  {
    id: 'adult',
    position: { x: -0.46, y: 0.18 },
    heading: { x: 0.88, y: -0.32 },
    apparentSpan: 0.44,
    opacity: 0.86,
    collisionRadius: 0.21,
    phaseOffset: 0.13,
    depthLayer: 0.06,
    depthFactor: 0.96,
    motionSeed: 0x74a0d101,
    motionPhase: 3.861,
    motionRate: 0.97,
  },
  {
    id: 'juvenile',
    position: { x: 0.50, y: -0.24 },
    heading: { x: -0.78, y: 0.45 },
    apparentSpan: 0.35,
    opacity: 0.68,
    collisionRadius: 0.18,
    phaseOffset: 0.61,
    depthLayer: -0.04,
    depthFactor: 1.03,
    motionSeed: 0x74a0d102,
    motionPhase: 18.117,
    motionRate: 1.04,
  },
  {
    id: 'drifter',
    position: { x: -0.15, y: 0.66 },
    heading: { x: 0.34, y: -0.94 },
    apparentSpan: 0.31,
    opacity: 0.58,
    collisionRadius: 0.15,
    phaseOffset: 0.34,
    depthLayer: -0.18,
    depthFactor: 1.09,
    motionSeed: 0x74a0d103,
    motionPhase: 10.098,
    motionRate: 0.93,
  },
  {
    id: 'deep-one',
    position: { x: -0.55, y: -0.63 },
    heading: { x: 0.70, y: 0.71 },
    apparentSpan: 0.33,
    opacity: 0.61,
    collisionRadius: 0.16,
    phaseOffset: 0.82,
    depthLayer: -0.10,
    depthFactor: 0.99,
    motionSeed: 0x74a0d104,
    motionPhase: 24.354,
    motionRate: 1.07,
  },
  {
    id: 'high-glider',
    position: { x: 0.60, y: 0.58 },
    heading: { x: -0.91, y: -0.42 },
    apparentSpan: 0.28,
    opacity: 0.52,
    collisionRadius: 0.14,
    phaseOffset: 0.47,
    depthLayer: -0.25,
    depthFactor: 1.14,
    motionSeed: 0x74a0d105,
    motionPhase: 13.959,
    motionRate: 0.95,
  },
  {
    id: 'small-current',
    position: { x: 0.04, y: -0.68 },
    heading: { x: -0.25, y: 0.97 },
    apparentSpan: 0.26,
    opacity: 0.49,
    collisionRadius: 0.13,
    phaseOffset: 0.06,
    depthLayer: -0.31,
    depthFactor: 1.18,
    motionSeed: 0x74a0d106,
    motionPhase: 1.782,
    motionRate: 1.02,
  },
  {
    id: 'edge-scout',
    position: { x: 0.67, y: 0.12 },
    heading: { x: -0.66, y: 0.75 },
    apparentSpan: 0.30,
    opacity: 0.56,
    collisionRadius: 0.15,
    phaseOffset: 0.71,
    depthLayer: -0.14,
    depthFactor: 1.08,
    motionSeed: 0x74a0d107,
    motionPhase: 21.087,
    motionRate: 0.99,
  },
]);

function fallbackSpec(index, count) {
  const phase = fract(index * 0.61803398875 + 0.17);
  const angle = phase * TAU;
  const ring = index % 2 ? 0.62 : 0.48;
  return {
    id: `octopus-${index + 1}`,
    position: {
      x: Math.cos(angle) * ring,
      y: Math.sin(angle) * Math.min(0.68, ring),
    },
    heading: normalize({ x: -Math.sin(angle), y: Math.cos(angle) }),
    apparentSpan: 0.26 + (index % 4) * 0.025,
    opacity: 0.46 + (index % 5) * 0.045,
    collisionRadius: 0.13 + (index % 3) * 0.012,
    phaseOffset: phase,
    depthLayer: -0.08 - (index % Math.max(1, count)) * 0.025,
    depthFactor: clamp(0.96 + (index % 7) * 0.035, 0.94, 1.18),
    motionSeed: (0x74a0d100 + index + 1) >>> 0,
    motionPhase: phase * 29.7,
    motionRate: 0.92 + (index % 9) * 0.02,
  };
}

function defaultSpecs(count) {
  return Array.from({ length: count }, (_, index) => (
    DEFAULT_CITY_SPECS[index] || fallbackSpec(index, count)
  ));
}

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
    initialAgents,
    agentCount = 7,
    bounds = {},
    meetingDelayRange = [8.0, 13.0],
    startlePropagationRadius = 0.74,
  } = {}) {
    if (initialAgents !== undefined && (!Array.isArray(initialAgents) || initialAgents.length < 1)) {
      throw new TypeError('initialAgents must be a non-empty array when supplied.');
    }

    const requestedCount = Math.max(1, Math.min(24, Math.floor(Number(agentCount) || 7)));
    const sourceSpecs = initialAgents || defaultSpecs(requestedCount);

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
    this.activeMeeting = null;
    this.nextMeetingAt = this.random.range(...this.meetingDelayRange);
    this.transitionLog = [];
    this.startleLog = [];
    this.pendingStartles = [];
    this.startlePropagationRadius = clamp(startlePropagationRadius, 0, 1.5);
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

    this.agents = sourceSpecs.map((source, index) => {
      const fallback = DEFAULT_CITY_SPECS[index] || fallbackSpec(index, sourceSpecs.length);
      return this.createAgent({
        ...fallback,
        ...source,
        position: { ...fallback.position, ...(source.position || {}) },
        heading: { ...fallback.heading, ...(source.heading || {}) },
      }, index);
    });

    this.agents.forEach((agent) => {
      this.chooseWanderTarget(agent);
      agent.motionState = this.makeMotionState(agent);
    });
    this.resolveWorldConstraints();
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
      partnerIndex: null,
      apparentSpan: clamp(spec.apparentSpan ?? 0.40, 0.24, 0.62),
      opacity: clamp(spec.opacity ?? 0.75, 0.30, 1),
      depthLayer: clamp(spec.depthLayer ?? -index * 0.04, -0.55, 0.25),
      depthFactor: clamp(spec.depthFactor ?? 1, 0.90, 1.24),
      collisionRadius: clamp(spec.collisionRadius ?? 0.16, 0.10, 0.26),
      reactionRadius: clamp((spec.apparentSpan ?? 0.40) * 0.62 + 0.055, 0.22, 0.43),
      armPhase: fract(spec.phaseOffset ?? index * 0.47),
      armPeriod: this.random.range(3.3, 4.4),
      motionSeed: (Number(spec.motionSeed) >>> 0) || ((0x74a0d100 + index + 1) >>> 0),
      motionPhase: Number.isFinite(spec.motionPhase)
        ? spec.motionPhase
        : fract(spec.phaseOffset ?? index * 0.47) * 29.7,
      motionRate: clamp(spec.motionRate ?? 1, 0.92, 1.08),
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
      lastScheduledEvent: -1,
      startleRemaining: 0,
      startleDirection: copyPoint(heading),
      startleEvent: -1,
      socialCooldown: 0,
      navigationHeading: copyPoint(heading),
      routeStart: copyPoint(spec.position),
      routeControl: copyPoint(spec.position),
      routeLength: 0.5,
      routeOrdinal: 0,
      verticalTargetSign: spec.position.y >= 0 ? -1 : 1,
      continuationRoute: false,
      currentPhase: fract((spec.currentPhase ?? spec.phaseOffset ?? index * 0.47) + 0.23),
      currentVelocity: { x: 0, y: 0 },
      verticalMinimum: spec.position.y,
      verticalMaximum: spec.position.y,
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

  configureRoute(agent, candidate, { continuation = false, curveScale = 1 } = {}) {
    const start = copyPoint(agent.controller.offset);
    const chordX = candidate.x - start.x;
    const chordY = candidate.y - start.y;
    const length = Math.max(0.05, screenDistance(candidate, start, this.aspect));
    const direction = screenDirection(chordX, chordY, this.aspect, agent.controller.heading);
    const perpendicular = { x: -direction.y, y: direction.x };
    const curveSign = this.random.next() < 0.5 ? -1 : 1;
    const curveDistance = Math.min(0.24, length * 0.22) * curveScale * curveSign;
    const curveOffset = screenDisplacement(perpendicular, curveDistance, this.aspect);
    const xLimit = Math.max(0.2, this.bounds.x - agent.collisionRadius / this.aspect - 0.025);
    const yLimit = Math.max(0.2, this.bounds.y - agent.collisionRadius - 0.025);

    agent.routeStart = start;
    agent.routeControl = {
      x: clamp((start.x + candidate.x) * 0.5 + curveOffset.x, -xLimit, xLimit),
      y: clamp((start.y + candidate.y) * 0.5 + curveOffset.y, -yLimit, yLimit),
    };
    agent.routeLength = length;
    agent.target = candidate;
    agent.continuationRoute = continuation;
    agent.stateDuration = clamp(length / 0.046 + this.random.range(2.8, 5.0), 6.2, 24);
    agent.armPeriod = this.random.range(3.3, 4.5) / agent.motionRate;
    return candidate;
  }

  chooseWanderTarget(agent) {
    let candidate = copyPoint(agent.controller.offset);
    const radiusX = Math.max(0.16,
      this.bounds.x - agent.collisionRadius / this.aspect - 0.075);
    const radiusY = Math.max(0.16, this.bounds.y - agent.collisionRadius - 0.055);
    const targetSign = agent.verticalTargetSign || (agent.controller.offset.y >= 0 ? -1 : 1);
    for (let attempt = 0; attempt < 18; attempt += 1) {
      candidate = {
        x: this.random.range(-radiusX, radiusX),
        // Every broad route deliberately crosses the tank. The horizontal
        // sample and Bezier bow keep those crossings from reading as lanes.
        y: targetSign * this.random.range(Math.min(0.48, radiusY * 0.72), radiusY),
      };
      if (screenDistance(candidate, agent.controller.offset, this.aspect) < 0.40) continue;
      if (this.agents?.some((other) => other !== agent
        && screenDistance(candidate, other.controller.offset, this.aspect)
          < agent.collisionRadius + other.collisionRadius + 0.12)) continue;
      break;
    }
    agent.verticalTargetSign = -targetSign;
    agent.routeOrdinal += 1;
    return this.configureRoute(agent, candidate);
  }

  chooseContinuationTarget(agent) {
    const base = this.boundaryAdjustedHeading(agent, agent.controller.heading, 1.55);
    const maximumJitter = this.random.range(20, 35) * Math.PI / 180;
    const jitter = this.random.range(-maximumJitter, maximumJitter);
    const cosine = Math.cos(jitter);
    const sine = Math.sin(jitter);
    const direction = this.boundaryAdjustedHeading(agent, {
      x: base.x * cosine - base.y * sine,
      y: base.x * sine + base.y * cosine,
    }, 1.75);
    let distance = this.random.range(0.35, 0.55);
    let displacement = screenDisplacement(direction, distance, this.aspect);
    const xLimit = Math.max(0.2, this.bounds.x - agent.collisionRadius / this.aspect);
    const yLimit = Math.max(0.2, this.bounds.y - agent.collisionRadius);
    let candidate = {
      x: clamp(agent.controller.offset.x + displacement.x, -xLimit, xLimit),
      y: clamp(agent.controller.offset.y + displacement.y, -yLimit, yLimit),
    };
    if (screenDistance(candidate, agent.controller.offset, this.aspect) < 0.33) {
      // Close to glass, a literal forward waypoint cannot be 0.35 units away.
      // Use the maximum biologically plausible continuation turn toward the
      // roomier vertical side instead of clamping a long vector into a short,
      // nearly sideways target.
      const baseAngle = Math.atan2(agent.controller.heading.y, agent.controller.heading.x);
      let best = null;
      for (const sign of [-1, 1]) {
        for (let degrees = 20; degrees <= 35; degrees += 1) {
          const angle = baseAngle + sign * degrees * Math.PI / 180;
          const trialDirection = { x: Math.cos(angle), y: Math.sin(angle) };
          for (let trialDistance = 0.35; trialDistance <= 0.551; trialDistance += 0.01) {
            const trialDisplacement = screenDisplacement(
              trialDirection,
              trialDistance,
              this.aspect,
            );
            const trial = {
              x: clamp(agent.controller.offset.x + trialDisplacement.x, -xLimit, xLimit),
              y: clamp(agent.controller.offset.y + trialDisplacement.y, -yLimit, yLimit),
            };
            const resolvedDistance = screenDistance(trial, agent.controller.offset, this.aspect);
            const resolvedDirection = screenDirection(
              trial.x - agent.controller.offset.x,
              trial.y - agent.controller.offset.y,
              this.aspect,
              agent.controller.heading,
            );
            const resolvedError = Math.abs(wrapAngle(
              Math.atan2(resolvedDirection.y, resolvedDirection.x) - baseAngle,
            ));
            if (resolvedDistance < 0.33 || resolvedError > 35 * Math.PI / 180 + 1e-6) continue;
            const score = resolvedDistance - resolvedError * 0.025;
            if (!best || score > best.score) best = { candidate: trial, score };
          }
        }
      }
      if (best) candidate = best.candidate;
    }
    return this.configureRoute(agent, candidate, { continuation: true, curveScale: 0.45 });
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
      agent.partnerIndex = null;
    } else if (behavior === ROAM_BEHAVIORS.WANDER) {
      agent.fleeStage = 'none';
      agent.partnerId = null;
      agent.partnerIndex = null;
      if (reason === 'glide-complete') this.chooseContinuationTarget(agent);
      else this.chooseWanderTarget(agent);
    } else if (behavior === ROAM_BEHAVIORS.RECOVER) {
      agent.partnerId = null;
      agent.partnerIndex = null;
      agent.fleeStage = 'glide';
    } else if (behavior === ROAM_BEHAVIORS.FLEE) {
      agent.partnerId = null;
      agent.partnerIndex = null;
    }
  }

  eligibleMeetingAgents() {
    return this.agents.filter((agent) => (
      agent.behavior === ROAM_BEHAVIORS.WANDER
      || agent.behavior === ROAM_BEHAVIORS.IDLE
    ) && agent.touchCooldown <= 0 && agent.socialCooldown <= 0);
  }

  canMeet() {
    return !this.meetingActive && this.eligibleMeetingAgents().length >= 2;
  }

  selectMeetingPair() {
    const eligible = this.eligibleMeetingAgents();
    let selected = null;
    let selectedScore = Infinity;
    for (let firstIndex = 0; firstIndex < eligible.length; firstIndex += 1) {
      for (let secondIndex = firstIndex + 1; secondIndex < eligible.length; secondIndex += 1) {
        const first = eligible[firstIndex];
        const second = eligible[secondIndex];
        const score = screenDistance(
          first.controller.offset,
          second.controller.offset,
          this.aspect,
        ) + this.random.range(0, 0.075);
        if (score < selectedScore) {
          selectedScore = score;
          selected = [first, second];
        }
      }
    }
    return selected;
  }

  forceMeeting(requestedPair = null) {
    if (!this.canMeet()) return false;
    let pair = null;
    if (Array.isArray(requestedPair) && requestedPair.length === 2) {
      pair = requestedPair.map((value) => (
        typeof value === 'number'
          ? this.agents[value]
          : this.agents.find((agent) => agent === value || agent.id === value)
      ));
      if (pair.some((agent) => !agent || !this.eligibleMeetingAgents().includes(agent))) return false;
    } else {
      pair = this.selectMeetingPair();
    }
    if (!pair) return false;

    this.meetingEpisode += 1;
    this.meetingActive = true;
    const [first, second] = pair;
    this.activeMeeting = { firstId: first.id, secondId: second.id };
    this.enterBehavior(first, ROAM_BEHAVIORS.MEET, 9.0, 'social-rendezvous');
    this.enterBehavior(second, ROAM_BEHAVIORS.MEET, 9.0, 'social-rendezvous');
    first.partnerId = second.id;
    first.partnerIndex = second.index;
    second.partnerId = first.id;
    second.partnerIndex = first.index;
    this.nextMeetingAt = Infinity;
    return true;
  }

  getMeetingAgents() {
    if (!this.meetingActive || !this.activeMeeting) return [];
    const first = this.agents.find((agent) => agent.id === this.activeMeeting.firstId);
    const second = this.agents.find((agent) => agent.id === this.activeMeeting.secondId);
    return first && second ? [first, second] : [];
  }

  cancelMeeting(fleeingAgent = null) {
    if (!this.meetingActive) return;
    const pair = this.getMeetingAgents();
    if (fleeingAgent && !pair.includes(fleeingAgent)) return;
    this.meetingActive = false;
    this.activeMeeting = null;
    pair.forEach((agent) => {
      if (agent === fleeingAgent) return;
      if (agent.behavior === ROAM_BEHAVIORS.MEET
        || agent.behavior === ROAM_BEHAVIORS.INSPECT) {
        this.enterBehavior(agent, ROAM_BEHAVIORS.WANDER, this.random.range(4.4, 7.2), 'partner-startled');
      }
      agent.socialCooldown = this.random.range(3.5, 6.5);
    });
    this.scheduleNextMeeting();
  }

  beginInspect() {
    if (!this.meetingActive) return;
    const duration = this.random.range(2.2, 3.4);
    this.getMeetingAgents().forEach((agent) => {
      this.enterBehavior(agent, ROAM_BEHAVIORS.INSPECT, duration, 'close-contact');
    });
  }

  finishInspect() {
    if (!this.meetingActive) return;
    const pair = this.getMeetingAgents();
    this.meetingActive = false;
    this.activeMeeting = null;
    pair.forEach((agent) => {
      this.enterBehavior(agent, ROAM_BEHAVIORS.WANDER, this.random.range(4.8, 7.5), 'inspection-complete');
      agent.socialCooldown = this.random.range(4.5, 7.5);
    });
    this.scheduleNextMeeting();
  }

  beginFlee(agent, pointerDistance, {
    threatPoint = this.pointer,
    eventId = this.pointer.eventId,
    pointerType = this.pointer.pointerType,
    reason = 'pointer-contact',
    propagated = false,
  } = {}) {
    const position = agent.controller.offset;
    let away = screenDirection(
      position.x - threatPoint.x,
      position.y - threatPoint.y,
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
    this.enterBehavior(agent, ROAM_BEHAVIORS.FLEE, 2.2, reason);
    agent.fleeDirection = away;
    agent.targetHeading = copyPoint(away);
    agent.navigationHeading = copyPoint(away);
    agent.fleeStage = 'turn';
    agent.fleeStrokeTime = 0;
    agent.fleeStrokeIndex = 0;
    agent.fleeEpisode += 1;
    agent.startleRemaining = 0;
    agent.touchCooldown = 2.0;
    agent.turnSide = Math.sign(cross(agent.controller.heading, away))
      || (this.random.next() < 0.5 ? -1 : 1);
    agent.lastReactEvent = eventId;
    const interaction = {
      time: this.time,
      agentId: agent.id,
      eventId,
      pointerType,
      distance: pointerDistance,
      fleeEpisode: agent.fleeEpisode,
      propagated,
    };
    this.startleLog.push(interaction);
    if (this.startleLog.length > 128) this.startleLog.shift();
    if (!propagated) this.lastInteraction = interaction;
    return true;
  }

  scheduleStartlePropagation(source, eventId) {
    if (this.startlePropagationRadius <= 0) return;
    const sourcePoint = copyPoint(source.controller.offset);
    const candidates = this.agents
      .filter((agent) => agent !== source
        && agent.lastScheduledEvent !== eventId
        && agent.lastReactEvent !== eventId
        && agent.touchCooldown <= 0
        && agent.behavior !== ROAM_BEHAVIORS.FLEE
        && agent.behavior !== ROAM_BEHAVIORS.RECOVER)
      .map((agent) => ({
        agent,
        distance: screenDistance(agent.controller.offset, sourcePoint, this.aspect),
      }))
      .filter(({ distance }) => distance <= this.startlePropagationRadius)
      .sort((left, right) => left.distance - right.distance || left.agent.index - right.agent.index);

    candidates.forEach(({ agent, distance }, rank) => {
      agent.lastScheduledEvent = eventId;
      this.pendingStartles.push({
        agentId: agent.id,
        eventId,
        sourceAgentId: source.id,
        threatPoint: sourcePoint,
        distance,
        triggerAt: this.time + 0.10 + rank * 0.085 + this.random.range(0.012, 0.038),
        pointerType: this.pointer.pointerType,
      });
    });
    this.pendingStartles.sort((left, right) => (
      left.triggerAt - right.triggerAt
      || this.agents.findIndex((agent) => agent.id === left.agentId)
        - this.agents.findIndex((agent) => agent.id === right.agentId)
    ));
  }

  beginStartle(agent, pending) {
    if (agent.behavior === ROAM_BEHAVIORS.MEET
      || agent.behavior === ROAM_BEHAVIORS.INSPECT) this.cancelMeeting();
    const away = screenDirection(
      agent.controller.offset.x - pending.threatPoint.x,
      agent.controller.offset.y - pending.threatPoint.y,
      this.aspect,
      { x: -agent.controller.heading.x, y: -agent.controller.heading.y },
    );
    agent.startleDirection = away;
    agent.startleRemaining = this.random.range(0.32, 0.48);
    agent.startleEvent = pending.eventId;
    agent.lastReactEvent = pending.eventId;
    agent.touchCooldown = Math.max(agent.touchCooldown, 0.62);
    agent.turnSide = Math.sign(cross(agent.controller.heading, away))
      || (this.random.next() < 0.5 ? -1 : 1);
    const reaction = {
      time: this.time,
      agentId: agent.id,
      eventId: pending.eventId,
      pointerType: pending.pointerType,
      distance: pending.distance,
      fleeEpisode: agent.fleeEpisode,
      propagated: true,
      reaction: 'orient-freeze',
    };
    this.startleLog.push(reaction);
    if (this.startleLog.length > 128) this.startleLog.shift();
  }

  processPendingStartles() {
    while (this.pendingStartles.length > 0
      && this.pendingStartles[0].triggerAt <= this.time + EPSILON) {
      const pending = this.pendingStartles.shift();
      const agent = this.agents.find((candidate) => candidate.id === pending.agentId);
      if (!agent
        || agent.lastReactEvent === pending.eventId
        || agent.touchCooldown > 0
        || agent.behavior === ROAM_BEHAVIORS.FLEE
        || agent.behavior === ROAM_BEHAVIORS.RECOVER) continue;
      this.beginStartle(agent, pending);
    }
  }

  processPointer() {
    if (!this.pointer.active) return;
    const contacts = [];
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
        contacts.push({ agent, distance });
      } else if (inside) {
        agent.pointerLatched = true;
      }
    });

    contacts.sort((left, right) => left.distance - right.distance || left.agent.index - right.agent.index);
    const primary = contacts[0];
    if (primary) {
      this.beginFlee(primary.agent, primary.distance);
      this.scheduleStartlePropagation(primary.agent, this.pointer.eventId);
      contacts.slice(1).forEach(({ agent, distance }, rank) => {
        if (agent.lastScheduledEvent === this.pointer.eventId) return;
        agent.lastScheduledEvent = this.pointer.eventId;
        this.pendingStartles.push({
          agentId: agent.id,
          eventId: this.pointer.eventId,
          sourceAgentId: primary.agent.id,
          threatPoint: copyPoint(this.pointer),
          distance,
          triggerAt: this.time + 0.055 + rank * 0.065,
          pointerType: this.pointer.pointerType,
        });
      });
      this.pendingStartles.sort((left, right) => left.triggerAt - right.triggerAt);
    }
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

  meetingPartner(agent) {
    if (!agent.partnerId) return null;
    return this.agents.find((candidate) => candidate.id === agent.partnerId) || null;
  }

  routeHeading(agent) {
    const position = agent.controller.offset;
    const distance = screenDistance(position, agent.target, this.aspect);
    const progress = clamp(1 - distance / Math.max(0.05, agent.routeLength));
    const lookAhead = clamp(progress + 0.13 + (1 - progress) * 0.07);
    const aim = quadraticBezier(agent.routeStart, agent.routeControl, agent.target, lookAhead);
    const predictiveSeconds = agent.continuationRoute ? 0.52 : 0.78;
    return screenDirection(
      aim.x - position.x - agent.controller.velocity.x * predictiveSeconds,
      aim.y - position.y - agent.controller.velocity.y * predictiveSeconds,
      this.aspect,
      agent.controller.heading,
    );
  }

  crowdAvoidance(agent) {
    let avoidanceX = 0;
    let avoidanceY = 0;
    this.agents.forEach((other) => {
      if (other === agent || other.id === agent.partnerId) return;
      const dx = agent.controller.offset.x - other.controller.offset.x;
      const dy = agent.controller.offset.y - other.controller.offset.y;
      const distance = screenDistance(agent.controller.offset, other.controller.offset, this.aspect);
      const comfort = agent.collisionRadius + other.collisionRadius + 0.16;
      if (distance >= comfort) return;
      let away;
      if (distance < EPSILON) {
        const angle = fract((agent.index + 1) * 0.618 + (other.index + 1) * 0.173) * TAU;
        away = { x: Math.cos(angle), y: Math.sin(angle) };
      } else {
        away = screenDirection(dx, dy, this.aspect, agent.controller.heading);
      }
      const pressure = 1 - smootherstep(
        agent.collisionRadius + other.collisionRadius,
        comfort,
        distance,
      );
      avoidanceX += away.x * pressure;
      avoidanceY += away.y * pressure;
    });
    return { x: avoidanceX, y: avoidanceY };
  }

  smoothNavigationHeading(agent, desired, deltaTime) {
    if (agent.behavior === ROAM_BEHAVIORS.FLEE) {
      agent.navigationHeading = copyPoint(desired);
      return agent.navigationHeading;
    }
    const currentAngle = Math.atan2(agent.navigationHeading.y, agent.navigationHeading.x);
    const targetAngle = Math.atan2(desired.y, desired.x);
    const error = wrapAngle(targetAngle - currentAngle);
    const response = agent.startleRemaining > 0
      ? 3.15
      : agent.behavior === ROAM_BEHAVIORS.MEET
      || agent.behavior === ROAM_BEHAVIORS.INSPECT
      ? 2.6
      : agent.behavior === ROAM_BEHAVIORS.IDLE ? 1.05 : 1.65;
    const step = error * (1 - Math.exp(-response * deltaTime));
    const maximumRate = agent.startleRemaining > 0 ? 1.8 : 1.15;
    const angle = currentAngle + clamp(step, -maximumRate * deltaTime, maximumRate * deltaTime);
    agent.navigationHeading = { x: Math.cos(angle), y: Math.sin(angle) };
    return agent.navigationHeading;
  }

  desiredHeading(agent, deltaTime) {
    const position = agent.controller.offset;
    const partner = this.meetingPartner(agent);
    let desired = agent.targetHeading;
    if (agent.behavior === ROAM_BEHAVIORS.WANDER) {
      desired = this.routeHeading(agent);
    } else if (agent.behavior === ROAM_BEHAVIORS.IDLE) {
      desired = agent.idleHeading;
    } else if (agent.behavior === ROAM_BEHAVIORS.MEET
      || agent.behavior === ROAM_BEHAVIORS.INSPECT) {
      if (!partner) return this.smoothNavigationHeading(agent, agent.controller.heading, deltaTime);
      desired = screenDirection(
        partner.controller.offset.x - position.x,
        partner.controller.offset.y - position.y,
        this.aspect,
        agent.controller.heading,
      );
    } else if (agent.behavior === ROAM_BEHAVIORS.FLEE) {
      desired = agent.fleeDirection;
    } else if (agent.behavior === ROAM_BEHAVIORS.RECOVER) {
      desired = agent.controller.heading;
    }
    if (agent.startleRemaining > 0 && agent.behavior !== ROAM_BEHAVIORS.FLEE) {
      desired = agent.startleDirection;
    }
    const avoidance = this.crowdAvoidance(agent);
    const avoidanceWeight = agent.behavior === ROAM_BEHAVIORS.FLEE ? 1.25 : 0.72;
    desired = normalize({
      x: desired.x + avoidance.x * avoidanceWeight,
      y: desired.y + avoidance.y * avoidanceWeight,
    }, desired);
    const boundaryWeight = agent.behavior === ROAM_BEHAVIORS.FLEE ? 1.55 : 1;
    desired = this.boundaryAdjustedHeading(agent, desired, boundaryWeight);
    agent.targetHeading = this.smoothNavigationHeading(agent, desired, deltaTime);
    return agent.targetHeading;
  }

  advanceBehavior(agent, deltaTime) {
    agent.stateAge += deltaTime;
    agent.touchCooldown = Math.max(0, agent.touchCooldown - deltaTime);
    agent.socialCooldown = Math.max(0, agent.socialCooldown - deltaTime);
    agent.startleRemaining = Math.max(0, agent.startleRemaining - deltaTime);
    agent.armPhase = fract(agent.armPhase + deltaTime / agent.armPeriod);

    if (agent.behavior === ROAM_BEHAVIORS.WANDER) {
      const targetDistance = screenDistance(agent.controller.offset, agent.target, this.aspect);
      if (targetDistance < 0.15 || agent.stateAge >= agent.stateDuration) {
        if (agent.continuationRoute) {
          agent.stateAge = 0;
          this.chooseWanderTarget(agent);
        } else {
          this.enterBehavior(agent, ROAM_BEHAVIORS.IDLE, this.random.range(1.4, 3.4), 'wander-pause');
        }
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

    const partner = this.meetingPartner(agent);
    const heading = this.desiredHeading(agent, deltaTime);
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
      const separation = partner
        ? screenDistance(agent.controller.offset, partner.controller.offset, this.aspect)
        : Infinity;
      const stopDistance = partner
        ? agent.collisionRadius + partner.collisionRadius + 0.045
        : Infinity;
      const approachGain = partner
        ? smootherstep(stopDistance, stopDistance + 0.12, separation)
        : 0;
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

    if (agent.startleRemaining > 0 && agent.behavior !== ROAM_BEHAVIORS.FLEE) {
      // Neighbours only orient and briefly hold position. They do not copy the
      // touched animal's mantle jet, which would make the aquarium pulse as one.
      agent.drive = 0;
      jet = 0;
      squeeze = 0;
      glide = 0;
      intake = 0.24;
      demand = 0;
      braking = true;
      turning = false;
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
    this.applyCurrentDrift(agent, deltaTime);
    agent.motionState = this.makeMotionState(agent);
  }

  applyCurrentDrift(agent, deltaTime) {
    // This is a low-frequency water current, not locomotion noise. The body
    // moves with the water while controller.velocity remains water-relative.
    const phase = agent.currentPhase * TAU;
    const position = agent.controller.offset;
    const targetCurrent = {
      x: Math.sin(this.time * 0.16 + position.y * 1.8 + phase) * 0.0065,
      y: Math.cos(this.time * 0.11 + position.x * 1.55 - phase) * 0.0042
        + Math.sin(this.time * 0.047 + phase * 0.7) * 0.0018,
    };
    const response = 1 - Math.exp(-0.72 * deltaTime);
    agent.currentVelocity.x += (targetCurrent.x - agent.currentVelocity.x) * response;
    agent.currentVelocity.y += (targetCurrent.y - agent.currentVelocity.y) * response;
    agent.controller.offset.x += agent.currentVelocity.x * deltaTime;
    agent.controller.offset.y += agent.currentVelocity.y * deltaTime;
    agent.verticalMinimum = Math.min(agent.verticalMinimum, agent.controller.offset.y);
    agent.verticalMaximum = Math.max(agent.verticalMaximum, agent.controller.offset.y);
  }

  resolvePair(first, second) {
    const dx = second.controller.offset.x - first.controller.offset.x;
    const dy = second.controller.offset.y - first.controller.offset.y;
    let distance = Math.hypot(dx * this.aspect, dy);
    const minimum = first.collisionRadius + second.collisionRadius;
    const constraintMinimum = minimum + 2e-5;
    if (distance >= constraintMinimum) return false;

    let screenNormalX;
    let screenNormalY;
    if (distance < EPSILON) {
      const angle = fract((first.index + 1) * 0.754877666 + (second.index + 1) * 0.569840296) * TAU;
      screenNormalX = Math.cos(angle);
      screenNormalY = Math.sin(angle);
      distance = 0;
    } else {
      screenNormalX = dx * this.aspect / distance;
      screenNormalY = dy / distance;
    }
    const penetration = constraintMinimum - distance;
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
    return true;
  }

  resolveSeparation() {
    for (let pass = 0; pass < 12; pass += 1) {
      let corrected = false;
      for (let firstIndex = 0; firstIndex < this.agents.length; firstIndex += 1) {
        for (let secondIndex = firstIndex + 1; secondIndex < this.agents.length; secondIndex += 1) {
          corrected = this.resolvePair(
            this.agents[firstIndex],
            this.agents[secondIndex],
          ) || corrected;
        }
      }
      this.resolveHardBounds();
      if (!corrected) break;
    }

    this.minimumDistance = Infinity;
    for (let firstIndex = 0; firstIndex < this.agents.length; firstIndex += 1) {
      for (let secondIndex = firstIndex + 1; secondIndex < this.agents.length; secondIndex += 1) {
        this.minimumDistance = Math.min(this.minimumDistance, screenDistance(
          this.agents[firstIndex].controller.offset,
          this.agents[secondIndex].controller.offset,
          this.aspect,
        ));
      }
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
        if (agent.currentVelocity.x * agent.controller.offset.x > 0) agent.currentVelocity.x = 0;
      }
      if (agent.controller.offset.y < -yLimit || agent.controller.offset.y > yLimit) {
        agent.controller.offset.y = clamp(agent.controller.offset.y, -yLimit, yLimit);
        if (agent.controller.velocity.y * agent.controller.offset.y > 0) {
          agent.controller.velocity.y = 0;
        }
        if (agent.currentVelocity.y * agent.controller.offset.y > 0) agent.currentVelocity.y = 0;
      }
    });
  }

  resolveWorldConstraints() {
    this.resolveHardBounds();
    this.resolveSeparation();
    this.resolveHardBounds();
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

    this.processPendingStartles();
    this.processPointer();
    if (!this.meetingActive && this.time >= this.nextMeetingAt && this.canMeet()) {
      this.forceMeeting();
    }

    this.agents.forEach((agent) => this.advanceBehavior(agent, dt));
    this.resolveWorldConstraints();

    const [first, second] = this.getMeetingAgents();
    if (first && second
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
      activeMeeting: this.activeMeeting ? { ...this.activeMeeting } : null,
      nextMeetingAt: this.nextMeetingAt,
      minimumDistance: this.minimumDistance,
      lastInteraction: this.lastInteraction ? { ...this.lastInteraction } : null,
      pendingStartles: this.pendingStartles.map((pending) => ({
        ...pending,
        threatPoint: copyPoint(pending.threatPoint),
      })),
      startleLog: this.startleLog.map((entry) => ({ ...entry })),
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
        partnerIndex: agent.partnerIndex,
        apparentSpan: agent.apparentSpan,
        opacity: agent.opacity,
        depthLayer: agent.depthLayer,
        depthFactor: agent.depthFactor,
        collisionRadius: agent.collisionRadius,
        reactionRadius: agent.reactionRadius,
        motionSeed: agent.motionSeed,
        motionPhase: agent.motionPhase,
        motionRate: agent.motionRate,
        fleeStage: agent.fleeStage,
        fleeEpisode: agent.fleeEpisode,
        fleeStrokeIndex: agent.fleeStrokeIndex,
        jet: agent.jetCycle?.jet || 0,
        armPhase: agent.armPhase,
        startleRemaining: agent.startleRemaining,
        startleEvent: agent.startleEvent,
        continuationRoute: agent.continuationRoute,
        currentVelocity: copyPoint(agent.currentVelocity),
        verticalMinimum: agent.verticalMinimum,
        verticalMaximum: agent.verticalMaximum,
      })),
    };
  }
}
