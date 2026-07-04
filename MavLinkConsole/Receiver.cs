using MavLinkSharp;
using MavLinkSharp.Protocols;
using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace MavLinkConsole;

static class Receiver
{
    public static async Task RunAsync(UdpClient udpClient, IPEndPoint remoteEndPoint, CancellationToken cancellationToken = default)
    {
        // 1. Create a Pipe for the byte stream
        var pipe = new Pipe();

        // 2. Create a Channel for parsed Frames (decouple IO from logic)
        var channel = Channel.CreateBounded<Frame>(new BoundedChannelOptions(100)
        {
            SingleWriter = true,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        var fillTask = FillPipeAsync(udpClient, pipe.Writer, cancellationToken);
        var parseTask = ParsePipeAsync(pipe.Reader, channel.Writer, cancellationToken);
        var processTask = ProcessChannelAsync(channel.Reader, udpClient, remoteEndPoint, cancellationToken);

        await Task.WhenAll(fillTask, parseTask, processTask);
    }

    private static async Task FillPipeAsync(UdpClient udpClient, PipeWriter writer, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await udpClient.ReceiveAsync();
                await writer.WriteAsync(result.Buffer, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // In a real app, log to a file or telemetry
            }
        }
        await writer.CompleteAsync();
    }

    private static async Task ParsePipeAsync(PipeReader reader, ChannelWriter<Frame> writer, CancellationToken ct)
    {
        var frame = new Frame();
        while (!ct.IsCancellationRequested)
        {
            ReadResult result;
            try
            {
                result = await reader.ReadAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            ReadOnlySequence<byte> buffer = result.Buffer;

            while (true)
            {
                if (frame.TryParse(buffer, out SequencePosition consumed, out SequencePosition examined))
                {
                    // Given the current architecture, we'll create a copy for the channel.

                    // TODO: In a high-perf app, use a pool of Frames.
                    var resultFrame = new Frame();
                    var frameSequence = buffer.Slice(0, buffer.GetPosition(0, consumed));
                    // Since it's a small slice (a single frame), copying to an array is fine for this console example.
                    resultFrame.TryParse(frameSequence.ToArray().AsSpan());

                    await writer.WriteAsync(resultFrame, ct);
                    buffer = buffer.Slice(consumed);
                }
                else
                {
                    reader.AdvanceTo(consumed, examined);
                    break;
                }
            }

            if (result.IsCompleted) break;
        }
        writer.Complete();
    }

    private static async Task ProcessChannelAsync(ChannelReader<Frame> reader, UdpClient udpClient, IPEndPoint remoteEndPoint, CancellationToken ct)
    {
        await foreach (var frame in reader.ReadAllAsync(ct))
        {
            // Check if this is a COMMAND_ACK using the CommandProtocol
            if (CommandProtocol.TryParseCommandAck(frame, out var cmdResult))
            {
                string resultLabel = cmdResult.Success ? "ACCEPTED" : cmdResult.Result.ToString();
                TerminalLayout.WriteRx($"Rx => ACK for cmd {cmdResult.Command}: {resultLabel}");
            }
            // If it's a COMMAND_LONG, respond with COMMAND_ACK to demonstrate round-trip
            else if (frame.MessageId == 76)
            {
                var name = Metadata.Messages[frame.MessageId].Name;
                TerminalLayout.WriteRx($"Rx => {name} (command {frame.Fields["command"]}) — sending ACK");

                var ackMsg = MavLinkContext.Default.Metadata.MessagesDictionary[CommandProtocol.CommandAckId];
                var ackFrame = new Frame
                {
                    Context = MavLinkContext.Default,
                    StartMarker = Protocol.V2.StartMarker,
                    SystemId = 2,
                    ComponentId = 1,
                    PacketSequence = 0,
                    MessageId = CommandProtocol.CommandAckId,
                    Message = ackMsg
                };
                ackFrame.SetFields(new Dictionary<string, object>
                {
                    ["command"] = (ushort)frame.Fields["command"],
                    ["result"] = (byte)0, // MAV_RESULT_ACCEPTED
                    ["progress"] = (byte)0,
                    ["result_param2"] = 0,
                    ["target_system"] = frame.SystemId,
                    ["target_component"] = frame.ComponentId
                });

                var response = ackFrame.ToBytes();
                udpClient.Send(response, response.Length, remoteEndPoint);
            }
            else
            {
                TerminalLayout.WriteRx($"Rx => " +
                    $"Seq: {frame.PacketSequence:D3}, " +
                    $"SysId: {frame.SystemId:X2}, " +
                    $"CompId: {frame.ComponentId:X2}, " +
                    $"Id: {frame.MessageId:X4}, " +
                    $"Name: {Metadata.Messages[frame.MessageId].Name}");
            }
        }
    }
}
