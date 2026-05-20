using Xunit;

namespace S7CommPlusDriver.Tests
{
    public sealed class S7ClientTransportTests
    {
        [Fact]
        public void ConnectUsesInjectedTransportAndTimeouts()
        {
            var transport = new FakeS7Transport { ConnectError = S7Consts.errTCPConnectionFailed };
            var client = new S7Client(() => transport)
            {
                PLCPort = 120,
                ConnTimeout = 111,
                RecvTimeout = 222,
                SendTimeout = 333
            };
            client.SetConnectionParams("1.2.3.4", 0x0600, new byte[] { 1, 2, 3 });

            var error = client.Connect();

            Assert.Equal(S7Consts.errTCPConnectionFailed, error);
            Assert.Equal(1, transport.ConnectCount);
            Assert.Equal(("1.2.3.4", 120, 111, 222, 333), transport.LastConnect);
        }

        [Fact]
        public void DisconnectClosesInjectedTransport()
        {
            var transport = new FakeS7Transport { Connected = true };
            var client = new S7Client(() => transport);

            var error = client.Disconnect(50);

            Assert.Equal(0, error);
            Assert.Equal(1, transport.CloseCount);
            Assert.False(transport.Connected);
        }
    }
}
