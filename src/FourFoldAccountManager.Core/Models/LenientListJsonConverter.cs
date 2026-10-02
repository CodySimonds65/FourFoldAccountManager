using System.Text.Json;
using System.Text.Json.Serialization;

namespace FourFoldAccountManager.Core.Models;

// Settings files may be hand-edited or written by an older/newer build. Loading settings must
// never throw because of list data such as overlay cards or tabs, so this reads the raw JSON element and
// skips whatever it cannot make sense of instead of failing the whole PanelSettings deserialization.
public sealed class LenientListJsonConverter<T> : JsonConverter<IReadOnlyList<T>>
    where T : class
{
    public override IReadOnlyList<T> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var element = JsonElement.ParseValue(ref reader);
        if (element.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<T>();
        }

        var result = new List<T>();
        foreach (var item in element.EnumerateArray())
        {
            T? value;
            try
            {
                value = item.Deserialize<T>(options);
            }
            catch (JsonException)
            {
                continue;
            }

            if (value is not null)
            {
                result.Add(value);
            }
        }

        return Array.AsReadOnly(result.ToArray());
    }

    public override void Write(
        Utf8JsonWriter writer,
        IReadOnlyList<T> value,
        JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options);
}
