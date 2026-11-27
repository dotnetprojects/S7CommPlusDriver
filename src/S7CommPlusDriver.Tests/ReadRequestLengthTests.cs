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

        [Fact]
        public void PlaintextEncodingLeavesTheAddressCrcAvailableForLaterRequests()
        {
            var address = new ItemAddress("8A0E0001.2") { SymbolCrc = uint.MaxValue };
            var plaintextRead = S7CommPlusProtocolSession.CreateReadRequestBatch(
                new[] { address }, 0, 1, out _, usePlaintextFraming: true);
            var plaintextWrite = new SetMultiVariablesRequest(ProtocolVersion.V2)
            {
                OmitSymbolCrc = true,
                WithIntegrityId = false
            };
            plaintextWrite.AddressListVar.Add(address);
            plaintextWrite.ValueList.Add(new ValueBool(true));

            AssertLength(plaintextRead);
            Assert.Equal(0u, ReadFirstAddressCrc(plaintextRead));
            Assert.Equal(0u, ReadFirstAddressCrc(plaintextWrite));
            Assert.Equal(uint.MaxValue, address.SymbolCrc);

            var secureRead = S7CommPlusProtocolSession.CreateReadRequestBatch(new[] { address }, 0, 1, out _);
            Assert.Equal(uint.MaxValue, ReadFirstAddressCrc(secureRead));
            plaintextWrite.OmitSymbolCrc = false;
            plaintextWrite.WithIntegrityId = true;
            Assert.Equal(uint.MaxValue, ReadFirstAddressCrc(plaintextWrite));
        }

        private static uint ReadFirstAddressCrc(IS7pRequest request)
        {
            using var stream = new MemoryStream();
            request.Serialize(stream);
            stream.Position = 18; // Request header plus link/object ID
            S7p.DecodeUInt32Vlq(stream, out _); // Item count
            S7p.DecodeUInt32Vlq(stream, out _); // Field count
            S7p.DecodeUInt32Vlq(stream, out var crc);
            return crc;
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
