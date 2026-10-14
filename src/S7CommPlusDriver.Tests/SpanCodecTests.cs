using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    public sealed class SpanCodecTests
    {
        [StructLayout(LayoutKind.Explicit)]
        private struct SingleBits
        {
            [FieldOffset(0)] internal uint Bits;
            [FieldOffset(0)] internal float Value;
        }
        [StructLayout(LayoutKind.Explicit)]
        private struct DoubleBits
        {
            [FieldOffset(0)] internal ulong Bits;
            [FieldOffset(0)] internal double Value;
        }

        [Theory]
        [InlineData("0000000000000000")]
        [InlineData("0102030405060708")]
        [InlineData("8000000000000000")]
        [InlineData("FFFFFFFFFFFFFFFF")]
        public void UInt64AndInt64PreserveBigEndianWireBits(string hex)
        {
            var bytes = Hex(hex);
            ulong expected = 0;
            foreach (var b in bytes) expected = (expected << 8) | b;
            using var stream = new MemoryStream(bytes);
            Assert.Equal(8, S7p.DecodeUInt64(stream, out var unsigned)); Assert.Equal(expected, unsigned);
            stream.Position = 0;
            Assert.Equal(8, S7p.DecodeInt64(stream, out var signed)); Assert.Equal(unchecked((long)expected), signed);
        }

        [Theory]
        [InlineData("00000000")]
        [InlineData("80000000")]
        [InlineData("3F800000")]
        [InlineData("7F800000")]
        [InlineData("FF800000")]
        [InlineData("7FC01234")]
        [InlineData("00000001")]
        public void FloatPreservesZeroInfinityNanAndSubnormalBits(string hex)
        {
            var bytes = Hex(hex);
            using var input = new MemoryStream(bytes);
            Assert.Equal(4, S7p.DecodeFloat(input, out var value));
            var bits = new SingleBits { Value = value }.Bits;
            Assert.Equal(Convert.ToUInt32(hex, 16), bits);
            using var output = new MemoryStream();
            Assert.Equal(4, S7p.EncodeFloat(output, value)); Assert.Equal(bytes, output.ToArray());
        }

        [Theory]
        [InlineData("0000000000000000")]
        [InlineData("8000000000000000")]
        [InlineData("3FF0000000000000")]
        [InlineData("7FF0000000000000")]
        [InlineData("FFF0000000000000")]
        [InlineData("7FF8123456789ABC")]
        [InlineData("0000000000000001")]
        public void DoublePreservesZeroInfinityNanAndSubnormalBits(string hex)
        {
            var bytes = Hex(hex);
            using var input = new MemoryStream(bytes);
            Assert.Equal(8, S7p.DecodeDouble(input, out var value));
            Assert.Equal(Convert.ToUInt64(hex, 16), new DoubleBits { Value = value }.Bits);
            using var output = new MemoryStream();
            Assert.Equal(8, S7p.EncodeDouble(output, value)); Assert.Equal(bytes, output.ToArray());
        }

        [Theory]
        [InlineData(0, "00")]
        [InlineData(63, "3F")]
        [InlineData(64, "8040")]
        [InlineData(-64, "FF40")]
        [InlineData(-255, "FE01")]
        [InlineData(int.MaxValue, "87FFFFFF7F")]
        [InlineData(int.MinValue, "F880808000")]
        public void SignedIntVlqPreservesGoldenWireEncoding(int value, string hex)
        {
            using var stream = new MemoryStream();
            S7p.EncodeInt32Vlq(stream, value); Assert.Equal(Hex(hex), stream.ToArray());
            stream.Position = 0; S7p.DecodeInt32Vlq(stream, out var decoded); Assert.Equal(value, decoded);
        }

        [Theory]
        [InlineData(0UL, "00")]
        [InlineData(127UL, "7F")]
        [InlineData(128UL, "8100")]
        [InlineData(0x00FFFFFFFFFFFFFFUL, "FFFFFFFFFFFFFF7F")]
        [InlineData(0x0100000000000000UL, "80C080808080808000")]
        [InlineData(ulong.MaxValue, "FFFFFFFFFFFFFFFFFF")]
        public void UInt64VlqPreservesTheNinthByteBoundary(ulong value, string hex)
        {
            using var stream = new MemoryStream();
            S7p.EncodeUInt64Vlq(stream, value); Assert.Equal(Hex(hex), stream.ToArray());
            stream.Position = 0; S7p.DecodeUInt64Vlq(stream, out var decoded); Assert.Equal(value, decoded);
        }

        [Theory]
        [InlineData(0L)]
        [InlineData(63L)]
        [InlineData(-64L)]
        [InlineData(0x007FFFFFFFFFFFFFL)]
        [InlineData(0x0080000000000000L)]
        [InlineData(-0x0080000000000000L)]
        [InlineData(long.MinValue)]
        [InlineData(long.MaxValue)]
        public void SignedLongVlqPreservesExtremeValues(long value)
        {
            using var stream = new MemoryStream();
            S7p.EncodeInt64Vlq(stream, value);
            stream.Position = 0; S7p.DecodeInt64Vlq(stream, out var decoded); Assert.Equal(value, decoded);
        }

        [Fact]
        public void Utf8DecodeHonorsNonzeroMemoryStreamOriginAndPosition()
        {
            const string text = "Grüße 日本語 😀";
            var utf8 = Encoding.UTF8.GetBytes(text);
            var bytes = new byte[utf8.Length + 10]; Array.Copy(utf8, 0, bytes, 5, utf8.Length);
            using var stream = new MemoryStream(bytes, 3, utf8.Length + 4, false, true);
            stream.Position = 2;
            Assert.Equal(utf8.Length, S7p.DecodeWString(stream, utf8.Length, out var decoded));
            Assert.Equal(text, decoded); Assert.Equal(2 + utf8.Length, stream.Position);
        }

        [Fact]
        public void FixedWidthSpanViewsHonorTheMemoryStreamOriginAndActiveRange()
        {
            var data = Hex("FFEE0102030405060708AABB");
            using var stream = new MemoryStream(data, 1, 10, false, true);
            stream.Position = 1;
            Assert.Equal(8, S7p.DecodeUInt64(stream, out var value));
            Assert.Equal(0x0102030405060708UL, value); Assert.Equal(9, stream.Position);
            Assert.Equal(0, S7p.DecodeUInt64(stream, out _)); Assert.Equal(9, stream.Position);
            stream.Position = 1;
            Assert.Equal(4, S7p.DecodeFloat(stream, out var single));
            Assert.Equal(0x01020304u, new SingleBits { Value = single }.Bits); Assert.Equal(5, stream.Position);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(10)]
        [InlineData(5000)]
        public void Utf8EncodingMatchesStandardEncodingForStackAndPoolPaths(int length)
        {
            var text = new string('ü', length) + "😀";
            using var stream = new MemoryStream();
            Assert.Equal(Encoding.UTF8.GetByteCount(text), S7p.EncodeWString(stream, text));
            Assert.Equal(Encoding.UTF8.GetBytes(text), stream.ToArray());
        }

        [Fact]
        public void FixedWidthTruncationCannotProduceNumericValues()
        {
            using var stream = new MemoryStream(new byte[3]);
            Assert.Equal(0, S7p.DecodeFloat(stream, out var single)); Assert.Equal(0, single);
            Assert.Equal(0, S7p.DecodeDouble(stream, out var wide)); Assert.Equal(0, wide);
            Assert.Equal(0, S7p.DecodeUInt64(stream, out _)); Assert.Equal(0, S7p.DecodeInt64(stream, out _));
            Assert.Equal(0, stream.Position);
        }

#if NET6_0_OR_GREATER
        [Fact]
        public void NumericCodecsDoNotAllocateTemporaryArrays()
        {
            using var stream = new MemoryStream(128);
            void Run()
            {
                stream.SetLength(0);
                S7p.EncodeFloat(stream, 1.25f); S7p.EncodeDouble(stream, -2.5);
                S7p.EncodeUInt64(stream, ulong.MaxValue); S7p.EncodeInt64(stream, long.MinValue);
                S7p.EncodeInt32Vlq(stream, int.MinValue); S7p.EncodeUInt64Vlq(stream, ulong.MaxValue);
                S7p.EncodeInt64Vlq(stream, long.MinValue);
                stream.Position = 0;
                S7p.DecodeFloat(stream, out _); S7p.DecodeDouble(stream, out _);
                S7p.DecodeUInt64(stream, out _); S7p.DecodeInt64(stream, out _);
            }
            Run();
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) Run();
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
#endif

        private static byte[] Hex(string value) => Enumerable.Range(0, value.Length / 2)
            .Select(i => Convert.ToByte(value.Substring(i * 2, 2), 16)).ToArray();
    }
}
