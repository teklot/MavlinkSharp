using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MavLinkSharp.Connection
{
    /// <summary>
    /// TCP transport implementation for <see cref="MavLinkConnection"/>.
    /// Supports both client-mode (connect to remote) and server-mode (listen and accept).
    /// </summary>
    public class TcpTransport : ITransport
    {
        private TcpClient _tcpClient;
        private NetworkStream _stream;
        private TcpListener _listener;
        private bool _connected;
        private readonly object _lock = new object();

        /// <summary>
        /// Creates a new TCP transport that connects to the specified remote endpoint.
        /// </summary>
        /// <param name="remoteAddress">The remote IP address to connect to.</param>
        /// <param name="remotePort">The remote port to connect to.</param>
        public TcpTransport(string remoteAddress, int remotePort)
        {
            _tcpClient = new TcpClient();
            _tcpClient.ConnectAsync(IPAddress.Parse(remoteAddress), remotePort).GetAwaiter().GetResult();
            _stream = _tcpClient.GetStream();
            _connected = true;
        }

        /// <summary>
        /// Creates a new TCP transport that connects to the specified remote endpoint.
        /// </summary>
        /// <param name="remoteEndPoint">The remote endpoint to connect to.</param>
        public TcpTransport(IPEndPoint remoteEndPoint)
        {
            _tcpClient = new TcpClient();
            _tcpClient.ConnectAsync(remoteEndPoint.Address, remoteEndPoint.Port).GetAwaiter().GetResult();
            _stream = _tcpClient.GetStream();
            _connected = true;
        }

        /// <summary>
        /// Creates a new TCP transport that listens on the specified local endpoint
        /// and waits for a client connection.
        /// </summary>
        /// <param name="localEndPoint">The local endpoint to listen on.</param>
        /// <param name="asServer">Must be <c>true</c> to create a server transport.</param>
        public TcpTransport(IPEndPoint localEndPoint, bool asServer)
        {
            if (!asServer)
                throw new ArgumentException("Use the client constructor for client mode.", nameof(asServer));

            _listener = new TcpListener(localEndPoint);
            _listener.Start();
        }

        /// <summary>
        /// Creates a new TCP transport using an existing <see cref="TcpClient"/>.
        /// </summary>
        /// <param name="tcpClient">The pre-configured TCP client.</param>
        public TcpTransport(TcpClient tcpClient)
        {
            _tcpClient = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));
            _stream = tcpClient.GetStream();
            _connected = tcpClient.Connected;
        }

        /// <inheritdoc/>
        public bool IsConnected
        {
            get { lock (_lock) { return _connected; } }
        }

        /// <summary>
        /// Gets the underlying <see cref="TcpClient"/> instance.
        /// </summary>
        public TcpClient Client => _tcpClient;

        /// <summary>
        /// Gets the underlying <see cref="NetworkStream"/> for direct access.
        /// </summary>
        public NetworkStream Stream => _stream;

        /// <inheritdoc/>
        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (_listener != null)
            {
                _tcpClient = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                _stream = _tcpClient.GetStream();
            }

            lock (_lock)
            {
                _connected = true;
            }
        }

        /// <inheritdoc/>
        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                _connected = false;
            }

            _stream?.Dispose();
            _tcpClient?.Dispose();
            _listener?.Stop();

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public async Task<int> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            if (_stream == null || !_connected)
                throw new InvalidOperationException("Not connected.");

            byte[] buffer;
            if (MemoryMarshal.TryGetArray(data, out var arraySegment))
            {
                buffer = arraySegment.Array;
            }
            else
            {
                buffer = data.ToArray();
            }

            await _stream.WriteAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
            return buffer.Length;
        }

        /// <inheritdoc/>
        public async Task<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_stream == null || !_connected)
                throw new InvalidOperationException("Not connected.");

            byte[] tempBuffer = new byte[buffer.Length];
            int bytesRead = await _stream.ReadAsync(tempBuffer, 0, tempBuffer.Length, cancellationToken).ConfigureAwait(false);
            tempBuffer.AsSpan(0, bytesRead).CopyTo(buffer.Span);
            return bytesRead;
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }

        /// <summary>
        /// Releases the TCP client and listener resources.
        /// </summary>
        public void Dispose()
        {
            _stream?.Dispose();
            _tcpClient?.Dispose();
            _listener?.Stop();
        }
    }
}
