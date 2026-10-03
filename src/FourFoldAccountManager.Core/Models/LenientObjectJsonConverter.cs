using System.Text.Json;
using System.Text.Json.Serialization;

namespace FourFoldAccountManager.Core.Models;

// A hand-edited or older/newer-build value with the wrong JSON shape (a string instead of an object, a missing or
// out-of-range field) must not fail the whole PanelSettings load. It reads as null instead, and SettingsStore falls
// back to that setting's default.
public sealed class LenientObjectJsonConverter<T> : JsonConverter<T?>
    where T : class
{
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            var element = JsonElement.ParseValue(ref reader);
            return element.ValueKind == JsonValueKind.Object ? element.Deserialize<T>(options) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options);
}
