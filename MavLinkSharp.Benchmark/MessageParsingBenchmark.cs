using BenchmarkDotNet.Attributes;
using System.Collections.Generic;
using System.Linq;

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
            
            var frame = new Frame
            {
                StartMarker = Protocol.V2.StartMarker,
                SystemId = 1,
                ComponentId = 1,
                MessageId = _messageId,
                Message = messageInfo,
                PacketSequence = 1
            };

            var values = new Dictionary<string, object>
            {
                { "custom_mode", (uint)0 },
                { "type", (byte)6 },         
                { "autopilot", (byte)8 },    
                { "base_mode", (byte)0 },
                { "system_status", (byte)4 }, 
                { "mavlink_version", (byte)3 }
            };
            frame.SetFields(values);

            _heartbeatPacket = frame.ToBytes();
        }

        [Benchmark]
        public bool TryParse()
        {
            return _frame.TryParse(_heartbeatPacket);
        }

        [Benchmark]
        public byte AccessFieldDictionary()
        {
            _frame.TryParse(_heartbeatPacket);
            return (byte)_frame.Fields["mavlink_version"];
        }

        [Benchmark]
        public byte AccessFieldTyped()
        {
            _frame.TryParse(_heartbeatPacket);
            return _frame.GetByte("mavlink_version");
        }
    }
}
