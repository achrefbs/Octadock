import * as THREE from 'three';
import { Octopus } from './Octopus.js?v=22';
import { RoamingOctopusSystem } from './RoamingOctopusSystem.js?v=3';
import { sampleCameraDepthBias } from './math.js?v=21';

const overlayCanvas = document.querySelector('#octopus-v10');
const journeyCanvas = document.querySelector('#gl');
const statusCanvas = journeyCanvas || overlayCanvas;
const externalEnabled = window.__OCTOPUS_V10_EXTERNAL !== false
  && window.location.protocol !== 'file:';

if (!statusCanvas || !externalEnabled) {
  // Direct-file previews retain the inline fallback. Hosted pages use V10.
} else {
  statusCanvas.dataset.v10Status = 'module';
  start().catch((error) => {
    statusCanvas.classList.remove('is-ready');
    statusCanvas.dataset.v10Status = 'error';
    statusCanvas.dataset.v10Error = String(error?.message || error);
    window.__OCTOPUS_V10__ = { status: 'error', error };
    console.error('[octopus-v10] renderer failed', error);
  });
}

function clamp(value, min = 0, max = 1) {
  return Math.min(max, Math.max(min, value));
}

function waitForBridge(timeoutMs = 8000) {
  return new Promise((resolve, reject) => {
    const started = performance.now();
    function poll() {
      const bridge = window.__OCTOPUS_JOURNEY__;
      if (bridge?.enabled && bridge.revision > 0) {
        resolve(bridge);
        return;
      }
      if (performance.now() - started > timeoutMs) {
        reject(new Error('The journey did not publish a V10 frame.'));
        return;
      }
      if (document.hidden) setTimeout(poll, 40);
      else requestAnimationFrame(poll);
    }
    poll();
  });
}

function findCrownAnchor(octopus) {
  octopus.updateMatrixWorld(true);
  const crownWorld = new THREE.Vector3();
  const sample = new THREE.Vector3();
  let roots = 0;

  for (let arm = 1; arm <= 8; arm += 1) {
    const root = octopus.model?.getObjectByName(`Arm_${String(arm).padStart(2, '0')}_00`);
    if (!root) continue;
    root.getWorldPosition(sample);
    crownWorld.add(sample);
    roots += 1;
  }

  if (roots > 0) crownWorld.multiplyScalar(1 / roots);
  else {
    const body = octopus.model?.getObjectByName('Body');
    if (body) body.getWorldPosition(crownWorld);
    else octopus.getWorldPosition(crownWorld);
  }

  return octopus.worldToLocal(crownWorld.clone());
}

function enforceExternalRoot(octopus) {
  // Octopus.simulate retains a tiny lab-only jet lift on modelPivot. The
  // website root controller owns attitude and displacement completely.
  octopus.visualTilt = 0;
  octopus.visualLift = 0;
  octopus.modelPivot.position.set(0, 0, 0);
  octopus.modelPivot.rotation.set(0, 0, 0);
}

function configureRenderer(renderer, shared) {
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1.04;
  renderer.autoClear = !shared;
  if (!shared) renderer.setClearColor(0x000000, 0);
}

function makeRenderer({ shared }) {
  if (shared) {
    if (!journeyCanvas) throw new Error('The journey canvas is unavailable.');
    const context = journeyCanvas.getContext('webgl2');
    if (!context) throw new Error('The journey WebGL2 context is unavailable.');
    const renderer = new THREE.WebGLRenderer({
      canvas: journeyCanvas,
      context,
      alpha: false,
      antialias: false,
      powerPreference: 'high-performance',
    });
    configureRenderer(renderer, true);
    return { renderer, context, canvas: journeyCanvas, shared: true };
  }

  const renderer = new THREE.WebGLRenderer({
    canvas: overlayCanvas,
    alpha: true,
    antialias: true,
    premultipliedAlpha: true,
    powerPreference: 'high-performance',
  });
  configureRenderer(renderer, false);
  return { renderer, context: renderer.getContext(), canvas: overlayCanvas, shared: false };
}

