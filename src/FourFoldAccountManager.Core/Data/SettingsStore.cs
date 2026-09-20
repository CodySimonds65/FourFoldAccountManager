using System.Text.Json;
using FourFoldAccountManager.Core.Models;

namespace FourFoldAccountManager.Core.Data;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly LocalDataPaths _paths;

    public SettingsStore(LocalDataPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public async Task<PanelSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.SettingsFilePath))
        {
            return PanelSettings.Default;
        }

        try
        {
            await using var stream = new FileStream(
                _paths.SettingsFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var settings = await JsonSerializer.DeserializeAsync<PanelSettings>(
                stream,
                JsonOptions,
                cancellationToken);

            return Validate(settings);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Panel settings are malformed. The original file has been left unchanged.",
                exception);
        }
    }

    public async Task SaveAsync(PanelSettings settings, CancellationToken cancellationToken = default)
    {
        var validatedSettings = Validate(settings);
        Directory.CreateDirectory(_paths.DataRoot);

        var temporaryPath = Path.Combine(
            _paths.DataRoot,
            $".settings-{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, validatedSettings, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_paths.SettingsFilePath))
            {
                File.Replace(temporaryPath, _paths.SettingsFilePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, _paths.SettingsFilePath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static PanelSettings Validate(PanelSettings? settings)
    {
        if (settings is null)
        {
            throw new InvalidDataException("Panel settings must contain a JSON object.");
        }

        if (!Enum.IsDefined(settings.Layout))
        {
            throw new InvalidDataException("Panel settings contain an unknown layout.");
        }

        if (settings.SlotAccountIds is null || settings.SlotAccountIds.Count != 4)
        {
            throw new InvalidDataException("Panel settings must contain exactly four slot assignments.");
        }

        var assignedIds = settings.SlotAccountIds.Where(id => id is not null).Select(id => id!.Value).ToArray();
        if (assignedIds.Any(id => id == Guid.Empty) || assignedIds.Distinct().Count() != assignedIds.Length)
        {
            throw new InvalidDataException("Panel settings contain an empty or duplicated account ID.");
        }

        return new PanelSettings(settings.Layout, settings.SlotAccountIds);
    }
}
