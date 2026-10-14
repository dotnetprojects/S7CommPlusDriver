using System;
using System.IO;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    public sealed class PlaintextBufferOwnershipTests
    {
        [Fact]
        public void PublicCallbackReceivesAnOwnedExactLengthCopy()
        {
            using var client = new S7Client();
            byte[]? received = null;
            client.OnDataReceived = (data, count) => { Assert.Equal(3, count); received = data; };
            var source = new byte[] { 1, 2, 3, 99, 99 };
            ((IS7TlsPlaintextCallback)client).OnPlaintextData(source, 3);
            source[0] = 42;
            Assert.Equal(new byte[] { 1, 2, 3 }, received);
            Assert.NotSame(source, received);
        }

        [Fact]
        public void BorrowedProtocolCallbackConsumesOnlyTheActivePrefix()
        {
            using var client = new S7Client();
            byte[]? received = null; var countReceived = 0;
            client.OnBorrowedDataReceived = (data, count) => { received = data; countReceived = count; };
            var source = new byte[] { 1, 2, 3, 99, 99 };
            ((IS7TlsPlaintextCallback)client).OnPlaintextData(source, 3);
            Assert.Same(source, received); Assert.Equal(3, countReceived);
        }

        [Fact]
        public void ProtocolCopiesBorrowedPayloadBeforeTheBufferIsReused()
        {
            var session = new S7CommPlusProtocolSession();
            session.DebugResetReceiveDispatcherForTests();
            using var client = new S7Client();
            client.OnBorrowedDataReceived = (data, count) => session.DebugOnBorrowedDataReceivedForTests(data, count);
            var source = new byte[] { 0x72, ProtocolVersion.V1, 0, 1, 0xAA, 0x72, ProtocolVersion.V1, 0, 0, 99 };
            ((IS7TlsPlaintextCallback)client).OnPlaintextData(source, 9);
            Array.Clear(source, 0, source.Length);
            Assert.Equal(0, session.DebugReceiveNextS7plusPduForTests(100, out MemoryStream pdu));
            Assert.Equal(new byte[] { ProtocolVersion.V1, 0xAA }, pdu.ToArray());
        }

        [Fact]
        public void DeclaredLengthCannotReadInactiveBorrowedBufferTail()
        {
            var session = new S7CommPlusProtocolSession();
            session.DebugResetReceiveDispatcherForTests();
            var source = new byte[] { 0x72, ProtocolVersion.V1, 0, 2, 0xAA, 0xBB, 0x72, ProtocolVersion.V1, 0, 0 };
            session.DebugOnBorrowedDataReceivedForTests(source, 5);
            Assert.Equal(S7Consts.errIsoInvalidPDU, session.DebugReceiveNextS7plusPduForTests(100, out var pdu));
            Assert.Null(pdu);
        }

#if NET6_0_OR_GREATER
        [Fact]
        public void BorrowedDeliveryDoesNotAllocateAnEightKilobyteScratchArray()
        {
            using var client = new S7Client();
            client.OnBorrowedDataReceived = (_, _) => { };
            var source = new byte[128];
            var sink = (IS7TlsPlaintextCallback)client;
            sink.OnPlaintextData(source, source.Length);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) sink.OnPlaintextData(source, source.Length);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
#endif
    }
}
