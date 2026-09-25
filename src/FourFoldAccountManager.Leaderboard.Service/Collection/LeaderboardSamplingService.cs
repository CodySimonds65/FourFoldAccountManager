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
            var priorPass = _schedule.LastPass;
            var idle = priorPass is null ? TimeSpan.Zero : now - priorPass.FinishedAtUtc;
            var continuous = priorPass is not null && idle >= TimeSpan.Zero &&
                idle <= options.MinimumSampleInterval * 3;
            var sampledPlayerIds = new HashSet<int>();
            var active = await store.GetActiveProfilesAsync(now - options.ActiveLeaseDuration, ct);
            var profiles = active.GroupBy(x => x.PlayerId).Select(x => x.First())
                .OrderBy(x => x.PlayerId).ToArray();
            var fetched = false;
            foreach (var profile in profiles)
            {
                ct.ThrowIfCancellationRequested();
                var pending = await store.GetActiveProfilesNeedingBaselineAsync(
                    now + _clock.GetElapsedTime(start) - options.ActiveLeaseDuration, ct);
                var priority = pending.FirstOrDefault(x => !sampledPlayerIds.Contains(x.PlayerId));
                if (priority is not null) await SampleActiveAsync(priority);
                if (!sampledPlayerIds.Contains(profile.PlayerId)) await SampleActiveAsync(profile);
            }
            var elapsed = _clock.GetElapsedTime(start);
            _schedule.Complete(now + elapsed, elapsed, sampledPlayerIds);

            async Task SampleActiveAsync(ActiveLeaderboardProfile candidate)
            {
                if (fetched) await Task.Delay(options.MinimumSampleInterval, _clock, ct);
                var observedAt = fetched ? now + _clock.GetElapsedTime(start) : now;
                var currentActive = await store.GetActiveProfilesAsync(observedAt - options.ActiveLeaseDuration, ct);
                var currentProfile = currentActive.FirstOrDefault(x => x.PlayerId == candidate.PlayerId);
                if (currentProfile is null) return;
                var sampled = await SampleAsync(currentProfile.PlayerId, currentProfile.Username,
                    observedAt, now, continuous && priorPass!.SampledPlayerIds.Contains(candidate.PlayerId)
                        ? priorPass.Elapsed + idle : null, ct);
                if (!sampled) return;
                fetched = true;
                sampledPlayerIds.Add(candidate.PlayerId);
            }
        }
        catch
        {
            _schedule.Clear();
            throw;
        }
        finally
        {
            _runGate.Release();
        }
    }

    private async Task<bool> SampleAsync(int playerId, string username, DateTimeOffset now,
        DateTimeOffset passStartedAt, TimeSpan? continuousGap, CancellationToken ct)
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
            await store.MarkNeedsBaselineAsync(playerId, ct);
            return true;
        }
        now += _clock.GetElapsedTime(fetchStarted);

        if (!string.Equals(username.Trim(), current.Username.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            await store.MarkNeedsBaselineAsync(playerId, ct);
            return true;
        }

        if (previous is null || previous.NeedsBaseline)
        {
            await SaveAsync(null);
            return true;
        }

        var maxSampleGap = options.MinimumSampleInterval * 3;
        if (continuousGap is { } knownGap)
            maxSampleGap = TimeSpan.FromTicks(Math.Max(maxSampleGap.Ticks,
                (knownGap + (now - passStartedAt)).Ticks));
        if (now - previous.LastSampledAtUtc > maxSampleGap)
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
            await store.MarkNeedsBaselineAsync(playerId, ct);
            return true;
        }
        await SaveAsync(gain.ValidGain > 0 ? gain.ValidGain : null);
        return true;

        async Task SaveAsync(long? validGain) => await store.SaveObservationAsync(
            new PlayerObservation(playerId, current.Username.Trim(), XpSnapshotJson.Serialize(current),
                validGain, now, false), previous, options.ActiveLeaseDuration, ct);
    }
}
