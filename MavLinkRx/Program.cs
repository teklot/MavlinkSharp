using MavLinkSharp;
using MavLinkSharp.Enums;
using System.Net;
using System.Net.Sockets;

namespace MavLinkRx;

class Program
{
    private const int MavLinkUdpPort = 14550; // Standard MAVLink UDP port
    private const string TargetIpAddress = "127.0.0.1"; // Localhost

    static void Main(string[] args)
    {
        Console.WriteLine("MavLinkRx started. Listening for MavLink messages...");

        // Initialize MavLinkSharp with the common dialect
        MavLink.Initialize(DialectType.Common);

        using (var udpClient = new UdpClient(MavLinkUdpPort))
        {
            var remoteEndPoint = new IPEndPoint(IPAddress.Parse(TargetIpAddress), MavLinkUdpPort);

            while (true)
            {
                try
                {
                    byte[] receivedBytes = udpClient.Receive(ref remoteEndPoint);
                    
                    if (Message.TryParse(receivedBytes, out var frame))
                    {
                        Console.WriteLine($"Received => " +
                            $"Seq: {frame.PacketSequence:D3}, " +
                            $"SysId: {frame.SystemId:X2}, " +
                            $"CompId: {frame.ComponentId:X2}, " +
                            $"Id: {frame.MessageId:X4}, " +
                            $"Name: {Metadata.Messages[frame.MessageId].Name}");
                    }
                    else 
                    {
                        if (frame.ErrorReason != ErrorReason.None)
                        {
                            Console.WriteLine($"Error parsing packet: {frame.ErrorReason}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error receiving or parsing packet: {ex.Message}");
                }
            }
        }
    }
}
