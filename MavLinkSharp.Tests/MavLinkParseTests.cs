using System.Buffers.Binary;

namespace MavLinkSharp.Tests
{
    public class MavLinkParseTests
    {
        public MavLinkParseTests()
        {
            // Ensure initialization happens once or is safe to call
             MavLink.Initialize("common.xml");
        }

        [Fact]
        public void Parse_ValidHeartbeatPacket_ReturnsCorrectFrame()
        {
            // Arrange
            byte systemId = 1;
            byte componentId = 1;
            byte sequence = 0;
            uint messageId = 0; // HEARTBEAT

            var messageInfo = Metadata.Messages[messageId];

            // Create a dummy payload for HEARTBEAT
            var payload = new byte[messageInfo.PayloadLength];
            // uint32_t custom_mode, uint8_t type, uint8_t autopilot, uint8_t base_mode, uint8_t system_status, uint8_t mavlink_version
            payload[4] = 8; // type = MAV_TYPE_GCS
            payload[8] = 3; // mavlink_version

            // Manually construct MAVLink 2 packet
            var packetBytes = CreatePacketRaw(systemId, componentId, sequence, messageId, payload);

            // Act
            var result = Message.TryParse(packetBytes, out var frame);

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
        public void Parse_ValidAttitudePacket_ReturnsCorrectFrame()
        {
            // Arrange
            byte systemId = 1;
            byte componentId = 1;
            byte sequence = 10;
            uint messageId = 30; // ATTITUDE

            var values = new Dictionary<string, object>
            {
                { "time_boot_ms", (uint)12345678 },
                { "roll", 1.5f },
                { "pitch", -0.5f },
                { "yaw", 2.0f },
                { "rollspeed", 0.1f },
                { "pitchspeed", -0.1f },
                { "yawspeed", 0.05f }
            };

            var packetBytes = CreateMavLink2Packet(systemId, componentId, sequence, messageId, values);

            // Act
            var result = Message.TryParse(packetBytes, out var frame);

            // Assert
            Assert.True(result, "Should parse valid ATTITUDE packet");
            Assert.Equal(messageId, frame.MessageId);
            Assert.Equal("ATTITUDE", Metadata.Messages[frame.MessageId].Name);
            
            Assert.Equal((uint)12345678, frame.Fields["time_boot_ms"]);
            Assert.Equal(1.5f, (float)frame.Fields["roll"], 4);
            Assert.Equal(-0.5f, (float)frame.Fields["pitch"], 4);
        }

        [Fact]
        public void Parse_ValidSysStatusPacket_ReturnsCorrectFrame()
        {
            // Arrange
            byte systemId = 1;
            byte componentId = 1;
            byte sequence = 20;
            uint messageId = 1; // SYS_STATUS

            var values = new Dictionary<string, object>
            {
                { "onboard_control_sensors_present", (uint)0xFFFFFFFF },
                { "onboard_control_sensors_enabled", (uint)0xF0F0F0F0 },
                { "onboard_control_sensors_health", (uint)0x0F0F0F0F },
                { "load", (ushort)500 },
                { "voltage_battery", (ushort)12000 },
                { "current_battery", (short)100 },
                { "battery_remaining", (sbyte)50 },
                { "drop_rate_comm", (ushort)1 },
                { "errors_comm", (ushort)2 },
                { "errors_count1", (ushort)3 },
                { "errors_count2", (ushort)4 },
                { "errors_count3", (ushort)5 },
                { "errors_count4", (ushort)6 }
            };

            var packetBytes = CreateMavLink2Packet(systemId, componentId, sequence, messageId, values);

            // Act
            var result = Message.TryParse(packetBytes, out var frame);

            // Assert
            Assert.True(result, "Should parse valid SYS_STATUS packet");
            Assert.Equal(messageId, frame.MessageId);
            
            Assert.Equal((ushort)12000, frame.Fields["voltage_battery"]);
            Assert.Equal((sbyte)50, frame.Fields["battery_remaining"]);
        }

        [Fact]
        public void Parse_InvalidPacket_ReturnsFalseWithBadChecksum()
        {
            // Arrange
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
            var emptyPacket = Array.Empty<byte>();

            // Act
            var result = Message.TryParse(emptyPacket, out var frame);

            // Assert
            Assert.False(result);
            Assert.NotNull(frame);
            Assert.Equal(Enums.ErrorReason.StartMarkerNotFound, frame.ErrorReason);
        }

        private byte[] CreateMavLink2Packet(byte systemId, byte componentId, byte sequence, uint messageId, Dictionary<string, object> fieldValues)
        {
            var messageInfo = Metadata.Messages[messageId];
            var payload = new byte[messageInfo.PayloadLength];
            var span = payload.AsSpan();
            
            // Fill payload based on OrderedFields
            foreach (var field in messageInfo.OrderedFields)
            {
                if (fieldValues.TryGetValue(field.Name, out var value))
                {
                   WriteValue(ref span, field.DataType, value);
                }
                else
                {
                    // Skip bytes if no value provided (leave as 0)
                    span = span.Slice(field.Length);
                }
            }

            return CreatePacketRaw(systemId, componentId, sequence, messageId, payload);
        }

        private void WriteValue(ref Span<byte> span, Type type, object value)
        {
            if (type == typeof(char))
            {
                span[0] = (byte)((char)value);
                span = span.Slice(1);
            }
            else if (type == typeof(sbyte))
            {
                span[0] = (byte)((sbyte)value);
                span = span.Slice(1);
            }
            else if (type == typeof(byte))
            {
                span[0] = (byte)value;
                span = span.Slice(1);
            }
            else if (type == typeof(short))
            {
                BinaryPrimitives.WriteInt16LittleEndian(span, (short)value);
                span = span.Slice(2);
            }
            else if (type == typeof(ushort))
            {
                BinaryPrimitives.WriteUInt16LittleEndian(span, (ushort)value);
                span = span.Slice(2);
            }
            else if (type == typeof(int))
            {
                BinaryPrimitives.WriteInt32LittleEndian(span, (int)value);
                span = span.Slice(4);
            }
            else if (type == typeof(uint))
            {
                BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)value);
                span = span.Slice(4);
            }
            else if (type == typeof(float))
            {
                // BinaryPrimitives doesn't have WriteSingleLittleEndian in all versions, use BitConverter
                 var i = BitConverter.SingleToInt32Bits((float)value);
                 BinaryPrimitives.WriteInt32LittleEndian(span, i);
                 span = span.Slice(4);
            }
            else if (type == typeof(long))
            {
                BinaryPrimitives.WriteInt64LittleEndian(span, (long)value);
                span = span.Slice(8);
            }
            else if (type == typeof(ulong))
            {
                BinaryPrimitives.WriteUInt64LittleEndian(span, (ulong)value);
                span = span.Slice(8);
            }
            else if (type == typeof(double))
            {
                 var l = BitConverter.DoubleToInt64Bits((double)value);
                 BinaryPrimitives.WriteInt64LittleEndian(span, l);
                 span = span.Slice(8);
            }
            else
            {
                throw new Exception($"Unknown type: {type}");
            }
        }

        private byte[] CreatePacketRaw(byte systemId, byte componentId, byte sequence, uint messageId, byte[] payload)
        {
            var messageInfo = Metadata.Messages[messageId];
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
            
            return packetBytes.ToArray();
        }
    }
}
