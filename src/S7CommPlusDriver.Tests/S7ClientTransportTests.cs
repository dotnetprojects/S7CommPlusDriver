using S7CommPlusDriver.Internal;
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
        public void UpdatingTimeoutsPropagatesToExistingTransport()
        {
            var transport = new FakeS7Transport();
            var client = new S7Client(() => transport)
            {
                RecvTimeout = 222,
                SendTimeout = 333
            };

            client.SetTransportTimeouts(120000, 120000);

            Assert.Equal(120000, client.RecvTimeout);
            Assert.Equal(120000, client.SendTimeout);
            Assert.Equal((120000, 120000), transport.UpdatedTimeouts);
        }

        [Fact]
        public void ConnectAcceptsConnectionConfirmForShortRemoteTsap()
        {
            var transport = new FakeS7Transport { EmptyReceiveDelayMilliseconds = 500 };
            transport.EnqueueReceive(new byte[] { 0x03, 0x00, 0x00, 0x23 });
            transport.EnqueueReceive(new byte[] { 0x1E, 0xD0, 0x00 });
            transport.EnqueueReceive(new byte[28]);

            var client = new S7Client(() => transport);
            client.SetConnectionParams("1.2.3.4", 0x0600, System.Text.Encoding.ASCII.GetBytes(S7CommPlusDefaults.RemoteTsapEs));

            var error = client.Connect();
            client.Disconnect(50);

            Assert.Equal(0, error);
            Assert.Single(transport.Sent);
            Assert.Equal(35, transport.Sent[0].Length);
            Assert.Equal(S7CommPlusProtocolConstants.DefaultIsoTpduSize, client.PduSizeNegotiated);
        }

        [Fact]
        public void ConnectReadsNegotiatedTpduSizeFromConnectionConfirm()
        {
            var transport = new FakeS7Transport { EmptyReceiveDelayMilliseconds = 500 };
            transport.EnqueueReceive(new byte[] { 0x03, 0x00, 0x00, 0x0E });
            transport.EnqueueReceive(new byte[] { 0x09, 0xD0, 0x00 });
            transport.EnqueueReceive(new byte[] { 0x00, 0x00, 0x01, 0x00, 0xC0, 0x01, 0x09 });

            var client = new S7Client(() => transport);
            client.SetConnectionParams("1.2.3.4", 0x0600, System.Text.Encoding.ASCII.GetBytes(S7CommPlusDefaults.RemoteTsapEs));

            var error = client.Connect();
            client.Disconnect(50);

            Assert.Equal(0, error);
            Assert.Equal(512, client.PduSizeNegotiated);
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
