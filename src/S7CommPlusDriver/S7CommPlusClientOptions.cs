using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Text;

namespace S7CommPlusDriver
{
    public sealed class S7CommPlusClientOptions
    {
        public string Address { get; set; } = string.Empty;
        public int Port { get; set; } = 102;
        public ushort LocalTsap { get; set; } = 0x0600;
        public string RemoteTsap { get; set; } = "SIMATIC-ROOT-HMI";
        public string Password { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(5);
        public TimeSpan DisconnectTimeout { get; set; } = TimeSpan.FromSeconds(2);
        public bool AutoReconnect { get; set; } = true;
        public bool WriteEnabled { get; set; } = false;
        public ILogger Logger { get; set; } = NullLogger.Instance;

        internal int ConnectTimeoutMilliseconds => ToPositiveMilliseconds(ConnectTimeout, nameof(ConnectTimeout));
        internal int RequestTimeoutMilliseconds => ToPositiveMilliseconds(RequestTimeout, nameof(RequestTimeout));
        internal int DisconnectTimeoutMilliseconds => ToPositiveMilliseconds(DisconnectTimeout, nameof(DisconnectTimeout));
        internal byte[] RemoteTsapBytes => Encoding.ASCII.GetBytes(RemoteTsap ?? string.Empty);

        internal S7CommPlusClientOptions Clone()
        {
            return (S7CommPlusClientOptions)MemberwiseClone();
        }

        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(Address))
            {
                throw new ArgumentException("PLC address is required.", nameof(Address));
            }
            if (Port <= 0 || Port > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(Port), "Port must be between 1 and 65535.");
            }
            if (RemoteTsapBytes.Length == 0)
            {
                throw new ArgumentException("Remote TSAP is required.", nameof(RemoteTsap));
            }
            _ = ConnectTimeoutMilliseconds;
            _ = RequestTimeoutMilliseconds;
            _ = DisconnectTimeoutMilliseconds;
            Logger ??= NullLogger.Instance;
        }

        private static int ToPositiveMilliseconds(TimeSpan value, string name)
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(name, "Timeout must be greater than zero.");
            }
            if (value.TotalMilliseconds > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(name, "Timeout is too large.");
            }
            return Math.Max(1, (int)value.TotalMilliseconds);
        }
    }
}
