using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using Org.BouncyCastle.X509;
using S7CommPlusDriver.Tls;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using Xunit;
using TlsCertificate = Org.BouncyCastle.Tls.Certificate;
using TlsProtocolVersion = Org.BouncyCastle.Tls.ProtocolVersion;

namespace S7CommPlusDriver.Tests
{
    public sealed class BouncyCastleTlsConnectorTests
    {
        private const string OmsExporterLabel = "EXPERIMENTAL_OMS";
        private const int OmsExporterSecretLength = 32;

        [Fact]
        public void OmsExporterSecretRemainsAvailableAfterHandshakeCompletion()
        {
            var serverInput = new BlockingInputStream();
            BouncyCastleTlsConnector? connector = null;
            var serverOutput = new CallbackOutputStream(
                (data, offset, count) => connector!.ReadCompleted(Copy(data, offset, count), count));
            var callback = new LoopbackConnectorCallback(serverInput);
            connector = new BouncyCastleTlsConnector(callback);
            var server = new ExporterTlsServer();
            Exception? serverException = null;

            var serverThread = new Thread(() =>
            {
                try
                {
                    var protocol = new TlsServerProtocol(serverInput, serverOutput);
                    protocol.Accept(server);
                }
                catch (Exception ex)
                {
                    serverException = ex;
                }
            })
            {
                IsBackground = true,
                Name = "S7CommPlus test TLS server"
            };

            try
            {
                serverThread.Start();
                connector.StartHandshake();

                Assert.True(serverThread.Join(TimeSpan.FromSeconds(10)), "TLS server handshake did not complete.");
                Assert.Null(serverException);

                var firstExport = connector.GetOmsExporterSecret();
                var secondExport = connector.GetOmsExporterSecret();

                Assert.Equal(OmsExporterSecretLength, firstExport.Length);
                Assert.Equal(server.OmsExporterSecret, firstExport);
                Assert.Equal(firstExport, secondExport);
                Assert.NotSame(firstExport, secondExport);
            }
            finally
            {
                connector.Dispose();
                serverInput.Complete();
            }
        }

        private static byte[] Copy(byte[] data, int offset, int count)
        {
            var copy = new byte[count];
            Buffer.BlockCopy(data, offset, copy, 0, count);
            return copy;
        }

        private sealed class ExporterTlsServer : DefaultTlsServer
        {
            private readonly AsymmetricKeyParameter _privateKey;
            private readonly byte[] _certificate;

            public ExporterTlsServer()
                : base(new BcTlsCrypto(new SecureRandom()))
            {
                var keyPairGenerator = new RsaKeyPairGenerator();
                keyPairGenerator.Init(new KeyGenerationParameters(Crypto.SecureRandom, 2048));
                var keyPair = keyPairGenerator.GenerateKeyPair();

                _privateKey = keyPair.Private;
                _certificate = CreateCertificate(keyPair);
            }

            public byte[] OmsExporterSecret { get; private set; } = Array.Empty<byte>();

            public override int[] GetCipherSuites()
            {
                return new[]
                {
                    CipherSuite.TLS_AES_256_GCM_SHA384,
                    CipherSuite.TLS_AES_128_GCM_SHA256
                };
            }

            public override TlsCredentials GetCredentials()
            {
                var tlsCertificate = m_context.Crypto.CreateCertificate(_certificate);
                var certificate = new TlsCertificate(
                    TlsUtilities.EmptyBytes,
                    new[] { new CertificateEntry(tlsCertificate, null) });
                var signatureAlgorithm = TlsUtilities.ChooseSignatureAndHashAlgorithm(
                    m_context,
                    m_context.SecurityParameters.ClientSigAlgs,
                    SignatureAlgorithm.rsa);

                return new BcDefaultTlsCredentialedSigner(
                    new TlsCryptoParameters(m_context),
                    (BcTlsCrypto)m_context.Crypto,
                    _privateKey,
                    certificate,
                    signatureAlgorithm);
            }

            public override void NotifyHandshakeComplete()
            {
                base.NotifyHandshakeComplete();
                OmsExporterSecret = m_context.ExportKeyingMaterial(
                    OmsExporterLabel,
                    null,
                    OmsExporterSecretLength);
            }

            protected override TlsProtocolVersion[] GetSupportedVersions()
            {
                return TlsProtocolVersion.TLSv13.Only();
            }

            private static byte[] CreateCertificate(AsymmetricCipherKeyPair keyPair)
            {
                var certificateGenerator = new X509V3CertificateGenerator();
                var name = new X509Name("CN=S7CommPlusDriver Test");
                certificateGenerator.SetSerialNumber(BigInteger.One);
                certificateGenerator.SetIssuerDN(name);
                certificateGenerator.SetSubjectDN(name);
                certificateGenerator.SetNotBefore(DateTime.UtcNow.AddMinutes(-1));
                certificateGenerator.SetNotAfter(DateTime.UtcNow.AddMinutes(5));
                certificateGenerator.SetPublicKey(keyPair.Public);

                return certificateGenerator
                    .Generate(new Asn1SignatureFactory("SHA256WITHRSA", keyPair.Private))
                    .GetEncoded();
            }
        }

        private sealed class LoopbackConnectorCallback : IS7TlsConnectorCallback
        {
            private readonly BlockingInputStream _serverInput;

            public LoopbackConnectorCallback(BlockingInputStream serverInput)
            {
                _serverInput = serverInput;
            }

            public void WriteData(byte[] data, int dataLength)
            {
                _serverInput.Add(data, dataLength);
            }

            public void OnDataAvailable()
            {
            }

            public void OnSslError(int sslError, string sslState)
            {
            }
        }

        private sealed class BlockingInputStream : Stream
        {
            private readonly BlockingCollection<byte[]> _incoming = new BlockingCollection<byte[]>();
            private byte[] _current = Array.Empty<byte>();
            private int _offset;

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public void Add(byte[] data, int count)
            {
                _incoming.Add(Copy(data, 0, count));
            }

            public void Complete()
            {
                _incoming.CompleteAdding();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                while (_current == null || _offset >= _current.Length)
                {
                    try
                    {
                        _current = _incoming.Take();
                        _offset = 0;
                    }
                    catch (InvalidOperationException)
                    {
                        return 0;
                    }
                }

                var bytesToCopy = Math.Min(count, _current.Length - _offset);
                Buffer.BlockCopy(_current, _offset, buffer, offset, bytesToCopy);
                _offset += bytesToCopy;
                return bytesToCopy;
            }

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        private sealed class CallbackOutputStream : Stream
        {
            private readonly Action<byte[], int, int> _write;

            public CallbackOutputStream(Action<byte[], int, int> write)
            {
                _write = write;
            }

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count)
            {
                _write(buffer, offset, count);
            }
        }
    }
}
