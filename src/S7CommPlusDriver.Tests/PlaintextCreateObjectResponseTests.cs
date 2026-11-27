using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
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
            Assert.Equal(0x11UL, resp.ReturnValue);
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
                SendCreateObjectResponse(legacyConnection, HexToBytes(CreateObjectResponseHex));
                AssertConnectionClosed(legacyConnection);
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

        [Theory]
        [InlineData(S7CommPlusSecurityMode.Auto, "V2.2", false, false, true)]
        [InlineData(S7CommPlusSecurityMode.Auto, "V4.5", false, false, false)]
        [InlineData(S7CommPlusSecurityMode.Auto, "V2.2", true, false, false)]
        [InlineData(S7CommPlusSecurityMode.Auto, "V2.2", false, true, false)]
        [InlineData(S7CommPlusSecurityMode.Tls, "V2.2", false, false, false)]
        public async Task InitSslRejectionAllowsPlaintextOnlyForKnownFirmwareInAuto(
            S7CommPlusSecurityMode securityMode, string firmware, bool hasChallenge, bool invalidFill, bool connects)
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
                using (var firstConnection = AcceptCotpConnection(listener))
                {
                    var probe = ReadTpkt(firstConnection);
                    Assert.Equal(Functioncode.InitSsl, (probe[14] << 8) | probe[15]);
                    SendResponse(firstConnection, probe, HexToBytes("0132000005a9000000003401"));
                    if (securityMode == S7CommPlusSecurityMode.Tls)
                    {
                        AssertConnectionClosed(firstConnection);
                        return;
                    }

                    var response = HexToBytes(CreateObjectResponseHex);
                    var versionOffset = Encoding.ASCII.GetString(response).IndexOf("V2.2", StringComparison.Ordinal);
                    Encoding.ASCII.GetBytes(firmware).CopyTo(response, versionOffset);
                    if (invalidFill)
                        response[response.Length - 1] = 1;
                    if (hasChallenge)
                    {
                        using var withChallenge = new MemoryStream();
                        withChallenge.Write(response, 0, response.Length - 5);
                        withChallenge.WriteByte(ElementID.Attribute);
                        S7p.EncodeUInt32Vlq(withChallenge, Ids.ServerSessionChallenge);
                        new ValueUSIntArray(new byte[20]).Serialize(withChallenge);
                        withChallenge.Write(response, response.Length - 5, 5);
                        response = withChallenge.ToArray();
                    }
                    SendCreateObjectResponse(firstConnection, response);
                    if (connects)
                    {
                        var setup = ReadTpkt(firstConnection);
                        Assert.Equal(Functioncode.SetMultiVariables, (setup[14] << 8) | setup[15]);
                        SendResponse(firstConnection, setup, HexToBytes("0232000005420000000034000000"));
                        return;
                    }
                    AssertConnectionClosed(firstConnection);
                }

                using var legacyConnection = AcceptCotpConnection(listener);
                SendCreateObjectResponse(legacyConnection, HexToBytes(CreateObjectResponseHex));
                AssertConnectionClosed(legacyConnection);
            });

            try
            {
                var result = await Task.Run(() => session.Connect(options));
                Assert.Equal(connects ? 0 : securityMode == S7CommPlusSecurityMode.Tls
                    ? S7Consts.errInitSslResponse : S7Consts.errS7CommPlusLegacyAuthentication, result);
                Assert.Equal(connects ? S7CommPlusSecurityMode.Plaintext : (S7CommPlusSecurityMode?)null,
                    options.NegotiatedSecurityMode);
                await server;
                Assert.False(listener.Pending());
            }
            finally
            {
                listener.Stop();
                session.Disconnect();
            }
        }

        private static void SendCreateObjectResponse(NetworkStream connection, byte[] response)
        {
            var request = ReadTpkt(connection);
            Assert.Equal(Functioncode.CreateObject, (request[14] << 8) | request[15]);
            SendResponse(connection, request, response);
            Assert.Equal(7, ReadTpkt(connection).Length); // Empty DT acknowledgement.
        }

        private static void SendResponse(NetworkStream connection, byte[] request, byte[] response)
        {
            // The request has ten framing bytes beyond the leading version byte in the response.
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
            packet.WriteTo(connection);
        }

        private static void AssertConnectionClosed(NetworkStream connection)
        {
            try
            {
                Assert.Equal(-1, connection.ReadByte());
            }
            catch (IOException ex) when (ex.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionReset })
            {
                // An abortive close also confirms there was no further request.
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
