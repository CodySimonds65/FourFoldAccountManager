# FourFold plugin author guide

This guide is for people who want to build a FourFold plugin. You need HTML, CSS and JavaScript. Nothing else.

## What a plugin is

A plugin is a folder with a `plugin.json` file and ordinary web files: HTML, JavaScript, CSS, images. FourFold shows the plugin's icon in the plugin strip. Clicking the icon opens the plugin's page as a panel.

The page runs in its own sandboxed browser. It talks to FourFold only through `window.fourfold`, a small API that gives it:

- read-only data from FourFold: the accounts, their XP, stats, equipment, silver, gold and location, and the timer (never logins);
- web requests to the sites the plugin declares;
- a small private store;
- overlay cards, drawn by FourFold.

The page keeps running while its panel is closed, so its cards stay up to date. It stops when the user switches the plugin off or closes FourFold. It runs whenever the plugin is switched on: when FourFold starts, on **Reload**, and on every file save in the dev folder. Keep start-up work light.

The panel is narrow: about 250 px wide in the main window, and as wide as the window allows in the pop-out tools window. Design a single narrow column. FourFold draws a 24-pixel bar at the top of the panel, above your page. It shows the plugin's name, its author and a COMMUNITY tag (DEV for a plugin in the dev folder), and a long name or author is shortened with an ellipsis. Your page gets the rest of the panel.

## Quick start

