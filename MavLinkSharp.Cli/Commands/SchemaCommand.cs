using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using MavLinkSharp.Cli.Cli;

namespace MavLinkSharp.Cli.Commands;

/// <summary>
/// Implements <c>mavlinkx schema</c>: introspects the message and enum definitions of a dialect.
/// </summary>
internal static class SchemaCommand
{
    private static class Opt
    {
        internal static readonly Option<string[]> Filter = SharedOptions.Filter;
        internal static readonly Option<bool> Json = SharedOptions.Json;
    }

    internal static Command Create()
    {
        var command = new Command("schema", "Show the message and enum definitions of a dialect.")
        {
            Opt.Filter,
            Opt.Json
        };

        command.Subcommands.Add(CreateMessages());
        command.Subcommands.Add(CreateMessage());
        command.Subcommands.Add(CreateEnums());
        command.Subcommands.Add(CreateEnum());

        return command;
    }

    private static Command CreateMessages()
    {
        var command = new Command("messages", "List every message in the dialect.") { Opt.Filter, Opt.Json };

        command.SetAction((parseResult, _) =>
        {
            MavLinkContext context = Resolve(parseResult);
            string[]? filter = parseResult.GetValue(Opt.Filter);
            bool asJson = parseResult.GetValue(Opt.Json);

            Message[] messages = context.Metadata.MessagesDictionary.Values
                .Where(message => Matches(message.Name, filter))
                .OrderBy(message => message.Id)
                .ToArray();

            using var json = asJson ? new JsonLineWriter() : null;

            foreach (Message message in messages)
            {
                if (json is not null)
                {
                    json.Write(writer =>
                    {
                        writer.WriteStartObject();
                        writer.WriteNumber("id", message.Id);
                        writer.WriteString("name", message.Name);
                        writer.WriteNumber("payloadLength", message.PayloadLength);
                        writer.WriteNumber("maxPayloadLength", message.MaxPayloadLength);
                        writer.WriteNumber("fieldCount", message.OrderedFields.Count);
                        writer.WriteNumber("crcExtra", message.CrcExtra);
                        writer.WriteBoolean("included", message.Included);
                        writer.WriteString("description", message.Description ?? string.Empty);
                        writer.WriteEndObject();
                    });

                    continue;
                }

                Console.Out.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0,6}  {1,-34} payload {2,3}  fields {3,3}",
                    message.Id,
                    message.Name,
                    message.PayloadLength,
                    message.OrderedFields.Count));
            }

