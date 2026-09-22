using System.IO;
using FourFoldAccountManager.Core.Data;
using FourFoldAccountManager.Core.Tracking;
using FourFoldAccountManager.Desktop.Services;

namespace FourFoldAccountManager.Desktop.Tests;

public sealed class XpTrackerCoordinatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 21, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Reset_rate_only_resets_selected_account_and_notifies_once()
    {
        var root = Directory.CreateTempSubdirectory("fourfold-xp-coordinator-");
        var coordinator = new XpTrackerCoordinator(new LocalDataPaths(root.FullName), _ => Task.CompletedTask);
        var reset = Guid.NewGuid();
        var untouched = Guid.NewGuid();
        try
        {
            coordinator.Start(reset, null, null);
            coordinator.Start(untouched, null, null);
            Seed(coordinator.GetSessionForTesting(reset));
            Seed(coordinator.GetSessionForTesting(untouched));

            var changed = 0;
            coordinator.Changed += (_, _) => changed++;

            var beforeUnknown = coordinator.GetStates().ToDictionary(state => state.AccountId);
            Assert.Equal(10d / 3000d, beforeUnknown[reset].HoursUntilNextLevel);
            coordinator.ResetRate(Guid.NewGuid());
            var afterUnknown = coordinator.GetStates().ToDictionary(state => state.AccountId);

            Assert.Equal(0, changed);
            Assert.Equal(beforeUnknown[reset], afterUnknown[reset]);
            Assert.Equal(beforeUnknown[untouched], afterUnknown[untouched]);

            coordinator.ResetRate(reset);

            var states = coordinator.GetStates().ToDictionary(state => state.AccountId);
            Assert.Equal(1, changed);
            Assert.Null(states[reset].RatePerHour);
            Assert.Null(states[reset].HoursUntilNextLevel);
            Assert.Equal(100, states[reset].SessionGain);
            Assert.Equal(beforeUnknown[untouched], states[untouched]);
        }
        finally
        {
            await coordinator.DisposeAsync();
            root.Delete(true);
        }
    }

    [Fact]
    public async Task Reset_all_resets_selected_account_and_unknown_id_is_a_noop()
    {
        var root = Directory.CreateTempSubdirectory("fourfold-xp-coordinator-");
        var coordinator = new XpTrackerCoordinator(new LocalDataPaths(root.FullName), _ => Task.CompletedTask);
        var reset = Guid.NewGuid();
        var untouched = Guid.NewGuid();
        try
        {
            coordinator.Start(reset, null, null);
            coordinator.Start(untouched, null, null);
            Seed(coordinator.GetSessionForTesting(reset));
            Seed(coordinator.GetSessionForTesting(untouched));

            var changed = 0;
            coordinator.Changed += (_, _) => changed++;

            var before = coordinator.GetStates().ToDictionary(state => state.AccountId);
            Assert.Equal(10d / 3000d, before[reset].HoursUntilNextLevel);
            coordinator.ResetAll(reset);
            coordinator.ResetAll(Guid.NewGuid());

            var states = coordinator.GetStates().ToDictionary(state => state.AccountId);
            Assert.Equal(1, changed);
            Assert.Null(states[reset].RatePerHour);
            Assert.Null(states[reset].HoursUntilNextLevel);
            Assert.Equal(0, states[reset].SessionGain);
            Assert.Equal(before[untouched], states[untouched]);
        }
        finally
        {
            await coordinator.DisposeAsync();
            root.Delete(true);
        }
    }

    private static void Seed(XpTrackingSession? session)
    {
        Assert.NotNull(session);
        session!.ApplySnapshot(Snapshot(13, 800, 910), Start);
        session.ApplySnapshot(Snapshot(13, 900, 910), Start.AddMinutes(2));
    }

    [Fact]
    public async Task Injected_polling_loop_starts_once_and_is_cancelled_on_stop_and_dispose()
    {
        var root = Directory.CreateTempSubdirectory("fourfold-xp-coordinator-");
        var tokens = new List<CancellationToken>();
        var coordinator = new XpTrackerCoordinator(new LocalDataPaths(root.FullName), token =>
        {
            tokens.Add(token);
            return Task.CompletedTask;
        });
        try
        {
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            coordinator.Start(first, null, null);
            coordinator.Start(second, null, null);
            Assert.Single(tokens);
            Assert.False(tokens[0].IsCancellationRequested);

            coordinator.Stop(first);
            Assert.False(tokens[0].IsCancellationRequested);
            coordinator.Stop(second);
            Assert.True(tokens[0].IsCancellationRequested);

            coordinator.Start(first, null, null);
            Assert.Equal(2, tokens.Count);
            Assert.False(tokens[1].IsCancellationRequested);
        }
        finally
        {
            await coordinator.DisposeAsync();
            root.Delete(true);
        }

        Assert.True(tokens[1].IsCancellationRequested);
    }

    private static PlayerProgressSnapshot Snapshot(int level, long currentXp, long nextLevelXp) => new(
        "Desmond",
        "Scout",
        new Dictionary<string, ClassXpSnapshot>
        {
            ["Scout"] = new(level, currentXp, nextLevelXp, null)
        },
        []);
}
