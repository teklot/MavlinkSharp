using System.CommandLine;
using System.Globalization;
using MavLinkSharp.Cli.Cli;
using MavLinkSharp.Connection;

namespace MavLinkSharp.Cli.Commands;

/// <summary>
/// Implements <c>mavlinkx replay</c>: sends a recorded trace back out onto a transport.
/// </summary>
internal static class ReplayCommand
{
    private static class Opt
    {
        internal static readonly Argument<FileInfo> Trace = new("trace")
        {
            Description = "Trace file to replay, either '.jsonl' or '.raw'."
        };

        internal static readonly Option<string?> Target = new("--target")
        {
            Description = "Where to send: 'udp://host:14550', 'tcp://host:5760' or 'serial://COM3:57600'. " +
                          "Omit to decode the trace without sending anything."
        };

        internal static readonly Option<double> Speed = new("--speed")
        {
            Description = "Timing multiplier when --timing is set. 0 sends as fast as possible.",
            DefaultValueFactory = _ => 1.0
        };

        internal static readonly Option<bool> Timing = new("--timing")
        {
            Description = "Reproduce the original gaps between packets, scaled by --speed."
        };

        internal static readonly Option<bool> Loop = new("--loop")
        {
            Description = "Replay the trace repeatedly."
        };

        internal static readonly Option<int?> Limit = new("--limit", "-n")
        {
            Description = "Replay at most this many packets per pass."
        };

        internal static readonly Option<bool> DryRun = new("--dry-run")
        {
            Description = "Decode and report, but never open the target transport."
        };
    }

    internal static Command Create()
    {
        var command = new Command("replay", "Replay a recorded trace, optionally sending it to a target.")
        {
            Opt.Trace,
            Opt.Target,
            Opt.Speed,
            Opt.Timing,
            Opt.Loop,
            Opt.Limit,
            Opt.DryRun
        };

        command.SetAction((parseResult, actionToken) => RunAsync(parseResult, actionToken));

        return command;
    }

    private static async Task<int> RunAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        FileInfo trace = parseResult.GetValue(Opt.Trace)
                         ?? throw new InvalidOperationException("A trace file is required.");

        if (!trace.Exists)
        {
            Console.Error.WriteLine($"mavlinkx: trace not found: {trace.FullName}");
            return Program.ExitFailure;
        }

        string? dialect = parseResult.GetValue(SharedOptions.Dialect);
        string? target = parseResult.GetValue(Opt.Target);
        bool dryRun = parseResult.GetValue(Opt.DryRun);
        bool timing = parseResult.GetValue(Opt.Timing);
        bool loop = parseResult.GetValue(Opt.Loop);
        double speed = parseResult.GetValue(Opt.Speed);
        int? limit = parseResult.GetValue(Opt.Limit);

        if (speed < 0)
        {
            Console.Error.WriteLine("mavlinkx: --speed cannot be negative.");
            return Program.ExitFailure;
        }

        // Prefer the dialect recorded in the trace so a replay decodes exactly what was captured.
        dialect ??= TraceReader.ReadDialect(trace.FullName);

        var context = DialectResolver.Resolve(dialect);
        bool isRaw = TraceWriter.IsRawPath(trace.FullName);

        Console.Error.WriteLine($"replaying {trace.FullName}");
        Console.Error.WriteLine($"  format: {(isRaw ? "raw packet bytes" : "JSON lines")}, dialect: {DialectResolver.Normalize(dialect)}");

        if (string.IsNullOrWhiteSpace(target) || dryRun)
        {
            Console.Error.WriteLine("  mode: decode only (no --target)");
            int decoded = isRaw
                ? DecodeRaw(trace.FullName, context, limit, cancellationToken)
                : DecodeJsonLines(trace.FullName, context, limit);

            Console.Error.WriteLine($"decoded {decoded} packets");
            return Program.ExitSuccess;
        }

        ITransport transport = TransportFactory.CreateSender(target);

        try
        {
            await transport.ConnectAsync(cancellationToken);

            int sent = isRaw
                ? await ReplayRawAsync(trace.FullName, context, transport, timing, speed, limit, loop, cancellationToken)
                : await ReplayJsonLinesAsync(trace.FullName, context, transport, timing, speed, limit, loop, cancellationToken);

            Console.Error.WriteLine($"sent {sent} packets to {target}");
            return Program.ExitSuccess;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("mavlinkx: cancelled.");
            return Program.ExitCancelled;
        }
        finally
        {
            try
            {
                await transport.DisconnectAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"mavlinkx: disconnect failed: {exception.Message}");
            }

