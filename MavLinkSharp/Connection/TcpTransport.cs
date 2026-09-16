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
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        private TcpListener? _listener;
        private IPEndPoint? _remoteEndPoint;
        private string? _remoteHost;
        private int _remotePort;
        private bool _connected;
        private readonly object _lock = new object();
        private readonly object _ioLock = new object();

        /// <summary>
        /// Creates a new TCP transport configured to connect to the specified remote address.
        /// The connection is established when <see cref="ConnectAsync"/> is called, allowing
        /// the transport (and <see cref="MavLinkConnection"/> auto-reconnect) to retry
        /// while the remote endpoint is unavailable.
        /// </summary>
        /// <param name="remoteAddress">The remote IP address or hostname to connect to.</param>
        /// <param name="remotePort">The remote port to connect to.</param>
        public TcpTransport(string remoteAddress, int remotePort)
        {
            if (string.IsNullOrWhiteSpace(remoteAddress))
                throw new ArgumentException("A remote address is required.", nameof(remoteAddress));

            if (IPAddress.TryParse(remoteAddress, out var address))
            {
                _remoteEndPoint = NormalizeRemote(new IPEndPoint(address, remotePort));
            }
            else
            {
                _remoteHost = remoteAddress;
                _remotePort = remotePort;
            }
        }

        /// <summary>
        /// Creates a new TCP transport configured to connect to the specified remote endpoint.
        /// The connection is established when <see cref="ConnectAsync"/> is called.
        /// </summary>
        /// <param name="remoteEndPoint">The remote endpoint to connect to.</param>
        public TcpTransport(IPEndPoint remoteEndPoint)
        {
            _remoteEndPoint = NormalizeRemote(remoteEndPoint ?? throw new ArgumentNullException(nameof(remoteEndPoint)));
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
        /// If the client was previously connected, the remote endpoint is captured so the
        /// transport can reconnect when the connection is lost.
        /// </summary>
        /// <param name="tcpClient">The pre-configured TCP client.</param>
        public TcpTransport(TcpClient tcpClient)
        {
            _tcpClient = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));
            _connected = tcpClient.Connected;
            _stream = tcpClient.Connected ? tcpClient.GetStream() : null;

            try
            {
                if (tcpClient.Client?.RemoteEndPoint is IPEndPoint ep)
                    _remoteEndPoint = NormalizeRemote(ep);
            }
            catch
            {
                _remoteEndPoint = null;
            }
        }

        /// <inheritdoc/>
        public bool IsConnected
        {
            get { lock (_lock) { return _connected; } }
        }

        /// <summary>
        /// Gets the underlying <see cref="TcpClient"/> instance.
        /// </summary>
        public TcpClient? Client => _tcpClient;

        /// <summary>
        /// Gets the underlying <see cref="NetworkStream"/> for direct access.
        /// </summary>
        public NetworkStream? Stream => _stream;

        /// <inheritdoc/>
        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (_listener != null)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                lock (_ioLock)
                {
                    _tcpClient = client;
                    _stream = client.GetStream();
                    lock (_lock) { _connected = true; }
                }
                return;
            }

            TcpClient? clientToConnect = null;
            string? remoteHost = _remoteHost;
            IPEndPoint? remoteEndPoint = _remoteEndPoint;

            lock (_ioLock)
            {
                // Already connected (e.g. a wrapped TcpClient that is still alive).
                if (_tcpClient != null && _tcpClient.Connected && _stream != null)
                {
                    lock (_lock) { _connected = true; }
                    return;
                }

                // Dispose any stale client/stream from a previous (broken) connection.
                DisposeClient();

                if (remoteHost != null)
                {
                    _tcpClient = new TcpClient();
                }
                else
                {
                    if (remoteEndPoint == null)
                        throw new InvalidOperationException(
                            "Cannot (re)connect: the remote endpoint is unknown. " +
                            "Connect the TcpClient before wrapping it, or construct the transport with an explicit endpoint.");

                    _tcpClient = new TcpClient(remoteEndPoint.AddressFamily);
                }

                clientToConnect = _tcpClient;
            }

            try
            {
                if (remoteHost != null)
                {
                    await clientToConnect.ConnectAsync(remoteHost, _remotePort).ConfigureAwait(false);
                }
                else
                {
                    await clientToConnect.ConnectAsync(remoteEndPoint!.Address, remoteEndPoint!.Port).ConfigureAwait(false);
                }
            }
            catch
            {
                DisposeClient();
                throw;
            }

            _stream = _tcpClient!.GetStream();
            lock (_lock) { _connected = true; }
        }

        /// <inheritdoc/>
        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            lock (_ioLock)
            {
                lock (_lock) { _connected = false; }

                if (_listener != null)
                {
                    _stream?.Dispose();
                    _tcpClient?.Dispose();
                    _listener.Stop();
                    _stream = null;
                    _tcpClient = null;
                }
                else
                {
                    DisposeClient();
                }
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public async Task<int> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            NetworkStream? stream = _stream;
            if (stream == null || !IsConnected)
                throw new InvalidOperationException("Not connected.");

            byte[] buffer;
            if (MemoryMarshal.TryGetArray(data, out var arraySegment))
            {
                buffer = arraySegment.Array!;
            }
            else
            {
                buffer = data.ToArray();
            }

            await stream.WriteAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
            return buffer.Length;
        }

        /// <inheritdoc/>
        public async Task<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            NetworkStream? stream = _stream;
            if (stream == null || !IsConnected)
                throw new InvalidOperationException("Not connected.");

            byte[] tempBuffer = new byte[buffer.Length];
            int bytesRead = await stream.ReadAsync(tempBuffer, 0, tempBuffer.Length, cancellationToken).ConfigureAwait(false);
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
            lock (_ioLock)
            {
                lock (_lock) { _connected = false; }
                _stream?.Dispose();
                _tcpClient?.Dispose();
                _listener?.Stop();
                _stream = null;
                _tcpClient = null;
            }
        }

        private void DisposeClient()
        {
            _stream?.Dispose();
            _tcpClient?.Dispose();
            _stream = null;
            _tcpClient = null;
            lock (_lock) { _connected = false; }
        }

        private static IPEndPoint NormalizeRemote(IPEndPoint endpoint)
        {
            if (endpoint.Address.IsIPv4MappedToIPv6)
                return new IPEndPoint(endpoint.Address.MapToIPv4(), endpoint.Port);
            return endpoint;
        }
    }
}