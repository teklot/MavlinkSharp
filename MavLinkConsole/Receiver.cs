using MavLinkSharp;
using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading.Channels;

namespace MavLinkConsole;

static class Receiver
{
    public static async Task RunAsync(UdpClient udpClient, CancellationToken cancellationToken = default)
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
        var processTask = ProcessChannelAsync(channel.Reader, cancellationToken);

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
                var frame = new Frame();
                if (frame.TryParse(buffer, out SequencePosition consumed, out SequencePosition examined))
                {
                    await writer.WriteAsync(frame, ct);
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

    private static async Task ProcessChannelAsync(ChannelReader<Frame> reader, CancellationToken ct)
    {
        await foreach (var frame in reader.ReadAllAsync(ct))
        {
            TerminalLayout.WriteRx($"Rx => " +
                $"Seq: {frame.PacketSequence:D3}, " +
                $"SysId: {frame.SystemId:X2}, " +
                $"CompId: {frame.ComponentId:X2}, " +
                $"Id: {frame.MessageId:X4}, " +
                $"Name: {Metadata.Messages[frame.MessageId].Name}");
        }
    }

    // Keep the old Run method for compatibility if needed, but redirected to the async one
    public static void Run(UdpClient udpClient)
    {
        RunAsync(udpClient).GetAwaiter().GetResult();
    }
}