// Each check tries something a plugin must not be able to do. PASS means FourFold stopped it.
const results = document.getElementById('results');
const violations = [];
document.addEventListener('securitypolicyviolation', event =>
  violations.push({ directive: event.effectiveDirective, blocked: event.blockedURI }));
const settle = () => new Promise(resolve => setTimeout(resolve, 1000));
const violated = (directive, blocked = '') =>
  violations.filter(found => found.directive.startsWith(directive) && found.blocked.startsWith(blocked)).length;

function report(name, passed, detail = '') {
  const item = document.createElement('li');
  item.textContent = `${passed ? 'PASS' : 'FAIL'}: ${name}${detail ? ` (${detail})` : ''}`;
  item.style.color = passed ? 'var(--ff-accent)' : 'var(--ff-danger)';
  results.append(item);
}

async function rejects(name, action, expectedCode) {
  try {
    await action();
    report(name, false, 'it was allowed');
  } catch (error) {
    report(name, !expectedCode || error.code === expectedCode, error.code ?? error.message);
  }
}

async function run() {
  report('Inline script is blocked', window.inlineScriptRan !== true);

  let evalRan = false;
  try { evalRan = new Function('return true')(); } catch { /* blocked */ }
  report('eval is blocked', evalRan !== true);

  // These are stopped by the content security policy, which reports each one as a violation.
  const remote = document.createElement('script');
  remote.src = 'https://example.com/remote.js';
  document.head.append(remote);
  fetch('https://example.org/').catch(() => {});
  try { new WebSocket('wss://example.org/').onerror = () => {}; } catch { /* also blocked */ }
  const frame = document.createElement('iframe');
  frame.src = 'https://example.com/';
  frame.hidden = true;
  document.body.append(frame);
  const popup = window.open('https://example.com/');
  let workerMade = false;
  try { new Worker('check.js'); workerMade = true; } catch { /* blocked */ }
  let serviceWorkerRegistered = false;
  try {
    await navigator.serviceWorker.register('check.js');
    serviceWorkerRegistered = true;
  } catch { /* blocked */ }
  await settle();
  report('A script from another site is blocked, even a declared one',
    violated('script-src-elem', 'https://example.com') > 0);
  report('Page fetch and WebSocket to an undeclared site are blocked', violated('connect-src') >= 2);
  report('Frames are blocked', violated('frame-src') > 0);
  report('Web Workers are blocked', !workerMade || violated('worker-src') > 0);
  report('Service workers are blocked', !serviceWorkerRegistered);
  report('WebRTC is removed', typeof RTCPeerConnection === 'undefined');
  report('alert() shows nothing (check by eye)', alert('This should not appear') === undefined);

  // If window.close() worked, FourFold would be closing instead of getting here.
  let stillRunning = false;
  window.close();
  stillRunning = true;
  report('window.close() does nothing', stillRunning);

  // A sandboxed frame's window has a close() the injected library doesn't replace. FourFold itself must ignore it.
  let stillRunningAfterFrame = false;
  try {
    const sandboxed = document.createElement('iframe');
    sandboxed.sandbox = '';
    sandboxed.hidden = true;
    document.body.append(sandboxed);
    sandboxed.contentWindow.close.call(window);
  } catch { /* refused, which is fine too */ }
  stillRunningAfterFrame = true;
  report('window.close() through a sandboxed frame does nothing', stillRunningAfterFrame);

  report('New windows are blocked', popup === null || popup.closed);

  await rejects('FourFold-run fetch to an undeclared site is refused',
    () => fourfold.http.fetch('https://example.org/'), 'site-not-allowed');
  await rejects('FourFold-run fetch to the local network is refused',
    () => fourfold.http.fetch('https://localhost/'), 'site-not-allowed');
  await rejects('Only GET and POST are accepted',
    () => fourfold.http.fetch('https://example.com/', { method: 'DELETE' }), 'invalid-argument');

  try {
    const response = await fourfold.http.fetch('https://example.com/');
    report('FourFold-run fetch to a declared site works', response.status === 200, `status ${response.status}`);
  } catch (error) {
    report('FourFold-run fetch to a declared site works', false, error.code ?? error.message);
  }

  await rejects('An undeclared card is refused',
    () => fourfold.cards.set('nope', null, { rows: [] }), 'not-declared');
  await rejects('openExternal is refused while the checks run', () => fourfold.openExternal('https://example.com/'));

  await fourfold.storage.set('probe', { at: Date.now() });
  report('Storage round-trips', (await fourfold.storage.get('probe')) !== null);
  await rejects('Storage over 256 KB is refused',
    () => fourfold.storage.set('big', 'x'.repeat(300 * 1024)), 'limit-exceeded');

  // FourFold answers a read at once, so these may never overlap: each is answered, or refused past 32 at a time.
  const many = await Promise.allSettled(Array.from({ length: 40 }, () => fourfold.storage.get('probe')));
  report('More than 32 calls at once are refused or answered',
    many.every(result => result.status === 'fulfilled' || result.reason.code === 'limit-exceeded'));
}

document.getElementById('open').addEventListener('click', () => fourfold.openExternal('https://example.com/'));
document.getElementById('leave').addEventListener('click', () => { location.href = 'https://example.com/'; });
document.getElementById('flood').addEventListener('click', () => {
  // Large messages first, so the budget for message text trips whatever the machine's speed.
  const pad = 'x'.repeat(500000);
  for (let i = 0; i < 20; i++) window.chrome.webview.postMessage({ id: -1 - i, method: 'timer.get', pad });
  for (let i = 0; i < 20000; i++) window.chrome.webview.postMessage({ id: -100 - i, method: 'timer.get' });
});
run().catch(error => report('The check itself ran', false, error.message));
