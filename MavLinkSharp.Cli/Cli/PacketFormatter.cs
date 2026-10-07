using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MavLinkSharp.Cli.Cli;

/// <summary>
/// Renders and parses <see cref="PacketRecord"/> values as JSON and as human readable text.
/// </summary>
internal static class PacketFormatter
{
    /// <summary>
    /// Writes a packet as a single line JSON object.
    /// </summary>
    internal static void WriteJson(Utf8JsonWriter writer, PacketRecord record)
    {
        writer.WriteStartObject();
        writer.WriteString("record", "packet");
        writer.WriteString("timestamp", record.TimestampUtc.ToString("O", CultureInfo.InvariantCulture));
        writer.WriteNumber("version", record.Version);
        writer.WriteNumber("messageId", record.MessageId);

        if (!string.IsNullOrEmpty(record.Name))
        {
            writer.WriteString("name", record.Name);
        }

        writer.WriteNumber("sequence", record.Sequence);
        writer.WriteNumber("systemId", record.SystemId);
        writer.WriteNumber("componentId", record.ComponentId);
        writer.WriteNumber("payloadLength", record.PayloadLength);
        writer.WriteNumber("checksum", record.Checksum);

        if (record.Signed)
        {
            writer.WriteBoolean("signed", true);
        }

        writer.WritePropertyName("fields");
        writer.WriteStartObject();
        foreach (KeyValuePair<string, object> field in record.Fields)
        {
            writer.WritePropertyName(field.Key);
            WriteValue(writer, field.Value);
        }

        writer.WriteEndObject();

        if (record.Raw is { Length: > 0 })
        {
            writer.WriteString("raw", Convert.ToHexString(record.Raw));
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes a field dictionary as a JSON object.
    /// </summary>
    internal static void WriteFieldsJson(Utf8JsonWriter writer, IReadOnlyDictionary<string, object> fields)
    {
        writer.WriteStartObject();
        foreach (KeyValuePair<string, object> field in fields)
        {
            writer.WritePropertyName(field.Key);
            WriteValue(writer, field.Value);
        }

        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                return;
            case char character:
                writer.WriteStringValue(character.ToString());
                return;
            case char[] characters:
                // Written as text; char[] is a single MAVLink char field, not a list.
                writer.WriteStringValue(new string(characters));
                return;
            case byte byteValue:
                writer.WriteNumberValue(byteValue);
                return;
            case sbyte sbyteValue:
                writer.WriteNumberValue(sbyteValue);
                return;
            case short shortValue:
                writer.WriteNumberValue(shortValue);
                return;
            case ushort ushortValue:
                writer.WriteNumberValue(ushortValue);
                return;
            case int intValue:
                writer.WriteNumberValue(intValue);
                return;
            case uint uintValue:
                writer.WriteNumberValue(uintValue);
                return;
            case long longValue:
                writer.WriteNumberValue(longValue);
                return;
            case ulong ulongValue:
                writer.WriteNumberValue(ulongValue);
                return;
            case float floatValue:
                WriteReal(writer, floatValue);
                return;
            case double doubleValue:
                WriteReal(writer, doubleValue);
                return;
            case bool boolValue:
                writer.WriteBooleanValue(boolValue);
                return;
            case string stringValue:
                writer.WriteStringValue(stringValue);
                return;
        }

        if (value is System.Collections.IEnumerable sequence)
        {
            writer.WriteStartArray();
            foreach (object? item in sequence)
            {
                WriteValue(writer, item);
            }

            writer.WriteEndArray();
            return;
        }

        writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Non finite values are written as strings because JSON has no representation for them.
    /// </summary>
    private static void WriteReal(Utf8JsonWriter writer, double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
            return;
        }

        writer.WriteNumberValue(value);
    }

    /// <summary>
    /// Reads a JSON <c>fields</c> object back into CLR values, using the dialect metadata to restore types.
    /// </summary>
    internal static Dictionary<string, object> ReadFields(JsonElement element, Message? message)
    {
        var fields = new Dictionary<string, object>(StringComparer.Ordinal);

        if (element.ValueKind != JsonValueKind.Object)
        {
            return fields;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (message?.FieldsByName.TryGetValue(property.Name, out Field? field) == true)
            {
                fields[property.Name] = ConvertValue(property.Value, field);
            }
            else
            {
                fields[property.Name] = ToClrValue(property.Value);
            }
        }

        return fields;
    }

    private static object ConvertValue(JsonElement value, Field field)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return field.ArrayLength > 0 ? Array.Empty<byte>() : (object)0;
        }

