using MavLinkSharp;
using MavLinkSharp.Connection;
using MavLinkSharp.Enums;
using MavLinkSharp.Protocols;

namespace MavLinkConsole;

/// <summary>
/// MavLinkConsole demos entry point.
/// With no arguments, an interactive menu is shown.
/// Command-line options run a specific demo non-interactively:
///   MavLinkConsole --all       -> run all demos (Mission, Param, then the UDP Tx/Rx loop)
///   MavLinkConsole --tx        -> run only the UDP Tx/Rx demo (continues until Ctrl+C)
///   MavLinkConsole --mission   -> run only the in-memory Mission Protocol demo, then exit
///   MavLinkConsole --param     -> run only the in-memory Parameter Protocol demo, then exit
///   MavLinkConsole --help/-h   -> show usage help
/// </summary>
class Program
{
    private const int MavLinkUdpPort = 14550; // Standard MAVLink UDP port
    private const string TargetIpAddress = "127.0.0.1"; // Localhost (assuming same machine for Tx/Rx)
    private static Task? _previousCleanup;

    static async Task Main(string[] args)
    {
        // Initialize MavLinkSharp with the common dialect
        MavLink.Initialize(DialectType.Common);

        // If help was requested, print usage and exit.
        if (args.Contains("--help") || args.Contains("-h"))
        {
            PrintHelp();
            return;
        }

        // If any meaningful argument was provided, run non-interactively.
        if (args.Length > 0)
        {
            await RunCliAsync(args);
            return;
        }

        // No arguments: show the interactive options menu.
        await RunMenuAsync();
    }

    private static async Task RunCliAsync(string[] args)
    {
        bool runMission = args.Contains("--mission") || args.Contains("--all");
        bool runParam = args.Contains("--param") || args.Contains("--all");
        bool runTxRx = args.Contains("--tx") || args.Contains("--all");

        // Protocol demos use a full-width single pane (no Tx/Rx divider).
        if (runMission || runParam)
            TerminalLayout.Initialize(split: false);

        if (runMission)
            await MissionSample.RunAsync(CancellationToken.None);

        if (runParam)
            await ParameterSample.RunAsync(CancellationToken.None);

        // The UDP Tx/Rx stream uses the split-pane layout and runs until Ctrl+C.
        if (runTxRx)
        {
            TerminalLayout.Initialize(split: true);
            await RunTxRxAsync(CancellationToken.None);
        }
    }

    private static async Task RunMenuAsync()
    {
        while (true)
        {
            TryClearScreen();
            Console.WriteLine("=== MavLinkConsole ===");
            Console.WriteLine();
            Console.WriteLine("Protocol demos (in-memory, complete automatically):");
            Console.WriteLine("  1. Mission Protocol demo");
            Console.WriteLine("  2. Parameter Protocol demo");
            Console.WriteLine("  3. All protocol demos");
            Console.WriteLine();
            Console.WriteLine("UDP telemetry demo (S=pause, R=resume, Enter=back):");
            Console.WriteLine("  4. Tx/Rx demo");
            Console.WriteLine();
            Console.WriteLine("  0. Exit");
            Console.WriteLine();
            Console.Write("Choose an option: ");

            string? input = Console.ReadLine();
            if (!int.TryParse(input?.Trim(), out int choice))
            {
                Console.WriteLine("Please enter a valid number.");
                PressEnterToContinue();
                continue;
            }

            if (choice == 0)
                return;

            if (choice is >= 1 and <= 4)
            {
                await RunMenuChoiceAsync(choice);

                if (choice != 4)
                {
                    Console.WriteLine();
                    PressEnterToContinue();
                }
            }
            else
            {
                Console.WriteLine("Unknown option.");
                PressEnterToContinue();
            }
        }
    }

    private static async Task RunMenuChoiceAsync(int choice)
    {
        switch (choice)
        {
            case 1:
                TerminalLayout.Initialize(split: false);
                await MissionSample.RunAsync(CancellationToken.None);
                break;
            case 2:
                TerminalLayout.Initialize(split: false);
                await ParameterSample.RunAsync(CancellationToken.None);
                break;
            case 3:
                TerminalLayout.Initialize(split: false);
                await MissionSample.RunAsync(CancellationToken.None);
                await ParameterSample.RunAsync(CancellationToken.None);
                break;
            case 4:
                TerminalLayout.Initialize(split: true);
                using (var cts = new CancellationTokenSource())
                using (var pauseEvent = new ManualResetEventSlim(true))
                {
                    _ = Task.Run(() =>
                    {
                        while (!cts.IsCancellationRequested)
                        {
                            try
                            {
                                if (Console.KeyAvailable)
                                {
                                    var key = Console.ReadKey(intercept: true);
                                    if (key.Key == ConsoleKey.Enter)
                                    {
                                        cts.Cancel();
                                        break;
                                    }
                                    else if (key.Key == ConsoleKey.S)
                                    {
                                        pauseEvent.Reset();
                                        TerminalLayout.WriteTx("--- Tx paused (press R to resume) ---");
                                    }
                                    else if (key.Key == ConsoleKey.R)
                                    {
                                        pauseEvent.Set();
                                        TerminalLayout.WriteTx("--- Tx resumed ---");
                                    }
                                }
                                Thread.Sleep(50);
                            }
                            catch (InvalidOperationException)
                            {
                                break;
                            }
                        }
                    });
                    await RunTxRxAsync(cts.Token, pauseEvent);
                }
                break;
        }
    }

