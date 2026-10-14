using System;
using System.IO;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    public sealed class UInt32VlqEncodingTests
    {
        [Theory]
        [InlineData(0u, new byte[] { 0x00 })]
        [InlineData(127u, new byte[] { 0x7f })]
        [InlineData(128u, new byte[] { 0x81, 0x00 })]
        [InlineData(16383u, new byte[] { 0xff, 0x7f })]
        [InlineData(16384u, new byte[] { 0x81, 0x80, 0x00 })]
        [InlineData(2097151u, new byte[] { 0xff, 0xff, 0x7f })]
        [InlineData(2097152u, new byte[] { 0x81, 0x80, 0x80, 0x00 })]
        [InlineData(268435455u, new byte[] { 0xff, 0xff, 0xff, 0x7f })]
        [InlineData(268435456u, new byte[] { 0x81, 0x80, 0x80, 0x80, 0x00 })]
        [InlineData(uint.MaxValue, new byte[] { 0x8f, 0xff, 0xff, 0xff, 0x7f })]
        public void EncoderPreservesWireBytesAndFollowingValue(uint value, byte[] expected)
        {
            using var stream = new MemoryStream();
            var length = S7p.EncodeUInt32Vlq(stream, value);
            Assert.Equal(expected.Length, length);
            Assert.Equal(expected, stream.ToArray());
            stream.WriteByte(42);
            stream.Position = 0;
            Assert.Equal(length, S7p.DecodeUInt32Vlq(stream, out var decoded));
            Assert.Equal(value, decoded);
            Assert.Equal(42, stream.ReadByte());
        }

#if NET6_0_OR_GREATER
        [Fact]
        public void RepeatedAddressFieldEncodingDoesNotAllocateScratchArrays()
        {
            using var stream = new MemoryStream(5);
            for (var i = 0; i < 100; i++)
            {
                stream.Position = 0;
                S7p.EncodeUInt32Vlq(stream, uint.MaxValue);
            }
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 10000; i++)
            {
                stream.Position = 0;
                S7p.EncodeUInt32Vlq(stream, (uint)i);
            }
            Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        }
#endif
    }
}
