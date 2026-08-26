using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jundiai.Api;

/// <summary>
/// Produz uma representação JSON determinística antes de calcular hashes persistidos.
/// PostgreSQL jsonb não preserva a representação textual original (ordem de propriedades/whitespace),
/// portanto hashes de integridade devem ser calculados sobre conteúdo canônico, não sobre bytes acidentais.
/// </summary>
public static class DurableJson
{
    public static string SerializeCanonical(object? value, JsonSerializerOptions options) =>
        Canonicalize(JsonSerializer.Serialize(value, options));

    public static string Canonicalize(string json)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false, SkipValidation = false }))
        {
            WriteCanonical(writer, document.RootElement);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string Sha256Canonical(string json) => Sha256Text(Canonicalize(json));

    public static string Sha256Text(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;

            case JsonValueKind.Number:
            {
                var raw = element.GetRawText();
                if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                    writer.WriteNumberValue(integer);
                else if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    writer.WriteNumberValue(number);
                else if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var floating))
                    writer.WriteNumberValue(floating);
                else
                    writer.WriteRawValue(raw, skipInputValidation: false);
                break;
            }

            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidOperationException($"JsonValueKind não suportado para canonicalização: {element.ValueKind}.");
        }
    }
}
