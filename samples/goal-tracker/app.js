// Goal tracker: a level goal per account, kept in plugin storage and shown on a card.
const container = document.getElementById('accounts');
let goals = {};

function progressTo(goal, xp) {
  if (!goal || xp.level === null) return null;
  if (xp.level >= goal) return 1;
  const withinLevel = xp.nextLevelXp ? xp.currentXp / xp.nextLevelXp : 0;
  return Math.min(1, (xp.level + withinLevel) / goal);
}

async function render() {
  const accounts = await fourfold.accounts.list();
  // Events can start a second render while this one is waiting, so the page is swapped once, at the end.
  const panels = [];
  for (const account of accounts) {
    const xp = await fourfold.xp.get(account.id);
    const goal = goals[account.id] ?? null;
    const progress = progressTo(goal, xp);

    const card = document.createElement('div');
    card.className = 'account';
    const title = document.createElement('h2');
    title.textContent = `${account.label}: ${xp.className ?? 'no class yet'} ${xp.level ?? ''}`;
    const label = document.createElement('label');
    label.textContent = 'Goal level';
    const input = document.createElement('input');
    input.type = 'number';
    input.min = '1';
    input.value = goal ?? '';
    input.addEventListener('change', async () => {
      const value = Number.parseInt(input.value, 10);
      if (Number.isFinite(value) && value > 0) goals[account.id] = value;
      else delete goals[account.id];
      await fourfold.storage.set('goals', goals);
      await render();
    });
    const bar = document.createElement('div');
    bar.className = 'bar';
    const fill = document.createElement('div');
    fill.style.width = `${Math.round((progress ?? 0) * 100)}%`;
    bar.append(fill);
    card.append(title, label, input, bar);
    panels.push(card);

    if (goal && progress !== null) {
      await fourfold.cards.set('goal', account.id, {
        summary: `Level ${xp.level} of ${goal}`,
        rows: [
          { label: 'Level', value: `${xp.level} / ${goal}`, progress },
          { label: 'XP/hr', value: xp.xpPerHour ? Math.round(xp.xpPerHour).toLocaleString() : '—' }
        ]
      });
    } else {
      await fourfold.cards.clear('goal', account.id);
    }
  }
  container.replaceChildren(...panels);
}

async function start() {
  goals = (await fourfold.storage.get('goals')) ?? {};
  fourfold.accounts.onChanged(render);
  fourfold.xp.onUpdated(render);
  await render();
}

start().catch(error => { container.textContent = `Goal tracker failed: ${error.message}`; });
