# FourFold plugin author guide

This guide is for people who want to build a FourFold plugin. You need HTML, CSS and JavaScript. Nothing else.

## What a plugin is

A plugin is a folder with a `plugin.json` file and ordinary web files: HTML, JavaScript, CSS, images. FourFold shows the plugin's icon in the plugin strip. Clicking the icon opens the plugin's page as a panel.

The page runs in its own sandboxed browser. It talks to FourFold only through `window.fourfold`, a small API that gives it:

- read-only data from FourFold: the accounts, their XP and stats, and the timer (never logins);
- web requests to the sites the plugin declares;
- a small private store;
- overlay cards, drawn by FourFold.

The page keeps running while its panel is closed, so its cards stay up to date. It stops when the user switches the plugin off or closes FourFold.

## Quick start

1. Open the plugin list (the wrench in the plugin strip).
2. Switch on **Developer mode**, at the bottom of the list.
3. Press **Open dev plugins folder**. It is `%LOCALAPPDATA%\FourFoldAccountManager\dev-plugins`.
4. Copy the `samples/goal-tracker` folder into it, so that `plugin.json` sits at `dev-plugins\goal-tracker\plugin.json`.
5. The plugin appears in the strip and the plugin list with a **DEV** badge.

Every folder directly inside `dev-plugins` is one plugin.

- **Saving a file** in a plugin's folder reloads the plugin within about a second. A changed `plugin.json` is read again, including its cards and sites.
- **A rejected `plugin.json`** still gets a row in the plugin list, with the folder name and the reason. Fix the file and it loads.
- **Right-click, Inspect** opens the browser developer tools. This works for plugins in the dev folder only. Blocked requests and rejected calls show up in the console.
- **F5** reloads the plugin's page.
- **Developer mode off** stops every dev plugin and removes it from the strip. Order and on/off state are kept.

Dev plugins are not reviewed, and FourFold honors their `plugin.json` as written. Right now the dev folder is the only place a community plugin can run.

## plugin.json

```json
{
  "id": "cody.goal-tracker",
  "name": "Goal tracker",
  "shortLabel": "Goals",
  "version": "1.0.0",
  "author": "Cody",
  "description": "Tracks a level goal per account.",
  "apiVersion": 1,
  "panel": "index.html",
  "icon": "icon.png",
  "sites": ["https://wiki.example.com"],
  "anySite": false,
  "cards": [{ "id": "goal", "name": "Goal", "scope": "account" }]
}
```

A `plugin.json` that breaks any rule below is rejected: the plugin doesn't load, and the plugin list shows the reason. Text values are trimmed. Fields not listed here are ignored. The file can be at most 64 KB, and it is never served to your page.

