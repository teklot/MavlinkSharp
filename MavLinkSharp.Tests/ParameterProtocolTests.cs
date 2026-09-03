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

        public ParameterProtocolTests()
        {
            _context = new MavLinkContext();
            _context.Initialize(DialectType.Common);
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
