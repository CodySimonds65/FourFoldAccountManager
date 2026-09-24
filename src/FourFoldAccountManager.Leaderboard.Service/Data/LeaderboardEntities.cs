namespace FourFoldAccountManager.Leaderboard.Service.Data;

public sealed class InstallationParticipationEntity
{
    public Guid InstallationId { get; set; }
    public bool SharingEnabled { get; set; }
    public DateTimeOffset LastHeartbeatAtUtc { get; set; }
    public List<InstallationProfileEntity> Profiles { get; set; } = [];
}

public sealed class InstallationProfileEntity
{
    public Guid InstallationId { get; set; }
    public int PlayerId { get; set; }
    public string Username { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTimeOffset? LastActiveAtUtc { get; set; }
    public InstallationParticipationEntity Installation { get; set; } = null!;
}

public sealed class PlayerSampleStateEntity
{
    public int PlayerId { get; set; }
    public string Username { get; set; } = "";
    public string SnapshotJson { get; set; } = "{}";
    public DateTimeOffset LastSampledAtUtc { get; set; }
    public bool NeedsBaseline { get; set; }
    public bool HasEverScoredGain { get; set; }
}

public sealed class XpGainEventEntity
{
    public long Id { get; set; }
    public int PlayerId { get; set; }
    public long Gain { get; set; }
    public string Username { get; set; } = "";
    public DateTimeOffset ObservedAtUtc { get; set; }
}
