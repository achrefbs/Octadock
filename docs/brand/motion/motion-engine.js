(() => {
  'use strict';

  const DURATIONS = {
    'dock-resolve': 1200,
    'port-handshake': 600,
    'signal-to-dock': 6000,
    'quiet-current': 5600,
  };
  const FROST = '#F2F6FC';
  const TEAL = '#2DD4BF';
  const CYAN = '#38BDF8';

  const state = {
    concept: 'dock-resolve',
    currentTime: 0,
    playing: false,
    reduced: false,
    startedAt: 0,
    frame: 0,
  };

  const $ = (selector) => document.querySelector(selector);
  const stage = $('#motion-stage');
  const symbol = $('#logo-symbol');
  const mantle = $('#mantle');
  const wordmark = $('#motion-wordmark');
  const halo = $('#dock-halo');
  const tracerLayer = $('#tracers');
  const tracers = Array.from(document.querySelectorAll('.tracer'));
  const progress = $('#progress');
  const timeLabel = $('#time-label');

  const ports = Object.fromEntries(
    Array.from({ length: 8 }, (_, index) => {
      const id = `port-${String(index + 1).padStart(2, '0')}`;
      return [id, document.getElementById(`${id}-motion`)];
    })
  );

  function clamp(value, min = 0, max = 1) {
    return Math.min(max, Math.max(min, value));
  }

  function segment(time, start, end) {
    return clamp((time - start) / Math.max(1, end - start));
  }

  function cubicBezier(x1, y1, x2, y2) {
    const sampleCurveX = (t) => ((1 - 3 * x2 + 3 * x1) * t + (3 * x2 - 6 * x1)) * t * t + 3 * x1 * t;
    const sampleCurveY = (t) => ((1 - 3 * y2 + 3 * y1) * t + (3 * y2 - 6 * y1)) * t * t + 3 * y1 * t;
    const sampleDerivativeX = (t) => (3 * (1 - 3 * x2 + 3 * x1) * t + 2 * (3 * x2 - 6 * x1)) * t + 3 * x1;
    return (x) => {
      let t = x;
      for (let i = 0; i < 5; i += 1) {
        const derivative = sampleDerivativeX(t);
        if (Math.abs(derivative) < 1e-6) break;
        t -= (sampleCurveX(t) - x) / derivative;
      }
      return sampleCurveY(clamp(t));
    };
  }

  const ease = {
    enter: cubicBezier(.16, 1, .3, 1),
    dock: cubicBezier(.22, 1, .36, 1),
    settle: cubicBezier(.4, 0, .2, 1),
    ambient: cubicBezier(.37, 0, .63, 1),
    in: cubicBezier(.4, 0, 1, 1),
  };

  function lerp(a, b, t) {
    return a + (b - a) * t;
  }

  function parseHex(hex) {
    const value = hex.replace('#', '');
    return [0, 2, 4].map((offset) => parseInt(value.slice(offset, offset + 2), 16));
  }

  function mixColor(from, to, t) {
    const a = parseHex(from);
    const b = parseHex(to);
    return `rgb(${a.map((value, index) => Math.round(lerp(value, b[index], t))).join(',')})`;
  }

  function transform(element, x = 0, y = 0, scale = 1) {
    element.style.transformBox = 'fill-box';
    element.style.transformOrigin = 'center';
    element.style.transform = `translate(${x}px, ${y}px) scale(${scale})`;
  }

  function setPort(id, { x = 0, y = 0, scale = 1, opacity = 1, color = FROST } = {}) {
    const element = ports[id];
    transform(element, x, y, scale);
    element.style.opacity = opacity;
    element.style.fill = color;
  }

  function resetVisuals() {
    transform(symbol, 0, 0, 1);
    transform(mantle, 0, 0, 1);
    mantle.style.opacity = 1;
    mantle.style.clipPath = 'inset(0 0 0 0)';
    wordmark.style.opacity = 1;
    wordmark.style.transform = 'translate(0, 0)';
    wordmark.style.letterSpacing = '0';
    halo.style.opacity = .08;
    halo.style.transform = 'scale(1)';
    tracerLayer.style.opacity = 0;
    tracers.forEach((path) => {
      path.style.strokeDasharray = '1';
      path.style.strokeDashoffset = '1';
      path.style.opacity = 0;
    });
    Object.keys(ports).forEach((id) => setPort(id));
  }

  const PAIRS = [
    { left: 'port-01', right: 'port-08', start: 140, resolve: 400 },
    { left: 'port-02', right: 'port-07', start: 200, resolve: 460 },
    { left: 'port-03', right: 'port-06', start: 260, resolve: 520 },
    { left: 'port-04', right: 'port-05', start: 320, resolve: 580 },
  ];

  function renderDockResolve(time) {
    const mantleP = ease.enter(segment(time, 0, 220));
    mantle.style.opacity = mantleP;
    transform(mantle, 0, lerp(-6, 0, mantleP), lerp(.965, 1, mantleP));

    PAIRS.forEach((pair) => {
      const dockP = ease.dock(segment(time, pair.start, pair.start + 320));
      const resolveP = ease.settle(segment(time, pair.resolve, pair.resolve + 240));
      const color = mixColor(TEAL, FROST, resolveP);
      setPort(pair.left, { x: lerp(-14, 0, dockP), y: lerp(3, 0, dockP), scale: lerp(.86, 1, dockP), opacity: dockP, color });
      setPort(pair.right, { x: lerp(14, 0, dockP), y: lerp(3, 0, dockP), scale: lerp(.86, 1, dockP), opacity: dockP, color });
    });

    let rootScale = 1;
    if (time >= 600 && time < 680) rootScale = lerp(1, 1.012, ease.settle(segment(time, 600, 680)));
    if (time >= 680 && time < 840) rootScale = lerp(1.012, 1, ease.settle(segment(time, 680, 840)));
    transform(symbol, 0, 0, rootScale);

    const wordP = ease.enter(segment(time, 820, 1080));
    wordmark.style.opacity = wordP;
    wordmark.style.transform = `translate(${lerp(-5, 0, wordP)}px, 0)`;
    halo.style.opacity = lerp(.035, .095, ease.settle(segment(time, 0, 840)));
  }

  function pairPulse(time, start, left, right) {
    const local = segment(time, start, start + 180);
    const rise = local < .39 ? ease.enter(local / .39) : 1 - ease.dock((local - .39) / .61);
    const colorMix = Math.sin(Math.PI * local);
    const color = mixColor(FROST, TEAL, colorMix);
    setPort(left, { y: -2 * rise, color });
    setPort(right, { y: -2 * rise, color });
  }

  function renderPortHandshake(time) {
    let scale = 1;
    if (time < 70) scale = lerp(1, .985, ease.in(segment(time, 0, 70)));
    else if (time < 190) scale = lerp(.985, 1.012, ease.enter(segment(time, 70, 190)));
    else if (time < 310) scale = lerp(1.012, 1, ease.settle(segment(time, 190, 310)));
    transform(symbol, 0, 0, scale);
    pairPulse(time, 60, 'port-04', 'port-05');
    pairPulse(time, 105, 'port-03', 'port-06');
    pairPulse(time, 150, 'port-02', 'port-07');
    pairPulse(time, 195, 'port-01', 'port-08');
    halo.style.opacity = lerp(.06, .13, Math.sin(Math.PI * segment(time, 40, 420)));
  }

  function updateTracerPaths(portrait) {
    const paths = portrait
      ? [
          'M30 120 C160 150 150 400 260 455', 'M20 340 C170 340 250 430 330 535',
          'M80 700 C210 650 320 575 405 555', 'M300 930 C370 730 455 615 470 565',
          'M700 930 C630 730 545 615 530 565', 'M920 700 C790 650 680 575 595 555',
          'M980 340 C830 340 750 430 670 535', 'M970 120 C840 150 850 400 740 455',
        ]
      : [
          'M20 140 C170 170 170 380 300 430', 'M10 350 C170 350 260 470 370 530',
          'M100 900 C240 760 350 620 430 550', 'M330 980 C400 760 450 630 470 560',
          'M670 980 C600 760 550 630 530 560', 'M900 900 C760 760 650 620 570 550',
          'M990 350 C830 350 740 470 630 530', 'M980 140 C830 170 830 380 700 430',
        ];
    tracers.forEach((path, index) => path.setAttribute('d', paths[index]));
  }

  function renderSignalToDock(time) {
    const portrait = stage.classList.contains('portrait');
    updateTracerPaths(portrait);
    tracerLayer.style.opacity = 1;
    halo.style.opacity = lerp(0, .18, ease.settle(segment(time, 0, 1000)));
    halo.style.transform = `scale(${lerp(.92, 1, ease.settle(segment(time, 0, 1000)))})`;

    tracers.forEach((path, index) => {
      const drawStart = 700 + (index % 4) * 90;
      const draw = ease.settle(segment(time, drawStart, 2400));
      const fade = 1 - ease.settle(segment(time, 3250, 3900));
      path.style.strokeDashoffset = String(1 - draw);
      path.style.opacity = String(.7 * draw * fade);
    });

    mantle.style.opacity = 1;
    const reveal = ease.enter(segment(time, 2750, 3450));
    mantle.style.clipPath = `inset(0 ${100 - reveal * 100}% 0 0)`;

    const socialPairs = [
      { left: 'port-01', right: 'port-08', start: 2200 },
      { left: 'port-02', right: 'port-07', start: 2350 },
      { left: 'port-03', right: 'port-06', start: 2500 },
      { left: 'port-04', right: 'port-05', start: 2650 },
    ];
    socialPairs.forEach((pair) => {
      const dock = ease.dock(segment(time, pair.start, pair.start + 600));
      const resolve = ease.enter(segment(time, 3450, 4050));
      const color = mixColor(TEAL, FROST, resolve);
      setPort(pair.left, { x: lerp(-30, 0, dock), scale: lerp(.5, 1, dock), opacity: dock, color });
      setPort(pair.right, { x: lerp(30, 0, dock), scale: lerp(.5, 1, dock), opacity: dock, color });
    });

    const symbolSettle = ease.enter(segment(time, 3450, 4050));
    transform(symbol, 0, 0, lerp(.97, 1, symbolSettle));
    const word = ease.enter(segment(time, 4000, 4800));
    wordmark.style.opacity = word;
    wordmark.style.transform = `translate(0, ${lerp(6, 0, word)}px)`;
    wordmark.style.letterSpacing = `${lerp(.12, 0, word)}em`;
  }

  function ambientPulse(time, start, left, right) {
    const local = segment(time, start, start + 500);
    const amount = .18 * Math.sin(Math.PI * local);
    const color = mixColor(FROST, TEAL, amount / .18);
    setPort(left, { color });
    setPort(right, { color });
  }

  function renderQuietCurrent(time) {
    const half = time <= 2800 ? ease.ambient(segment(time, 0, 2800)) : 1 - ease.ambient(segment(time, 2800, 5600));
    halo.style.opacity = lerp(.06, .14, half);
    halo.style.transform = `scale(${lerp(.96, 1.04, half)})`;
    ambientPulse(time, 700, 'port-01', 'port-08');
    ambientPulse(time, 950, 'port-02', 'port-07');
    ambientPulse(time, 1200, 'port-03', 'port-06');
    ambientPulse(time, 1450, 'port-04', 'port-05');
  }

  function render(time) {
    const duration = DURATIONS[state.concept];
    state.currentTime = clamp(time, 0, duration);
    resetVisuals();
    stage.classList.toggle('portrait', state.concept === 'signal-to-dock');
    if (state.reduced) {
      halo.style.opacity = .06;
    } else if (state.concept === 'dock-resolve') renderDockResolve(state.currentTime);
    else if (state.concept === 'port-handshake') renderPortHandshake(state.currentTime);
    else if (state.concept === 'signal-to-dock') renderSignalToDock(state.currentTime);
    else renderQuietCurrent(state.currentTime);

    if (progress) progress.value = String(state.currentTime / duration);
    if (timeLabel) timeLabel.textContent = `${(state.currentTime / 1000).toFixed(2)} / ${(duration / 1000).toFixed(2)} s`;
  }

  function tick(now) {
    if (!state.playing) return;
    const duration = DURATIONS[state.concept];
    let elapsed = now - state.startedAt;
    if (state.concept === 'quiet-current') elapsed %= duration;
    if (elapsed >= duration) {
      render(duration);
      state.playing = false;
      updateButtons();
      return;
    }
    render(elapsed);
    state.frame = requestAnimationFrame(tick);
  }

  function play() {
    cancelAnimationFrame(state.frame);
    if (state.currentTime >= DURATIONS[state.concept]) state.currentTime = 0;
    state.playing = true;
    state.startedAt = performance.now() - state.currentTime;
    state.frame = requestAnimationFrame(tick);
    updateButtons();
  }

  function pause() {
    state.playing = false;
    cancelAnimationFrame(state.frame);
    updateButtons();
  }

  function replay() {
    pause();
    render(0);
    play();
  }

  function select(concept) {
    if (!DURATIONS[concept]) return;
    pause();
    state.concept = concept;
    document.querySelectorAll('[data-concept]').forEach((button) => {
      button.classList.toggle('active', button.dataset.concept === concept);
      button.setAttribute('aria-pressed', String(button.dataset.concept === concept));
    });
    render(0);
  }

  function seek(time) {
    pause();
    render(Number(time) || 0);
  }

  function updateButtons() {
    const playButton = $('#play');
    if (playButton) playButton.textContent = state.playing ? 'Pause' : 'Play';
  }

  document.querySelectorAll('[data-concept]').forEach((button) => {
    button.addEventListener('click', () => select(button.dataset.concept));
  });
  $('#play')?.addEventListener('click', () => (state.playing ? pause() : play()));
  $('#replay')?.addEventListener('click', replay);
  progress?.addEventListener('input', () => seek(Number(progress.value) * DURATIONS[state.concept]));
  $('#reduced-motion')?.addEventListener('change', (event) => {
    state.reduced = event.target.checked;
    pause();
    render(state.reduced ? DURATIONS[state.concept] : state.currentTime);
  });

  const params = new URLSearchParams(location.search);
  const initial = params.get('concept');
  if (initial && DURATIONS[initial]) state.concept = initial;
  if (params.get('capture') === '1') document.body.classList.add('capture-mode');
  if (params.get('alpha') === '1') {
    document.body.classList.add('alpha');
    document.documentElement.classList.add('alpha');
  }
  select(state.concept);
  if (params.get('autoplay') === '1' && !matchMedia('(prefers-reduced-motion: reduce)').matches) play();

  window.motionAPI = {
    durations: DURATIONS,
    select,
    seek,
    play,
    pause,
    replay,
    getState: () => ({ ...state }),
  };
  window.seek = seek;
})();
