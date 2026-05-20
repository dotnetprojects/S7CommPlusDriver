namespace S7CommPlusDriver
{
    public enum S7CommPlusNotificationReturnCode : byte
    {
        EndOfList = 0x00,
        AddressingError = 0x03,
        AddressingError1200 = 0x13,
        AlarmObject = 0x81,
        LegacyValue = 0x83,
        ValueWithUInt32Reference = 0x92,
        ValueWithVlqReference = 0x9B,
        OnlineStatusTable = 0x9C
    }
}
