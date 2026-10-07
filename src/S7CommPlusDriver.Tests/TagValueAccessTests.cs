using System;
using System.Linq;
using S7CommPlusDriver.ClientApi;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    public sealed class TagValueAccessTests
    {
        [Theory]
        [InlineData(Softdatatype.S7COMMP_SOFTDATATYPE_LREAL)]
        [InlineData(Softdatatype.S7COMMP_SOFTDATATYPE_WSTRING)]
        public void ArraysWithoutDedicatedTagTypesSupportBoxedReadsAndWrites(uint datatype)
        {
            var tag = S7CommPlusProtocolSession.CreateResolvedPlcTag(new VarInfo
            {
                Name = "DB.Values", AccessSequence = "8A0E0001.1", Softdatatype = datatype,
                ArrayElementCount = 2, ArrayDimensions = new[] { new S7CommPlusArrayDimension(0, 2) },
            });
            Array values = datatype == Softdatatype.S7COMMP_SOFTDATATYPE_LREAL ? new double[] { 1, 2 } : new string[] { "a", "b" };
            tag.SetValue(values);
            Assert.Same(values, tag.GetValue());
            values.SetValue(datatype == Softdatatype.S7COMMP_SOFTDATATYPE_LREAL ? (object)3d : "c", 1);
            tag.PrepareAggregateWrite();
            Assert.Equal(values.GetValue(1), tag.AggregateElements[1].GetValue());
            tag.CompleteAggregateRead(0);
            Assert.Equal(values.Cast<object>(), Assert.IsAssignableFrom<Array>(tag.GetValue()).Cast<object>());
        }

        [Fact]
        public void BoxedAccessPreservesTheConcreteValueType()
        {
            PlcTag tag = new PlcTagInt("Count", new ItemAddress("8A0E0001.1"), Softdatatype.S7COMMP_SOFTDATATYPE_INT);
            tag.SetValue((short)123);
            Assert.Equal((short)123, Assert.IsType<short>(tag.GetValue()));
            Assert.Throws<ArgumentException>(() => tag.SetValue("123"));
            Assert.Equal((short)123, tag.GetValue());
        }

        [Fact]
        public void BoxedStringAccessUsesTheTypedSetter()
        {
            PlcTag tag = new PlcTagString("Name", new ItemAddress("8A0E0001.1"), Softdatatype.S7COMMP_SOFTDATATYPE_STRING, 5);
            tag.SetValue("hello");
            Assert.Equal("hello", tag.GetValue());
            var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => tag.SetValue("hello world"));
            Assert.IsType<ArgumentOutOfRangeException>(error.InnerException);
        }

        [Theory]
        [InlineData(12, 14)]
        [InlineData(14, 12)]
        public void ScalarStringMetadataHandlesBothFieldOrders(ushort first, ushort second)
        {
            POffsetInfoType offset = new POffsetInfoType_String { UnspecifiedOffsetinfo1 = first, UnspecifiedOffsetinfo2 = second };
            Assert.Equal(12, offset.MaxStringLength());
        }

        [Fact]
        public void BothArrayShapesExposeTheirStringElementCapacity()
        {
            POffsetInfoType[] offsets =
            {
                new POffsetInfoType_Array1Dim { UnspecifiedOffsetinfo1 = 42 },
                new POffsetInfoType_ArrayMDim { UnspecifiedOffsetinfo1 = 42 },
            };
            Assert.All(offsets, offset => Assert.Equal(42, offset.MaxStringLength()));
            Assert.Equal(0, new POffsetInfoType_Std().MaxStringLength());
        }
    }
}
