using MavLinkSharp.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace MavLinkSharp
{
    /// <summary>
    /// Represents a message definition from a MAVLink XML dialect.
    /// This class also serves as the main entry point for parsing the MAVLink protocol.
    /// </summary>
    [XmlType("message")]
    public class Message
    {
        /// <summary>
        /// Unique index number of this message.
        /// </summary>
        [XmlAttribute(AttributeName = "id")]
        public uint Id { get; set; }

        /// <summary>
        /// Human readable form for the message. It is used for naming helper functions in generated libraries, but is not sent over the wire.
        /// </summary>
        [XmlAttribute(AttributeName = "name")]
        public string Name { get; set; }

        /// <summary>
        /// A tag indicating that the message is a "work in progress" (optional).
        /// </summary>
        [XmlElement(ElementName = "wip")]
        public Wip Wip { get; set; }

        /// <summary>
        /// A tag indicating that the message is deprecated (optional).
        /// </summary>
        [XmlElement(ElementName = "deprecated")]
        public Deprecated Deprecated { get; set; }

        /// <summary>
        /// Human readable description of message, shown in user interfaces and in code comments. This should contain all information (and hyperlinks) to fully understand the message.
        /// </summary>
        [XmlElement(ElementName = "description")]
        public string Description { get; set; }

        /// <summary>
        /// Encodes one field of the message. The field value is its name/text string used in GUI documentation (but not sent over the wire). Every message must have at least one field.
        /// </summary>
        [XmlElement(ElementName = "field")]
        public List<Field> Fields { get; set; } = new List<Field>();

        /// <summary>
        /// This self-closing tag is used to indicate that subsequent fields apply to MAVLink 2 only.
        /// </summary>
        /// <remarks><![CDATA[The tag should be used for MAVLink 1 messages only (id < 256) that have been extended in MAVLink 2.]]></remarks>
        [XmlElement(ElementName = "extensions")]
        public Extensions Extensions { get; set; }

        #region Helpers
        /// <summary>
        /// Payload length in bytes (base fields only).
        /// </summary>
        public int PayloadLength { get; set; }

        /// <summary>
        /// Maximum payload length in bytes (including extensions).
        /// </summary>
        public int MaxPayloadLength { get; set; }

        /// <summary>
        /// The message base fields ordered according to the MAVLink spec.
        /// </summary>
        [XmlIgnore]
        public List<Field> OrderedBaseFields => OrderedFields.Where(x => !x.Extended).ToList();

        /// <summary>
        /// The message extended fields.
        /// </summary>
        [XmlIgnore]
        public List<Field> ExtendedFields => OrderedFields.Where(x => x.Extended).ToList();

        /// <summary>
        /// The message fields ordered according to the MAVLink spec.
        /// </summary>
        [XmlIgnore]
        public List<Field> OrderedFields { get; private set; }

        /// <summary>
        /// Whether the message to be parsed.
        /// </summary>
        [XmlIgnore]
        public bool Included { get; private set; }

        /// <summary>
        /// The checksum of the XML structure for each message used to verify that the sender
        /// and receiver have a shared understanding of the over-the-wire format of a
        /// particular message. 
        /// Format: "message_name [field1_type field1_name [field2_type field2_name [...]]]"
        /// </summary>
        /// <remarks>
        /// Extension fields are not included in the CRC_EXTRA calculation.
        /// See <seealso cref="SetCrcExtra"/> for more details.
        /// </remarks>
        [XmlIgnore]
        public byte CrcExtra { get; private set; }

        /// <summary>
        /// Include the message for parsing.
        /// </summary>
        /// <remarks>
        /// The recommended way to filter messages is by using the static <see cref="MavLink.IncludeMessages(uint[])"/> method.
        /// </remarks>
        /// <seealso cref="MavLink.IncludeMessages(uint[])"/>
        public void Include()
        {
            Included = true;
        }

        /// <summary>
        /// Exclude the message from parsing.
        /// </summary>
        /// <remarks>
        /// The recommended way to filter messages is by using the static <see cref="MavLink.ExcludeMessages(uint[])"/> method.
        /// </remarks>
        /// <seealso cref="MavLink.ExcludeMessages(uint[])"/>
        public void Exclude()
        {
            Included = false;
        }

        /// <summary>
        /// Attempts to parse a MAVLink frame from a raw byte span. It scans the span for a valid MAVLink 1 or MAVLink 2 start marker and processes the frame.
        /// </summary>
        /// <param name="packet">The byte span that may contain a MAVLink frame.</param>
        /// <param name="frame">When this method returns, contains the parsed MAVLink frame if the parse was successful, or a frame with an <see cref="Frame.ErrorReason"/> if it failed.</param>
        /// <returns><c>true</c> if a valid MAVLink frame was successfully parsed; otherwise, <c>false</c>.</returns>
        /// <remarks>
        /// If parsing fails, the <see cref="Frame.ErrorReason"/> property on the output <paramref name="frame"/> will be set to indicate the specific cause of the failure (e.g., bad checksum, message not found).
        /// This method is the primary entry point for parsing incoming MAVLink data.
        /// </remarks>
        public static bool TryParse(ReadOnlySpan<byte> packet, out Frame frame)
        {
            MavLink.ThrowIfNotInitialized();

            frame = new Frame();

            #region Old Implementation
            //var offset = 0;

            //for (; offset < packet.Length; offset++)
            //{
            //    if (packet[offset] == Protocol.V2.StartMarker)
            //    {
            //        return TryParseV2(packet, offset, frame);
            //    }

            //    if (packet[offset] == Protocol.V1.StartMarker)
            //    {
            //        return TryParseV1(packet, offset, frame);
            //    }
            //}
            #endregion

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
                    if (TryParseV2(packet, offset + nextMarkerIndex, frame)) return true;
                }
                else
                {
                    if (TryParseV1(packet, offset + nextMarkerIndex, frame)) return true;
                }

                // If parsing failed at this marker, skip it and continue searching from the next byte
                offset += nextMarkerIndex + 1;
            }

            // If we found markers but none were valid, frame.ErrorReason will hold the last failure reason.
            // If we never found any markers at all, set it to StartMarkerNotFound.
            if (frame.ErrorReason == ErrorReason.None)
            {
                frame.ErrorReason = ErrorReason.StartMarkerNotFound;
            }

            return false;
            #endregion
        }

        private static bool TryParseV2(ReadOnlySpan<byte> packet, int offset, Frame frame)
        {
            frame.StartMarker = packet[offset];

            var packetLength = packet.Length - offset;

            if (packetLength < Protocol.V2.PacketLengthMin)
            {
                frame.ErrorReason = ErrorReason.FrameTooShort;

                return false;
            }

            if (packetLength > Protocol.V2.PacketLengthMax)
            {
                frame.ErrorReason = ErrorReason.FrameTooLong;

                return false;
            }

            frame.PayloadLength = packet[offset + Protocol.V2.OffsetPayloadLength];

            // Sanity check, ditch this packet just in case
            if (packetLength < frame.PayloadLength + Protocol.V2.PacketLengthMin)
            {
                frame.ErrorReason = ErrorReason.FrameTooShort;

                return false;
            }

            frame.IncompatibilityFlags = packet[offset + Protocol.V2.OffsetIncompatibilityFlags];

            frame.CompatibilityFlags = packet[offset + Protocol.V2.OffsetCompatibilityFlags];

            frame.PacketSequence = packet[offset + Protocol.V2.OffsetPacketSequence];

            frame.SystemId = packet[offset + Protocol.V2.OffsetSystemId];

            frame.ComponentId = packet[offset + Protocol.V2.OffsetComponentId];

            frame.MessageId = packet[offset + Protocol.V2.OffsetMessageId] +
                (uint)(packet[offset + Protocol.V2.OffsetMessageId + 1] << 8) +
                (uint)(packet[offset + Protocol.V2.OffsetMessageId + 2] << 16);

            if (Metadata.Messages.ContainsKey(frame.MessageId))
            {
                if (!Metadata.Messages[frame.MessageId].Included)
                {
                    frame.ErrorReason = ErrorReason.MessageExcluded;

                    return false;
                }
            }
            else
            {
                frame.ErrorReason = ErrorReason.MessageNotFound;

                return false;
            }

            var message = Metadata.Messages[frame.MessageId];

            var pls = offset + Protocol.V2.OffsetPayload;
            var ple = offset + Protocol.V2.OffsetPayload + frame.PayloadLength;

            // Check for truncated message (e.g. no extensions)
            if (frame.PayloadLength <= message.MaxPayloadLength)
            {
                frame.Payload = new byte[message.MaxPayloadLength];

                packet.Slice(pls, frame.PayloadLength).CopyTo(frame.Payload);
            }
            else
            {
                frame.ErrorReason = ErrorReason.PayloadLengthInvalid;

                return false;
            }

            ReadOnlySpan<byte> span = frame.Payload;

            foreach (var field in message.OrderedFields)
            {
                frame.Fields[field.Name] = field.GetValue(ref span);
            }

            if (packet.Length < ple + Protocol.V2.ChecksumLength)
            {
                frame.ErrorReason = ErrorReason.FrameHasNoChecksum;

                return false;
            }

            frame.Checksum = ((ushort)((packet[ple + 1] << 8) | packet[ple]));

            // The CRC covers the whole message including the CRC Extra,
            // except the magic byte and the signature (if present)
            // We need a temporary buffer or we slice efficiently.
            // But we need to append CrcExtra.
            // Allocating a buffer is easiest but we want to be efficient.
            // Crc.Calculate(ReadOnlySpan) is available.
            // We can calculate CRC of the slice (packet excluding start marker and checksum), 
            // then Accumulate CrcExtra.
            // Protocol.V2.OffsetPayload is where payload starts.
            // ple is where payload ends.
            // The span for CRC starts at offset + 1 (skipping start marker) and ends at ple.
            
            var crcSpan = packet.Slice(offset + 1, ple - (offset + 1));
            var checksum = Crc.Calculate(crcSpan);
            checksum = Crc.Accumulate(message.CrcExtra, checksum);

            if (frame.Checksum != checksum)
            {
                frame.ErrorReason = ErrorReason.BadChecksum;

                return false;
            }

            var signatureOffset = ple + Protocol.V2.ChecksumLength;

            // Check if the packet includes a signature
            if (packet.Length > signatureOffset)
            {
                if (packet.Length < signatureOffset + Protocol.V2.SignatureLength)
                {
                    frame.ErrorReason = ErrorReason.SignatureLengthInvalid;

                    return false;
                }
                else
                {
                    frame.Signature = packet.Slice(signatureOffset, Protocol.V2.SignatureLength).ToArray();
                }
            }

            return true;
        }

        private static bool TryParseV1(ReadOnlySpan<byte> packet, int offset, Frame frame)
        {
            frame.StartMarker = packet[offset];

            var packetLength = packet.Length - offset;

            if (packetLength < Protocol.V1.PacketLengthMin)
            {
                frame.ErrorReason = ErrorReason.FrameTooShort;

                return false;
            }

            if (packet.Length > Protocol.V1.PacketLengthMax)
            {
                frame.ErrorReason = ErrorReason.FrameTooLong;

                return false;
            }

            frame.PayloadLength = packet[offset + Protocol.V1.OffsetPayloadLength];

            // Sanity check, ditch this packet just in case
            if (packetLength < frame.PayloadLength + Protocol.V1.PacketLengthMin)
            {
                frame.ErrorReason = ErrorReason.FrameTooShort;

                return false;
            }

            frame.PacketSequence = packet[offset + Protocol.V1.OffsetPacketSequence];

            frame.SystemId = packet[offset + Protocol.V1.OffsetSystemId];

            frame.ComponentId = packet[offset + Protocol.V1.OffsetComponentId];

            frame.MessageId = packet[offset + Protocol.V1.OffsetMessageId];

            if (Metadata.Messages.ContainsKey(frame.MessageId))
            {
                if (!Metadata.Messages[frame.MessageId].Included)
                {
                    frame.ErrorReason = ErrorReason.MessageExcluded;

                    return false;
                }
            }
            else
            {
                frame.ErrorReason = ErrorReason.MessageNotFound;

                return false;
            }

            var message = Metadata.Messages[frame.MessageId];

            var pls = offset + Protocol.V1.OffsetPayload;
            var ple = offset + Protocol.V1.OffsetPayload + frame.PayloadLength;

            // Check for truncated message (e.g. no extensions)
            if (frame.PayloadLength <= message.PayloadLength)
            {
                frame.Payload = new byte[message.PayloadLength];

                packet.Slice(pls, frame.PayloadLength).CopyTo(frame.Payload);
            }
            else
            {
                frame.ErrorReason = ErrorReason.PayloadLengthInvalid;

                return false;
            }

            ReadOnlySpan<byte> span = frame.Payload;

            foreach (var field in message.OrderedFields)
            {
                frame.Fields[field.Name] = field.GetValue(ref span);
            }

            if (packet.Length < ple + Protocol.V1.ChecksumLength)
            {
                frame.ErrorReason = ErrorReason.FrameHasNoChecksum;

                return false;
            }

            frame.Checksum = ((ushort)((packet[ple + 1] << 8) | packet[ple]));

            // The CRC covers the whole message including the CRC Extra,
            // except the magic byte and the signature (if present)
            var crcSpan = packet.Slice(offset + 1, ple - (offset + 1));
            var crc = Crc.Calculate(crcSpan);
            crc = Crc.Accumulate(message.CrcExtra, crc);

            if (frame.Checksum != crc)
            {
                frame.ErrorReason = ErrorReason.BadChecksum;

                return false;
            }

            return true;
        }

        internal void SetOrderedFields()
        {
            // Fields are sorted according to their native data size
            var fields = Fields.Where(x => !x.Extended).OrderByDescending(x => x.Ordinal).ToList();

            // Extension fields are sent in XML-declaration order and are not included
            var extensions = Fields.Where(x => x.Extended).ToList();

            OrderedFields = new List<Field>(fields);

            OrderedFields.AddRange(extensions);
        }

        internal void SetCrcExtra()
        {
            var extra = new List<byte>(Encoding.UTF8.GetBytes($"{Name} "));

            foreach (var field in OrderedBaseFields)
            {
                // TODO: create a get property for the curated field type, e.g. CuratedType
                var position = field.Type.IndexOf("[");

                var type = position > -1 ? field.Type.Substring(0, position) : field.Type.Replace("_mavlink_version", "");

                extra.AddRange(Encoding.UTF8.GetBytes($"{type} {field.Name} "));

                if (field.ArrayLength > 0)
                {
                    extra.Add(Convert.ToByte(field.ArrayLength));
                }
            }

            var crc = Crc.Calculate(extra.ToArray());

            CrcExtra = (byte)((crc & 0xFF) ^ (crc >> 8));
        }
        #endregion
    }
}