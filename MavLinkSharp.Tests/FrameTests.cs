using System.Buffers;
using System.Buffers.Binary;
using MavLinkSharp;
using MavLinkSharp.Enums;

namespace MavLinkSharp.Tests
{
    public class FrameTests
    {
        public FrameTests()
        {
            MavLink.Initialize("common.xml");
        }

        #region IDisposable + ArrayPool Tests

        [Fact]
        public void Frame_Dispose_ReturnsBuffersToPool()
        {
            var frame = new Frame();
            frame.Dispose();

            Assert.Empty(frame.Payload);
            Assert.Empty(frame.Signature);
        }

        [Fact]
        public void Frame_DoubleDispose_DoesNotThrow()
        {
            var frame = new Frame();
            frame.Dispose();
            var ex = Record.Exception(() => frame.Dispose());
            Assert.Null(ex);
        }

        [Fact]
        public void Frame_Payload_BufferSize_IsCorrect()
        {
            using var frame = new Frame();
            Assert.True(frame.Payload.Length >= 255);
        }

        [Fact]
        public void Frame_Signature_BufferSize_IsCorrect()
        {
            using var frame = new Frame();
            Assert.True(frame.Signature.Length >= 13);
        }

        [Fact]
        public void Frame_Reset_ClearsPayload()
        {
            using var frame = new Frame();
            frame.Payload[0] = 0xFF;
            frame.Payload[1] = 0xAB;

            var heartBeatPacket = CreateMinimalV2Heartbeat();
            frame.TryParse(heartBeatPacket);

            var msg = Metadata.Messages[0];
            if (msg.MaxPayloadLength < 255)
            {
                Assert.Equal(0, frame.Payload[msg.MaxPayloadLength]);
            }
        }

        #endregion

        #region ToString Tests

        [Fact]
        public void ToString_V2Heartbeat_ContainsExpectedParts()
        {
            using var frame = new Frame();
            var packet = CreateMinimalV2Heartbeat();
            frame.TryParse(packet);

            var str = frame.ToString();
            Assert.Contains("MAVLink2", str);
            Assert.Contains("HEARTBEAT", str);
            Assert.Contains("Sys=1", str);
            Assert.Contains("Comp=1", str);
        }

        [Fact]
        public void ToString_V1Packet_ContainsV1()
        {
            using var frame = new Frame();
            var packet = CreateMinimalV1Heartbeat();
            frame.TryParse(packet);

            var str = frame.ToString();
            Assert.Contains("MAVLink1", str);
        }

        [Fact]
        public void ToString_ErrorFrame_ContainsError()
        {
            using var frame = new Frame();
            var badPacket = new byte[] { 0xFD, 0x00, 0x00, 0x00, 0x00, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0xFF };
            frame.TryParse(badPacket);

            if (frame.ErrorReason != ErrorReason.None)
            {
                Assert.Contains("Error=", frame.ToString());
            }
        }

        #endregion

        #region Streaming Path Tests (ReadOnlySequence)

        [Fact]
        public void TryParse_SingleSegmentSequence_ParsesFrame()
        {
            using var frame = new Frame();
            var packet = CreateMinimalV2Heartbeat();

            var sequence = new ReadOnlySequence<byte>(packet);
            var result = frame.TryParse(sequence, out var consumed, out var examined);

            Assert.True(result);
            Assert.Equal<uint>(0, frame.MessageId);
            Assert.Equal<byte>(1, frame.SystemId);
        }

        [Fact]
        public void TryParse_TwoSegmentSequence_SplitsHeader()
        {
            using var frame = new Frame();
            var packet = CreateMinimalV2Heartbeat();

            int splitPoint = 5;
            var seg1 = new MemorySequence<byte>(new ReadOnlyMemory<byte>(packet, 0, splitPoint));
            var seg2 = seg1.Append(new ReadOnlyMemory<byte>(packet, splitPoint, packet.Length - splitPoint));
            var sequence = new ReadOnlySequence<byte>(seg1, 0, seg2, seg2.Memory.Length);

            var result = frame.TryParse(sequence, out var consumed, out var examined);

            Assert.True(result);
            Assert.Equal<uint>(0, frame.MessageId);
        }

