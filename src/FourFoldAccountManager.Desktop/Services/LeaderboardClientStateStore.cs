using System.Text.Json;
using System.IO;
using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Leaderboard;

namespace FourFoldAccountManager.Desktop.Services;

public sealed record LeaderboardClientState(
    Guid InstallationId,
    IReadOnlyList<LeaderboardPage> CachedPages,
    ParticipationHeartbeat? PendingParticipation);

public sealed class LeaderboardClientStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly LocalDataPaths _paths;

    public LeaderboardClientStateStore(LocalDataPaths paths) => _paths = paths;

    public async Task<LeaderboardClientState> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_paths.LeaderboardStateFilePath))
            return new LeaderboardClientState(Guid.NewGuid(), [], null);
        try
        {
            await using var stream = File.OpenRead(_paths.LeaderboardStateFilePath);
            var value = await JsonSerializer.DeserializeAsync<LeaderboardClientState>(stream, JsonOptions, ct);
            if (value is null || value.InstallationId == Guid.Empty) throw new InvalidDataException();
            return new LeaderboardClientState(value.InstallationId,
                (value.CachedPages ?? []).Where(page => page is not null &&
                    LeaderboardApiClient.IsValidPage(page, page.Period, page.Page, page.PageSize)).ToArray(),
                value.PendingParticipation);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
        {
            return new LeaderboardClientState(Guid.NewGuid(), [], null);
        }
    }

    public async Task SaveAsync(LeaderboardClientState state, CancellationToken ct = default)
    {
        if (state.InstallationId == Guid.Empty) throw new ArgumentException("Installation ID is required.", nameof(state));
        Directory.CreateDirectory(_paths.DataRoot);
        var temporaryPath = Path.Combine(_paths.DataRoot, $".leaderboard-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, ct);
                await stream.FlushAsync(ct);
                stream.Flush(true);
            }
            if (File.Exists(_paths.LeaderboardStateFilePath))
                File.Replace(temporaryPath, _paths.LeaderboardStateFilePath, null);
            else File.Move(temporaryPath, _paths.LeaderboardStateFilePath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
