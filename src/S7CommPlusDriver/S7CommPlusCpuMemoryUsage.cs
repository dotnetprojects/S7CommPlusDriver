using System;
using System.Collections.Generic;

namespace S7CommPlusDriver
{
    public sealed class S7CommPlusCpuMemoryUsage
    {
        internal S7CommPlusCpuMemoryUsage(IReadOnlyList<S7CommPlusCpuMemoryArea> areas)
        {
            Areas = areas ?? Array.Empty<S7CommPlusCpuMemoryArea>();
        }

        public IReadOnlyList<S7CommPlusCpuMemoryArea> Areas { get; }
    }

    public sealed class S7CommPlusCpuMemoryArea
    {
        internal S7CommPlusCpuMemoryArea(string key, string name, long totalBytes, long usedBytes)
        {
            Key = key ?? string.Empty;
            Name = name ?? string.Empty;
            TotalBytes = Math.Max(0, totalBytes);
            UsedBytes = Math.Max(0, usedBytes);
        }

        public string Key { get; }
        public string Name { get; }
        public long TotalBytes { get; }
        public long UsedBytes { get; }
        public long FreeBytes => Math.Max(0, TotalBytes - UsedBytes);
        public double? UsedPercent => TotalBytes > 0 ? Math.Min(100.0, UsedBytes * 100.0 / TotalBytes) : (double?)null;
        public double? FreePercent => TotalBytes > 0 ? Math.Max(0.0, FreeBytes * 100.0 / TotalBytes) : (double?)null;
    }
}
