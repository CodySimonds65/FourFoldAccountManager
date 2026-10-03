using System.Text.Json;
using System.Text.Json.Serialization;

namespace FourFoldAccountManager.Core.Models;

// A value with the wrong JSON type (a number or object where a string belongs) reads as null instead of failing the
// whole PanelSettings load; SettingsStore then falls back to that setting's default.
public sealed class LenientStringJsonConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var element = JsonElement.ParseValue(ref reader);
        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options);
}
