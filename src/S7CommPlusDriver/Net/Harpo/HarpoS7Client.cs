using HarpoS7;
using HarpoS7.Auth;
using HarpoS7.Extensions;
using HarpoS7.PoC;
using HarpoS7.PublicKeys.Exceptions;
using HarpoS7.PublicKeys.Impl;
using HarpoS7.Utilities.Auth;
using HarpoS7.Utilities.Extensions;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace S7CommPlusDriver.Net.Harpo
{
    public class HarpoS7Client : IS7Client
    {
        public IS7Client._OnDataReceived OnDataReceived { get; set; }

        private string _address;
        private int _port = 102;

        public int SetConnectionParams(string address, ushort LocalTSAP, byte[] RemoteTSAP)
        {
            _address = address;
            return 0;
        }

        public int Connect()
        {
            ConnectAsync().Wait();
            return 0;
        }

        public int Disconnect()
        {
            return 0;
        }

        public void Send(byte[] Buffer)
        {
        }

        public int SslActivate()
        {
            return 0;
        }

        private async Task ConnectAsync()
        {
            var readBuffer = new byte[1024];
            if (!IPEndPoint.TryParse(_address + ":" + _port, out var endPoint))
            {
                //TODO: error...
                return;
            }

            using var client = new TcpClient();

            try
            {
                await client.ConnectAsync(endPoint);
            }
            catch (SocketException ex)
            {
                //TODO: error...
                return;
            }

            var stream = client.GetStream();

            // Send COTP Connection Request
            var cotpConnectionRequest = new byte[]
            {
                0x03, 0x00, 0x00, 0x24, 0x1F, 0xE0, 0x00, 0x00, 0x00, 0x01, 0x00, 0xC1,
                0x02, 0x06, 0x00, 0xC2, 0x10, 0x53, 0x49, 0x4D, 0x41, 0x54, 0x49, 0x43,
                0x2D, 0x52, 0x4F, 0x4F, 0x54, 0x2D, 0x48, 0x4D, 0x49, 0xC0, 0x01, 0x0A
            };

            await stream.WriteAsync(cotpConnectionRequest);

            _ = await stream.ReadAsync(readBuffer);

            // write empty DT-Data
            var emptyDtData = new byte[]
            {
                0x03, 0x00, 0x00, 0x07, 0x02, 0xF0, 0x00
            };
            await stream.WriteAsync(emptyDtData);

            // Send S7CommPlus CreateObject request (creates a session object on the PLC)
            var createObjectRequest = new byte[]
            {
                0x03, 0x00, 0x01, 0x10, 0x02, 0xF0, 0x80, 0x72, 0x01, 0x01, 0x01, 0x31,
                0x00, 0x00, 0x04, 0xCA, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x01, 0x20,
                0x36, 0x00, 0x00, 0x01, 0x1D, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00,
                0xA1, 0x00, 0x00, 0x00, 0xD3, 0x82, 0x1F, 0x00, 0x00, 0xA3, 0x81, 0x69,
                0x00, 0x15, 0x15, 0x53, 0x65, 0x72, 0x76, 0x65, 0x72, 0x53, 0x65, 0x73,
                0x73, 0x69, 0x6F, 0x6E, 0x5F, 0x31, 0x43, 0x39, 0x43, 0x33, 0x38, 0x31,
                0xA3, 0x82, 0x21, 0x00, 0x15, 0x41, 0x30, 0x3A, 0x3A, 0x3A, 0x36, 0x2E,
                0x30, 0x3A, 0x3A, 0x41, 0x53, 0x49, 0x58, 0x20, 0x41, 0x58, 0x38, 0x38,
                0x31, 0x37, 0x39, 0x20, 0x55, 0x53, 0x42, 0x20, 0x33, 0x2E, 0x30, 0x20,
                0x74, 0x6F, 0x20, 0x47, 0x69, 0x67, 0x61, 0x62, 0x69, 0x74, 0x20, 0x45,
                0x74, 0x68, 0x65, 0x72, 0x6E, 0x65, 0x74, 0x20, 0x41, 0x64, 0x61, 0x70,
                0x74, 0x65, 0x72, 0x2E, 0x54, 0x43, 0x50, 0x49, 0x50, 0x2E, 0x31, 0xA3,
                0x82, 0x28, 0x00, 0x15, 0x0A, 0x52, 0x65, 0x61, 0x64, 0x20, 0x57, 0x72,
                0x69, 0x74, 0x65, 0xA3, 0x82, 0x29, 0x00, 0x15, 0x0B, 0x48, 0x4D, 0x49,
                0x20, 0x52, 0x54, 0x20, 0x4F, 0x4D, 0x53, 0x2B, 0xA3, 0x82, 0x2A, 0x00,
                0x15, 0x08, 0x59, 0x6F, 0x75, 0x72, 0x48, 0x6F, 0x73, 0x74, 0xA3, 0x82,
                0x2B, 0x00, 0x04, 0x02, 0xA3, 0x82, 0x2C, 0x00, 0x12, 0x01, 0xC9, 0xC3,
                0x81, 0xA3, 0x82, 0x2D, 0x00, 0x15, 0x0F, 0x52, 0x65, 0x61, 0x64, 0x2F,
                0x57, 0x72, 0x69, 0x74, 0x65, 0x20, 0x74, 0x61, 0x67, 0x73, 0xA1, 0x00,
                0x00, 0x00, 0xD3, 0x81, 0x7F, 0x00, 0x00, 0xA3, 0x81, 0x69, 0x00, 0x15,
                0x15, 0x53, 0x75, 0x62, 0x73, 0x63, 0x72, 0x69, 0x70, 0x74, 0x69, 0x6F,
                0x6E, 0x43, 0x6F, 0x6E, 0x74, 0x61, 0x69, 0x6E, 0x65, 0x72, 0xA2, 0xA2,
                0x00, 0x00, 0x00, 0x00, 0x72, 0x01, 0x00, 0x00
            };

            await stream.WriteAsync(createObjectRequest);

            _ = await stream.ReadAsync(readBuffer);

            await stream.WriteAsync(emptyDtData);

            // read the session object id
            const int sessionIdOffset = 0x17;
            var sessionId = Vlq.DecodeAsVlq32(readBuffer.AsSpan(sessionIdOffset, 5), out _);

            // read the public key fingerprint
            // the string length is serialized as a VLQ-encoded number

            const int plcSimPacketFingerprintLengthOffset = 0x37;
            const int realPlcPacketFingerprintLengthOffset = 0x2F;

            // max length of a 32-bit VLQ number is 5 (4+1) bytes
            var fingerprintLength = Vlq.DecodeAsVlq32(readBuffer.AsSpan(plcSimPacketFingerprintLengthOffset, 5), out var vlqLength);
            var fingerprintValueOffset = plcSimPacketFingerprintLengthOffset + vlqLength;

            var fingerprintStringBytes = readBuffer.AsMemory(fingerprintValueOffset, (int)fingerprintLength);
            var fingerprintString = Encoding.UTF8.GetString(fingerprintStringBytes.Span);

            if (!fingerprintString.StartsWith("03:") && !fingerprintString.StartsWith("00:") && !fingerprintString.StartsWith("01:"))
            {
                fingerprintLength = Vlq.DecodeAsVlq32(readBuffer.AsSpan(realPlcPacketFingerprintLengthOffset, 5), out vlqLength);
                fingerprintValueOffset = realPlcPacketFingerprintLengthOffset + vlqLength;

                fingerprintStringBytes = readBuffer.AsMemory(fingerprintValueOffset, (int)fingerprintLength);
                fingerprintString = Encoding.UTF8.GetString(fingerprintStringBytes.Span);

                if (!fingerprintString.StartsWith("03:") && !fingerprintString.StartsWith("00:") && !fingerprintString.StartsWith("01:"))
                {
                    return;
                }
            }

            // again, you would normally deserialize the response packet
            // and read the challenge array safely, instead of relying on byte offsets
            var rawChallengeArrayOffset = fingerprintString.StartsWith("03:") ? 0x7D : 0x75;
            const int rawChallengeArrayLength = 20;

            // read the 20-long byte buffer (the challenge)
            var challenge = readBuffer.AsMemory(rawChallengeArrayOffset, rawChallengeArrayLength);

            // reverse string and parse fingerprint
            var publicKeyFingerprint = new byte[Constants.KeyIdLength];
            Helpers.ParseAndReverseBytes(fingerprintString, publicKeyFingerprint);

            // get the matching public key from the KeyStore
            var store = new DefaultPublicKeyStore();
            var publicKey = new byte[store.GetPublicKeyLength(fingerprintString)];

            try
            {
                store.ReadPublicKey(publicKey.AsSpan(), fingerprintString);
            }
            catch (UnknownPublicKeyException)
            {
                return;
            }

            // create buffers
            var sessionKey = new byte[Constants.SessionKeyLength];
            var keyBlob = new byte[fingerprintString.StartsWith("03:") ? CommonConstants.EncryptedBlobLengthPlcSim : CommonConstants.EncryptedBlobLengthRealPlc];

            var publicKeyFamily = fingerprintString.ToPublicKeyFamily();

            // auth locally
            LegacyAuthenticationScheme.Authenticate(
                keyBlob.AsSpan(),
                sessionKey.AsSpan(),
                challenge.Span,
                publicKey.AsSpan(),
                publicKeyFamily
            );

            // construct metadata
            var pubKeyId = new byte[Constants.KeyIdLength];
            var sessionKeyId = new byte[Constants.KeyIdLength];

            publicKey.DeriveKeyId(pubKeyId);
            sessionKey.DeriveKeyId(sessionKeyId);
        }
    }
}
