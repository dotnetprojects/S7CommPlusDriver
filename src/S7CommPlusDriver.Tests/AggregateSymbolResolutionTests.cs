using S7CommPlusDriver.Internal;
using S7CommPlusDriver.ClientApi;
using System.Linq;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    public sealed class AggregateSymbolResolutionTests
    {
        [Fact]
        public void MultidimensionalArrayWithoutIndexIsResolvedAsAggregate()
        {
            var variable = CreateVariable(new POffsetInfoType_ArrayMDim());

            var isAggregate = S7CommPlusProtocolSession.IsAggregatePrimitiveArray(variable, string.Empty);

            Assert.True(isAggregate);
        }

        [Fact]
        public void MultidimensionalArrayWithIndexIsResolvedAsElement()
        {
            var variable = CreateVariable(new POffsetInfoType_ArrayMDim());

            var isAggregate = S7CommPlusProtocolSession.IsAggregatePrimitiveArray(variable, "[1,2]");

            Assert.False(isAggregate);
        }

        [Fact]
        public void ScalarWithoutIndexIsNotResolvedAsAggregate()
        {
            var variable = CreateVariable(new POffsetInfoType_Std());

            var isAggregate = S7CommPlusProtocolSession.IsAggregatePrimitiveArray(variable, string.Empty);

            Assert.False(isAggregate);
        }

        [Fact]
        public void PackedBooleanArrayAccessIdsExcludeAlignmentGaps()
        {
            var variable = CreateVariable(new POffsetInfoType_ArrayMDim
            {
                ArrayElementCount = 10,
                MdimArrayElementCount = new uint[] { 5, 2, 0, 0, 0, 0 },
            });
            variable.Softdatatype = Softdatatype.S7COMMP_SOFTDATATYPE_BBOOL;

            var accessIds = S7CommPlusProtocolSession.GetAggregateArrayElementAccessIds(variable);

            Assert.Equal(new uint[] { 0, 1, 2, 3, 4, 8, 9, 10, 11, 12 }, accessIds);
        }

        [Fact]
        public void AggregateElementAddressDoesNotReuseAggregateSymbolCrc()
        {
            var address = S7CommPlusProtocolSession.CreateAggregateArrayElementAddress("8A0E1645.9.12", 3);

            Assert.Equal("8A0E1645.9.12.3", address.GetAccessString());
            Assert.Equal(0U, address.SymbolCrc);
        }

        [Theory]
        [InlineData(Softdatatype.S7COMMP_SOFTDATATYPE_STRING, typeof(PlcTagStringArray), typeof(PlcTagString))]
        [InlineData(Softdatatype.S7COMMP_SOFTDATATYPE_DATEANDTIME, typeof(PlcTagDateAndTimeArray), typeof(PlcTagDateAndTime))]
        [InlineData(Softdatatype.S7COMMP_SOFTDATATYPE_DTL, typeof(PlcTagDTLArray), typeof(PlcTagDTL))]
        public void ComplexArrayBrowseMetadataCreatesTypedParentAndScalarElements(
            uint softdatatype,
            System.Type expectedParentType,
            System.Type expectedElementType)
        {
            var variable = new VarInfo
            {
                Name = "DB.Values",
                AccessSequence = "8A0E0001.F",
                Softdatatype = softdatatype,
                ArrayElementCount = 2,
                ArrayDimensions = new[] { new S7CommPlusArrayDimension(5, 2) },
            };

            var tag = S7CommPlusProtocolSession.CreateResolvedPlcTag(variable);

            Assert.IsType(expectedParentType, tag);
            Assert.Equal(2, tag.AggregateElements.Count);
            Assert.All(tag.AggregateElements, element => Assert.IsType(expectedElementType, element));
            var expectedAddresses = softdatatype == Softdatatype.S7COMMP_SOFTDATATYPE_DTL
                ? new[] { "8A0E0001.F.0.1", "8A0E0001.F.1.1" }
                : new[] { "8A0E0001.F.0", "8A0E0001.F.1" };
            Assert.Equal(expectedAddresses, tag.AggregateElements.Select(element => element.Address.GetAccessString()));
        }

        [Fact]
        public void DtlAggregateElementAddressIncludesPackedRelationSelector()
        {
            var address = S7CommPlusProtocolSession.CreateAggregateArrayElementAddress(
                "8A0E1645.9.12",
                3,
                requiresRelationSelector: true);

            Assert.Equal("8A0E1645.9.12.3.1", address.GetAccessString());
            Assert.Equal(0U, address.SymbolCrc);
        }

        [Fact]
        public void IndexedDtlSymbolResolvesFromAggregateBrowseMetadata()
        {
            var aggregate = new VarInfo
            {
                Name = "DB.Timestamps",
                AccessSequence = "8A0E0001.F",
                Softdatatype = Softdatatype.S7COMMP_SOFTDATATYPE_DTL,
                ArrayElementCount = 2,
                ArrayDimensions = new[] { new S7CommPlusArrayDimension(5, 2) },
            };

            var resolved = S7CommPlusProtocolSession.TryCreateResolvedPrimitiveArrayElement(
                aggregate,
                "DB.Timestamps[6]",
                "6",
                out var tag);

            Assert.True(resolved);
            Assert.IsType<PlcTagDTL>(tag);
            Assert.Equal("8A0E0001.F.1.1", tag.Address.GetAccessString());
            Assert.Equal(0U, tag.Address.SymbolCrc);
        }

        /// <summary>
        /// Creates the minimum member metadata needed to exercise aggregate-array selection independently of a live PLC.
        /// </summary>
        /// <param name="offsetInfo">The scalar or array dimension metadata assigned to the synthetic member.</param>
        /// <returns>A synthetic member with an unsigned-integer PLC datatype.</returns>
        private static PVartypeListElement CreateVariable(POffsetInfoType offsetInfo)
        {
            return new PVartypeListElement
            {
                Softdatatype = Softdatatype.S7COMMP_SOFTDATATYPE_UDINT,
                OffsetInfoType = offsetInfo,
            };
        }
    }
}
