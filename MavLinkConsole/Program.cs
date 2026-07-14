using MavLinkSharp;
using MavLinkSharp.Connection;
using MavLinkSharp.Enums;
using MavLinkSharp.Protocols;
using System.Net;

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

        var transport = new UdpTransport(MavLinkUdpPort, TargetIpAddress, MavLinkUdpPort);
        var options = new ConnectionOptions
        {
            SystemId = 1,
            ComponentId = 1,
            HeartbeatIntervalMs = 0, // manual heartbeat in Transmitter
            AutoReconnect = false
        };

        using var connection = new MavLinkConnection(transport, options);

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

        var ct = new CancellationTokenSource();
        await connection.ConnectAsync(ct.Token);

        // Run Tx and Rx tasks concurrently
        var txTask = Task.Run(() => Transmitter.RunAsync(connection, ct.Token));
        var rxTask = Task.Delay(Timeout.Infinite, ct.Token);

        // Keep the application alive until both tasks complete (which will be never in this case)
        // or a cancellation token is used. For this example, we just await them.
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
