const EPSILON = 1e-7;

function clamp(value, min, max) {
  return Math.min(max, Math.max(min, value));
}

function wrapAngle(angle) {
  while (angle > Math.PI) angle -= Math.PI * 2;
  while (angle < -Math.PI) angle += Math.PI * 2;
  return angle;
}

/**
 * A small water-relative dynamics model for the website composition layer.
 *
 * Positions and velocities are expressed in normalized screen coordinates.
 * Page progress owns the moving anchor; this controller owns only the animal's
 * inertial displacement around that anchor. Propulsion is a force pulse, not a
 * direct mapping from wheel pixels to position.
 */
export class ScrollSwimController {
  constructor({
    maxSpeed = 0.31,
    maxOffsetX = 0.16,
    maxOffsetY = 0.19,
    turnNaturalFrequency = 5.2,
    cruiseNaturalFrequency = 4.1,
    turnDampingRatio = 1.05,
    cruiseDampingRatio = 1.0,
    maximumTurnAcceleration = 16,
    maximumCruiseAcceleration = 9,
    maximumTurnRate = 3.2,
    maximumCruiseTurnRate = 2.8,
  } = {}) {
    this.maxSpeed = maxSpeed;
    this.maxOffsetX = maxOffsetX;
    this.maxOffsetY = maxOffsetY;
    this.turnNaturalFrequency = turnNaturalFrequency;
    this.cruiseNaturalFrequency = cruiseNaturalFrequency;
    this.turnDampingRatio = turnDampingRatio;
    this.cruiseDampingRatio = cruiseDampingRatio;
    this.maximumTurnAcceleration = maximumTurnAcceleration;
    this.maximumCruiseAcceleration = maximumCruiseAcceleration;
    this.maximumTurnRate = maximumTurnRate;
    this.maximumCruiseTurnRate = maximumCruiseTurnRate;
    this.offset = { x: 0, y: 0 };
    this.velocity = { x: 0, y: 0 };
    this.heading = { x: 0, y: -1 };
    this.headingAngle = -Math.PI / 2;
    this.angularVelocity = 0;
    this.angularAcceleration = 0;
    this.turnWasActive = false;
    this.turnTargetAngle = null;
    this.turnStartError = Math.PI;
    this.turnProgress = 0;
    this.turnEnvelope = 0;
    this.bodyMass = 1;
    this.addedMass = 0.58;
    this.previousAddedMass = this.addedMass;
    this.effectiveMass = this.bodyMass + this.addedMass;
    this.previousSqueeze = 0;
    this.acceleration = { x: 0, y: 0 };
    this.telemetry = {};
  }

  reset({ headingX = 0, headingY = -1 } = {}) {
    const length = Math.hypot(headingX, headingY) || 1;
    this.offset.x = 0;
    this.offset.y = 0;
    this.velocity.x = 0;
    this.velocity.y = 0;
    this.heading.x = headingX / length;
    this.heading.y = headingY / length;
    this.headingAngle = Math.atan2(this.heading.y, this.heading.x);
    this.angularVelocity = 0;
    this.angularAcceleration = 0;
    this.turnWasActive = false;
    this.turnTargetAngle = null;
    this.turnStartError = Math.PI;
    this.turnProgress = 0;
    this.turnEnvelope = 0;
    this.addedMass = 0.58;
    this.previousAddedMass = this.addedMass;
    this.effectiveMass = this.bodyMass + this.addedMass;
    this.previousSqueeze = 0;
    this.acceleration.x = 0;
    this.acceleration.y = 0;
  }

