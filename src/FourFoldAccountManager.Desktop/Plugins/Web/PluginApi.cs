using System.Text.Json;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Overlay;
using FourFoldAccountManager.Core.Plugins;

namespace FourFoldAccountManager.Desktop.Plugins.Web;

// Answers one plugin's API messages. Every request is checked against that plugin's manifest; a bad or hostile
// message gets an error reply (or none) and never throws into the app.
internal sealed class PluginApi(
    PluginManifest manifest,
    PluginTrust trust,
    IPluginHostData host,
    PluginStorage storage,
    PluginHttpFetcher http,
    PluginCardStore cards,
    TimeProvider clock)
{
    public const int MaximumMessageLength = 512 * 1024;

    // Calls still waiting on a reply (storage, a web request). Without a cap a plugin could queue thousands of
    // half-megabyte writes and grow the app's memory without bound.
    public const int MaximumCallsInFlight = 32;

    // Each write rewrites the plugin's whole storage file, so writes are limited, not just their size.
    public const int MaximumStorageWritesPerMinute = 120;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();
    private readonly Queue<long> _storageWrites = new();
    private int _callsInFlight;
    private long? _lastOpened;

    // Returns the reply JSON, or null when the message can't be answered (too large, not JSON, or no id).
    public async Task<string?> HandleAsync(string messageJson)
    {
        JsonElement id = default;
        var counted = false;
        try
        {
            if (messageJson.Length > MaximumMessageLength)
            {
                return null;
            }

            using var document = JsonDocument.Parse(messageJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("id", out var idElement) ||
                idElement.ValueKind != JsonValueKind.Number)
            {
                return null;
            }

            id = idElement.Clone();
            counted = true;
            if (Interlocked.Increment(ref _callsInFlight) > MaximumCallsInFlight)
            {
                return Error(id, "limit-exceeded", "Too many requests at once.");
            }

            var method = root.TryGetProperty("method", out var methodElement) &&
                         methodElement.ValueKind == JsonValueKind.String
                ? methodElement.GetString()
                : null;
            // A message without params gets an empty object, so every later property lookup is safe.
            var parameters = root.TryGetProperty("params", out var paramsElement) &&
                             paramsElement.ValueKind == JsonValueKind.Object
                ? paramsElement
                : EmptyObject;
            var result = await DispatchAsync(method, parameters);
            return JsonSerializer.Serialize(new { id, result }, Json);
        }
        catch (PluginApiException exception)
        {
            return Error(id, exception.Code, exception.Message);
        }
        catch (JsonException)
        {
            return Error(id, "invalid-argument", "The request wasn't valid.");
        }
        catch (Exception)
        {
            return Error(id, "unavailable", "FourFold couldn't complete the request.");
        }
        finally
        {
            if (counted)
            {
                Interlocked.Decrement(ref _callsInFlight);
            }
        }
    }

    private static string? Error(JsonElement id, string code, string message) =>
        id.ValueKind == JsonValueKind.Undefined
            ? null
            : JsonSerializer.Serialize(new { id, error = new { code, message } }, Json);

    private async Task<object?> DispatchAsync(string? method, JsonElement parameters)
    {
        switch (method)
        {
            case "accounts.list":
                return host.GetAccounts();
            case "xp.get":
                return host.GetXp(AccountId(parameters)) ?? throw UnknownAccount();
            case "stats.get":
            {
                var accountId = AccountId(parameters);
                return host.GetAccounts().Any(account => account.Id == accountId)
                    ? host.GetStats(accountId)
                    : throw UnknownAccount();
            }
            case "timer.get":
                return host.GetTimer();
            case "storage.get":
                return await storage.GetAsync(Text(parameters, "key"));
            case "storage.set":
                if (!parameters.TryGetProperty("value", out var value))
                {
                    throw new PluginApiException("invalid-argument", "storage.set needs a value.");
                }

                var setKey = Text(parameters, "key");
                TakeStorageWriteSlot();
                await storage.SetAsync(setKey, value);
                return null;
            case "storage.remove":
                var removeKey = Text(parameters, "key");
                TakeStorageWriteSlot();
                await storage.RemoveAsync(removeKey);
                return null;
            case "cards.set":
                cards.Set(CardKey(parameters), CardContent(parameters));
                return null;
            case "cards.clear":
                cards.Clear(CardKey(parameters));
                return null;
            case "http.fetch":
                return await http.FetchAsync(new PluginHttpRequest(
                    Text(parameters, "url"), OptionalText(parameters, "method"), Headers(parameters),
                    OptionalText(parameters, "body")));
            case "openExternal":
                OpenExternal(Text(parameters, "url"));
                return null;
            default:
                throw new PluginApiException("invalid-argument", $"Unknown method '{method}'.");
        }
    }

    // A rolling minute on the same monotonic clock as openExternal. Only a call refused by this limit isn't recorded; one
    // that is refused later, for its size or its key, has already taken a slot.
    private void TakeStorageWriteSlot()
    {
        lock (_storageWrites)
        {
            var now = clock.GetTimestamp();
            while (_storageWrites.Count > 0 &&
                   clock.GetElapsedTime(_storageWrites.Peek(), now) >= TimeSpan.FromSeconds(60))
            {
                _storageWrites.Dequeue();
            }

            if (_storageWrites.Count >= MaximumStorageWritesPerMinute)
            {
                throw new PluginApiException("limit-exceeded", "A plugin can write to its storage 120 times a minute.");
            }

            _storageWrites.Enqueue(now);
        }
    }

    private static PluginApiException UnknownAccount() => new("invalid-argument", "That account id isn't known.");

    private static string Text(JsonElement parameters, string name) =>
        OptionalText(parameters, name) ?? throw new PluginApiException("invalid-argument", $"'{name}' must be text.");

    private static string? OptionalText(JsonElement parameters, string name) =>
        parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty(name, out var element) &&
        element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static Guid AccountId(JsonElement parameters) =>
        Guid.TryParse(OptionalText(parameters, "accountId"), out var accountId) ? accountId : throw UnknownAccount();

    private static Dictionary<string, string>? Headers(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object || !parameters.TryGetProperty("headers", out var element) ||
            element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in element.EnumerateObject().Where(header => header.Value.ValueKind == JsonValueKind.String))
        {
            result[header.Name] = header.Value.GetString()!;
        }

        return result;
    }

    private OverlayCardKey CardKey(JsonElement parameters)
    {
        var cardId = Text(parameters, "cardId");
        var card = manifest.Cards.FirstOrDefault(candidate => candidate.Id == cardId) ??
                   throw new PluginApiException("not-declared", $"The card '{cardId}' isn't declared in plugin.json.");
        Guid? accountId = null;
        if (card.Scope == OverlayAddOnScope.Account)
        {
            var id = AccountId(parameters);
            if (host.GetAccounts().All(account => account.Id != id))
            {
                throw UnknownAccount();
            }

            accountId = id;
        }
        else if (parameters.TryGetProperty("accountId", out var element) && element.ValueKind != JsonValueKind.Null)
        {
            throw new PluginApiException("invalid-argument", "A global card takes a null accountId.");
        }

        return new OverlayCardKey(OverlayAddOnKind.Plugin, accountId, PluginCardId.Create(manifest.Id, cardId));
    }

    private static PluginCardContent CardContent(JsonElement parameters)
    {
        var summary = OptionalText(parameters, "summary") ?? string.Empty;
        var rows = new List<PluginCardRow>();
        if (parameters.TryGetProperty("rows", out var rowsElement) && rowsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rowsElement.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object)
                {
                    throw new PluginApiException("invalid-argument", "Every row must be an object.");
                }

                double? progress = null;
                if (row.TryGetProperty("progress", out var progressElement) &&
                    progressElement.ValueKind != JsonValueKind.Null)
                {
                    var number = progressElement.ValueKind == JsonValueKind.Number
                        ? progressElement.GetDouble()
                        : double.NaN;
                    if (number is not (>= 0 and <= 1))
                    {
                        throw new PluginApiException("invalid-argument", "A row's progress must be from 0 to 1.");
                    }

                    progress = number;
                }

                rows.Add(new PluginCardRow(
                    OptionalText(row, "label") ?? string.Empty, OptionalText(row, "value") ?? string.Empty, progress));
            }
        }

        if (summary.Length > 40 || rows.Count > 8 || rows.Any(row => row.Label.Length > 40 || row.Value.Length > 40))
        {
            throw new PluginApiException(
                "limit-exceeded", "A card takes a summary of up to 40 characters and up to 8 rows of up to 40 characters.");
        }

        // A line break, a direction override or an invisible separator would let a card's text pose as a different row or
        // hide its end.
        if (PluginText.HasUnsafeCharacter(summary) ||
            rows.Any(row => PluginText.HasUnsafeCharacter(row.Label) || PluginText.HasUnsafeCharacter(row.Value)))
        {
            throw new PluginApiException(
                "invalid-argument", "A card's text can't contain control characters or invisible formatting characters.");
        }

        return new PluginCardContent(summary, rows);
    }

    private void OpenExternal(string url)
    {
        // A "user@" prefix can make a link read as a trusted site while it goes somewhere else.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.UserInfo.Length > 0 || TryGetIdnHost(uri) is not { } idnHost || PluginNetworkPolicy.IsLocalHost(idnHost))
        {
            throw new PluginApiException("invalid-argument", "Only https links can be opened.");
        }

        // A plugin can send the user only where it may go itself: the sites it declared, or anywhere once the hub has
        // cleared it for any website. That list is what the hub shows and what was reviewed.
        if (PluginNetworkPolicy.IsPluginHost(idnHost) || !PluginNetworkPolicy.IsAllowed(uri, manifest, trust))
        {
            throw new PluginApiException("site-not-allowed", "A plugin can only open links on the sites it declares.");
        }

        if (!host.IsPanelShowing(manifest.Id))
        {
            throw new PluginApiException("unavailable", "A link can only be opened while the plugin's panel is showing.");
        }

        // The monotonic timestamp, not the wall clock: setting the system time can't skip the wait.
        var now = clock.GetTimestamp();
        if (_lastOpened is { } last && clock.GetElapsedTime(last, now) < TimeSpan.FromSeconds(2))
        {
            throw new PluginApiException("limit-exceeded", "A plugin can open one link every 2 seconds.");
        }

        _lastOpened = now;
        // The browser gets the host as it was checked (its xn-- form), not a Unicode or fullwidth spelling of it.
        host.OpenInBrowser(new UriBuilder(uri) { Host = idnHost }.Uri);
    }

    // IdnHost throws for some invalid international host names; such a link is a bad argument, not a failure.
    private static string? TryGetIdnHost(Uri uri)
    {
        try
        {
            return uri.IdnHost;
        }
        catch (UriFormatException)
        {
            return null;
        }
    }
}
