namespace S7CommPlusDriver
{
    internal sealed class SocketS7Transport : IS7Transport
    {
        private MsgSocket _socket;

        public bool Connected => _socket?.Connected == true;

        public int Connect(string address, int port, int connectTimeoutMilliseconds, int receiveTimeoutMilliseconds, int sendTimeoutMilliseconds)
        {
            _socket = new MsgSocket
            {
                ConnectTimeout = connectTimeoutMilliseconds,
                ReadTimeout = receiveTimeoutMilliseconds,
                WriteTimeout = sendTimeoutMilliseconds
            };
            return _socket.Connect(address, port);
        }

        /// <summary>
        /// Applies request-phase deadlines to the current socket after its shorter connection-phase deadlines are no longer needed.
        /// </summary>
        /// <param name="receiveTimeoutMilliseconds">Maximum wait for a socket receive.</param>
        /// <param name="sendTimeoutMilliseconds">Maximum wait for a socket send.</param>
        public void SetTimeouts(int receiveTimeoutMilliseconds, int sendTimeoutMilliseconds)
        {
            if (_socket == null)
            {
                return;
            }

            _socket.ReadTimeout = receiveTimeoutMilliseconds;
            _socket.WriteTimeout = sendTimeoutMilliseconds;
        }

        public int Send(byte[] buffer)
        {
            return _socket?.Send(buffer, buffer.Length) ?? S7Consts.errTCPNotConnected;
        }

        public int Send(byte[] buffer, int size)
        {
            return _socket?.Send(buffer, size) ?? S7Consts.errTCPNotConnected;
        }

        public int Receive(byte[] buffer, int start, int size)
        {
            return _socket?.Receive(buffer, start, size) ?? S7Consts.errTCPNotConnected;
        }

        public int Close()
        {
            _socket?.Close();
            _socket = null;
            return 0;
        }

        public void Dispose()
        {
            Close();
        }
    }
}
