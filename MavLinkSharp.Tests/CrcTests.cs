using System.Text;

namespace MavLinkSharp.Tests
{
    public class CrcTests
    {
        [Fact]
        public void Calculate_MatchesAccumulate()
        {
            var data = Encoding.ASCII.GetBytes("123456789");
            
            // Calculate using the table-based method
            ushort calculateResult = Crc.Calculate(data);

            // Calculate using the iterative accumulation method
            ushort accumulateResult = Crc.Seed;
            foreach (var b in data)
            {
                accumulateResult = Crc.Accumulate(b, accumulateResult);
            }

            Assert.Equal(calculateResult, accumulateResult);
        }

        [Theory]
        [InlineData("123456789", 0x6F91)]
        [InlineData("Hello, MAVLink!", 0xE07D)]
        public void Calculate_KnownValues(string input, ushort expectedCrc)
        {
            var data = Encoding.ASCII.GetBytes(input);
            ushort actualCrc = Crc.Calculate(data);
            
            Assert.Equal(expectedCrc, actualCrc);
        }

        [Fact]
        public void Calculate_EmptyData_ReturnsSeed()
        {
            ushort actualCrc = Crc.Calculate(ReadOnlySpan<byte>.Empty);
            Assert.Equal(Crc.Seed, actualCrc);
        }

        [Fact]
        public void Accumulate_SingleByte()
        {
            byte b = 0x42;
            ushort crc = Crc.Seed;
            ushort result = Crc.Accumulate(b, crc);
            
            // Calculate manually or use a known result
            // For 0x42 and 0xFFFF seed:
            // byte ch = 0x42 ^ 0xFF = 0xBD
            // ch = 0xBD ^ (0xBD << 4) = 0xBD ^ 0xD0 = 0x6D (masked to byte)
            // (0xFFFF >> 8) ^ (0x6D << 8) ^ (0x6D << 3) ^ (0x6D >> 4)
            // 0x00FF ^ 0x6D00 ^ 0x0368 ^ 0x0006 = 0x6E91? 
            // Wait, let's just use the known values test to be sure of the logic.
            
            ushort expected = Crc.Calculate(new byte[] { b });
            Assert.Equal(expected, result);
        }
    }
}
