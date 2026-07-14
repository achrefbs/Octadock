import * as THREE from 'three';

// Blender exports the mantle apex along model +Y. A mantle-first jet therefore
// travels +Y and the arm crown must streamline toward -Y. Older revisions sent
// the arms down +Z, ninety degrees away from the biological travel axis.
const MANTLE_AXIS = new THREE.Vector3(0, 1, 0);

// Complete the redirect before the final two joints, leaving a short passive
// tip while distributing curvature over the muscular middle of the arm.
const TURN_COMPLETION = 0.88;
const WEB_LOCKED_JOINTS = 2;
// The closed crown remains a broad trailing cone, not a single-axis rope.
// 0.22 is also large enough that the maximum authored water-load bend in
// HydrostatMotion cannot invert an arm's radial component during a turn.
const LOOSE_LANE_RADIUS = 0.26;
const TIGHT_LANE_RADIUS = 0.22;

const OPEN_CURL_VARIATION = [0.92, 1.05, 0.96, 1.09, 0.90, 1.03, 0.95, 1.07];
const OPEN_DROP_VARIATION = [1.03, 0.91, 1.08, 0.96, 1.06, 0.93, 1.09, 0.95];

function smootherstepLocal(edge0, edge1, value) {
  const x = THREE.MathUtils.clamp((value - edge0) / (edge1 - edge0), 0, 1);
  return x * x * x * (x * (x * 6 - 15) + 10);
}

function smoothstepLocal(edge0, edge1, value) {
  const x = THREE.MathUtils.clamp((value - edge0) / (edge1 - edge0), 0, 1);
  return x * x * (3 - 2 * x);
}

/**
 * Build the broad, drag-producing crown used at the end of recovery. Real
 * octopus arms do not become eight ruler-straight spokes: the recovering arms
 * retain gentle plan-view curvature, droop and torsion. A shared curl direction
 * preserves radial ordering, while small deterministic differences stop the
 * silhouette from reading as a mechanical umbrella.
 */
export function buildOpenSwimDirections({ armIndex, root, restDirections }) {
  if (!restDirections?.length) throw new Error('An open swim crown requires authored arm directions.');
  const radial = new THREE.Vector3(root.x, 0, root.z);
  if (radial.lengthSq() < 1e-8) radial.copy(restDirections[0]).setY(0);
  radial.normalize();
  const tangent = new THREE.Vector3(-radial.z, 0, radial.x);
  const lastIndex = Math.max(1, restDirections.length - 1);
  const curlVariation = OPEN_CURL_VARIATION[armIndex % OPEN_CURL_VARIATION.length];
  const dropVariation = OPEN_DROP_VARIATION[armIndex % OPEN_DROP_VARIATION.length];

  return restDirections.map((authored, boneIndex) => {
    if (boneIndex < 3) return authored.clone().normalize();
    const s = boneIndex / lastIndex;
    const free = smootherstepLocal(0.20, 0.50, s);
    const middle = Math.sin(Math.PI * s) ** 2;
    const direction = authored.clone();
    direction.addScaledVector(tangent, free * (0.105 * middle + 0.070 * s * s) * curlVariation);
    direction.addScaledVector(MANTLE_AXIS, -free * (0.115 * middle + 0.045 * s) * dropVariation);
    return direction.normalize();
  });
}

/**
 * Solve a smooth under-body streamline for a complete arm chain.
 *
 * The original nine-joint version used a cubic curve followed by a
 * shortest-quaternion limiter. For upstream-facing arms that limiter
 * necessarily chose an outward U-turn. V10 uses sixteen physical joints per
 * arm and an ordered cylindrical guide instead. Authored web directions 00–01
 * remain exact. Every free joint then rotates in the plane formed by its own
 * crown radius and the downstream axis. The authored tangential component is
 * faded out through the proximal third. Since the radial component stays
 * positive, neighboring arms cannot exchange lanes or form an H-shaped cross.
 */
export function buildTrailingDirections({
  armIndex,
  root,
  rootDirection,
  restDirections = null,
  segmentLengths,
  trailingSign = 1,
  tightness = 1,
}) {
  const totalLength = segmentLengths.reduce((sum, length) => sum + length, 0);
  if (!(totalLength > 0)) throw new Error('A swim guide requires positive arm segment lengths.');

  const authoredDirections = restDirections?.length === segmentLengths.length
    ? restDirections.map((direction) => direction.clone().normalize())
    : segmentLengths.map(() => rootDirection.clone().normalize());
  const lockedCount = Math.min(WEB_LOCKED_JOINTS, segmentLengths.length);
  const directions = authoredDirections.slice(0, lockedCount);
  if (lockedCount === segmentLengths.length) {
    const end = root.clone();
    directions.forEach((direction, index) => end.addScaledVector(direction, segmentLengths[index]));
    return {
      directions,
      end,
      curveLength: totalLength,
      armLength: totalLength,
    };
  }

  const sign = trailingSign < 0 ? -1 : 1;
  const start = authoredDirections[lockedCount - 1];
  const laneRadius = THREE.MathUtils.lerp(
    LOOSE_LANE_RADIUS,
    TIGHT_LANE_RADIUS,
    tightness,
  );
  const radial = new THREE.Vector3(root.x, 0, root.z);
  if (radial.lengthSq() < 1e-8) radial.copy(rootDirection).setY(0);
  radial.normalize();
  const downstream = new THREE.Vector3(0, -sign, 0);
  const tangent = new THREE.Vector3(-radial.z, 0, radial.x);
  const startRadial = Math.max(1e-4, start.dot(radial));
  const startDownstream = start.dot(downstream);
  const startTangent = start.dot(tangent);
  const startAngle = Math.atan2(startDownstream, startRadial);
  const terminalAngle = Math.atan2(1, laneRadius);
  const freeCount = segmentLengths.length - lockedCount;
  const freeHeadSpan = segmentLengths
    .slice(lockedCount, -1)
    .reduce((sum, length) => sum + length, 0);
  let traversed = 0;

  for (let freeIndex = 0; freeIndex < freeCount; freeIndex += 1) {
    // Parameterize by physical segment length. The first free direction is
    // exactly the last locked direction, eliminating the former root hinge.
    const headProgress = freeHeadSpan > 1e-8
      ? THREE.MathUtils.clamp(traversed / freeHeadSpan, 0, 1)
      : freeIndex / Math.max(1, freeCount - 1);
    const redirect = smoothstepLocal(0, TURN_COMPLETION, headProgress);
    const angle = THREE.MathUtils.lerp(startAngle, terminalAngle, redirect);
    const tangentRetention = 1 - smootherstepLocal(0, 0.42, headProgress);
    const direction = radial.clone().multiplyScalar(Math.cos(angle))
      .addScaledVector(downstream, Math.sin(angle))
      .addScaledVector(tangent, startTangent * tangentRetention)
      .normalize();
    directions.push(direction);
    traversed += segmentLengths[lockedCount + freeIndex];
  }

  const end = root.clone();
  directions.forEach((direction, index) => end.addScaledVector(direction, segmentLengths[index]));
  return {
    directions,
    end,
    curveLength: totalLength,
    armLength: totalLength,
  };
}
