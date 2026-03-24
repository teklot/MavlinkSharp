namespace MavLinkSharp.Tests
{
    public class MavLinkUninitializedTests
    {
        private byte[] CreatePacketRaw(byte systemId, byte componentId, byte sequence, uint messageId, byte[] payload, byte crcExtra)
        {
            var packetBytes = new List<byte>();

            // Header (MAVLink 2)
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

            // Calculate Checksum (covers header except start marker, and payload)
            var crcSpan = packetBytes.Skip(1).ToArray();
            ushort checksum = Crc.Calculate(crcSpan);
            checksum = Crc.Accumulate(crcExtra, checksum);

            packetBytes.Add((byte)(checksum & 0xFF));
            packetBytes.Add((byte)((checksum >> 8) & 0xFF));
            
            return packetBytes.ToArray();
        }

        [Fact]
        public void TryParse_BeforeInitialize_ThrowsException()
        {
            // Arrange
            var ctx = new MavLinkContext(); // Fresh, uninitialized context
            var packet = new byte[] { 0xFD, 0x09, 0x00, 0x00, 0x00, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x03, 0x51, 0x04 };

            // Act & Assert
            var frame = new Frame { Context = ctx };
            Assert.Throws<InvalidOperationException>(() => frame.TryParse(packet));
        }

        [Fact]
        public void MavLinkContext_Isolation_Tests()
        {
            // Arrange
            var ctx1 = new MavLinkContext();
            var ctx2 = new MavLinkContext();
            
            // Act
            ctx1.Initialize(MavLinkSharp.Enums.DialectType.Common);
            var hb = ctx1.Metadata.MessagesDictionary[0];
            var packet = CreatePacketRaw(1, 1, 0, 0, new byte[hb.MaxPayloadLength], hb.CrcExtra);

            // Assert
            Assert.True(ctx1.IsInitialized, "ctx1 should be initialized");
            Assert.False(ctx2.IsInitialized, "ctx2 should not be initialized");

            var frame1 = new Frame { Context = ctx1 };
            bool parsed = frame1.TryParse(packet);
            Assert.True(parsed, $"frame1 should parse packet. Error: {frame1.ErrorReason}");

            var frame2 = new Frame { Context = ctx2 };
            Assert.Throws<InvalidOperationException>(() => frame2.TryParse(packet)); // Should still throw because ctx2 is not initialized
        }
    }
}
