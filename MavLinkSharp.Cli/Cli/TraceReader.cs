using System.Text;
using System.Text.Json;
using MavLinkSharp.Enums;

namespace MavLinkSharp.Cli.Cli;

/// <summary>
/// Describes a region of a raw trace that could not be decoded as a valid MAVLink frame.
/// </summary>
/// <param name="Reason">The parser's failure reason for the region.</param>
/// <param name="Offset">Byte offset of the region within the file.</param>
/// <param name="Skipped">Number of bytes skipped while scanning past the region.</param>
internal readonly record struct RawTraceError(ErrorReason Reason, long Offset, long Skipped);

/// <summary>
/// Reads MAVLink traces written by <see cref="TraceWriter"/>.
/// </summary>
internal static class TraceReader
{
    /// <summary>
    /// Reads every packet from a JSON lines trace.
    /// </summary>
    internal static List<PacketRecord> ReadJsonLines(string path, MavLinkContext context)
    {
        var records = new List<PacketRecord>();
        string? line;

        using var reader = new StreamReader(path, Encoding.UTF8);

        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0)
            {
                continue;
            }

            PacketRecord? record = ParseRecord(line, context);
            if (record is not null)
            {
                records.Add(record);
            }
        }

        return records;
    }

    /// <summary>
    /// Parses a single JSON lines trace entry, returning <c>null</c> for the header line.
    /// </summary>
    internal static PacketRecord? ParseRecord(string line, MavLinkContext context)
    {
        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (root.TryGetProperty("record", out JsonElement kind) &&
            kind.ValueKind == JsonValueKind.String &&
            kind.GetString() == "header")
        {
            return null;
        }

        var messageId = GetUInt32(root, "messageId");

        if (!context.Metadata.MessagesDictionary.TryGetValue(messageId, out Message? message))
        {
            message = null;
        }

        var fields = root.TryGetProperty("fields", out JsonElement fieldsElement)
            ? PacketFormatter.ReadFields(fieldsElement, message)
            : new Dictionary<string, object>(StringComparer.Ordinal);

        return new PacketRecord
        {
            TimestampUtc = ParseTimestamp(GetString(root, "timestamp")),
            Version = (int)GetUInt32(root, "version"),
            MessageId = messageId,
            Name = GetString(root, "name") ?? message?.Name,
            Sequence = (byte)GetUInt32(root, "sequence"),
            SystemId = (byte)GetUInt32(root, "systemId"),
            ComponentId = (byte)GetUInt32(root, "componentId"),
            PayloadLength = (byte)GetUInt32(root, "payloadLength"),
            Checksum = (ushort)GetUInt32(root, "checksum"),
            Signed = root.TryGetProperty("signed", out JsonElement signed) && signed.ValueKind == JsonValueKind.True,
            Fields = fields,
            Raw = ParseHex(GetString(root, "raw"))
        };
    }

    /// <summary>
    /// Reads the dialect selector recorded in a JSON lines trace header, if present.
    /// </summary>
    internal static string? ReadDialect(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8);
        string? line = reader.ReadLine();

        if (string.IsNullOrEmpty(line))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            return document.RootElement.TryGetProperty("dialect", out JsonElement dialect)
                   && dialect.ValueKind == JsonValueKind.String
                ? dialect.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Walks a raw byte trace, invoking <paramref name="onFrame"/> for every decoded frame and
    /// <paramref name="onError"/> for every region that could not be decoded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The trace is walked one candidate frame at a time instead of relying on the scan-and-skip positions
    /// returned by <c>Frame.TryParse(ReadOnlySequence&lt;byte&gt;)</c>. That method derives its consumed position
    /// from the first start marker it sees but takes the frame length from the first frame that actually
    /// decodes, so once a frame is undecodable it can return a position that does not reach the frame it
    /// decoded. Walking candidates directly keeps every reported offset, length and byte count exact.
    /// </para>
    /// <para>
    /// Consecutive undecodable bytes are reported as a single region, because a corrupt capture typically
    /// fails at every candidate offset within it.
    /// </para>
    /// </remarks>
    /// <param name="path">The raw trace path.</param>
    /// <param name="context">The dialect context used for decoding.</param>
    /// <param name="onFrame">Called per frame; return <c>false</c> to stop.</param>
    /// <param name="onError">Optional callback for undecodable regions.</param>
    /// <param name="cancellationToken">Token used for cancellation checks between frames.</param>
    /// <returns>The number of frames handed to <paramref name="onFrame"/>.</returns>
    internal static long ReadRaw(
        string path,
        MavLinkContext context,
        Func<Frame, bool> onFrame,
        Action<RawTraceError>? onError = null,
        CancellationToken cancellationToken = default)
    {
        byte[] data = File.ReadAllBytes(path);
        long frames = 0;
        int cursor = 0;
        int junkStart = -1;
        var junkReason = ErrorReason.None;

        while (!cancellationToken.IsCancellationRequested && cursor + Protocol.V1.PacketLengthMin <= data.Length)
        {
            int start = FindStartMarker(data, cursor);

            if (start < 0)
            {
                // No further start marker: everything left is junk, but only worth reporting when a frame
                // boundary has already been found, otherwise the file simply ended early.
                if (junkStart >= 0)
                {
                    onError?.Invoke(new RawTraceError(junkReason, junkStart, data.Length - junkStart));
                }

                break;
            }

            int frameLength = GetFrameLength(data, start);

            if (frameLength <= 0 || start + frameLength > data.Length)
            {
                // The advertised length runs past the end of the file, so the trace is truncated.
                onError?.Invoke(new RawTraceError(ErrorReason.PayloadLengthInvalid, start, data.Length - start));
                break;
            }

            using var frame = new Frame { Context = context };

            if (!frame.TryParse(data.AsSpan(start, frameLength)))
            {
                // Keep the first reason of the run so the region is reported once, then resync from the next byte.
                if (junkStart < 0)
                {
                    junkStart = start;
                    junkReason = frame.ErrorReason;
                }

                cursor = start + 1;
                continue;
            }

            if (junkStart >= 0)
            {
                onError?.Invoke(new RawTraceError(junkReason, junkStart, start - junkStart));
                junkStart = -1;
                junkReason = ErrorReason.None;
            }

            // The decoded frame can be shorter than the advertised candidate when a start marker inside the
            // candidate decoded first, so trust the parser.
            cursor = start + frame.PacketLength;
            frames++;

            if (!onFrame(frame))
            {
                return frames;
            }
        }

        return frames;
    }

    /// <summary>
    /// Finds the next MAVLink start marker at or after <paramref name="from"/>.
    /// </summary>
    private static int FindStartMarker(byte[] data, int from)
    {
        for (int i = from; i < data.Length; i++)
        {
            if (data[i] == Protocol.V2.StartMarker || data[i] == Protocol.V1.StartMarker)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Gets the frame length implied by the header at <paramref name="start"/>, or <c>0</c> when the header
    /// cannot describe a frame.
    /// </summary>
    private static int GetFrameLength(byte[] data, int start)
    {
        bool isV2 = data[start] == Protocol.V2.StartMarker;
        int headerLength = isV2 ? Protocol.V2.HeaderLength : Protocol.V1.HeaderLength;

        if (start + headerLength > data.Length)
        {
            return 0;
        }

        int frameLength = headerLength + data[start + 1] + Protocol.V1.ChecksumLength;

        // A v2 frame carries a signature when the signing bit is set in its incompatibility flags.
        if (isV2 && (data[start + 2] & MavLinkSigning.SigningFlag) != 0)
        {
            frameLength += Protocol.V2.SignatureLength;
        }

        return frameLength;
    }

    private static string? GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static uint GetUInt32(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            return 0;
        }

        if (value.ValueKind != JsonValueKind.Number)
        {
            return 0;
        }

        if (value.TryGetUInt32(out uint result))
        {
            return result;
        }

        return value.TryGetInt64(out long signed) ? (uint)signed : (uint)value.GetDouble();
    }

    private static DateTime ParseTimestamp(string? text)
    {
        return DateTime.TryParse(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out DateTime value)
            ? value
            : default;
    }

    private static byte[]? ParseHex(string? text)
    {
        return string.IsNullOrEmpty(text) ? null : Convert.FromHexString(text);
    }
}