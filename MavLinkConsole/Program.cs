using MavLinkSharp;
using MavLinkSharp.Enums;
using System.Net;
using System.Net.Sockets;

namespace MavLinkConsole;

class Program
{
    private const int MavLinkUdpPort = 14550; // Standard MAVLink UDP port
    private const string TargetIpAddress = "127.0.0.1"; // Localhost (assuming same machine for Tx/Rx)

    static async Task Main(string[] args)
    {
        TerminalLayout.Initialize();

        // Initialize MavLinkSharp with the common dialect
        MavLink.Initialize(DialectType.Common);

        // Create a single UDP client for both sending and receiving
        // It's crucial to bind it for receiving first.
        using (var udpClient = new UdpClient(MavLinkUdpPort))
        {
            var remoteEndPoint = new IPEndPoint(IPAddress.Parse(TargetIpAddress), MavLinkUdpPort);

            // Run Tx and Rx tasks concurrently
            var txTask = Task.Run(() => Transmitter.Run(udpClient, remoteEndPoint));
            var rxTask = Receiver.RunAsync(udpClient);

            // Keep the application alive until both tasks complete (which will be never in this case)
            // or a cancellation token is used. For this example, we just await them.
            await Task.WhenAll(txTask, rxTask); 
        }
    }
}
