import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { HydrostatMotion } from '../assets/octopus-v10/HydrostatMotion.js?v=22';

// The authored solid model and its existing hydrostat rig are reused unchanged.
// Unlike the full aquarium, this stage has no page-global renderer or navigation.
const MODEL_URL = new URL('../assets/octopus-v10/octopus-rigged-v12.glb', import.meta.url);
const MODEL_BASE = new URL('../assets/octopus-v10/', import.meta.url).href;
const DIRECTIONS = {
  air: { yaw: -0.28, roll: -0.06, exposure: 1.02, rim: 0xd5ebe5 },
  studio: { yaw: 0.30, roll: 0.08, exposure: 1.00, rim: 0xe8e7c8 },
  nocturne: { yaw: -0.42, roll: -0.04, exposure: 1.10, rim: 0x8bcbbd },
};
const stages = new Map();

function disposeModel(model) {
  if (!model) return;
  const geometries = new Set();
  const materials = new Set();
  const textures = new Set();
  const skeletons = new Set();
  model.traverse(object => {
    if (object.geometry) geometries.add(object.geometry);
    if (object.skeleton) skeletons.add(object.skeleton);
    const list = Array.isArray(object.material) ? object.material : [object.material];
    list.filter(Boolean).forEach(material => materials.add(material));
  });
  materials.forEach(material => {
    Object.values(material).forEach(value => {
      if (value?.isTexture) textures.add(value);
    });
    material.dispose();
  });
  textures.forEach(texture => texture.dispose());
  geometries.forEach(geometry => geometry.dispose());
  skeletons.forEach(skeleton => skeleton.dispose());
}

class MascotStage {
  constructor(host) {
    this.host = host;
    this.canvas = host.querySelector('[data-mascot-canvas]');
    this.poster = host.querySelector('[data-mascot-poster]');
    this.toggle = host.querySelector('[data-mascot-toggle]');
    this.direction = DIRECTIONS[host.dataset.direction] || DIRECTIONS.air;
    this.motionPreference = window.matchMedia('(prefers-reduced-motion: reduce)');
    this.wantsMotion = !this.motionPreference.matches;
    this.pointer = new THREE.Vector2();
    this.smoothedPointer = new THREE.Vector2();
    this.abort = new AbortController();
    this.cleanup = [];
    this.frames = 0;
    this.elapsed = 0;
    this.lastTime = 0;
    this.animationFrame = 0;
    this.loaded = false;
    this.failed = false;
    this.disposed = false;
    this.inView = false;

    host.dataset.mascotState = 'loading';
    host.dataset.mascotPlaying = 'false';
    host.dataset.mascotFrames = '0';
    this.showPoster(true);
    if (this.toggle) this.toggle.disabled = true;
    this.updateToggle();
    this.start().catch(error => {
      if (!this.disposed && error.name !== 'AbortError') {
        this.fallback('The octopus animation is unavailable.');
      }
    });
  }

  listen(target, type, callback, options) {
    target.addEventListener(type, callback, options);
    this.cleanup.push(() => target.removeEventListener(type, callback, options));
  }

  showPoster(visible) {
    if (this.poster) {
      this.poster.hidden = !visible;
      this.poster.style.visibility = visible ? 'visible' : 'hidden';
    }
    if (this.canvas) {
      this.canvas.hidden = visible;
      this.canvas.style.visibility = visible ? 'hidden' : 'visible';
    }
  }

  updateToggle() {
    if (!this.toggle) return;
    const available = this.loaded && !this.failed && !this.disposed;
    const playing = available && this.wantsMotion;
    this.toggle.hidden = !available && !this.failed;
    this.toggle.disabled = !available;
    this.toggle.setAttribute('aria-pressed', String(playing));
    const label = this.failed
      ? 'Octopus animation unavailable'
      : playing ? 'Pause octopus animation' : 'Play octopus animation';
    this.toggle.setAttribute('aria-label', label);
    this.toggle.title = label;
    this.toggle.dataset.playing = String(playing);
    // Optional text lets a host keep its own icon without losing the state label.
    const text = this.toggle.querySelector('[data-mascot-toggle-label]');
    if (text) text.textContent = playing ? 'Pause' : 'Play';
    else this.toggle.textContent = this.failed ? 'Still preview' : playing ? 'Pause motion' : 'Play motion';
  }

