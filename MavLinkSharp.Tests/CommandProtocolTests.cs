using MavLinkSharp;
using MavLinkSharp.Enums;
using MavLinkSharp.Protocols;

namespace MavLinkSharp.Tests
{
    public class CommandProtocolTests
    {
        private readonly MavLinkContext _context;

        public CommandProtocolTests()
        {
            _context = new MavLinkContext();
            _context.Initialize(DialectType.Common);
        }

        [Fact]
        public void CreateCommandLong_CreatesValidFrame()
        {
            var frame = CommandProtocol.CreateCommandLong(
                _context, systemId: 1, componentId: 1,
                targetSystem: 2, targetComponent: 3,
                command: 16, // MAV_CMD_NAV_WAYPOINT
                parameters: [0f, 0f, 0f, 0f, 47.5f, 8.4f, 100f],
                confirmation: 0, sequence: 5);

            Assert.Equal(CommandProtocol.CommandLongId, frame.MessageId);
            Assert.Equal((ushort)16, frame.Fields["command"]);
            Assert.Equal((byte)2, frame.Fields["target_system"]);
            Assert.Equal((byte)3, frame.Fields["target_component"]);
            Assert.Equal((byte)0, frame.Fields["confirmation"]);

            // Round-trip serialization
            var bytes = frame.ToBytes();
            var parsed = new Frame { Context = _context };
            Assert.True(parsed.TryParse(bytes));
            Assert.Equal(CommandProtocol.CommandLongId, parsed.MessageId);
            Assert.Equal((byte)2, parsed.Fields["target_system"]);
        }

        [Fact]
        public void CreateCommandInt_CreatesValidFrame()
        {
            var frame = CommandProtocol.CreateCommandInt(
                _context, systemId: 1, componentId: 1,
                targetSystem: 2, targetComponent: 3,
                frameType: 0, // MAV_FRAME_GLOBAL
                command: 21,  // MAV_CMD_NAV_LAND
                parameters: [0f, 0f, 0f, 0f],
                x: 473977420, y: 85455940, z: 100.0f,
                sequence: 3);

            Assert.Equal(CommandProtocol.CommandIntId, frame.MessageId);
            Assert.Equal((ushort)21, frame.Fields["command"]);
            Assert.Equal(473977420, frame.Fields["x"]);

            var bytes = frame.ToBytes();
            var parsed = new Frame { Context = _context };
            Assert.True(parsed.TryParse(bytes));
            Assert.Equal(CommandProtocol.CommandIntId, parsed.MessageId);
        }

        [Fact]
        public void TryParseCommandAck_WithValidFrame_ReturnsResult()
        {
            var frame = BuildCommandAckFrame(76, MavResult.Accepted, progress: 0, resultParam2: 0);

            var success = CommandProtocol.TryParseCommandAck(frame, out var result);

            Assert.True(success);
            Assert.Equal((ushort)76, result.Command);
            Assert.Equal(MavResult.Accepted, result.Result);
            Assert.True(result.Success);
        }

        [Fact]
        public void TryParseCommandAck_WithFailedResult_ReturnsNotSuccess()
        {
            var frame = BuildCommandAckFrame(16, MavResult.Failed, progress: 0, resultParam2: 0);

            var success = CommandProtocol.TryParseCommandAck(frame, out var result);

            Assert.True(success);
            Assert.Equal(MavResult.Failed, result.Result);
            Assert.False(result.Success);
        }

        [Fact]
        public void TryParseCommandAck_WithInProgress_ReturnsProgress()
        {
            var frame = BuildCommandAckFrame(16, MavResult.InProgress, progress: 50, resultParam2: 0);

            var success = CommandProtocol.TryParseCommandAck(frame, out var result);

            Assert.True(success);
            Assert.Equal(MavResult.InProgress, result.Result);
            Assert.Equal((byte)50, result.Progress);
        }

        [Fact]
        public void TryParseCommandAck_WithNonAckFrame_ReturnsFalse()
        {
            var frame = new Frame { Context = _context };
            var heartbeat = _context.Metadata.MessagesDictionary[0];
            frame.Message = heartbeat;
            frame.MessageId = 0;
            frame.SetFields(new Dictionary<string, object>
            {
                ["type"] = (byte)0,
                ["autopilot"] = (byte)0,
                ["base_mode"] = (byte)0,
                ["custom_mode"] = (uint)0,
                ["system_status"] = (byte)0,
                ["mavlink_version"] = (byte)3
            });

            var success = CommandProtocol.TryParseCommandAck(frame, out var result);

            Assert.False(success);
            Assert.Null(result);
        }

