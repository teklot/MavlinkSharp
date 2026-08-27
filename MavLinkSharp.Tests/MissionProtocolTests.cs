using MavLinkSharp;
using MavLinkSharp.Enums;
using MavLinkSharp.Protocols;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace MavLinkSharp.Tests
{
    public class MissionProtocolTests
    {
        private readonly MavLinkContext _context;

        public MissionProtocolTests()
        {
            _context = new MavLinkContext();
            _context.Initialize(DialectType.Common);
        }

        [Fact]
        public void CreateMissionRequestList_CreatesValidFrame()
        {
            var frame = MissionProtocol.CreateMissionRequestList(
                _context, systemId: 1, componentId: 1,
                targetSystem: 2, targetComponent: 3,
                missionType: MavMissionType.Mission, sequence: 5);

            Assert.Equal(MissionProtocol.MissionRequestListId, frame.MessageId);
            Assert.Equal((byte)2, frame.Fields["target_system"]);
            Assert.Equal((byte)3, frame.Fields["target_component"]);
            Assert.Equal((byte)MavMissionType.Mission, frame.Fields["mission_type"]);

            var bytes = frame.ToBytes();
            var parsed = new Frame { Context = _context };
            Assert.True(parsed.TryParse(bytes));
            Assert.Equal(MissionProtocol.MissionRequestListId, parsed.MessageId);
        }

        [Fact]
        public void CreateMissionCount_CreatesValidFrame()
        {
            var frame = MissionProtocol.CreateMissionCount(
                _context, 1, 1, 2, 3, count: 3);

            Assert.Equal(MissionProtocol.MissionCountId, frame.MessageId);
            Assert.Equal((ushort)3, frame.Fields["count"]);
            Assert.Equal((byte)MavMissionType.Mission, frame.Fields["mission_type"]);
        }

        [Fact]
        public void CreateMissionClearAll_CreatesValidFrame()
        {
            var frame = MissionProtocol.CreateMissionClearAll(
                _context, 1, 1, 2, 3, MavMissionType.Mission);

            Assert.Equal(MissionProtocol.MissionClearAllId, frame.MessageId);
            Assert.Equal((byte)2, frame.Fields["target_system"]);
        }

        [Fact]
        public void CreateMissionRequestInt_CreatesValidFrame()
        {
            var frame = MissionProtocol.CreateMissionRequestInt(
                _context, 1, 1, 2, 3, seq: 7);

            Assert.Equal(MissionProtocol.MissionRequestIntId, frame.MessageId);
            Assert.Equal((ushort)7, frame.Fields["seq"]);
        }

        [Fact]
        public void CreateMissionItemInt_CreatesValidFrame()
        {
            var item = new MissionItem
            {
                Seq = 0,
                Command = 16, // MAV_CMD_NAV_WAYPOINT
                Frame = MavFrame.GlobalRelativeAltInt,
                Current = 1,
                AutoContinue = 1,
                Param1 = 0f, Param2 = 0f,
                Param3 = 5.0f, Param4 = 0f,
                X = 473977420,  // latitude * 1e7
                Y = 85455940,   // longitude * 1e7
                Z = 100.0f,
                Type = MavMissionType.Mission
            };

            var frame = MissionProtocol.CreateMissionItemInt(_context, 1, 1, 2, 3, item);

            Assert.Equal(MissionProtocol.MissionItemIntId, frame.MessageId);
            Assert.Equal((ushort)0, frame.Fields["seq"]);
            Assert.Equal((byte)MavFrame.GlobalRelativeAltInt, frame.Fields["frame"]);
            Assert.Equal((ushort)16, frame.Fields["command"]);
            Assert.Equal(473977420, frame.Fields["x"]);
            Assert.Equal(85455940, frame.Fields["y"]);
            Assert.Equal(100.0f, frame.Fields["z"]);
            Assert.Equal((byte)MavMissionType.Mission, frame.Fields["mission_type"]);

            // Round-trip via parser.
            var bytes = frame.ToBytes();
            var parsed = new Frame { Context = _context };
            Assert.True(parsed.TryParse(bytes));
            Assert.True(MissionProtocol.TryParseMissionItem(parsed, out var parsedItem));
            Assert.Equal(473977420, parsedItem!.X);
            Assert.Equal(85455940, parsedItem.Y);
            Assert.Equal(100.0f, parsedItem.Z);
            Assert.Equal(MavFrame.GlobalRelativeAltInt, parsedItem.Frame);
        }

        [Fact]
        public void CreateMissionSetCurrent_CreatesValidFrame()
        {
            var frame = MissionProtocol.CreateMissionSetCurrent(_context, 1, 1, 2, 3, seq: 4);

            Assert.Equal(MissionProtocol.MissionSetCurrentId, frame.MessageId);
            Assert.Equal((ushort)4, frame.Fields["seq"]);
        }

        [Fact]
        public void TryParseMissionAck_WithAccepted_ReturnsSuccess()
        {
            var frame = BuildMissionAckFrame(MavMissionResult.Accepted);

            var success = MissionProtocol.TryParseMissionAck(frame, out var ack);

            Assert.True(success);
            Assert.Equal(MavMissionResult.Accepted, ack!.Result);
            Assert.True(ack.Success);
        }

        [Fact]
        public void TryParseMissionAck_WithError_ReturnsNotSuccess()
        {
            var frame = BuildMissionAckFrame(MavMissionResult.NoSpace);

            var success = MissionProtocol.TryParseMissionAck(frame, out var ack);

            Assert.True(success);
            Assert.Equal(MavMissionResult.NoSpace, ack!.Result);
            Assert.False(ack.Success);
        }

        [Fact]
        public void TryParseMissionAck_WithNonAckFrame_ReturnsFalse()
        {
            var frame = BuildRequestFrame(0);

            var success = MissionProtocol.TryParseMissionAck(frame, out var ack);

            Assert.False(success);
            Assert.Null(ack);
        }

        [Fact]
        public async Task UploadMissionAsync_HappyPath_ReturnsAcceptedAck()
        {
            var items = BuildItems(2);
            var requests = new Queue<Frame>(new[]
            {
                BuildRequestFrame(0),
                BuildRequestFrame(1)
            });
            var ack = BuildMissionAckFrame(MavMissionResult.Accepted);

            int sendCount = 0;
            var result = await MissionProtocol.UploadMissionAsync(
                items, _context, 1, 1, 2, 3, MavMissionType.Mission,
                sendAsync: (bytes, _) =>
                {
                    sendCount++;
                    return Task.CompletedTask;
                },
                receiveFrameAsync: _ => Task.FromResult(requests.Count > 0 ? requests.Dequeue() : ack),
                timeoutMs: 500, itemTimeoutMs: 250,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(result.Success);
            Assert.Equal(MavMissionResult.Accepted, result.Result);
            // 1x MISSION_COUNT + 2x MISSION_ITEM_INT
            Assert.Equal(3, sendCount);
        }

        [Fact]
        public async Task UploadMissionAsync_WithRetry_RetransmitsCount()
        {
            var items = BuildItems(1);
            var ack = BuildMissionAckFrame(MavMissionResult.Accepted);
            var request0 = BuildRequestFrame(0);

            // First receive throws (timeout), second returns the request.
            var receives = new Queue<System.Func<Task<Frame>>>(new System.Func<Task<Frame>>[]
            {
                () => throw new System.OperationCanceledException(),
                () => Task.FromResult(request0),
                () => Task.FromResult(ack)
            });

            int sendCount = 0;
            var result = await MissionProtocol.UploadMissionAsync(
                items, _context, 1, 1, 2, 3, MavMissionType.Mission,
                sendAsync: (bytes, _) =>
                {
                    sendCount++;
                    return Task.CompletedTask;
                },
                receiveFrameAsync: _ => receives.Dequeue()(),
                timeoutMs: 20, itemTimeoutMs: 20, maxRetries: 3,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(result.Success);
            // MISSION_COUNT re-sent once after the first timeout.
            Assert.Equal(3, sendCount);
        }

        [Fact]
        public async Task UploadMissionAsync_WithAbortingAck_Throws()
        {
            var items = BuildItems(2);
            var request0 = BuildRequestFrame(0);
            var abort = BuildMissionAckFrame(MavMissionResult.Error);

            // GCS sends count, vehicle responds request(0), then when item is sent vehicle aborts.
            var receives = new Queue<Frame>(new[] { request0, abort });

            await Assert.ThrowsAsync<MissionAbortedException>(() =>
                MissionProtocol.UploadMissionAsync(
                    items, _context, 1, 1, 2, 3, MavMissionType.Mission,
                    sendAsync: (bytes, _) => Task.CompletedTask,
                    receiveFrameAsync: _ => Task.FromResult(receives.Dequeue()),
                    timeoutMs: 500, itemTimeoutMs: 250,
                    cancellationToken: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task UploadMissionAsync_WithTimeoutExhausted_Throws()
        {
            var items = BuildItems(1);

            await Assert.ThrowsAsync<TimeoutException>(() =>
                MissionProtocol.UploadMissionAsync(
                    items, _context, 1, 1, 2, 3, MavMissionType.Mission,
                    sendAsync: (bytes, _) => Task.CompletedTask,
                    receiveFrameAsync: _ => throw new System.OperationCanceledException(),
                    timeoutMs: 10, itemTimeoutMs: 10, maxRetries: 1,
                    cancellationToken: TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task DownloadMissionAsync_HappyPath_ReturnsItems()
        {
            var countFrame = BuildCountFrame(2);
            var item0 = BuildItemFrame(0, 16, 473977420, 85455940);
            var item1 = BuildItemFrame(1, 21, 475000000, 85000000);

            var receives = new Queue<Frame>(new[] { countFrame, item0, item1 });
            var sentMessageIds = new List<uint>();

            var result = await MissionProtocol.DownloadMissionAsync(
                _context, 1, 1, 2, 3, MavMissionType.Mission,
                sendAsync: (bytes, _) =>
                {
                    var f = new Frame { Context = _context };
                    f.TryParse(bytes);
                    sentMessageIds.Add(f.MessageId);
                    return Task.CompletedTask;
                },
                receiveFrameAsync: _ => Task.FromResult(receives.Dequeue()),
                timeoutMs: 500, itemTimeoutMs: 250,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(2, result.Items.Count);
            Assert.Equal((ushort)16, result.Items[0].Command);
            Assert.Equal(473977420, result.Items[0].X);
            Assert.Equal(85455940, result.Items[0].Y);
            Assert.Equal((ushort)21, result.Items[1].Command);

            // MISSION_REQUEST_LIST + 2x MISSION_REQUEST_INT + final MISSION_COUNT ack.
            Assert.Equal(MissionProtocol.MissionRequestListId, sentMessageIds[0]);
            Assert.Equal(MissionProtocol.MissionRequestIntId, sentMessageIds[1]);
            Assert.Equal(MissionProtocol.MissionRequestIntId, sentMessageIds[2]);
            Assert.Equal(MissionProtocol.MissionCountId, sentMessageIds[3]);
        }

        [Fact]
        public async Task DownloadMissionAsync_EmptyMission_ReturnsNoItems()
        {
            var countFrame = BuildCountFrame(0);
            var receives = new Queue<Frame>(new[] { countFrame });

            var result = await MissionProtocol.DownloadMissionAsync(
                _context, 1, 1, 2, 3, MavMissionType.Mission,
                sendAsync: (bytes, _) => Task.CompletedTask,
                receiveFrameAsync: _ => Task.FromResult(receives.Dequeue()),
                timeoutMs: 500,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Empty(result.Items);
        }

        [Fact]
        public async Task ClearMissionAsync_Success_ReturnsAck()
        {
            var ack = BuildMissionAckFrame(MavMissionResult.Accepted);

            int sendCount = 0;
            var result = await MissionProtocol.ClearMissionAsync(
                _context, 1, 1, 2, 3,
                sendAsync: (bytes, _) =>
                {
                    sendCount++;
                    return Task.CompletedTask;
                },
                receiveFrameAsync: _ => Task.FromResult(ack),
                timeoutMs: 500,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(result.Success);
            Assert.Equal(1, sendCount);
        }

        [Fact]
        public async Task ClearMissionAsync_ErrorAck_ReturnsNotSuccess()
        {
            var ack = BuildMissionAckFrame(MavMissionResult.Denied);

            var result = await MissionProtocol.ClearMissionAsync(
                _context, 1, 1, 2, 3,
                sendAsync: (bytes, _) => Task.CompletedTask,
                receiveFrameAsync: _ => Task.FromResult(ack),
                timeoutMs: 500,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result.Success);
            Assert.Equal(MavMissionResult.Denied, result.Result);
        }

        [Fact]
        public async Task Mission_UploadDownloadClear_ThroughInMemoryVehicle()
        {
            var g2v = Channel.CreateUnbounded<Frame>();
            var v2g = Channel.CreateUnbounded<Frame>();
            var stored = new List<MissionItem>();
            var vehicleLock = new object();
            int expectedCount = 0;

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
                            case MissionProtocol.MissionCountId:
                                expectedCount = (ushort)frame.Fields["count"];
                                await RelayAsync(v2g, MissionProtocol.CreateMissionRequestInt(_context, 2, 3, 1, 1, 0).ToBytes(), ct);
                                break;

                            case MissionProtocol.MissionItemIntId:
                                MissionProtocol.TryParseMissionItem(frame, out var item);
                                lock (vehicleLock) { if (item!.Seq >= stored.Count) stored.Add(item); else stored[item.Seq] = item; }
                                if (item.Seq + 1 < expectedCount)
                                    await RelayAsync(v2g, MissionProtocol.CreateMissionRequestInt(_context, 2, 3, 1, 1, (ushort)(item.Seq + 1)).ToBytes(), ct);
                                else
                                {
                                    await RelayAsync(v2g, MissionProtocol.CreateMissionCount(_context, 2, 3, 1, 1, (ushort)stored.Count).ToBytes(), ct);
                                    await RelayAsync(v2g, BuildMissionAckFrame(MavMissionResult.Accepted).ToBytes(), ct);
                                }
                                break;

                            case MissionProtocol.MissionRequestListId:
                                int count;
                                lock (vehicleLock) count = stored.Count;
                                await RelayAsync(v2g, MissionProtocol.CreateMissionCount(_context, 2, 3, 1, 1, (ushort)count).ToBytes(), ct);
                                break;

                            case MissionProtocol.MissionRequestIntId:
                                var seq = (ushort)frame.Fields["seq"];
                                MissionItem? requested;
                                lock (vehicleLock) requested = seq < stored.Count ? stored[seq] : null;
                                if (requested != null)
                                    await RelayAsync(v2g, MissionProtocol.CreateMissionItemInt(_context, 2, 3, 1, 1, requested).ToBytes(), ct);
                                break;

                            case MissionProtocol.MissionClearAllId:
                                lock (vehicleLock) stored.Clear();
                                await RelayAsync(v2g, BuildMissionAckFrame(MavMissionResult.Accepted).ToBytes(), ct);
                                break;
                        }
                    }
                }
            });

            var ct = TestContext.Current.CancellationToken;
            var vehicleTask = VehicleResponderAsync(ct);

            var uploadItems = new List<MissionItem>
            {
                new MissionItem { Seq = 0, Command = 16, X = 1, Y = 2, Z = 3 },
                new MissionItem { Seq = 1, Command = 16, X = 4, Y = 5, Z = 6 },
                new MissionItem { Seq = 2, Command = 21, X = 7, Y = 8, Z = 9 },
            };

            var uploadAck = await MissionProtocol.UploadMissionAsync(
                uploadItems, _context, 1, 1, 2, 3, MavMissionType.Mission,
                sendAsync: (b, t) => RelayAsync(g2v, b, t),
                receiveFrameAsync: t => ReceiveAsync(v2g, t),
                timeoutMs: 1500, itemTimeoutMs: 250, maxRetries: 5,
                cancellationToken: ct);
            Assert.True(uploadAck.Success);

            var download = await MissionProtocol.DownloadMissionAsync(
                _context, 1, 1, 2, 3, MavMissionType.Mission,
                sendAsync: (b, t) => RelayAsync(g2v, b, t),
                receiveFrameAsync: t => ReceiveAsync(v2g, t),
                timeoutMs: 1500, itemTimeoutMs: 250, maxRetries: 5,
                cancellationToken: ct);
            Assert.Equal(3, download.Items.Count);
            Assert.Equal((ushort)16, download.Items[0].Command);
            Assert.Equal(4, download.Items[1].X);
            Assert.Equal((ushort)21, download.Items[2].Command);

            var clearAck = await MissionProtocol.ClearMissionAsync(
                _context, 1, 1, 2, 3,
                sendAsync: (b, t) => RelayAsync(g2v, b, t),
                receiveFrameAsync: t => ReceiveAsync(v2g, t),
                timeoutMs: 1500, maxRetries: 5,
                cancellationToken: ct);
            Assert.True(clearAck.Success);

            g2v.Writer.TryComplete();
            await vehicleTask;
        }

        private static List<MissionItem> BuildItems(int count)
        {
            var items = new List<MissionItem>(count);
            for (int i = 0; i < count; i++)
            {
                items.Add(new MissionItem
                {
                    Seq = (ushort)i,
                    Command = (ushort)(16 + i), // MAV_CMD_NAV_WAYPOINT, LAND, ...
                    Frame = MavFrame.GlobalRelativeAltInt,
                    Current = 0,
                    AutoContinue = 1,
                    Z = 100.0f,
                    Type = MavMissionType.Mission
                });
            }
            return items;
        }

        private Frame BuildMissionAckFrame(MavMissionResult result)
        {
            var msg = _context.Metadata.MessagesDictionary[MissionProtocol.MissionAckId];
            var frame = new Frame
            {
                Context = _context,
                StartMarker = Protocol.V2.StartMarker,
                SystemId = 2,
                ComponentId = 3,
                PacketSequence = 0,
                MessageId = MissionProtocol.MissionAckId,
                Message = msg
            };
            frame.SetFields(new Dictionary<string, object>
            {
                ["target_system"] = (byte)1,
                ["target_component"] = (byte)1,
                ["type"] = (byte)result,
                ["mission_type"] = (byte)MavMissionType.Mission
            });
            return frame;
        }

        private Frame BuildRequestFrame(ushort seq)
        {
            var msg = _context.Metadata.MessagesDictionary[MissionProtocol.MissionRequestIntId];
            var frame = new Frame
            {
                Context = _context,
                StartMarker = Protocol.V2.StartMarker,
                SystemId = 2,
                ComponentId = 3,
                PacketSequence = 0,
                MessageId = MissionProtocol.MissionRequestIntId,
                Message = msg
            };
            frame.SetFields(new Dictionary<string, object>
            {
                ["target_system"] = (byte)1,
                ["target_component"] = (byte)1,
                ["seq"] = seq,
                ["mission_type"] = (byte)MavMissionType.Mission
            });
            return frame;
        }

        private Frame BuildCountFrame(ushort count)
        {
            var msg = _context.Metadata.MessagesDictionary[MissionProtocol.MissionCountId];
            var frame = new Frame
            {
                Context = _context,
                StartMarker = Protocol.V2.StartMarker,
                SystemId = 2,
                ComponentId = 3,
                PacketSequence = 0,
                MessageId = MissionProtocol.MissionCountId,
                Message = msg
            };
            frame.SetFields(new Dictionary<string, object>
            {
                ["target_system"] = (byte)1,
                ["target_component"] = (byte)1,
                ["count"] = count,
                ["mission_type"] = (byte)MavMissionType.Mission
            });
            return frame;
        }

        private Frame BuildItemFrame(ushort seq, ushort command, int x, int y)
        {
            var msg = _context.Metadata.MessagesDictionary[MissionProtocol.MissionItemIntId];
            var frame = new Frame
            {
                Context = _context,
                StartMarker = Protocol.V2.StartMarker,
                SystemId = 2,
                ComponentId = 3,
                PacketSequence = 0,
                MessageId = MissionProtocol.MissionItemIntId,
                Message = msg
            };
            frame.SetFields(new Dictionary<string, object>
            {
                ["target_system"] = (byte)1,
                ["target_component"] = (byte)1,
                ["seq"] = seq,
                ["frame"] = (byte)MavFrame.GlobalRelativeAltInt,
                ["command"] = command,
                ["current"] = (byte)0,
                ["autocontinue"] = (byte)1,
                ["param1"] = 0f,
                ["param2"] = 0f,
                ["param3"] = 0f,
                ["param4"] = 0f,
                ["x"] = x,
                ["y"] = y,
                ["z"] = 100.0f,
                ["mission_type"] = (byte)MavMissionType.Mission
            });
            return frame;
        }
    }
}
