using System.Text.Json;
using System.Text.Json.Serialization;

namespace FourFoldAccountManager.Core.Models;

// A hand-edited or older/newer-build statsWindow value with the wrong JSON shape must not fail the whole
// PanelSettings load; it reads as null, which opens the window at its default size.
public sealed class LenientStatsWindowPlacementJsonConverter : JsonConverter<StatsWindowPlacement?>
{
    public override StatsWindowPlacement? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var element = JsonElement.ParseValue(ref reader);
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        try
        {
            return element.Deserialize<StatsWindowPlacement>(options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, StatsWindowPlacement? value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options);
}
