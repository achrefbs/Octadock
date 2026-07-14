import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { damp, smoothstep } from './math.js';
import { HydrostatMotion } from './HydrostatMotion.js?v=20';

// Keep a revision token on generated rigs. Vite serves its HTML fallback for a
// missing public asset with status 200, which browsers may otherwise cache and
// later hand to GLTFLoader as if it were the binary model.
const DEFAULT_MODEL_URL = new URL('./octopus-wire-v13.glb', import.meta.url).href;
const REST_COLOR = new THREE.Color(0x2dd4bf);
const ACTIVE_COLOR = new THREE.Color(0x5eead4);
const colorTarget = new THREE.Color();

function makeLivingSkinMaterial() {
  return new THREE.MeshBasicMaterial({
    color: REST_COLOR,
    wireframe: true,
    transparent: true,
    opacity: 0.24,
    depthTest: true,
    depthWrite: false,
    side: THREE.FrontSide,
    blending: THREE.NormalBlending,
    fog: true,
    toneMapped: false,
  });
}

function makeDetailMaterials() {
  const hidden = () => new THREE.MeshBasicMaterial({
    color: 0x2dd4bf,
    wireframe: true,
    transparent: true,
    opacity: 0,
    depthWrite: false,
    toneMapped: false,
  });
  return {
    sucker: hidden(),
    eye: hidden(),
    iris: hidden(),
    pupil: hidden(),
    siphonOpening: hidden(),
  };
}

export class Octopus extends THREE.Group {
  constructor({ modelUrl = DEFAULT_MODEL_URL, scale = 0.60 } = {}) {
    super();
    if (!Number.isFinite(scale) || scale <= 0) {
      throw new TypeError('Octopus scale must be a positive finite number.');
    }
    this.name = 'RiggedOctopus';
    this.modelUrl = String(modelUrl);
    this.loaded = false;
    this.loadError = null;
    this.debug = false;
    this.currentActionName = '';
    this.visualTilt = 0;
    this.visualLift = 0;
    this.gaze = new THREE.Vector2();
    this.gazeSmoothed = new THREE.Vector2();
    this.pupils = [];
    this.pupilRestPositions = [];
    this.pickables = [];
    this.suckerMeshes = [];

    this.skinMaterial = makeLivingSkinMaterial();
    this.underlayMaterial = new THREE.MeshBasicMaterial({
      color: 0x06302c,
      transparent: true,
      opacity: 0.045,
      depthTest: true,
      depthWrite: true,
      side: THREE.FrontSide,
      polygonOffset: true,
      polygonOffsetFactor: 1,
      polygonOffsetUnits: 1,
      fog: true,
      toneMapped: false,
    });
    this.detailMaterials = makeDetailMaterials();
    this.modelPivot = new THREE.Group();
    this.modelPivot.name = 'Swim attitude pivot';
    this.modelPivot.scale.setScalar(scale);
    // Model +Y is the mantle-first axis and model -Y is the arm-trailing axis.
    // Presentation attitude belongs to the root controller, not this pivot.
    this.modelPivot.rotation.y = 0;
    this.add(this.modelPivot);

    this.ready = this.loadModel();
  }

  async loadModel() {
    try {
      const loader = new GLTFLoader();
      const gltf = await loader.loadAsync(this.modelUrl);
      this.model = gltf.scene;
      this.model.name = 'Continuous octopus model';
      this.modelPivot.add(this.model);

      this.model.traverse((object) => {
        const name = object.name.toLowerCase();
        if (!object.isMesh) return;
        object.castShadow = false;
        object.receiveShadow = false;
        this.pickables.push(object);

        if (name.includes('all_suckers') || name.includes('all suckers')) {
          object.material = this.detailMaterials.sucker;
          object.castShadow = false;
          this.suckerMeshes.push(object);
        } else if (name.includes('pupil')) {
          object.material = this.detailMaterials.pupil;
          this.pupils.push(object);
          this.pupilRestPositions.push(object.position.clone());
        } else if (name.includes('iris')) {
          object.material = this.detailMaterials.iris;
        } else if (name.includes('eye_globe') || name.includes('eye globe')) {
          object.material = this.detailMaterials.eye;
        } else if (name.includes('siphon_opening') || name.includes('siphon opening')) {
          object.material = this.detailMaterials.siphonOpening;
        } else if (name.includes('octopus_continuous_body')
          || name.includes('octopus continuous body')
          || name === 'siphon') {
          // Render the same skinned surface twice: a nearly transparent deep
          // teal occluder keeps rear edges from becoming noise, then the site-
          // teal wire pass supplies the visible technical specimen.
          const count = object.geometry.index?.count ?? object.geometry.attributes.position.count;
          object.geometry.clearGroups();
          object.geometry.addGroup(0, count, 0);
          object.geometry.addGroup(0, count, 1);
          object.material = [this.underlayMaterial, this.skinMaterial];
        }
      });

      this.mixer = new THREE.AnimationMixer(this.model);
      this.actions = new Map();
      gltf.animations.forEach((clip) => {
        const action = this.mixer.clipAction(clip);
        action.setLoop(THREE.LoopRepeat, Infinity);
        action.clampWhenFinished = false;
        this.actions.set(clip.name, action);
      });

      // The GLB clips remain available as authored reference poses, but the
      // runtime is driven joint-by-joint. This prevents five-keyframe loops from
      // dictating the animal's movement and lets physics and deformation share
      // the same propulsion cycle.
      this.mixer.stopAllAction();
      this.motion = new HydrostatMotion(this.model);
      this.currentActionName = 'Idle_Breathe';
      this.loaded = true;
      this.dispatchEvent({ type: 'ready', clips: [...this.actions.keys()] });
      return this;
    } catch (error) {
      this.loadError = error;
      console.error('Octopus model failed to load', error);
      throw error;
    }
  }

