namespace S7CommPlusDriver
{
    public enum SiemensOmsAsRole
    {
        NoRole = 0,
        Hmi = 1,
        TisModifying = 2,
        TisMonitoring = 3,
        EngineeringSystem = 4
    }

    public enum SiemensOmsProtocolType
    {
        NoProtocol = 0,
        ClassicSps7 = 1,
        S7PlusTransport = 2
    }

    public enum SiemensOmsProtectionLevel
    {
        NotConfigured = 0,
        NoProtection = 1,
        WriteProtection = 2,
        ReadWriteProtection = 3,
        CompleteProtection = 4,
        FailsafeProtection = 5
    }

    public enum SiemensOmsAlarmMessageType
    {
        Invalid = 0,
        Alarm = 1,
        Notify = 2,
        InfoReport = 3,
        EventAcknowledgement = 4
    }

    public enum SiemensOmsAlarmJobState
    {
        Waiting = 0,
        Processing = 1,
        Distributing = 2
    }

    public enum SiemensOmsAlarmEnableDisableResult
    {
        Ok = 0,
        InvalidRid = 32769,
        InvalidAlid = 32770
    }

    public static class SiemensOmsEnumNames
    {
        public static string AlarmMessageTypeName(int value) =>
            System.Enum.IsDefined(typeof(SiemensOmsAlarmMessageType), value)
                ? ((SiemensOmsAlarmMessageType)value).ToString()
                : $"Unknown({value})";
    }
}
