using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using MavLinkSharp.Cli.Cli;
using MavLinkSharp.Connection;

namespace MavLinkSharp.Cli.Commands;

/// <summary>
/// Implements <c>mavlinkx capture</c>: records live traffic from a transport into a trace file.
/// </summary>
internal static class CaptureCommand
{
    /// <summary>
    /// The command's arguments and options. Symbols are held statically so the action can read values from the
    /// parse result by reference.
    /// </summary>
    private static class Opt
    {
        internal static readonly Argument<string> Endpoint = new("endpoint")
        {
            Description = "Endpoint to listen on: 'udp://:14550', 'tcp://:5760' to accept a connection, " +
                          "'tcp://host:5760' to connect out, or 'serial://COM3:57600'."
        };

        internal static readonly Option<FileInfo> Output = new("--output", "-o")
        {
            Description = "Trace file to write. A '.raw' extension stores byte exact packets, anything else is JSON lines.",
            Required = true
        };

        internal static readonly Option<string?> Remote = new("--remote")
        {
            Description = "Fixed UDP peer as 'host:port'. Without it the socket only listens and never sends."
        };

        internal static readonly Option<string[]> Filter = new("--filter", "-f")
        {
            Description = "Only capture matching messages, for example '30', 'ATTITUDE', 'MSG_*' or a range '40-50'. " +
                          "Repeatable; prefix with '!' to exclude.",
            AllowMultipleArgumentsPerToken = true
        };

        internal static readonly Option<bool> IncludeRaw = new("--include-raw")
        {
            Description = "Embed the original packet bytes so 'replay' stays byte exact. Implied by '.raw' output."
        };

        internal static readonly Option<double?> Duration = new("--duration")
        {
            Description = "Stop automatically after this many seconds."
        };

        internal static readonly Option<long?> MaxPackets = new("--max-packets")
        {
            Description = "Stop automatically after this many captured packets."
        };

        internal static readonly Option<byte> SystemId = new("--system-id")
        {
            Description = "System id used for outgoing traffic.",
            DefaultValueFactory = _ => (byte)1
        };

        internal static readonly Option<byte> ComponentId = new("--component-id")
        {
            Description = "Component id used for outgoing traffic.",
            DefaultValueFactory = _ => (byte)1
        };

        internal static readonly Option<string?> SecretKey = new("--secret-key")
        {
            Description = "Validate MAVLink 2 signatures using a 32 byte hex key or a passphrase."
        };

        internal static readonly Option<byte> LinkId = new("--link-id")
        {
            Description = "Link id to expect in incoming signatures.",
            DefaultValueFactory = _ => (byte)0
        };

        internal static readonly Option<bool> Verbose = new("--verbose", "-v")
        {
            Description = "Echo a one line summary for every captured packet to stderr."
        };

        internal static readonly Option<bool> Quiet = new("--quiet", "-q")
        {
            Description = "Suppress progress output."
        };
    }

    internal static Command Create()
    {
        var command = new Command("capture", "Record live MAVLink traffic into a trace file.")
        {
            Opt.Endpoint,
            Opt.Output,
            Opt.Remote,
            Opt.Filter,
            Opt.IncludeRaw,
            Opt.Duration,
            Opt.MaxPackets,
            Opt.SystemId,
            Opt.ComponentId,
            Opt.SecretKey,
            Opt.LinkId,
            Opt.Verbose,
            Opt.Quiet
        };

        command.SetAction((parseResult, actionToken) => RunAsync(parseResult, actionToken));

        return command;
    }

    private static async Task<int> RunAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        string endpoint = parseResult.GetValue(Opt.Endpoint) ?? string.Empty;
        FileInfo output = parseResult.GetValue(Opt.Output)
                          ?? throw new InvalidOperationException("The --output option is required.");
        string? remote = parseResult.GetValue(Opt.Remote);
        string[]? filterExpressions = parseResult.GetValue(Opt.Filter);
        bool includeRaw = parseResult.GetValue(Opt.IncludeRaw);
        bool verbose = parseResult.GetValue(Opt.Verbose);
        bool quiet = parseResult.GetValue(Opt.Quiet);
        double? duration = parseResult.GetValue(Opt.Duration);
        long? maxPackets = parseResult.GetValue(Opt.MaxPackets);
        string? dialect = parseResult.GetValue(SharedOptions.Dialect);

        var context = DialectResolver.Resolve(dialect);
        var filter = PacketFilter.Parse(filterExpressions);

        using var writer = TraceWriter.Create(output.FullName, dialect, context, includeRaw);
        ITransport transport = TransportFactory.Create(endpoint, remote);

        var options = new ConnectionOptions
        {
            SystemId = parseResult.GetValue(Opt.SystemId),
            ComponentId = parseResult.GetValue(Opt.ComponentId),
            Context = context,

            // A capture tool stays silent on the wire and does not try to reconnect.
            HeartbeatIntervalMs = 0,
            AutoReconnect = false,
            Signing = CreateSigning(parseResult.GetValue(Opt.SecretKey), parseResult.GetValue(Opt.LinkId))
        };

