using MavLinkSharp;
using MavLinkSharp.Enums;
using System.Net;
using System.Net.Sockets;

namespace MavLinkConsole;

static class Receiver
{
    public static void Run(UdpClient udpClient)
    {
        var remoteEndPoint = new IPEndPoint(IPAddress.Any, 0); // Listener does not specify target IP

        while (true)
        {
            try
            {
                // UdpClient.Receive is synchronous and blocking.
                // In a real application, consider an async version or a separate thread.
                byte[] receivedBytes = udpClient.Receive(ref remoteEndPoint);
                
                if (Message.TryParse(receivedBytes, out var frame))
                {
                    Console.ForegroundColor = ConsoleColor.Green; // Highlight Rx messages
                    Console.WriteLine($"Rx => " +
                        $"Seq: {frame.PacketSequence:D3}, " +
                        $"SysId: {frame.SystemId:X2}, " +
                        $"CompId: {frame.ComponentId:X2}, " +
                        $"Id: {frame.MessageId:X4}, " +
                        $"Name: {Metadata.Messages[frame.MessageId].Name}");
                    Console.ResetColor();
                }
                else 
                {
                    if (frame.ErrorReason != ErrorReason.None)
                    {
                        Console.ForegroundColor = ConsoleColor.Red; // Highlight errors
                        Console.WriteLine($"Rx: Error parsing packet: {frame.ErrorReason}");
                        Console.ResetColor();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red; // Highlight exceptions
                Console.WriteLine($"Rx: Error receiving or parsing packet: {ex.Message}");
                Console.ResetColor();
            }
        }
    }
}