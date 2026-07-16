using System;

namespace S7CommPlusDriver
{
    internal interface IS7Transport : IDisposable
    {
        bool Connected { get; }
        int Connect(string address, int port, int connectTimeoutMilliseconds, int receiveTimeoutMilliseconds, int sendTimeoutMilliseconds);

        /// <summary>
        /// Reconfigures receive and send deadlines after connection establishment without replacing the active transport.
        /// </summary>
        /// <param name="receiveTimeoutMilliseconds">Maximum wait for the next socket receive operation.</param>
        /// <param name="sendTimeoutMilliseconds">Maximum wait for the next socket send operation.</param>
        void SetTimeouts(int receiveTimeoutMilliseconds, int sendTimeoutMilliseconds);
        int Send(byte[] buffer);
        int Send(byte[] buffer, int size);
        int Receive(byte[] buffer, int start, int size);
        int Close();
    }
}
