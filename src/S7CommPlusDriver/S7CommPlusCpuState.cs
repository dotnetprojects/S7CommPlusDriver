namespace S7CommPlusDriver
{
    public sealed class S7CommPlusCpuState
    {
        internal S7CommPlusCpuState(int rawOperatingState, S7CommPlusCpuOperatingState operatingState, int? rawStateSwitch = null, string stateSwitch = null)
        {
            RawOperatingState = rawOperatingState;
            OperatingState = operatingState;
            RawStateSwitch = rawStateSwitch;
            StateSwitch = stateSwitch;
        }

        public int RawOperatingState { get; }
        public S7CommPlusCpuOperatingState OperatingState { get; }
        public int? RawStateSwitch { get; }
        public string StateSwitch { get; }
        public bool IsRun => OperatingState == S7CommPlusCpuOperatingState.Run;
        public bool IsStop => OperatingState == S7CommPlusCpuOperatingState.Stop;
    }
}