Users get plugins from the plugin hub: they open the plugin list (the wrench in the plugin strip) and choose **Plugin hub**. The dev folder is for authors, to try a plugin before it is submitted. To submit one, see [Getting on the hub](#getting-on-the-hub).

1. Open the plugin list (the wrench in the plugin strip).
2. Switch on **Developer mode**, at the bottom of the list.
3. Press **Open dev plugins folder**. It is `%LOCALAPPDATA%\FourFoldAccountManager\dev-plugins`.
4. Put a copy of the [plugin template](https://github.com/CodySimonds65/FourFoldAccountManager-plugin-template) in that folder, so that `plugin.json` sits at `dev-plugins\my-plugin\plugin.json`. Either make your own repository from it (**Use this template** on GitHub) and clone that, or download it as a ZIP (**Code**, then **Download ZIP**) and unpack it. Getting on the hub needs the repository, but trying things out doesn't.
5. The plugin appears in the strip and the plugin list with a **DEV** badge.

Change `id`, `name`, `shortLabel` and `author` in `plugin.json` first. The template comes with `fourfold.d.ts`, which gives your editor autocomplete and inline documentation for the API.

Two plugins from the hub are worked examples, each in its own repository. [Goal tracker](https://github.com/CodySimonds65/FourFoldAccountManager-goal-tracker) keeps a goal in storage and fills a card. [Silver tracker](https://github.com/CodySimonds65/FourFoldAccountManager-silver-tracker) reads `fourfold.profile`, keeps its maths in a module, and has a check that runs with Node. [`samples/sandbox-check`](samples/sandbox-check) is a different kind of sample: it tests the sandbox, and contacts example.com each time it runs. See [Check your own plugin](#check-your-own-plugin).

Every folder directly inside `dev-plugins` is one plugin.

- **Saving a file** in a plugin's folder reloads the plugin within about a second. A changed `plugin.json` is read again, including its cards and sites.
- **A rejected `plugin.json`** still gets a row in the plugin list, with the folder name and the reason. Fix the file and it loads.
- **Right-click, Inspect** opens the browser developer tools. This works for plugins in the dev folder only. Blocked requests show up in the console, and so does a rejected call that your code doesn't catch.
- **F5** reloads the plugin's page.
- **Developer mode off** stops every dev plugin and removes it from the strip. Order and on/off state are kept.

Dev plugins are not reviewed, and FourFold honors their `plugin.json` as written. While developer mode is on, a dev plugin runs instead of an installed hub plugin with the same id.

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

A `plugin.json` that breaks any rule below is rejected: the plugin doesn't load, and the plugin list shows the reason. Text values are trimmed. The `name`, `shortLabel`, `author` and `description` fields, and a card's `name`, can't contain control characters (a line break or a bell character, say) or invisible formatting characters (a right-to-left override, say), because FourFold shows them. Fields not listed here are ignored. The file can be at most 64 KB, and it is never served to your page.

| Field | Required | Rule |
|---|---|---|
| `id` | yes | `author.plugin-name`. Lowercase letters and digits, with single dashes allowed between them, in parts joined by dots. At least one dot, at most 64 characters. The first part can't be a Windows device name (`con`, `prn`, `aux`, `nul`, `com0` to `com9`, `lpt0` to `lpt9`), and an id can't begin with the label `xn` (`xn.tools` is rejected). With each dot written as `--`, the id can be at most 63 characters. Two plugins can't share an id. |
| `name` | yes | 1 to 40 characters. |
| `shortLabel` | yes | 1 to 8 characters, shown on the strip. |
| `version` | yes | `MAJOR.MINOR.PATCH`, digits only, such as `1.0.0`. |
| `author` | yes | 1 to 40 characters. |
| `description` | no | At most 200 characters. |
| `apiVersion` | yes | A whole number: the lowest API version the plugin needs. FourFold supports `1` and `2`. Declare `2` if the plugin calls `fourfold.profile` or reads `equipment`, so that an older FourFold tells the user to update instead of running a plugin that can't work. A higher number is rejected with "Update FourFold to use this plugin." |
| `panel` | yes | Path to an existing `.html` file inside the plugin folder. Forward slashes only. |
| `icon` | no | Path to a `.png` file inside the plugin folder, at most 256 by 256 pixels and 64 KB. Forward slashes only. Without one, the strip shows a default icon. |
| `sites` | no | At most 10 websites the plugin may contact. Each is an origin: `https://host` or `https://host:port`, with no path, query or `user@`. The host must be a DNS name: no IP address, no trailing dot, and no name that only works on the local network. `localhost`, `.localhost`, `.local`, `.internal`, `.lan` and `.home.arpa` names are refused, and so is a single-label name such as `router`. A subdomain is its own site, so `https://example.com` doesn't cover `https://www.example.com`. An international host name is fine: FourFold converts it to its `xn--` form, and that form is what it shows users. After the conversion the host can contain only letters, digits, dots and dashes, so a name with an underscore or any other punctuation is refused. |
| `anySite` | no | `true` lets the plugin contact any `https` site. Default `false`. FourFold honors it for plugins in the dev folder. A plugin from the plugin hub gets it only when the maintainers also approve it there. See [Getting on the hub](#getting-on-the-hub). |
| `cards` | no | At most 6 overlay cards. See [Cards](#cards). |

Each entry in `cards`:

| Field | Required | Rule |
|---|---|---|
| `id` | yes | Lowercase letters and digits, with single dashes allowed between them. 1 to 32 characters. Unique in the plugin. |
| `name` | yes | 1 to 24 characters. It is the card's title. |
| `scope` | yes | `account` (one card per open account) or `global` (one card). |

## The API

`window.fourfold` is there before your scripts run. It is frozen, so you can't change it. For autocomplete and inline documentation of everything below, the plugin template has a types file: [`fourfold.d.ts`](https://github.com/CodySimonds65/FourFoldAccountManager-plugin-template/blob/main/fourfold.d.ts).

- Every call returns a promise.
- An `on...` call takes a callback and returns a function that stops the callback.
- A call that is refused rejects with an `Error`. Its `code` property is one of the [error codes](#error-codes) and its `message` says which rule was broken.
- A rejected call never stops your plugin.
- Events can arrive in bursts (`xp.onUpdated` fires once per account), so a handler can start while the last run is still waiting for answers. Run redraws one after another, as the `queue` in [Goal tracker's `app.js`](https://github.com/CodySimonds65/FourFoldAccountManager-goal-tracker/blob/main/app.js) does.

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
| `isStale` | boolean | `true` when there is no XP data yet, when the last read failed, and for a closed account. |

FourFold reads an account's XP only while that account is open, and only if FourFold's own XP tracker can read it. If the XP tracker plugin shows no data for an account, neither will yours. For a closed account the number and text fields are `null`, `classes` is empty and `isStale` is `true`. An id that isn't one of the user's accounts is rejected with `invalid-argument`.

`fourfold.xp.onUpdated(callback)` calls `callback({ accountId })` when that account's XP data changes, about once a minute for each open account.

### Stats

```js
const stats = await fourfold.stats.get(account.id);
if (stats) console.log(stats.className, stats.level, stats.hp, stats.attack);
```

`fourfold.stats.get(accountId)` returns `{ className, level, hp, sp, attack, magic, skill, speed, luck, defense, resistance, equipment }` for the account's active class, or `null` when the account is closed or hasn't been read yet. There is no stats event: read `stats.get` again when `xp.onUpdated` fires. `hp` to `resistance` are numbers, or `null` when unknown. `equipment` is `{ armor, helmet, hair, weapon }`: each is the slot as the profile page words it, at most 64 characters, or `null` when unknown. An empty slot comes through as the page's own word for it (`Empty`, say), not as `null`. A plugin that reads `equipment` needs `"apiVersion": 2`. An unknown account id is rejected with `invalid-argument`.

### Profile

```js
const profile = await fourfold.profile.get(account.id);
if (!profile.isStale && profile.silver !== null) console.log(account.label, 'has', profile.silver, 'silver');
```

`fourfold.profile.get(accountId)` returns facts from the account's public profile page. It needs `"apiVersion": 2`.

| Field | Type | Meaning |
|---|---|---|
| `silver` | number or null | The account's silver. |
| `gold` | number or null | The account's gold. |
| `location` | string or null | Where the character is, as the profile page words it. At most 64 characters. |
| `playerId` | number or null | The account's public player id. Like `inGameName`, it is `null` unless the user has filled in that account's **Ranking username**. It is also `null` until the profile has been read under that name. |
| `updatedAt` | string or null | Time of the last successful read, as an ISO 8601 date. |
| `isStale` | boolean | `true` when there is no data yet, when the last read failed, and for a closed account. |

The data comes from the same read as XP: about once a minute, for open accounts only. There is no profile event: read `profile.get` again when `xp.onUpdated` fires. That event also fires for changes that aren't a new read, and `updatedAt` only changes with a new one, so compare it to tell them apart. After a failed read `silver`, `gold`, `location` and `updatedAt` keep their last values and `isStale` is `true`. For a closed account the number and text fields are `null`. An unknown account id is rejected with `invalid-argument`.

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

- **Where:** a site in your `sites`, or any `https` site if the plugin has `anySite` and FourFold honors it. The host name must match exactly. Plain `http` never works. A site on the local network never works, and FourFold also checks the addresses the host name resolves to.
- **Method:** `GET` (default) or `POST`.
- **`body`:** must be a string, so `JSON.stringify` an object yourself. A body that isn't a string is sent empty, and `body` is ignored for `GET`. It is sent as UTF-8 with a `text/plain` content type unless you set `Content-Type` in `headers`.
- **`headers`:** an object of strings. A name must be a valid header name, and a value must be printable ASCII, or the call fails with `invalid-argument`. `Cookie`, `Host`, `Content-Length`, `Transfer-Encoding`, `Connection`, `Proxy-*` and `Sec-*` are dropped. FourFold sends `User-Agent: FourFold-Plugin/1` unless you set your own.
- **No shared state:** no cookies are kept or sent, and no FourFold credentials are attached.
- **Redirects:** followed up to 5 times. Each one must go to an allowed site. Your headers are not sent again once a redirect leaves the original host or port. `301` and `302` on a `POST`, and `303`, turn the request into a `GET`.
- **Response:** `status` is the HTTP status. A `404` or `500` still resolves, so check `status`. `headers` is an object of the response headers (`Set-Cookie` is left out). Header names come in whatever case the server used, so compare them case-insensitively. `text` is the body read as UTF-8, so it only suits text.
- **Limits:** at most 60 requests a minute (each redirect counts), a 15 second timeout, a 2 MB response.

### Opening a link

```js
button.addEventListener('click', () => {
  fourfold.openExternal('https://wiki.example.com/').catch(error => console.warn(error.code));
});
```

`fourfold.openExternal(url)` opens an `https` link in the user's default browser. The example works because the `plugin.json` above declares `https://wiki.example.com`.

- A URL that isn't a valid `https` link fails with `invalid-argument`. That covers other schemes, a `user@` part, `localhost`, a `.localhost`, `.local`, `.internal`, `.lan` or `.home.arpa` name, a single-label name such as `router`, a local network address, and a host name that can't be converted to a real host name.
- The link must be on one of the plugin's `sites`, with the same host and port, or on any public host if the plugin has `anySite` and FourFold honors it. The path and query can be anything. A link to any other site fails with `site-not-allowed`.
- It works only while the plugin's panel is showing, and only right after the user clicks or presses a key in your page. Otherwise it fails with `unavailable`.
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

The page's own browser storage (IndexedDB, the Cache API, `localStorage`) is capped at about 5 MB per plugin, and a write past the cap fails with a `QuotaExceededError`. Keep settings in `fourfold.storage`: it is the supported place for them.

### Card calls

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
| `site-not-allowed` | `http.fetch` to a site that isn't allowed: not in `sites`, not `https`, or on the local network. This includes a redirect to such a site. `openExternal` to an `https` site the plugin isn't allowed to contact also gets this code. |
| `limit-exceeded` | A size, rate or count limit. See the table below. |
| `unavailable` | FourFold couldn't do it right now: storage can't be read, a web request failed or took longer than 15 seconds, `openExternal` with no recent click or key press, or no panel showing, or an unexpected failure. |

### Limits

| What | Limit |
|---|---|
| One message from your page to FourFold | 512 KB. A larger one isn't sent: the call is rejected with `limit-exceeded`. |
| Calls waiting for an answer at once | 32. More are rejected with `limit-exceeded`. |
| `http.fetch` | 60 requests a minute, 5 redirects, 15 seconds, 2 MB response. |
| Storage | 256 KB in all, 120 writes a minute, keys up to 64 characters. |
| Browser storage | About 5 MB per plugin in IndexedDB, the Cache API and `localStorage`. |
| `openExternal` | One link every 2 seconds. |
| Card text | Summary up to 40 characters, up to 8 rows, label and value up to 40 characters each. |
| Card redraws | At most four a second. |
| One served file | 8 MB. |
| Flood stop | More than 2,000 requests or messages, more than 8 MB of messages, or more than 64 MB of replies, in a second. See [When a plugin is stopped](#when-a-plugin-is-stopped). |

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

When FourFold honors `anySite`, `img-src` and `connect-src` accept `https:` in place of the declared sites. WebSockets still need a declared site. The developer tools console may warn about an unknown `webrtc` directive in this policy. It is harmless.

What that means for you:

- **Scripts** come only from your own files. There are no inline `<script>` blocks, no inline event handlers such as `onclick="..."`, and no `eval` or `new Function`. Put libraries in your folder and load them from there.
- **Styles** come from your own files. Inline `style` attributes and `<style>` blocks are allowed. Stylesheets from other sites (Google Fonts, say) are not.
- **Fonts** come from your own files only.
- **Images** come from your own files, `data:` URLs and your declared sites.
- **`fetch`, XHR and WebSocket** from the page go only to your own address and your declared sites, over `https` and `wss`. Plain `http` never works. A site that doesn't allow cross-origin requests refuses them: use `fourfold.http.fetch` for that site.
- **Blocked outright:** loading anything in a frame, `<object>` and `<embed>`, form submissions, `<base>`, audio and video files, and every kind of worker (Web Workers, shared workers, service workers). `RTCPeerConnection` and the other WebRTC classes are removed.

### The local network

FourFold refuses local names and addresses for page requests and for `http.fetch`. That covers `localhost`, `.localhost`, `.local`, `.internal`, `.lan` and `.home.arpa` names, single-label names such as `router`, and addresses in `127.0.0.0/8`, `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, `169.254.0.0/16`, `100.64.0.0/10`, `::1`, `fc00::/7` and `fe80::/10`, in any spelling. Only `http.fetch` also checks what a host name resolves to, so declare only hosts you trust.

### The browser around your page

- Navigating to any other site is cancelled. Links between your own pages work. New windows are blocked. Downloads are cancelled.
- Every permission request is denied: camera, microphone, location, clipboard read, notifications, screen capture and the rest.
- `alert`, `confirm` and `prompt` do nothing, and so do `window.close()` and `window.print()`.
- Password saving and autofill are off. Sign-in prompts and links to other apps (`mailto:`, say) are cancelled.
- The page has no access to FourFold's objects, to a game's cookies or pages, or to any other plugin.

### When a plugin is stopped

FourFold stops a plugin that:

- sends more than 2,000 requests or messages in one second (every request the page makes counts, including for its own files);
- sends more than 8 MB of messages in one second, or is sent more than 64 MB of FourFold's replies in one second (a loop of `storage.get` calls on a big value can reach it);
- loads more than 32 MB of its own files in one second;
- crashes, or stops responding.

The panel then shows "This plugin stopped." with a **Reload** button. The plugin's cards keep their last data, drawn dimmed, until it runs again. Reload starts the page fresh. Stored data is kept. FourFold and other plugins are not affected.

### Check your own plugin

`samples/sandbox-check` tries the main things above that a plugin must not be able to do, and prints PASS or FAIL for each. It contacts example.com each time it runs, so it needs an internet connection. Its **Flood FourFold** button should stop the plugin.

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

Text with control characters (line breaks, for example) or invisible formatting characters (a right-to-left override, say) fails with `invalid-argument`. A text too long, or too many rows, fails with `limit-exceeded`.

**Clear** it with `fourfold.cards.clear(cardId, accountId)`.

**What the user sees:**

- The card shows the account's label (for an `account` card), the card's `name` as its title, then your rows: label on the left, value on the right, and a thin bar under each row that has `progress`.
- A card with no rows shows "No data yet".
- The `summary` is not drawn on the card. The Overlays panel shows it beside the card's switch. Before your first `set`, the Overlays panel shows "No data yet" there.
- The Overlays panel and the floating checklist list a card as "Card name (plugin short label)", for example "Goal (Goals)", so users can tell your card from another plugin's, and from FourFold's own. The card itself shows only the name.
- A value too long for its row, and a summary too long for the Overlays panel, are trimmed with an ellipsis.
- FourFold redraws cards at most four times a second. Setting the same content again changes nothing.

**How users switch cards on:** a declared card is offered wherever FourFold's own cards are. The Overlays panel has one switch per open account for an `account` card and one switch for a `global` card. The card can then be dragged and resized over the game, or shown in its own floating window when Floating cards mode is on. It starts at 220 by 120 and can shrink to 150 by 44.

If the user switches the plugin off in the plugin list, its cards are hidden everywhere and their placement is kept. If the plugin stops, its cards keep their last data, drawn dimmed.

## What plugins can't do

- Read or control the game, or see its pages, cookies or sessions.
- See logins: usernames, emails and passwords are never exposed. Plugins can't see other plugins or any file outside their own folder either.
- Get real-time data. XP and profile data arrive about once a minute, and the timer sends no tick events.
- Use shortcut keys, sounds or desktop notifications.
- Draw custom cards. Cards are FourFold's data cards.
- Add a settings page behind the cog. Keep settings in your own panel.
- Run while FourFold is closed or the plugin is switched off. (A plugin's page does keep running while its panel is hidden.)
- Print (`window.print()` does nothing), or rely on file pickers, which open the system's own dialog.

## Getting on the hub

FourFold installs plugins only from its plugin hub, and its maintainers review every plugin before it is listed.
The hub is a GitHub repository:
[FourFoldAccountManager-plugin-hub](https://github.com/CodySimonds65/FourFoldAccountManager-plugin-hub).

1. Put your plugin in a public GitHub repository, at the root or in a folder.
2. Test the exact commit you want listed, with developer mode on. The hub's check (step 4) runs on Linux, where
   names are case-sensitive. `panel`, `icon` and `path` must match the real file and folder names letter for
   letter, even though Windows lets a mismatch work in the dev folder.
3. Open a pull request on the hub that adds your entry file, `plugins/<your plugin id>.json`:

   ```json
   {
     "repository": "https://github.com/you/your-plugin",
     "commit": "0123456789abcdef0123456789abcdef01234567"
   }
   ```

   `repository` must be exactly `https://github.com/owner/repo`, with no trailing slash, no `/tree/...` and no `.git`
   at the end. `commit` is the full 40-character commit in lowercase. Add `"path"` when the plugin is in a folder.
   `path` is written with forward slashes, uses only letters, digits, `.`, `_` and `-`, has no trailing slash, no `.`
   or `..` parts, and at most 200 characters. The pull request changes only that one file.
4. A check validates your `plugin.json` with the same rules FourFold uses, builds the package (the zip that users
   install), and scans the files in your repository at that commit for secrets. If the check fails, its summary page
   says why. Fix it in your plugin's repository (then put the new commit in your entry file), or in the entry file
   itself. Then a maintainer reads the code. When they merge, the plugin is on the hub.

The check downloads your whole repository at that commit, not only the plugin's folder. It must be public, under
50 MB as a download, at most 200 MB once unpacked, and hold at most 20,000 files. No path in your repository can
have more than 63 parts, counting the folders from the repository root and the file's name.

To update, raise `version` in `plugin.json` and open a pull request that changes `commit` in your entry file. Users
get the new version automatically once it is merged: FourFold checks the hub when it starts and every 6 hours.

A `version` is three numbers joined by dots, such as `1.4.0`, and each number can be at most 2147483647. The check
refuses a version it can't compare, and an update must raise it, comparing number by number (`1.10.0` is higher than
`1.9.0`).

What goes into the package: `plugin.json` and every file of a type FourFold serves (see
[Where your page runs](#where-your-page-runs)) in the plugin's folder (the repository root, or `path`). At most 500
files and 5 MB in all. Every such file goes in, whether or not your page loads it, so keep tests and screenshots
outside the plugin's folder (put the plugin in a folder and set `path`), or in a folder whose name starts with a
dot. Everything else is left out, such as `README.md` and `LICENSE`, and so is any file or folder whose
name starts with a dot. Your panel page, and everything it loads, must be in the package, so none of them can start
with a dot. The files that go in must also unpack on Windows:

- A file or folder name can't contain `< > " | ? *` or a control character, and can't end in a dot or a space. A
  file with a `\` or a `:` in its path is left out.
- A file or folder can't be named like a Windows device: `con`, `prn`, `aux`, `nul`, `com0` to `com9` or `lpt0` to
  `lpt9`, in any case, alone or before a dot. `aux.js` is refused, and `auxiliary.js` is fine.
- Two files can't differ only by capital letters.

A user can uninstall a plugin: FourFold asks first, and uninstalling deletes the plugin's saved data. If the
maintainers remove a plugin from the hub, FourFold stops it for everyone who has it and shows them why. Its saved
data is kept until they uninstall it.

**Never put a secret key in a plugin.** Everything in a plugin is public and runs on users' machines. A plugin that
needs a secret key can't be listed.

A plugin gets access to any website only when it asks for it in `plugin.json` **and** a maintainer approves it on the
hub. Ask in your pull request and say why. Users see "Can contact any website" next to a plugin that has it in the hub
before they install it. For every other plugin, the hub's **Details** list the sites it declares.

The hub's [README](https://github.com/CodySimonds65/FourFoldAccountManager-plugin-hub#readme) has the rest of the
submission rules: what a pull request may change, the secret scan and any-website access.