        Type elementType = field.ElementType ?? typeof(double);

        if (field.ArrayLength > 0)
        {
            return ConvertArray(value, elementType);
        }

        return ConvertScalar(value, elementType);
    }

    private static object ConvertArray(JsonElement value, Type elementType)
    {
        if (elementType == typeof(char) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString()?.ToCharArray() ?? Array.Empty<char>();
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            // Tolerate a scalar where an array was expected by broadcasting it.
            return ConvertScalar(value, elementType);
        }

        var items = value.EnumerateArray().ToArray();

        if (elementType == typeof(byte))
        {
            return items.Select(item => ToByte(item)).ToArray();
        }

        if (elementType == typeof(sbyte))
        {
            return items.Select(item => (sbyte)ToSByte(item)).ToArray();
        }

        if (elementType == typeof(short))
        {
            return items.Select(item => (short)ToInt64(item)).ToArray();
        }

        if (elementType == typeof(ushort))
        {
            return items.Select(item => (ushort)ToUInt64(item)).ToArray();
        }

        if (elementType == typeof(int))
        {
            return items.Select(item => (int)ToInt64(item)).ToArray();
        }

        if (elementType == typeof(uint))
        {
            return items.Select(item => (uint)ToUInt64(item)).ToArray();
        }

        if (elementType == typeof(long))
        {
            return items.Select(item => ToInt64(item)).ToArray();
        }

        if (elementType == typeof(ulong))
        {
            return items.Select(item => ToUInt64(item)).ToArray();
        }

        if (elementType == typeof(float))
        {
            return items.Select(item => (float)ToDouble(item)).ToArray();
        }

        if (elementType == typeof(double))
        {
            return items.Select(item => ToDouble(item)).ToArray();
        }

        if (elementType == typeof(char))
        {
            return items.Select(item => (char)ToInt64(item)).ToArray();
        }

        return items.Select(ToClrValue).ToArray();
    }

    private static object ConvertScalar(JsonElement value, Type type)
    {
        if (type == typeof(char) && value.ValueKind == JsonValueKind.String)
        {
            string? text = value.GetString();
            return text is { Length: > 0 } ? text[0] : '\0';
        }

        if (type == typeof(string))
        {
            return value.ToString();
        }

        if (type == typeof(bool))
        {
            return value.ValueKind == JsonValueKind.True;
        }

        if (type == typeof(float))
        {
            return (float)ToDouble(value);
        }

        if (type == typeof(double))
        {
            return ToDouble(value);
        }

        if (type == typeof(byte))
        {
            return ToByte(value);
        }

        if (type == typeof(sbyte))
        {
            return ToSByte(value);
        }

        if (type == typeof(short))
        {
            return (short)ToInt64(value);
        }

        if (type == typeof(ushort))
        {
            return (ushort)ToUInt64(value);
        }

        if (type == typeof(int))
        {
            return (int)ToInt64(value);
        }

        if (type == typeof(uint))
        {
            return (uint)ToUInt64(value);
        }

        if (type == typeof(long))
        {
            return ToInt64(value);
        }

        if (type == typeof(ulong))
        {
            return ToUInt64(value);
        }

        return ToClrValue(value);
    }

    private static object ToClrValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => ToDouble(value),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => string.Empty,
            _ => value.ToString()
        };
    }

    private static byte ToByte(JsonElement value) => (byte)ToUInt64(value);

    private static sbyte ToSByte(JsonElement value) => (sbyte)ToInt64(value);

    private static long ToInt64(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetInt64(out long integral))
            {
                return integral;
            }

            return (long)value.GetDouble();
        }

        return value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out long parsed)
            ? parsed
            : 0;
    }

    private static ulong ToUInt64(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetUInt64(out ulong integral))
            {
                return integral;
            }

            return value.TryGetInt64(out long signed) ? (ulong)signed : (ulong)value.GetDouble();
        }

        return value.ValueKind == JsonValueKind.String && ulong.TryParse(value.GetString(), out ulong parsed)
            ? parsed
            : 0;
    }

    private static double ToDouble(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            return value.GetDouble();
        }

        return value.ValueKind == JsonValueKind.String
               && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : double.NaN;
    }

    /// <summary>
    /// Formats a single field value for console output.
    /// </summary>
    internal static string FormatValue(object? value)
    {
        switch (value)
        {
            case null:
                return "-";
            case char character:
                return character.ToString();
            case char[] characters:
                return new string(characters);
            case string text:
                return text;
            case byte[] bytes:
                // Byte arrays are identifiers or opaque data; hex stays readable and compact.
                return Convert.ToHexString(bytes);
            case float or double:
                return FormatReal(System.Convert.ToDouble(value, CultureInfo.InvariantCulture));
            case bool boolean:
                return boolean ? "true" : "false";
        }

        if (value is System.Collections.IEnumerable sequence)
        {
            var builder = new StringBuilder("[");
            bool first = true;
            foreach (object? item in sequence)
            {
                if (!first)
                {
                    builder.Append(", ");
                }

                builder.Append(FormatValue(item));
                first = false;
            }

            return builder.Append(']').ToString();
        }

        return System.Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static string FormatReal(double value)
    {
        // Avoid rendering negative zero as "-0".
        if (value == 0)
        {
            return "0";
        }

        return value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Builds the one line summary used by console output.
    /// </summary>
    internal static string Summarize(PacketRecord record)
    {
        var builder = new StringBuilder();
        string name = string.IsNullOrEmpty(record.Name) ? $"MESSAGE_{record.MessageId}" : record.Name;

        builder.Append("V").Append(record.Version).Append(' ');
        builder.Append(name.PadRight(30)).Append(' ');
        builder.Append("id=").Append(record.MessageId).Append(' ');
        builder.Append("sys=").Append(record.SystemId).Append(" comp=").Append(record.ComponentId).Append(' ');
        builder.Append("seq=").Append(record.Sequence).Append(' ');
        builder.Append("len=").Append(record.PayloadLength);

        if (record.Signed)
        {
            builder.Append(" signed");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Builds the indented per field detail block used by verbose output.
    /// </summary>
    internal static string Detail(PacketRecord record, Message? message)
    {
        var builder = new StringBuilder();
        builder.Append("  timestamp  ").Append(record.TimestampUtc.ToString("O", CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("  version    ").Append(record.Version).Append('\n');
        builder.Append("  message    ").Append(record.Name ?? record.MessageId.ToString())
            .Append(" (id ").Append(record.MessageId).Append(")\n");
        builder.Append("  system     ").Append(record.SystemId)
            .Append("  component ").Append(record.ComponentId)
            .Append("  sequence ").Append(record.Sequence).Append('\n');
        builder.Append("  length     ").Append(record.PayloadLength)
            .Append("  checksum 0x").Append(record.Checksum.ToString("X4", CultureInfo.InvariantCulture)).Append('\n');

        if (record.Signed)
        {
            builder.Append("  signed     true\n");
        }

        if (message is not null)
        {
            builder.Append("  fields     ").Append(message.OrderedFields.Count).Append('\n');
        }

        foreach (KeyValuePair<string, object> field in record.Fields)
        {
            builder.Append("    ").Append(field.Key.PadRight(24)).Append("= ")
                .Append(FormatValue(field.Value));

            if (message?.FieldsByName.TryGetValue(field.Key, out Field? definition) == true)
            {
                builder.Append("  [").Append(definition.Type);

                if (!string.IsNullOrEmpty(definition.Units))
                {
                    builder.Append(", ").Append(definition.Units);
                }

                if (!string.IsNullOrEmpty(definition.Enum))
                {
                    builder.Append(", ").Append(definition.Enum);
                }

                builder.Append(']');
            }

            builder.Append('\n');
        }

        if (record.Raw is { Length: > 0 })
        {
            builder.Append("  raw        ").Append(Convert.ToHexString(record.Raw)).Append('\n');
        }

        return builder.ToString();
    }
}