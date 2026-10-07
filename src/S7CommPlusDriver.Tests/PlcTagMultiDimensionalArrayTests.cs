using System;
using System.Linq;
using S7CommPlusDriver.ClientApi;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    /// <summary>
    /// Verifies that a multidimensional primitive PLC array (e.g. <c>Array[1..2, 1..3] of Int</c>) is reshaped into a genuine
    /// multidimensional CLR array on read, and correctly distributed back into its individual element tags on write, instead of
    /// collapsing into (or requiring) a flat one-dimensional array.
    /// </summary>
    /// <remarks>
    /// Regression test for a design gap ported from the upstream fork's <c>e91d8f2</c> ("Extended DataTypes") commit, which
    /// introduced dedicated <c>PlcTagXxxArrayMDim</c> types there. This driver instead extends the existing
    /// aggregate-element mechanism (<see cref="PlcTag.SetAggregateElements"/>, <see cref="PlcTag.CompleteAggregateRead"/>,
    /// <see cref="PlcTag.SetValue"/>) to reshape/distribute values without introducing new tag types.
    /// </remarks>
    public sealed class PlcTagMultiDimensionalArrayTests
    {
        private static VarInfo CreateTwoByThreeIntArrayVarInfo()
        {
            return new VarInfo
            {
                Name = "DB.Matrix",
                AccessSequence = "8A0E0001.F",
                SymbolCrc = 0x11223344,
                Softdatatype = Softdatatype.S7COMMP_SOFTDATATYPE_INT,
                ArrayElementCount = 6,
                ArrayDimensions = new[]
                {
                    new S7CommPlusArrayDimension(1, 2),
                    new S7CommPlusArrayDimension(1, 3),
                },
            };
        }

        [Fact]
        public void CreateResolvedPlcTagReshapesMultiDimensionalReadIntoGenuineMultiDimensionalArray()
        {
            var tag = S7CommPlusProtocolSession.CreateResolvedPlcTag(CreateTwoByThreeIntArrayVarInfo());

            Assert.Equal(6, tag.AggregateElements.Count);
            for (var index = 0; index < tag.AggregateElements.Count; index++)
            {
                tag.AggregateElements[index].ProcessReadResult(new ValueInt((short)(index + 1)), 0);
            }
            tag.CompleteAggregateRead(0);

            var value = Assert.IsType<short[,]>(tag.GetValue());
            Assert.Equal(2, value.GetLength(0));
            Assert.Equal(3, value.GetLength(1));
            // Row-major: outermost (first declared) dimension varies slowest.
            Assert.Equal(1, value[0, 0]);
            Assert.Equal(2, value[0, 1]);
            Assert.Equal(3, value[0, 2]);
            Assert.Equal(4, value[1, 0]);
            Assert.Equal(5, value[1, 1]);
            Assert.Equal(6, value[1, 2]);
        }

        [Fact]
        public void CreateResolvedPlcTagDistributesMultiDimensionalWriteIntoElementTags()
        {
            var tag = S7CommPlusProtocolSession.CreateResolvedPlcTag(CreateTwoByThreeIntArrayVarInfo());

            var writeValue = new short[2, 3] { { 10, 20, 30 }, { 40, 50, 60 } };
            tag.SetValue(writeValue);

            var elementValues = tag.AggregateElements
                .Select(element => ((PlcTagInt)element).Value)
                .ToArray();
            Assert.Equal(new short[] { 10, 20, 30, 40, 50, 60 }, elementValues);
        }

        [Fact]
        public void CreateResolvedPlcTagKeepsFlatArrayForOneDimensionalAggregate()
        {
            var varInfo = new VarInfo
            {
                Name = "DB.Values",
                AccessSequence = "8A0E0001.F",
                SymbolCrc = 0x55667788,
                Softdatatype = Softdatatype.S7COMMP_SOFTDATATYPE_INT,
                ArrayElementCount = 3,
                ArrayDimensions = new[] { new S7CommPlusArrayDimension(0, 3) },
            };

            var tag = S7CommPlusProtocolSession.CreateResolvedPlcTag(varInfo);
            for (var index = 0; index < tag.AggregateElements.Count; index++)
            {
                tag.AggregateElements[index].ProcessReadResult(new ValueInt((short)(index + 1)), 0);
            }
            tag.CompleteAggregateRead(0);

            var value = Assert.IsType<short[]>(tag.GetValue());
            Assert.Equal(new short[] { 1, 2, 3 }, value);
        }

        [Fact]
        public void CreateResolvedPlcTagReshapesMultiDimensionalBoolReadIntoGenuineMultiDimensionalArray()
        {
            var tag = S7CommPlusProtocolSession.CreateResolvedPlcTag(CreateTwoByTwoVarInfo(Softdatatype.S7COMMP_SOFTDATATYPE_BOOL, "DB.Flags", 0x22334455));
            var expected = new[] { true, false, false, true };
            for (var index = 0; index < tag.AggregateElements.Count; index++)
            {
                tag.AggregateElements[index].ProcessReadResult(new ValueBool(expected[index]), 0);
            }
            tag.CompleteAggregateRead(0);

            var value = Assert.IsType<bool[,]>(tag.GetValue());
            Assert.Equal(2, value.GetLength(0));
            Assert.Equal(2, value.GetLength(1));
            Assert.True(value[0, 0]);
            Assert.False(value[0, 1]);
            Assert.False(value[1, 0]);
            Assert.True(value[1, 1]);
        }

        [Fact]
        public void CreateResolvedPlcTagDistributesMultiDimensionalBoolWriteIntoElementTags()
        {
            var tag = S7CommPlusProtocolSession.CreateResolvedPlcTag(CreateTwoByTwoVarInfo(Softdatatype.S7COMMP_SOFTDATATYPE_BOOL, "DB.Flags", 0x22334455));
            tag.SetValue(new[,] { { true, false }, { false, true } });

            var elementValues = tag.AggregateElements
                .Select(element => ((PlcTagBool)element).Value)
                .ToArray();
            Assert.Equal(new[] { true, false, false, true }, elementValues);
        }

        [Fact]
        public void CreateResolvedPlcTagReshapesMultiDimensionalRealReadIntoGenuineMultiDimensionalArray()
        {
            var tag = S7CommPlusProtocolSession.CreateResolvedPlcTag(CreateTwoByTwoVarInfo(Softdatatype.S7COMMP_SOFTDATATYPE_REAL, "DB.Measurements", 0x33445566));
            var expected = new[] { 1.5f, 2.5f, 3.5f, 4.5f };
            for (var index = 0; index < tag.AggregateElements.Count; index++)
            {
                tag.AggregateElements[index].ProcessReadResult(new ValueReal(expected[index]), 0);
            }
            tag.CompleteAggregateRead(0);

            var value = Assert.IsType<float[,]>(tag.GetValue());
            Assert.Equal(2, value.GetLength(0));
            Assert.Equal(2, value.GetLength(1));
            Assert.Equal(1.5f, value[0, 0]);
            Assert.Equal(2.5f, value[0, 1]);
            Assert.Equal(3.5f, value[1, 0]);
            Assert.Equal(4.5f, value[1, 1]);
        }

        [Fact]
        public void CreateResolvedPlcTagDistributesMultiDimensionalRealWriteIntoElementTags()
        {
            var tag = S7CommPlusProtocolSession.CreateResolvedPlcTag(CreateTwoByTwoVarInfo(Softdatatype.S7COMMP_SOFTDATATYPE_REAL, "DB.Measurements", 0x33445566));
            tag.SetValue(new[,] { { 1.5f, 2.5f }, { 3.5f, 4.5f } });

            var elementValues = tag.AggregateElements
                .Select(element => ((PlcTagReal)element).Value)
                .ToArray();
            Assert.Equal(new[] { 1.5f, 2.5f, 3.5f, 4.5f }, elementValues);
        }

        [Fact]
        public void CreateResolvedPlcTagReshapesMultiDimensionalStringReadIntoGenuineMultiDimensionalArray()
        {
            var varInfo = CreateTwoByTwoVarInfo(Softdatatype.S7COMMP_SOFTDATATYPE_STRING, "DB.Texts", 0x44556677);
            varInfo.MaxStringLength = 10;
            var tag = S7CommPlusProtocolSession.CreateResolvedPlcTag(varInfo);
            var expected = new[] { "aa", "bb", "cc", "dd" };
            for (var index = 0; index < tag.AggregateElements.Count; index++)
            {
                tag.AggregateElements[index].ProcessReadResult(CreateStringReadValue(expected[index], 10), 0);
            }
            tag.CompleteAggregateRead(0);

            var value = Assert.IsType<string[,]>(tag.GetValue());
            Assert.Equal(2, value.GetLength(0));
            Assert.Equal(2, value.GetLength(1));
            Assert.Equal("aa", value[0, 0]);
            Assert.Equal("bb", value[0, 1]);
            Assert.Equal("cc", value[1, 0]);
            Assert.Equal("dd", value[1, 1]);
        }

        [Fact]
        public void CreateResolvedPlcTagDistributesMultiDimensionalStringWriteIntoElementTags()
        {
            var varInfo = CreateTwoByTwoVarInfo(Softdatatype.S7COMMP_SOFTDATATYPE_STRING, "DB.Texts", 0x44556677);
            varInfo.MaxStringLength = 10;
            var tag = S7CommPlusProtocolSession.CreateResolvedPlcTag(varInfo);
            tag.SetValue(new[,] { { "aa", "bb" }, { "cc", "dd" } });

            var elementValues = tag.AggregateElements
                .Select(element => ((PlcTagString)element).Value)
                .ToArray();
            Assert.Equal(new[] { "aa", "bb", "cc", "dd" }, elementValues);
        }

        [Fact]
        public void CreateResolvedPlcTagReshapesMultiDimensionalDtlStructReadIntoGenuineMultiDimensionalArray()
        {
            var tag = S7CommPlusProtocolSession.CreateResolvedPlcTag(CreateTwoByTwoVarInfo(Softdatatype.S7COMMP_SOFTDATATYPE_DTL, "DB.Timestamps", 0x66778899));
            var expected = new[]
            {
                new DateTime(2020, 1, 1),
                new DateTime(2020, 1, 2),
                new DateTime(2020, 1, 3),
                new DateTime(2020, 1, 4),
            };
            for (var index = 0; index < tag.AggregateElements.Count; index++)
            {
                tag.AggregateElements[index].ProcessReadResult(CreateDtlReadValue(expected[index]), 0);
            }
            tag.CompleteAggregateRead(0);

            var value = Assert.IsType<DateTime[,]>(tag.GetValue());
            Assert.Equal(2, value.GetLength(0));
            Assert.Equal(2, value.GetLength(1));
            Assert.Equal(expected[0], value[0, 0]);
            Assert.Equal(expected[1], value[0, 1]);
            Assert.Equal(expected[2], value[1, 0]);
            Assert.Equal(expected[3], value[1, 1]);
        }

        private static VarInfo CreateTwoByTwoVarInfo(uint softdatatype, string name, uint symbolCrc)
        {
            return new VarInfo
            {
                Name = name,
                AccessSequence = "8A0E0001.F",
                SymbolCrc = symbolCrc,
                Softdatatype = softdatatype,
                ArrayElementCount = 4,
                ArrayDimensions = new[]
                {
                    new S7CommPlusArrayDimension(0, 2),
                    new S7CommPlusArrayDimension(0, 2),
                },
            };
        }

        private static ValueUSIntArray CreateStringReadValue(string text, byte maxLength)
        {
            var bytes = System.Text.Encoding.GetEncoding("ISO-8859-1").GetBytes(text);
            var b = new byte[maxLength + 2];
            b[0] = maxLength;
            b[1] = (byte)bytes.Length;
            Array.Copy(bytes, 0, b, 2, bytes.Length);
            return new ValueUSIntArray(b);
        }

        private static ValueStruct CreateDtlReadValue(DateTime value)
        {
            var structValue = new ValueStruct(0x02000043);
            var barr = new byte[12];
            barr[0] = (byte)(value.Year >> 8);
            barr[1] = (byte)value.Year;
            barr[2] = (byte)value.Month;
            barr[3] = (byte)value.Day;
            barr[5] = (byte)value.Hour;
            barr[6] = (byte)value.Minute;
            barr[7] = (byte)value.Second;
            structValue.AddStructElement(0x02000043, new ValueByteArray(barr));
            return structValue;
        }
    }
}
