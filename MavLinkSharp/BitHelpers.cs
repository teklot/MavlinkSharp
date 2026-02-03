using System.Runtime.InteropServices;

namespace MavLinkSharp
{
    internal static class BitHelpers
    {
        [StructLayout(LayoutKind.Explicit)]
        private struct Int32SingleUnion
        {
            [FieldOffset(0)] public int Int32;
            [FieldOffset(0)] public float Single;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct Int64DoubleUnion
        {
            [FieldOffset(0)] public long Int64;
            [FieldOffset(0)] public double Double;
        }

        public static float Int32BitsToSingle(int value)
        {
            var union = new Int32SingleUnion { Int32 = value };
            return union.Single;
        }

        public static double Int64BitsToDouble(long value)
        {
            var union = new Int64DoubleUnion { Int64 = value };
            return union.Double;
        }
    }
}
