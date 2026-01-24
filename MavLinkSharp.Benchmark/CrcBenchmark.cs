using BenchmarkDotNet.Attributes;

namespace MavLinkSharp.Benchmark
{
    [MemoryDiagnoser]
    public class CrcBenchmark
    {
        private readonly byte[] _payload = [.. Enumerable.Range(0, 255).Select(i => (byte)i)];

        [Benchmark]
        public UInt16 CrcCalculate()
        {
            return Crc.Calculate(_payload, false);
        }

        [Benchmark]
        public UInt16 CrcCalculateSpan()
        {
            return Crc.Calculate(_payload);
        }
    }
}
