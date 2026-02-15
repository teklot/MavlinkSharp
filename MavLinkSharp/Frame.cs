using MavLinkSharp.Enums;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace MavLinkSharp
{
    /// <summary>
    /// Represents a single, parsed MAVLink message frame.
    /// This class contains the raw data from the wire and the decoded payload fields.
    /// </summary>
    public class Frame
    {
        #region Common Properties
        /// <summary>
        /// Protocol start marker.
        /// For MAVLink 1, this is 0xFE. For MAVLink 2, this is 0xFD.
        /// </summary>
        public byte StartMarker { get; set; }
        /// <summary>
        /// Length of the payload.
        /// </summary>
        public byte PayloadLength { get; set; }
        /// <summary>
        /// MAVLink 2 incompatibility flags. If null, the frame is MAVLink 1.
        /// </summary>
        public byte? IncompatibilityFlags { get; set; }
        /// <summary>
        /// MAVLink 2 compatibility flags. If null, the frame is MAVLink 1.
        /// </summary>
        public byte? CompatibilityFlags { get; set; }
        /// <summary>
        /// Sequence of the packet.
        /// </summary>
        public byte PacketSequence { get; set; }
        /// <summary>
        /// ID of the sending system.
        /// </summary>
        public byte SystemId { get; set; }
        /// <summary>
        /// ID of the sending component.
        /// </summary>
        public byte ComponentId { get; set; }
        /// <summary>
        /// ID of the message.
        /// </summary>
        public uint MessageId { get; set; }
        /// <summary>
        /// The message metadata associated with this frame.
        /// </summary>
        public Message Message { get; set; }
        /// <summary>
        /// The raw message payload.
        /// </summary>
        public byte[] Payload { get; } = new byte[255];
        /// <summary>
        /// Checksum of the frame.
        /// </summary>
        public ushort Checksum { get; internal set; }
        /// <summary>
        /// MAVLink 2 signature for signing packets. If null, the frame is not signed.
        /// </summary>
        public byte[] Signature { get; } = new byte[13];

        /// <summary>
        /// Indicates whether the frame includes a signature.
        /// </summary>
        public bool HasSignature { get; internal set; }
        #endregion

        #region Extra Properties
        /// <summary>
        /// The UTC timestamp when the frame object was created.
        /// </summary>
        public DateTime Timestamp { get; private set; }

        private Dictionary<string, object> _fields;
        /// <summary>
        /// A dictionary holding the decoded payload fields as key-value pairs (field name and value).
        /// The fields are lazily decoded when this property is first accessed.
        /// </summary>
        public Dictionary<string, object> Fields
        {
            get
            {
                if (_fields == null)
                {
                    _fields = new Dictionary<string, object>();
                    if (Message != null)
                    {
                        // Use MaxPayloadLength for MAVLink 2 because we clear the buffer up to that point in TryParse
                        int readLength = StartMarker == Protocol.V2.StartMarker 
                            ? Message.MaxPayloadLength 
                            : Message.PayloadLength;

                        ReadOnlySpan<byte> span = Payload.AsSpan(0, readLength);
                        foreach (var field in Message.OrderedFields)
                        {
                            var fieldSpan = span.Slice(field.Offset, field.Length);
                            _fields[field.Name] = field.GetValue(ref fieldSpan);
                        }
                    }
                }
                return _fields;
            }
        }

        /// <summary>
        /// If an error occurred during parsing, this property specifies the reason.
        /// </summary>
        public ErrorReason ErrorReason { get; internal set; }
        #endregion

        /// <summary>
        /// Initializes a new instance of the <see cref="Frame"/> class and sets the creation timestamp.
        /// </summary>
        /// <param name="messageName">For internal use. (Currently not used and can be ignored).</param>
        public Frame(string messageName = null)
        {
            Timestamp = DateTime.UtcNow;
        }

        internal void Reset()
        {
            StartMarker = 0;
            PayloadLength = 0;
            IncompatibilityFlags = null;
            CompatibilityFlags = null;
            PacketSequence = 0;
            SystemId = 0;
            ComponentId = 0;
            MessageId = 0;
            Message = null;
            Checksum = 0;
            HasSignature = false;
            Timestamp = DateTime.UtcNow;
            _fields = null;
            ErrorReason = ErrorReason.None;
        }

        /// <summary>
        /// Populates the frame's payload fields with the provided values.
        /// </summary>
        /// <param name="values">A dictionary of field names and their corresponding values.</param>
        public void SetFields(IDictionary<string, object> values)
        {
            if (Message == null) throw new InvalidOperationException("Message metadata must be set before setting fields.");

            foreach (var field in Message.OrderedFields)
            {
                if (values.TryGetValue(field.Name, out var value))
                {
                    field.SetValue(Payload.AsSpan(field.Offset), value);
                }
            }

            PayloadLength = (byte)Message.PayloadLength;
        }

        /// <summary>
        /// Serializes the current frame into a MAVLink packet (byte array).
        /// </summary>
        /// <returns>A byte array containing the full MAVLink packet, including header, payload, and checksum.</returns>
        public byte[] ToBytes()
        {
            if (Message == null) throw new InvalidOperationException("Message metadata must be set before serialization.");

            bool isV2 = StartMarker == Protocol.V2.StartMarker || StartMarker == 0; // Default to V2 if not specified
            byte startMarker = isV2 ? Protocol.V2.StartMarker : Protocol.V1.StartMarker;
            int headerLen = isV2 ? Protocol.V2.HeaderLength : Protocol.V1.HeaderLength;
            int totalLen = headerLen + PayloadLength + Protocol.V1.ChecksumLength; // Checksum length is same for both

            byte[] packet = new byte[totalLen];
            Span<byte> span = packet.AsSpan();

            span[0] = startMarker;
            span[1] = PayloadLength;

            if (isV2)
            {
                span[2] = IncompatibilityFlags ?? 0;
                span[3] = CompatibilityFlags ?? 0;
                span[4] = PacketSequence;
                span[5] = SystemId;
                span[6] = ComponentId;
                span[7] = (byte)(MessageId & 0xFF);
                span[8] = (byte)((MessageId >> 8) & 0xFF);
                span[9] = (byte)((MessageId >> 16) & 0xFF);
            }
            else
            {
                span[2] = PacketSequence;
                span[3] = SystemId;
                span[4] = ComponentId;
                span[5] = (byte)MessageId;
            }

            Payload.AsSpan(0, PayloadLength).CopyTo(span.Slice(headerLen, PayloadLength));

            // CRC calculation: starts from offset 1, includes header (except start marker) and payload, then CrcExtra
            ushort checksum = Crc.Calculate(span.Slice(1, headerLen - 1 + PayloadLength));
            checksum = Crc.Accumulate(Message.CrcExtra, checksum);

            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(headerLen + PayloadLength), checksum);

            return packet;
        }

        /// <summary>
        /// Attempts to parse a MAVLink frame from a <see cref="ReadOnlySequence{Byte}"/> into this <see cref="Frame"/> instance.
        /// Useful for streaming scenarios using <see cref="System.IO.Pipelines.PipeReader"/>.
        /// </summary>
        /// <param name="sequence">The byte sequence to parse.</param>
        /// <param name="consumed">When this method returns, contains the position of the first byte after the parsed frame (including any skipped junk bytes).</param>
        /// <param name="examined">When this method returns, contains the position of the last byte examined during the parse attempt.</param>
        /// <returns><c>true</c> if a valid MAVLink frame was successfully parsed; otherwise, <c>false</c>.</returns>
        public bool TryParse(ReadOnlySequence<byte> sequence, out SequencePosition consumed, out SequencePosition examined)
        {
            MavLink.ThrowIfNotInitialized();

            // Default values
            consumed = sequence.Start;
            examined = sequence.End;

            if (sequence.Length < Protocol.V1.PacketLengthMin) return false;

            var readerPos = sequence.Start;
            while (sequence.Slice(readerPos).Length >= Protocol.V1.PacketLengthMin)
            {
                var remaining = sequence.Slice(readerPos);
                var firstSpan = remaining.First.Span;

                // Find next marker in the first segment of the remaining sequence
                int markerIdx = -1;
                for (int i = 0; i < firstSpan.Length; i++)
                {
                    if (firstSpan[i] == Protocol.V2.StartMarker || firstSpan[i] == Protocol.V1.StartMarker)
                    {
                        markerIdx = i;
                        break;
                    }
                }

                if (markerIdx == -1)
                {
                    // No marker in this segment, move to next segment
                    readerPos = remaining.GetPosition(firstSpan.Length);
                    continue;
                }

                // Marker found!
                var markerPos = remaining.GetPosition(markerIdx);
                bool isV2 = firstSpan[markerIdx] == Protocol.V2.StartMarker;

                var fromMarker = sequence.Slice(markerPos);
                if (fromMarker.Length < 2)
                {
                    consumed = markerPos;
                    examined = sequence.End;
                    return false;
                }

                // Read payload length (byte at offset 1)
                Span<byte> h = stackalloc byte[2];
                fromMarker.Slice(0, 2).CopyTo(h);
                byte payloadLen = h[1];

                int minLen = isV2 ? Protocol.V2.PacketLengthMin : Protocol.V1.PacketLengthMin;
                int totalLen = minLen + payloadLen;

                if (fromMarker.Length < totalLen)
                {
                    consumed = markerPos;
                    examined = sequence.End;
                    return false;
                }

                // We have enough data for a frame, attempt parsing
                Span<byte> buffer = stackalloc byte[Protocol.V2.PacketLengthMax];
                int copyLen = (int)Math.Min(fromMarker.Length, (long)Protocol.V2.PacketLengthMax);
                fromMarker.Slice(0, copyLen).CopyTo(buffer);

                if (this.TryParse(buffer.Slice(0, copyLen)))
                {
                    // Success!
                    int frameSize = isV2 ? (Protocol.V2.HeaderLength + this.PayloadLength + Protocol.V2.ChecksumLength) : (Protocol.V1.HeaderLength + this.PayloadLength + Protocol.V1.ChecksumLength);
                    if (isV2 && this.HasSignature) frameSize += Protocol.V2.SignatureLength;

                    consumed = sequence.GetPosition(frameSize, markerPos);
                    examined = consumed;
                    return true;
                }

                // If parsing failed at this marker (e.g. bad CRC), skip it and continue searching
                readerPos = sequence.GetPosition(1, markerPos);
            }

            // No valid frames found in the entire sequence
            consumed = sequence.End;
            examined = sequence.End;
            return false;
        }

        /// <summary>
        /// Attempts to parse a MAVLink frame from a raw byte span into this <see cref="Frame"/> instance.
        /// It scans the span for a valid MAVLink 1 or MAVLink 2 start marker and processes the frame.
        /// </summary>
        /// <param name="packet">The byte span that may contain a MAVLink frame.</param>
        /// <returns><c>true</c> if a valid MAVLink frame was successfully parsed; otherwise, <c>false</c>.</returns>
        /// <remarks>
        /// If parsing fails, the <see cref="ErrorReason"/> property will be set to indicate the specific cause of the failure.
        /// This method is optimized for zero-allocation reuse of the frame object.
        /// </remarks>
        public bool TryParse(ReadOnlySpan<byte> packet)
        {
            MavLink.ThrowIfNotInitialized();

            this.Reset();

            #region Implementation
            int offset = 0;
            while (offset < packet.Length)
            {
                var slice = packet.Slice(offset);

                // Find the next occurrence of either marker
                int v2Index = slice.IndexOf(Protocol.V2.StartMarker);
                int v1Index = slice.IndexOf(Protocol.V1.StartMarker);

                int nextMarkerIndex = -1;
                bool isV2 = false;

                if (v2Index != -1 && v1Index != -1)
                {
                    if (v2Index <= v1Index)
                    {
                        nextMarkerIndex = v2Index;
                        isV2 = true;
                    }
                    else
                    {
                        nextMarkerIndex = v1Index;
                    }
                }
                else if (v2Index != -1)
                {
                    nextMarkerIndex = v2Index;
                    isV2 = true;
                }
                else if (v1Index != -1)
                {
                    nextMarkerIndex = v1Index;
                }

                if (nextMarkerIndex == -1) break;

                // Attempt to parse at the found marker
                if (isV2)
                {
                    if (TryParseV2(packet, offset + nextMarkerIndex)) return true;
                }
                else
                {
                    if (TryParseV1(packet, offset + nextMarkerIndex)) return true;
                }

                // If parsing failed at this marker, skip it and continue searching from the next byte
                offset += nextMarkerIndex + 1;
            }

            // If we found markers but none were valid, ErrorReason will hold the last failure reason.
            // If we never found any markers at all, set it to StartMarkerNotFound.
            if (this.ErrorReason == ErrorReason.None)
            {
                this.ErrorReason = ErrorReason.StartMarkerNotFound;
            }

            return false;
            #endregion
        }

        private bool TryParseV2(ReadOnlySpan<byte> packet, int offset)
        {
            this.StartMarker = packet[offset];

            var packetLength = packet.Length - offset;

            if (packetLength < Protocol.V2.PacketLengthMin)
            {
                this.ErrorReason = ErrorReason.FrameTooShort;

                return false;
            }

            if (packetLength > Protocol.V2.PacketLengthMax)
            {
                this.ErrorReason = ErrorReason.FrameTooLong;

                return false;
            }

            this.PayloadLength = packet[offset + Protocol.V2.OffsetPayloadLength];

            // Sanity check, ditch this packet just in case
            if (packetLength < this.PayloadLength + Protocol.V2.PacketLengthMin)
            {
                this.ErrorReason = ErrorReason.FrameTooShort;

                return false;
            }

            this.IncompatibilityFlags = packet[offset + Protocol.V2.OffsetIncompatibilityFlags];

            this.CompatibilityFlags = packet[offset + Protocol.V2.OffsetCompatibilityFlags];

            this.PacketSequence = packet[offset + Protocol.V2.OffsetPacketSequence];

            this.SystemId = packet[offset + Protocol.V2.OffsetSystemId];

            this.ComponentId = packet[offset + Protocol.V2.OffsetComponentId];

            this.MessageId = packet[offset + Protocol.V2.OffsetMessageId] +
                (uint)(packet[offset + Protocol.V2.OffsetMessageId + 1] << 8) +
                (uint)(packet[offset + Protocol.V2.OffsetMessageId + 2] << 16);

            if (!Metadata.Messages.TryGetValue(this.MessageId, out var message))
            {
                this.ErrorReason = ErrorReason.MessageNotFound;

                return false;
            }

            if (!message.Included)
            {
                this.ErrorReason = ErrorReason.MessageExcluded;

                return false;
            }

            this.Message = message;

            var pls = offset + Protocol.V2.OffsetPayload;
            var ple = offset + Protocol.V2.OffsetPayload + this.PayloadLength;

            // Check for truncated message (e.g. no extensions)
            if (this.PayloadLength <= message.MaxPayloadLength)
            {
                packet.Slice(pls, this.PayloadLength).CopyTo(this.Payload);

                // If the payload is shorter than the message's maximum payload length, 
                // the remaining bytes should be treated as zero (MAVLink 2 truncation).
                if (this.PayloadLength < message.MaxPayloadLength)
                {
                    this.Payload.AsSpan(this.PayloadLength, message.MaxPayloadLength - this.PayloadLength).Clear();
                }
            }
            else
            {
                this.ErrorReason = ErrorReason.PayloadLengthInvalid;

                return false;
            }

            if (packet.Length < ple + Protocol.V2.ChecksumLength)
            {
                this.ErrorReason = ErrorReason.FrameHasNoChecksum;

                return false;
            }

            this.Checksum = ((ushort)((packet[ple + 1] << 8) | packet[ple]));

            // The CRC covers the whole message including the CRC Extra,
            // except the magic byte and the signature (if present)
            var crcSpan = packet.Slice(offset + 1, ple - (offset + 1));
            var checksum = Crc.Calculate(crcSpan);
            checksum = Crc.Accumulate(message.CrcExtra, checksum);

            if (this.Checksum != checksum)
            {
                this.ErrorReason = ErrorReason.BadChecksum;

                return false;
            }

            var signatureOffset = ple + Protocol.V2.ChecksumLength;

            // Check if the packet includes a signature
            if (packet.Length > signatureOffset)
            {
                if (packet.Length < signatureOffset + Protocol.V2.SignatureLength)
                {
                    this.ErrorReason = ErrorReason.SignatureLengthInvalid;

                    return false;
                }
                else
                {
                    packet.Slice(signatureOffset, Protocol.V2.SignatureLength).CopyTo(this.Signature);

                    this.HasSignature = true;
                }
            }

            return true;
        }

        private bool TryParseV1(ReadOnlySpan<byte> packet, int offset)
        {
            this.StartMarker = packet[offset];

            var packetLength = packet.Length - offset;

            if (packetLength < Protocol.V1.PacketLengthMin)
            {
                this.ErrorReason = ErrorReason.FrameTooShort;

                return false;
            }

            if (packet.Length > Protocol.V1.PacketLengthMax)
            {
                this.ErrorReason = ErrorReason.FrameTooLong;

                return false;
            }

            this.PayloadLength = packet[offset + Protocol.V1.OffsetPayloadLength];

            // Sanity check, ditch this packet just in case
            if (packetLength < this.PayloadLength + Protocol.V1.PacketLengthMin)
            {
                this.ErrorReason = ErrorReason.FrameTooShort;

                return false;
            }

            this.PacketSequence = packet[offset + Protocol.V1.OffsetPacketSequence];

            this.SystemId = packet[offset + Protocol.V1.OffsetSystemId];

            this.ComponentId = packet[offset + Protocol.V1.OffsetComponentId];

            this.MessageId = packet[offset + Protocol.V1.OffsetMessageId];

            if (!Metadata.Messages.TryGetValue(this.MessageId, out var message))
            {
                this.ErrorReason = ErrorReason.MessageNotFound;

                return false;
            }

            if (!message.Included)
            {
                this.ErrorReason = ErrorReason.MessageExcluded;

                return false;
            }

            this.Message = message;

            var pls = offset + Protocol.V1.OffsetPayload;
            var ple = offset + Protocol.V1.OffsetPayload + this.PayloadLength;

            // Check for truncated message (e.g. no extensions)
            if (this.PayloadLength <= message.PayloadLength)
            {
                packet.Slice(pls, this.PayloadLength).CopyTo(this.Payload);
            }
            else
            {
                this.ErrorReason = ErrorReason.PayloadLengthInvalid;

                return false;
            }

            if (packet.Length < ple + Protocol.V1.ChecksumLength)
            {
                this.ErrorReason = ErrorReason.FrameHasNoChecksum;

                return false;
            }

            this.Checksum = ((ushort)((packet[ple + 1] << 8) | packet[ple]));

            // The CRC covers the whole message including the CRC Extra,
            // except the magic byte and the signature (if present)
            var crcSpan = packet.Slice(offset + 1, ple - (offset + 1));
            var crc = Crc.Calculate(crcSpan);
            crc = Crc.Accumulate(message.CrcExtra, crc);

            if (this.Checksum != crc)
            {
                this.ErrorReason = ErrorReason.BadChecksum;

                return false;
            }

            return true;
        }
    }
}
