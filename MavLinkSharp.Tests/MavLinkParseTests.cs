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
            var payload = new byte[messageInfo.MaxPayloadLength];
            // uint32_t custom_mode, uint8_t type, uint8_t autopilot, uint8_t base_mode, uint8_t system_status, uint8_t mavlink_version
            payload[4] = 8; // type = MAV_TYPE_GCS
            payload[8] = 3; // mavlink_version

            // Manually construct MAVLink 2 packet
            var packetBytes = CreatePacketRaw(systemId, componentId, sequence, messageId, payload);

            // Act
            var frame = new Frame();
            var result = frame.TryParse(packetBytes);

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
            Assert.Equal(payload.Length, frame.PayloadLength);
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
            var frame = new Frame();
            var result = frame.TryParse(packetBytes);

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
            var frame = new Frame();
            var result = frame.TryParse(packetBytes);

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
            var frame = new Frame();
            var result = frame.TryParse(invalidPacket);

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
            var frame = new Frame();
            var result = frame.TryParse(emptyPacket);

            // Assert
            Assert.False(result);
            Assert.NotNull(frame);
            Assert.Equal(Enums.ErrorReason.StartMarkerNotFound, frame.ErrorReason);
        }

        [Fact]
        public void Parse_ValidHomePositionPacket_ReturnsCorrectFrame()
        {
            // Arrange
            byte systemId = 1;
            byte componentId = 1;
            byte sequence = 42;
            uint messageId = 242; // HOME_POSITION

            var values = new Dictionary<string, object>
            {
                { "latitude", (int)473977420 },
                { "longitude", (int)85455940 },
                { "altitude", (int)488000 },
                { "x", 10.0f },
                { "y", 20.0f },
                { "z", -30.0f },
                { "q", new float[] { 1.0f, 0.0f, 0.0f, 0.0f } },
                { "approach_x", 0.0f },
                { "approach_y", 0.0f },
                { "approach_z", 0.0f },
                { "time_usec", (ulong)1234567890 }
            };
            var packetBytes = CreateMavLink2Packet(systemId, componentId, sequence, messageId, values);

            // Act
            var frame = new Frame();
            var result = frame.TryParse(packetBytes.AsSpan());

            // Assert
            Assert.True(result, "Should parse valid HOME_POSITION packet");
            Assert.Equal(messageId, frame.MessageId);
            Assert.Equal("HOME_POSITION", Metadata.Messages[frame.MessageId].Name);

            Assert.Equal((int)473977420, frame.Fields["latitude"]);
            Assert.Equal((int)85455940, frame.Fields["longitude"]);
            Assert.Equal(10.0f, (float)frame.Fields["x"], 4);
            
            var q = (float[])frame.Fields["q"];
            Assert.Equal(4, q.Length);
            Assert.Equal(1.0f, q[0]);

            Assert.Equal((ulong)1234567890, frame.Fields["time_usec"]);
        }

        [Fact]
        public void Parse_ValidStatusTextPacket_ReturnsCorrectFrame()
        {
            // Arrange
            byte systemId = 1;
            byte componentId = 1;
            byte sequence = 50;
            uint messageId = 253; // STATUSTEXT

            string messageText = "Hello MAVLink!";
            char[] textChars = new char[50];
            for (int i = 0; i < messageText.Length; i++) textChars[i] = messageText[i];

            var values = new Dictionary<string, object>
            {
                { "severity", (byte)6 }, // MAV_SEVERITY_INFO
                { "text", textChars },
                { "id", (ushort)1234 },
                { "chunk_seq", (byte)0 }
            };

            var packetBytes = CreateMavLink2Packet(systemId, componentId, sequence, messageId, values);

            // Act
            var frame = new Frame();
            var result = frame.TryParse(packetBytes.AsSpan());

            // Assert
            Assert.True(result, "Should parse valid STATUSTEXT packet");
            Assert.Equal(messageId, frame.MessageId);
            Assert.Equal(6, (byte)frame.Fields["severity"]);
            
            var resultChars = (char[])frame.Fields["text"];
            string resultText = new string(resultChars).TrimEnd('\0');
            Assert.Equal(messageText, resultText);
            Assert.Equal((ushort)1234, frame.Fields["id"]);
        }

        [Fact]
        public void Parse_ValidTimeSyncPacket_ReturnsCorrectFrame()
        {
            // Arrange
            byte systemId = 1;
            byte componentId = 1;
            byte sequence = 60;
            uint messageId = 111; // TIMESYNC

            var values = new Dictionary<string, object>
            {
                { "tc1", (long)-123456789012345 },
                { "ts1", (long)987654321098765 },
                { "target_system", (byte)1 },
                { "target_component", (byte)1 }
            };

            var packetBytes = CreateMavLink2Packet(systemId, componentId, sequence, messageId, values);

            // Act
            var frame = new Frame();
            var result = frame.TryParse(packetBytes.AsSpan());

            // Assert
            Assert.True(result, "Should parse valid TIMESYNC packet");
            Assert.Equal(messageId, frame.MessageId);
            Assert.Equal((long)-123456789012345, frame.Fields["tc1"]);
            Assert.Equal((long)987654321098765, frame.Fields["ts1"]);
            Assert.Equal((byte)1, frame.Fields["target_system"]);
        }

        [Fact]
        public void Parse_ValidWheelDistancePacket_ReturnsCorrectFrame()
        {
            // Arrange
            byte systemId = 1;
            byte componentId = 1;
            byte sequence = 70;
            uint messageId = 9000; // WHEEL_DISTANCE

            double[] distances = new double[16];
            for (int i = 0; i < 16; i++) distances[i] = i * 1.1;

            var values = new Dictionary<string, object>
            {
                { "time_usec", (ulong)1234567890 },
                { "count", (byte)4 },
                { "distance", distances }
            };

            var packetBytes = CreateMavLink2Packet(systemId, componentId, sequence, messageId, values);

            // Act
            var frame = new Frame();
            var result = frame.TryParse(packetBytes.AsSpan());

            // Assert
            Assert.True(result, "Should parse valid WHEEL_DISTANCE packet");
            Assert.Equal(messageId, frame.MessageId);
            
            var resultDistances = (double[])frame.Fields["distance"];
            Assert.Equal(16, resultDistances.Length);
            Assert.Equal(1.1, resultDistances[1], 4);
            Assert.Equal(16.5, resultDistances[15], 4);
        }

        private byte[] CreateMavLink2Packet(byte systemId, byte componentId, byte sequence, uint messageId, Dictionary<string, object> fieldValues)
        {
            var messageInfo = Metadata.Messages[messageId];
            var payload = new byte[messageInfo.MaxPayloadLength];
            var span = payload.AsSpan();
            
            // Fill payload based on OrderedFields
            foreach (var field in messageInfo.OrderedFields)
            {
                if (fieldValues.TryGetValue(field.Name, out var value))
                {
                    if (field.DataType.IsArray)
                    {
                        var array = (Array)value;
                        var elementType = field.DataType.GetElementType();
                        if (elementType != null && array != null)
                        {
                            for (int i = 0; i < array.Length; i++)
                            {
                                var elementValue = array.GetValue(i);
                                if (elementValue != null)
                                {
                                    WriteValue(ref span, elementType, elementValue);
                                }
                            }
                        }
                    }
                    else
                    {
                        WriteValue(ref span, field.DataType, value);
                    }
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

            // Calculate Checksum (covers header except start marker, and payload)
            var crcBytes = packetBytes.Skip(1).ToArray();
            ushort checksum = Crc.Calculate(crcBytes);
            checksum = Crc.Accumulate(messageInfo.CrcExtra, checksum);

            packetBytes.Add((byte)(checksum & 0xFF));
            packetBytes.Add((byte)((checksum >> 8) & 0xFF));
            
            return packetBytes.ToArray();
        }
    }
}
