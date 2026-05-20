namespace S7CommPlusDriver.Internal
{
    internal static class LegacyOmsConstants
    {
        public const string EngineeringTsap = "SIMATIC-ROOT-ES";
        public const string HmiTsap = "SIMATIC-ROOT-HMI";

        public const uint HighSessionIdStart = 0x70000000;
        public const int ChallengeLength = 20;
        public const int PacketDigestLength = 32;
        public const int PacketDigestFieldLength = PacketDigestLength + 1;

        public const int ServerSessionRole = 299;
        public const int ServerSessionChallenge = 303;
        public const int SessionServerChallenge = ServerSessionChallenge;
        public const int ServerSessionResponse = 304;
        public const int ServerSessionRoles = 305;
        public const int ServerSessionClientVersion = 306;
        public const int ClientSessionPassword = 309;
        public const int ClientSessionLegitimated = 310;
        public const int ClientSessionCommunicationFormat = 311;
        public const int LidSessionVersionStruct = 314;
        public const int LidSessionVersionSystemOms = 315;
        public const int LidSessionVersionProjectOms = 316;
        public const int LidSessionVersionSystemPaom = 317;
        public const int LidSessionVersionProjectPaom = 318;
        public const int LidSessionVersionSystemPaomString = 319;
        public const int LidSessionVersionProjectPaomString = 320;
        public const int LidSessionVersionProjectFormat = 321;
        public const int StructSecurityKey = 1800;
        public const int SecurityKeyVersion = 1801;
        public const int SecurityKeySecurityLevel = 1802;
        public const int SecurityKeyPublicKeyId = 1803;
        public const int SecurityKeySymmetricKeyId = 1804;
        public const int SecurityKeyEncryptedKey = 1805;
        public const int EncryptionData = 1811;
        public const int StructMac = 1820;
        public const int MacAlgorithm = 1821;
        public const int MacEncryptedKey = 1822;
        public const int MacData = 1823;
        public const int SecurityKeyId = 1825;
        public const int SecurityKeyIdValue = 1826;
        public const int SecurityKeyIdFlags = 1827;
        public const int SecurityKeyIdInternalFlags = 1828;
        public const int ServerSessionSessionKey = 1830;
        public const int SessionKey = ServerSessionSessionKey;
        public const int EffectiveProtectionLevel = 1842;
        public const int CurrentPlcSecurityLevel = EffectiveProtectionLevel;
        public const int ActiveProtectionLevel = 1843;
        public const int ExpectedLegitimationLevel = 1844;
        public const int CollaborationToken = 1845;
        public const int Legitimate = 1846;
        public const int LegacySessionSecretResponse = Legitimate;
        public const int LegacyAuthenticationCompatibilityFlag = 1902;
    }

    internal enum LegacyOmsSecurityType
    {
        None = 0,
        Csi = 2,
        Tls = 4
    }

    internal enum LegacyServerSessionRole : byte
    {
        EngineeringSystem = 1,
        Hmi = 2
    }

    internal enum LegacyOmsPublicKeyType
    {
        CpuPublicKey = 2,
        FamilyPublicKey = 4,
        CommPublicKey = 16
    }

    internal enum LegacyOmsPublicKeyFamily
    {
        Cpu1500 = 0x0000,
        Cpu1200 = 0x0100,
        WinAc = 0x0200,
        VPlc = 0x0300,
        OpenSsl = 0x8000,
        Pms = 0x8200,
        NoSecurity = 0xFF00
    }
}
