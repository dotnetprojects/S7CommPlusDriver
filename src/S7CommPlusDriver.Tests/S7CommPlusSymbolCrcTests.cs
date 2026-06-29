using System.Collections.Generic;
using S7CommPlusDriver.Internal;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    public sealed class S7CommPlusSymbolCrcTests
    {
        [Fact]
        public void ComputesKnownScalarCrcs()
        {
            Assert.Equal(0x4CA70840u, S7CommPlusSymbolCrc.ComputeItemCrc("Test2", Softdatatype.S7COMMP_SOFTDATATYPE_DINT));
            Assert.Equal(0xFC8BA389u, S7CommPlusSymbolCrc.ComputeItemCrc("flag", Softdatatype.S7COMMP_SOFTDATATYPE_BOOL));
        }

        [Fact]
        public void ComputesKnownArrayCrcs()
        {
            Assert.Equal(0x0CC865FBu, S7CommPlusSymbolCrc.ComputeArrayItemCrc("TEST 3", Softdatatype.S7COMMP_SOFTDATATYPE_BOOL, 0));
            Assert.Equal(0x6580D02Cu, S7CommPlusSymbolCrc.ComputeArrayItemCrc("readings", Softdatatype.S7COMMP_SOFTDATATYPE_REAL, 0));
            Assert.Equal(0x6E16F413u, S7CommPlusSymbolCrc.ComputeArrayItemCrc("sintItems", Softdatatype.S7COMMP_SOFTDATATYPE_SINT, 1));
            Assert.Equal(0x007CD376u, S7CommPlusSymbolCrc.ComputeArrayItemCrc("realItems", Softdatatype.S7COMMP_SOFTDATATYPE_REAL, 1));
        }

        [Fact]
        public void ComputesNestedStructCrc()
        {
            var path = new List<S7CommPlusSymbolCrc.PathSegment>
            {
                S7CommPlusSymbolCrc.PathSegment.Member("TEST 4", Softdatatype.S7COMMP_SOFTDATATYPE_STRUCT),
                S7CommPlusSymbolCrc.PathSegment.Member("T4ChildBool", Softdatatype.S7COMMP_SOFTDATATYPE_BOOL)
            };

            Assert.Equal(0x1AC12998u, S7CommPlusSymbolCrc.ComputeFromSegments(path));
        }

        [Fact]
        public void BBoolUsesBoolTypeCode()
        {
            Assert.Equal(
                S7CommPlusSymbolCrc.ComputeItemCrc("packedFlag", Softdatatype.S7COMMP_SOFTDATATYPE_BOOL),
                S7CommPlusSymbolCrc.ComputeItemCrc("packedFlag", Softdatatype.S7COMMP_SOFTDATATYPE_BBOOL));
        }

        [Fact]
        public void ComputesNestedArrayMemberCrc()
        {
            var path = new List<S7CommPlusSymbolCrc.PathSegment>
            {
                S7CommPlusSymbolCrc.PathSegment.Array("items", Softdatatype.S7COMMP_SOFTDATATYPE_STRUCT, 1),
                S7CommPlusSymbolCrc.PathSegment.Member("value", Softdatatype.S7COMMP_SOFTDATATYPE_REAL)
            };

            var repeated = S7CommPlusSymbolCrc.ComputeFromSegments(path);

            Assert.NotEqual(0u, repeated);
            Assert.Equal(repeated, S7CommPlusSymbolCrc.ComputeFromSegments(path));
        }
    }
}
