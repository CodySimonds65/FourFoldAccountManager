// Silver tracker: silver earned per hour, session totals and a silver goal for each open account, in the panel and
// on a card. The maths is in rate.mjs.
import { applyRead, createSession, parseGoal, rateAt, toGoal } from './rate.mjs';

const container = document.getElementById('accounts');
const empty = document.getElementById('empty');
const sessions = new Map(); // account id -> session; open accounts only
const blocks = new Map(); // account id -> that account's elements in the panel
let goals = {};

const whole = value => Math.round(value).toLocaleString('en-US');
const signed = value => (value > 0 ? '+' : '') + whole(value);
// A card's text can be at most 40 characters, and FourFold refuses a longer line, so clip it.
const fit = text => (text.length > 40 ? `${text.slice(0, 39)}…` : text);
const goalFor = id => (Number.isSafeInteger(goals[id]) && goals[id] > 0 ? goals[id] : null);

function duration(hours) {
  if (hours >= 10000) return '9,999h+';
  const minutes = Math.max(1, Math.round(hours * 60));
  return minutes < 60 ? `${minutes}m` : `${whole(Math.floor(minutes / 60))}h ${minutes % 60}m`;
}

function goalText(left) {
  if (left.remaining === 0) return 'Reached';
  const toGo = `${whole(left.remaining)} to go`;
  return left.hours === null ? toGo : `${toGo} · ${duration(left.hours)}`;
}

// Everything one account's block and card show.
function describe(account, session, read) {
  const tracked = session.lastAt !== null;
  const balance = tracked ? session.lastBalance : null;
  const rate = tracked ? rateAt(session, session.lastAt) : null;
  const goal = goalFor(account.id);
  return {
    id: account.id,
    label: account.label,
    balance,
    rate,
    earned: session.earned,
    net: tracked ? balance - session.startBalance : null,
    goal,
    left: goal !== null && tracked ? toGoal(goal, balance, rate) : null,
    status: !tracked ? 'No data yet'
      : read.isStale ? 'Stale; the last read failed'
      : rate === null ? 'Collecting baseline' : 'Tracking'
  };
}

async function setCard(view) {
  if (view.balance === null) {
    await fourfold.cards.clear('silver', view.id);
    return;
  }
  const rows = [
    { label: 'Silver/hr', value: fit(view.rate === null ? 'No rate yet' : whole(view.rate)) },
    { label: 'Session', value: fit(`+${whole(view.earned)} (net ${signed(view.net)})`) }
  ];
  if (view.left) rows.push({ label: 'Goal', value: fit(goalText(view.left)), progress: view.left.progress });
  await fourfold.cards.set('silver', view.id, {
    summary: fit(view.rate === null ? 'No rate yet' : `${whole(view.rate)}/hr`),
    rows
  });
}

function element(tag, className, parent) {
  const created = document.createElement(tag);
  if (className) created.className = className;
  parent.append(created);
  return created;
}

// One account's block, built once. Its handlers look the account up by id, so they never hold stale data.
function createBlock(id) {
  const root = document.createElement('div');
  root.className = 'account';
  const parts = { root, title: element('h2', '', root), rate: element('p', 'rate', root) };
  parts.session = element('p', '', root);
  parts.balance = element('p', '', root);
  const label = element('label', '', root);
  label.textContent = 'Goal';
  parts.input = element('input', '', label);
  parts.input.type = 'text';
  parts.input.inputMode = 'numeric';
  parts.input.placeholder = 'Target silver';
  parts.bar = element('div', 'bar', root);
  parts.fill = element('div', '', parts.bar);
  parts.goal = element('p', '', root);
  parts.status = element('p', 'status', root);
  const reset = element('button', '', root);
  reset.type = 'button';
  reset.textContent = 'Reset';

  parts.input.addEventListener('change', async () => {
    const goal = parseGoal(parts.input.value);
    if (goal === null) delete goals[id];
    else goals[id] = goal;
    await saveGoals();
    await render();
  });
  // Leaving the box redraws it at once, so a goal that wasn't accepted doesn't stay on show.
  parts.input.addEventListener('blur', render);
  // Ending the session is enough: the next refresh starts a new one from the latest read.
  reset.addEventListener('click', () => {
    sessions.delete(id);
    render();
  });
  return parts;
}

function updateBlock(parts, view) {
  parts.title.textContent = view.label;
  parts.title.title = view.label;
  parts.rate.textContent = view.rate === null ? 'No rate yet' : `${whole(view.rate)} silver/hr`;
  const tracked = view.balance !== null;
  parts.session.hidden = !tracked;
  parts.balance.hidden = !tracked;
  if (tracked) {
    parts.session.textContent = `Session: +${whole(view.earned)} earned, ${signed(view.net)} net`;
    parts.balance.textContent = `Balance: ${whole(view.balance)}`;
  }
  // Never rewrite the box under the user's cursor.
  if (document.activeElement !== parts.input) parts.input.value = view.goal === null ? '' : whole(view.goal);
  parts.bar.hidden = !view.left;
  parts.goal.hidden = !view.left;
  if (view.left) {
    parts.fill.style.width = `${Math.round(view.left.progress * 100)}%`;
    parts.goal.textContent = goalText(view.left);
  }
  parts.status.textContent = view.status;
}

function paint(views) {
  for (const [id, parts] of blocks) {
    if (views.every(view => view.id !== id)) {
      parts.root.remove();
      blocks.delete(id);
    }
  }
  views.forEach((view, index) => {
    let parts = blocks.get(view.id);
    if (!parts) blocks.set(view.id, (parts = createBlock(view.id)));
    // Moving a block drops its focus, so only move one that is out of place.
    if (container.children[index] !== parts.root) container.insertBefore(parts.root, container.children[index] ?? null);
    updateBlock(parts, view);
  });
  empty.hidden = views.length > 0;
}

async function refresh() {
  const open = (await fourfold.accounts.list()).filter(account => account.isOpen);
  // A closed account's session is over, and its card goes with it.
  for (const id of [...sessions.keys()]) {
    if (open.every(account => account.id !== id)) {
      sessions.delete(id);
      await fourfold.cards.clear('silver', id).catch(() => {});
    }
  }

  const views = [];
  for (const account of open) {
    let session = sessions.get(account.id);
    if (!session) sessions.set(account.id, (session = createSession()));
    const read = await fourfold.profile.get(account.id);
    applyRead(session, read);
    const view = describe(account, session, read);
    // A refused card must not stop the panel from updating.
    await setCard(view).catch(error => console.warn(error.code ?? error.message));
    views.push(view);
  }
  paint(views);
}

// Events arrive in bursts (xp.onUpdated fires once per account), so refreshes run one after another, never
// overlapping.
let queue = Promise.resolve();
const render = () => (queue = queue.then(refresh).catch(error => console.warn(error.code ?? error.message)));

async function saveGoals() {
  // Goals for accounts that no longer exist go, so the store doesn't grow for ever.
  const known = new Set((await fourfold.accounts.list()).map(account => account.id));
  for (const id of Object.keys(goals)) {
    if (!known.has(id)) delete goals[id];
  }
  await fourfold.storage.set('goals', goals);
}

async function start() {
  const saved = await fourfold.storage.get('goals');
  goals = saved && typeof saved === 'object' && !Array.isArray(saved) ? saved : {};
  fourfold.accounts.onChanged(render);
  fourfold.xp.onUpdated(render);
  await render();
}

start().catch(error => { container.textContent = `Silver tracker failed: ${error.message}`; });
