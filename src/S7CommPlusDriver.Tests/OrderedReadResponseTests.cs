using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    public sealed class OrderedReadResponseTests
    {
        [Fact]
        public void MixedValuesErrorsAndMissingItemsMatchDictionaryDecoder()
        {
            var data = Response(new[]
            {
                (4u, (PValue)new ValueWString("hello")),
                (1u, (PValue)new ValueBool(true)),
                (3u, (PValue)new ValueDInt(-123)),
                (2u, (PValue)new ValueUDInt(uint.MaxValue))
            }, new[] { (3u, 0x8000UL), (5u, 0x9000UL) });
            using var referenceStream = new MemoryStream(data);
            var reference = GetMultiVariablesResponse.DeserializeFromPdu(referenceStream)!;
            using var stream = new MemoryStream(data);
            var response = GetMultiVariablesReadResponse.ReadHeader(stream)!;
            var values = new object[8];
            var errors = Enumerable.Repeat(ulong.MaxValue, 8).ToArray();

            Assert.Equal(0, response.ReadItems(stream, values, errors, 1, 6));
            Assert.Equal(reference.SequenceNumber, response.SequenceNumber);
            Assert.Equal(reference.IntegrityId, response.IntegrityId);
            Assert.Equal(reference.ReturnValue, response.ReturnValue);
            foreach (var item in reference.Values)
            {
                using var expected = new MemoryStream(); item.Value.Serialize(expected);
                using var actual = new MemoryStream(); Assert.IsAssignableFrom<PValue>(values[item.Key]).Serialize(actual);
                Assert.Equal(expected.ToArray(), actual.ToArray());
                Assert.Equal(reference.ErrorValues.TryGetValue(item.Key, out var error) ? error : 0UL, errors[item.Key]);
            }
            Assert.Equal(0x9000UL, errors[5]);
            Assert.Null(values[6]); Assert.Equal(ulong.MaxValue, errors[6]);
            Assert.Null(values[0]); Assert.Null(values[7]);
            Assert.Equal(ulong.MaxValue, errors[0]); Assert.Equal(ulong.MaxValue, errors[7]);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void DuplicateAndOutOfRangeItemsAreRejected(bool errors, bool duplicate)
        {
            var valueItems = errors ? Array.Empty<(uint, PValue)>()
                : duplicate ? new[] { (1u, (PValue)new ValueBool(true)), (1u, (PValue)new ValueBool(false)) }
                : new[] { (2u, (PValue)new ValueBool(true)) };
            var errorItems = !errors ? Array.Empty<(uint, ulong)>()
                : duplicate ? new[] { (1u, ulong.MaxValue), (1u, 1UL) } : new[] { (2u, 1UL) };
            using var stream = new MemoryStream(Response(valueItems, errorItems));
            var response = GetMultiVariablesReadResponse.ReadHeader(stream)!;
            Assert.Equal(S7Consts.errIsoInvalidPDU, response.ReadItems(stream,
                new object[1], new[] { ulong.MaxValue }, 0, 1));
        }

        [Fact]
        public void EveryTruncatedPrefixFailsInsteadOfProducingACompleteRead()
        {
            var data = Response(new[] { (1u, (PValue)new ValueBool(true)) }, Array.Empty<(uint, ulong)>());
            for (var length = 0; length < data.Length; length++)
            {
                using var stream = new MemoryStream(data, 0, length);
                var response = GetMultiVariablesReadResponse.ReadHeader(stream);
                if (response == null) continue;
                Assert.Throws<InvalidDataException>(() => response.ReadItems(stream,
                    new object[1], new[] { ulong.MaxValue }, 0, 1));
            }
        }

        [Fact]
        public void UnexpectedFunctionIsRejectedBeforeDecodingItems()
        {
            var data = Response(Array.Empty<(uint, PValue)>(), Array.Empty<(uint, ulong)>());
            data[5] ^= 1;
            using var stream = new MemoryStream(data);
            Assert.Null(GetMultiVariablesReadResponse.ReadHeader(stream));
        }

        private static byte[] Response(IEnumerable<(uint Index, PValue Value)> values, IEnumerable<(uint Index, ulong Error)> errors)
        {
            using var stream = new MemoryStream();
            S7p.EncodeByte(stream, ProtocolVersion.V2); S7p.EncodeByte(stream, Opcode.Response);
            S7p.EncodeUInt16(stream, 0); S7p.EncodeUInt16(stream, Functioncode.GetMultiVariables);
            S7p.EncodeUInt16(stream, 0); S7p.EncodeUInt16(stream, 0x1234); S7p.EncodeByte(stream, 0x34);
            S7p.EncodeUInt64Vlq(stream, 1);
            foreach (var item in values) { S7p.EncodeUInt32Vlq(stream, item.Index); item.Value.Serialize(stream); }
            S7p.EncodeUInt32Vlq(stream, 0);
            foreach (var item in errors) { S7p.EncodeUInt32Vlq(stream, item.Index); S7p.EncodeUInt64Vlq(stream, item.Error); }
            S7p.EncodeUInt32Vlq(stream, 0); S7p.EncodeUInt32Vlq(stream, 0x89abcdef);
            return stream.ToArray();
        }
    }
}
