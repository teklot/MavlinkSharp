using MavLinkSharp;
using MavLinkSharp.Connection;
using MavLinkSharp.Enums;
using MavLinkSharp.Protocols;

namespace MavLinkConsole;

class Program
{
    private const int MavLinkUdpPort = 14550; // Standard MAVLink UDP port
    private const string TargetIpAddress = "127.0.0.1"; // Localhost (assuming same machine for Tx/Rx)

    static async Task Main(string[] args)
    {
        TerminalLayout.Initialize();

        // Initialize MavLinkSharp with the common dialect
        MavLink.Initialize(DialectType.Common);

        // Usage:
        //   MavLinkConsole            -> run both demos (Mission, then the UDP Tx/Rx loop)
        //   MavLinkConsole --all      -> run both demos (Mission, then the UDP Tx/Rx loop)
        //   MavLinkConsole --tx       -> run only the UDP Tx/Rx demo (continues until Ctrl+C)
        //   MavLinkConsole --mission  -> run only the in-memory Mission Protocol demo, then exit
        bool runMission = args.Length == 0 || args.Contains("--mission") || args.Contains("--all");
        bool runTxRx = args.Length == 0 || args.Contains("--tx") || args.Contains("--all");

        var ct = new CancellationTokenSource();

        if (runMission)
            await MissionSample.RunAsync(ct.Token);

        if (runTxRx)
            await RunTxRxAsync(ct.Token);

        ct.Cancel();
    }

    private static async Task RunTxRxAsync(CancellationToken cancellationToken)
    {
        var transport = new UdpTransport(MavLinkUdpPort, TargetIpAddress, MavLinkUdpPort);
        var options = new ConnectionOptions
        {
            SystemId = 1,
            ComponentId = 1,
            HeartbeatIntervalMs = 0, // manual heartbeat in Transmitter
            AutoReconnect = false
        };

        await using var connection = new MavLinkConnection(transport, options);

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
        var txTask = Task.Run(() => Transmitter.RunAsync(connection, cancellationToken));
        var rxTask = Task.Delay(Timeout.Infinite, cancellationToken);

        await Task.WhenAny(txTask, rxTask);

        await connection.DisconnectAsync();
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
