namespace MavLinkSharp.Tests
{
    public class MavLinkParseTests
    {
        [Fact]
        public void Parse_ValidHeartbeatPacket_ReturnsCorrectFrame()
        {
            // Arrange
            MavLink.Initialize("common.xml");
            byte systemId = 1;
            byte componentId = 1;
            byte sequence = 0;
            uint messageId = 0; // HEARTBEAT

            var messageInfo = Metadata.Messages[messageId];

            // Create a dummy payload for HEARTBEAT
            var payload = new byte[messageInfo.PayloadLength];
            // For simplicity, we'll leave payload as all zeros.
            // A more complete test could fill this with actual data.
            // uint32_t custom_mode, uint8_t type, uint8_t autopilot, uint8_t base_mode, uint8_t system_status, uint8_t mavlink_version
            payload[4] = 8; // type = MAV_TYPE_GCS
            payload[8] = 3; // mavlink_version

            // Manually construct MAVLink 2 packet
            var packetBytes = new List<byte>();

            // Header
            packetBytes.Add(Protocol.V2.StartMarker);
            packetBytes.Add((byte)payload.Length);
            packetBytes.Add(0); // Incompatibility Flags
            packetBytes.Add(0); // Compatibility Flags
            packetBytes.Add(sequence);
            packetBytes.Add(systemId);
            packetBytes.Add(componentId);
            packetBytes.Add((byte)(messageId & 0xFF));
            packetBytes.Add((byte)((messageId >> 8) & 0xFF));
            packetBytes.Add((byte)((messageId >> 16) & 0xFF));

            // Payload
            packetBytes.AddRange(payload);

            // Calculate Checksum
            var signatureBytes = new List<byte>();
            signatureBytes.AddRange(packetBytes.Skip(1)); // All bytes except start marker
            signatureBytes.Add(messageInfo.CrcExtra);

            ushort checksum = Crc.Calculate(signatureBytes.ToArray());

            packetBytes.Add((byte)(checksum & 0xFF));
            packetBytes.Add((byte)((checksum >> 8) & 0xFF));

            // Act
            var result = Message.TryParse(packetBytes.ToArray(), out var frame);

            // Assert
            Assert.True(result);
            Assert.NotNull(frame);
            Assert.Equal(systemId, frame.SystemId);
            Assert.Equal(componentId, frame.ComponentId);
            Assert.Equal(sequence, frame.PacketSequence);
            Assert.Equal(messageId, frame.MessageId);
            Assert.NotNull(frame.Fields);
            Assert.True(frame.Fields.Count > 0);
            Assert.Equal("HEARTBEAT", Metadata.Messages[frame.MessageId].Name);
            Assert.Equal(payload.Length, frame.Payload.Length);
        }

        [Fact]
        public void Parse_InvalidPacket_ReturnsFalseWithBadChecksum()
        {
            // Arrange
            MavLink.Initialize("common.xml");
            // This packet has a valid header but an incorrect checksum
            var invalidPacket = new byte[] { 0xFD, 0x09, 0x00, 0x00, 0x00, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00 };

            // Act
            var result = Message.TryParse(invalidPacket, out var frame);

            // Assert
            Assert.False(result);
            Assert.NotNull(frame); // Frame is not null, but contains error info
            Assert.Equal(Enums.ErrorReason.BadChecksum, frame.ErrorReason);
        }



        [Fact]
        public void Parse_EmptyPacket_ReturnsFalse()
        {
            // Arrange
            MavLink.Initialize("common.xml");
            var emptyPacket = new byte[0];

            // Act
            var result = Message.TryParse(emptyPacket, out var frame);

            // Assert
            Assert.False(result);
            Assert.NotNull(frame);
            Assert.Equal(Enums.ErrorReason.StartMarkerNotFound, frame.ErrorReason);
        }
    }
}
