(() => {
  const scene = document.querySelector('#shelf-handoff');
  const toggle = document.querySelector('#handoff-toggle');
  if (!scene || !toggle) return;
  const preference = matchMedia('(prefers-reduced-motion: reduce)');
  let playing = !preference.matches;
  let visible = false;
  const canvas = scene.querySelector('.handoff-scene');
  const align = () => {
    const bounds = canvas.getBoundingClientRect();
    for (const [prefix, selector] of [['source', '.handoff-source'], ['target', '.message-drop']]) {
      const box = scene.querySelector(selector).getBoundingClientRect();
      canvas.style.setProperty('--' + prefix + '-x', (box.left - bounds.left) + 'px');
      canvas.style.setProperty('--' + prefix + '-y', (box.top - bounds.top) + 'px');
      canvas.style.setProperty('--' + prefix + '-w', box.width + 'px');
      canvas.style.setProperty('--' + prefix + '-h', box.height + 'px');
    }
  };
  if ('ResizeObserver' in window) new ResizeObserver(align).observe(canvas);
  window.addEventListener('resize', align);
  document.fonts?.ready.then(align);
  align();
  const render = () => {
    scene.classList.toggle('has-animation', playing || !preference.matches);
    scene.classList.toggle('is-playing', playing && visible);
    toggle.textContent = playing ? 'Pause animation' : 'Play animation';
  };
  toggle.hidden = false;
  toggle.addEventListener('click', () => {
    playing = !playing;
    scene.classList.toggle('motion-requested', playing && preference.matches);
    render();
  });
  preference.addEventListener('change', () => {
    playing = !preference.matches;
    scene.classList.remove('motion-requested');
    render();
  });
  if ('IntersectionObserver' in window) {
    new IntersectionObserver(entries => { visible = entries[0].isIntersecting; render(); }, { threshold: .15 }).observe(scene);
  } else visible = true;
  render();
})();