  update(deltaTime, input = {}) {
    const dt = clamp(Number.isFinite(deltaTime) ? deltaTime : 1 / 120, 1 / 1000, 1 / 30);
    let commandX = Number.isFinite(input.headingX) ? input.headingX : this.heading.x;
    let commandY = Number.isFinite(input.headingY) ? input.headingY : this.heading.y;
    const commandLength = Math.hypot(commandX, commandY);
    if (commandLength > EPSILON) {
      commandX /= commandLength;
      commandY /= commandLength;
    } else {
      commandX = this.heading.x;
      commandY = this.heading.y;
    }

    const turning = Boolean(input.turning);
    const braking = Boolean(input.braking);
    const demand = clamp(input.demand ?? 0, 0, 1);
    const jet = clamp(input.jet ?? 0, 0, 1);
    const squeeze = clamp(input.squeeze ?? 0, 0, 1);
    const glide = clamp(input.glide ?? 0, 0, 1);
    const intake = clamp(input.intake ?? 0, 0, 1);
    const thrustMultiplier = clamp(input.thrustMultiplier ?? 1, 0, 3);
    const squeezeRate = Math.max(0, (squeeze - this.previousSqueeze) / dt);
    this.previousSqueeze = squeeze;

    // A bluff, open animal must accelerate some surrounding water with it.
    // Contraction sheds most of that added mass; refilling restores it. These
    // normalized values keep the website-scale solver bounded while retaining
    // the variable-mass term in d[(m + ma)v]/dt = sum(F).
    this.addedMass = 0.22 + (1 - squeeze) * 0.36 + intake * 0.08;
    const addedMassRate = (this.addedMass - this.previousAddedMass) / dt;
    this.previousAddedMass = this.addedMass;
    this.effectiveMass = this.bodyMass + this.addedMass;

    // Heading is a critically damped torque/inertia system. A reversal now has
    // finite angular acceleration and distal-arm lag instead of an instantaneous
    // constant-rate rotation whose acceleration grew with the render rate.
    const targetAngle = Math.atan2(commandY, commandX);
    let angleError = wrapAngle(targetAngle - this.headingAngle);
    // Opposite headings have two equally short arcs. Choosing one with a
    // tiny X component left the animal nine degrees off-axis when the next
    // jet began. Keep the target exact and use the authored turn side only to
    // choose whether the 180-degree arc passes through screen-left or right.
    if (turning && Math.abs(Math.abs(angleError) - Math.PI) < 1e-4) {
      const turnSide = Math.sign(input.turnSide || 1);
      const verticalSign = Math.sign(this.heading.y) || 1;
      angleError = -Math.PI * turnSide * verticalSign;
    }
    const angularInertia = 0.52 + this.addedMass * 0.48;
    const targetChanged = this.turnTargetAngle === null
      || Math.abs(wrapAngle(targetAngle - this.turnTargetAngle)) > 1e-3;
    if (turning && (!this.turnWasActive || targetChanged)) {
      this.turnTargetAngle = targetAngle;
      this.turnStartError = Math.max(Math.PI / 180, Math.abs(angleError));
      this.turnProgress = 0;
    } else if (!turning) {
      this.turnTargetAngle = targetAngle;
      this.turnProgress = 0;
    }

    // One critically damped angular system is the sole turn clock. The former
    // fixed journey timer and final quaternion filter could lead/lag this body
    // by tens of degrees. A lower bounded turn rate gives the giant specimen
    // visible weight without ever stepping the heading directly.
    const naturalFrequency = turning
      ? this.turnNaturalFrequency
      : this.cruiseNaturalFrequency;
    const dampingRatio = turning
      ? this.turnDampingRatio
      : this.cruiseDampingRatio;
    const maximumAngularAcceleration = turning
      ? this.maximumTurnAcceleration
      : this.maximumCruiseAcceleration;
    const desiredAngularAcceleration = naturalFrequency * naturalFrequency * angleError
      - 2 * dampingRatio * naturalFrequency * this.angularVelocity;
    const maximumTorque = maximumAngularAcceleration * angularInertia;
    const controlTorque = clamp(
      desiredAngularAcceleration * angularInertia,
      -maximumTorque,
      maximumTorque,
    );
    this.angularAcceleration = controlTorque / angularInertia;
    this.angularVelocity += this.angularAcceleration * dt;
    const maxTurnRate = turning
      ? this.maximumTurnRate
      : this.maximumCruiseTurnRate;
    this.angularVelocity = clamp(this.angularVelocity, -maxTurnRate, maxTurnRate);
    const angleStep = this.angularVelocity * dt;
    if (Math.sign(angleStep) === Math.sign(angleError) && Math.abs(angleStep) >= Math.abs(angleError)) {
      this.headingAngle = targetAngle;
      this.angularVelocity = 0;
      this.angularAcceleration = 0;
    } else {
      this.headingAngle = wrapAngle(this.headingAngle + angleStep);
    }
    this.heading.x = Math.cos(this.headingAngle);
    this.heading.y = Math.sin(this.headingAngle);

    const remainingTurnError = Math.abs(wrapAngle(targetAngle - this.headingAngle));
    if (turning) {
      const resolvedProgress = clamp(
        1 - remainingTurnError / Math.max(1e-5, this.turnStartError),
        0,
        1,
      );
      this.turnProgress = Math.max(this.turnProgress, resolvedProgress);
    }
    const targetTurnEnvelope = turning ? Math.sin(Math.PI * this.turnProgress) : 0;
    this.turnEnvelope += (targetTurnEnvelope - this.turnEnvelope)
      * (1 - Math.exp(-10 * dt));
    const turnSettled = turning
      && remainingTurnError < Math.PI / 90
      && Math.abs(this.angularVelocity) < 0.15;
    this.turnWasActive = turning;

    const tangentX = -this.heading.y;
    const tangentY = this.heading.x;
    const forwardVelocity = this.velocity.x * this.heading.x + this.velocity.y * this.heading.y;
    const lateralVelocity = this.velocity.x * tangentX + this.velocity.y * tangentY;

    // Jet momentum is an external force. The -ma_dot*v term recovers momentum
    // while the mantle shrinks and takes it back during refill; both are divided
    // by body plus added mass below. No jet fires while braking or turning.
    const canThrust = !braking && !turning;
    const jetMomentumForce = canThrust
      ? jet * (1.05 + demand * 1.44) * thrustMultiplier
      : 0;
    const addedMassForce = clamp(-addedMassRate * forwardVelocity, -0.58, 0.58);
    const jetAcceleration = (jetMomentumForce + addedMassForce) / this.effectiveMass;

    // Anisotropic quadratic drag: the streamlined forward bundle coasts, while
    // sideways motion and an open/refilling crown are damped much more heavily.
    const parallelDrag = 0.40 + intake * 1.15 + (1 - squeeze) * 0.18 - glide * 0.13;
    const lateralDrag = 2.8 + intake * 2.6
      + (braking ? 5.8 : turning ? 3.2 : 0);
    const linearDrag = 0.34 + intake * 0.72
      + (braking ? 4.8 : turning ? 1.85 : 0);

    const dragForward = -parallelDrag * forwardVelocity * Math.abs(forwardVelocity);
    const dragLateral = -lateralDrag * lateralVelocity * Math.abs(lateralVelocity);
    const axialForce = jetMomentumForce + addedMassForce + dragForward;
    this.acceleration.x = (this.heading.x * axialForce
      + tangentX * dragLateral
      - this.velocity.x * linearDrag) / this.effectiveMass;
    this.acceleration.y = (this.heading.y * axialForce
      + tangentY * dragLateral
      - this.velocity.y * linearDrag) / this.effectiveMass;

    this.velocity.x += this.acceleration.x * dt;
    this.velocity.y += this.acceleration.y * dt;
    const speed = Math.hypot(this.velocity.x, this.velocity.y);
    if (speed > this.maxSpeed) {
      const scale = this.maxSpeed / speed;
      this.velocity.x *= scale;
      this.velocity.y *= scale;
    }

    this.offset.x += this.velocity.x * dt;
    this.offset.y += this.velocity.y * dt;

    // The website composition frame clips projected displacement, but it is
    // not a wall in the water. Preserve water-relative momentum at the limit so
    // later strokes still accelerate and deform the animal instead of freezing.
    const ellipse = (this.offset.x / this.maxOffsetX) ** 2
      + (this.offset.y / this.maxOffsetY) ** 2;
    if (ellipse > 1) {
      const scale = 1 / Math.sqrt(ellipse);
      this.offset.x *= scale;
      this.offset.y *= scale;
    }

    this.telemetry = {
      forwardVelocity,
      lateralVelocity,
      speed: Math.hypot(this.velocity.x, this.velocity.y),
      jetAcceleration,
      jetMomentumForce,
      addedMassForce,
      addedMass: this.addedMass,
      effectiveMass: this.effectiveMass,
      addedMassRate,
      squeezeRate,
      headingAngle: this.headingAngle,
      angularVelocity: this.angularVelocity,
      angularAcceleration: this.angularAcceleration,
      angularInertia,
      controlTorque,
      remainingTurnError,
      turnProgress: this.turnProgress,
      turnEnvelope: this.turnEnvelope,
      turnSettled,
      offsetX: this.offset.x,
      offsetY: this.offset.y,
    };
    return this;
  }
}
