using System;

namespace S7CommPlusDriver
{
    internal interface IS7Transport : IDisposable
    {
        bool Connected { get; }
        int Connect(string address, int port, int connectTimeoutMilliseconds, int receiveTimeoutMilliseconds, int sendTimeoutMilliseconds);
        int Send(byte[] buffer);
        int Send(byte[] buffer, int size);
        int Receive(byte[] buffer, int start, int size);
        int Close();
    }
}
