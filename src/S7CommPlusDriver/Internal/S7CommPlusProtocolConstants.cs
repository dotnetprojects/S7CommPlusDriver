namespace S7CommPlusDriver.Internal
{
    internal static class S7CommPlusProtocolConstants
    {
        public const byte FrameMarker = 0x72;
        public const int DefaultIsoTpduSize = 1024;
        public const int TpktHeaderLength = 4;
        public const int CotpHeaderLength = 3;
        public const int S7CommPlusHeaderLength = 4;
        public const int S7CommPlusTrailerLength = 4;
        public const int TlsRecordHeaderLength = 5;
        public const int TlsAesGcmRecordOverhead = 17;
        public const int MaxCotpParameterLength = 255;

        public const byte RequestWithResponseTransportFlags = 0x34;
        public const byte CreateObjectTransportFlags = 0x36;
        public const byte FireAndForgetTransportFlags = 0x74;
        public const byte ValueArrayFlag = 0x10;
        public const byte ValueAddressArrayFlag = 0x20;
        public const ushort GetVarSubstreamedRequestUnknown1 = 0x0001;

        public const uint SubscriptionRelationIdStart = 0x7fffc001;
        public const uint TisSubscriptionRefRelationIdStart = 0x51020001;
        public const short DefaultSubscriptionCreditLimit = 10;
        public const short DefaultSubscriptionCreditLimitStep = 5;
        public const ushort SubscriptionTicksUnlimited = 65535;
        public const int SubscriptionDefaultAttribute1055 = 1055;
        public const uint SubscriptionListCreateFlag = 0x80000000;
        public const uint SubscriptionItemAddressHeaderFlag = 0x80040000;
        public const uint AlarmSubscriptionReferenceListHeader = 0x80010000;
        public const ushort AlarmDomainAll = 65535;
        public const byte AlarmSubscriptionTriggerAndTransmitMode = 3;
        public const string AlarmSubscriptionName = "S7pDriver_Alarming";

        public const int SystemLimitPlcSubscriptions = 0;
        public const int SystemLimitPlcAttributes = 1;
        public const int SystemLimitSubscriptionMemory = 2;
        public const int SystemLimitTagsPerReadRequest = 1000;
        public const int SystemLimitTagsPerWriteRequest = 1001;
    }

    internal enum SubscriptionFunctionClass : byte
    {
        Variables = 0,
        Tis = 1,
        Alarms = 2
    }

    internal enum SubscriptionRouteMode : byte
    {
        Alarm = 0x02,
        Tis = 0x01,
        CyclicAndChangedValues = 0x14
    }
}