            Console.Error.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0} messages in {1}",
                messages.Length,
                DialectResolver.Normalize(parseResult.GetValue(SharedOptions.Dialect))));

            return Task.FromResult(Program.ExitSuccess);
        });

        return command;
    }

    private static Command CreateMessage()
    {
        var nameArgument = new Argument<string>("name")
        {
            Description = "Message name, for example ATTITUDE."
        };

        var command = new Command("message", "Show the field layout of a single message.") { nameArgument, Opt.Json };

        command.SetAction((parseResult, _) =>
        {
            MavLinkContext context = Resolve(parseResult);
            string name = parseResult.GetValue(nameArgument) ?? string.Empty;
            bool asJson = parseResult.GetValue(Opt.Json);

            Message? message = context.Metadata.MessagesDictionary.Values
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)
                    || (uint.TryParse(name, out uint id) && candidate.Id == id));

            if (message is null)
            {
                Console.Error.WriteLine($"mavlinkx: no message named '{name}' in this dialect.");
                return Task.FromResult(Program.ExitFailure);
            }

            using var json = asJson ? new JsonLineWriter() : null;

            if (json is not null)
            {
                json.Write(writer => WriteMessageJson(writer, message));
                return Task.FromResult(Program.ExitSuccess);
            }

            Console.Out.WriteLine($"{message.Name} (id {message.Id})");
            Console.Out.WriteLine($"  description  {message.Description}");
            Console.Out.WriteLine($"  payload      {message.PayloadLength} bytes, up to {message.MaxPayloadLength} with extensions");
            Console.Out.WriteLine($"  crc extra    0x{message.CrcExtra:X2}");
            Console.Out.WriteLine($"  fields       {message.OrderedFields.Count}");

            int index = 0;

            foreach (Field field in message.OrderedFields)
            {
                Console.Out.WriteLine(DescribeField(field, ++index));
            }

            return Task.FromResult(Program.ExitSuccess);
        });

        return command;
    }

    private static Command CreateEnums()
    {
        var command = new Command("enums", "List every enum in the dialect.") { Opt.Filter, Opt.Json };

        command.SetAction((parseResult, _) =>
        {
            MavLinkContext context = Resolve(parseResult);
            string[]? filter = parseResult.GetValue(Opt.Filter);
            bool asJson = parseResult.GetValue(Opt.Json);

            Enum[] enums = context.Metadata.EnumsDictionary.Values
                .Where(value => Matches(value.Name, filter))
                .OrderBy(value => value.Name, StringComparer.Ordinal)
                .ToArray();

            using var json = asJson ? new JsonLineWriter() : null;

            foreach (Enum value in enums)
            {
                if (json is not null)
                {
                    json.Write(writer =>
                    {
                        writer.WriteStartObject();
                        writer.WriteString("name", value.Name);
                        writer.WriteBoolean("bitmask", value.Bitmask);
                        writer.WriteNumber("entryCount", value.Entries.Count);
                        writer.WriteString("description", value.Description ?? string.Empty);
                        writer.WriteEndObject();
                    });

                    continue;
                }

                Console.Out.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0,-40} {1,5} entries{2}",
                    value.Name,
                    value.Entries.Count,
                    value.Bitmask ? "  (bitmask)" : string.Empty));
            }

            Console.Error.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0} enums in {1}",
                enums.Length,
                DialectResolver.Normalize(parseResult.GetValue(SharedOptions.Dialect))));

            return Task.FromResult(Program.ExitSuccess);
        });

        return command;
    }

    private static Command CreateEnum()
    {
        var nameArgument = new Argument<string>("name")
        {
            Description = "Enum name, for example MAV_CMD."
        };

        var command = new Command("enum", "Show every value of a single enum.") { nameArgument, Opt.Json };

        command.SetAction((parseResult, _) =>
        {
            MavLinkContext context = Resolve(parseResult);
            string name = parseResult.GetValue(nameArgument) ?? string.Empty;
            bool asJson = parseResult.GetValue(Opt.Json);

            if (!context.Metadata.EnumsDictionary.TryGetValue(name, out Enum? value)
                && !context.Metadata.EnumsDictionary.TryGetValue(name.ToUpperInvariant(), out value))
            {
                Console.Error.WriteLine($"mavlinkx: no enum named '{name}' in this dialect.");
                return Task.FromResult(Program.ExitFailure);
            }

            using var json = asJson ? new JsonLineWriter() : null;

            foreach (Entry entry in value.Entries.OrderBy(entry => entry.Value))
            {
                if (json is not null)
                {
                    json.Write(writer =>
                    {
                        writer.WriteStartObject();
                        writer.WriteString("name", entry.Name);
                        writer.WriteNumber("value", entry.Value);
                        writer.WriteString("description", entry.Description ?? string.Empty);
                        writer.WriteEndObject();
                    });

                    continue;
                }

                Console.Out.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0,12}  {1,-40} {2}",
                    entry.Value,
                    entry.Name,
                    entry.Description ?? string.Empty));
            }

            return Task.FromResult(Program.ExitSuccess);
        });

        return command;
    }

    private static MavLinkContext Resolve(ParseResult parseResult)
    {
        return DialectResolver.Resolve(parseResult.GetValue(SharedOptions.Dialect));
    }

    private static bool Matches(string name, string[]? filter)
    {
        if (filter is null || filter.Length == 0)
        {
            return true;
        }

        foreach (string pattern in filter)
        {
            if (WildcardMatcher.IsMatch(pattern, name))
            {
                return true;
            }
        }

        return false;
    }

    private static string DescribeField(Field field, int index)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append("  ")
            .Append(index.ToString(CultureInfo.InvariantCulture).PadLeft(3))
            .Append(' ')
            .Append(field.Name.PadRight(28))
            .Append("offset ")
            .Append(field.Offset.ToString(CultureInfo.InvariantCulture).PadLeft(3))
            .Append("  length ")
            .Append(field.Length.ToString(CultureInfo.InvariantCulture).PadLeft(3))
            .Append("  ")
            .Append(field.Type);

        if (field.ArrayLength > 0)
        {
            builder.Append('[').Append(field.ArrayLength).Append(']');
        }

        if (!string.IsNullOrEmpty(field.Units))
        {
            builder.Append("  ").Append(field.Units);
        }

        if (!string.IsNullOrEmpty(field.Enum))
        {
            builder.Append("  ").Append(field.Enum);
        }

        if (field.Extended)
        {
            builder.Append("  (extension)");
        }

        return builder.ToString();
    }

    private static void WriteMessageJson(Utf8JsonWriter writer, Message message)
    {
        writer.WriteStartObject();
        writer.WriteNumber("id", message.Id);
        writer.WriteString("name", message.Name);
        writer.WriteNumber("payloadLength", message.PayloadLength);
        writer.WriteNumber("maxPayloadLength", message.MaxPayloadLength);
        writer.WriteNumber("crcExtra", message.CrcExtra);
        writer.WriteBoolean("included", message.Included);
        writer.WriteString("description", message.Description ?? string.Empty);

        writer.WritePropertyName("fields");
        writer.WriteStartArray();

        foreach (Field field in message.OrderedFields)
        {
            writer.WriteStartObject();
            writer.WriteString("name", field.Name);
            writer.WriteString("type", field.Type);
            writer.WriteNumber("offset", field.Offset);
            writer.WriteNumber("length", field.Length);

            if (field.ArrayLength > 0)
            {
                writer.WriteNumber("arrayLength", field.ArrayLength);
            }

            if (!string.IsNullOrEmpty(field.Units))
            {
                writer.WriteString("units", field.Units);
            }

            if (!string.IsNullOrEmpty(field.Enum))
            {
                writer.WriteString("enum", field.Enum);
            }

            writer.WriteBoolean("extended", field.Extended);
            writer.WriteString("description", field.TagBody ?? string.Empty);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}