  async start() {
    if (!this.canvas) {
      this.fallback('The octopus stage is unavailable.');
      return;
    }
    // Asking for a context first avoids renderer diagnostics for the expected
    // no-WebGL fallback path. This stage does not require a GPU to show its poster.
    const context = this.canvas.getContext('webgl2', {
      alpha: true,
      antialias: true,
      powerPreference: 'low-power',
      preserveDrawingBuffer: true,
    });
    if (!context) {
      this.fallback('WebGL is unavailable.');
      return;
    }

    this.renderer = new THREE.WebGLRenderer({ canvas: this.canvas, context, alpha: true });
    this.renderer.setClearColor(0x000000, 0);
    this.renderer.outputColorSpace = THREE.SRGBColorSpace;
    this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
    this.renderer.toneMappingExposure = this.direction.exposure;
    this.scene = new THREE.Scene();
    this.camera = new THREE.OrthographicCamera(-3, 3, 3, -3, 0.1, 60);
    this.camera.position.set(4.4, 3.4, 10.0);
    this.camera.lookAt(0, 0, 0);

    this.scene.add(new THREE.HemisphereLight(0xf4f7f2, 0x5a6e61, 1.5));
    const key = new THREE.DirectionalLight(0xfff2e2, 3.0);
    key.position.set(-4, 7, 6);
    this.scene.add(key);
    const fill = new THREE.DirectionalLight(0xe4eeff, 0.8);
    fill.position.set(5, 2, 3);
    this.scene.add(fill);
    const rim = new THREE.DirectionalLight(this.direction.rim, 2.0);
    rim.position.set(-1, 4, -5);
    this.scene.add(rim);

    this.listen(this.canvas, 'webglcontextlost', event => {
      event.preventDefault();
      this.fallback('The graphics context was interrupted.');
    });
    this.listen(document, 'visibilitychange', () => this.syncPlayback());
    this.listen(this.motionPreference, 'change', () => {
      // A new reduced-motion preference always stops autoplay, even after a
      // previous explicit play. Turning it off does not override a manual pause.
      if (this.motionPreference.matches) this.wantsMotion = false;
      this.updateToggle();
      this.syncPlayback();
    });
    if (this.toggle) {
      this.listen(this.toggle, 'click', () => {
        this.wantsMotion = !this.wantsMotion;
        this.updateToggle();
        this.syncPlayback();
      });
    }
    this.listen(this.host, 'pointermove', event => {
      if (!this.wantsMotion || event.pointerType === 'touch') return;
      if (event.target.closest('a, button, input, select, textarea, [role="button"]')) return;
      const rect = this.host.getBoundingClientRect();
      if (!rect.width || !rect.height) return;
      this.pointer.set(
        THREE.MathUtils.clamp(((event.clientX - rect.left) / rect.width - 0.5) * 2, -1, 1),
        THREE.MathUtils.clamp(((event.clientY - rect.top) / rect.height - 0.5) * 2, -1, 1),
      );
    }, { passive: true });
    this.listen(this.host, 'pointerleave', () => this.pointer.set(0, 0), { passive: true });

    const rect = this.host.getBoundingClientRect();
    this.inView = rect.bottom > 0 && rect.top < innerHeight && rect.right > 0 && rect.left < innerWidth;
    this.intersection = new IntersectionObserver(entries => {
      this.inView = entries[0].isIntersecting;
      this.syncPlayback();
    }, { threshold: 0.01 });
    this.intersection.observe(this.host);
    this.resizeObserver = new ResizeObserver(() => this.resize());
    this.resizeObserver.observe(this.host);

    const response = await fetch(MODEL_URL, { signal: this.abort.signal });
    if (!response.ok) throw new Error(`Local model response: ${response.status}`);
    const buffer = await response.arrayBuffer();
    if (this.disposed || this.failed) return;
    const gltf = await new GLTFLoader().parseAsync(buffer, MODEL_BASE);
    if (this.disposed || this.failed) {
      disposeModel(gltf.scene);
      return;
    }
    this.model = gltf.scene;
    this.model.traverse(object => {
      if (!object.isMesh) return;
      const materials = Array.isArray(object.material) ? object.material : [object.material];
      materials.forEach(material => {
        if (material.name === 'Living octopus skin') {
          // The exported body is neutral until the original site reskins it.
          // Preserve the rig/geometry and give this solid presentation its teal.
          material.color.setHex(0x318b7e);
          material.roughness = 0.42;
          material.metalness = 0.03;
        } else if (material.name === 'Sucker tissue') {
          material.color.setHex(0xb6cfba);
          material.roughness = 0.60;
        }
      });
    });
    this.motion = new HydrostatMotion(this.model, { seed: 12741, phase: 0.85, rate: 0.85 });
    this.motionState = {
      mode: 'swim',
      propulsionStyle: 'arm',
      navigationSpeed: 0.025,
      thrust: 0,
      steer: 0,
      motionScale: 0.30,
      bundleBaseline: 0.42,
      strokeAmplitude: 0.16,
      armPhase: 0.42,
      externalAttitude: true,
    };
    // A deterministic settled pose is also the first and only frame when the
    // operating system asks for reduced motion.
    for (let step = 0; step < 180; step += 1) this.motion.update(1 / 60, this.motionState);
    this.model.updateMatrixWorld(true);
    const bounds = new THREE.Box3().setFromObject(this.model, true);
    const center = bounds.getCenter(new THREE.Vector3());
    const size = bounds.getSize(new THREE.Vector3());
    const span = Math.max(size.x, size.y, size.z);
    if (!Number.isFinite(span) || span <= 0) throw new Error('Invalid local model bounds.');
    this.model.position.sub(center);
    this.frame = new THREE.Group();
    this.frame.scale.setScalar(3.5 / span);
    this.frame.rotation.set(0, this.direction.yaw, this.direction.roll);
    this.frame.position.y = 0.25;
    this.frame.add(this.model);
    this.scene.add(this.frame);

    this.loaded = true;
    this.host.dataset.mascotModel = 'octopus-rigged-v12';
    this.resize();
    this.showPoster(false);
    this.updateToggle();
    this.syncPlayback();
  }

