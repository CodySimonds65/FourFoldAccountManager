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
        var coordinator = new XpTrackerCoordinator(new LocalDataPaths(root.FullName));
        var reset = Guid.NewGuid();
        var untouched = Guid.NewGuid();
        try
        {
            coordinator.Start(reset, null, null);
            coordinator.Start(untouched, null, null);
            WaitForPoll(coordinator);
            Seed(coordinator.GetSessionForTesting(reset));
            Seed(coordinator.GetSessionForTesting(untouched));

            var changed = 0;
            coordinator.Changed += (_, _) => changed++;

            var beforeUnknown = coordinator.GetStates().ToDictionary(state => state.AccountId);
            coordinator.ResetRate(Guid.NewGuid());
            var afterUnknown = coordinator.GetStates().ToDictionary(state => state.AccountId);

            Assert.Equal(0, changed);
            Assert.Equal(beforeUnknown[reset], afterUnknown[reset]);
            Assert.Equal(beforeUnknown[untouched], afterUnknown[untouched]);

            coordinator.ResetRate(reset);

            var states = coordinator.GetStates().ToDictionary(state => state.AccountId);
            Assert.Equal(1, changed);
            Assert.Null(states[reset].RatePerHour);
            Assert.Equal(100, states[reset].SessionGain);
            Assert.Equal(3000, states[untouched].RatePerHour);
            Assert.Equal(100, states[untouched].SessionGain);
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
        var coordinator = new XpTrackerCoordinator(new LocalDataPaths(root.FullName));
        var reset = Guid.NewGuid();
        var untouched = Guid.NewGuid();
        try
        {
            coordinator.Start(reset, null, null);
            coordinator.Start(untouched, null, null);
            WaitForPoll(coordinator);
            Seed(coordinator.GetSessionForTesting(reset));
            Seed(coordinator.GetSessionForTesting(untouched));

            var changed = 0;
            coordinator.Changed += (_, _) => changed++;

            coordinator.ResetAll(reset);
            coordinator.ResetAll(Guid.NewGuid());

            var states = coordinator.GetStates().ToDictionary(state => state.AccountId);
            Assert.Equal(1, changed);
            Assert.Null(states[reset].RatePerHour);
            Assert.Equal(0, states[reset].SessionGain);
            Assert.Equal(3000, states[untouched].RatePerHour);
            Assert.Equal(100, states[untouched].SessionGain);
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

    private static void WaitForPoll(XpTrackerCoordinator coordinator)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (coordinator.GetStates().All(state => state.Status == "Add a ranking username")) return;
            Thread.Sleep(10);
        }

        throw new TimeoutException("The coordinator did not complete its no-network poll.");
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