            await transport.DisposeAsync();
        }
    }

    private static int DecodeRaw(string path, MavLinkContext context, int? limit, CancellationToken cancellationToken)
    {
        long frames = 0;

        TraceReader.ReadRaw(
            path,
            context,
            frame =>
            {
                var record = PacketRecord.FromFrame(frame, includeRaw: false);
                Console.Out.WriteLine($"{record.TimestampUtc:HH:mm:ss.fff} {PacketFormatter.Summarize(record)}");
                frames++;
                return limit is not > 0 || frames < limit.Value;
            },
            error => Console.Error.WriteLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "  offset {0}: {1} ({2} bytes skipped)",
                    error.Offset,
                    error.Reason,
                    error.Skipped)),
            cancellationToken);

        return (int)frames;
    }

    private static int DecodeJsonLines(string path, MavLinkContext context, int? limit)
    {
        List<PacketRecord> records = TraceReader.ReadJsonLines(path, context);
        int shown = 0;

        foreach (PacketRecord record in records)
        {
            Console.Out.WriteLine($"{record.TimestampUtc:HH:mm:ss.fff} {PacketFormatter.Summarize(record)}");

            if (++shown == limit)
            {
                break;
            }
        }

        if (limit is > 0 && records.Count > limit.Value)
        {
            Console.Error.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "  {0} of {1} packets shown (--limit)",
                limit.Value,
                records.Count));
        }

        return shown;
    }

    private static async Task<int> ReplayJsonLinesAsync(
        string path,
        MavLinkContext context,
        ITransport transport,
        bool timing,
        double speed,
        int? limit,
        bool loop,
        CancellationToken cancellationToken)
    {
        List<PacketRecord> records = TraceReader.ReadJsonLines(path, context);
        int sent = 0;
        bool warnedAboutSigning = false;

        do
        {
            DateTime? previous = null;

            foreach (PacketRecord record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (limit is > 0 && sent >= limit.Value)
                {
                    break;
                }

                byte[]? bytes = ToBytes(record, context, ref warnedAboutSigning);

                if (bytes is null)
                {
                    continue;
                }

                if (timing && speed > 0 && previous is not null)
                {
                    TimeSpan gap = record.TimestampUtc - previous.Value;

                    if (gap > TimeSpan.Zero)
                    {
                        await Task.Delay(gap / speed, cancellationToken);
                    }
                }

                previous = record.TimestampUtc;
                await transport.SendAsync(bytes, cancellationToken);
                sent++;
            }
        }
        while (loop && sent < (limit ?? int.MaxValue));

        return sent;
    }

    private static async Task<int> ReplayRawAsync(
        string path,
        MavLinkContext context,
        ITransport transport,
        bool timing,
        double speed,
        int? limit,
        bool loop,
        CancellationToken cancellationToken)
    {
        // Raw traces carry no timestamps, so --timing has no effect. The captured bytes are sent verbatim,
        // so the frames are collected first and then written asynchronously.
        if (timing)
        {
            Console.Error.WriteLine("  note: raw traces carry no timestamps, so --timing has no effect");
        }

        var packets = new List<byte[]>();

        TraceReader.ReadRaw(
            path,
            context,
            frame =>
            {
                byte[] bytes = frame.HasSignature && frame.Signing is null
                    ? TrimSignature(frame.ToBytes())
                    : frame.ToBytes();

                packets.Add(bytes);
                return limit is not > 0 || packets.Count < limit.Value;
            },
            null,
            cancellationToken);

        int sent = 0;

        do
        {
            foreach (byte[] bytes in packets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await transport.SendAsync(bytes, cancellationToken);
                sent++;
            }
        }
        while (loop && (limit is not > 0 || sent < limit.Value));

        return sent;
    }

    /// <summary>
    /// Rebuilds packet bytes from a record, preferring the captured bytes so replays stay byte exact.
    /// </summary>
    private static byte[]? ToBytes(PacketRecord record, MavLinkContext context, ref bool warnedAboutSigning)
    {
        if (record.Raw is { Length: > 0 })
        {
            return record.Raw;
        }

        if (record.Signed && !warnedAboutSigning)
        {
            warnedAboutSigning = true;
            Console.Error.WriteLine("  warning: signed packet cannot be re-encoded without its secret key; sending unsigned");
        }

        if (!context.Metadata.MessagesDictionary.TryGetValue(record.MessageId, out Message? message))
        {
            Console.Error.WriteLine($"  skipping unknown message id {record.MessageId}");
            return null;
        }

        using var frame = new Frame
        {
            Context = context,
            Message = message,
            StartMarker = record.Version == 2 ? Protocol.V2.StartMarker : Protocol.V1.StartMarker,
            PacketSequence = record.Sequence,
            SystemId = record.SystemId,
            ComponentId = record.ComponentId,
            MessageId = record.MessageId
        };

        frame.SetFields(record.Fields);
        return frame.ToBytes();
    }

    private static byte[] TrimSignature(byte[] bytes)
    {
        Array.Resize(ref bytes, bytes.Length - Protocol.V2.SignatureLength);
        return bytes;
    }
}