using FourFoldAccountManager.Core.Tracking;
using FourFoldAccountManager.Leaderboard.Service.Data;

namespace FourFoldAccountManager.Leaderboard.Service.Collection;

public sealed class LeaderboardSamplingService(
    ILeaderboardStore store,
    ILeaderboardPublicProfileSource source,
    LeaderboardCollectionOptions options,
    TimeProvider? timeProvider = null,
    LeaderboardSamplingSchedule? schedule = null)
{
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly LeaderboardSamplingSchedule _schedule = schedule ?? new();

    public async Task RunOnceAsync(DateTimeOffset nowUtc, CancellationToken ct)
    {
        if (!options.CanCollect) return;
        var now = nowUtc.ToUniversalTime();
        await _runGate.WaitAsync(ct);
        try
        {
            var start = _clock.GetTimestamp();
            var attemptedPlayerIds = new HashSet<int>();
            var fetched = false;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (fetched) await Task.Delay(options.RequestSpacing, _clock, ct);
                // Re-read after every request so a profile that just started participating
                // takes the next request slot ahead of routine samples.
                var observedAt = now + _clock.GetElapsedTime(start);
                var due = await store.GetProfilesDueForSampleAsync(observedAt - options.ActiveLeaseDuration,
                    observedAt - options.MinimumSampleInterval, ct);
                var candidate = due.FirstOrDefault(x => !attemptedPlayerIds.Contains(x.PlayerId) &&
                    _schedule.CanSample(x.PlayerId, observedAt));
                if (candidate is null) return;
                attemptedPlayerIds.Add(candidate.PlayerId);
                fetched = await SampleAsync(candidate.PlayerId, candidate.Username, observedAt, ct);
            }
        }
        finally
        {
            _runGate.Release();
        }
    }

    private async Task<bool> SampleAsync(int playerId, string username, DateTimeOffset now, CancellationToken ct)
    {
        var previous = await store.GetPlayerStateAsync(playerId, ct);
        if (previous is not null && !previous.NeedsBaseline &&
            now - previous.LastSampledAtUtc < options.MinimumSampleInterval)
            return false;

        var fetchStarted = _clock.GetTimestamp();
        PlayerProgressSnapshot current;
        try
        {
            current = await source.FetchAsync(playerId, ct);
            if (current is null || string.IsNullOrWhiteSpace(current.Username) ||
                current.Classes is null || current.InvalidClasses is null ||
                current.Classes.Count == 0)
                throw new InvalidDataException("The public profile has no class progression.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await RequestRebaselineAsync();
            return true;
        }
        now += _clock.GetElapsedTime(fetchStarted);

        if (!string.Equals(username.Trim(), current.Username.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            await RequestRebaselineAsync();
            return true;
        }

        if (previous is null || previous.NeedsBaseline)
        {
            await SaveAsync(null);
            return true;
        }

        if (now - previous.LastSampledAtUtc > options.MinimumSampleInterval * 3)
        {
            await SaveAsync(null);
            return true;
        }

        XpGainResult gain;
        PlayerProgressSnapshot before;
        HashSet<string> pending;
        try
        {
            before = XpSnapshotJson.Deserialize(previous.SnapshotJson, previous.Username);
            pending = new HashSet<string>(before.InvalidClasses, StringComparer.OrdinalIgnoreCase);
            var excluded = new HashSet<string>(pending, StringComparer.OrdinalIgnoreCase);
            excluded.UnionWith(current.InvalidClasses);
            gain = XpProgressCalculator.Calculate(
                before with
                {
                    Classes = before.Classes.Where(x => !excluded.Contains(x.Key))
                        .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase),
                    InvalidClasses = []
                },
                current with
                {
                    Classes = current.Classes.Where(x => !excluded.Contains(x.Key))
                        .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase),
                    InvalidClasses = []
                });
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or ArgumentException)
        {
            await SaveAsync(null);
            return true;
        }

        var nextPending = new HashSet<string>(current.InvalidClasses, StringComparer.OrdinalIgnoreCase);
        foreach (var name in pending)
        {
            if (!current.Classes.ContainsKey(name) || current.InvalidClasses.Contains(name, StringComparer.OrdinalIgnoreCase))
                nextPending.Add(name);
        }
        foreach (var name in gain.InvalidClasses)
        {
            if (pending.Contains(name) && current.Classes.ContainsKey(name) &&
                !current.InvalidClasses.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;
            if (before.Classes.ContainsKey(name) || !current.Classes.ContainsKey(name))
                nextPending.Add(name);
        }
        current = current with { InvalidClasses = nextPending.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray() };

        if (gain.ValidClassCount == 0 && !pending.Any(name => current.Classes.ContainsKey(name) &&
                !current.InvalidClasses.Contains(name, StringComparer.OrdinalIgnoreCase)))
        {
            await RequestRebaselineAsync();
            return true;
        }
        await SaveAsync(gain.ValidGain > 0 ? gain.ValidGain : null);
        return true;

        async Task SaveAsync(long? validGain)
        {
            await store.SaveObservationAsync(
                new PlayerObservation(playerId, current.Username.Trim(), XpSnapshotJson.Serialize(current),
                    validGain, now, false), previous, options.ActiveLeaseDuration, ct);
            _schedule.Succeeded(playerId);
        }

        // A pending baseline is otherwise due immediately; wait a full interval before asking again.
        async Task RequestRebaselineAsync()
        {
            await store.MarkNeedsBaselineAsync(playerId, ct);
            _schedule.Defer(playerId, now + options.MinimumSampleInterval);
        }
    }
}
