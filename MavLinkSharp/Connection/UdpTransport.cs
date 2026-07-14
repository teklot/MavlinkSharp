using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MavLinkSharp.Connection
{
    /// <summary>
    /// UDP transport implementation for <see cref="MavLinkConnection"/>.
    /// Supports both client-mode (send to remote) and server-mode (listen on local port).
    /// </summary>
    public class UdpTransport : ITransport
    {
        private readonly UdpClient _udpClient;
        private IPEndPoint _remoteEndPoint;
        private bool _connected;
        private readonly object _lock = new object();

        /// <summary>
        /// Creates a new UDP transport that listens on the specified local port
        /// and sends to the specified remote endpoint.
        /// </summary>
        /// <param name="localPort">The local port to bind to for receiving. Use 0 for auto-assign.</param>
        /// <param name="remoteAddress">The remote IP address to send to.</param>
        /// <param name="remotePort">The remote port to send to.</param>
        public UdpTransport(int localPort, string remoteAddress, int remotePort)
        {
            _udpClient = new UdpClient(localPort);
            _remoteEndPoint = new IPEndPoint(IPAddress.Parse(remoteAddress), remotePort);
        }

        /// <summary>
        /// Creates a new UDP transport that listens on the specified local port
        /// and sends to the specified remote endpoint.
        /// </summary>
        /// <param name="localPort">The local port to bind to for receiving. Use 0 for auto-assign.</param>
        /// <param name="remoteEndPoint">The remote endpoint to send to.</param>
        public UdpTransport(int localPort, IPEndPoint remoteEndPoint)
        {
            _udpClient = new UdpClient(localPort);
            _remoteEndPoint = remoteEndPoint ?? throw new ArgumentNullException(nameof(remoteEndPoint));
        }

        /// <summary>
        /// Creates a new UDP transport bound to the specified local endpoint.
        /// The remote endpoint must be set via <see cref="SetRemoteEndpoint"/> before sending.
        /// </summary>
        /// <param name="localEndPoint">The local endpoint to bind to.</param>
        public UdpTransport(IPEndPoint localEndPoint)
        {
            _udpClient = new UdpClient(localEndPoint);
        }

        /// <summary>
        /// Creates a new UDP transport using an existing <see cref="UdpClient"/>.
        /// </summary>
        /// <param name="udpClient">The pre-configured UDP client.</param>
        /// <param name="remoteEndPoint">Optional remote endpoint for sending.</param>
        public UdpTransport(UdpClient udpClient, IPEndPoint remoteEndPoint = null)
        {
            _udpClient = udpClient ?? throw new ArgumentNullException(nameof(udpClient));
            _remoteEndPoint = remoteEndPoint;
        }

        /// <inheritdoc/>
        public bool IsConnected
        {
            get { lock (_lock) { return _connected; } }
        }

        /// <summary>
        /// Gets the underlying <see cref="UdpClient"/> instance.
        /// </summary>
        public UdpClient Client => _udpClient;

        /// <summary>
        /// Sets or updates the remote endpoint for sending.
        /// </summary>
        /// <param name="remoteEndPoint">The remote endpoint to send to.</param>
        public void SetRemoteEndpoint(IPEndPoint remoteEndPoint)
        {
            _remoteEndPoint = remoteEndPoint ?? throw new ArgumentNullException(nameof(remoteEndPoint));
        }

        /// <inheritdoc/>
        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                _connected = true;
            }
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                _connected = false;
            }
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public async Task<int> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            if (_remoteEndPoint == null)
                throw new InvalidOperationException("No remote endpoint configured. Call SetRemoteEndpoint() first.");

            byte[] buffer;
            if (MemoryMarshal.TryGetArray(data, out var arraySegment))
            {
                buffer = arraySegment.Array;
            }
            else
            {
                buffer = data.ToArray();
            }

            return await _udpClient.SendAsync(buffer, buffer.Length, _remoteEndPoint).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var result = await _udpClient.ReceiveAsync().ConfigureAwait(false);

            int copyLength = Math.Min(result.Buffer.Length, buffer.Length);
            result.Buffer.AsSpan(0, copyLength).CopyTo(buffer.Span);
            return copyLength;
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }

        /// <summary>
        /// Releases the UDP client resources.
        /// </summary>
        public void Dispose()
        {
            _udpClient?.Dispose();
        }
    }
}
