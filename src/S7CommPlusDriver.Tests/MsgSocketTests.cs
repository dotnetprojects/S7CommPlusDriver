using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    public sealed class MsgSocketTests
    {
        [Fact]
        public async Task ReceiveTimeoutKeepsConnectionOpen()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            try
            {
                var acceptTask = listener.AcceptSocketAsync();
                var socket = new MsgSocket
                {
                    ConnectTimeout = 1000,
                    ReadTimeout = 100,
                    WriteTimeout = 1000
                };
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;

                var connectError = socket.Connect(IPAddress.Loopback.ToString(), port);
                using var serverSocket = await acceptTask;

                var buffer = new byte[1];
                var receiveError = socket.Receive(buffer, 0, buffer.Length);
                var connectedAfterTimeout = socket.Connected;

                socket.Close();

                Assert.Equal(0, connectError);
                Assert.Equal(S7Consts.errTCPReceiveTimeout, receiveError);
                Assert.True(connectedAfterTimeout);
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