  resize() {
    if (this.disposed || this.failed || !this.renderer) return;
    const { width, height } = this.host.getBoundingClientRect();
    if (width < 1 || height < 1) return;
    const aspect = width / height;
    const extent = 2.22 / Math.min(1, aspect);
    this.camera.left = -extent * aspect;
    this.camera.right = extent * aspect;
    this.camera.top = extent;
    this.camera.bottom = -extent;
    this.camera.updateProjectionMatrix();
    // Keep high-density mobile screens from multiplying the scene's GPU cost.
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 1.6));
    this.renderer.setSize(Math.round(width), Math.round(height), false);
    if (this.loaded) this.render();
  }

  render() {
    if (!this.loaded || this.disposed || this.failed) return;
    this.renderer.render(this.scene, this.camera);
    this.frames += 1;
    this.host.dataset.mascotFrames = String(this.frames);
  }

  syncPlayback() {
    if (!this.loaded || this.failed || this.disposed) return;
    const playing = this.wantsMotion && this.inView && !document.hidden;
    this.host.dataset.mascotState = playing ? 'ready' : 'still';
    this.host.dataset.mascotPlaying = String(playing);
    if (playing && !this.animationFrame) {
      this.lastTime = 0;
      this.animationFrame = requestAnimationFrame(time => this.tick(time));
    } else if (!playing && this.animationFrame) {
      cancelAnimationFrame(this.animationFrame);
      this.animationFrame = 0;
      this.lastTime = 0;
    }
  }

  tick(time) {
    this.animationFrame = 0;
    if (this.disposed || this.failed || !this.wantsMotion || !this.inView || document.hidden) {
      this.syncPlayback();
      return;
    }
    const delta = this.lastTime ? Math.min((time - this.lastTime) / 1000, 1 / 30) : 1 / 60;
    this.lastTime = time;
    this.elapsed += delta;
    this.motionState.armPhase = (0.42 + this.elapsed / 13) % 1;
    this.motion.update(delta, this.motionState);
    const response = 1 - Math.exp(-2.3 * delta);
    this.smoothedPointer.lerp(this.pointer, response);
    this.frame.rotation.y = this.direction.yaw + this.smoothedPointer.x * 0.10 + Math.sin(this.elapsed * 0.23) * 0.055;
    this.frame.rotation.x = this.smoothedPointer.y * 0.035;
    this.frame.rotation.z = this.direction.roll + Math.sin(this.elapsed * 0.28) * 0.018;
    this.frame.position.y = 0.25 + Math.sin(this.elapsed * 0.40) * 0.055;
    this.render();
    this.animationFrame = requestAnimationFrame(next => this.tick(next));
  }

  fallback(reason) {
    if (this.disposed) return;
    this.failed = true;
    this.abort.abort();
    cancelAnimationFrame(this.animationFrame);
    this.animationFrame = 0;
    this.host.dataset.mascotState = 'fallback';
    this.host.dataset.mascotPlaying = 'false';
    this.host.dataset.mascotError = reason;
    this.showPoster(true);
    this.updateToggle();
    disposeModel(this.model);
    this.model = null;
    this.renderer?.dispose();
    this.renderer = null;
  }

  dispose() {
    if (this.disposed) return;
    this.disposed = true;
    this.abort.abort();
    cancelAnimationFrame(this.animationFrame);
    this.animationFrame = 0;
    this.intersection?.disconnect();
    this.resizeObserver?.disconnect();
    this.cleanup.splice(0).forEach(remove => remove());
    disposeModel(this.model);
    this.model = null;
    this.renderer?.dispose();
    this.renderer?.forceContextLoss();
    this.renderer = null;
    this.host.dataset.mascotState = 'still';
    this.host.dataset.mascotPlaying = 'false';
    this.showPoster(true);
    this.updateToggle();
  }
}

function mountStages() {
  document.querySelectorAll('[data-mascot]').forEach(host => {
    if (!stages.has(host)) stages.set(host, new MascotStage(host));
  });
}

mountStages();
window.addEventListener('pagehide', () => {
  stages.forEach(stage => stage.dispose());
  stages.clear();
});
window.addEventListener('pageshow', event => {
  if (event.persisted) mountStages();
});
