using MavLinkSharp;
using MavLinkSharp.Enums;
using MavLinkSharp.Protocols;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace MavLinkSharp.Tests
{
    public class ParameterProtocolTests
    {
        private readonly MavLinkContext _context;
        private readonly MavLinkContext _devContext;

        public ParameterProtocolTests()
        {
            _context = new MavLinkContext();
            _context.Initialize(DialectType.Common);
            _devContext = new MavLinkContext();
            _devContext.Initialize(DialectType.Development);
        }

        [Fact]
        public void CreateParamRequestRead_CreatesValidFrame()
        {
            var frame = ParameterProtocol.CreateParamRequestRead(
                _context, systemId: 1, componentId: 1, targetSystem: 2, targetComponent: 3,
                paramId: "RC1_MAX", paramIndex: -1, sequence: 4);

            Assert.Equal(ParameterProtocol.ParamRequestReadId, frame.MessageId);
            Assert.Equal((byte)2, frame.Fields["target_system"]);
            Assert.Equal((byte)3, frame.Fields["target_component"]);
            Assert.Equal(-1, (short)frame.Fields["param_index"]);
            Assert.Equal("RC1_MAX", ToString((char[])frame.Fields["param_id"]));

            var parsed = new Frame { Context = _context };
            Assert.True(parsed.TryParse(frame.ToBytes()));
            Assert.Equal("RC1_MAX", ToString((char[])parsed.Fields["param_id"]));
        }

        [Fact]
        public void CreateParamRequestList_RoundTrips()
        {
            var frame = ParameterProtocol.CreateParamRequestList(_context, 1, 1, 2, 3, sequence: 2);

            Assert.Equal(ParameterProtocol.ParamRequestListId, frame.MessageId);
            Assert.Equal((byte)2, frame.Fields["target_system"]);

            var parsed = new Frame { Context = _context };
            Assert.True(parsed.TryParse(frame.ToBytes()));
            Assert.Equal((byte)3, parsed.Fields["target_component"]);
        }

        [Fact]
        public void CreateParamValue_RoundTrips()
        {
            var param = new MavParamValue
            {
                ParamId = "BATT_VOLT",
                Value = 12.6f,
                Type = MavParamType.Real32,
                ParamCount = 42,
                ParamIndex = 7
            };
            var frame = ParameterProtocol.CreateParamValue(_context, 2, 3, param, sequence: 1);

            var parsed = new Frame { Context = _context };
            Assert.True(parsed.TryParse(frame.ToBytes()));
            Assert.True(ParameterProtocol.TryParseParamValue(parsed, out var value));
            Assert.NotNull(value);
            Assert.Equal("BATT_VOLT", value.ParamId);
            Assert.Equal(12.6f, value.Value, 3);
            Assert.Equal(MavParamType.Real32, value.Type);
            Assert.Equal((ushort)42, value.ParamCount);
            Assert.Equal((ushort)7, value.ParamIndex);
        }

        [Fact]
        public void CreateParamSet_RoundTrips()
        {
            var frame = ParameterProtocol.CreateParamSet(
                _context, 1, 1, 2, 3, "THR_MAX", 900f, MavParamType.UInt16, sequence: 3);

            var parsed = new Frame { Context = _context };
            Assert.True(parsed.TryParse(frame.ToBytes()));
            Assert.Equal(ParameterProtocol.ParamSetId, parsed.MessageId);
            Assert.Equal("THR_MAX", ToString((char[])parsed.Fields["param_id"]));
            Assert.Equal(900f, (float)parsed.Fields["param_value"], 3);
            Assert.Equal((byte)MavParamType.UInt16, parsed.Fields["param_type"]);
        }

        [Fact]
        public void TryParseParamValue_WithNonValueFrame_ReturnsFalse()
        {
            var frame = new Frame { Context = _context, MessageId = 0, Message = _context.Metadata.MessagesDictionary[0] };
            Assert.False(ParameterProtocol.TryParseParamValue(frame, out var value));
            Assert.Null(value);
        }

        [Fact]
        public void MavParamValue_Get_ConvertsTypedValues()
        {
            var param = new MavParamValue { ParamId = "INT_PARAM", Value = 5f, Type = MavParamType.Int32 };
            Assert.Equal(5, param.Get<int>());
            Assert.Equal(5f, param.Get<float>());
            Assert.Equal(5L, param.Get<long>());
            Assert.Equal((short)5, param.Get<short>());
        }

        [Fact]
        public void ParameterCache_StoresAndGetsTypedValues()
        {
            var cache = new ParameterCache();
            cache.Set(new MavParamValue { ParamId = "ALT_AMSL", Value = 123.4f, Type = MavParamType.Real32, ParamCount = 3, ParamIndex = 0 });
            cache.Set(new MavParamValue { ParamId = "WP_TOTAL", Value = 7f, Type = MavParamType.Int16, ParamCount = 3, ParamIndex = 1 });

            Assert.Equal(2, cache.Count);
            Assert.True(cache.TryGet("ALT_AMSL", out MavParamValue? v));
            Assert.NotNull(v);
            Assert.Equal(123.4f, v.Value, 3);
            Assert.True(cache.TryGet<int>("WP_TOTAL", out int wp));
            Assert.Equal(7, wp);
            Assert.Null(cache.Get("MISSING"));
        }

        [Fact]
        public async Task DownloadParametersAsync_HappyPath_PopulatesCache()
        {
            var frames = new[]
            {
                BuildValueFrame("RC1_MAX", 1000f, count: 3, index: 0),
                BuildValueFrame("THR_MAX", 900f, count: 3, index: 1),
                BuildValueFrame("BATT_VOLT", 12.6f, count: 3, index: 2)
            };

            var download = await ParameterProtocol.DownloadParametersAsync(
                _context, 1, 1, 2, 3,
                sendAsync: (bytes, _) => Task.CompletedTask,
                receiveFrameAsync: ReceiveFromQueue(frames, idleAfter: 3),
                timeoutMs: 500, itemTimeoutMs: 25, maxRetries: 1, idleTolerance: 3,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(3, download.Cache.Count);
            Assert.True(download.Cache.TryGet<float>("BATT_VOLT", out float bv));
            Assert.Equal(12.6f, bv, 3);
            Assert.True(download.Complete);
        }

        [Fact]
        public async Task ReadParameterAsync_ReturnsMatchingValue()
        {
            var frame = BuildValueFrame("WP_TOTAL", 7f, count: 10, index: 3);
            var result = await ParameterProtocol.ReadParameterAsync(
                _context, 1, 1, 2, 3, "WP_TOTAL",
                sendAsync: (bytes, _) => Task.CompletedTask,
                receiveFrameAsync: _ => Task.FromResult(frame),
                timeoutMs: 500, maxRetries: 1,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal("WP_TOTAL", result.ParamId);
            Assert.Equal(7f, result.Value, 3);
        }

        [Fact]
        public async Task SetParameterAsync_ParamsSetAckedByValue_ReturnsValue()
        {
            var ackFrame = BuildValueFrame("THR_MAX", 850f, count: 5, index: 0);
            var result = await ParameterProtocol.SetParameterAsync(
                _context, 1, 1, 2, 3, "THR_MAX", 850f, MavParamType.UInt16,
                sendAsync: (bytes, _) => Task.CompletedTask,
                receiveFrameAsync: _ => Task.FromResult(ackFrame),
                timeoutMs: 500, maxRetries: 1,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal("THR_MAX", result.ParamId);
            Assert.Equal(850f, result.Value, 3);
        }

        [Fact]
        public async Task SetParameterAsync_RetriesOnTimeout_ThenSucceeds()
        {
            var ackFrame = BuildValueFrame("THR_MAX", 850f, count: 5, index: 0);
            int calls = 0;
            var result = await ParameterProtocol.SetParameterAsync(
                _context, 1, 1, 2, 3, "THR_MAX", 850f, MavParamType.UInt16,
                sendAsync: (bytes, _) => Task.CompletedTask,
                receiveFrameAsync: _ =>
                {
                    calls++;
                    if (calls == 1) throw new System.OperationCanceledException();
                    return Task.FromResult(ackFrame);
                },
                timeoutMs: 25, maxRetries: 1,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(850f, result.Value, 3);
            Assert.Equal(2, calls);
        }

        [Fact]
        public async Task SetParameterAsync_WhenTimeoutExhausted_Throws()
        {
            await Assert.ThrowsAsync<TimeoutException>(() =>
                ParameterProtocol.SetParameterAsync(
                    _context, 1, 1, 2, 3, "THR_MAX", 850f, MavParamType.UInt16,
                    sendAsync: (bytes, _) => Task.CompletedTask,
                    receiveFrameAsync: _ => throw new System.OperationCanceledException(),
                    timeoutMs: 10, maxRetries: 1,
                    cancellationToken: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Parameter_ReadSet_ThroughInMemoryVehicle()
        {
            var g2v = Channel.CreateUnbounded<Frame>();
            var v2g = Channel.CreateUnbounded<Frame>();

            var paramsById = new Dictionary<string, MavParamValue>
            {
                ["RC1_MAX"] = new MavParamValue { ParamId = "RC1_MAX", Value = 1000f, Type = MavParamType.Real32, ParamCount = 3, ParamIndex = 0 },
                ["THR_MAX"] = new MavParamValue { ParamId = "THR_MAX", Value = 900f, Type = MavParamType.Real32, ParamCount = 3, ParamIndex = 1 },
                ["BATT_VOLT"] = new MavParamValue { ParamId = "BATT_VOLT", Value = 12.6f, Type = MavParamType.Real32, ParamCount = 3, ParamIndex = 2 }
            };

            Task RelayAsync(Channel<Frame> link, byte[] bytes, CancellationToken ct)
            {
                var frame = new Frame { Context = _context };
                Assert.True(frame.TryParse(bytes), "Relay parse failed: " + frame.ErrorReason);
                return link.Writer.WriteAsync(frame, ct).AsTask();
            }

            Task<Frame> ReceiveAsync(Channel<Frame> link, CancellationToken ct)
                => link.Reader.ReadAsync(ct).AsTask();

            Task VehicleResponderAsync(CancellationToken ct) => Task.Run(async () =>
            {
                while (await g2v.Reader.WaitToReadAsync(ct))
                {
                    while (g2v.Reader.TryRead(out var frame))
                    {
                        switch (frame.MessageId)
                        {
                            case ParameterProtocol.ParamRequestListId:
                                foreach (var p in paramsById.Values)
                                    await RelayAsync(v2g, ParameterProtocol.CreateParamValue(_context, 2, 3, p).ToBytes(), ct);
                                break;

                            case ParameterProtocol.ParamRequestReadId:
                                var id = ToString((char[])frame.Fields["param_id"]);
                                if (paramsById.TryGetValue(id, out var requested))
                                    await RelayAsync(v2g, ParameterProtocol.CreateParamValue(_context, 2, 3, requested).ToBytes(), ct);
                                break;

                            case ParameterProtocol.ParamSetId:
                                var setId = ToString((char[])frame.Fields["param_id"]);
                                var updated = new MavParamValue
                                {
                                    ParamId = setId,
                                    Value = (float)frame.Fields["param_value"],
                                    Type = (MavParamType)(byte)frame.Fields["param_type"],
                                    ParamCount = (ushort)(paramsById.TryGetValue(setId, out var old) ? old.ParamCount : 0),
                                    ParamIndex = (ushort)(paramsById.TryGetValue(setId, out var old2) ? old2.ParamIndex : 0)
                                };
                                paramsById[setId] = updated;
                                await RelayAsync(v2g, ParameterProtocol.CreateParamValue(_context, 2, 3, updated).ToBytes(), ct);
                                break;
                        }
                    }
                }
            });

            var ct = TestContext.Current.CancellationToken;
            var vehicleTask = VehicleResponderAsync(ct);

            var readAck = await ParameterProtocol.ReadParameterAsync(
                _context, 1, 1, 2, 3, "RC1_MAX",
                sendAsync: (b, t) => RelayAsync(g2v, b, t),
                receiveFrameAsync: t => ReceiveAsync(v2g, t),
                timeoutMs: 1500, maxRetries: 5,
                cancellationToken: ct);
            Assert.Equal("RC1_MAX", readAck.ParamId);
            Assert.Equal(1000f, readAck.Value, 3);

            var setAck = await ParameterProtocol.SetParameterAsync(
                _context, 1, 1, 2, 3, "THR_MAX", 850f, MavParamType.UInt16,
                sendAsync: (b, t) => RelayAsync(g2v, b, t),
                receiveFrameAsync: t => ReceiveAsync(v2g, t),
                timeoutMs: 1500, maxRetries: 5,
                cancellationToken: ct);
            Assert.Equal(850f, setAck.Value, 3);

            var download = await ParameterProtocol.DownloadParametersAsync(
                _context, 1, 1, 2, 3,
                sendAsync: (b, t) => RelayAsync(g2v, b, t),
                receiveFrameAsync: t => ReceiveAsync(v2g, t),
                timeoutMs: 1000, itemTimeoutMs: 50, maxRetries: 1, idleTolerance: 2,
                cancellationToken: ct);
            Assert.Equal(3, download.Cache.Count);
            Assert.True(download.Cache.TryGet<float>("THR_MAX", out float thr));
            Assert.Equal(850f, thr, 3);

            g2v.Writer.TryComplete();
            await vehicleTask;
        }

        [Fact]
        public void CreateParamExtRequestRead_CreatesValidFrame()
        {
            var frame = ParameterProtocol.CreateParamExtRequestRead(
                _context, 1, 1, 2, 3, "CFG_MODE", paramIndex: -1, sequence: 4);

            Assert.Equal(ParameterProtocol.ParamExtRequestReadId, frame.MessageId);
            Assert.Equal((byte)2, frame.Fields["target_system"]);
            Assert.Equal((byte)3, frame.Fields["target_component"]);
            Assert.Equal((short)-1, frame.Fields["param_index"]);
            Assert.Equal("CFG_MODE", ToString((char[])frame.Fields["param_id"]));

            var parsed = new Frame { Context = _context };
            Assert.True(parsed.TryParse(frame.ToBytes()));
            Assert.Equal("CFG_MODE", ToString((char[])parsed.Fields["param_id"]));
        }

        [Fact]
        public void CreateParamExtRequestList_RoundTrips()
        {
            var frame = ParameterProtocol.CreateParamExtRequestList(_context, 1, 1, 2, 3, sequence: 2);

            Assert.Equal(ParameterProtocol.ParamExtRequestListId, frame.MessageId);
            Assert.Equal((byte)2, frame.Fields["target_system"]);

            var parsed = new Frame { Context = _context };
            Assert.True(parsed.TryParse(frame.ToBytes()));
            Assert.Equal((byte)3, parsed.Fields["target_component"]);
        }

        [Fact]
        public void CreateParamExtValue_RoundTrips_CustomString()
        {
            var value = MavParamExtValue.Create("CFG_MODE", "AUTO");
            value.ParamCount = 3;
            value.ParamIndex = 0;
            var frame = ParameterProtocol.CreateParamExtValue(_context, 2, 3, value, sequence: 1);

            var parsed = new Frame { Context = _context };
            Assert.True(parsed.TryParse(frame.ToBytes()));
            Assert.True(ParameterProtocol.TryParseParamExtValue(parsed, out var result));
            Assert.NotNull(result);
            Assert.Equal("CFG_MODE", result.ParamId);
            Assert.Equal(MavParamExtType.Custom, result.Type);
            Assert.Equal("AUTO", result.Get<string>());
            Assert.Equal((ushort)3, result.ParamCount);
            Assert.Equal((ushort)0, result.ParamIndex);
        }

        [Fact]
        public void CreateParamExtValue_RoundTrips_Real64Precision()
        {
            var value = MavParamExtValue.Create("CFG_GAIN", Math.PI);
            value.ParamCount = 1;
            value.ParamIndex = 0;
            var frame = ParameterProtocol.CreateParamExtValue(_context, 2, 3, value);

            var parsed = new Frame { Context = _context };
            Assert.True(parsed.TryParse(frame.ToBytes()));
            Assert.True(ParameterProtocol.TryParseParamExtValue(parsed, out var result));
            Assert.NotNull(result);
            Assert.Equal(MavParamExtType.Real64, result.Type);
            Assert.Equal(Math.PI, result.Get<double>());
        }

        [Fact]
        public void CreateParamExtSet_RoundTripsLittleEndianValue()
        {
            var value = MavParamExtValue.Create("CFG_U16", (ushort)0x1234);
            var frame = ParameterProtocol.CreateParamExtSet(_context, 1, 1, 2, 3, value, sequence: 3);

            var parsed = new Frame { Context = _context };
            Assert.True(parsed.TryParse(frame.ToBytes()));
            Assert.Equal(ParameterProtocol.ParamExtSetId, parsed.MessageId);
            Assert.Equal((byte)2, parsed.Fields["target_system"]);
            Assert.Equal("CFG_U16", ToString((char[])parsed.Fields["param_id"]));
            Assert.Equal((byte)MavParamExtType.UInt16, parsed.Fields["param_type"]);

            var wire = WireValueBytes((char[])parsed.Fields["param_value"]);
            Assert.Equal(0x34, wire[0]); // little-endian: low byte first
            Assert.Equal(0x12, wire[1]);
            Assert.Equal(0, wire[2]);

            var decoded = new MavParamExtValue { ParamId = "CFG_U16", RawValue = wire, Type = MavParamExtType.UInt16 };
            Assert.Equal((ushort)0x1234, decoded.Get<ushort>());
        }

        [Fact]
        public void CreateParamExtAck_RoundTrips()
        {
            var value = MavParamExtValue.Create("CFG_MODE", "GUIDED");
            var frame = ParameterProtocol.CreateParamExtAck(_context, 2, 3, value, ParamExtAckResult.Accepted, sequence: 6);

            var parsed = new Frame { Context = _context };
            Assert.True(parsed.TryParse(frame.ToBytes()));
            Assert.True(ParameterProtocol.TryParseParamExtAck(parsed, out var ack));
            Assert.NotNull(ack);
            Assert.Equal("CFG_MODE", ack.ParamId);
            Assert.Equal(MavParamExtType.Custom, ack.Type);
            Assert.Equal("GUIDED", ack.Get<string>());
            Assert.Equal(ParamExtAckResult.Accepted, ack.Result);
        }

        [Fact]
        public void CreateParamAckTransaction_OnCommonDialect_Throws()
        {
            var value = new MavParamValue { ParamId = "THR_MAX", Value = 850f, Type = MavParamType.UInt16 };
            Assert.Throws<ParameterExchangeException>(() =>
                ParameterProtocol.CreateParamAckTransaction(_context, 2, 3, 1, 1, value, ParamExtAckResult.Accepted));
        }

        [Fact]
        public void CreateParamAckTransaction_RoundTrips()
        {
            var value = new MavParamValue { ParamId = "THR_MAX", Value = 850f, Type = MavParamType.UInt16 };
            var frame = ParameterProtocol.CreateParamAckTransaction(_devContext, 2, 3, 1, 1, value, ParamExtAckResult.Accepted, sequence: 8);

            var parsed = new Frame { Context = _devContext };
            Assert.True(parsed.TryParse(frame.ToBytes()));
            Assert.True(ParameterProtocol.TryParseParamAckTransaction(parsed, out var ack));
            Assert.NotNull(ack);
            Assert.Equal("THR_MAX", ack.ParamId);
            Assert.Equal(850f, ack.Value, 3);
            Assert.Equal(MavParamType.UInt16, ack.Type);
            Assert.Equal(ParamExtAckResult.Accepted, ack.Result);
        }

        [Fact]
        public void MavParamExtValue_RoundTrips_AllNumericTypes()
        {
            AssertExtRoundTrip((byte)200, MavParamExtType.UInt8);
            AssertExtRoundTrip((sbyte)-100, MavParamExtType.Int8);
            AssertExtRoundTrip((short)-12345, MavParamExtType.Int16);
            AssertExtRoundTrip((ushort)61234, MavParamExtType.UInt16);
            AssertExtRoundTrip(-123456789, MavParamExtType.Int32);
            AssertExtRoundTrip(3212345678u, MavParamExtType.UInt32);
            AssertExtRoundTrip(long.MinValue, MavParamExtType.Int64);
            AssertExtRoundTrip(ulong.MaxValue, MavParamExtType.UInt64);
            AssertExtRoundTrip(3.14159f, MavParamExtType.Real32);
            AssertExtRoundTrip(Math.PI, MavParamExtType.Real64);
        }

        [Fact]
        public void MavParamExtValue_Get_ConvertsAcrossTypes()
        {
            var value = MavParamExtValue.Create("P", 42);
            Assert.Equal(42, value.Get<int>());
            Assert.Equal(42L, value.Get<long>());
            Assert.Equal(42d, value.Get<double>());
            Assert.Equal(42m, value.Get<decimal>());
            Assert.Equal((byte)42, value.Get<byte>());
        }

        [Fact]
        public void MavParamExtValue_EncodesNumericLittleEndian()
        {
            var value = MavParamExtValue.Create("P", (ushort)0xABCD);
            Assert.Equal(0xCD, value.RawValue[0]);
            Assert.Equal(0xAB, value.RawValue[1]);
            Assert.Equal(0, value.RawValue[2]);
        }

        [Fact]
        public void MavParamExtValue_CustomString_TruncatesAt128Bytes()
        {
            var value = MavParamExtValue.Create("P", new string('A', 300));
            Assert.Equal(MavParamExtType.Custom, value.Type);
            Assert.Equal(128, value.Get<string>().Length);
            Assert.Equal(new string('A', 128), value.Get<string>());
        }

        [Fact]
        public void MavParamExtValue_Get_WrongType_Throws()
        {
            var custom = MavParamExtValue.Create("P", "text");
            Assert.Throws<InvalidCastException>(() => custom.Get<int>());

            var numeric = MavParamExtValue.Create("P", 5);
            Assert.Throws<InvalidCastException>(() => numeric.Get<string>());
        }

        [Fact]
        public void MavParamExtValue_Create_InfersTypeFromValue()
        {
            Assert.Equal(MavParamExtType.Custom, MavParamExtValue.Create("P", "x").Type);
            Assert.Equal(MavParamExtType.UInt8, MavParamExtValue.Create("P", (byte)1).Type);
            Assert.Equal(MavParamExtType.Int8, MavParamExtValue.Create("P", (sbyte)1).Type);
            Assert.Equal(MavParamExtType.Int16, MavParamExtValue.Create("P", (short)1).Type);
            Assert.Equal(MavParamExtType.UInt16, MavParamExtValue.Create("P", (ushort)1).Type);
            Assert.Equal(MavParamExtType.Int32, MavParamExtValue.Create("P", 1).Type);
            Assert.Equal(MavParamExtType.UInt32, MavParamExtValue.Create("P", 1u).Type);
            Assert.Equal(MavParamExtType.Int64, MavParamExtValue.Create("P", 1L).Type);
            Assert.Equal(MavParamExtType.UInt64, MavParamExtValue.Create("P", 1ul).Type);
            Assert.Equal(MavParamExtType.Real32, MavParamExtValue.Create("P", 1f).Type);
            Assert.Equal(MavParamExtType.Real64, MavParamExtValue.Create("P", 1d).Type);
            Assert.Equal(MavParamExtType.Real64, MavParamExtValue.Create("P", 1m).Type);
        }

        [Fact]
        public void MavParamExtValue_Create_StringWithNonCustomType_Throws()
        {
            Assert.Throws<ArgumentException>(() => MavParamExtValue.Create("P", "text", MavParamExtType.UInt8));
        }

        [Fact]
        public void ParameterExtCache_StoresAndGetsTypedValues()
        {
            var cache = new ParameterExtCache();
            cache.Set(MavParamExtValue.Create("CFG_STR", "hello"));
            cache.Set(MavParamExtValue.Create("CFG_NUM", 42));

            Assert.Equal(2, cache.Count);
            Assert.True(cache.TryGet("CFG_STR", out MavParamExtValue? v));
            Assert.NotNull(v);
            Assert.Equal("hello", v.Get<string>());
            Assert.True(cache.TryGet<int>("CFG_NUM", out int n));
            Assert.Equal(42, n);
            Assert.Null(cache.Get("MISSING"));
            Assert.True(cache.Remove("CFG_NUM"));
            Assert.Equal(1, cache.Count);
            cache.Clear();
            Assert.Equal(0, cache.Count);
        }

        [Fact]
        public async Task DownloadExtParametersAsync_HappyPath_PopulatesCache()
        {
            var frames = new[]
            {
                BuildExtValueFrame("CFG_MODE", "AUTO", count: 3, index: 0),
                BuildExtValueFrame("CFG_GAIN", 2.75d, count: 3, index: 1),
                BuildExtValueFrame("CFG_CNT", (ushort)7, count: 3, index: 2)
            };

            var download = await ParameterProtocol.DownloadExtParametersAsync(
                _context, 1, 1, 2, 3,
                sendAsync: (bytes, _) => Task.CompletedTask,
                receiveFrameAsync: ReceiveFromQueue(frames, idleAfter: 3),
                timeoutMs: 500, itemTimeoutMs: 25, maxRetries: 1, idleTolerance: 3,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(3, download.Cache.Count);
            Assert.True(download.Cache.TryGet<string>("CFG_MODE", out string mode));
            Assert.Equal("AUTO", mode);
            Assert.True(download.Cache.TryGet<double>("CFG_GAIN", out double gain));
            Assert.Equal(2.75d, gain, 5);
            Assert.True(download.Complete);
        }

        [Fact]
        public async Task ReadExtParameterAsync_ReturnsMatchingValue()
        {
            var frame = BuildExtValueFrame("CFG_MODE", "AUTO", count: 10, index: 3);
            var result = await ParameterProtocol.ReadExtParameterAsync(
                _context, 1, 1, 2, 3, "CFG_MODE",
                sendAsync: (bytes, _) => Task.CompletedTask,
                receiveFrameAsync: _ => Task.FromResult(frame),
                timeoutMs: 500, maxRetries: 1,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal("CFG_MODE", result.ParamId);
            Assert.Equal("AUTO", result.Get<string>());
        }

        [Fact]
        public async Task SetExtParameterAsync_Accepted_ReturnsAckValue()
        {
            var value = MavParamExtValue.Create("CFG_MODE", "GUIDED");
            var ackFrame = ParameterProtocol.CreateParamExtAck(_context, 2, 3, value, ParamExtAckResult.Accepted);
            var ack = await ParameterProtocol.SetExtParameterAsync(
                _context, 1, 1, 2, 3, value,
                sendAsync: (bytes, _) => Task.CompletedTask,
                receiveFrameAsync: _ => Task.FromResult(ackFrame),
                timeoutMs: 500, maxRetries: 1,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal("CFG_MODE", ack.ParamId);
            Assert.Equal("GUIDED", ack.Get<string>());
            Assert.Equal(ParamExtAckResult.Accepted, ack.Result);
        }

        [Fact]
        public async Task SetExtParameterAsync_InProgressThenAccepted_WaitsForFinalAck()
        {
            var value = MavParamExtValue.Create("CFG_GAIN", 4.5d);
            var frames = new[]
            {
                ParameterProtocol.CreateParamExtAck(_context, 2, 3, value, ParamExtAckResult.InProgress),
                ParameterProtocol.CreateParamExtAck(_context, 2, 3, value, ParamExtAckResult.Accepted)
            };

            var ack = await ParameterProtocol.SetExtParameterAsync(
                _context, 1, 1, 2, 3, value,
                sendAsync: (bytes, _) => Task.CompletedTask,
                receiveFrameAsync: ReceiveFromQueue(frames, idleAfter: 0),
                timeoutMs: 500, maxRetries: 1,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(ParamExtAckResult.Accepted, ack.Result);
            Assert.Equal(4.5d, ack.Get<double>(), 5);
        }

        [Fact]
        public async Task SetExtParameterAsync_Failed_Throws()
        {
            var value = MavParamExtValue.Create("CFG_MODE", "GUIDED");
            var ackFrame = ParameterProtocol.CreateParamExtAck(_context, 2, 3, value, ParamExtAckResult.Failed);

            await Assert.ThrowsAsync<ParameterExchangeException>(() =>
                ParameterProtocol.SetExtParameterAsync(
                    _context, 1, 1, 2, 3, value,
                    sendAsync: (bytes, _) => Task.CompletedTask,
                    receiveFrameAsync: _ => Task.FromResult(ackFrame),
                    timeoutMs: 500, maxRetries: 1,
                    cancellationToken: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task SetExtParameterAsync_WhenTimeoutExhausted_Throws()
        {
            var value = MavParamExtValue.Create("CFG_MODE", "GUIDED");

            await Assert.ThrowsAsync<TimeoutException>(() =>
                ParameterProtocol.SetExtParameterAsync(
                    _context, 1, 1, 2, 3, value,
                    sendAsync: (bytes, _) => Task.CompletedTask,
                    receiveFrameAsync: _ => throw new System.OperationCanceledException(),
                    timeoutMs: 10, maxRetries: 1,
                    cancellationToken: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task BeginTransactionAsync_SendsStartCommandAndAwaitsAck()
        {
            var sent = new List<byte[]>();
            var frames = new[] { BuildCommandAckFrame(ParameterProtocol.MavCmdParamTransaction, MavResult.Accepted) };

            var result = await ParameterProtocol.BeginTransactionAsync(
                _context, 1, 1, 2, 3, ParamTransactionTransport.Param,
                sendAsync: (bytes, _) => { sent.Add(bytes); return Task.CompletedTask; },
                receiveFrameAsync: ReceiveFromQueue(frames, idleAfter: 0),
                timeoutMs: 500, maxRetries: 0,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(MavResult.Accepted, result.Result);
            Assert.True(result.Success);

            var parsed = new Frame { Context = _context };
            Assert.True(parsed.TryParse(sent[0]));
            Assert.Equal(CommandProtocol.CommandLongId, parsed.MessageId);
            Assert.Equal((ushort)ParameterProtocol.MavCmdParamTransaction, parsed.Fields["command"]);
            Assert.Equal(0f, parsed.Fields["param1"]); // Start
            Assert.Equal(0f, parsed.Fields["param2"]); // Param transport
        }

        [Fact]
        public async Task CommitTransactionAsync_SendsCommitCommandOverParamExt()
        {
            var sent = new List<byte[]>();
            var frames = new[] { BuildCommandAckFrame(ParameterProtocol.MavCmdParamTransaction, MavResult.Accepted) };

            var result = await ParameterProtocol.CommitTransactionAsync(
                _context, 1, 1, 2, 3, ParamTransactionTransport.ParamExt,
                sendAsync: (bytes, _) => { sent.Add(bytes); return Task.CompletedTask; },
                receiveFrameAsync: ReceiveFromQueue(frames, idleAfter: 0),
                timeoutMs: 500, maxRetries: 0,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(result.Success);

            var parsed = new Frame { Context = _context };
            Assert.True(parsed.TryParse(sent[0]));
            Assert.Equal(1f, parsed.Fields["param1"]); // Commit
            Assert.Equal(1f, parsed.Fields["param2"]); // ParamExt transport
        }

        [Fact]
        public async Task CancelTransactionAsync_SendsCancelCommand()
        {
            var sent = new List<byte[]>();
            var frames = new[] { BuildCommandAckFrame(ParameterProtocol.MavCmdParamTransaction, MavResult.Accepted) };

            var result = await ParameterProtocol.CancelTransactionAsync(
                _context, 1, 1, 2, 3, ParamTransactionTransport.Param,
                sendAsync: (bytes, _) => { sent.Add(bytes); return Task.CompletedTask; },
                receiveFrameAsync: ReceiveFromQueue(frames, idleAfter: 0),
                timeoutMs: 500, maxRetries: 0,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(result.Success);

            var parsed = new Frame { Context = _context };
            Assert.True(parsed.TryParse(sent[0]));
            Assert.Equal(2f, parsed.Fields["param1"]); // Cancel
            Assert.Equal(0f, parsed.Fields["param2"]); // Param transport
        }

        [Fact]
        public async Task SetParameterTransactionalAsync_FullFlow_BeginSetCommit()
        {
            var value = new MavParamValue { ParamId = "THR_MAX", Value = 850f, Type = MavParamType.UInt16 };
            var frames = new Frame[]
            {
                BuildCommandAckFrame(ParameterProtocol.MavCmdParamTransaction, MavResult.Accepted, _devContext),
                ParameterProtocol.CreateParamAckTransaction(_devContext, 2, 3, 1, 1, value, ParamExtAckResult.Accepted),
                BuildCommandAckFrame(ParameterProtocol.MavCmdParamTransaction, MavResult.Accepted, _devContext)
            };

            var ack = await ParameterProtocol.SetParameterTransactionalAsync(
                _devContext, 1, 1, 2, 3, "THR_MAX", 850f, MavParamType.UInt16,
                sendAsync: (bytes, _) => Task.CompletedTask,
                receiveFrameAsync: ReceiveFromQueue(frames, idleAfter: 0),
                timeoutMs: 500, maxRetries: 0,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal("THR_MAX", ack.ParamId);
            Assert.Equal(850f, ack.Value, 3);
            Assert.Equal(ParamExtAckResult.Accepted, ack.Result);
        }

        [Fact]
        public async Task SetParameterTransactionalAsync_InProgressThenAccepted()
        {
            var value = new MavParamValue { ParamId = "THR_MAX", Value = 850f, Type = MavParamType.UInt16 };
            var frames = new Frame[]
            {
                BuildCommandAckFrame(ParameterProtocol.MavCmdParamTransaction, MavResult.Accepted, _devContext),
                ParameterProtocol.CreateParamAckTransaction(_devContext, 2, 3, 1, 1, value, ParamExtAckResult.InProgress),
                ParameterProtocol.CreateParamAckTransaction(_devContext, 2, 3, 1, 1, value, ParamExtAckResult.Accepted),
                BuildCommandAckFrame(ParameterProtocol.MavCmdParamTransaction, MavResult.Accepted, _devContext)
            };

            var ack = await ParameterProtocol.SetParameterTransactionalAsync(
                _devContext, 1, 1, 2, 3, "THR_MAX", 850f, MavParamType.UInt16,
                sendAsync: (bytes, _) => Task.CompletedTask,
                receiveFrameAsync: ReceiveFromQueue(frames, idleAfter: 0),
                timeoutMs: 500, maxRetries: 0,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(ParamExtAckResult.Accepted, ack.Result);
            Assert.Equal(850f, ack.Value, 3);
        }

        [Fact]
        public async Task SetParameterTransactionalAsync_BeginRejected_Throws()
        {
            var frames = new Frame[]
            {
                BuildCommandAckFrame(ParameterProtocol.MavCmdParamTransaction, MavResult.Denied, _devContext)
            };

            await Assert.ThrowsAsync<ParameterExchangeException>(() =>
                ParameterProtocol.SetParameterTransactionalAsync(
                    _devContext, 1, 1, 2, 3, "THR_MAX", 850f, MavParamType.UInt16,
                    sendAsync: (bytes, _) => Task.CompletedTask,
                    receiveFrameAsync: ReceiveFromQueue(frames, idleAfter: 0),
                    timeoutMs: 500, maxRetries: 0,
                    cancellationToken: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task SetParameterTransactionalAsync_OnCommonDialect_Throws()
        {
            await Assert.ThrowsAsync<ParameterExchangeException>(() =>
                ParameterProtocol.SetParameterTransactionalAsync(
                    _context, 1, 1, 2, 3, "THR_MAX", 850f, MavParamType.UInt16,
                    sendAsync: (bytes, _) => Task.CompletedTask,
                    receiveFrameAsync: _ => Task.FromResult<Frame>(null!),
                    timeoutMs: 100, maxRetries: 0,
                    cancellationToken: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task SetExtParameterTransactionalAsync_FullFlow_BeginSetCommit()
        {
            var value = MavParamExtValue.Create("CFG_MODE", "GUIDED");
            var frames = new Frame[]
            {
                BuildCommandAckFrame(ParameterProtocol.MavCmdParamTransaction, MavResult.Accepted),
                ParameterProtocol.CreateParamExtAck(_context, 2, 3, value, ParamExtAckResult.Accepted),
                BuildCommandAckFrame(ParameterProtocol.MavCmdParamTransaction, MavResult.Accepted)
            };

            var ack = await ParameterProtocol.SetExtParameterTransactionalAsync(
                _context, 1, 1, 2, 3, value,
                sendAsync: (bytes, _) => Task.CompletedTask,
                receiveFrameAsync: ReceiveFromQueue(frames, idleAfter: 0),
                timeoutMs: 500, maxRetries: 0,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal("CFG_MODE", ack.ParamId);
            Assert.Equal("GUIDED", ack.Get<string>());
            Assert.Equal(ParamExtAckResult.Accepted, ack.Result);
        }

        [Fact]
        public async Task ExtParameter_ReadSetDownload_ThroughInMemoryVehicle()
        {
            var g2v = Channel.CreateUnbounded<Frame>();
            var v2g = Channel.CreateUnbounded<Frame>();

            MavParamExtValue CreateStored<T>(string id, T value, ushort index)
            {
                var stored = MavParamExtValue.Create(id, value);
                stored.ParamCount = 3;
                stored.ParamIndex = index;
                return stored;
            }

            var paramsById = new Dictionary<string, MavParamExtValue>
            {
                ["CFG_MODE"] = CreateStored("CFG_MODE", "AUTO", 0),
                ["CFG_GAIN"] = CreateStored("CFG_GAIN", 2.75d, 1),
                ["CFG_CNT"] = CreateStored("CFG_CNT", (ushort)7, 2)
            };

            Task RelayAsync(Channel<Frame> link, byte[] bytes, CancellationToken ct)
            {
                var frame = new Frame { Context = _context };
                Assert.True(frame.TryParse(bytes), "Relay parse failed: " + frame.ErrorReason);
                return link.Writer.WriteAsync(frame, ct).AsTask();
            }

            Task<Frame> ReceiveAsync(Channel<Frame> link, CancellationToken ct)
                => link.Reader.ReadAsync(ct).AsTask();

            Task VehicleResponderAsync(CancellationToken ct) => Task.Run(async () =>
            {
                while (await g2v.Reader.WaitToReadAsync(ct))
                {
                    while (g2v.Reader.TryRead(out var frame))
                    {
                        switch (frame.MessageId)
                        {
                            case ParameterProtocol.ParamExtRequestListId:
                                foreach (var p in paramsById.Values)
                                    await RelayAsync(v2g, ParameterProtocol.CreateParamExtValue(_context, 2, 3, p).ToBytes(), ct);
                                break;

                            case ParameterProtocol.ParamExtRequestReadId:
                                var id = ToString((char[])frame.Fields["param_id"]);
                                if (paramsById.TryGetValue(id, out var requested))
                                    await RelayAsync(v2g, ParameterProtocol.CreateParamExtValue(_context, 2, 3, requested).ToBytes(), ct);
                                break;

                            case ParameterProtocol.ParamExtSetId:
                                var setId = ToString((char[])frame.Fields["param_id"]);
                                var updated = new MavParamExtValue
                                {
                                    ParamId = setId,
                                    RawValue = WireValueBytes((char[])frame.Fields["param_value"]),
                                    Type = (MavParamExtType)(byte)frame.Fields["param_type"],
                                    ParamCount = (ushort)paramsById.Count,
                                    ParamIndex = (ushort)0
                                };
                                paramsById[setId] = updated;
                                await RelayAsync(v2g, ParameterProtocol.CreateParamExtAck(_context, 2, 3, updated, ParamExtAckResult.Accepted).ToBytes(), ct);
                                break;
                        }
                    }
                }
            });

            var ct = TestContext.Current.CancellationToken;
            var vehicleTask = VehicleResponderAsync(ct);

            var read = await ParameterProtocol.ReadExtParameterAsync(
                _context, 1, 1, 2, 3, "CFG_MODE",
                sendAsync: (b, t) => RelayAsync(g2v, b, t),
                receiveFrameAsync: t => ReceiveAsync(v2g, t),
                timeoutMs: 1500, maxRetries: 5,
                cancellationToken: ct);
            Assert.Equal("AUTO", read.Get<string>());

            var setAck = await ParameterProtocol.SetExtParameterAsync(
                _context, 1, 1, 2, 3, MavParamExtValue.Create("CFG_MODE", "GUIDED"),
                sendAsync: (b, t) => RelayAsync(g2v, b, t),
                receiveFrameAsync: t => ReceiveAsync(v2g, t),
                timeoutMs: 1500, maxRetries: 5,
                cancellationToken: ct);
            Assert.Equal("GUIDED", setAck.Get<string>());

            var download = await ParameterProtocol.DownloadExtParametersAsync(
                _context, 1, 1, 2, 3,
                sendAsync: (b, t) => RelayAsync(g2v, b, t),
                receiveFrameAsync: t => ReceiveAsync(v2g, t),
                timeoutMs: 1000, itemTimeoutMs: 50, maxRetries: 1, idleTolerance: 2,
                cancellationToken: ct);
            Assert.Equal(3, download.Cache.Count);
            Assert.True(download.Cache.TryGet<string>("CFG_MODE", out string mode));
            Assert.Equal("GUIDED", mode);
            Assert.True(download.Cache.TryGet<double>("CFG_GAIN", out double gain));
            Assert.Equal(2.75d, gain, 5);
            Assert.True(download.Complete);

            g2v.Writer.TryComplete();
            await vehicleTask;
        }

        [Fact]
        public async Task ParameterTransaction_ThroughInMemoryVehicle()
        {
            var g2v = Channel.CreateUnbounded<Frame>();
            var v2g = Channel.CreateUnbounded<Frame>();

            var classicParams = new Dictionary<string, MavParamValue>
            {
                ["THR_MAX"] = new MavParamValue { ParamId = "THR_MAX", Value = 900f, Type = MavParamType.UInt16, ParamCount = 1, ParamIndex = 0 }
            };
            var extParams = new Dictionary<string, MavParamExtValue>
            {
                ["CFG_MODE"] = MavParamExtValue.Create("CFG_MODE", "AUTO")
            };

            Task RelayAsync(Channel<Frame> link, byte[] bytes, CancellationToken ct)
            {
                var frame = new Frame { Context = _devContext };
                Assert.True(frame.TryParse(bytes), "Relay parse failed: " + frame.ErrorReason);
                return link.Writer.WriteAsync(frame, ct).AsTask();
            }

            Task<Frame> ReceiveAsync(Channel<Frame> link, CancellationToken ct)
                => link.Reader.ReadAsync(ct).AsTask();

            Task VehicleResponderAsync(CancellationToken ct) => Task.Run(async () =>
            {
                while (await g2v.Reader.WaitToReadAsync(ct))
                {
                    while (g2v.Reader.TryRead(out var frame))
                    {
                        switch (frame.MessageId)
                        {
                            case CommandProtocol.CommandLongId:
                                var command = (ushort)frame.Fields["command"];
                                if (command == ParameterProtocol.MavCmdParamTransaction)
                                    await RelayAsync(v2g, BuildCommandAckFrame(ParameterProtocol.MavCmdParamTransaction, MavResult.Accepted, _devContext).ToBytes(), ct);
                                break;

                            case ParameterProtocol.ParamSetId:
                                var setId = ToString((char[])frame.Fields["param_id"]);
                                var newValue = (float)frame.Fields["param_value"];
                                var newType = (MavParamType)(byte)frame.Fields["param_type"];
                                classicParams[setId] = new MavParamValue { ParamId = setId, Value = newValue, Type = newType, ParamCount = 1, ParamIndex = 0 };
                                await RelayAsync(v2g, ParameterProtocol.CreateParamAckTransaction(_devContext, 2, 3, 1, 1, classicParams[setId], ParamExtAckResult.Accepted).ToBytes(), ct);
                                break;

                            case ParameterProtocol.ParamExtSetId:
                                var extId = ToString((char[])frame.Fields["param_id"]);
                                var updated = new MavParamExtValue
                                {
                                    ParamId = extId,
                                    RawValue = WireValueBytes((char[])frame.Fields["param_value"]),
                                    Type = (MavParamExtType)(byte)frame.Fields["param_type"]
                                };
                                extParams[extId] = updated;
                                await RelayAsync(v2g, ParameterProtocol.CreateParamExtAck(_devContext, 2, 3, updated, ParamExtAckResult.Accepted).ToBytes(), ct);
                                break;
                        }
                    }
                }
            });

            var ct = TestContext.Current.CancellationToken;
            var vehicleTask = VehicleResponderAsync(ct);

            var classicAck = await ParameterProtocol.SetParameterTransactionalAsync(
                _devContext, 1, 1, 2, 3, "THR_MAX", 850f, MavParamType.UInt16,
                sendAsync: (b, t) => RelayAsync(g2v, b, t),
                receiveFrameAsync: t => ReceiveAsync(v2g, t),
                timeoutMs: 1500, maxRetries: 5,
                cancellationToken: ct);
            Assert.Equal("THR_MAX", classicAck.ParamId);
            Assert.Equal(850f, classicAck.Value, 3);
            Assert.Equal(ParamExtAckResult.Accepted, classicAck.Result);

            var extAck = await ParameterProtocol.SetExtParameterTransactionalAsync(
                _devContext, 1, 1, 2, 3, MavParamExtValue.Create("CFG_MODE", "GUIDED"),
                sendAsync: (b, t) => RelayAsync(g2v, b, t),
                receiveFrameAsync: t => ReceiveAsync(v2g, t),
                timeoutMs: 1500, maxRetries: 5,
                cancellationToken: ct);
            Assert.Equal("CFG_MODE", extAck.ParamId);
            Assert.Equal("GUIDED", extAck.Get<string>());
            Assert.Equal(ParamExtAckResult.Accepted, extAck.Result);

            g2v.Writer.TryComplete();
            await vehicleTask;
        }

        private static void AssertExtRoundTrip<T>(T expected, MavParamExtType type)
        {
            var value = MavParamExtValue.Create("P", expected, type);
            Assert.Equal(type, value.Type);
            Assert.Equal(expected, value.Get<T>());
        }

        private Frame BuildExtValueFrame<T>(string id, T value, ushort count, ushort index)
        {
            var param = MavParamExtValue.Create(id, value);
            param.ParamCount = count;
            param.ParamIndex = index;
            return ParameterProtocol.CreateParamExtValue(_context, 2, 3, param);
        }

        private Frame BuildCommandAckFrame(ushort command, MavResult result, MavLinkContext? context = null)
        {
            var ctx = context ?? _context;
            var ackMsg = ctx.Metadata.MessagesDictionary[CommandProtocol.CommandAckId];
            var frame = new Frame
            {
                Context = ctx,
                StartMarker = Protocol.V2.StartMarker,
                SystemId = 2,
                ComponentId = 3,
                PacketSequence = 0,
                MessageId = CommandProtocol.CommandAckId,
                Message = ackMsg
            };

            frame.SetFields(new Dictionary<string, object>
            {
                ["command"] = command,
                ["result"] = (byte)result,
                ["progress"] = (byte)0,
                ["result_param2"] = 0,
                ["target_system"] = (byte)1,
                ["target_component"] = (byte)1
            });
            return frame;
        }

        private static byte[] WireValueBytes(char[] raw)
        {
            var bytes = new byte[raw.Length];
            for (int i = 0; i < raw.Length; i++)
                bytes[i] = (byte)raw[i];
            return bytes;
        }

        private Frame BuildValueFrame(string id, float value, ushort count, ushort index)
        {
            var param = new MavParamValue { ParamId = id, Value = value, Type = MavParamType.Real32, ParamCount = count, ParamIndex = index };
            return ParameterProtocol.CreateParamValue(_context, 2, 3, param);
        }

        private Func<CancellationToken, Task<Frame>> ReceiveFromQueue(Frame[] frames, int idleAfter)
        {
            var queue = new Queue<Frame>(frames);
            int idle = 0;
            return _ =>
            {
                if (queue.Count > 0)
                {
                    idle = 0;
                    return Task.FromResult(queue.Dequeue());
                }
                if (idle++ < idleAfter)
                    return Task.FromResult<Frame>(null!);
                throw new System.OperationCanceledException();
            };
        }

        private static string ToString(char[] raw)
        {
            int len = 0;
            while (len < raw.Length && raw[len] != '\0') len++;
            return new string(raw, 0, len);
        }
    }
}
