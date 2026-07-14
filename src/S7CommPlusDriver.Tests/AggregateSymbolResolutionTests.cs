using S7CommPlusDriver.Internal;
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
