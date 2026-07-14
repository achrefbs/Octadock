(function installDirectionTransition(root) {
  'use strict';

  function normalizeDirection(value) {
    if (value > 0) return 1;
    if (value < 0) return -1;
    return 0;
  }

  function sampleDirectionIntent({
    intentPx = 0,
    deltaPx = 0,
    deltaTime = 1 / 60,
    viewportHeight = 720,
  } = {}) {
    const dt = Math.max(0, Number.isFinite(deltaTime) ? deltaTime : 1 / 60);
    const height = Math.max(1, Number.isFinite(viewportHeight) ? viewportHeight : 720);
    const previous = Number.isFinite(intentPx) ? intentPx : 0;
    const delta = Number.isFinite(deltaPx) ? deltaPx : 0;
    const value = previous * Math.exp(-dt / 0.09) + delta;
    const thresholdPx = Math.max(10, Math.min(24, height * 0.018));
    return {
      intentPx: value,
      thresholdPx,
      direction: normalizeDirection(value),
      accepted: Math.abs(value) >= thresholdPx,
    };
  }

  /**
   * Resolve one real scroll gesture without mutating locomotion state.
   *
   * `dir` remains the committed direction until a turn finishes, while
   * `queuedDir` is the direction currently being prepared. During a redirect
   * the latest gesture must therefore be compared with both values: matching
   * `dir` is a cancellation, not a no-op.
   */
  function resolveDirectionTransition(snapshot, requestedDirection) {
    const direction = normalizeDirection(requestedDirection);
    const state = snapshot?.state || 'idle';
    const currentDirection = normalizeDirection(snapshot?.dir) || 1;
    const queuedDirection = normalizeDirection(snapshot?.queuedDir);

    if (!direction) return { action: 'none', direction: 0 };

    if (state === 'brake') {
      if (direction === currentDirection) {
        return { action: 'abort-brake', direction };
      }
      if (direction === queuedDirection) {
        return { action: 'keep-redirect', direction };
      }
      return { action: 'retarget-brake', direction };
    }

    if (state === 'turn') {
      if (direction === queuedDirection) {
        return { action: 'keep-redirect', direction };
      }
      return { action: 'retarget-turn', direction };
    }

    if (direction === currentDirection) {
      return { action: 'none', direction };
    }
    return { action: 'start-brake', direction };
  }

  root.OctadockDirectionTransition = Object.freeze({
    normalizeDirection,
    sampleDirectionIntent,
    resolveDirectionTransition,
  });
}(typeof globalThis === 'object' ? globalThis : window));
