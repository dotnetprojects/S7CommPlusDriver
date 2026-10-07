using System.Linq;
using S7CommPlusDriver.ClientApi;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    /// <summary>
    /// Verifies that a structural PLC member (UDT/<c>STRUCT</c> instance or struct array) resolves to a tag instead of
    /// <see langword="null"/> and that a struct array reports its PLC-declared shape through
    /// <see cref="PlcTag.ArrayDimensions"/> without materializing one tag per element.
    /// </summary>
    /// <remarks>
    /// Regression test for a deviation ported into <c>S7CommPlusProtocolSession.browsePlcTagBySymbol</c>: the legacy
    /// driver returned a tag for every relation-bearing member once the symbol path was exhausted, while the new driver
    /// returned <see langword="null"/> for everything except DTL and <c>PlcTags.TagFactory</c> had no
    /// <c>S7COMMP_SOFTDATATYPE_STRUCT</c> case at all. Struct arrays are resolved through
    /// <see cref="PlcTag.ArrayDimensions"/> because the legacy placeholder tag carried the array declaration as well.
    /// </remarks>
    public sealed class PlcTagStructArrayTests
    {
        [Fact]
        public void CreateResolvedPlcTagReturnsStructTagWithDeclaredArrayShape()
        {
            var varInfo = new VarInfo
            {
                Name = "fb_CaseVariables_DB.l_GML_Struct",
                AccessSequence = "8A0E0001.F",
                SymbolCrc = 0x10203040,
                Softdatatype = Softdatatype.S7COMMP_SOFTDATATYPE_STRUCT,
                ArrayElementCount = 10000,
                ArrayDimensions = new[] { new S7CommPlusArrayDimension(1, 10000) },
            };

            var tag = S7CommPlusProtocolSession.CreateResolvedPlcTag(varInfo);

            var structTag = Assert.IsType<PlcTagStruct>(tag);
            var dimension = Assert.Single(structTag.ArrayDimensions);
            Assert.Equal(1, dimension.LowerBound);
            Assert.Equal(10000u, dimension.ElementCount);
            // Struct elements are structures, not scalar wire values, so no element tag is materialized.
            Assert.Empty(structTag.AggregateElements);
        }

        [Fact]
        public void CreateResolvedPlcTagReturnsStructTagWithAllDeclaredDimensions()
        {
            var varInfo = new VarInfo
            {
                Name = "DB.Matrix",
                AccessSequence = "8A0E0001.F",
                SymbolCrc = 0x20304050,
                Softdatatype = Softdatatype.S7COMMP_SOFTDATATYPE_STRUCT,
                ArrayElementCount = 4,
                ArrayDimensions = new[]
                {
                    new S7CommPlusArrayDimension(1, 2),
                    new S7CommPlusArrayDimension(5, 2),
                },
            };

            var tag = Assert.IsType<PlcTagStruct>(S7CommPlusProtocolSession.CreateResolvedPlcTag(varInfo));

            Assert.Collection(
                tag.ArrayDimensions,
                dimension =>
                {
                    Assert.Equal(1, dimension.LowerBound);
                    Assert.Equal(2u, dimension.ElementCount);
                },
                dimension =>
                {
                    Assert.Equal(5, dimension.LowerBound);
                    Assert.Equal(2u, dimension.ElementCount);
                });
            Assert.Empty(tag.AggregateElements);
        }

        [Fact]
        public void CreateResolvedPlcTagReturnsStructTagWithoutShapeForScalarStructMember()
        {
            var varInfo = new VarInfo
            {
                Name = "DB.Settings",
                AccessSequence = "8A0E0001.F",
                SymbolCrc = 0x30405060,
                Softdatatype = Softdatatype.S7COMMP_SOFTDATATYPE_STRUCT,
            };

            var tag = Assert.IsType<PlcTagStruct>(S7CommPlusProtocolSession.CreateResolvedPlcTag(varInfo));

            Assert.Empty(tag.ArrayDimensions);
            Assert.Empty(tag.AggregateElements);
        }

        [Fact]
        public void CreateResolvedPlcTagKeepsElementExpansionAndShapeForPrimitiveArray()
        {
            var varInfo = new VarInfo
            {
                Name = "DB.Measurements",
                AccessSequence = "8A0E0001.F",
                SymbolCrc = 0x40506070,
                Softdatatype = Softdatatype.S7COMMP_SOFTDATATYPE_REAL,
                ArrayElementCount = 6,
                ArrayDimensions = new[]
                {
                    new S7CommPlusArrayDimension(1, 2),
                    new S7CommPlusArrayDimension(1, 3),
                },
            };

            var tag = S7CommPlusProtocolSession.CreateResolvedPlcTag(varInfo);

            Assert.NotNull(tag);
            Assert.Equal(6, tag.AggregateElements.Count);
            Assert.Equal(new[] { 2u, 3u }, tag.ArrayDimensions.Select(dimension => dimension.ElementCount).ToArray());
            Assert.Equal(new[] { 1, 1 }, tag.ArrayDimensions.Select(dimension => dimension.LowerBound).ToArray());
        }

        [Fact]
        public void PlcTagStructProcessReadResultPublishesPackedStructIdentifier()
        {
            var tag = new PlcTagStruct("DB.Settings", new ItemAddress("8A0E0001.F.5"), Softdatatype.S7COMMP_SOFTDATATYPE_STRUCT);

            tag.ProcessReadResult(new ValueStruct(0x0200005A), 0);

            Assert.Equal(0x0200005Au, tag.Value);
            Assert.Equal(PlcTagQC.TAG_QUALITY_GOOD, tag.Quality);
            Assert.Equal(0UL, tag.LastReadError);
        }

        [Fact]
        public void PlcTagStructProcessReadResultFlagsItemErrors()
        {
            var tag = new PlcTagStruct("DB.Settings", new ItemAddress("8A0E0001.F.5"), Softdatatype.S7COMMP_SOFTDATATYPE_STRUCT);

            tag.ProcessReadResult(new ValueStruct(0x0200005A), 1);

            Assert.Equal(PlcTagQC.TAG_QUALITY_BAD, tag.Quality);
            Assert.Equal(1UL, tag.LastReadError);
        }

        [Fact]
        public void PlcTagStructProcessReadResultFlagsUnexpectedValueType()
        {
            var tag = new PlcTagStruct("DB.Settings", new ItemAddress("8A0E0001.F.5"), Softdatatype.S7COMMP_SOFTDATATYPE_STRUCT);

            tag.ProcessReadResult(new ValueReal(1.5f), 0);

            Assert.Equal(PlcTagQC.TAG_QUALITY_BAD, tag.Quality);
        }

        [Fact]
        public void PlcTagStructGetWriteValueWritesPackedStructIdentifier()
        {
            var tag = new PlcTagStruct("DB.Settings", new ItemAddress("8A0E0001.F.5"), Softdatatype.S7COMMP_SOFTDATATYPE_STRUCT)
            {
                Value = 0x0200005A,
            };

            var writeValue = Assert.IsType<ValueStruct>(tag.GetWriteValue());

            Assert.Equal(0x0200005Au, writeValue.GetValue());
        }
    }
}