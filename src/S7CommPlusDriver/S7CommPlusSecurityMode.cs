namespace S7CommPlusDriver
{
    public enum S7CommPlusSecurityMode
    {
        Tls = 0,
        LegacyChallenge = 1,
        Auto = 2,
        /// <summary>
        /// Explicit opt-in to unencrypted S7CommPlus without CSI/Harpo challenge authentication.
        /// Used by older S7-1200 firmware such as V2.2 that rejects InitSsl and does not present a CSI challenge.
        /// </summary>
        Plaintext = 3
    }
}
