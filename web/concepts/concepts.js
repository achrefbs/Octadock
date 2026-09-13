const icons = {
  region: '<path d="M8 3H5a2 2 0 0 0-2 2v3m13-5h3a2 2 0 0 1 2 2v3M3 16v3a2 2 0 0 0 2 2h3m8 0h3a2 2 0 0 0 2-2v-3"/><path d="M8 8h8v8H8z"/>',
  window: '<rect x="3" y="4" width="18" height="16" rx="3"/><path d="M3 9h18m-14-2h.01M10 7h.01"/>',
  scroll: '<rect x="5" y="3" width="14" height="18" rx="3"/><path d="m9 8 3-3 3 3m-3-3v14m-3-3 3 3 3-3"/>',
  copy: '<rect x="8" y="8" width="12" height="12" rx="3"/><path d="M15 8V5a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h3"/>',
  edit: '<path d="m15 4 5 5M4 20l5-1L21 7a2 2 0 0 0-5-5L4 14z"/>',
  save: '<path d="M12 3v12m-4-4 4 4 4-4M4 16v3a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-3"/>',
  arrow: '<path d="M4 12h16m-6-6 6 6-6 6"/>',
  check: '<path d="m5 12 4 4L19 6"/>',
  text: '<path d="M4 5h16M12 5v15m-4 0h8"/>',
  close: '<path d="m6 6 12 12M6 18 18 6"/>',
  more: '<circle cx="5" cy="12" r="1"/><circle cx="12" cy="12" r="1"/><circle cx="19" cy="12" r="1"/>',
};
const icon = name => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${icons[name] || icons.region}</svg>`;
document.querySelectorAll('[data-icon]').forEach(el => el.innerHTML = icon(el.dataset.icon));

document.querySelectorAll('[data-demo]').forEach(demo => {
  const modeButtons = [...demo.querySelectorAll('[data-mode]')];
  const status = demo.querySelector('[data-demo-status]');
  let mode = 'Region';
  let count = 3;
  modeButtons.forEach(button => button.addEventListener('click', () => {
    mode = button.dataset.mode;
    modeButtons.forEach(item => item.setAttribute('aria-pressed', String(item === button)));
    demo.dataset.captureMode = mode.toLowerCase();
    demo.querySelector('[data-mode-label]').textContent = mode;
    status.textContent = `${mode} selected. Try the capture button.`;
  }));
  demo.querySelector('[data-capture]').addEventListener('click', () => {
    count += 1;
    demo.classList.remove('captured');
    void demo.offsetWidth;
    demo.classList.add('captured');
    const history = demo.querySelector('[data-history]');
    const thumb = document.createElement('button');
    thumb.type = 'button';
    thumb.className = 'history-thumb';
    thumb.setAttribute('aria-label', `Select ${mode.toLowerCase()} capture ${count}`);
    thumb.setAttribute('aria-pressed', 'false');
    thumb.innerHTML = '<img src="scene.svg" alt="">';
    thumb.style.setProperty('--position', `${30 + (count * 13) % 70}%`);
    history.prepend(thumb);
    if (history.children.length > 5) history.lastElementChild.remove();
    selectThumbnail(thumb);
    status.textContent = `${mode} captured in this demo. Select a thumbnail, then copy, mark up, or save it.`;
  });
  function selectThumbnail(thumb) {
    demo.querySelectorAll('.history-thumb').forEach(item => item.setAttribute('aria-pressed', String(item === thumb)));
    const shot = demo.querySelector('.selected-shot');
    shot.style.setProperty('--position', thumb.style.getPropertyValue('--position') || '50%');
    status.textContent = 'Capture selected. Copy, mark up, or save from the tools below.';
  }
  demo.querySelector('[data-history]').addEventListener('click', e => {
    const thumb = e.target.closest('.history-thumb');
    if (thumb) selectThumbnail(thumb);
  });
  demo.querySelector('[data-edit]').addEventListener('click', e => {
    const active = demo.classList.toggle('annotated');
    e.currentTarget.setAttribute('aria-pressed', String(active));
    status.textContent = active ? 'Markup added. Click the pen again to remove it.' : 'Markup removed.';
  });
  demo.querySelector('[data-copy]').addEventListener('click', async () => {
    try {
      await navigator.clipboard.writeText('A moment worth keeping. Captured with Octadock.');
      status.textContent = 'Sample text copied to your clipboard.';
    } catch {
      status.textContent = 'Clipboard access is unavailable here. You can still save the sample image.';
    }
  });
  demo.querySelector('[data-save]').addEventListener('click', () => {
    const link = document.createElement('a');
    link.href = 'scene.svg';
    link.download = 'octadock-sample-landscape.svg';
    link.click();
    status.textContent = 'Sample landscape saved as an SVG image.';
  });
});

const release = document.querySelector('#release-info');
document.querySelectorAll('[data-release]').forEach(button => button.addEventListener('click', () => release.showModal()));
release?.querySelector('[data-close]')?.addEventListener('click', () => release.close());
release?.addEventListener('click', event => { if (event.target === release) release.close(); });

const preferenceKey = 'octadock-landing-preference';
document.querySelectorAll('[data-prefer]').forEach(button => button.addEventListener('click', () => {
  try { localStorage.setItem(preferenceKey, button.dataset.prefer); } catch { /* Private browsing may block persistence. */ }
  document.querySelectorAll('[data-prefer]').forEach(item => item.setAttribute('aria-pressed', String(item === button)));
  const message = document.querySelector('[data-preference-status]');
  if (message) message.textContent = `${button.dataset.prefer} selected on this browser. Tell me your choice in the task to apply it.`;
}));
try {
  const saved = localStorage.getItem(preferenceKey);
  document.querySelectorAll('[data-prefer]').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.prefer === saved)));
} catch { /* The chooser works without local storage. */ }
