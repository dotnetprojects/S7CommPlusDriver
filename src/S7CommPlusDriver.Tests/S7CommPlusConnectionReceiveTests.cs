using System.IO;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    public sealed class S7CommPlusConnectionReceiveTests
    {
        [Fact]
        public void MalformedPduPublishesReceiveError()
        {
            var connection = new S7CommPlusConnection();
            connection.DebugResetReceiveDispatcherForTests();

            connection.DebugOnDataReceivedForTests(new byte[] { 0x00, ProtocolVersion.V1, 0x00, 0x00 });
            var error = connection.DebugReceiveNextS7plusPduForTests(100, out var pdu);

            Assert.Equal(S7Consts.errIsoInvalidPDU1, error);
            Assert.Null(pdu);
        }

        [Fact]
        public void InvalidProtocolVersionPublishesReceiveError()
        {
            var connection = new S7CommPlusConnection();
            connection.DebugResetReceiveDispatcherForTests();

            connection.DebugOnDataReceivedForTests(new byte[] { 0x72, 0x42, 0x00, 0x00 });
            var error = connection.DebugReceiveNextS7plusPduForTests(100, out var pdu);

            Assert.Equal(S7Consts.errIsoInvalidPDU2, error);
            Assert.Null(pdu);
        }

        [Fact]
        public void CompletePduIsDispatchedWithoutPolling()
        {
            var connection = new S7CommPlusConnection();
            connection.DebugResetReceiveDispatcherForTests();

            connection.DebugOnDataReceivedForTests(new byte[] { 0x72, ProtocolVersion.V1, 0x00, 0x01, 0xAA, 0x72, ProtocolVersion.V1, 0x00, 0x00 });
            var error = connection.DebugReceiveNextS7plusPduForTests(100, out MemoryStream pdu);

            Assert.Equal(0, error);
            Assert.NotNull(pdu);
            Assert.Equal(new byte[] { ProtocolVersion.V1, 0xAA }, pdu.ToArray());
        }

        [Fact]
        public void ReceiveTimeoutReturnsJobTimeout()
        {
            var connection = new S7CommPlusConnection();
            connection.DebugResetReceiveDispatcherForTests();

            var error = connection.DebugReceiveNextS7plusPduForTests(10, out var pdu);

            Assert.Equal(S7Consts.errCliJobTimeout, error);
            Assert.Null(pdu);
        }
    }
}
