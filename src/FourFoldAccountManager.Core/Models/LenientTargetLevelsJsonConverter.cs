using System.Text.Json;
using System.Text.Json.Serialization;

namespace FourFoldAccountManager.Core.Models;

// XP calculator targets arrived after settings files existed, so a wrong-shaped value loads as "no targets"
// and unreadable entries are skipped instead of failing the whole settings load.
public sealed class LenientTargetLevelsJsonConverter : JsonConverter<IReadOnlyDictionary<Guid, long>>
{
    public override IReadOnlyDictionary<Guid, long> Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var targets = new Dictionary<Guid, long>();
        JsonElement element;
        try
        {
            element = JsonElement.ParseValue(ref reader);
        }
        catch (JsonException)
        {
            return targets;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return targets;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (Guid.TryParse(property.Name, out var accountId) &&
                property.Value.ValueKind == JsonValueKind.Number &&
                property.Value.TryGetInt64(out var level))
            {
                targets.TryAdd(accountId, level);
            }
        }

        return targets;
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<Guid, long> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (accountId, level) in value)
        {
            writer.WriteNumber(accountId.ToString(), level);
        }

        writer.WriteEndObject();
    }
}
