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

    /// <summary>The plaintext buffer is borrowed and valid only for the duration of the callback.</summary>
    internal interface IS7TlsPlaintextCallback
    {
        void OnPlaintextData(byte[] data, int dataLength);
    }

    /// <summary>The encrypted buffer slice is borrowed for a synchronous transport send.</summary>
    internal interface IS7TlsRecordCallback
    {
        void WriteEncryptedData(byte[] data, int offset, int count);
    }
}
