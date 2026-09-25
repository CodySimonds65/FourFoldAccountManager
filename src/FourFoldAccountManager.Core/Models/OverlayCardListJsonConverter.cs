using System.Text.Json;
using System.Text.Json.Serialization;

namespace FourFoldAccountManager.Core.Models;

// Settings files may be hand-edited or written by an older/newer build. Loading settings must
// never throw because of overlay data, so this reads the raw JSON element and skips whatever it
// cannot make sense of instead of failing the whole PanelSettings deserialization.
public sealed class OverlayCardListJsonConverter : JsonConverter<IReadOnlyList<OverlayCardPlacement>>
{
    public override IReadOnlyList<OverlayCardPlacement> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var element = JsonElement.ParseValue(ref reader);
        if (element.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<OverlayCardPlacement>();
        }

        var result = new List<OverlayCardPlacement>();
        foreach (var item in element.EnumerateArray())
        {
            OverlayCardPlacement? placement;
            try
            {
                placement = item.Deserialize<OverlayCardPlacement>(options);
            }
            catch (JsonException)
            {
                continue;
            }

            if (placement is not null)
            {
                result.Add(placement);
            }
        }

        return Array.AsReadOnly(result.ToArray());
    }

    public override void Write(
        Utf8JsonWriter writer,
        IReadOnlyList<OverlayCardPlacement> value,
        JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options);
}
