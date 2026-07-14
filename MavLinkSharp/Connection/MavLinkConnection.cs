using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using MavLinkSharp.Protocols;

namespace MavLinkSharp.Connection
{
    /// <summary>
    /// Event arguments for the Connected event.
    /// </summary>
    public class ConnectedEventArgs : EventArgs
    {
        /// <summary>
        /// Creates a new instance of <see cref="ConnectedEventArgs"/>.
        /// </summary>
        public ConnectedEventArgs() { }
    }

    /// <summary>
    /// Event arguments for the Disconnected event.
    /// </summary>
    public class DisconnectedEventArgs : EventArgs
    {
        /// <summary>
        /// Gets whether the disconnection was due to an error.
        /// </summary>
        public bool HasError { get; }

        /// <summary>
        /// Gets the exception that caused the disconnection, if any.
        /// </summary>
        public Exception Exception { get; }

        /// <summary>
        /// Creates a new instance of <see cref="DisconnectedEventArgs"/>.
        /// </summary>
        /// <param name="exception">The exception that caused the disconnection, or null.</param>
        public DisconnectedEventArgs(Exception exception = null)
        {
            HasError = exception != null;
            Exception = exception;
        }
    }

    /// <summary>
    /// Provides a high-level, event-driven MAVLink connection that wraps an <see cref="ITransport"/>.
    /// Handles frame parsing, automatic sequence numbering, heartbeats, reconnection,
    /// and typed message handlers.
    /// </summary>
#if NETSTANDARD2_0
    public class MavLinkConnection : IDisposable
#else
    public class MavLinkConnection : IAsyncDisposable, IDisposable
#endif
    {
        private readonly ITransport _transport;
        private readonly ConnectionOptions _options;
        private readonly MavLinkContext _context;
        private readonly Frame _receiveFrame;
        private readonly ConcurrentDictionary<uint, List<Action<Frame>>> _messageHandlers;
        private readonly ConcurrentDictionary<uint, List<Action<CommandResult>>> _commandAckHandlers;
        private readonly List<Action<Frame>> _packetReceivedHandlers;
        private readonly object _sequenceLock = new object();
        private byte _packetSequence;
        private CancellationTokenSource _receiveCts;
        private Task _receiveTask;
        private Task _heartbeatTask;
        private Task _reconnectTask;
        private bool _disposed;
        private bool _userDisconnected;

        /// <summary>
        /// Occurs when the connection is established.
        /// </summary>
        public event EventHandler<ConnectedEventArgs> Connected;

        /// <summary>
        /// Occurs when the connection is lost.
        /// </summary>
        public event EventHandler<DisconnectedEventArgs> Disconnected;

        /// <summary>
        /// Occurs when any MAVLink packet is received.
        /// </summary>
        public event Action<Frame> PacketReceived
        {
            add { _packetReceivedHandlers.Add(value); }
            remove { _packetReceivedHandlers.Remove(value); }
        }

        /// <summary>
        /// Creates a new MavLinkConnection wrapping the specified transport.
        /// </summary>
        /// <param name="transport">The transport layer to use for communication.</param>
        /// <param name="options">Optional connection configuration.</param>
        public MavLinkConnection(ITransport transport, ConnectionOptions options = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _options = options ?? new ConnectionOptions();
            _context = _options.Context ?? MavLinkContext.Default;
            _receiveFrame = new Frame { Context = _context };
            _messageHandlers = new ConcurrentDictionary<uint, List<Action<Frame>>>();
            _commandAckHandlers = new ConcurrentDictionary<uint, List<Action<CommandResult>>>();
            _packetReceivedHandlers = new List<Action<Frame>>();
        }

        /// <summary>
        /// Gets the underlying transport.
        /// </summary>
        public ITransport Transport => _transport;

        /// <summary>
        /// Gets whether the connection is active.
        /// </summary>
        public bool IsConnected => _transport.IsConnected;

        /// <summary>
        /// Gets the connection options.
        /// </summary>
        public ConnectionOptions Options => _options;

        /// <summary>
        /// Gets the current packet sequence number.
        /// </summary>
        public byte PacketSequence
        {
            get { lock (_sequenceLock) { return _packetSequence; } }
        }

        /// <summary>
        /// Gets the next packet sequence number and increments the counter.
        /// </summary>
        public byte NextSequence()
        {
            lock (_sequenceLock)
            {
                byte seq = _packetSequence;
                _packetSequence++;
                return seq;
            }
        }

        /// <summary>
        /// Connects to the transport and starts the receive loop, heartbeat, and reconnection monitoring.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token to stop the connection.</param>
        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            _userDisconnected = false;
            _context.ThrowIfNotInitialized();

            await _transport.ConnectAsync(cancellationToken).ConfigureAwait(false);
            OnConnected();

            _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _receiveTask = RunReceiveLoopAsync(_receiveCts.Token);

            if (_options.HeartbeatIntervalMs > 0)
            {
                _heartbeatTask = RunHeartbeatLoopAsync(_receiveCts.Token);
            }
        }