        [Fact]
        public void TryParse_SplitAtPayload_ParsesCorrectly()
        {
            using var frame = new Frame();
            var packet = CreateMinimalV2Heartbeat();

            int splitPoint = Protocol.V2.OffsetPayload;
            var seg1 = new MemorySequence<byte>(new ReadOnlyMemory<byte>(packet, 0, splitPoint));
            var seg2 = seg1.Append(new ReadOnlyMemory<byte>(packet, splitPoint, packet.Length - splitPoint));
            var sequence = new ReadOnlySequence<byte>(seg1, 0, seg2, seg2.Memory.Length);

            var result = frame.TryParse(sequence, out var consumed, out var examined);

            Assert.True(result);
            Assert.Equal<uint>(0, frame.MessageId);
        }

        [Fact]
        public void TryParse_IncompleteFrame_ReturnsFalse()
        {
            using var frame = new Frame();
            var packet = CreateMinimalV2Heartbeat();

            var incomplete = new ReadOnlySequence<byte>(packet, 0, 6);
            var result = frame.TryParse(incomplete, out var consumed, out var examined);

            Assert.False(result);
        }

        [Fact]
        public void TryParse_JunkBeforeFrame_SkipsAndParses()
        {
            using var frame = new Frame();
            var packet = CreateMinimalV2Heartbeat();

            var junk = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 };
            var combined = new byte[junk.Length + packet.Length];
            junk.CopyTo(combined, 0);
            packet.CopyTo(combined, junk.Length);

            var sequence = new ReadOnlySequence<byte>(combined);
            var result = frame.TryParse(sequence, out var consumed, out var examined);

            Assert.True(result);
            Assert.Equal<uint>(0, frame.MessageId);
        }

        [Fact]
        public void TryParse_EmptySequence_ReturnsFalse()
        {
            using var frame = new Frame();
            var sequence = ReadOnlySequence<byte>.Empty;
            var result = frame.TryParse(sequence, out var consumed, out var examined);

            Assert.False(result);
        }

        [Fact]
        public void TryParse_TwoFramesInSequence_ParsesFirst()
        {
            using var frame = new Frame();
            var packet1 = CreateMinimalV2Heartbeat();
            var packet2 = CreateMinimalV2Heartbeat();

            packet2[Protocol.V2.OffsetSystemId] = 42;

            var combined = new byte[packet1.Length + packet2.Length];
            packet1.CopyTo(combined, 0);
            packet2.CopyTo(combined, packet1.Length);

            var sequence = new ReadOnlySequence<byte>(combined);
            var result = frame.TryParse(sequence, out var consumed, out var examined);

            Assert.True(result);
            Assert.Equal<byte>(1, frame.SystemId);
        }

        #endregion

        #region Fuzz Tests

        [Fact]
        public void TryParse_RandomBytes_DoesNotThrow()
        {
            using var frame = new Frame();
            var rng = new Random(42);

            for (int i = 0; i < 1000; i++)
            {
                int length = rng.Next(1, 300);
                var data = new byte[length];
                rng.NextBytes(data);

                var ex = Record.Exception(() => frame.TryParse(data));
                Assert.Null(ex);
            }
        }

        [Fact]
        public void TryParse_AllZeros_DoesNotThrow()
        {
            using var frame = new Frame();
            var data = new byte[256];
            var ex = Record.Exception(() => frame.TryParse(data));
            Assert.Null(ex);
        }

        [Fact]
        public void TryParse_AllFF_DoesNotThrow()
        {
            using var frame = new Frame();
            var data = new byte[256];
            data.AsSpan().Fill(0xFF);
            var ex = Record.Exception(() => frame.TryParse(data));
            Assert.Null(ex);
        }

        [Fact]
        public void TryParse_SingleByte_DoesNotThrow()
        {
            using var frame = new Frame();
            var ex = Record.Exception(() => frame.TryParse(new byte[] { 0xFD }));
            Assert.Null(ex);
        }

        [Fact]
        public void TryParse_OnlyStartMarker_DoesNotThrow()
        {
            using var frame = new Frame();
            var ex = Record.Exception(() => frame.TryParse(new byte[] { 0xFD, 0xFE }));
            Assert.Null(ex);
        }

        [Fact]
        public void Fuzz_ReadOnlySequence_RandomBytes_DoesNotThrow()
        {
            using var frame = new Frame();
            var rng = new Random(42);

            for (int i = 0; i < 500; i++)
            {
                int length = rng.Next(1, 300);
                var data = new byte[length];
                rng.NextBytes(data);

                var sequence = new ReadOnlySequence<byte>(data);
                var ex = Record.Exception(() => frame.TryParse(sequence, out _, out _));
                Assert.Null(ex);
            }
        }

