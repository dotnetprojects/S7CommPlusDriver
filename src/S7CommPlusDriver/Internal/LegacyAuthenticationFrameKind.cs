#if HARPOS7_LEGACY_AUTH
namespace S7CommPlusDriver.Internal
{
    internal enum LegacyAuthenticationFrameKind
    {
        Auto = 0,
        S71500HighSession = 1,
        S71500LowSessionCompact = 2,
        S71500V31Compact = 3,
        PlcSimHighSession = 4
    }
}
#endif
