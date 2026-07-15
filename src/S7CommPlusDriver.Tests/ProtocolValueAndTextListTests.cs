using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    public sealed class ProtocolValueAndTextListTests
    {
        [Fact]
        public void AddressArrayOfVariantsRoundTripsCapturedWireShape()
        {
            var bytes = new byte[]
            {
                0xA0, Datatype.Variant, 0x02, 0x01, 0x02
            };

            using var input = new MemoryStream(bytes);
            var value = Assert.IsType<ValueVariantArray>(PValue.Deserialize(input));

            Assert.Equal(new uint[] { 1, 2 }, Array.ConvertAll(value.GetValue(), item => item.GetValue()));
            Assert.Equal(input.Length, input.Position);
            using var output = new MemoryStream();
            value.Serialize(output);
            Assert.Equal(bytes, output.ToArray());
        }

        [Fact]
        public void AddressArrayOfStructsDecodesNestedValuesWithoutRepeatedHeaders()
        {
            var bytes = new byte[]
            {
                0x20, Datatype.Struct, 0x02,
                0x00, 0x00, 0x01, 0x00,
                0x01, 0x00, Datatype.UDInt, 0x07, 0x00,
                0x00, 0x00, 0x01, 0x01,
                0x00
            };

            using var input = new MemoryStream(bytes);
            var value = Assert.IsType<ValueStructArray>(PValue.Deserialize(input));
            var structs = value.GetValue();

            Assert.Equal(2, structs.Length);
            Assert.Equal(0x100u, structs[0].GetValue());
            Assert.Equal(7u, Assert.IsType<ValueUDInt>(structs[0].GetStructElement(1)).GetValue());
            Assert.Equal(0x101u, structs[1].GetValue());
            Assert.Equal(input.Length, input.Position);
            using var output = new MemoryStream();
            value.Serialize(output);
            Assert.Equal(bytes, output.ToArray());
        }

        [Fact]
        public void NullArrayConsumesOnlyItsCount()
        {
            var bytes = new byte[] { 0x10, Datatype.Null, 0x03 };

            using var input = new MemoryStream(bytes);
            var value = Assert.IsType<ValueNullArray>(PValue.Deserialize(input));

            Assert.Equal(3u, value.Count);
            Assert.Equal(input.Length, input.Position);
        }

        [Fact]
        public void AddressArrayOfS7StringDescriptorsConsumesEveryLength()
        {
            var bytes = new byte[] { 0x20, Datatype.S7String, 0x02, 0x0A, 0x14 };

            using var input = new MemoryStream(bytes);
            var value = Assert.IsType<ValueS7StringArray>(PValue.Deserialize(input));

            Assert.Equal(new uint[] { 10, 20 }, Array.ConvertAll(value.GetValue(), item => item.MaximumLength));
            Assert.Equal(input.Length, input.Position);
        }

        [Fact]
        public void TextListEntriesUseSigned32BitValuesAndEightByteRecords()
        {
            var listTable = new byte[26];
            BinaryPrimitives.WriteUInt32LittleEndian(listTable.AsSpan(16), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(listTable.AsSpan(20), 0x1234);
            BinaryPrimitives.WriteUInt32LittleEndian(listTable.AsSpan(22), 16);

            var entryTable = new byte[36];
            BinaryPrimitives.WriteUInt32LittleEndian(entryTable.AsSpan(16), 2);
            BinaryPrimitives.WriteUInt32LittleEndian(entryTable.AsSpan(20), 70_000);
            BinaryPrimitives.WriteUInt32LittleEndian(entryTable.AsSpan(24), 8);
            BinaryPrimitives.WriteUInt32LittleEndian(entryTable.AsSpan(28), UInt32.MaxValue);
            BinaryPrimitives.WriteUInt32LittleEndian(entryTable.AsSpan(32), 15);

            var stringTable = new byte[21];
            WriteText(stringTable, 8, "Alpha");
            WriteText(stringTable, 15, "Last");
            var lists = new List<S7CommPlusTextList>();

            var result = S7CommPlusTextListService.DecodeTextListLibrary(
                listTable,
                entryTable,
                stringTable,
                1031,
                S7CommPlusTextListScope.LanguageSpecific,
                lists);

            Assert.Equal(0, result);
            var list = Assert.Single(lists);
            Assert.Equal(0x1234, list.ListId);
            Assert.Collection(
                list.Entries,
                entry =>
                {
                    Assert.Equal(70_000, entry.From);
                    Assert.Equal("Alpha", entry.Text);
                },
                entry =>
                {
                    Assert.Equal(-1, entry.From);
                    Assert.Equal("Last", entry.Text);
                });
        }

        private static void WriteText(byte[] destination, int offset, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.AsSpan(offset), (ushort)bytes.Length);
            bytes.CopyTo(destination.AsSpan(offset + 2));
        }
    }
}