        var stats = new CaptureStats();
        var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        using var connection = new MavLinkConnection(transport, options);

        long captured = 0;

        connection.PacketReceived += frame =>
        {
            if (!filter.IsMatch(frame.MessageId, frame.Message?.Name))
            {
                return;
            }

            // The frame is pooled and reused: copy what is needed synchronously, and never dispose it.
            var record = PacketRecord.FromFrame(frame, writer.IncludeRaw);
            writer.Write(record);
            stats.Add(record);
            captured++;

            if (verbose)
            {
                Console.Error.WriteLine($"{record.TimestampUtc:HH:mm:ss.fff} {PacketFormatter.Summarize(record)}");
            }
            else if (!quiet && captured % 100 == 0)
            {
                Console.Error.WriteLine($"  {captured} packets...");
            }

            if (maxPackets is > 0 && captured >= maxPackets.Value)
            {
                stop.Cancel();
            }
        };

        connection.Disconnected += (_, args) =>
        {
            if (args.HasError && !quiet)
            {
                Console.Error.WriteLine($"mavlinkx: link closed: {args.Exception?.Message}");
            }
        };

        if (duration is > 0)
        {
            stop.CancelAfter(TimeSpan.FromSeconds(duration.Value));
        }

        await connection.ConnectAsync(stop.Token);

        if (!quiet)
        {
            Console.Error.WriteLine($"listening on {endpoint}, writing {output.FullName}");
            Console.Error.WriteLine(writer.IsRaw
                ? "  format: raw packet bytes"
                : "  format: JSON lines (replay is byte exact only with --include-raw)");

            if (!filter.IsEmpty && filterExpressions is not null)
            {
                Console.Error.WriteLine($"  filter: {string.Join(", ", filterExpressions)}");
            }
        }

        try
        {
            await Task.Delay(Timeout.Infinite, stop.Token);
        }
        catch (OperationCanceledException)
        {
            // Requested shutdown.
        }

        await ShutdownAsync(connection, transport);
        stopwatch.Stop();

        stats.WriteSummary(captured, stopwatch.Elapsed, output.FullName, writer.IncludeRaw);
        return Program.ExitSuccess;
    }

    private static MavLinkSigning? CreateSigning(string? secretKey, byte linkId)
    {
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            return null;
        }

        string key = secretKey.Trim();

        // A 64 character hex string is treated as the raw 32 byte key, anything else as a passphrase.
        var signing = key.Length == MavLinkSigning.SecretKeyLength * 2 && key.All(Uri.IsHexDigit)
            ? new MavLinkSigning(Convert.FromHexString(key))
            : new MavLinkSigning(key);

        signing.LinkId = linkId;
        return signing;
    }

    /// <summary>
    /// Disconnects without waiting indefinitely: the UDP and serial transports block on receive and ignore
    /// cancellation, so the socket or port is closed to unblock the pending read.
    /// </summary>
    private static async Task ShutdownAsync(MavLinkConnection connection, ITransport transport)
    {
        Task disconnect = connection.DisconnectAsync();

        // Observe any late failure rather than leaving an unobserved task exception behind.
        _ = disconnect.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        await Task.WhenAny(disconnect, Task.Delay(TimeSpan.FromSeconds(2)));
        await transport.DisposeAsync();
    }

    private sealed class CaptureStats
    {
        private readonly Dictionary<string, long> _byMessage = new(StringComparer.Ordinal);
        private readonly Dictionary<byte, long> _bySystem = new();
        private long _bytes;

        internal void Add(PacketRecord record)
        {
            _bytes += record.Raw?.Length
                      ?? record.PayloadLength + Protocol.V1.HeaderLength + Protocol.V1.ChecksumLength;

            string name = string.IsNullOrEmpty(record.Name)
                ? record.MessageId.ToString(CultureInfo.InvariantCulture)
                : record.Name;

            _byMessage[name] = _byMessage.GetValueOrDefault(name) + 1;
            _bySystem[record.SystemId] = _bySystem.GetValueOrDefault(record.SystemId) + 1;
        }

        internal void WriteSummary(long captured, TimeSpan elapsed, string path, bool includesRaw)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "captured {0} packets ({1} bytes) in {2:0.0}s -> {3}{4}",
                captured,
                _bytes,
                elapsed.TotalSeconds,
                path,
                includesRaw ? " (raw bytes embedded)" : string.Empty));

            if (captured == 0)
            {
                return;
            }

            foreach (KeyValuePair<string, long> entry in _byMessage.OrderByDescending(e => e.Value))
            {
                Console.Error.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "  {0,-32} {1,8}",
                    entry.Key,
                    entry.Value));
            }

            foreach (KeyValuePair<byte, long> entry in _bySystem.OrderBy(e => e.Key))
            {
                Console.Error.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "  system {0,-28} {1,8}",
                    entry.Key,
                    entry.Value));
            }
        }
    }
}