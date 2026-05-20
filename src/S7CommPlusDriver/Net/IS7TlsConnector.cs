using System;

namespace S7CommPlusDriver
{
    internal interface IS7TlsConnector : IDisposable
    {
        void Write(byte[] data, int dataLength);
        void ReadCompleted(byte[] data, int dataLength);
        int Receive(ref byte[] buffer, int bufferSize);
        byte[] GetOmsExporterSecret();
    }

    internal interface IS7TlsConnectorCallback
    {
        void WriteData(byte[] data, int dataLength);
        void OnDataAvailable();
        void OnSslError(int sslError, string sslState);
    }
}