        /// <summary>
        /// Disconnects from the transport and stops all background tasks.
        /// </summary>
        public async Task DisconnectAsync()
        {
            _userDisconnected = true;
            await StopBackgroundTasksAsync().ConfigureAwait(false);
            await _transport.DisconnectAsync().ConfigureAwait(false);
            OnDisconnected(new DisconnectedEventArgs());
        }

        /// <summary>
        /// Registers a handler for a specific MAVLink message ID.
        /// </summary>
        /// <param name="messageId">The message ID to listen for.</param>
        /// <param name="handler">The handler to invoke when a matching message is received.</param>
        public void OnMessage(uint messageId, Action<Frame> handler)
        {
            _messageHandlers.AddOrUpdate(messageId,
                _ => new List<Action<Frame>> { handler },
                (_, list) => { list.Add(handler); return list; });
        }

        /// <summary>
        /// Registers a handler for a specific MAVLink message by name.
        /// </summary>
        /// <param name="messageName">The message name (e.g., "HEARTBEAT") to listen for.</param>
        /// <param name="handler">The handler to invoke when a matching message is received.</param>
        public void OnMessage(string messageName, Action<Frame> handler)
        {
            foreach (var kvp in _context.Metadata.MessagesDictionary)
            {
                if (string.Equals(kvp.Value.Name, messageName, StringComparison.OrdinalIgnoreCase))
                {
                    OnMessage(kvp.Key, handler);
                    return;
                }
            }
            throw new ArgumentException($"Message '{messageName}' not found in the current dialect.", nameof(messageName));
        }

        /// <summary>
        /// Registers a handler for COMMAND_ACK messages.
        /// </summary>
        /// <param name="handler">The handler to invoke when a COMMAND_ACK is received.</param>
        public void OnCommandAck(Action<CommandResult> handler)
        {
            _commandAckHandlers.AddOrUpdate(CommandProtocol.CommandAckId,
                _ => new List<Action<CommandResult>> { handler },
                (_, list) => { list.Add(handler); return list; });
        }

