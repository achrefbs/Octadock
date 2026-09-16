'use strict';

const dialog = document.querySelector('#download-dialog');
const form = document.querySelector('#signup-form');
const status = document.querySelector('#signup-status');
const submit = document.querySelector('#signup-submit');
const installer = document.querySelector('#installer-download');
const downloadUrl = 'https://octadock.com/downloads/Octadock-0.3.0-alpha.2-Setup.exe';
let lastTrigger;

function finishDownload(subscribed = false) {
  if (installer) {
    dialog?.close();
    document.querySelector('#download-status').textContent = subscribed
      ? 'Your signup request was received. Your download is starting. If it does not start, use the installer button.'
      : 'Your download is starting. If it does not start, use the installer button.';
    const link = document.createElement('a');
    link.href = downloadUrl;
    link.download = '';
    document.body.append(link);
    link.click();
    link.remove();
  } else {
    window.location.assign('download.html?start=1' + (subscribed ? '&subscribed=1' : ''));
  }
}

if (dialog && typeof dialog.showModal === 'function') {
  document.querySelectorAll('[data-download], [data-download-file]').forEach(link => {
    link.addEventListener('click', event => {
      // Modified clicks retain ordinary browser navigation.
      if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
      event.preventDefault();
      lastTrigger = link;
      status.textContent = '';
      dialog.showModal();
    });
  });
  dialog.querySelector('[data-close-dialog]').addEventListener('click', () => dialog.close());
  dialog.addEventListener('close', () => lastTrigger?.focus());
  dialog.querySelector('[data-skip-email]').addEventListener('click', event => {
    event.preventDefault();
    finishDownload();
  });
  form.addEventListener('submit', async event => {
    event.preventDefault();
    if (!form.reportValidity()) return;
    const fields = new FormData(form);
    submit.disabled = true;
    submit.textContent = 'Saving your choice…';
    status.textContent = '';
    try {
      const response = await fetch('/api/subscribe', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          email: fields.get('email'),
          consent: fields.get('consent') === 'on',
          website: fields.get('website'),
          consentVersion: 'octadock-marketing-v1',
        }),
        signal: AbortSignal.timeout(10000),
      });
      if (!response.ok) throw new Error('Signup unavailable');
      form.reset();
      finishDownload(true);
    } catch {
      status.textContent = 'We could not save your signup. Try again, or download without email below.';
    } finally {
      submit.disabled = false;
      submit.textContent = 'Subscribe & download';
    }
  });
}

// An explicit skip or successful signup opens the same styled installation page.
const query = new URLSearchParams(window.location.search);
if (installer && query.get('start') === '1') {
  history.replaceState(null, '', window.location.pathname);
  finishDownload(query.get('subscribed') === '1');
}
