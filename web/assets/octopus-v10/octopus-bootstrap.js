const canvas = document.querySelector('#gl');

async function boot() {
  if (canvas) canvas.dataset.v10Status = 'import-three';
  await import('three');
  if (canvas) canvas.dataset.v10Status = 'import-motion';
  await import('./HydrostatMotion.js?v=21');
  if (canvas) canvas.dataset.v10Status = 'import-rig';
  await import('./Octopus.js?v=23');
  if (canvas) canvas.dataset.v10Status = 'import-layer';
  await import('./octopus-layer.js?v=31');
}

if (canvas) canvas.dataset.v10Status = 'bootstrap';

boot().catch((error) => {
  if (canvas) {
    canvas.classList.remove('is-ready');
    canvas.dataset.v10Status = 'import-error';
    canvas.dataset.v10Error = String(error?.message || error);
  }
  console.error('[octopus-v10] module import failed', error);
});
