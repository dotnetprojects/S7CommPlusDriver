using System;

namespace OpenSsl
{
    internal sealed class OpenSslTlsConnector : S7CommPlusDriver.IS7TlsConnector
    {
        private readonly OpenSSLConnector _connector;

        public OpenSslTlsConnector(IntPtr context, S7CommPlusDriver.IS7TlsConnectorCallback dataSink)
        {
            _connector = new OpenSSLConnector(context, new CallbackAdapter(dataSink));
            _connector.ExpectConnect();
        }

        public void Write(byte[] data, int dataLength)
        {
            _connector.Write(data, dataLength);
        }

        public void ReadCompleted(byte[] data, int dataLength)
        {
            _connector.ReadCompleted(data, dataLength);
        }

        public int Receive(ref byte[] buffer, int bufferSize)
        {
            return _connector.Receive(ref buffer, bufferSize);
        }

        public byte[] GetOmsExporterSecret()
        {
            return _connector.getOMSExporterSecret();
        }

        public void Dispose()
        {
            _connector.Dispose();
        }

        private sealed class CallbackAdapter : OpenSSLConnector.IConnectorCallback
        {
            private readonly S7CommPlusDriver.IS7TlsConnectorCallback _dataSink;

            public CallbackAdapter(S7CommPlusDriver.IS7TlsConnectorCallback dataSink)
            {
                _dataSink = dataSink;
            }

            public void WriteData(byte[] pData, int dataLength)
            {
                _dataSink.WriteData(pData, dataLength);
            }

            public void OnDataAvailable()
            {
                _dataSink.OnDataAvailable();
            }

            public void OnSslError(int sslError, string sslState)
            {
                _dataSink.OnSslError(sslError, sslState);
            }
        }
    }
}
