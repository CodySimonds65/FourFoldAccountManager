using System.Text.Json;
using FourFoldAccountManager.Core.Plugins;

namespace FourFoldAccountManager.Desktop.Plugins.Web;

// The window.fourfold library, injected into every plugin document before its own scripts run.
internal static class PluginSdk
{
    private const string Script = """
        (() => {
          // Best effort: the content security policy's webrtc 'block' is the intended lock, but this browser doesn't
          // enforce it yet, and the hub review (part 3) rejects plugins that use WebRTC.
          for (const name of ['RTCPeerConnection', 'webkitRTCPeerConnection', 'RTCDataChannel', 'RTCSessionDescription', 'RTCIceCandidate']) {
            try {
              delete window[name];
              Object.defineProperty(window, name, { value: undefined, configurable: false, writable: false });
            } catch {}
          }
          const info = __FOURFOLD_INFO__;
          const pending = new Map();
          const listeners = new Map();
          let nextId = 1;
          const call = (method, params) => new Promise((resolve, reject) => {
            const id = nextId++;
            const message = { id, method, params: params ?? {} };
            // FourFold drops a message this long without a reply, which would leave the promise waiting for ever.
            if (JSON.stringify(message).length > 512 * 1024) {
              reject(Object.assign(new Error('The message is too large.'), { code: 'limit-exceeded' }));
              return;
            }
            pending.set(id, { resolve, reject });
            window.chrome.webview.postMessage(message);
          });
          window.chrome.webview.addEventListener('message', event => {
            const message = event.data;
            if (!message) return;
            if (message.id !== undefined) {
              const waiting = pending.get(message.id);
              if (!waiting) return;
              pending.delete(message.id);
              if (message.error) {
                waiting.reject(Object.assign(new Error(message.error.message), { code: message.error.code }));
              } else {
                waiting.resolve(message.result);
              }
              return;
            }
            if (message.event) {
              for (const callback of listeners.get(message.event) ?? []) {
                try { callback(message.data); } catch (error) { console.error(error); }
              }
            }
          });
          const on = (name, callback) => {
            const callbacks = listeners.get(name) ?? new Set();
            listeners.set(name, callbacks);
            callbacks.add(callback);
            return () => callbacks.delete(callback);
          };
          const applyTheme = () => {
            for (const [name, value] of Object.entries(info.cssVariables)) {
              document.documentElement.style.setProperty(name, value);
            }
          };
          if (document.documentElement) {
            applyTheme();
          } else {
            // The page's <html> doesn't exist yet when this runs; theme it the moment it does, before the first paint.
            const observer = new MutationObserver(() => {
              if (!document.documentElement) return;
              observer.disconnect();
              applyTheme();
            });
            observer.observe(document, { childList: true });
          }
          window.fourfold = Object.freeze({
            plugin: Object.freeze(info.plugin),
            theme: Object.freeze(info.theme),
            accounts: Object.freeze({
              list: () => call('accounts.list'),
              onChanged: callback => on('accounts.changed', callback)
            }),
            xp: Object.freeze({
              get: accountId => call('xp.get', { accountId }),
              onUpdated: callback => on('xp.updated', callback)
            }),
            stats: Object.freeze({ get: accountId => call('stats.get', { accountId }) }),
            timer: Object.freeze({
              get: () => call('timer.get'),
              onChanged: callback => on('timer.changed', callback)
            }),
            http: Object.freeze({ fetch: (url, options) => call('http.fetch', { url, ...(options ?? {}) }) }),
            openExternal: url => navigator.userActivation && !navigator.userActivation.isActive
              ? Promise.reject(Object.assign(new Error('openExternal needs a click.'), { code: 'unavailable' }))
              : call('openExternal', { url }),
            storage: Object.freeze({
              get: key => call('storage.get', { key }),
              set: (key, value) => call('storage.set', { key, value }),
              remove: key => call('storage.remove', { key })
            }),
            cards: Object.freeze({
              set: (cardId, accountId, content) => call('cards.set', { cardId, accountId, ...(content ?? {}) }),
              clear: (cardId, accountId) => call('cards.clear', { cardId, accountId })
            })
          });
        })();
        """;

    // The app's palette, matching Theme.xaml.
    private static readonly (string Name, string Css, string Color)[] Palette =
    [
        ("background", "--ff-background", "#101419"),
        ("surface", "--ff-surface", "#171D24"),
        ("surfaceRaised", "--ff-surface-raised", "#1D252F"),
        ("border", "--ff-border", "#29333E"),
        ("text", "--ff-text", "#F2F0E9"),
        ("textMuted", "--ff-text-muted", "#98A4B1"),
        ("accent", "--ff-accent", "#E7C16B"),
        ("danger", "--ff-danger", "#E57777")
    ];

    public static string Build(PluginManifest manifest)
    {
        var info = JsonSerializer.Serialize(new
        {
            plugin = new { id = manifest.Id, version = manifest.Version, apiVersion = manifest.ApiVersion },
            theme = Palette.ToDictionary(entry => entry.Name, entry => entry.Color),
            cssVariables = Palette.ToDictionary(entry => entry.Css, entry => entry.Color)
        });
        return Script.Replace("__FOURFOLD_INFO__", info);
    }
}
