using System.Text.Json;
using FourFoldAccountManager.Core.Tracking;

namespace FourFoldAccountManager.Leaderboard.Service.Data;

/// <summary>Stores only the class XP fields used by XpProgressCalculator.</summary>
public static class XpSnapshotJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(PlayerProgressSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var classes = snapshot.Classes.ToDictionary(
            pair => pair.Key,
            pair => new ClassXp(pair.Value.Level, pair.Value.CurrentXp, pair.Value.NextLevelXp),
            StringComparer.OrdinalIgnoreCase);
        return JsonSerializer.Serialize(new SnapshotData(classes, snapshot.InvalidClasses.ToArray()), Options);
    }

    public static PlayerProgressSnapshot Deserialize(string json, string username)
    {
        var data = Parse(json);
        var classes = data.Classes.ToDictionary(
            pair => pair.Key,
            pair => new ClassProfileSnapshot(pair.Value.Level, pair.Value.CurrentXp,
                pair.Value.NextLevelXp, null),
            StringComparer.OrdinalIgnoreCase);
        return new PlayerProgressSnapshot(username, null, classes, data.InvalidClasses ?? []);
    }

    public static string Normalize(string json)
    {
        var data = Parse(json);
        return JsonSerializer.Serialize(data, Options);
    }

    private static SnapshotData Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("XP snapshot JSON is required.", nameof(json));
        var data = JsonSerializer.Deserialize<SnapshotData>(json, Options);
        if (data?.Classes is null) throw new JsonException("XP snapshot classes are required.");
        return data with { InvalidClasses = data.InvalidClasses ?? [] };
    }

    private sealed record SnapshotData(Dictionary<string, ClassXp> Classes, string[]? InvalidClasses);
    private sealed record ClassXp(int Level, long CurrentXp, long NextLevelXp);
}