async function start() {
  let lastJourneyRenderAt = -Infinity;
  let lastJourneyBeforePostAt = -Infinity;
  let renderFromJourney = null;
  let renderBeforePost = null;
  const noteJourneyRender = () => {
    lastJourneyRenderAt = performance.now();
    if (renderFromJourney) renderFromJourney(lastJourneyRenderAt);
  };
  const noteJourneyBeforePost = () => {
    lastJourneyBeforePostAt = performance.now();
    if (renderBeforePost) renderBeforePost(lastJourneyBeforePostAt);
  };
  journeyCanvas?.addEventListener('octadock:journey-before-post', noteJourneyBeforePost);
  journeyCanvas?.addEventListener('octadock:journey-rendered', noteJourneyRender);

  const bridge = await waitForBridge();
  statusCanvas.dataset.v10Status = 'bridge';

  const scene = new THREE.Scene();
  const camera = new THREE.Camera();
  camera.matrixAutoUpdate = false;
  scene.add(camera);

  const fog = new THREE.FogExp2(0x05080f, bridge.environment.density);
  scene.fog = fog;
  // MeshBasic wire/underlay materials use the site's own teal values directly;
  // octopus-only key, warm and rim lights would reintroduce a foreign palette.

  const system = new RoamingOctopusSystem({
    seed: 0x0c7ad0c,
    aspect: bridge.viewport.cssWidth / Math.max(1, bridge.viewport.cssHeight),
  });
  const rigs = system.agents.map((agent) => {
    const frame = new THREE.Group();
    frame.name = `Camera-relative roaming frame: ${agent.id}`;
    scene.add(frame);
    const octopus = new Octopus();
    frame.add(octopus);
    return {
      agent,
      frame,
      octopus,
      longestSide: 1,
      crownAnchor: new THREE.Vector3(),
      ndc: new THREE.Vector2(),
      screenHeading: new THREE.Vector2(agent.controller.heading.x, agent.controller.heading.y),
    };
  });
  statusCanvas.dataset.v10Status = 'model';
  await Promise.all(rigs.map(({ octopus }) => octopus.ready));

  // Parsing the rigged proxy can still occupy the main thread for more than
  // longer than one frame. Wait for the journey to resume before deciding
  // whether its synchronous composite hook exists; otherwise a valid shared
  // compositor is misclassified as the legacy direct-context fallback.
  if (performance.now() - lastJourneyBeforePostAt > 250) {
    await new Promise((resolve) => {
      let settled = false;
      const finish = () => {
        if (settled) return;
        settled = true;
        clearTimeout(timeout);
        journeyCanvas?.removeEventListener('octadock:journey-before-post', finish);
        resolve();
      };
      const timeout = setTimeout(finish, 1200);
      journeyCanvas?.addEventListener('octadock:journey-before-post', finish, { once: true });
    });
  }

  // Every animal owns an independent skeleton and hydrostat solver. They share
  // only the renderer and camera, so social contact cannot fuse their rigs.
  rigs.forEach((rig) => {
    rig.octopus.updateMatrixWorld(true);
    const bounds = new THREE.Box3().setFromObject(rig.octopus);
    const size = bounds.getSize(new THREE.Vector3());
    rig.longestSide = Math.max(size.x, size.y, size.z);
    rig.crownAnchor.copy(findCrownAnchor(rig.octopus));
    rig.octopus.position.copy(rig.crownAnchor).multiplyScalar(-1);
  });

  // A journey-owned render event guarantees that Three draws after the custom
  // renderer on the same #gl context. Without that ordering contract, retain
  // the isolated transparent canvas rather than racing two render loops.
  let renderTarget;
  const journeyEventAvailable = performance.now() - lastJourneyRenderAt < 250;
  if (journeyEventAvailable || !overlayCanvas) {
    try {
      renderTarget = makeRenderer({ shared: true });
    } catch (error) {
      console.warn('[octopus-v10] shared journey context unavailable; using overlay', error);
      renderTarget = makeRenderer({ shared: false });
    }
  } else {
    renderTarget = makeRenderer({ shared: false });
  }
  const { renderer } = renderTarget;
  // The bridge capability is the contract. A background tab may suspend the
  // journey while this larger GLB parses, so recency of the last event is not
  // evidence that the hook is absent. Keeping the target ready here guarantees
  // the same shared ACES grade when the tab becomes visible.
  const compositeTarget = renderTarget.shared
    && bridge.composite?.supported
    ? new THREE.WebGLRenderTarget(1, 1, {
      format: THREE.RGBAFormat,
      type: THREE.UnsignedByteType,
      depthBuffer: true,
      stencilBuffer: false,
    })
    : null;
  if (compositeTarget) {
    compositeTarget.texture.name = 'Octopus linear journey composite';
    compositeTarget.texture.colorSpace = THREE.NoColorSpace;
    compositeTarget.samples = Math.min(4, renderer.capabilities.maxSamples || 0);
    renderer.outputColorSpace = THREE.LinearSRGBColorSpace;
    renderer.toneMapping = THREE.NoToneMapping;
    renderer.toneMappingExposure = 1;
    renderer.setClearColor(0x000000, 0);
    bridge.composite.ready = false;
  }

  const diagnostics = {
    status: 'loading',
    bridge,
    scene,
    camera,
    renderer,
    renderMode: compositeTarget
      ? 'journey-composite'
      : renderTarget.shared ? 'journey-context' : 'overlay',
    system,
    rigs,
    agents: system.agents,
    state: system.snapshot(),
    fixedSteps: 0,
  };
  window.__OCTOPUS_V10__ = diagnostics;
  window.__OCTOPUS_ROAM__ = diagnostics;

  let simulationTime = bridge.time;
  const fixedStep = 1 / 120;
  rigs.forEach((rig) => {
    for (let index = 0; index < 90; index += 1) {
      simulationTime += fixedStep;
      rig.octopus.simulate(fixedStep, simulationTime, rig.agent.motionState);
      enforceExternalRoot(rig.octopus);
    }
    rig.octopus.updateVisuals(rig.agent.motionState);
  });

  const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const cameraRight = new THREE.Vector3();
  const cameraUp = new THREE.Vector3();
  const cameraBack = new THREE.Vector3();
  const cameraForward = new THREE.Vector3();
  const planePoint = new THREE.Vector3();
  const rayPoint = new THREE.Vector3();
  const rayDirection = new THREE.Vector3();
  const planeOffset = new THREE.Vector3();
  const headWorld = new THREE.Vector3();
  const faceWorld = new THREE.Vector3();
  const sideWorld = new THREE.Vector3();
  const basis = new THREE.Matrix4();
  const targetAttitude = new THREE.Quaternion();
  const turnBankQuaternion = new THREE.Quaternion();
  let previousTime = performance.now();
  let accumulator = 0;
  let width = 0;
  let height = 0;
  let pixelRatio = 0;
  let renderedFrames = 0;

  function applyCamera() {
    camera.projectionMatrix.fromArray(bridge.camera.projection);
    camera.projectionMatrixInverse.copy(camera.projectionMatrix).invert();
    camera.matrixWorldInverse.fromArray(bridge.camera.view);
    camera.matrixWorld.copy(camera.matrixWorldInverse).invert();
    camera.matrix.copy(camera.matrixWorld);
    camera.matrix.decompose(camera.position, camera.quaternion, camera.scale);
    camera.matrixWorldNeedsUpdate = false;
  }

  function applySize() {
    if (renderTarget.shared) {
      const nextWidth = Math.max(1, journeyCanvas.width);
      const nextHeight = Math.max(1, journeyCanvas.height);
      if (nextWidth === width && nextHeight === height) return;
      width = nextWidth;
      height = nextHeight;
      pixelRatio = 1;
      // Never call setSize/setPixelRatio on the shared canvas: assigning the
      // canvas width, even to the same number, clears the journey framebuffer.
      renderer.setViewport(0, 0, width, height);
      compositeTarget?.setSize(width, height);
      return;
    }

    const nextWidth = Math.max(1, bridge.viewport.cssWidth || innerWidth);
    const nextHeight = Math.max(1, bridge.viewport.cssHeight || innerHeight);
    const nextPixelRatio = Math.min(1.7, bridge.viewport.dpr || devicePixelRatio || 1);
    if (nextWidth === width && nextHeight === height && nextPixelRatio === pixelRatio) return;
    width = nextWidth;
    height = nextHeight;
    pixelRatio = nextPixelRatio;
    renderer.setPixelRatio(pixelRatio);
    renderer.setSize(width, height, false);
  }

  function applyPlacement(rig) {
    const { agent, frame } = rig;
    const controller = agent.controller;
    const ndcX = controller.offset.x;
    const ndcY = controller.offset.y;
    rig.ndc.set(ndcX, ndcY);
    // Controller headings are NDC vectors; camera-plane X spans `aspect` times
    // more world space. Correcting X here makes the visible mantle axis line up
    // exactly with the path measured in CSS pixels.
    rig.screenHeading.set(controller.heading.x * system.aspect, controller.heading.y).normalize();

    // The journey still owns the camera and underwater grade. The autonomous
    // agents own absolute screen-space positions on one camera-facing water
    // plane, so scrolling cannot redirect or shear their trajectories.
    planePoint.fromArray(bridge.creature.anchorPosition);
    cameraForward.set(0, 0, -1).applyQuaternion(camera.quaternion).normalize();
    const planeDepth = planeOffset.copy(planePoint).sub(camera.position).dot(cameraForward);
    rayPoint.set(ndcX, ndcY, 0).unproject(camera);
    rayDirection.copy(rayPoint).sub(camera.position).normalize();
    const denominator = rayDirection.dot(cameraForward);
    const distance = Math.abs(denominator) > 1e-6 ? planeDepth / denominator : -1;
    if (distance > 0 && Number.isFinite(distance)) {
      frame.position.copy(camera.position).addScaledVector(rayDirection, distance);
    } else {
      frame.position.fromArray(bridge.creature.position);
    }

    // Rig semantics: local +Y is mantle-first and local +Z is the facial side.
    // Fast threat turns gain a small out-of-plane arc; ordinary wandering stays
    // almost flat to the camera and reads as slow, deliberate observation.
    cameraRight.setFromMatrixColumn(camera.matrixWorld, 0).normalize();
    cameraUp.setFromMatrixColumn(camera.matrixWorld, 1).normalize();
    cameraBack.setFromMatrixColumn(camera.matrixWorld, 2).normalize();
    const turnEnvelope = clamp(controller.telemetry.turnEnvelope || 0);
    const turnSign = Math.sign(controller.angularVelocity) || agent.turnSide || 1;
    const fleeGain = agent.behavior === 'flee' ? 1 : 0.42;
    const turnDepth = turnEnvelope * 0.24 * turnSign * fleeGain;
    const turnBank = clamp(controller.angularVelocity / 8, -1, 1) * 0.15 * fleeGain;
    headWorld.copy(cameraRight).multiplyScalar(rig.screenHeading.x)
      .addScaledVector(cameraUp, rig.screenHeading.y)
      .addScaledVector(cameraBack, sampleCameraDepthBias(rig.screenHeading.y, turnDepth))
      .normalize();
    faceWorld.copy(cameraBack)
      .addScaledVector(headWorld, -cameraBack.dot(headWorld))
      .normalize();
    if (Math.abs(turnBank) > 1e-5) {
      turnBankQuaternion.setFromAxisAngle(headWorld, turnBank);
      faceWorld.applyQuaternion(turnBankQuaternion).normalize();
    }
    sideWorld.crossVectors(headWorld, faceWorld).normalize();
    faceWorld.crossVectors(sideWorld, headWorld).normalize();
    basis.makeBasis(sideWorld, headWorld, faceWorld);
    targetAttitude.setFromRotationMatrix(basis);
    frame.quaternion.copy(targetAttitude);

    // Projection-derived sizing keeps both animals the same apparent size as
    // the website camera dives. It replaces the old giant authored scale.
    const projectionY = Math.max(1e-5, Math.abs(camera.projectionMatrix.elements[5]));
    const mobileGain = (bridge.viewport.cssWidth || innerWidth) <= 760 ? 0.88 : 1;
    const rootScale = agent.apparentSpan * mobileGain * Math.max(0.5, planeDepth)
      / (projectionY * Math.max(1e-5, rig.longestSide));
    frame.scale.setScalar(rootScale);
  }

  function render(now = performance.now()) {
    const deltaTime = Math.min(0.05, Math.max(0, (now - previousTime) / 1000));
    previousTime = now;
    applySize();
    applyCamera();

    system.setAspect(
      (bridge.viewport.cssWidth || innerWidth)
      / Math.max(1, bridge.viewport.cssHeight || innerHeight),
    );
    if (!reducedMotion) {
      accumulator = Math.min(accumulator + deltaTime, fixedStep * 7);
      let steps = 0;
      while (accumulator >= fixedStep && steps < 7) {
        system.update(fixedStep);
        simulationTime += fixedStep;
        rigs.forEach((rig) => {
          rig.octopus.simulate(fixedStep, simulationTime, rig.agent.motionState);
          enforceExternalRoot(rig.octopus);
        });
        accumulator -= fixedStep;
        steps += 1;
        diagnostics.fixedSteps += 1;
      }
    }

    // Journey scroll transitions no longer drive either animal, but the legacy
    // camera track still expects neutral physical feedback to leave its own
    // brake/turn bookkeeping without waiting for a timeout.
    const locomotionFeedback = bridge.locomotion;
    const neutralDirection = locomotionFeedback.state === 'turn'
      ? (locomotionFeedback.queuedDir || locomotionFeedback.dir || -1)
      : (locomotionFeedback.dir || -1);
    locomotionFeedback.physicalSpeed = 0;
    locomotionFeedback.physicalHeadingX = 0;
    locomotionFeedback.physicalHeadingY = -neutralDirection;
    locomotionFeedback.physicalAngularVelocity = 0;
    locomotionFeedback.physicalTurnProgress = 1;
    locomotionFeedback.physicalTurnEnvelope = 0;
    locomotionFeedback.physicalTurnSettled = true;

    rigs.forEach((rig) => {
      applyPlacement(rig);
      rig.octopus.updateVisuals(rig.agent.motionState);
      const activityGain = rig.agent.behavior === 'flee' ? 1.08 : 1;
      rig.octopus.skinMaterial.opacity *= rig.agent.opacity * activityGain;
      rig.octopus.underlayMaterial.opacity *= rig.agent.opacity;
      const other = system.agents[1 - rig.agent.index];
      const pointerDistance = Math.hypot(
        (system.pointer.x - rig.agent.controller.offset.x) * system.aspect,
        system.pointer.y - rig.agent.controller.offset.y,
      );
      const awareOfPointer = system.pointer.active
        && pointerDistance < rig.agent.reactionRadius * 2.25;
      const social = rig.agent.behavior === 'meet' || rig.agent.behavior === 'inspect';
      const gazeTarget = awareOfPointer
        ? system.pointer
        : social ? other.controller.offset : rig.agent.target;
      rig.octopus.setGaze(
        clamp((gazeTarget.x - rig.agent.controller.offset.x) * 2.2, -1, 1),
        clamp((gazeTarget.y - rig.agent.controller.offset.y) * 2.2, -1, 1),
      );
    });

    diagnostics.state = system.snapshot();
    diagnostics.status = 'ready';
    // DOM-readable telemetry keeps browser QA deterministic even when the
    // page execution realm deliberately hides mutable window globals.
    const primary = rigs[0];
    statusCanvas.dataset.v10Visible = 'true';
    statusCanvas.dataset.v10Mode = 'autonomous-roam';
    statusCanvas.dataset.v10AgentCount = String(rigs.length);
    statusCanvas.dataset.v10State = primary.agent.behavior;
    statusCanvas.dataset.v10Ndc = `${primary.ndc.x.toFixed(3)},${primary.ndc.y.toFixed(3)}`;
    statusCanvas.dataset.v10PhysicalOffset = `${primary.agent.controller.offset.x.toFixed(4)},${primary.agent.controller.offset.y.toFixed(4)}`;
    statusCanvas.dataset.v10Scale = primary.frame.scale.x.toFixed(4);
    statusCanvas.dataset.v10Heading = `${primary.screenHeading.x.toFixed(3)},${primary.screenHeading.y.toFixed(3)}`;
    statusCanvas.dataset.v10Speed = primary.agent.controller.telemetry.speed?.toFixed(4) || '0.0000';
    statusCanvas.dataset.v10Style = 'autonomous-teal-wireframe';
    statusCanvas.dataset.v10MinDistance = system.minimumDistance.toFixed(4);
    statusCanvas.dataset.v10Interaction = system.lastInteraction
      ? `${system.lastInteraction.agentId}:flee-${system.lastInteraction.fleeEpisode}`
      : 'none';
    statusCanvas.dataset.v10World = `${primary.frame.position.x.toFixed(2)},${primary.frame.position.y.toFixed(2)},${primary.frame.position.z.toFixed(2)}`;
    statusCanvas.dataset.v10Camera = `${camera.position.x.toFixed(2)},${camera.position.y.toFixed(2)},${camera.position.z.toFixed(2)}`;
    rigs.forEach((rig, index) => {
      const prefix = `v10Agent${index}`;
      statusCanvas.dataset[`${prefix}Id`] = rig.agent.id;
      statusCanvas.dataset[`${prefix}State`] = rig.agent.behavior;
      statusCanvas.dataset[`${prefix}Stage`] = rig.agent.fleeStage;
      statusCanvas.dataset[`${prefix}Ndc`] = `${rig.ndc.x.toFixed(4)},${rig.ndc.y.toFixed(4)}`;
      statusCanvas.dataset[`${prefix}Heading`] = `${rig.screenHeading.x.toFixed(4)},${rig.screenHeading.y.toFixed(4)}`;
      statusCanvas.dataset[`${prefix}Speed`] = (rig.agent.controller.telemetry.speed || 0).toFixed(4);
      statusCanvas.dataset[`${prefix}Scale`] = rig.frame.scale.x.toFixed(4);
      statusCanvas.dataset[`${prefix}FleeEpisode`] = String(rig.agent.fleeEpisode);
      statusCanvas.dataset[`${prefix}Jet`] = (rig.agent.jetCycle?.jet || 0).toFixed(4);
    });

    if (renderTarget.shared) {
      renderer.resetState();
      if (compositeTarget) {
        // Render linear RGBA into an isolated target. The journey resumes this
        // same synchronous event, alpha-composes the raw texture into rtScene,
        // then applies particles, bloom and one shared ACES grade.
        renderer.setRenderTarget(compositeTarget);
        renderer.setViewport(0, 0, width, height);
        renderer.setScissorTest(false);
        renderer.clear(true, true, false);
        renderer.render(scene, camera);
        renderer.setRenderTarget(null); // resolves multisampling before raw GL reads it
        const properties = renderer.properties.get(compositeTarget.texture);
        bridge.composite.texture = properties.__webglTexture || null;
        bridge.composite.width = width;
        bridge.composite.height = height;
        bridge.composite.ready = Boolean(bridge.composite.texture);
      } else {
        // Fallback for engines without the pre-post hook: draw after the
        // journey's full-screen composite without clearing its color.
        renderer.setRenderTarget(null);
        renderer.setViewport(0, 0, width, height);
        renderer.setScissorTest(false);
        renderer.clearDepth();
        renderer.render(scene, camera);
      }
      renderedFrames += 1;
      statusCanvas.dataset.v10Frames = String(renderedFrames);
      statusCanvas.dataset.v10Draws = String(renderer.info.render.calls);
      statusCanvas.dataset.v10Triangles = String(renderer.info.render.triangles);
      statusCanvas.dataset.v10GlError = String(renderTarget.context.getError());
      statusCanvas.dataset.v10Composite = String(Boolean(compositeTarget && bridge.composite.ready));
      renderer.resetState();
      renderTarget.context.bindVertexArray(null);
      renderTarget.context.bindFramebuffer(renderTarget.context.FRAMEBUFFER, null);
      renderTarget.context.useProgram(null);
    } else {
      renderer.render(scene, camera);
    }
  }

  diagnostics.status = 'ready';
  diagnostics.state = system.snapshot();
  statusCanvas.dataset.v10Status = 'ready';
  statusCanvas.dataset.v10RenderMode = diagnostics.renderMode;
  if (renderTarget.shared) statusCanvas.classList.remove('is-ready');
  else statusCanvas.classList.add('is-ready');

  function queuePointer(event, pressed = false) {
    system.setPointer(
      event.clientX / Math.max(1, innerWidth) * 2 - 1,
      1 - event.clientY / Math.max(1, innerHeight) * 2,
      {
        pressed,
        pointerType: event.pointerType,
        eventId: event.timeStamp,
      },
    );
    if (reducedMotion && typeof bridge.requestFrame === 'function') bridge.requestFrame();
  }
  addEventListener('pointermove', (event) => queuePointer(event), { passive: true });
  addEventListener('pointerdown', (event) => queuePointer(event, true), { passive: true });
  addEventListener('pointerup', (event) => {
    if (event.pointerType !== 'mouse') system.clearPointer();
  }, { passive: true });
  addEventListener('pointercancel', () => system.clearPointer(), { passive: true });
  addEventListener('mouseout', (event) => {
    if (!event.relatedTarget) system.clearPointer();
  }, { passive: true });
  addEventListener('blur', () => system.clearPointer(), { passive: true });

  render();
  if (compositeTarget) {
    renderBeforePost = render;
  } else if (renderTarget.shared) {
    renderFromJourney = render;
  } else if (reducedMotion) {
    addEventListener('resize', () => requestAnimationFrame(render), { passive: true });
  } else {
    renderer.setAnimationLoop(render);
    setInterval(() => {
      if (document.hidden) render(performance.now());
    }, 50);
  }
  if ((reducedMotion || new URLSearchParams(location.search).has('freeze'))
    && typeof bridge.requestFrame === 'function') {
    queueMicrotask(() => bridge.requestFrame());
  }

}
