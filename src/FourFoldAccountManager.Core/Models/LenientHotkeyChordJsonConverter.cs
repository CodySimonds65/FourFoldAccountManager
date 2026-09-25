using System.Text.Json;
using System.Text.Json.Serialization;

namespace FourFoldAccountManager.Core.Models;

// Timer shortcuts arrived after settings files existed, so a hand-edited or older/newer-build value with the
// wrong JSON shape (a string instead of an object, an out-of-range virtualKey, a non-numeric modifiers) must
// not fail the whole PanelSettings load. This reads the raw JSON element and returns null on any shape or
// range problem instead of throwing; SettingsStore.ValidOrDefault then falls back to that shortcut's default,
// the same way it already handles a semantically-invalid (but well-shaped) chord.
public sealed class LenientHotkeyChordJsonConverter : JsonConverter<GlobalHotkeyChord?>
{
    public override GlobalHotkeyChord? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        JsonElement element;
        try
        {
            element = JsonElement.ParseValue(ref reader);
        }
        catch (JsonException)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!TryGetProperty(element, "virtualKey", out var virtualKeyElement) ||
            virtualKeyElement.ValueKind != JsonValueKind.Number ||
            !virtualKeyElement.TryGetUInt16(out var virtualKey))
        {
            return null;
        }

        if (!TryGetProperty(element, "modifiers", out var modifiersElement) ||
            modifiersElement.ValueKind != JsonValueKind.Number ||
            !modifiersElement.TryGetInt32(out var modifiersValue))
        {
            return null;
        }

        return new GlobalHotkeyChord(virtualKey, (GlobalHotkeyModifiers)modifiersValue);
    }

    public override void Write(Utf8JsonWriter writer, GlobalHotkeyChord? value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options);

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
