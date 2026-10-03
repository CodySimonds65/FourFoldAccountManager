using System.Text.Json;
using System.Text.Json.Serialization;

namespace FourFoldAccountManager.Core.Models;

// A hand-edited or older/newer-build toolsWindow value with the wrong JSON shape must not fail the whole
// PanelSettings load; it reads as null, so the tools window is closed and opens at its default size when first shown.
public sealed class LenientToolsWindowPlacementJsonConverter : JsonConverter<ToolsWindowPlacement?>
{
    public override ToolsWindowPlacement? Read(
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
            return element.Deserialize<ToolsWindowPlacement>(options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, ToolsWindowPlacement? value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options);
}
