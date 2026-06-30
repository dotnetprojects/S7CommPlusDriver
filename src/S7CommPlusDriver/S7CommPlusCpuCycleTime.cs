namespace S7CommPlusDriver
{
    public sealed class S7CommPlusCpuCycleTime
    {
        internal S7CommPlusCpuCycleTime(
            double? configuredMinimumMilliseconds,
            double? configuredMaximumMilliseconds,
            double shortestMilliseconds,
            double currentMilliseconds,
            double longestMilliseconds)
        {
            ConfiguredMinimumMilliseconds = configuredMinimumMilliseconds;
            ConfiguredMaximumMilliseconds = configuredMaximumMilliseconds;
            ShortestMilliseconds = shortestMilliseconds;
            CurrentMilliseconds = currentMilliseconds;
            LongestMilliseconds = longestMilliseconds;
        }

        public double? ConfiguredMinimumMilliseconds { get; }
        public double? ConfiguredMaximumMilliseconds { get; }
        public double ShortestMilliseconds { get; }
        public double CurrentMilliseconds { get; }
        public double LongestMilliseconds { get; }
    }
}
