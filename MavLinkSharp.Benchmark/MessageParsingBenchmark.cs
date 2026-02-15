using BenchmarkDotNet.Attributes;

namespace MavLinkSharp.Benchmark
{
    [MemoryDiagnoser]
    public class MessageParsingBenchmark
    {
        private byte[] _heartbeatPacket = null!;
        private uint _messageId = 0; // HEARTBEAT
        private readonly Frame _frame = new Frame();

        [GlobalSetup]
        public void Setup()
        {
            MavLink.Initialize("common.xml");

            // Manually construct a valid HEARTBEAT packet
            var messageInfo = Metadata.Messages[_messageId];
            var payload = new byte[messageInfo.PayloadLength];
            payload[4] = 8; // type = MAV_TYPE_GCS
            payload[8] = 3; // mavlink_version

            var packetBytes = new System.Collections.Generic.List<byte>();

            // Header for MAVLink 2
            packetBytes.Add(Protocol.V2.StartMarker);
            packetBytes.Add((byte)payload.Length);
            packetBytes.Add(0); // Incompatibility Flags
            packetBytes.Add(0); // Compatibility Flags
            packetBytes.Add(0); // Sequence
            packetBytes.Add(1); // SystemId
            packetBytes.Add(1); // ComponentId
            packetBytes.Add((byte)(_messageId & 0xFF));
            packetBytes.Add((byte)((_messageId >> 8) & 0xFF));
            packetBytes.Add((byte)((_messageId >> 16) & 0xFF));
            
            // Payload
            packetBytes.AddRange(payload);

            // Checksum
            var signatureBytes = new List<byte>();
            signatureBytes.AddRange(packetBytes.Skip(1));
            signatureBytes.Add(messageInfo.CrcExtra);
            ushort checksum = Crc.Calculate(signatureBytes.ToArray());
            packetBytes.Add((byte)(checksum & 0xFF));
            packetBytes.Add((byte)((checksum >> 8) & 0xFF));
            
            _heartbeatPacket = packetBytes.ToArray();
        }

        [Benchmark]
        public bool TryParse()
        {
            return _frame.TryParse(_heartbeatPacket);
        }
    }
}
