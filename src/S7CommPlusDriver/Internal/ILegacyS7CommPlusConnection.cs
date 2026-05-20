using S7CommPlusDriver.ClientApi;
using System.Collections.Generic;

namespace S7CommPlusDriver.Internal
{
    internal interface ILegacyS7CommPlusConnection
    {
        bool IsConnected { get; }
        int Connect(S7CommPlusClientOptions options);
        int Disconnect(int timeoutMilliseconds);
        int Browse(out List<VarInfo> varInfoList);
        PlcTag GetPlcTagBySymbol(string symbol);
        int GetCpuInfos(out S7CommPlusConnection.CpuInfo cpuInfo);
        int ReadValues(List<ItemAddress> addresslist, out List<object> values, out List<ulong> errors);
        int WriteValues(List<ItemAddress> addresslist, List<PValue> values, out List<ulong> errors);
    }
}
