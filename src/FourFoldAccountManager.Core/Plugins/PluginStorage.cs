using System.Text.Json;
using FourFoldAccountManager.Core.Data;

namespace FourFoldAccountManager.Core.Plugins;

// One plugin's private key-value store: a small JSON file, saved atomically. A file that can't be read loads as
// empty, so a damaged store never stops the plugin or the app.
public sealed class PluginStorage(string filePath)
{
    public const int MaximumBytes = 256 * 1024;

    public const int MaximumKeyLength = 64;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, JsonElement>? _values;

    public async Task<JsonElement?> GetAsync(string key)
    {
        ValidateKey(key);
        await _gate.WaitAsync();
        try
        {
            return (await LoadAsync()).TryGetValue(key, out var value) ? value : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetAsync(string key, JsonElement value)
    {
        ValidateKey(key);
        await _gate.WaitAsync();
        try
        {
            var values = await LoadAsync();
            var next = new Dictionary<string, JsonElement>(values, StringComparer.Ordinal) { [key] = value.Clone() };
            if (JsonSerializer.SerializeToUtf8Bytes(next).Length > MaximumBytes)
            {
                throw new PluginApiException("limit-exceeded", "Plugin storage is limited to 256 KB.");
            }

            await AtomicJsonFile.WriteAsync(filePath, next, CancellationToken.None);
            _values = next;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(string key)
    {
        ValidateKey(key);
        await _gate.WaitAsync();
        try
        {
            var values = await LoadAsync();
            if (!values.ContainsKey(key))
            {
                return;
            }

            var next = new Dictionary<string, JsonElement>(values, StringComparer.Ordinal);
            next.Remove(key);
            await AtomicJsonFile.WriteAsync(filePath, next, CancellationToken.None);
            _values = next;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > MaximumKeyLength)
        {
            throw new PluginApiException("invalid-argument", "A storage key must be 1 to 64 characters.");
        }
    }

    private async Task<Dictionary<string, JsonElement>> LoadAsync()
    {
        if (_values is not null)
        {
            return _values;
        }

        try
        {
            await using var stream = File.OpenRead(filePath);
            _values = await JsonSerializer.DeserializeAsync<Dictionary<string, JsonElement>>(stream) ?? [];
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            _values = [];
        }

        return _values;
    }
}
