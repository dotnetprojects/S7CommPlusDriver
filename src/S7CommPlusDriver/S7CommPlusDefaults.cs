using System;

namespace S7CommPlusDriver
{
    public static class S7CommPlusDefaults
    {
        public const int IsoTcpPort = 102;
        public const ushort LocalTsap = 0x0600;
        public const string RemoteTsapHmi = "SIMATIC-ROOT-HMI";
        public const string RemoteTsapEs = "SIMATIC-ROOT-ES";

        public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan DisconnectTimeout = TimeSpan.FromSeconds(2);
        public static readonly TimeSpan BrowseTimeout = TimeSpan.FromSeconds(120);
    }
}
