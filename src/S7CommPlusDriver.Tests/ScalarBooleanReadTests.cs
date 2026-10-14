using System;
using System.IO;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    public sealed class ScalarBooleanReadTests
    {
        [Theory]
        [InlineData(0, 0)]
        [InlineData(0, 1)]
        [InlineData(1, 1)]
        [InlineData(2, 0)]
        public void BooleanReadPreservesValueAndFlags(int flags, int value)
        {
            using var stream = new MemoryStream(new[] { (byte)flags, (byte)Datatype.Bool, (byte)value });
            var result = Assert.IsType<ValueBool>(PValue.Deserialize(stream));
            Assert.Equal(value != 0, result.GetValue());
            using var encoded = new MemoryStream(); result.Serialize(encoded);
            Assert.Equal(new[] { (byte)flags, (byte)Datatype.Bool, (byte)value }, encoded.ToArray());
        }

#if NET6_0_OR_GREATER
        [Fact]
        public void OrdinaryBooleanDecodingDoesNotAllocatePerSignal()
        {
            using var stream = new MemoryStream(new[] { (byte)0, (byte)Datatype.Bool, (byte)1 });
            _ = PValue.Deserialize(stream);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) { stream.Position = 0; _ = PValue.Deserialize(stream); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
#endif
    }
}
