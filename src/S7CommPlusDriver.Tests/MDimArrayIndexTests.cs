using System;
using System.Reflection;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    public sealed class MDimArrayIndexTests
    {
        [Theory]
        [InlineData(false, "[1].Member", "8A0E0001.F.0.1")]
        [InlineData(false, "[3].Member", "8A0E0001.F.2.1")]
        [InlineData(true, "[1].Member", "8A0E0001.F.0.1")]
        [InlineData(true, "[3].Member", "8A0E0001.F.2.1")]
        public void SingleIndexResolvesRelationArrayElement(bool fbArray, string symbol, string expectedAddress)
        {
            var info = new VarInfo { AccessSequence = "8A0E0001.F" };
            var arguments = new object[] { symbol, CreateVariable(fbArray, false), info };

            ParseIndex(arguments);

            Assert.Equal(expectedAddress, info.AccessSequence);
            Assert.True(info.ContainsIndexedArray);
            Assert.Equal("Member", arguments[0]);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MultipleIndicesPreserveDimensionOrderAndNegativeBounds(bool fbArray)
        {
            var info = new VarInfo { AccessSequence = "8A0E0001.F" };
            // Declaration: Array[-2..-1, 1..3]; the last dimension varies fastest.
            var arguments = new object[] { "[-1,3].Member", CreateVariable(fbArray, true), info };

            ParseIndex(arguments);

            Assert.Equal("8A0E0001.F.5.1", info.AccessSequence);
            Assert.Equal("Member", arguments[0]);
        }

        [Theory]
        [InlineData(false, false, "[0]", typeof(ArgumentOutOfRangeException))]
        [InlineData(true, false, "[4]", typeof(ArgumentOutOfRangeException))]
        [InlineData(false, false, "[1,2]", typeof(ArgumentException))]
        [InlineData(true, false, "[1,2]", typeof(ArgumentException))]
        [InlineData(false, true, "[1]", typeof(ArgumentException))]
        [InlineData(true, true, "[1]", typeof(ArgumentException))]
        [InlineData(false, false, "[]", typeof(ArgumentException))]
        [InlineData(true, false, "[1,]", typeof(ArgumentException))]
        public void InvalidIndicesAreRejected(bool fbArray, bool twoDimensions, string symbol, Type expectedException)
        {
            var info = new VarInfo { AccessSequence = "8A0E0001.F" };
            var arguments = new object[] { symbol, CreateVariable(fbArray, twoDimensions), info };

            var error = Assert.Throws<TargetInvocationException>(() => ParseIndex(arguments));

            Assert.IsType(expectedException, error.InnerException);
            Assert.Equal("8A0E0001.F", info.AccessSequence);
            Assert.False(info.ContainsIndexedArray);
        }

        private static PVartypeListElement CreateVariable(bool fbArray, bool twoDimensions)
        {
            var counts = new uint[] { 3, twoDimensions ? 2u : 0u, 0, 0, 0, 0 };
            var bounds = new[] { 1, twoDimensions ? -2 : 0, 0, 0, 0, 0 };
            POffsetInfoType offsetInfo = fbArray
                ? new POffsetInfoType_FbArray { MdimArrayElementCount = counts, MdimArrayLowerBounds = bounds }
                : new POffsetInfoType_StructMDim { MdimArrayElementCount = counts, MdimArrayLowerBounds = bounds };
            return new PVartypeListElement
            {
                OffsetInfoType = offsetInfo,
                Softdatatype = Softdatatype.S7COMMP_SOFTDATATYPE_STRUCT,
            };
        }

        private static void ParseIndex(object[] arguments)
        {
            var session = new S7CommPlusProtocolSession();
            typeof(S7CommPlusProtocolSession)
                .GetMethod("calcAccessSeqForMDimArray", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(session, arguments);
        }
    }
}