        #endregion

        #region Frame Round-Trip Tests

        [Fact]
        public void RoundTrip_V2Heartbeat_ParseSerializeParse()
        {
            using var frame1 = new Frame();
            var packet = CreateMinimalV2Heartbeat();
            Assert.True(frame1.TryParse(packet), "First parse should succeed");

            var serialized = frame1.ToBytes();

            using var frame2 = new Frame();
            Assert.True(frame2.TryParse(serialized), "Second parse should succeed");

            Assert.Equal(frame1.MessageId, frame2.MessageId);
            Assert.Equal(frame1.SystemId, frame2.SystemId);
            Assert.Equal(frame1.ComponentId, frame2.ComponentId);
            Assert.Equal(frame1.PayloadLength, frame2.PayloadLength);
        }

        [Fact]
        public void RoundTrip_V2Attitude_ParseSerializeParse()
        {
            using var frame1 = new Frame();
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
            var packet = CreateMavLink2Packet(1, 1, 10, 30, values);
            Assert.True(frame1.TryParse(packet), "First parse should succeed");

            var serialized = frame1.ToBytes();

            using var frame2 = new Frame();
            Assert.True(frame2.TryParse(serialized), "Second parse should succeed");

            Assert.Equal(1.5f, (float)frame2.Fields["roll"], 4);
            Assert.Equal(-0.5f, (float)frame2.Fields["pitch"], 4);
        }

        [Fact]
        public void RoundTrip_TypedAccessors_Preserved()
        {
            using var frame1 = new Frame();
            var values = new Dictionary<string, object>
            {
                { "latitude", (int)473977420 },
                { "longitude", (int)85455940 },
                { "altitude", (int)488000 },
                { "x", 10.5f },
                { "y", 20.6f },
                { "z", -30.7f },
                { "q", new float[] { 1.0f, 0.2f, 0.3f, 0.4f } },
                { "approach_x", 1.1f },
                { "approach_y", 2.2f },
                { "approach_z", 3.3f },
                { "time_usec", (ulong)1234567890 }
            };
            var packet = CreateMavLink2Packet(1, 1, 42, 242, values);
            Assert.True(frame1.TryParse(packet));

            var serialized = frame1.ToBytes();

            using var frame2 = new Frame();
            Assert.True(frame2.TryParse(serialized));

            Assert.Equal((ulong)1234567890, frame2.GetUInt64("time_usec"));
            Assert.Equal((int)473977420, frame2.GetInt32("latitude"));
            Assert.Equal(10.5f, frame2.GetSingle("x"), 4);
        }

        #endregion

        #region Extension Field Tests

        [Fact]
        public void Message_WithExtensions_HasSeparateBaseAndExtendedFields()
        {
            var msg = Metadata.Messages[1]; // SYS_STATUS
            Assert.True(msg.ExtendedFields.Count > 0, "SYS_STATUS should have extension fields");
            Assert.True(msg.OrderedBaseFields.Count > 0, "SYS_STATUS should have base fields");
            Assert.Equal(msg.OrderedBaseFields.Count + msg.ExtendedFields.Count, msg.OrderedFields.Count);
        }

        [Fact]
        public void Message_WithExtensions_PayloadLengthExcludesExtensions()
        {
            var msg = Metadata.Messages[1]; // SYS_STATUS
            int baseSize = msg.OrderedBaseFields.Sum(f => f.Length);
            int extSize = msg.ExtendedFields.Sum(f => f.Length);
            Assert.Equal(baseSize, msg.PayloadLength);
            Assert.Equal(baseSize + extSize, msg.MaxPayloadLength);
        }

        [Fact]
        public void Message_WithExtensions_ExtensionFieldsMarkedExtended()
        {
            var msg = Metadata.Messages[1]; // SYS_STATUS
            foreach (var field in msg.ExtendedFields)
            {
                Assert.True(field.Extended, $"Field {field.Name} should be marked as Extended");
            }
            foreach (var field in msg.OrderedBaseFields)
            {
                Assert.False(field.Extended, $"Field {field.Name} should NOT be marked as Extended");
            }
        }

