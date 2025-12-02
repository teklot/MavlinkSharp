using System;
using System.Collections.Generic;

namespace MavLinkSharp
{
    /// <summary>
    /// Implements the CRC-16/MCRF4XX a.k.a used for MAVLink checksum.
    /// </summary>
    public class Crc
    {
        /// <summary>
        /// The initial seed for the CRC-16 calculation.
        /// </summary>
        public const UInt16 Seed = 0xffff;

        /// <summary>
        /// Accumulates the CRC-16 value for a single byte.
        /// </summary>
        /// <param name="b">The byte to accumulate.</param>
        /// <param name="crc">The current CRC value.</param>
        /// <returns>The new accumulated CRC value.</returns>
        public static UInt16 Accumulate(byte b, UInt16 crc)
        {
            unchecked
            {
                byte ch = (byte)(b ^ (byte)(crc & 0x00ff));
                ch = (byte)(ch ^ (ch << 4));
                return (UInt16)((crc >> 8) ^ (ch << 8) ^ (ch << 3) ^ (ch >> 4));
            }
        }

        /// <summary>
        /// Calculates the CRC-16 value for a sequence of bytes.
        /// </summary>
        /// <param name="bytes">The sequence of bytes to calculate the CRC for.</param>
        /// <returns>The final CRC-16 checksum.</returns>
        public static UInt16 Calculate(IEnumerable<byte> bytes)
        {
            UInt16 crc = Crc.Seed;

            foreach (var b in bytes)
            {
                crc = Crc.Accumulate(b, crc);
            }

            return crc;
        }
    }
}
