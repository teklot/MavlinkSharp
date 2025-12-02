namespace MavLinkSharp
{
    /// <summary>
    /// Contains constants that define the structure of MAVLink protocol frames for different versions.
    /// </summary>
    public class Protocol
    {
        /// <summary>
        /// Defines constants for the MAVLink 1 protocol structure.
        /// </summary>
        public class V1
        {
            /// <summary>
            /// The offset for the start marker (STX).
            /// </summary>
            public const int OffsetStartMarker = 0;
            /// <summary>
            /// The offset for the payload length field.
            /// </summary>
            public const int OffsetPayloadLength = 1;
            /// <summary>
            /// The offset for the packet sequence number.
            /// </summary>
            public const int OffsetPacketSequence = 2;
            /// <summary>
            /// The offset for the system ID (vehicle).
            /// </summary>
            public const int OffsetSystemId = 3;
            /// <summary>
            /// The offset for the component ID.
            /// </summary>
            public const int OffsetComponentId = 4;
            /// <summary>
            /// The offset for the message ID.
            /// </summary>
            public const int OffsetMessageId = 5;
            /// <summary>
            /// The offset where the message payload begins.
            /// </summary>
            public const int OffsetPayload = 6;

            /// <summary>
            /// The start marker for a MAVLink 1 frame.
            /// </summary>
            public const byte StartMarker = 0xFE;

            /// <summary>
            /// The length of the MAVLink 1 header.
            /// </summary>
            public const int HeaderLength = 6;
            /// <summary>
            /// The length of the MAVLink 1 checksum.
            /// </summary>
            public const int ChecksumLength = 2;

            /// <summary>
            /// The minimum possible length of a MAVLink 1 packet.
            /// </summary>
            public const int PacketLengthMin = 8;
            /// <summary>
            /// The maximum possible length of a MAVLink 1 packet.
            /// </summary>
            public const int PacketLengthMax = 263;
        }

        /// <summary>
        /// Defines constants for the MAVLink 2 protocol structure.
        /// </summary>
        public class V2
        {
            /// <summary>
            /// The offset for the start marker (STX).
            /// </summary>
            public const int OffsetStartMarker = 0;
            /// <summary>
            /// The offset for the payload length field.
            /// </summary>
            public const int OffsetPayloadLength = 1;
            /// <summary>
            /// The offset for the incompatibility flags.
            /// </summary>
            public const int OffsetIncompatibilityFlags = 2;
            /// <summary>
            /// The offset for the compatibility flags.
            /// </summary>
            public const int OffsetCompatibilityFlags = 3;
            /// <summary>
            /// The offset for the packet sequence number.
            /// </summary>
            public const int OffsetPacketSequence = 4;
            /// <summary>
            /// The offset for the system ID (vehicle).
            /// </summary>
            public const int OffsetSystemId = 5;
            /// <summary>
            /// The offset for the component ID.
            /// </summary>
            public const int OffsetComponentId = 6;
            /// <summary>
            /// The offset for the message ID (3 bytes).
            /// </summary>
            public const int OffsetMessageId = 7;
            /// <summary>
            /// The offset where the message payload begins.
            /// </summary>
            public const int OffsetPayload = 10;

            /// <summary>
            /// The start marker for a MAVLink 2 frame.
            /// </summary>
            public const byte StartMarker = 0xFD;

            /// <summary>
            /// The length of the MAVLink 2 header.
            /// </summary>
            public const int HeaderLength = 10;
            /// <summary>
            /// The length of the MAVLink 2 checksum.
            /// </summary>
            public const int ChecksumLength = 2;
            /// <summary>
            /// The length of the MAVLink 2 signature (if used).
            /// </summary>
            public const int SignatureLength = 13;

            /// <summary>
            /// The minimum possible length of a MAVLink 2 packet.
            /// </summary>
            public const int PacketLengthMin = 12;
            /// <summary>
            /// The maximum possible length of a MAVLink 2 packet.
            /// </summary>
            public const int PacketLengthMax = 280;
        }
    }
}
