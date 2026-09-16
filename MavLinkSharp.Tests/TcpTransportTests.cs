using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MavLinkSharp.Connection;
using Xunit;

namespace MavLinkSharp.Tests
{
    public class TcpTransportTests
    {
        [Fact]
        public async Task Connect_Disconnect_Reconnect_ReEstablishesSocket()
        {
            var ct = TestContext.Current.CancellationToken;
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var transport = new TcpTransport(new IPEndPoint(IPAddress.Loopback, port));

            await transport.ConnectAsync(ct);
            Assert.True(transport.IsConnected);
            Assert.True(transport.Client!.Connected);

            var serverClient1 = await listener.AcceptTcpClientAsync(ct);

            await transport.DisconnectAsync(ct);
            Assert.False(transport.IsConnected);
            Assert.Null(transport.Client);

            // A client-mode transport must be able to re-establish the socket after teardown.
            await transport.ConnectAsync(ct);
            var serverClient2 = await listener.AcceptTcpClientAsync(ct);
            Assert.True(transport.IsConnected);
            Assert.True(transport.Client!.Connected);
            Assert.NotSame(serverClient1, serverClient2);

            await transport.DisposeAsync();
            serverClient1.Dispose();
            serverClient2.Dispose();
            listener.Stop();
        }

        [Fact]
        public async Task Constructor_DoesNotThrowWhenHostUnavailable()
        {
            // Port 1 on loopback refuses connections immediately.
            var transport = new TcpTransport("127.0.0.1", 1);
            var ct = TestContext.Current.CancellationToken;

            Assert.False(transport.IsConnected);
            await Assert.ThrowsAsync<SocketException>(() => transport.ConnectAsync(ct));
            Assert.False(transport.IsConnected);
        }

        [Fact]
        public async Task Hostname_Constructor_Connects()
        {
            var ct = TestContext.Current.CancellationToken;
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var transport = new TcpTransport("localhost", port);

            await transport.ConnectAsync(ct);
            Assert.True(transport.IsConnected);

            var serverClient = await listener.AcceptTcpClientAsync(ct);

            await transport.DisposeAsync();
            serverClient.Dispose();
            listener.Stop();
        }

        [Fact]
        public async Task WrappedClient_ReconnectsToCapturedEndpoint()
        {
            var ct = TestContext.Current.CancellationToken;
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(IPAddress.Loopback, port, ct);
            var serverClient1 = await listener.AcceptTcpClientAsync(ct);

            var transport = new TcpTransport(tcpClient);
            Assert.True(transport.IsConnected);
            Assert.Same(tcpClient, transport.Client);

            await transport.DisconnectAsync(ct);
            Assert.False(transport.IsConnected);

            // Reconnect reuses the endpoint captured from the original TcpClient.
            await transport.ConnectAsync(ct);
            var serverClient2 = await listener.AcceptTcpClientAsync(ct);
            Assert.True(transport.IsConnected);
            Assert.True(transport.Client!.Connected);

            await transport.DisposeAsync();
            serverClient1.Dispose();
            serverClient2.Dispose();
            listener.Stop();
        }

        [Fact]
        public async Task WrappedNeverConnectedClient_ThrowsOnConnect()
        {
            var transport = new TcpTransport(new TcpClient());

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => transport.ConnectAsync(TestContext.Current.CancellationToken));
        }
    }
}