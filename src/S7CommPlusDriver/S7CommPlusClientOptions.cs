using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using S7CommPlusDriver.Internal;
using System;
using System.Text;

namespace S7CommPlusDriver
{
    public sealed class S7CommPlusClientOptions
    {
        public string Address { get; set; } = string.Empty;
        public int Port { get; set; } = S7CommPlusDefaults.IsoTcpPort;
        public ushort LocalTsap { get; set; } = S7CommPlusDefaults.LocalTsap;
        public string RemoteTsap { get; set; } = S7CommPlusDefaults.RemoteTsapHmi;
        public string Password { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public TimeSpan ConnectTimeout { get; set; } = S7CommPlusDefaults.ConnectTimeout;
        public TimeSpan RequestTimeout { get; set; } = S7CommPlusDefaults.RequestTimeout;
        public TimeSpan DisconnectTimeout { get; set; } = S7CommPlusDefaults.DisconnectTimeout;
        public TimeSpan BrowseTimeout { get; set; } = S7CommPlusDefaults.BrowseTimeout;
        public bool AutoReconnect { get; set; } = true;
        public bool WriteEnabled { get; set; } = false;
        public S7CommPlusSecurityMode SecurityMode { get; set; } = S7CommPlusSecurityMode.Tls;
        public S7CommPlusTlsBackend TlsBackend { get; set; } = S7CommPlusTlsBackend.BouncyCastle;
        public S7CommPlusSecurityMode? NegotiatedSecurityMode { get; internal set; }
        public Func<string, byte[]> LegacyPublicKeyResolver { get; set; }
        public ILogger Logger { get; set; } = NullLogger.Instance;

        internal int ConnectTimeoutMilliseconds => ToPositiveMilliseconds(ConnectTimeout, nameof(ConnectTimeout));
        internal int RequestTimeoutMilliseconds => ToPositiveMilliseconds(RequestTimeout, nameof(RequestTimeout));
        internal int DisconnectTimeoutMilliseconds => ToPositiveMilliseconds(DisconnectTimeout, nameof(DisconnectTimeout));
        internal int BrowseTimeoutMilliseconds => ToPositiveMilliseconds(BrowseTimeout, nameof(BrowseTimeout));
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
            if (string.IsNullOrWhiteSpace(RemoteTsap))
            {
                throw new ArgumentException("Remote TSAP is required.", nameof(RemoteTsap));
            }
            foreach (var character in RemoteTsap)
            {
                if (character > 0x7F)
                {
                    throw new ArgumentException("Remote TSAP must contain only ASCII characters.", nameof(RemoteTsap));
                }
            }
            if (RemoteTsapBytes.Length > S7CommPlusProtocolConstants.MaxCotpParameterLength)
            {
                throw new ArgumentOutOfRangeException(nameof(RemoteTsap), $"Remote TSAP must be {S7CommPlusProtocolConstants.MaxCotpParameterLength} bytes or shorter.");
            }
            if (!Enum.IsDefined(typeof(S7CommPlusSecurityMode), SecurityMode))
            {
                throw new ArgumentOutOfRangeException(nameof(SecurityMode), "Security mode is not supported.");
            }
            if (!Enum.IsDefined(typeof(S7CommPlusTlsBackend), TlsBackend))
            {
                throw new ArgumentOutOfRangeException(nameof(TlsBackend), "TLS backend is not supported.");
            }
#if !NET8_0_OR_GREATER
            if (SecurityMode != S7CommPlusSecurityMode.Tls)
            {
                throw new S7CommPlusUnsupportedSecurityModeException(
                    SecurityMode,
                    $"{Address}:{Port}",
                    "Legacy S7CommPlus challenge authentication is available only on net8.0 and later builds.");
            }
#endif
            _ = ConnectTimeoutMilliseconds;
            _ = RequestTimeoutMilliseconds;
            _ = DisconnectTimeoutMilliseconds;
            _ = BrowseTimeoutMilliseconds;
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
