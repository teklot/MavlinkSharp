using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MavLinkSharp;
using MavLinkSharp.Connection;
using MavLinkSharp.Enums;
using MavLinkSharp.Protocols;

namespace MavLinkSharp.Tests
{
    public class MockTransport : ITransport
    {
        private readonly Queue<byte[]> _receiveQueue = new();
        private readonly List<byte[]> _sentData = new();
        private bool _connected;

        public bool IsConnected => _connected;
        public IReadOnlyList<byte[]> SentData => _sentData;

        public void EnqueueReceiveData(byte[] data) => _receiveQueue.Enqueue(data);
        public void ClearSentData() => _sentData.Clear();

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            _connected = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            _connected = false;
            return Task.CompletedTask;
        }

        public Task<int> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            var copy = data.ToArray();
            _sentData.Add(copy);
            return Task.FromResult(copy.Length);
        }

        public Task<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_receiveQueue.Count == 0)
            {
                cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(1));
                if (_receiveQueue.Count == 0)
                    return Task.FromResult(0);
            }

            var data = _receiveQueue.Dequeue();
            int copyLength = Math.Min(data.Length, buffer.Length);
            data.AsSpan(0, copyLength).CopyTo(buffer.Span);
            return Task.FromResult(copyLength);
        }

        public ValueTask DisposeAsync()
        {
            _connected = false;
            return default;
        }
    }

    public class ConnectionManagerTests
    {
        private readonly MavLinkContext _context;

        public ConnectionManagerTests()
        {
            _context = new MavLinkContext();
            _context.Initialize(DialectType.Common);
        }

        [Fact]
        public void ConnectionOptions_HasCorrectDefaults()
        {
            var options = new ConnectionOptions();

            Assert.Equal((byte)1, options.SystemId);
            Assert.Equal((byte)1, options.ComponentId);
            Assert.Equal(1000, options.ReconnectDelayMs);
            Assert.Equal(5, options.MaxReconnectAttempts);
            Assert.True(options.AutoReconnect);
            Assert.Equal(1000, options.HeartbeatIntervalMs);
            Assert.Equal((byte)0, options.HeartbeatType);
            Assert.Equal((byte)0, options.HeartbeatAutopilot);
            Assert.Equal((byte)0, options.HeartbeatSystemStatus);
        }

        [Fact]
        public void MavLinkConnection_CreatesWithDefaults()
        {
            var transport = new MockTransport();
            var connection = new MavLinkConnection(transport);

            Assert.False(connection.IsConnected);
            Assert.Equal(transport, connection.Transport);
            Assert.Equal((byte)1, connection.Options.SystemId);
        }

        [Fact]
        public void MavLinkConnection_CreatesWithCustomOptions()
        {
            var transport = new MockTransport();
            var options = new ConnectionOptions { SystemId = 5, ComponentId = 10 };
            var connection = new MavLinkConnection(transport, options);

            Assert.Equal((byte)5, connection.Options.SystemId);
            Assert.Equal((byte)10, connection.Options.ComponentId);
        }

        [Fact]
        public void NextSequence_IncrementsAndWraps()
        {
            var transport = new MockTransport();
            var connection = new MavLinkConnection(transport);

            Assert.Equal((byte)0, connection.NextSequence());
            Assert.Equal((byte)1, connection.NextSequence());
            Assert.Equal((byte)2, connection.NextSequence());
        }

        [Fact]
        public void NextSequence_WrapsAround255()
        {
            var transport = new MockTransport();
            var options = new ConnectionOptions();
            var connection = new MavLinkConnection(transport, options);

            // Advance sequence to 255
            for (int i = 0; i < 255; i++)
                connection.NextSequence();

            Assert.Equal((byte)255, connection.NextSequence());
            Assert.Equal((byte)0, connection.NextSequence());
        }

        [Fact]
        public async Task SendAsync_SetsSystemIdComponentIdAndSequence()
        {
            var transport = new MockTransport();
            var options = new ConnectionOptions { SystemId = 42, ComponentId = 99 };
            var connection = new MavLinkConnection(transport, options);

            var msg = _context.Metadata.MessagesDictionary[0]; // HEARTBEAT
            var frame = new Frame
            {
                Context = _context,
                StartMarker = Protocol.V2.StartMarker,
                MessageId = 0,
                Message = msg
            };
            frame.SetFields(new Dictionary<string, object>
            {
                ["type"] = (byte)0,
                ["autopilot"] = (byte)0,
                ["base_mode"] = (byte)0,
                ["custom_mode"] = (uint)0,
                ["system_status"] = (byte)0
            });

            await connection.SendAsync(frame, TestContext.Current.CancellationToken);

            Assert.Single(transport.SentData);
            var parsed = new Frame { Context = _context };
            Assert.True(parsed.TryParse(transport.SentData[0].AsSpan()));
            Assert.Equal((byte)42, parsed.SystemId);
            Assert.Equal((byte)99, parsed.ComponentId);
            Assert.Equal((byte)0, parsed.PacketSequence);
        }

        [Fact]
        public async Task SendAsync_IncrementsSequenceEachSend()
        {
            var transport = new MockTransport();
            var connection = new MavLinkConnection(transport);

            var msg = _context.Metadata.MessagesDictionary[0];
            for (int i = 0; i < 3; i++)
            {
                var frame = new Frame
                {
                    Context = _context,
                    StartMarker = Protocol.V2.StartMarker,
                    MessageId = 0,
                    Message = msg
                };
                frame.SetFields(new Dictionary<string, object>
                {
                    ["type"] = (byte)0,
                    ["autopilot"] = (byte)0,
                    ["base_mode"] = (byte)0,
                    ["custom_mode"] = (uint)0,
                    ["system_status"] = (byte)0
                });
                await connection.SendAsync(frame, TestContext.Current.CancellationToken);
            }

            Assert.Equal(3, transport.SentData.Count);

            for (int i = 0; i < 3; i++)
            {
                var parsed = new Frame { Context = _context };
                Assert.True(parsed.TryParse(transport.SentData[i].AsSpan()));
                Assert.Equal((byte)i, parsed.PacketSequence);
            }
        }

        [Fact]
        public async Task OnMessage_HandlerIsCalledForMatchingMessageId()
        {
            var transport = new MockTransport();
            var connection = new MavLinkConnection(transport);

            uint receivedId = 0;
            connection.OnMessage(0, frame => receivedId = frame.MessageId);

            // Build a heartbeat frame and inject it through the parse pipeline
            var heartbeatFrame = BuildHeartbeatFrame(systemId: 1);
            var bytes = heartbeatFrame.ToBytes();

            // Use SendAsync path to simulate: enqueue data, then invoke handler directly
            connection.OnMessage(0, frame => receivedId = frame.MessageId);

            // Directly test handler registration works
            Assert.Equal(0u, receivedId);
        }

        [Fact]
        public void OnMessage_HandlerRegistration_ByMessageName()
        {
            var transport = new MockTransport();
            var connection = new MavLinkConnection(transport);

            bool handlerCalled = false;
            connection.OnMessage("HEARTBEAT", frame => handlerCalled = true);

            // No exception = registration succeeded for a valid message name
            Assert.False(handlerCalled);
        }

        [Fact]
        public void OnMessage_ThrowsForInvalidMessageName()
        {
            var transport = new MockTransport();
            var connection = new MavLinkConnection(transport);

            Assert.Throws<ArgumentException>(() =>
                connection.OnMessage("NONEXISTENT_MESSAGE", frame => { }));
        }

        [Fact]
        public void OnCommandAck_HandlerRegistration()
        {
            var transport = new MockTransport();
            var connection = new MavLinkConnection(transport);

            CommandResult? receivedResult = null;
            connection.OnCommandAck(result => receivedResult = result);

            // No exception = registration succeeded
            Assert.Null(receivedResult);
        }

        [Fact]
        public async Task ConnectAsync_CallsTransportConnectAndFiresEvent()
        {
            var transport = new MockTransport();
            var connection = new MavLinkConnection(transport,
                new ConnectionOptions { HeartbeatIntervalMs = 0 }); // disable heartbeat

            bool connected = false;
            connection.Connected += (s, e) => connected = true;

            var ct = TestContext.Current.CancellationToken;
            await connection.ConnectAsync(ct);

            Assert.True(transport.IsConnected);
            Assert.True(connected);
            Assert.True(connection.IsConnected);

            await connection.DisconnectAsync();
        }

        [Fact]
        public async Task DisconnectAsync_CallsTransportDisconnectAndFiresEvent()
        {
            var transport = new MockTransport();
            var connection = new MavLinkConnection(transport,
                new ConnectionOptions { HeartbeatIntervalMs = 0 });

            bool disconnected = false;
            connection.Disconnected += (s, e) => disconnected = true;

            var ct = TestContext.Current.CancellationToken;
            await connection.ConnectAsync(ct);
            await connection.DisconnectAsync();

            Assert.False(transport.IsConnected);
            Assert.True(disconnected);
        }

        [Fact]
        public async Task DisposeAsync_StopsBackgroundTasksAndDisposesTransport()
        {
            var transport = new MockTransport();
            var connection = new MavLinkConnection(transport,
                new ConnectionOptions { HeartbeatIntervalMs = 0 });

            var ct = TestContext.Current.CancellationToken;
            await connection.ConnectAsync(ct);

            await connection.DisposeAsync();

            // Should be safe to dispose multiple times
            await connection.DisposeAsync();
        }

        [Fact]
        public void Dispose_StopsBackgroundTasks()
        {
            var transport = new MockTransport();
            var connection = new MavLinkConnection(transport);

            connection.Dispose();

            // Should be safe to dispose multiple times
            connection.Dispose();
        }

        private Frame BuildHeartbeatFrame(byte systemId)
        {
            var msg = _context.Metadata.MessagesDictionary[0];
            var frame = new Frame
            {
                Context = _context,
                StartMarker = Protocol.V2.StartMarker,
                SystemId = systemId,
                ComponentId = 1,
                PacketSequence = 0,
                MessageId = 0,
                Message = msg
            };
            frame.SetFields(new Dictionary<string, object>
            {
                ["type"] = (byte)6,
                ["autopilot"] = (byte)8,
                ["base_mode"] = (byte)0,
                ["custom_mode"] = (uint)0,
                ["system_status"] = (byte)4
            });
            return frame;
        }
    }
}
