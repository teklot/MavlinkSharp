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

                byte[] payload = GeneratePayload(message, random);

                // 1. Calculate lengths (Header is 10 bytes for MAVLink 2)
                int headerLen = Protocol.V2.HeaderLength;
                int payloadLen = payload.Length;
                int totalPacketSize = headerLen + payloadLen + 2; // +2 for final CRC

                // 2. Single allocation for the entire packet
                byte[] packet = new byte[totalPacketSize];
                Span<byte> packetBytes = packet.AsSpan();

                // 3. Construct Header directly into the buffer
                packetBytes[0] = Protocol.V2.StartMarker;                   // 0: Start Marker (0xFD)
                packetBytes[1] = (byte)payloadLen;                          // 1: Payload Length
                packetBytes[2] = 0;                                         // 2: Incompatibility Flags (0 for now)
                packetBytes[3] = 0;                                         // 3: Compatibility Flags (0 for now)
                packetBytes[4] = packetSequence;                            // 4: Packet Sequence
                packetBytes[5] = systemId;                                  // 5: System ID
                packetBytes[6] = componentId;                               // 6: Component ID
                packetBytes[7] = (byte)(randomMessageId & 0xFF);            // 7: Message ID (LSB)
                packetBytes[8] = (byte)((randomMessageId >> 8) & 0xFF);     // 8: Message ID
                packetBytes[9] = (byte)((randomMessageId >> 16) & 0xFF);    // 9: Message ID (MSB)

                // 4. Copy Payload using Slice and CopyTo
                payload.AsSpan().CopyTo(packetBytes[headerLen..]);

                // 5. Construct Checksum Buffer (Header minus StartMarker + Payload + CrcExtra)
                // Total size for checksum = (10 - 1) + payloadLen + 1 = 10 + payloadLen
                var checksumBuffer = new byte[headerLen + payloadLen];
                Span<byte> checkSpan = checksumBuffer.AsSpan();

                // Copy Header (excluding StartMarker) and Payload in one efficient operation
                packetBytes[1..(headerLen + payloadLen)].CopyTo(checkSpan);

                // Append CRC_EXTRA to the last byte of the checksum buffer
                checkSpan[^1] = message.CrcExtra;

                // 6. Calculate CRC
                ushort checksum = Crc.Calculate(checksumBuffer);

                // 7. Write CRC to the end of the packet (LSB first)
                packetBytes[^2] = (byte)(checksum & 0xFF);                  // Checksum LSB
                packetBytes[^1] = (byte)((checksum >> 8) & 0xFF);           // Checksum MSB

                udpClient.Send(packetBytes.ToArray(), packetBytes.Length, remoteEndPoint);

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

    static byte[] GeneratePayload(Message messageDefinition, Random random)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        foreach (var field in messageDefinition.OrderedFields)
        {
            object randomValue = GenerateRandomValue(field, random);
            ConvertValueToBytes(bw, randomValue, field.DataType, field.ElementType, field.ArrayLength);
        }
        return ms.ToArray();
    }

    static void ConvertValueToBytes(BinaryWriter bw, object value, Type dataType, Type elementType, int arrayLength)
    {
        if (dataType.IsArray)
        {
            if (elementType == typeof(char))
            {
                char[] charArray = (char[])value;
                bw.Write(Encoding.ASCII.GetBytes(charArray)); // Assuming ASCII for char arrays/strings
            }
            else
            {
                Array array = (Array)value;
                foreach (var element in array)
                {
                    ConvertValueToBytes(bw, element, elementType, elementType, 0); // Recursive call for array elements
                }
            }
        }
        else
        {
            if (dataType == typeof(byte)) bw.Write((byte)value);
            else if (dataType == typeof(sbyte)) bw.Write((sbyte)value);
            else if (dataType == typeof(ushort)) bw.Write((ushort)value);
            else if (dataType == typeof(short)) bw.Write((short)value);
            else if (dataType == typeof(uint)) bw.Write((uint)value);
            else if (dataType == typeof(int)) bw.Write((int)value);
            else if (dataType == typeof(ulong)) bw.Write((ulong)value);
            else if (dataType == typeof(long)) bw.Write((long)value);
            else if (dataType == typeof(float)) bw.Write((float)value);
            else if (dataType == typeof(double)) bw.Write((double)value);
            else if (dataType == typeof(char)) bw.Write((char)value);
        }
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