        [Fact]
        public void CreateCommandCancel_CreatesValidFrame()
        {
            var frame = CommandProtocol.CreateCommandCancel(
                _context, systemId: 1, componentId: 1,
                targetSystem: 2, targetComponent: 3,
                command: 21);

            Assert.Equal(CommandProtocol.CommandCancelId, frame.MessageId);
            Assert.Equal((byte)2, frame.Fields["target_system"]);

            var bytes = frame.ToBytes();
            var parsed = new Frame { Context = _context };
            Assert.True(parsed.TryParse(bytes));
            Assert.Equal(CommandProtocol.CommandCancelId, parsed.MessageId);
        }

        [Fact]
        public async Task SendCommandAsync_WithMatchingAck_ReturnsResult()
        {
            var frame = CommandProtocol.CreateCommandLong(
                _context, 1, 1, 2, 3,
                command: 16,
                parameters: [0f, 0f, 0f, 0f, 47.5f, 8.4f, 100f]);

            var ackFrame = BuildCommandAckFrame(16, MavResult.Accepted);
            var ct = TestContext.Current.CancellationToken;

            var result = await CommandProtocol.SendCommandAsync(
                frame,
                sendAsync: (bytes, _) =>
                {
                    Assert.NotEmpty(bytes);
                    return Task.CompletedTask;
                },
                receiveFrameAsync: _ => Task.FromResult(ackFrame),
                timeoutMs: 1000,
                cancellationToken: ct);

            Assert.True(result.Success);
            Assert.Equal(MavResult.Accepted, result.Result);
        }

        [Fact]
        public async Task SendCommandAsync_WithWrongCommandId_KeepsWaiting()
        {
            var frame = CommandProtocol.CreateCommandLong(
                _context, 1, 1, 2, 3,
                command: 16,
                parameters: [0f, 0f, 0f, 0f, 47.5f, 8.4f, 100f]);

            var callCount = 0;
            var ackFrame = BuildCommandAckFrame(16, MavResult.Accepted);
            var wrongAckFrame = BuildCommandAckFrame(99, MavResult.Accepted);
            var ct = TestContext.Current.CancellationToken;

            var result = await CommandProtocol.SendCommandAsync(
                frame,
                sendAsync: (bytes, _) => Task.CompletedTask,
                receiveFrameAsync: _ =>
                {
                    callCount++;
                    return Task.FromResult(callCount == 1 ? wrongAckFrame : ackFrame);
                },
                timeoutMs: 5000,
                cancellationToken: ct);

            Assert.True(result.Success);
            Assert.Equal(2, callCount);
        }

        [Fact]
        public async Task SendCommandAsync_WithTimeout_ReturnsFailed()
        {
            var frame = CommandProtocol.CreateCommandLong(
                _context, 1, 1, 2, 3,
                command: 16,
                parameters: [0f, 0f, 0f, 0f, 47.5f, 8.4f, 100f]);

            var ct = TestContext.Current.CancellationToken;

            var result = await CommandProtocol.SendCommandAsync(
                frame,
                sendAsync: (bytes, _) => Task.CompletedTask,
                receiveFrameAsync: _ =>
                {
                    throw new OperationCanceledException();
                },
                timeoutMs: 500,
                retries: 2,
                cancellationToken: ct);

            Assert.False(result.Success);
            Assert.Equal(MavResult.Failed, result.Result);
        }

        [Fact]
        public async Task SendCommandAsync_WithCancellation_Cancels()
        {
            var frame = CommandProtocol.CreateCommandLong(
                _context, 1, 1, 2, 3,
                command: 16,
                parameters: [0f, 0f, 0f, 0f, 47.5f, 8.4f, 100f]);

            var ct = TestContext.Current.CancellationToken;

            var result = await CommandProtocol.SendCommandAsync(
                frame,
                sendAsync: (bytes, _) => Task.CompletedTask,
                receiveFrameAsync: _ =>
                {
                    throw new OperationCanceledException();
                },
                timeoutMs: 500,
                cancellationToken: ct);

            Assert.False(result.Success);
        }

        private Frame BuildCommandAckFrame(ushort command, MavResult result, byte progress = 0, int resultParam2 = 0)
        {
            var ackMsg = _context.Metadata.MessagesDictionary[CommandProtocol.CommandAckId];
            var frame = new Frame
            {
                Context = _context,
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
                ["progress"] = progress,
                ["result_param2"] = resultParam2,
                ["target_system"] = (byte)1,
                ["target_component"] = (byte)1
            });
            return frame;
        }
    }
}
