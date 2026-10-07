namespace MavLinkSharp.Cli.Cli;

/// <summary>
/// An immutable snapshot of a single MAVLink packet.
/// </summary>
/// <remarks>
/// <see cref="Frame"/> instances are pooled and reused by the connection's receive loop, and the same instance is
/// handed to every handler. A capture tool therefore has to copy everything it needs synchronously inside
/// <c>PacketReceived</c> and must never retain or dispose the frame.
/// </remarks>
internal sealed class PacketRecord
{
    internal DateTime TimestampUtc { get; init; }

    internal int Version { get; init; }

    internal byte Sequence { get; init; }

    internal byte SystemId { get; init; }

    internal byte ComponentId { get; init; }

    internal uint MessageId { get; init; }

    internal string? Name { get; init; }

    internal byte PayloadLength { get; init; }

    internal ushort Checksum { get; init; }

    internal bool Signed { get; init; }

    internal Dictionary<string, object> Fields { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The original packet bytes, present when the trace was captured with raw bytes enabled.
    /// </summary>
    internal byte[]? Raw { get; init; }

    /// <summary>
    /// Copies a frame into a snapshot. The frame's field dictionary is shallow copied; array values produced by
    /// <see cref="Frame.Fields"/> are already standalone copies, so they are safe to keep.
    /// </summary>
    internal static PacketRecord FromFrame(Frame frame, bool includeRaw)
    {
        return new PacketRecord
        {
            TimestampUtc = frame.Timestamp,
            Version = frame.StartMarker == Protocol.V2.StartMarker ? 2 : 1,
            Sequence = frame.PacketSequence,
            SystemId = frame.SystemId,
            ComponentId = frame.ComponentId,
            MessageId = frame.MessageId,
            Name = frame.Message?.Name,
            PayloadLength = frame.PayloadLength,
            Checksum = frame.Checksum,
            Signed = frame.HasSignature,
            Fields = new Dictionary<string, object>(frame.Fields, StringComparer.Ordinal),
            Raw = includeRaw ? Serialize(frame) : null
        };
    }

    private static byte[] Serialize(Frame frame)
    {
        byte[] bytes = frame.ToBytes();

        if (frame.HasSignature && frame.Signing is null)
        {
            // Frame.TryWriteBytes only fills the signature slot when a signing instance is attached, so the
            // trailing bytes are zero filled rather than the received signature. Drop them: what remains is the
            // packet exactly as it arrived, minus its signature.
            Array.Resize(ref bytes, bytes.Length - Protocol.V2.SignatureLength);
        }

        return bytes;
    }
}