        [Fact]
        public void Message_WithoutExtensions_BaseEqualsMax()
        {
            var msg = Metadata.Messages[0]; // HEARTBEAT has no extensions
            Assert.Empty(msg.ExtendedFields);
            Assert.Equal(msg.PayloadLength, msg.MaxPayloadLength);
        }

        [Fact]
        public void V2Packet_WithExtensions_ParsesExtensionFields()
        {
            using var frame = new Frame();
            var msg = Metadata.Messages[1]; // SYS_STATUS
            var payload = new byte[msg.MaxPayloadLength];

            // Set base fields
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0), 0x12345678);  // onboard_control_sensors_present
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), 0x12345678);  // onboard_control_sensors_enabled
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), 0x12345678);  // onboard_control_sensors_health
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(12), 500);          // load
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(14), 12000);       // voltage_battery
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(16), -50);          // current_battery
            payload[18] = 75;                                                           // battery_remaining
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(19), 10);          // drop_rate_comm
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(21), 0);           // errors_comm
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(23), 0);           // errors_count1
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(25), 0);           // errors_count2
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(27), 0);           // errors_count3
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(29), 0);           // errors_count4

            // Set extension fields (after base fields)
            int extOffset = msg.PayloadLength;
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(extOffset), 0xDEADBEEF);       // onboard_control_sensors_present_extended
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(extOffset + 4), 0xCAFEBABE);   // onboard_control_sensors_enabled_extended
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(extOffset + 8), 0x1234ABCD);   // onboard_control_sensors_health_extended

            var packet = CreatePacketRaw(1, 1, 0, 1, payload, isV2: true);
            Assert.True(frame.TryParse(packet));

            Assert.Equal(1u, frame.MessageId);
            Assert.Equal(0x12345678u, frame.GetUInt32("onboard_control_sensors_present"));
            Assert.Equal(0xDEADBEEFu, frame.GetUInt32("onboard_control_sensors_present_extended"));
            Assert.Equal(0xCAFEBABEu, frame.GetUInt32("onboard_control_sensors_enabled_extended"));
            Assert.Equal(0x1234ABCDu, frame.GetUInt32("onboard_control_sensors_health_extended"));
        }

        [Fact]
        public void V2Packet_ExtensionFields_RoundTrip()
        {
            using var frame1 = new Frame();
            var msg = Metadata.Messages[1]; // SYS_STATUS
            var values = new Dictionary<string, object>
            {
                { "onboard_control_sensors_present", 0x12345678u },
                { "onboard_control_sensors_enabled", 0x12345678u },
                { "onboard_control_sensors_health", 0x12345678u },
                { "load", (ushort)500 },
                { "voltage_battery", (ushort)12000 },
                { "current_battery", (short)-50 },
                { "battery_remaining", (sbyte)75 },
                { "drop_rate_comm", (ushort)10 },
                { "errors_comm", (ushort)0 },
                { "errors_count1", (ushort)0 },
                { "errors_count2", (ushort)0 },
                { "errors_count3", (ushort)0 },
                { "errors_count4", (ushort)0 },
                { "onboard_control_sensors_present_extended", 0xDEADBEEFu },
                { "onboard_control_sensors_enabled_extended", 0xCAFEBABEu },
                { "onboard_control_sensors_health_extended", 0x1234ABCDu }
            };
            var packet = CreateMavLink2Packet(1, 1, 0, 1, values);
            Assert.True(frame1.TryParse(packet));

            var serialized = frame1.ToBytes();

            using var frame2 = new Frame();
            Assert.True(frame2.TryParse(serialized));

            Assert.Equal(0xDEADBEEFu, frame2.GetUInt32("onboard_control_sensors_present_extended"));
            Assert.Equal(0xCAFEBABEu, frame2.GetUInt32("onboard_control_sensors_enabled_extended"));
            Assert.Equal(0x1234ABCDu, frame2.GetUInt32("onboard_control_sensors_health_extended"));
        }

        [Fact]
        public void V2Packet_TruncatedWithoutExtensions_ParsesSuccessfully()
        {
            using var frame = new Frame();
            var msg = Metadata.Messages[1]; // SYS_STATUS
            var payload = new byte[msg.PayloadLength]; // base fields only

            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0), 0xAAAAAAAA);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), 0xBBBBBBBB);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), 0xCCCCCCCC);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(12), 100);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(14), 11000);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(16), -25);
            payload[18] = 50;
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(19), 5);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(21), 0);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(23), 0);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(25), 0);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(27), 0);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(29), 0);

            var packet = CreatePacketRaw(1, 1, 0, 1, payload, isV2: true);
            Assert.True(frame.TryParse(packet));

            Assert.Equal(0xAAAAAAAAu, frame.GetUInt32("onboard_control_sensors_present"));
            // Extension fields should be zero (truncated)
            Assert.Equal(0u, frame.GetUInt32("onboard_control_sensors_present_extended"));
        }

        [Fact]
        public void CrcExtra_ExcludesExtensionFields()
        {
            var msg = Metadata.Messages[1]; // SYS_STATUS
            var baseCrcInput = $"{msg.Name} ";
            foreach (var field in msg.OrderedBaseFields)
            {
                var fieldType = field.Type.Contains("[")
                    ? field.Type.Substring(0, field.Type.IndexOf('['))
                    : field.Type.Replace("_mavlink_version", "");
                baseCrcInput += $"{fieldType} {field.Name} ";
                if (field.ArrayLength > 0)
                    baseCrcInput += (char)field.ArrayLength;
            }

            ushort baseCrc = Crc.Calculate(System.Text.Encoding.UTF8.GetBytes(baseCrcInput));
            byte expectedCrcExtra = (byte)((baseCrc & 0xFF) ^ (baseCrc >> 8));
            Assert.Equal(expectedCrcExtra, msg.CrcExtra);

            // Extension fields must NOT appear in CRC input
            var extFields = msg.ExtendedFields;
            foreach (var ext in extFields)
            {
                Assert.DoesNotContain(ext.Name!, baseCrcInput);
            }
        }

        [Fact]
        public void V1Packet_WithExtensions_ExtensionFieldsIgnored()
        {
            var msg = Metadata.Messages[1]; // SYS_STATUS has id=1, fits in V1
            var payload = new byte[msg.PayloadLength]; // V1 only carries base fields
            payload[0] = 0xFF;

            var packet = CreatePacketRaw(1, 1, 0, 1, payload, isV2: false);
            using var frame = new Frame();
            Assert.True(frame.TryParse(packet));

            Assert.Equal(msg.PayloadLength, frame.PayloadLength);
        }

        [Fact]
        public void MultipleMessages_ExtensionFieldCounts()
        {
            foreach (var (id, msg) in Metadata.Messages)
            {
                int baseFields = msg.OrderedBaseFields.Count;
                int extFields = msg.ExtendedFields.Count;
                Assert.Equal(baseFields + extFields, msg.OrderedFields.Count);
                Assert.Equal(msg.OrderedBaseFields.Sum(f => f.Length), msg.PayloadLength);
                Assert.Equal(msg.OrderedFields.Sum(f => f.Length), msg.MaxPayloadLength);
                Assert.True(msg.PayloadLength <= msg.MaxPayloadLength);
            }
        }

        #endregion

        #region Helpers

        private byte[] CreateMinimalV2Heartbeat()
        {
            uint messageId = 0;
            var msg = Metadata.Messages[messageId];
            var payload = new byte[msg.MaxPayloadLength];
            payload[4] = 8;
            payload[8] = 3;

            return CreatePacketRaw(1, 1, 0, messageId, payload, isV2: true);
        }

        private byte[] CreateMinimalV1Heartbeat()
        {
            uint messageId = 0;
            var msg = Metadata.Messages[messageId];
            var payload = new byte[msg.PayloadLength];
            payload[4] = 8;
            payload[5] = 3;

            return CreatePacketRaw(1, 1, 0, messageId, payload, isV2: false);
        }

        private byte[] CreatePacketRaw(byte systemId, byte componentId, byte sequence, uint messageId, byte[] payload, bool isV2 = true)
        {
            var packetBytes = new List<byte>();

            if (isV2)
            {
                packetBytes.Add(Protocol.V2.StartMarker);
                packetBytes.Add((byte)payload.Length);
                packetBytes.Add(0);
                packetBytes.Add(0);
                packetBytes.Add(sequence);
                packetBytes.Add(systemId);
                packetBytes.Add(componentId);
                packetBytes.Add((byte)(messageId & 0xFF));
                packetBytes.Add((byte)((messageId >> 8) & 0xFF));
                packetBytes.Add((byte)((messageId >> 16) & 0xFF));
            }
            else
            {
                packetBytes.Add(Protocol.V1.StartMarker);
                packetBytes.Add((byte)payload.Length);
                packetBytes.Add(sequence);
                packetBytes.Add(systemId);
                packetBytes.Add(componentId);
                packetBytes.Add((byte)messageId);
            }

            packetBytes.AddRange(payload);

            var crcBytes = packetBytes.Skip(1).ToArray();
            ushort checksum = Crc.Calculate(crcBytes);
            checksum = Crc.Accumulate(Metadata.Messages[messageId].CrcExtra, checksum);

            packetBytes.Add((byte)(checksum & 0xFF));
            packetBytes.Add((byte)((checksum >> 8) & 0xFF));

            return packetBytes.ToArray();
        }

        private byte[] CreateMavLink2Packet(byte systemId, byte componentId, byte sequence, uint messageId, Dictionary<string, object> fieldValues)
        {
            var messageInfo = Metadata.Messages[messageId];
            var payload = new byte[messageInfo.MaxPayloadLength];
            var span = payload.AsSpan();

            foreach (var field in messageInfo.OrderedFields)
            {
                if (fieldValues.TryGetValue(field.Name!, out var value))
                {
                    if (field.DataType!.IsArray)
                    {
                        var array = (Array)value;
                        var elementType = field.DataType.GetElementType()!;
                        for (int i = 0; i < array.Length; i++)
                        {
                            var elementValue = array.GetValue(i)!;
                            WriteValue(ref span, elementType, elementValue);
                        }
                    }
                    else
                    {
                        WriteValue(ref span, field.DataType, value);
                    }
                }
                else
                {
                    span = span.Slice(field.Length);
                }
            }

            return CreatePacketRaw(systemId, componentId, sequence, messageId, payload, isV2: true);
        }

        private void WriteValue(ref Span<byte> span, Type type, object value)
        {
            if (type == typeof(byte)) { span[0] = Convert.ToByte(value); span = span.Slice(1); }
            else if (type == typeof(sbyte)) { span[0] = (byte)Convert.ToSByte(value); span = span.Slice(1); }
            else if (type == typeof(char)) { span[0] = (byte)Convert.ToChar(value); span = span.Slice(1); }
            else if (type == typeof(short)) { BinaryPrimitives.WriteInt16LittleEndian(span, Convert.ToInt16(value)); span = span.Slice(2); }
            else if (type == typeof(ushort)) { BinaryPrimitives.WriteUInt16LittleEndian(span, Convert.ToUInt16(value)); span = span.Slice(2); }
            else if (type == typeof(int)) { BinaryPrimitives.WriteInt32LittleEndian(span, Convert.ToInt32(value)); span = span.Slice(4); }
            else if (type == typeof(uint)) { BinaryPrimitives.WriteUInt32LittleEndian(span, Convert.ToUInt32(value)); span = span.Slice(4); }
            else if (type == typeof(float)) { var i = BitConverter.SingleToInt32Bits(Convert.ToSingle(value)); BinaryPrimitives.WriteInt32LittleEndian(span, i); span = span.Slice(4); }
            else if (type == typeof(long)) { BinaryPrimitives.WriteInt64LittleEndian(span, Convert.ToInt64(value)); span = span.Slice(8); }
            else if (type == typeof(ulong)) { BinaryPrimitives.WriteUInt64LittleEndian(span, Convert.ToUInt64(value)); span = span.Slice(8); }
            else if (type == typeof(double)) { var l = BitConverter.DoubleToInt64Bits(Convert.ToDouble(value)); BinaryPrimitives.WriteInt64LittleEndian(span, l); span = span.Slice(8); }
            else { throw new Exception($"Unknown type: {type}"); }
        }

        private class MemorySequence<T> : ReadOnlySequenceSegment<T>
        {
            public MemorySequence(ReadOnlyMemory<T> memory)
            {
                Memory = memory;
                RunningIndex = 0;
            }

            public MemorySequence<T> Append(ReadOnlyMemory<T> memory)
            {
                var next = new MemorySequence<T>(memory)
                {
                    RunningIndex = RunningIndex + Memory.Length
                };
                Next = next;
                return next;
            }
        }

        #endregion
    }
}
