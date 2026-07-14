/* Octadock landing interactions. Classic script: it must run even where the
   3D module cannot (file://, old browsers), so the page always reads and the
   demo always works. Everything here is enhancement or fallback. */
(function () {
  'use strict';

  var reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  /* If the WebGL journey didn't boot within a beat, fall back to the static
     water treatment and reveal the screen room permanently. */
  window.setTimeout(function () {
    if (!window.__octoLive) {
      document.body.classList.add('no-octo');
      var room = document.getElementById('screenroom');
      if (room) { room.classList.add('live'); room.style.pointerEvents = 'auto'; }
      var hud = document.querySelector('.hud');
      if (hud) hud.classList.add('gone');
      var cue = document.querySelector('.scrollcue');
      if (cue) cue.classList.add('gone');
    }
  }, 1400);

  /* Scroll reveals: the .js class is added HERE so a failed load can never
     leave content hidden. */
  if ('IntersectionObserver' in window && !reduce) {
    document.documentElement.classList.add('js');
    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (en) {
        if (en.isIntersecting) {
          en.target.classList.add('in');
          io.unobserve(en.target);
        }
      });
    }, { threshold: 0.14, rootMargin: '0px 0px -6% 0px' });
    var revs = document.querySelectorAll('.rev');
    Array.prototype.forEach.call(revs, function (el, i) { io.observe(el); });
    Array.prototype.forEach.call(document.querySelectorAll('.cap'), function (el, i) {
      el.style.transitionDelay = (0.035 * i) + 's';
    });
  }

  /* Instrument rows: active state on hover (desktop) or centre-scroll (touch) */
  var caps = Array.prototype.slice.call(document.querySelectorAll('.cap'));
  caps.forEach(function (c) {
    c.addEventListener('pointerenter', function () {
      caps.forEach(function (x) { x.classList.remove('on'); });
      c.classList.add('on');
    });
  });
  if (window.matchMedia('(max-width:720px)').matches && 'IntersectionObserver' in window) {
    var cio = new IntersectionObserver(function (es) {
      es.forEach(function (e) {
        if (e.isIntersecting) {
          caps.forEach(function (x) { x.classList.remove('on'); });
          e.target.classList.add('on');
        }
      });
    }, { rootMargin: '-45% 0px -45% 0px' });
    caps.forEach(function (c) { cio.observe(c); });
  }

  /* Dock demo: press the camera, get a shelf card */
  var cap = document.getElementById('demo-capture');
  var flash = document.getElementById('demo-flash');
  var marquee = document.getElementById('demo-marquee');
  var card = document.getElementById('demo-card');
  var trash = document.getElementById('demo-trash');
  var copyBtn = document.getElementById('demo-copy');

  if (cap && flash && marquee && card) {
    cap.addEventListener('click', function () {
      card.classList.remove('show');
      if (reduce) { card.classList.add('show'); return; }
      flash.classList.remove('go');
      marquee.classList.remove('go');
      void flash.offsetWidth; /* restart the one-shot animations */
      flash.classList.add('go');
      marquee.classList.add('go');
      window.setTimeout(function () { card.classList.add('show'); }, 500);
    });
    if (trash) {
      trash.addEventListener('click', function () { card.classList.remove('show'); });
    }
    if (copyBtn) {
      var orig = copyBtn.innerHTML;
      var tick = null;
      copyBtn.addEventListener('click', function () {
        copyBtn.innerHTML =
          '<svg viewBox="0 0 24 24" fill="none" stroke="#0f8a6d" stroke-width="2.4" ' +
          'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
          '<path d="M20 6L9 17l-5-5"/></svg>';
        if (tick) window.clearTimeout(tick);
        tick = window.setTimeout(function () { copyBtn.innerHTML = orig; }, 1100);
      });
    }
  }
})();
