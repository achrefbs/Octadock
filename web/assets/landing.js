/* Octadock landing interactions. Classic script: it must run even where the
   3D module cannot (file://, old browsers), so the page always reads and its
   core controls still work. Everything here is enhancement or fallback. */
(function () {
  'use strict';

  var reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  /* If the WebGL aquarium did not boot within a beat, use the static water
     treatment. The content remains ordinary document flow either way. */
  window.setTimeout(function () {
    if (!window.__octoLive) {
      document.body.classList.add('no-octo');
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
    Array.prototype.forEach.call(revs, function (el) { io.observe(el); });
  }

  /* Copy the real PowerShell examples without their decorative prompt marks.
     Clipboard API is preferred; execCommand keeps file:// and older browsers
     useful without introducing a hidden form or a dependency. */
  var copyCommands = document.getElementById('copy-commands');
  var commandsCode = document.getElementById('commands-code');
  var copyStatus = document.getElementById('copy-status');

  function legacyCopy(text) {
    return new Promise(function (resolve, reject) {
      var active = document.activeElement;
      var field = document.createElement('textarea');
      field.value = text;
      field.readOnly = true;
      field.setAttribute('aria-hidden', 'true');
      field.style.position = 'fixed';
      field.style.left = '-9999px';
      field.style.top = '0';
      document.body.appendChild(field);
      field.focus();
      field.select();
      field.setSelectionRange(0, field.value.length);

      var copied = false;
      try {
        copied = document.execCommand('copy');
      } catch (error) {
        copied = false;
      }

      document.body.removeChild(field);
      if (active && typeof active.focus === 'function') {
        try { active.focus({ preventScroll: true }); } catch (error) { active.focus(); }
      }

      if (copied) resolve();
      else reject(new Error('The browser did not allow clipboard access.'));
    });
  }

  function writeClipboard(text) {
    if (window.isSecureContext && navigator.clipboard &&
        typeof navigator.clipboard.writeText === 'function') {
      return navigator.clipboard.writeText(text).catch(function () {
        return legacyCopy(text);
      });
    }
    return legacyCopy(text);
  }

  if (copyCommands && commandsCode && copyStatus) {
    var copyLabel = copyCommands.textContent;
    var resetCopyLabel = null;
    var copyPending = false;
    copyCommands.setAttribute('aria-describedby', 'copy-status');

    copyCommands.addEventListener('click', function () {
      if (copyPending) return;
      var text = commandsCode.textContent
        .replace(/\r\n?/g, '\n')
        .replace(/^>\s?/gm, '')
        .trim();

      if (!text) {
        copyStatus.textContent = 'There are no commands to copy.';
        return;
      }

      copyPending = true;
      copyCommands.setAttribute('aria-busy', 'true');
      copyStatus.textContent = '';
      writeClipboard(text).then(function () {
        copyPending = false;
        copyCommands.removeAttribute('aria-busy');
        copyCommands.textContent = 'Copied';
        copyStatus.textContent = 'Commands copied to the clipboard.';
        if (resetCopyLabel) window.clearTimeout(resetCopyLabel);
        resetCopyLabel = window.setTimeout(function () {
          copyCommands.textContent = copyLabel;
        }, 1600);
      }).catch(function () {
        copyPending = false;
        copyCommands.removeAttribute('aria-busy');
        copyCommands.textContent = copyLabel;
        copyStatus.textContent = 'Copy failed. Select the commands and press Ctrl+C.';
      });
    });
  }
})();