    private static void TryClearScreen()
    {
        try
        {
            Console.Clear();
            Console.CursorVisible = true;
        }
        catch (IOException)
        {
            // No attached console (e.g. piped input); clearing is a no-op.
        }
    }

    private static void PressEnterToContinue()
    {
        Console.WriteLine("Press Enter to continue...");
        Console.ReadLine();
    }

    private static void PrintHelp()
    {
        Console.WriteLine("MavLinkConsole demos");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  MavLinkConsole [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  (no arguments)                 Show the interactive options menu.");
        Console.WriteLine("  --all                          Run all demos (Mission, Param, then UDP Tx/Rx).");
        Console.WriteLine("  --tx                           Run only the UDP Tx/Rx demo (continues until Ctrl+C).");
        Console.WriteLine("  --mission                      Run only the in-memory Mission Protocol demo.");
        Console.WriteLine("  --param                        Run only the in-memory Parameter Protocol demo.");
        Console.WriteLine("  --help, -h                     Show this help.");
    }

    private static async Task RunTxRxAsync(CancellationToken cancellationToken, ManualResetEventSlim? pauseEvent = null)
    {
        // Wait for any previous connection cleanup to finish (port release).
        if (_previousCleanup is { } prev)
        {
            try { await prev; } catch { }
        }

        var transport = new UdpTransport(MavLinkUdpPort, TargetIpAddress, MavLinkUdpPort);
        var options = new ConnectionOptions
        {
            SystemId = 1,
            ComponentId = 1,
            HeartbeatIntervalMs = 0, // manual heartbeat in Transmitter
            AutoReconnect = false
        };

        var connection = new MavLinkConnection(transport, options);

        connection.OnCommandAck(result =>
        {
            string resultLabel = result.Success ? "ACCEPTED" : result.Result.ToString();
            TerminalLayout.WriteRx($"Rx => ACK for cmd {result.Command}: {resultLabel}");
        });

        connection.PacketReceived += frame =>
        {
            if (frame.MessageId == CommandProtocol.CommandAckId)
                return; // already handled above

            if (frame.MessageId == CommandProtocol.CommandLongId)
            {
                TerminalLayout.WriteRx($"Rx => Seq: {frame.PacketSequence:D3}, COMMAND_LONG (command {frame.Fields["command"]}) - sending ACK");

                _ = SendCommandAckAsync(connection, frame);
                return;
            }

            TerminalLayout.WriteRx($"Rx => " +
                $"Seq: {frame.PacketSequence:D3}, " +
                $"SysId: {frame.SystemId:X2}, " +
                $"CompId: {frame.ComponentId:X2}, " +
                $"Id: {frame.MessageId:X4}, " +
                $"Name: {Metadata.Messages[frame.MessageId].Name}");
        };

        await connection.ConnectAsync(cancellationToken);

        // Run Tx and Rx tasks concurrently; the Tx loop runs until cancelled, keeping the app alive.
        var txTask = Task.Run(() => Transmitter.RunAsync(connection, cancellationToken, pauseEvent));
        var rxTask = Task.Delay(Timeout.Infinite, cancellationToken);

        await Task.WhenAny(txTask, rxTask);

        // Fire-and-forget cleanup so we return to the menu immediately.
        _previousCleanup = Task.Run(() =>
        {
            try { transport.Dispose(); } catch { }
        });
    }

    private static async Task SendCommandAckAsync(MavLinkConnection connection, Frame commandFrame)
    {
        var ackMsg = MavLinkContext.Default.Metadata.MessagesDictionary[CommandProtocol.CommandAckId];
        var ackFrame = new Frame
        {
            Context = MavLinkContext.Default,
            StartMarker = Protocol.V2.StartMarker,
            SystemId = 2,
            ComponentId = 1,
            MessageId = CommandProtocol.CommandAckId,
            Message = ackMsg
        };
        ackFrame.SetFields(new Dictionary<string, object>
        {
            ["command"] = (ushort)commandFrame.Fields["command"],
            ["result"] = (byte)0,
            ["progress"] = (byte)0,
            ["result_param2"] = 0,
            ["target_system"] = commandFrame.SystemId,
            ["target_component"] = commandFrame.ComponentId
        });
        await connection.SendAsync(ackFrame);
        TerminalLayout.WriteRx($"Rx => Seq: {ackFrame.PacketSequence:D3}, " +
            $"COMMAND_ACK for cmd {commandFrame.Fields["command"]} (ACCEPTED)");
    }
}