  findAction(preferredName) {
    if (this.actions?.has(preferredName)) return this.actions.get(preferredName);
    const prefix = preferredName.split('_')[0].toLowerCase();
    return [...(this.actions?.entries() ?? [])]
      .find(([name]) => name.toLowerCase().includes(prefix))?.[1] ?? null;
  }

  playAction(name, fadeDuration = 0.72) {
    if (!this.mixer || name === this.currentActionName) return;
    const next = this.findAction(name);
    if (!next) return;
    next.enabled = true;
    next.reset();
    next.setEffectiveWeight(1);
    next.play();
    if (this.currentAction) this.currentAction.crossFadeTo(next, fadeDuration, true);
    this.currentAction = next;
    this.currentActionName = name;
  }

  setGaze(normalizedX, normalizedY) {
    this.gaze.set(
      THREE.MathUtils.clamp(normalizedX, -1, 1),
      THREE.MathUtils.clamp(normalizedY, -1, 1),
    );
  }

  simulate(deltaTime, time, state) {
    if (!this.loaded) return;

    const moving = state.navigationSpeed > 0.035 || Math.abs(state.thrust) > 0.04;
    const desiredAction = moving || state.mode === 'swim'
      ? 'Jet_Swim'
      : state.mode === 'explore'
        ? 'Crawl_Explore'
        : 'Idle_Breathe';
    this.currentActionName = desiredAction;
    this.motion.update(deltaTime, state);

    // A slow forward attitude change makes translation read as mantle-first
    // propulsion. It is deliberately damped, never a per-frame vibration.
    const previewingSwim = state.mode === 'swim';
    const externalAttitude = state.externalAttitude === true;
    const targetTilt = externalAttitude ? 0 : moving
      ? -0.24 - state.navigationSpeed * 0.08
      : previewingSwim ? -0.09 : 0;
    this.visualTilt = damp(this.visualTilt, targetTilt, moving ? 2.2 : 1.45, deltaTime);
    this.visualLift = damp(
      this.visualLift,
      externalAttitude ? 0 : moving ? 0.22 + state.navigationSpeed * 0.11 : previewingSwim ? 0.045 : 0,
      2.0,
      deltaTime,
    );
    this.modelPivot.rotation.x = this.visualTilt;
    const swimTelemetry = this.motion.telemetry;
    const jetLift = swimTelemetry?.jetting
      ? (state.jet ?? 0) * 0.055
      : previewingSwim ? (swimTelemetry?.armPower ?? 0) * 0.030 : 0;
    this.modelPivot.position.y = this.visualLift + jetLift;
    this.modelPivot.rotation.z = damp(
      this.modelPivot.rotation.z,
      externalAttitude ? 0 : -(state.steer ?? 0) * state.navigationSpeed * 0.10,
      2.4,
      deltaTime,
    );

    // Cups are tucked between bundled arms during a power stroke. Hiding them
    // in that state also prevents detached underside geometry at extreme bends.
    const suckerOpacity = 1 - smoothstep(0.32, 0.92, state.navigationSpeed) * 0.64;
    this.detailMaterials.sucker.opacity = suckerOpacity * 0.96;
    this.detailMaterials.sucker.depthWrite = suckerOpacity > 0.84;
    this.suckerMeshes.forEach((mesh) => { mesh.visible = suckerOpacity > 0.08; });

    this.gazeSmoothed.x = damp(this.gazeSmoothed.x, this.gaze.x, 4.5, deltaTime);
    this.gazeSmoothed.y = damp(this.gazeSmoothed.y, this.gaze.y, 4.5, deltaTime);
    this.pupils.forEach((pupil, index) => {
      const rest = this.pupilRestPositions[index];
      pupil.position.x = rest.x + this.gazeSmoothed.x * 0.010;
      pupil.position.z = rest.z - this.gazeSmoothed.y * 0.007;
    });
  }

  updateVisuals(state) {
    const activity = THREE.MathUtils.clamp(
      Math.max(state.navigationSpeed ?? 0, Math.abs(state.thrust ?? 0) * 0.35),
      0,
      1,
    );
    colorTarget.lerpColors(REST_COLOR, ACTIVE_COLOR, activity * 0.42);
    this.skinMaterial.color.copy(colorTarget);
    this.skinMaterial.opacity = 0.24 + activity * 0.06;
    this.underlayMaterial.opacity = 0.040 + activity * 0.012;
  }

  getPickables() {
    return [];
  }

  setHoveredArm() {}
  setArmTarget() {}
  releaseArm() {}

  setDebug(enabled) {
    this.debug = enabled;
    this.skinMaterial.opacity = enabled ? 0.42 : 0.24;
    this.skinMaterial.color.setHex(enabled ? 0x5eead4 : 0x2dd4bf);
  }

  resetMotion() {
    this.motion?.reset();
    this.visualTilt = 0;
    this.visualLift = 0;
    this.modelPivot.position.y = 0;
    this.modelPivot.rotation.x = 0;
    this.modelPivot.rotation.z = 0;
    this.currentActionName = 'Idle_Breathe';
  }

  dispose() {
    this.mixer?.stopAllAction();
    this.model?.traverse((object) => {
      if (object.isMesh) object.geometry.dispose();
    });
    this.skinMaterial.dispose();
    this.underlayMaterial.dispose();
    Object.values(this.detailMaterials).forEach((material) => material.dispose());
  }
}
