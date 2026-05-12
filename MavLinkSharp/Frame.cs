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
        /// MAVLink 2 signature for signing packets. If empty, the frame is not signed.
        /// </summary>
        public byte[] Signature { get; internal set; } = new byte[13];

        /// <summary>
        /// The dialect context to use for parsing and message metadata.
        /// Defaults to <see cref="MavLinkContext.Default"/>.
        /// </summary>
        public MavLinkContext Context { get; set; } = MavLinkContext.Default;

        /// <summary>
        /// Gets the signing configuration for this frame, if available.
        /// </summary>
        public MavLinkSigning Signing { get; set; }

        /// <summary>
        /// Indicates whether the frame includes a signature.
        /// </summary>
        public bool HasSignature
        {
            get
            {
                return Signature != null && Signature.Length > 0 &&
                       IncompatibilityFlags.HasValue &&
                       (IncompatibilityFlags.Value & MavLinkSigning.SigningFlag) != 0;
            }
        }

        /// <summary>
        /// Gets the total length of the packet in bytes.
        /// </summary>
        public int PacketLength
        {
            get
            {
                bool isV2 = StartMarker == Protocol.V2.StartMarker || StartMarker == 0;
                int headerLen = isV2 ? Protocol.V2.HeaderLength : Protocol.V1.HeaderLength;
                int len = headerLen + PayloadLength + Protocol.V1.ChecksumLength;
                if (isV2 && Signing != null) len += Protocol.V2.SignatureLength;
                return len;
            }
        }
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
                        foreach (var f in Message.OrderedFields)
                        {
                            var fieldSpan = span.Slice(f.Offset, f.Length);
                            _fields[f.Name] = f.GetValue(ref fieldSpan);
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

        #region Typed Accessors
        private ReadOnlySpan<byte> GetFieldSpan(string name, out Field field)
        {
            if (Message == null) throw new InvalidOperationException("Message metadata is not available.");
            if (!Message.FieldsByName.TryGetValue(name, out field))
                throw new ArgumentException($"Field '{name}' not found in message '{Message.Name}'.");

            return Payload.AsSpan(field.Offset, field.Length);
        }

        /// <summary>
        /// Gets the value of a byte field by name.
        /// </summary>
        /// <param name="name">The name of the field.</param>
        /// <returns>The byte value of the field.</returns>
        public byte GetByte(string name) => GetFieldSpan(name, out _)[0];

        /// <summary>
        /// Gets the value of a signed byte field by name.
        /// </summary>
        /// <param name="name">The name of the field.</param>
        /// <returns>The sbyte value of the field.</returns>
        public sbyte GetSByte(string name) => (sbyte)GetFieldSpan(name, out _)[0];

        /// <summary>
        /// Gets the value of a 16-bit unsigned integer field by name.
        /// </summary>
        /// <param name="name">The name of the field.</param>
        /// <returns>The ushort value of the field.</returns>
        public ushort GetUInt16(string name) => BinaryPrimitives.ReadUInt16LittleEndian(GetFieldSpan(name, out _));

        /// <summary>
        /// Gets the value of a 16-bit signed integer field by name.
        /// </summary>
        /// <param name="name">The name of the field.</param>
        /// <returns>The short value of the field.</returns>
        public short GetInt16(string name) => BinaryPrimitives.ReadInt16LittleEndian(GetFieldSpan(name, out _));

        /// <summary>
        /// Gets the value of a 32-bit unsigned integer field by name.
        /// </summary>
        /// <param name="name">The name of the field.</param>
        /// <returns>The uint value of the field.</returns>
        public uint GetUInt32(string name) => BinaryPrimitives.ReadUInt32LittleEndian(GetFieldSpan(name, out _));

        /// <summary>
        /// Gets the value of a 32-bit signed integer field by name.
        /// </summary>
        /// <param name="name">The name of the field.</param>
        /// <returns>The int value of the field.</returns>
        public int GetInt32(string name) => BinaryPrimitives.ReadInt32LittleEndian(GetFieldSpan(name, out _));

        /// <summary>
        /// Gets the value of a 64-bit unsigned integer field by name.
        /// </summary>
        /// <param name="name">The name of the field.</param>
        /// <returns>The ulong value of the field.</returns>
        public ulong GetUInt64(string name) => BinaryPrimitives.ReadUInt64LittleEndian(GetFieldSpan(name, out _));

        /// <summary>
        /// Gets the value of a 64-bit signed integer field by name.
        /// </summary>
        /// <param name="name">The name of the field.</param>
        /// <returns>The long value of the field.</returns>
        public long GetInt64(string name) => BinaryPrimitives.ReadInt64LittleEndian(GetFieldSpan(name, out _));

        /// <summary>
        /// Gets the value of a single-precision floating point field by name.
        /// </summary>
        /// <param name="name">The name of the field.</param>
        /// <returns>The float value of the field.</returns>
        public float GetSingle(string name) => BitHelpers.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(GetFieldSpan(name, out _)));

        /// <summary>
        /// Gets the value of a double-precision floating point field by name.
        /// </summary>
        /// <param name="name">The name of the field.</param>
        /// <returns>The double value of the field.</returns>
        public double GetDouble(string name) => BitHelpers.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(GetFieldSpan(name, out _)));

        /// <summary>
        /// Gets the value of a character field by name.
        /// </summary>
        /// <param name="name">The name of the field.</param>
        /// <returns>The char value of the field.</returns>
        public char GetChar(string name) => (char)GetFieldSpan(name, out _)[0];

        /// <summary>
        /// Gets the raw byte array of a field by name.
        /// </summary>
        /// <param name="name">The name of the field.</param>
        /// <returns>A byte array containing the field's data.</returns>
        public byte[] GetByteArray(string name) => GetFieldSpan(name, out _).ToArray();

        /// <summary>
        /// Gets an array of typed values for a field by name.
        /// </summary>
        /// <typeparam name="T">The type of elements in the array. Supported types are numeric types and char.</typeparam>
        /// <param name="name">The name of the field.</param>
        /// <returns>An array of type T containing the field's data.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the field is not an array.</exception>
        /// <exception cref="NotSupportedException">Thrown if the type T is not supported.</exception>
        public T[] GetArray<T>(string name) where T : struct
        {
            var span = GetFieldSpan(name, out var field);
            if (field.ArrayLength == 0) throw new InvalidOperationException($"Field '{name}' is not an array.");

            // Fast path for supported types using MemoryMarshal.Cast
            if (typeof(T) == typeof(byte)) return (T[])(object)span.ToArray();
            if (typeof(T) == typeof(sbyte)) return (T[])(object)System.Runtime.InteropServices.MemoryMarshal.Cast<byte, sbyte>(span).ToArray();
            if (typeof(T) == typeof(ushort)) return (T[])(object)System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ushort>(span).ToArray();
            if (typeof(T) == typeof(short)) return (T[])(object)System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(span).ToArray();
            if (typeof(T) == typeof(uint)) return (T[])(object)System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(span).ToArray();
            if (typeof(T) == typeof(int)) return (T[])(object)System.Runtime.InteropServices.MemoryMarshal.Cast<byte, int>(span).ToArray();
            if (typeof(T) == typeof(ulong)) return (T[])(object)System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ulong>(span).ToArray();
            if (typeof(T) == typeof(long)) return (T[])(object)System.Runtime.InteropServices.MemoryMarshal.Cast<byte, long>(span).ToArray();
            if (typeof(T) == typeof(float)) return (T[])(object)System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(span).ToArray();
            if (typeof(T) == typeof(double)) return (T[])(object)System.Runtime.InteropServices.MemoryMarshal.Cast<byte, double>(span).ToArray();

            // Fallback for char (since it's 2 bytes in C# but 1 byte in MAVLink)
            if (typeof(T) == typeof(char))
            {
                var chars = new char[field.ArrayLength];
                for (int i = 0; i < field.ArrayLength; i++) chars[i] = (char)span[i];
                return (T[])(object)chars;
            }

            throw new NotSupportedException($"Array type {typeof(T).Name} is not supported.");
        }
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
            Signature = new byte[13];
            Timestamp = DateTime.UtcNow;
            _fields = null;
            ErrorReason = ErrorReason.None;
            // Note: Context and Signing are preserved
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
        /// Enables MAVLink 2 signing on this frame.
        /// </summary>
        /// <param name="signing">The signing configuration to use.</param>
        /// <param name="linkId">Optional link ID override. If null, uses the link ID from the signing configuration.</param>
        public void EnableSigning(MavLinkSigning signing, byte? linkId = null)
        {
            if (signing == null)
                throw new ArgumentNullException(nameof(signing));

            Signing = signing;
            StartMarker = Protocol.V2.StartMarker;

            if (linkId.HasValue)
                signing.LinkId = linkId.Value;

            // Mark that we want signing (actual flag is set during serialization)
            Signature = new byte[MavLinkSigning.SignatureLength];
        }

        /// <summary>
        /// Attempts to write the current frame into a destination byte span.
        /// </summary>
        /// <param name="destination">The span to write the MAVLink packet into.</param>
        /// <param name="bytesWritten">When this method returns, contains the number of bytes written to the destination.</param>
        /// <returns><c>true</c> if the packet was successfully written; otherwise, <c>false</c> (e.g., if the destination is too small).</returns>
        public bool TryWriteBytes(Span<byte> destination, out int bytesWritten)
        {
            bytesWritten = 0;
            if (Message == null) throw new InvalidOperationException("Message metadata must be set before serialization.");

            int totalLen = PacketLength;
            if (destination.Length < totalLen) return false;

            bool isV2 = StartMarker == Protocol.V2.StartMarker || StartMarker == 0;
            byte startMarker = isV2 ? Protocol.V2.StartMarker : Protocol.V1.StartMarker;
            int headerLen = isV2 ? Protocol.V2.HeaderLength : Protocol.V1.HeaderLength;

            destination[0] = startMarker;
            destination[1] = PayloadLength;

            if (isV2)
            {
                // Set incompatibility flags - include signing flag if signing is enabled
                byte incompatFlags = IncompatibilityFlags ?? 0;
                if (Signing != null)
                    incompatFlags |= MavLinkSigning.SigningFlag;
                destination[2] = incompatFlags;

                destination[3] = CompatibilityFlags ?? 0;
                destination[4] = PacketSequence;
                destination[5] = SystemId;
                destination[6] = ComponentId;
                destination[7] = (byte)(MessageId & 0xFF);
                destination[8] = (byte)((MessageId >> 8) & 0xFF);
                destination[9] = (byte)((MessageId >> 16) & 0xFF);
            }
            else
            {
                destination[2] = PacketSequence;
                destination[3] = SystemId;
                destination[4] = ComponentId;
                destination[5] = (byte)MessageId;
            }

            Payload.AsSpan(0, PayloadLength).CopyTo(destination.Slice(headerLen));

            // CRC calculation: starts from offset 1, includes header (except start marker) and payload
            ushort checksum = Crc.Calculate(destination.Slice(1, headerLen - 1 + PayloadLength));
            checksum = Crc.Accumulate(Message.CrcExtra, checksum);

            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(headerLen + PayloadLength), checksum);

            // Handle signing for MAVLink 2
            if (isV2 && Signing != null)
            {
                // Signing data is header (without start marker) + payload + checksum
                // Start marker (index 0) is NOT included in signing data
                var signingData = destination.Slice(1, (headerLen - 1) + PayloadLength + Protocol.V2.ChecksumLength);
                var signature = Signing.GenerateSignature(signingData);
                signature.CopyTo(destination.Slice(headerLen + PayloadLength + Protocol.V2.ChecksumLength));
            }

            bytesWritten = totalLen;
            return true;
        }

        /// <summary>
        /// Serializes the current frame into a MAVLink packet (byte array).
        /// </summary>
        /// <returns>A byte array containing the full MAVLink packet, including header, payload, and checksum.</returns>
        public byte[] ToBytes()
        {
            byte[] packet = new byte[PacketLength];
            if (!TryWriteBytes(packet, out _))
            {
                throw new InvalidOperationException("Failed to write packet to buffer.");
            }
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
            Context.ThrowIfNotInitialized();

            // Default values
            consumed = sequence.Start;
            examined = sequence.End;

            if (sequence.Length < Protocol.V1.PacketLengthMin) return false;

            Span<byte> h = stackalloc byte[2];
            Span<byte> buffer = stackalloc byte[Protocol.V2.PacketLengthMax];

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
            Context.ThrowIfNotInitialized();

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

            if (!Context.Metadata.MessagesDictionary.TryGetValue(this.MessageId, out var message))
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

            // Check if this packet has a signature based on incompatibility flags
            bool hasSignatureFlag = this.IncompatibilityFlags.HasValue &&
                                    (this.IncompatibilityFlags.Value & MavLinkSigning.SigningFlag) != 0;

            if (hasSignatureFlag)
            {
                // Packet should have signature - verify we have enough data
                if (packet.Length < ple + Protocol.V2.ChecksumLength + Protocol.V2.SignatureLength)
                {
                    this.ErrorReason = ErrorReason.SignatureLengthInvalid;
                    return false;
                }
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

            // Handle signature if present
            if (hasSignatureFlag || Signing != null)
            {
                var signatureOffset = ple + Protocol.V2.ChecksumLength;

                if (packet.Length >= signatureOffset + Protocol.V2.SignatureLength)
                {
                    this.Signature = new byte[Protocol.V2.SignatureLength];
                    packet.Slice(signatureOffset, Protocol.V2.SignatureLength).CopyTo(this.Signature);

                    // Validate signature if signing is configured
                    if (Signing != null)
                    {
                        // The data to validate is: header (without start marker) + payload + checksum
                        var packetForSigning = packet.Slice(offset + 1, (ple - offset - 1) + Protocol.V2.ChecksumLength);
                        if (!Signing.ValidateSignature(packetForSigning, this.Signature))
                        {
                            this.ErrorReason = ErrorReason.BadSignature;
                            return false;
                        }
                    }
                }
                else
                {
                    this.ErrorReason = ErrorReason.SignatureLengthInvalid;
                    return false;
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

            if (!Context.Metadata.MessagesDictionary.TryGetValue(this.MessageId, out var message))
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
