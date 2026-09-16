'use strict';
(() => {
const token = window.location.hash.slice(1);
const form = document.querySelector('#unsubscribe-form');
const status = document.querySelector('#unsubscribe-status');
const button = document.querySelector('#unsubscribe-button');
history.replaceState(null, '', window.location.pathname);
if (!/^[a-f0-9]{64}\.[a-f0-9]{64}$/.test(token)) {
  form.hidden = true;
  status.textContent = 'Open the unsubscribe link in an Octadock email, or contact support@octadock.com to leave the list.';
} else {
  form.addEventListener('submit', async event => {
    event.preventDefault();
    button.disabled = true;
    status.textContent = '';
    try {
      const response = await fetch('/api/unsubscribe', { method: 'POST',
        headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ token }), signal: AbortSignal.timeout(10000) });
      if (!response.ok) throw new Error('Unsubscribe unavailable');
      form.hidden = true;
      status.textContent = 'You are unsubscribed. Your email address has been removed from the Octadock list.';
    } catch {
      status.textContent = 'We could not complete that request. Try again, or contact support@octadock.com.';
      button.disabled = false;
    }
  });
}
})();
