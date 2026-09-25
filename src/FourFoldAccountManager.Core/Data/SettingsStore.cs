using System.Text.Json;
using FourFoldAccountManager.Core.Calculation;
using FourFoldAccountManager.Core.Models;
using FourFoldAccountManager.Core.Overlay;
using FourFoldAccountManager.Core.Panel;

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
            // An existing account file may predate the sharing setting. Keep that installation private.
            return Validate(PanelSettings.Default with
            {
                ShareLinkedAccounts = !File.Exists(_paths.AccountsFilePath)
            });
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

        if (settings.SlotAccountIds is null || settings.SlotAccountIds.Count is not (4 or 5))
        {
            throw new InvalidDataException("Panel settings must contain four or five slot assignments.");
        }

        if (settings.RevealXpOverlayTabShortcut is null || !settings.RevealXpOverlayTabShortcut.IsValid)
        {
            throw new InvalidDataException("Panel settings contain an invalid XP overlay reveal shortcut.");
        }

        if (settings.ToggleDividerResizingShortcut is null || !settings.ToggleDividerResizingShortcut.IsValid)
        {
            throw new InvalidDataException("Panel settings contain an invalid divider resizing shortcut.");
        }

        if (settings.TwoByThreeTopRowFraction is < 0.2 or > 0.8)
        {
            throw new InvalidDataException("The 2 × 3 top-row height must be between 20% and 80%.");
        }

        var assignedIds = settings.SlotAccountIds.Where(id => id is not null).Select(id => id!.Value).ToArray();
        if (assignedIds.Any(id => id == Guid.Empty) || assignedIds.Distinct().Count() != assignedIds.Length)
        {
            throw new InvalidDataException("Panel settings contain an empty or duplicated account ID.");
        }

        if (settings.GameViewportSizes is null ||
            settings.GameViewportSizes.Any(entry => entry.Key == Guid.Empty || entry.Value is null || !entry.Value.IsValid))
        {
            throw new InvalidDataException("Panel settings contain an invalid game viewport size.");
        }

        var splitStates = ValidateSplitStates(settings);
        var assignments = settings.SlotAccountIds.Concat(new Guid?[5]).Take(5).ToArray();
        var overlayCards = OverlayCardPolicy.Normalize(settings.OverlayCards, settings.LegacyXpOverlayBoundsByAccount);
        return new PanelSettings(settings.Layout, assignments)
        {
            FillGameToPanel = settings.FillGameToPanel,
            ShareLinkedAccounts = settings.ShareLinkedAccounts,
            ShowFullScreenExitButton = settings.ShowFullScreenExitButton,
            PluginsSidebarExpanded = settings.PluginsSidebarExpanded,
            XpCalculatorTargetLevels = XpCalculatorTargets.Normalize(settings.XpCalculatorTargetLevels),
            RevealXpOverlayTabShortcut = settings.RevealXpOverlayTabShortcut,
            ToggleDividerResizingShortcut = settings.ToggleDividerResizingShortcut,
            TimerSplitShortcut = ValidOrDefault(settings.TimerSplitShortcut, GlobalHotkeyChord.DefaultTimerSplit),
            TimerFinishShortcut = ValidOrDefault(settings.TimerFinishShortcut, GlobalHotkeyChord.DefaultTimerFinish),
            TimerResetShortcut = ValidOrDefault(settings.TimerResetShortcut, GlobalHotkeyChord.DefaultTimerReset),
            TwoByThreeTopRowFraction = settings.TwoByThreeTopRowFraction,
            SplitStates = splitStates,
            GameViewportSizes = new Dictionary<Guid, GameViewportSize>(settings.GameViewportSizes),
            OverlayCards = overlayCards
        };
    }

    // Timer shortcuts arrived after settings files existed, so a bad one falls back instead of blocking the load.
    private static GlobalHotkeyChord ValidOrDefault(GlobalHotkeyChord? chord, GlobalHotkeyChord fallback) =>
        chord is { IsValid: true } ? chord : fallback;

    private static IReadOnlyList<PanelSplitState> ValidateSplitStates(PanelSettings settings)
    {
        if (settings.SplitStates is null)
        {
            throw new InvalidDataException("Panel settings contain invalid split states.");
        }

        var defaults = PanelLayoutPolicy.GetDefaultSplitStates();
        var suppliedStates = new Dictionary<string, PanelSplitState>(StringComparer.Ordinal);
        foreach (var state in settings.SplitStates)
        {
            if (state is null || string.IsNullOrWhiteSpace(state.Id) || !defaults.Any(defaultState => defaultState.Id == state.Id))
            {
                throw new InvalidDataException("Panel settings contain an unknown split state ID.");
            }

            if (!suppliedStates.TryAdd(state.Id, state))
            {
                throw new InvalidDataException("Panel settings contain duplicate split state IDs.");
            }
        }

        return Array.AsReadOnly(defaults.Select(defaultState =>
        {
            if (!suppliedStates.TryGetValue(defaultState.Id, out var suppliedState))
            {
                if (defaultState.Id == "2x3.rows")
                {
                    var legacyState = new PanelSplitState(
                        defaultState.Id,
                        Array.AsReadOnly(new[]
                        {
                            settings.TwoByThreeTopRowFraction,
                            Math.Round(1d - settings.TwoByThreeTopRowFraction, 12)
                        }));
                    try
                    {
                        _ = PanelLayoutPolicy.NormalizeAndValidateSplitState(legacyState, defaultState);
                    }
                    catch (ArgumentException exception)
                    {
                        throw new InvalidDataException("Panel settings contain invalid split state weights.", exception);
                    }

                    return legacyState;
                }

                return defaultState;
            }

            try
            {
                return PanelLayoutPolicy.NormalizeAndValidateSplitState(suppliedState, defaultState);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("Panel settings contain invalid split state weights.", exception);
            }
        }).ToArray());
    }
}
