using MavLinkSharp;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MavLinkConsole;

static class Transmitter
{
    private static readonly Random random = new();
    private static byte packetSequence = 0;

    public static void Run(UdpClient udpClient, IPEndPoint remoteEndPoint)
    {
        var messageIds = Metadata.Messages.Keys.ToList();
        if (!messageIds.Any())
        {
            TerminalLayout.WriteTx("Tx: No MavLink messages found in common.xml. Exiting Tx thread.");
            return;
        }

        while (true)
        {
            // Select a random message ID
            uint randomMessageId = messageIds[random.Next(messageIds.Count)];

            if (Metadata.Messages.TryGetValue(randomMessageId, out var message))
            {
                byte systemId = (byte)random.Next(1, 255);
                byte componentId = (byte)random.Next(1, 255);

                var fieldValues = GenerateFieldValues(message, random);

                var frame = new Frame
                {
                    StartMarker = Protocol.V2.StartMarker,
                    SystemId = systemId,
                    ComponentId = componentId,
                    MessageId = randomMessageId,
                    Message = message,
                    PacketSequence = packetSequence
                };

                frame.SetFields(fieldValues);

                byte[] packet = frame.ToBytes();

                udpClient.Send(packet, packet.Length, remoteEndPoint);

                TerminalLayout.WriteTx($"Tx => " +
                    $"Seq: {packetSequence:D3}, " +
                    $"SysId: {systemId:X2}, " +
                    $"CompId: {componentId:X2}, " +
                    $"Id: {message.Id:X4}, " +
                    $"Name: {message.Name}");
                
                packetSequence++; // Increment sequence number
            }
            else
            {
                TerminalLayout.WriteTx($"Tx: Could not find message definition for ID: {randomMessageId}");
            }

            Thread.Sleep(100); // Send message every 100ms
        }
    }

    static Dictionary<string, object> GenerateFieldValues(Message messageDefinition, Random random)
    {
        var values = new Dictionary<string, object>();
        foreach (var field in messageDefinition.OrderedFields)
        {
            values[field.Name] = GenerateRandomValue(field, random);
        }
        return values;
    }

    static object GenerateRandomValue(Field field, Random random)
    {
        if (field.DataType.IsArray)
        {
            // Handle char arrays (strings)
            if (field.ElementType == typeof(char))
            {
                char[] charArray = new char[field.ArrayLength];
                for (int i = 0; i < field.ArrayLength; i++)
                {
                    charArray[i] = (char)random.Next(32, 127); // Printable ASCII characters
                }
                return charArray;
            }
            else // Other array types
            {
                Array array = Array.CreateInstance(field.ElementType, field.ArrayLength);
                for (int i = 0; i < field.ArrayLength; i++)
                {
                    array.SetValue(GenerateSingleRandomValue(field.ElementType, random), i);
                }
                return array;
            }
        }
        else
        {
            return GenerateSingleRandomValue(field.DataType, random);
        }
    }

    static object GenerateSingleRandomValue(Type type, Random random)
    {
        if (type == typeof(byte)) return (byte)random.Next(256);
        if (type == typeof(sbyte)) return (sbyte)random.Next(-128, 128);
        if (type == typeof(ushort)) return (ushort)random.Next(65536);
        if (type == typeof(short)) return (short)random.Next(-32768, 32768);
        if (type == typeof(uint)) return (uint)random.Next();
        if (type == typeof(int)) return random.Next();
        if (type == typeof(ulong)) return (ulong)(random.NextDouble() * ulong.MaxValue); // Simplified for now
        if (type == typeof(long)) return (long)(random.NextDouble() * long.MaxValue * (random.Next(2) == 0 ? 1 : -1)); // Simplified
        if (type == typeof(float)) return (float)(random.NextDouble() * 1000.0f); // Example range
        if (type == typeof(double)) return random.NextDouble() * 1000.0; // Example range
        if (type == typeof(char)) return (char)random.Next('a', 'z' + 1);

        // Default for unsupported types, throw an exception
        throw new InvalidOperationException($"Unsupported type for random generation: {type.FullName}");
    }
}
