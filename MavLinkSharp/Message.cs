using MavLinkSharp.Enums;
using System;
using System.Collections.Generic;
using System.IO;
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
        /// Payload length in bytes.
        /// </summary>
        public int PayloadLength { get; set; }

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
        /// Attempts to parse a MAVLink frame from a raw byte array. It scans the array for a valid MAVLink 1 or MAVLink 2 start marker and processes the frame.
        /// </summary>
        /// <param name="packet">The byte array that may contain a MAVLink frame.</param>
        /// <param name="frame">When this method returns, contains the parsed MAVLink frame if the parse was successful, or a frame with an <see cref="Frame.ErrorReason"/> if it failed.</param>
        /// <returns><c>true</c> if a valid MAVLink frame was successfully parsed; otherwise, <c>false</c>.</returns>
        /// <remarks>
        /// If parsing fails, the <see cref="Frame.ErrorReason"/> property on the output <paramref name="frame"/> will be set to indicate the specific cause of the failure (e.g., bad checksum, message not found).
        /// This method is the primary entry point for parsing incoming MAVLink data.
        /// </remarks>
        public static bool TryParse(byte[] packet, out Frame frame)
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

            // Improve performance using Array.IndexOf to leverage optimized native implementations
            #region New Implementation
            // Attempt to find MAVLink 2.0 start marker first.
            // MAVLink 2.0 has a larger header and more features, so prioritizing it can simplify parsing logic
            // if a stream contains a mix of both versions and V2 is preferred.
            int v2Offset = Array.IndexOf(packet, Protocol.V2.StartMarker);

            // If V2 start marker is found, try to parse as V2.
            if (v2Offset != -1)
            {
                if (TryParseV2(packet, v2Offset, frame))
                {
                    return true;
                }
                // If V2 parsing failed, it might be a corrupted V2 packet or a V1 packet
                // that coincidentally had a byte matching the V2 start marker.
                // In this case, we fall through to try V1 parsing from the beginning of the packet.
            }

            // If V2 start marker was not found or V2 parsing failed, try to find MAVLink 1.0 start marker.
            int v1Offset = Array.IndexOf(packet, Protocol.V1.StartMarker);

            if (v1Offset != -1)
            {
                return TryParseV1(packet, v1Offset, frame);
            }
            #endregion

            frame.ErrorReason = ErrorReason.StartMarkerNotFound;

            return false;
        }

        private static bool TryParseV2(byte[] packet, int offset, Frame frame)
        {
            frame.StartMarker = packet[offset];

            var packetLength = packet.Length - offset;

            if (packetLength < Protocol.V2.PacketLengthMin)
            {
                frame.ErrorReason = ErrorReason.FrameTooShort;

                return false;
            }

            if (packet.Length > Protocol.V2.PacketLengthMax)
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

            MemoryStream stream;

            var pls = Protocol.V2.OffsetPayload;
            var ple = Protocol.V2.OffsetPayload + frame.PayloadLength;

            // Check for truncated message (e.g. no extensions)
            if (frame.PayloadLength <= message.PayloadLength)
            {
                frame.Payload = new byte[message.PayloadLength];

                packet[pls..ple].CopyTo(frame.Payload, 0);

                stream = new MemoryStream(frame.Payload);
            }
            else
            {
                frame.ErrorReason = ErrorReason.PayloadLengthInvalid;

                return false;
            }

            var br = new BinaryReader(stream);

            foreach (var field in message.OrderedFields)
            {
                frame.Fields[field.Name] = field.GetValue(br);
            }

            if (packet.Length < ple + Protocol.V2.ChecksumLength)
            {
                frame.ErrorReason = ErrorReason.FrameHasNoChecksum;

                return false;
            }

            frame.Checksum = ((ushort)((packet[ple + 1] << 8) | packet[ple]));

            // The CRC covers the whole message including the CRC Extra,
            // except the magic byte and the signature (if present)
            var bytes = new List<byte>(packet[1..ple])
            {
                message.CrcExtra
            };

            var checksum = Crc.Calculate(bytes);

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
                    frame.Signature = new byte[Protocol.V2.SignatureLength];

                    Array.Copy(packet, signatureOffset, frame.Signature, 0, Protocol.V2.SignatureLength);
                }
            }

            return true;
        }

        private static bool TryParseV1(byte[] packet, int offset, Frame frame)
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

            MemoryStream stream;

            var pls = Protocol.V1.OffsetPayload;
            var ple = Protocol.V1.OffsetPayload + frame.PayloadLength;

            // Check for truncated message (e.g. no extensions)
            if (frame.PayloadLength <= message.PayloadLength)
            {
                frame.Payload = new byte[message.PayloadLength];

                packet[pls..ple].CopyTo(frame.Payload, 0);

                stream = new MemoryStream(frame.Payload);
            }
            else
            {
                frame.ErrorReason = ErrorReason.PayloadLengthInvalid;

                return false;
            }

            var br = new BinaryReader(stream);

            foreach (var field in message.OrderedFields)
            {
                frame.Fields[field.Name] = field.GetValue(br);
            }

            if (packet.Length < ple + Protocol.V1.ChecksumLength)
            {
                frame.ErrorReason = ErrorReason.FrameHasNoChecksum;

                return false;
            }

            frame.Checksum = ((ushort)((packet[ple + 1] << 8) | packet[ple]));

            // The CRC covers the whole message including the CRC Extra,
            // except the magic byte and the signature (if present)
            var bytes = new List<byte>(packet[1..ple])
            {
                message.CrcExtra
            };

            var checksum = Crc.Calculate(bytes);

            if (frame.Checksum != checksum)
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

                var type = position > -1 ? field.Type[..position] : field.Type.Replace("_mavlink_version", "");

                extra.AddRange(Encoding.UTF8.GetBytes($"{type} {field.Name} "));

                if (field.ArrayLength > 0)
                {
                    extra.Add(Convert.ToByte(field.ArrayLength));
                }
            }

            var crc = Crc.Calculate(extra);

            CrcExtra = (byte)((crc & 0xFF) ^ (crc >> 8));
        }
        #endregion
    }
}