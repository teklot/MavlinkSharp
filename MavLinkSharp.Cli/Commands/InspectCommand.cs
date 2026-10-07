using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using MavLinkSharp.Cli.Cli;

namespace MavLinkSharp.Cli.Commands;

/// <summary>
/// Implements <c>mavlinkx inspect</c>: decodes a recorded trace file, including files captured by other tools.
/// </summary>
internal static class InspectCommand
{
    private static class Opt
    {
        internal static readonly Argument<FileInfo> File = new("file")
        {
            Description = "Trace or capture file. '.raw' is scanned as a raw MAVLink byte stream; anything else " +
                          "is read as a JSON lines trace."
        };

        internal static readonly Option<string[]> Filter = new("--filter", "-f")
        {
            Description = "Only show matching messages, for example '30', 'ATTITUDE', 'MSG_*' or a range '40-50'. " +
                          "Repeatable; prefix with '!' to exclude.",
            AllowMultipleArgumentsPerToken = true
        };

        internal static readonly Option<int?> Limit = new("--limit", "-n")
        {
            Description = "Stop after this many packets."
        };

        internal static readonly Option<bool> Verbose = SharedOptions.Verbose;

        internal static readonly Option<bool> Json = SharedOptions.Json;
    }

    internal static Command Create()
    {
        var command = new Command("inspect", "Decode a trace file and report what it contains.")
        {
            Opt.File,
            Opt.Filter,
            Opt.Limit,
            Opt.Verbose,
            Opt.Json
        };

        command.SetAction((parseResult, actionToken) => Run(parseResult, actionToken));

        return command;
    }

    private static Task<int> Run(ParseResult parseResult, CancellationToken cancellationToken)
    {
        FileInfo file = parseResult.GetValue(Opt.File)
                        ?? throw new InvalidOperationException("A file is required.");

        if (!file.Exists)
        {
            Console.Error.WriteLine($"mavlinkx: file not found: {file.FullName}");
            return Task.FromResult(Program.ExitFailure);
        }

        string? dialect = parseResult.GetValue(SharedOptions.Dialect);
        bool verbose = parseResult.GetValue(Opt.Verbose);
        bool asJson = parseResult.GetValue(Opt.Json);
        int? limit = parseResult.GetValue(Opt.Limit);
        var filter = PacketFilter.Parse(parseResult.GetValue(Opt.Filter));

        bool isRaw = TraceWriter.IsRawPath(file.FullName);

        // Prefer the dialect recorded in the trace so a JSON lines trace decodes as it was captured.
        dialect ??= isRaw ? null : TraceReader.ReadDialect(file.FullName);

        var context = DialectResolver.Resolve(dialect);
        var messages = new Dictionary<string, long>(StringComparer.Ordinal);
        long shown = 0;
        long errors = 0;

        using var json = asJson ? new JsonLineWriter() : null;

        try
        {
            if (isRaw)
            {
                TraceReader.ReadRaw(
                    file.FullName,
                    context,
                    frame =>
                    {
                        var record = PacketRecord.FromFrame(frame, includeRaw: false);

                        if (!filter.IsMatch(record.MessageId, record.Name))
                        {
                            return true;
                        }

                        Emit(record, context, verbose, json);
                        Count(messages, record);
                        shown++;
                        return limit is not > 0 || shown < limit.Value;
                    },
                    error =>
                    {
                        errors++;
                        Console.Error.WriteLine(string.Format(
                            CultureInfo.InvariantCulture,
                            "  offset {0}: {1} ({2} bytes skipped)",
                            error.Offset,
                            error.Reason,
                            error.Skipped));
                    },
                    cancellationToken);
            }
            else
            {
                foreach (PacketRecord record in TraceReader.ReadJsonLines(file.FullName, context))
                {
                    if (!filter.IsMatch(record.MessageId, record.Name))
                    {
                        continue;
                    }

                    Emit(record, context, verbose, json);
                    Count(messages, record);
                    shown++;

                    if (limit is > 0 && shown >= limit.Value)
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(Program.ExitCancelled);
        }
        catch (JsonException exception)
        {
            Console.Error.WriteLine($"mavlinkx: malformed JSON lines trace: {exception.Message}");
            return Task.FromResult(Program.ExitFailure);
        }

        WriteSummary(shown, errors, messages, isRaw);
        return Task.FromResult(Program.ExitSuccess);
    }

    private static void Emit(PacketRecord record, MavLinkContext context, bool verbose, JsonLineWriter? json)
    {
        if (json is not null)
        {
            json.Write(writer => PacketFormatter.WriteJson(writer, record));
            return;
        }

        Console.Out.WriteLine($"{record.TimestampUtc:HH:mm:ss.fff} {PacketFormatter.Summarize(record)}");

        if (verbose
            && record.MessageId != 0
            && context.Metadata.MessagesDictionary.TryGetValue(record.MessageId, out Message? message))
        {
            Console.Out.Write(PacketFormatter.Detail(record, message));
        }
    }

    private static void Count(Dictionary<string, long> messages, PacketRecord record)
    {
        string name = string.IsNullOrEmpty(record.Name)
            ? record.MessageId.ToString(CultureInfo.InvariantCulture)
            : record.Name;

        messages[name] = messages.GetValueOrDefault(name) + 1;
    }

    private static void WriteSummary(long shown, long errors, Dictionary<string, long> messages, bool isRaw)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "{0} packets shown{1}",
            shown,
            errors > 0
                ? string.Format(CultureInfo.InvariantCulture, ", {0} undecodable regions", errors)
                : string.Empty));

        foreach (KeyValuePair<string, long> entry in messages.OrderByDescending(e => e.Value))
        {
            Console.Error.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "  {0,-32} {1,8}",
                entry.Key,
                entry.Value));
        }

        if (isRaw && shown == 0 && errors == 0)
        {
            Console.Error.WriteLine("  no MAVLink start markers found; is this a MAVLink capture?");
        }
    }
}