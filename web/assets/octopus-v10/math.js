export const TAU = Math.PI * 2;

export function clamp01(value) {
  return Math.min(1, Math.max(0, value));
}

export function sampleCameraDepthBias(headingY, turnDepth = 0) {
  const boundedHeadingY = Math.min(1, Math.max(-1, Number.isFinite(headingY) ? headingY : 0));
  const boundedTurnDepth = Math.min(0.42, Math.max(-0.42, Number.isFinite(turnDepth) ? turnDepth : 0));
  return -boundedHeadingY * 0.22 + boundedTurnDepth * 0.86;
}

export function lerp(a, b, amount) {
  return a + (b - a) * amount;
}

export function smoothstep(edge0, edge1, value) {
  if (edge0 === edge1) return value < edge0 ? 0 : 1;
  const x = clamp01((value - edge0) / (edge1 - edge0));
  return x * x * (3 - 2 * x);
}

export function smootherstep(edge0, edge1, value) {
  if (edge0 === edge1) return value < edge0 ? 0 : 1;
  const x = clamp01((value - edge0) / (edge1 - edge0));
  return x * x * x * (x * (x * 6 - 15) + 10);
}

export function pulse(value, start, peak, end) {
  return smoothstep(start, peak, value) * (1 - smoothstep(peak, end, value));
}

export function damp(current, target, lambda, deltaTime) {
  return lerp(current, target, 1 - Math.exp(-lambda * deltaTime));
}

export function shortestAngleDelta(from, to) {
  let delta = (to - from) % TAU;
  if (delta > Math.PI) delta -= TAU;
  if (delta < -Math.PI) delta += TAU;
  return delta;
}

/**
 * Converts a normalized timeline position into continuous behavior weights.
 * Every output is reversible and differentiable enough to scrub without pops.
 */
export function sampleTimeline(progress, time = 0, scrollVelocity = 0) {
  const p = clamp01(progress);
  const explore = pulse(p, 0.08, 0.30, 0.52);
  const swim = pulse(p, 0.38, 0.57, 0.76);
  const guard = smoothstep(0.69, 0.94, p);
  const settle = 1 - smoothstep(0.04, 0.22, p);
  const breath = 0.5 + 0.5 * Math.sin(time * (1.15 + swim * 1.3));

  return {
    progress: p,
    scrollVelocity,
    settle,
    explore,
    reach: explore,
    swim,
    guard,
    breath,
    spread: 0.78 + explore * 0.34 - swim * 0.18 + guard * 0.29,
    curl: 0.12 + settle * 0.10 + explore * 0.16 + swim * 0.42 + guard * 0.58,
    lift: explore * 0.10 + swim * 0.48 + guard * 0.20,
    pulse: 0.18 + swim * 0.82 + Math.min(0.32, Math.abs(scrollVelocity) * 0.025),
    activity: 0.32 + explore * 0.24 + swim * 0.72 + guard * 0.18,
    bodyYaw: Math.sin(p * Math.PI * 1.55) * 0.18 - guard * 0.08,
    bodyPitch: -swim * 0.22 + guard * 0.06,
  };
}
