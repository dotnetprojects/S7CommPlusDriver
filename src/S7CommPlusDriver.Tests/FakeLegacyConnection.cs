using S7CommPlusDriver.ClientApi;
using S7CommPlusDriver.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace S7CommPlusDriver.Tests
{
    internal sealed class FakeLegacyConnection : ILegacyS7CommPlusConnection
    {
        public bool IsConnected { get; private set; }
        public int ConnectCount { get; private set; }
        public int DisconnectCount { get; private set; }
        public int ReadCount { get; private set; }
        public int MaxConcurrentReads { get; private set; }
        public int ActiveReads;

        public Func<S7CommPlusClientOptions, int>? ConnectHandler { get; set; }
        public Func<int, int>? DisconnectHandler { get; set; }
        public Func<(int Error, List<VarInfo> Vars)>? BrowseHandler { get; set; }
        public Func<string, PlcTag>? GetTagHandler { get; set; }
        public Func<(int Error, S7CommPlusConnection.CpuInfo CpuInfo)>? CpuInfoHandler { get; set; }
        public Func<List<ItemAddress>, (int Error, List<object?> Values, List<ulong> Errors)>? ReadHandler { get; set; }
        public Func<List<ItemAddress>, List<PValue>, (int Error, List<ulong> Errors)>? WriteHandler { get; set; }

        public int Connect(S7CommPlusClientOptions options)
        {
            ConnectCount++;
            var error = ConnectHandler?.Invoke(options) ?? 0;
            IsConnected = error == 0;
            return error;
        }

        public int Disconnect(int timeoutMilliseconds)
        {
            DisconnectCount++;
            IsConnected = false;
            return DisconnectHandler?.Invoke(timeoutMilliseconds) ?? 0;
        }

        public int Browse(out List<VarInfo> varInfoList)
        {
            var result = BrowseHandler?.Invoke() ?? (0, new List<VarInfo>());
            varInfoList = result.Vars;
            return result.Error;
        }

        public PlcTag GetPlcTagBySymbol(string symbol)
        {
            return GetTagHandler?.Invoke(symbol) ?? PlcTags.TagFactory(symbol, new ItemAddress("8A0E0001.F"), Softdatatype.S7COMMP_SOFTDATATYPE_INT);
        }

        public int GetCpuInfos(out S7CommPlusConnection.CpuInfo cpuInfo)
        {
            var result = CpuInfoHandler?.Invoke() ?? (0, new S7CommPlusConnection.CpuInfo { PlcName = "TestCpu" });
            cpuInfo = result.CpuInfo;
            return result.Error;
        }

        public int ReadValues(List<ItemAddress> addresslist, out List<object> values, out List<ulong> errors)
        {
            ReadCount++;
            var active = Interlocked.Increment(ref ActiveReads);
            MaxConcurrentReads = Math.Max(MaxConcurrentReads, active);
            try
            {
                var result = ReadHandler?.Invoke(addresslist)
                    ?? (0, new List<object?> { new ValueInt(123) }, new List<ulong> { 0 });
                values = result.Values.Cast<object>().ToList();
                errors = result.Errors;
                if (result.Error != 0)
                {
                    IsConnected = false;
                }
                return result.Error;
            }
            finally
            {
                Interlocked.Decrement(ref ActiveReads);
            }
        }

        public int WriteValues(List<ItemAddress> addresslist, List<PValue> values, out List<ulong> errors)
        {
            var result = WriteHandler?.Invoke(addresslist, values)
                ?? (0, new List<ulong>(new ulong[addresslist.Count]));
            errors = result.Errors;
            return result.Error;
        }
    }
}