| Field | Required | Rule |
|---|---|---|
| `id` | yes | `author.plugin-name`. Lowercase letters and digits, with single dashes allowed between them, in parts joined by dots. At least one dot, at most 64 characters. The first part can't be a Windows device name (`con`, `prn`, `aux`, `nul`, `com0` to `com9`, `lpt0` to `lpt9`). With each dot written as `--`, the id can be at most 63 characters. Two plugins can't share an id. |
| `name` | yes | 1 to 40 characters. |
| `shortLabel` | yes | 1 to 8 characters, shown on the strip. |
| `version` | yes | `MAJOR.MINOR.PATCH`, digits only, such as `1.0.0`. |
| `author` | yes | 1 to 40 characters. |
| `description` | no | At most 200 characters. |
| `apiVersion` | yes | A whole number. FourFold supports `1`. A higher number is rejected with "Update FourFold to use this plugin." |
| `panel` | yes | Path to an existing `.html` file inside the plugin folder. Forward slashes only. |
| `icon` | no | Path to a `.png` inside the plugin folder, at most 64 KB. Forward slashes only. Without one, the strip shows a default icon. |
| `sites` | no | At most 10 websites the plugin may contact. Each is an origin: `https://host` or `https://host:port`, with no path, query or `user@`. The host must be a DNS name: no IP address, no `localhost` or `.localhost` name, no trailing dot. A subdomain is its own site, so `https://example.com` doesn't cover `https://www.example.com`. |
| `anySite` | no | `true` lets the plugin contact any `https` site. Default `false`. FourFold honors it for plugins in the dev folder. |
| `cards` | no | At most 6 overlay cards. See [Cards](#cards). |

Each entry in `cards`:

| Field | Rule |
|---|---|
| `id` | Lowercase letters and digits, with single dashes allowed between them. 1 to 32 characters. Unique in the plugin. |
| `name` | 1 to 24 characters. It is the card's title. |
| `scope` | `account` (one card per open account) or `global` (one card). |

## The API

`window.fourfold` is there before your scripts run. It is frozen, so you can't change it.

- Every call returns a promise.
- An `on...` call takes a callback and returns a function that stops the callback.
- A call that is refused rejects with an `Error`. Its `code` property is one of the [error codes](#error-codes) and its `message` says which rule was broken.
- A rejected call never stops your plugin.

### Accounts

```js
const accounts = await fourfold.accounts.list();
for (const account of accounts) {
  console.log(account.label, account.inGameName, account.isOpen);
}
fourfold.accounts.onChanged(render);
```

`fourfold.accounts.list()` returns `[{ id, label, inGameName, isOpen }]`.

| Field | Type | Meaning |
|---|---|---|
| `id` | string | The account's id. Pass it to the calls below. |
| `label` | string | The account's label in FourFold. |
| `inGameName` | string or null | The account's public in-game name. It is `null` unless the user has filled in that account's **Ranking username**. When it is set, this is the name read from the account's profile page, or the Ranking username itself until the profile has been read. It never comes from the saved login. |
| `isOpen` | boolean | `true` while the account's game is open. |

`fourfold.accounts.onChanged(callback)` calls `callback` when an account is added, removed, relabeled, opened or closed. The callback gets no data. A change to `inGameName` alone doesn't fire it.

### XP

```js
const xp = await fourfold.xp.get(account.id);
title.textContent = `${account.label}: ${xp.className ?? 'no class yet'} ${xp.level ?? ''}`;
fourfold.xp.onUpdated(({ accountId }) => console.log('new XP data for', accountId));
```

`fourfold.xp.get(accountId)` returns:

| Field | Type | Meaning |
|---|---|---|
| `className` | string or null | The active class. |
| `level` | number or null | The active class's level. |
| `currentXp` | number or null | XP earned so far toward the next level. |
| `nextLevelXp` | number or null | XP the current level needs in total. |
| `xpUntilNextLevel` | number or null | `nextLevelXp - currentXp`. |
| `hoursUntilNextLevel` | number or null | At the current XP/hr. `null` when there is no rate yet. |
| `xpPerHour` | number or null | The current rate. |
| `sessionXp` | number | XP gained this session. `0` when unknown. |
| `classes` | array | `{ className, level, currentXp, nextLevelXp }` for every class. May be empty. |
| `updatedAt` | string or null | Time of the last successful read, as an ISO 8601 date. |
| `isStale` | boolean | `true` when the last read failed, or there is no data. |

FourFold reads an account's XP only while that account is open. For a closed account the number and text fields are `null`, `classes` is empty and `isStale` is `true`. An id that isn't one of the user's accounts is rejected with `invalid-argument`.

`fourfold.xp.onUpdated(callback)` calls `callback({ accountId })` when that account's XP data changes, about once a minute for each open account.

### Stats

```js
const stats = await fourfold.stats.get(account.id);
if (stats) console.log(stats.className, stats.level, stats.hp, stats.attack);
```

`fourfold.stats.get(accountId)` returns `{ className, level, hp, sp, attack, magic, skill, speed, luck, defense, resistance }` for the account's active class, or `null` before the account's first read. `hp` to `resistance` are numbers, or `null` when unknown. An unknown account id is rejected with `invalid-argument`.

### Timer

```js
let timer = await fourfold.timer.get();
let readAt = performance.now();
fourfold.timer.onChanged(async () => { timer = await fourfold.timer.get(); readAt = performance.now(); });
const elapsedMs = () => timer.state === 'running' ? timer.elapsedMs + (performance.now() - readAt) : timer.elapsedMs;
```

`fourfold.timer.get()` returns `{ state, elapsedMs, laps }`.

- `state` is `"ready"`, `"running"` or `"finished"`.
- `elapsedMs` is the time on the timer when you asked.
- `laps` is `[{ number, lapMs, totalMs }]`.

`fourfold.timer.onChanged(callback)` fires when the timer starts, splits, finishes or resets. It doesn't fire on every tick. Animate the time yourself from `elapsedMs`, as above.

### Web requests

```js
const response = await fourfold.http.fetch('https://example.com/');
console.log(response.status, response.text.length);
```

`fourfold.http.fetch(url, { method, headers, body })` returns `{ status, headers, text }`. FourFold makes the request, not your page, so it isn't subject to CORS. Only `url` is required.

- **Where:** a site in your `sites`, or any `https` site if the plugin has `anySite`. The host name must match exactly. Plain `http` never works. A site on the local network never works, and FourFold also checks the addresses the host name resolves to.
- **Method:** `GET` (default) or `POST`.
- **`body`:** a string. It is sent as UTF-8 with a `text/plain` content type unless you set `Content-Type` in `headers`.
- **`headers`:** an object of strings. A name must be a valid header name, and a value must be printable ASCII, or the call fails with `invalid-argument`. `Cookie`, `Host`, `Content-Length`, `Transfer-Encoding`, `Connection`, `Proxy-*` and `Sec-*` are dropped.
- **No shared state:** no cookies are kept or sent, and no FourFold credentials are attached.
- **Redirects:** followed up to 5 times. Each one must go to an allowed site. Your headers are not sent again once a redirect leaves the original host or port. `301` and `302` on a `POST`, and `303`, turn the request into a `GET`.
- **Response:** `status` is the HTTP status. A `404` or `500` still resolves, so check `status`. `headers` is an object of the response headers (`Set-Cookie` is left out). `text` is the body read as UTF-8, so it only suits text.
- **Limits:** at most 60 requests a minute (each redirect counts), a 15 second timeout, a 2 MB response.

### Opening a link

```js
button.addEventListener('click', () => {
  fourfold.openExternal('https://wiki.example.com/').catch(error => console.warn(error.code));
});
```

`fourfold.openExternal(url)` opens an `https` link in the user's default browser.

- The URL must be `https`, with no `user@`, and not `localhost`, a `.localhost` name or a local network address (`invalid-argument`).
- It works only while the plugin's panel is showing, and only in response to a click (`unavailable`).
- At most one link every 2 seconds (`limit-exceeded`).

### Storage

```js
const goals = (await fourfold.storage.get('goals')) ?? {};
await fourfold.storage.set('goals', goals);
await fourfold.storage.remove('goals');
```

Each plugin has a private key-value store that is kept between runs.

- `fourfold.storage.get(key)` returns the value, or `null` if there is none.
- `fourfold.storage.set(key, value)` saves any JSON value. `undefined` is rejected with `invalid-argument`.
- `fourfold.storage.remove(key)` deletes a key.
- Keys are 1 to 64 characters.
- Everything a plugin stores is capped at 256 KB, measured as the saved file. A `set` that would pass the cap is rejected with `limit-exceeded` and leaves existing data as it was.
- `set` and `remove` together are limited to 120 a minute (`limit-exceeded`).
- The data is saved in `%LOCALAPPDATA%\FourFoldAccountManager\plugin-data\<id>.json`. If that file can't be read at the moment (it is locked, say), the call is rejected with `unavailable`. A missing or damaged file loads as empty.

### Cards

```js
await fourfold.cards.set('goal', account.id, {
  summary: 'Level 12 of 50',
  rows: [{ label: 'Level', value: '12 / 50', progress: 0.24 }]
});
await fourfold.cards.clear('goal', account.id);
```

`fourfold.cards.set(cardId, accountId, { summary, rows })` and `fourfold.cards.clear(cardId, accountId)` fill and empty a card you declared in `plugin.json`. See [Cards](#cards).

### Look and identity

`fourfold.theme` holds FourFold's colors as hex strings. They are also set as CSS variables on `:root` before your page paints, so plain CSS can use them. The palette is fixed while the plugin runs.

| `fourfold.theme` | CSS variable | Value |
|---|---|---|
| `background` | `--ff-background` | `#101419` |
| `surface` | `--ff-surface` | `#171D24` |
| `surfaceRaised` | `--ff-surface-raised` | `#1D252F` |
| `border` | `--ff-border` | `#29333E` |
| `text` | `--ff-text` | `#F2F0E9` |
| `textMuted` | `--ff-text-muted` | `#98A4B1` |
| `accent` | `--ff-accent` | `#E7C16B` |
| `danger` | `--ff-danger` | `#E57777` |

```css
body { background: var(--ff-surface); color: var(--ff-text); }
```

`fourfold.plugin` is `{ id, version, apiVersion }`, copied from your `plugin.json`.

### Error codes

| Code | When |
|---|---|
| `invalid-argument` | A parameter is missing or wrong: an unknown account id, a missing or bad key, URL, header or method, control characters or a `progress` outside 0 to 1 in a card, a method FourFold doesn't have. |
| `not-declared` | `cards.set` or `cards.clear` for a card id that isn't in `plugin.json`. |
| `site-not-allowed` | `http.fetch` to a site that isn't allowed: not in `sites`, not `https`, or on the local network. This includes a redirect to such a site. |
| `limit-exceeded` | A size, rate or count limit. See the table below. |
| `unavailable` | FourFold couldn't do it right now: storage can't be read, a web request failed or took longer than 15 seconds, `openExternal` with no click or no panel showing, or an unexpected failure. |

### Limits

| What | Limit |
|---|---|
| One message from your page to FourFold | 512 KB. A larger message gets no reply, so its promise never settles. |
| Calls waiting for an answer at once | 32. More are rejected with `limit-exceeded`. |
| `http.fetch` | 60 requests a minute, 5 redirects, 15 seconds, 2 MB response. |
| Storage | 256 KB in all, 120 writes a minute, keys up to 64 characters. |
| `openExternal` | One link every 2 seconds. |
| Card text | Summary up to 40 characters, up to 8 rows, label and value up to 40 characters each. |
| Card redraws | At most four a second. |
| One served file | 8 MB. |
| Flood stop | See [The sandbox](#the-sandbox). |

## The sandbox

### Where your page runs

FourFold serves your plugin folder at its own web address:

```
https://<id with each "." written as "--">.fourfoldplugin/
```

For `cody.goal-tracker` that is `https://cody--goal-tracker.fourfoldplugin/`. It is the `Origin` that a declared site sees on your page's requests, which matters for CORS. No plugin can reach another plugin's address.

Use relative paths in your HTML. FourFold serves:

- only files inside the plugin folder;
- only `GET` requests;
- only these file types: `.html`, `.js`, `.mjs`, `.css`, `.json`, `.txt`, `.png`, `.jpg`, `.jpeg`, `.gif`, `.svg`, `.webp`, `.woff2`;
- files up to 8 MB each, never `plugin.json`.

Anything else gets a 404.

### What a page may load

Every response carries a content security policy. For a plugin that declares `https://example.com`, it is:

```
default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self'; img-src 'self' data: https://example.com; connect-src 'self' https://example.com wss://example.com; frame-src 'none'; worker-src 'none'; webrtc 'block'; object-src 'none'; base-uri 'none'; form-action 'none'
```

With `anySite`, `img-src` and `connect-src` accept `https:` in place of the declared sites. WebSockets still need a declared site.

What that means for you:

- **Scripts** come only from your own files. There are no inline `<script>` blocks, no inline event handlers such as `onclick="..."`, and no `eval` or `new Function`. Put libraries in your folder and load them from there.
- **Styles** come from your own files. Inline `style` attributes and `<style>` blocks are allowed. Stylesheets from other sites (Google Fonts, say) are not.
- **Fonts** come from your own files only.
- **Images** come from your own files, `data:` URLs and your declared sites.
- **`fetch`, XHR and WebSocket** from the page go only to your own address and your declared sites, over `https` and `wss`. Plain `http` never works. A site that doesn't allow cross-origin requests refuses them: use `fourfold.http.fetch` for that site.
- **Blocked outright:** frames, `<object>` and `<embed>`, form submissions, `<base>`, audio and video files, and every kind of worker (Web Workers, shared workers, service workers). `RTCPeerConnection` and the other WebRTC classes are removed.

### The local network

No page request and no `http.fetch` can reach the user's own network. That covers `localhost`, `.localhost` names, and addresses in `127.0.0.0/8`, `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, `169.254.0.0/16`, `100.64.0.0/10`, `::1`, `fc00::/7` and `fe80::/10`, in any spelling. Page requests are checked by host name or address. `http.fetch` also checks every address a host name resolves to.

### The browser around your page

- Leaving your plugin's page is cancelled. New windows are blocked. Downloads are cancelled.
- Every permission request is denied: camera, microphone, location, clipboard read, notifications, screen capture and the rest.
- `alert`, `confirm` and `prompt` do nothing.
- Password saving and autofill are off. Sign-in prompts and links to other apps (`mailto:`, say) are cancelled.
- The page has no access to FourFold's objects, to a game's cookies or pages, or to any other plugin.

### When a plugin is stopped

FourFold stops a plugin that:

- sends more than 2,000 requests or messages in one second (every request the page makes counts, including for its own files);
- sends more than 8 MB of messages in one second;
- loads more than 32 MB of its own files in one second;
- crashes, or stops responding.

The panel then shows "This plugin stopped." with a **Reload** button. The plugin's cards keep their last data, drawn dimmed, until it runs again. Reload starts the page fresh. Stored data is kept. FourFold and other plugins are not affected.

### Check your own plugin

`samples/sandbox-check` tries everything above that a plugin must not be able to do, and prints PASS or FAIL for each. It needs an internet connection, because it makes one request to example.com. Its **Flood FourFold** button should stop the plugin.

## Cards

A card is a small box FourFold draws over the game, or in a floating window. You send the data and FourFold draws it. You can't draw your own.

**Declare** each card in `plugin.json` under `cards`, with an `id`, a `name` and a `scope`.

- `account` scope: one card per open account. Pass that account's `id` as `accountId`.
- `global` scope: one card in all. Pass `null` as `accountId`.

**Fill** it with `fourfold.cards.set(cardId, accountId, { summary, rows })`:

| Part | Rule |
|---|---|
| `cardId` | A card declared in `plugin.json`, or the call fails with `not-declared`. |
| `accountId` | For an `account` card, the id of one of the user's accounts. For a `global` card, `null`. Anything else fails with `invalid-argument`. |
| `summary` | Optional text, at most 40 characters. |
| `rows` | At most 8 rows. Each is `{ label, value, progress }`. `label` and `value` are strings of at most 40 characters (anything that isn't a string is drawn empty, so convert numbers with `String()`). `progress` is optional: `null`, or a number from 0 to 1. |

Text with control characters (line breaks, for example) fails with `invalid-argument`. A text too long, or too many rows, fails with `limit-exceeded`.

**Clear** it with `fourfold.cards.clear(cardId, accountId)`.

**What the user sees:**

- The card shows the account's label (for an `account` card), the card's `name` as its title, then your rows: label on the left, value on the right, and a thin bar under each row that has `progress`.
- A card with no rows shows "No data yet".
- The `summary` is not drawn on the card. The Overlays panel shows it beside the card's switch. Before your first `set`, the Overlays panel shows "No data yet" there.
- FourFold redraws cards at most four times a second. Setting the same content again changes nothing.

**How users switch cards on:** a declared card is offered wherever FourFold's own cards are. The Overlays panel has one switch per open account for an `account` card and one switch for a `global` card. The card can then be dragged and resized over the game, or shown in its own floating window when Floating cards mode is on. It starts at 220 by 120 and can shrink to 150 by 44.

If the user switches the plugin off in the plugin list, its cards are hidden everywhere and their placement is kept. If the plugin stops, its cards keep their last data, drawn dimmed.

## What plugins can't do

- Read or control the game, or see its pages, cookies or sessions.
- See logins: usernames, emails and passwords are never exposed. Plugins can't see other plugins or any file outside their own folder either.
- Get real-time data. XP data arrives about once a minute, and the timer sends no tick events.
- Use shortcut keys, sounds or desktop notifications.
- Draw custom cards. Cards are FourFold's data cards.
- Add a settings page behind the cog. Keep settings in your own panel.
- Run while FourFold is closed or the plugin is switched off. (A plugin's page does keep running while its panel is hidden.)
- Rely on printing or file pickers. They open the system's own dialogs.

## Getting on the hub

Publishing to the plugin hub, and review by FourFold's maintainers, is coming.
