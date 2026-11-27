using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    public class PlaintextCreateObjectResponseTests
    {
        // Captured CreateObject response from CPU 1212C FW V2.2, with version and without framing.
        private const string CreateObjectResponseHex =
            "0132000004ca00000002361102872a8725a100000120821f0000a38169001500a3823200170000013a823b00048200823c00048140823d00048480c040823e00048480c040823f00151b313b36455337203231322d31424433302d30584230203b56322e328240001505323b35333682410003000300a200000000";

        [Fact]
        public void ParsesS71200Fw22CreateObjectResponseFromCapture()
        {
            var bytes = HexToBytes(CreateObjectResponseHex);
            using var stream = new MemoryStream(bytes);

            var resp = CreateObjectResponse.DeserializeFromPdu(stream);

            Assert.NotNull(resp);
            Assert.Equal(2, resp.ObjectIds.Count);
            var session = Assert.IsType<ValueStruct>(resp.ResponseObject.GetAttribute(Ids.ServerSessionVersion));
            var paom = Assert.IsType<ValueWString>(session.GetStructElement((uint)Ids.LID_SessionVersionSystemPAOMString));
            Assert.Contains("212-1BD30-0XB0", paom.GetValue());
            Assert.Contains("V2.2", paom.GetValue());
        }

        [Theory]
        [InlineData(S7CommPlusSecurityMode.Auto)]
        [InlineData(S7CommPlusSecurityMode.LegacyChallenge)]
        public async Task MissingChallengeFailsWithoutPlaintextSetup(S7CommPlusSecurityMode securityMode)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var options = new S7CommPlusClientOptions
            {
                Address = "127.0.0.1",
                Port = ((IPEndPoint)listener.LocalEndpoint).Port,
                SecurityMode = securityMode,
                ConnectTimeout = TimeSpan.FromSeconds(2)
            };
            var session = new S7CommPlusProtocolSession();
            var server = Task.Run(() =>
            {
                if (securityMode == S7CommPlusSecurityMode.Auto)
                {
                    using var tlsConnection = AcceptCotpConnection(listener);
                    ReadTpkt(tlsConnection); // Close the connection after receiving InitSsl.
                }

                using var legacyConnection = AcceptCotpConnection(listener);
                var request = ReadTpkt(legacyConnection);
                var response = HexToBytes(CreateObjectResponseHex);
                // Copy the request sequence into the response. The request has ten framing bytes
                // beyond the leading version byte included in the captured response.
                response[8] = request[18];
                response[9] = request[19];
                using var packet = new MemoryStream();
                packet.WriteByte(3);
                packet.WriteByte(0);
                S7p.EncodeUInt16(packet, (ushort)(14 + response.Length));
                packet.Write(new byte[] { 2, 0xf0, 0x80, 0x72, response[0] }, 0, 5);
                S7p.EncodeUInt16(packet, (ushort)(response.Length - 1));
                packet.Write(response, 1, response.Length - 1);
                packet.Write(new byte[] { 0x72, response[0], 0, 0 }, 0, 4);
                packet.WriteTo(legacyConnection);

                Assert.Equal(7, ReadTpkt(legacyConnection).Length); // Empty DT acknowledgement.
                try
                {
                    Assert.Equal(-1, legacyConnection.ReadByte());
                }
                catch (IOException ex) when (ex.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionReset })
                {
                    // An abortive close also confirms that no session setup followed.
                }
            });

            try
            {
                var result = await Task.Run(() => session.Connect(options));
                Assert.Equal(S7Consts.errS7CommPlusLegacyAuthentication, result);
                Assert.Null(options.NegotiatedSecurityMode);
                await server;
                Assert.False(listener.Pending());
            }
            finally
            {
                listener.Stop();
                session.Disconnect();
            }
        }

        private static NetworkStream AcceptCotpConnection(TcpListener listener)
        {
            var stream = new NetworkStream(listener.AcceptSocket(), ownsSocket: true) { ReadTimeout = 5000 };
            try
            {
                ReadTpkt(stream);
                var confirmation = new byte[] { 3, 0, 0, 11, 6, 0xd0, 0, 1, 0, 1, 0 };
                stream.Write(confirmation, 0, confirmation.Length);
                return stream;
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        private static byte[] ReadTpkt(Stream stream)
        {
            var header = new byte[4];
            ReadFully(stream, header, 0);
            var packet = new byte[(header[2] << 8) | header[3]];
            header.CopyTo(packet, 0);
            ReadFully(stream, packet, header.Length);
            return packet;
        }

        private static void ReadFully(Stream stream, byte[] bytes, int offset)
        {
            while (offset < bytes.Length)
            {
                var count = stream.Read(bytes, offset, bytes.Length - offset);
                if (count == 0) throw new EndOfStreamException();
                offset += count;
            }
        }

        private static byte[] HexToBytes(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }

            return bytes;
        }
    }
}
