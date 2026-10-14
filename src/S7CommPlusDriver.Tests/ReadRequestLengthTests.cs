using System;
using System.IO;
using S7CommPlusDriver.ClientApi;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    public sealed class ReadRequestLengthTests
    {
        [Theory]
        [InlineData(0u)]
        [InlineData(127u)]
        [InlineData(128u)]
        [InlineData(16383u)]
        [InlineData(16384u)]
        [InlineData(2097152u)]
        [InlineData(268435456u)]
        [InlineData(uint.MaxValue)]
        public void ComputedLengthMatchesWireBytesAcrossVlqBoundariesAndMutations(uint boundary)
        {
            var request = new GetMultiVariablesRequest(ProtocolVersion.V3)
            { IntegrityId = boundary, SessionId = uint.MaxValue, SequenceNumber = ushort.MaxValue, LinkId = 123 };
            var address = new ItemAddress { AccessArea = boundary, AccessSubArea = boundary, SymbolCrc = boundary };
            address.LID.Add(boundary);
            request.AddressList.Add(address);
            AssertLength(request);
            // Exercise the mutable compatibility API rather than relying on cached sizes.
            address.SymbolCrc = boundary ^ uint.MaxValue;
            address.LID.Add(uint.MaxValue);
            AssertLength(request);
            for (var i = 0; i < 128; i++) request.AddressList.Add(address);
            AssertLength(request);
            request.WithIntegrityId = false;
            AssertLength(request);
        }

        [Fact]
        public void LongAddressAndWideFieldCountMatchActualSerialization()
        {
            var request = new GetMultiVariablesRequest(ProtocolVersion.V2) { IntegrityId = uint.MaxValue };
            var address = new ItemAddress { AccessArea = uint.MaxValue };
            for (var i = 0; i < 16384; i++) address.LID.Add((uint)i);
            request.AddressList.Add(address);
            AssertLength(request);
        }

        private static void AssertLength(GetMultiVariablesRequest request)
        {
            using var stream = new MemoryStream();
            request.Serialize(stream);
            Assert.Equal(stream.Length, request.GetSerializedLength());
            var integrity = request.IntegrityId;
            request.IntegrityId = uint.MaxValue;
            using var widest = new MemoryStream();
            request.Serialize(widest);
            request.IntegrityId = integrity;
            Assert.Equal(widest.Length, request.GetSerializedLength(uint.MaxValue));
        }
    }
}
