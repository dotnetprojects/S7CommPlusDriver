namespace S7CommPlusDriver
{
    public sealed class S7CommPlusCpuState
    {
        internal S7CommPlusCpuState(int rawOperatingState, S7CommPlusCpuOperatingState operatingState)
        {
            RawOperatingState = rawOperatingState;
            OperatingState = operatingState;
        }

        public int RawOperatingState { get; }
        public S7CommPlusCpuOperatingState OperatingState { get; }
        public bool IsRun => OperatingState == S7CommPlusCpuOperatingState.Run;
        public bool IsStop => OperatingState == S7CommPlusCpuOperatingState.Stop;
    }
}