        /// <summary>
        /// Sends a frame over the connection with automatic sequence numbering.
        /// </summary>
        /// <param name="frame">The frame to send.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task SendAsync(Frame frame, CancellationToken cancellationToken = default)
        {
            frame.SystemId = _options.SystemId;
            frame.ComponentId = _options.ComponentId;
            frame.PacketSequence = NextSequence();
            frame.Context = _context;

            if (_options.Signing != null && frame.Signing == null)
            {
                frame.EnableSigning(_options.Signing);
            }

            byte[] bytes = frame.ToBytes();
            await _transport.SendAsync(bytes, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends a COMMAND_LONG or COMMAND_INT and waits for the matching COMMAND_ACK
        /// with timeout and retry support.
        /// </summary>
        /// <param name="commandFrame">The command frame to send.</param>
        /// <param name="timeoutMs">Milliseconds to wait for a COMMAND_ACK per attempt.</param>
        /// <param name="retries">Number of retry attempts on timeout.</param>
        /// <param name="progress">Optional progress reporter for in-progress results.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The command result.</returns>
        public async Task<CommandResult> SendCommandAsync(
            Frame commandFrame,
            int timeoutMs = 5000,
            int retries = 0,
            IProgress<CommandResult> progress = null,
            CancellationToken cancellationToken = default)
        {
            return await CommandProtocol.SendCommandAsync(
                commandFrame,
                async (bytes, ct) => await _transport.SendAsync(bytes, ct).ConfigureAwait(false),
                async (ct) =>
                {
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    linkedCts.CancelAfter(timeoutMs);
                    return await ReceiveFrameAsync(linkedCts.Token).ConfigureAwait(false);
                },
                timeoutMs,
                retries,
                progress,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates and sends a HEARTBEAT message.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task SendHeartbeatAsync(CancellationToken cancellationToken = default)
        {
            var msg = _context.Metadata.MessagesDictionary[0]; // HEARTBEAT is always ID 0
            var frame = new Frame
            {
                Context = _context,
                StartMarker = Protocol.V2.StartMarker,
                SystemId = _options.SystemId,
                ComponentId = _options.ComponentId,
                PacketSequence = NextSequence(),
                MessageId = 0,
                Message = msg
            };

            frame.SetFields(new Dictionary<string, object>
            {
                ["type"] = _options.HeartbeatType,
                ["autopilot"] = _options.HeartbeatAutopilot,
                ["base_mode"] = (byte)0,
                ["custom_mode"] = (uint)0,
                ["system_status"] = _options.HeartbeatSystemStatus
            });

            if (_options.Signing != null)
            {
                frame.EnableSigning(_options.Signing);
            }

            byte[] bytes = frame.ToBytes();
            await _transport.SendAsync(bytes, cancellationToken).ConfigureAwait(false);
        }

        private async Task RunReceiveLoopAsync(CancellationToken cancellationToken)
        {
            var pipe = new Pipe();

            var fillTask = FillPipeAsync(pipe.Writer, cancellationToken);
            var parseTask = ParsePipeAsync(pipe.Reader, cancellationToken);

            await Task.WhenAll(fillTask, parseTask).ConfigureAwait(false);
        }

        private async Task FillPipeAsync(PipeWriter writer, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    Memory<byte> memory = writer.GetMemory();
                    int bytesRead = await _transport.ReceiveAsync(memory, cancellationToken).ConfigureAwait(false);

                    if (bytesRead == 0)
                    {
                        break;
                    }

                    writer.Advance(bytesRead);
                    FlushResult result = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

                    if (result.IsCompleted || result.IsCanceled)
                    {
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        OnDisconnected(new DisconnectedEventArgs(ex));
                        if (_options.AutoReconnect && !_userDisconnected)
                        {
                            _reconnectTask = RunReconnectAsync();
                        }
                    }
                    break;
                }
            }

            await writer.CompleteAsync().ConfigureAwait(false);
        }

        private async Task ParsePipeAsync(PipeReader reader, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ReadResult result;
                try
                {
                    result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                ReadOnlySequence<byte> buffer = result.Buffer;

                while (true)
                {
                    if (_receiveFrame.TryParse(buffer, out SequencePosition consumed, out SequencePosition examined))
                    {
                        var resultFrame = new Frame { Context = _context };
                        var frameSequence = buffer.Slice(0, buffer.GetPosition(0, consumed));
                        resultFrame.TryParse(frameSequence.ToArray().AsSpan());

                        InvokePacketReceived(resultFrame);
                        buffer = buffer.Slice(consumed);
                    }
                    else
                    {
                        reader.AdvanceTo(consumed, examined);
                        break;
                    }
                }

                if (result.IsCompleted) break;
            }

            reader.Complete();
        }

        private void InvokePacketReceived(Frame frame)
        {
            foreach (var handler in _packetReceivedHandlers)
            {
                try
                {
                    handler(frame);
                }
                catch
                {
                    // Swallow handler exceptions to prevent breaking the receive loop
                }
            }

            if (_messageHandlers.TryGetValue(frame.MessageId, out var handlers))
            {
                foreach (var handler in handlers)
                {
                    try
                    {
                        handler(frame);
                    }
                    catch
                    {
                        // Swallow handler exceptions
                    }
                }
            }

            if (frame.MessageId == CommandProtocol.CommandAckId &&
                CommandProtocol.TryParseCommandAck(frame, out var cmdResult))
            {
                if (_commandAckHandlers.TryGetValue(CommandProtocol.CommandAckId, out var ackHandlers))
                {
                    foreach (var handler in ackHandlers)
                    {
                        try
                        {
                            handler(cmdResult);
                        }
                        catch
                        {
                            // Swallow handler exceptions
                        }
                    }
                }
            }
        }

        private async Task RunHeartbeatLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_options.HeartbeatIntervalMs, cancellationToken).ConfigureAwait(false);

                    if (_transport.IsConnected)
                    {
                        await SendHeartbeatAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Continue heartbeat loop even if a single send fails
                }
            }
        }

        private async Task RunReconnectAsync()
        {
            int attempt = 0;
            while (attempt < _options.MaxReconnectAttempts && !_userDisconnected)
            {
                try
                {
                    await Task.Delay(_options.ReconnectDelayMs).ConfigureAwait(false);
                    await _transport.ConnectAsync().ConfigureAwait(false);

                    OnConnected();
                    _receiveCts?.Dispose();
                    _receiveCts = new CancellationTokenSource();
                    _receiveTask = RunReceiveLoopAsync(_receiveCts.Token);

                    if (_options.HeartbeatIntervalMs > 0)
                    {
                        _heartbeatTask = RunHeartbeatLoopAsync(_receiveCts.Token);
                    }
                    return;
                }
                catch
                {
                    attempt++;
                }
            }
        }

        private void OnConnected()
        {
            Connected?.Invoke(this, new ConnectedEventArgs());
        }

        private void OnDisconnected(DisconnectedEventArgs args)
        {
            Disconnected?.Invoke(this, args);
        }

        private async Task StopBackgroundTasksAsync()
        {
            if (_receiveCts != null)
            {
                _receiveCts.Cancel();
            }

            if (_receiveTask != null)
            {
                try
                {
                    await _receiveTask.ConfigureAwait(false);
                }
                catch
                {
                    // Task may have been cancelled
                }
            }

            if (_heartbeatTask != null)
            {
                try
                {
                    await _heartbeatTask.ConfigureAwait(false);
                }
                catch
                {
                    // Task may have been cancelled
                }
            }

            if (_reconnectTask != null)
            {
                try
                {
                    await _reconnectTask.ConfigureAwait(false);
                }
                catch
                {
                    // Task may have been cancelled
                }
            }

            _receiveCts?.Dispose();
            _receiveCts = null;
        }

        private async Task<Frame> ReceiveFrameAsync(CancellationToken cancellationToken)
        {
            var pipe = new Pipe();

            var fillTask = FillPipeAsync(pipe.Writer, cancellationToken);
            var parseTask = ReadOneFrameAsync(pipe.Reader, cancellationToken);

            var frame = await parseTask.ConfigureAwait(false);
            await fillTask.ConfigureAwait(false);
            return frame;
        }

        private async Task<Frame> ReadOneFrameAsync(PipeReader reader, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ReadResult result;
                try
                {
                    result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }

                ReadOnlySequence<byte> buffer = result.Buffer;

                while (true)
                {
                    if (_receiveFrame.TryParse(buffer, out SequencePosition consumed, out SequencePosition examined))
                    {
                        var resultFrame = new Frame { Context = _context };
                        var frameSequence = buffer.Slice(0, buffer.GetPosition(0, consumed));
                        resultFrame.TryParse(frameSequence.ToArray().AsSpan());

                        reader.AdvanceTo(consumed);
                        return resultFrame;
                    }
                    else
                    {
                        reader.AdvanceTo(consumed, examined);
                        break;
                    }
                }

                if (result.IsCompleted) break;
            }

            reader.Complete();
            return null;
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                await StopBackgroundTasksAsync().ConfigureAwait(false);
                await _transport.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Releases resources used by the connection.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _receiveCts?.Cancel();
                _receiveCts?.Dispose();
            }
        }
    }
}
