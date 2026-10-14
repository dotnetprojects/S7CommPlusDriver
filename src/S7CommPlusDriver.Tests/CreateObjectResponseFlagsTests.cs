using System;
using System.IO;
using System.Linq;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    public sealed class CreateObjectResponseFlagsTests
    {
        [Fact]
        public void CapturedSubscriptionResponseDoesNotParseIntegrityAsVartypeList()
        {
            // Real response after a long bulk-read run: trailing integrity ID starts 0xAB.
            using var stream = new MemoryStream(Hex("0232000004CA000015E93400018780809F72AB6A00000000"));
            var response = CreateObjectResponse.DeserializeFromPdu(stream)!;
            Assert.NotNull(response); Assert.Equal(0UL, response.ReturnValue);
            Assert.Single(response.ObjectIds); Assert.Null(response.ResponseObject);
            Assert.Equal(18, stream.Position);
        }

        [Theory]
        [InlineData(0x1080u)]
        [InlineData(0x1100u)]
        [InlineData(0x1180u)]
        [InlineData(0x15eau)]
        [InlineData(0x1600u)]
        public void AllObjectLikeCounterPrefixesAreIgnoredWhenObjectFlagIsAbsent(uint integrity)
        {
            using var stream = Header(0x34);
            S7p.EncodeUInt32Vlq(stream, integrity); S7p.EncodeUInt32(stream, 0);
            stream.Position = 0;
            var response = CreateObjectResponse.DeserializeFromPdu(stream)!;
            Assert.Null(response.ResponseObject); Assert.Equal(new uint[] { 42 }, response.ObjectIds);
        }

        [Fact]
        public void DeclaredResponseObjectIsStillDecoded()
        {
            using var stream = Header(0x36);
            S7p.EncodeByte(stream, ElementID.StartOfObject); S7p.EncodeUInt32(stream, 123);
            S7p.EncodeUInt32Vlq(stream, 456); S7p.EncodeUInt32Vlq(stream, 0); S7p.EncodeUInt32Vlq(stream, 0);
            S7p.EncodeByte(stream, ElementID.TerminatingObject);
            S7p.EncodeByte(stream, 0); // Terminates the enclosing object decoder.
            stream.Position = 0;
            var response = CreateObjectResponse.DeserializeFromPdu(stream)!;
            Assert.NotNull(response.ResponseObject); Assert.Equal(stream.Length, stream.Position);
        }

        private static MemoryStream Header(byte flags)
        {
            var stream = new MemoryStream();
            S7p.EncodeByte(stream, ProtocolVersion.V2); S7p.EncodeByte(stream, Opcode.Response);
            S7p.EncodeUInt16(stream, 0); S7p.EncodeUInt16(stream, Functioncode.CreateObject); S7p.EncodeUInt16(stream, 0);
            S7p.EncodeUInt16(stream, 1); S7p.EncodeByte(stream, flags); S7p.EncodeUInt64Vlq(stream, 0);
            S7p.EncodeByte(stream, 1); S7p.EncodeUInt32Vlq(stream, 42);
            return stream;
        }
        private static byte[] Hex(string value) => Enumerable.Range(0, value.Length / 2)
            .Select(i => Convert.ToByte(value.Substring(i * 2, 2), 16)).ToArray();
    }
}
