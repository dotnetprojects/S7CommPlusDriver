using S7CommPlusDriver.ClientApi;
using System.Collections.Generic;

namespace S7CommPlusDriver.Internal
{
    internal sealed class LegacyS7CommPlusConnectionAdapter : ILegacyS7CommPlusConnection
    {
        private readonly S7CommPlusConnection _connection = new S7CommPlusConnection();

        public bool IsConnected => _connection.IsConnected;

        public int Connect(S7CommPlusClientOptions options)
        {
            return _connection.Connect(
                options.Address,
                options.Password,
                options.Username,
                options.RequestTimeoutMilliseconds,
                options.Port,
                options.LocalTsap,
                options.RemoteTsapBytes);
        }

        public int Disconnect(int timeoutMilliseconds)
        {
            return _connection.TryDisconnect(timeoutMilliseconds);
        }

        public int Browse(out List<VarInfo> varInfoList)
        {
            return _connection.Browse(out varInfoList);
        }

        public PlcTag GetPlcTagBySymbol(string symbol)
        {
            return _connection.getPlcTagBySymbol(symbol);
        }

        public int GetCpuInfos(out S7CommPlusConnection.CpuInfo cpuInfo)
        {
            return _connection.GetCpuInfos(out cpuInfo);
        }

        public int ReadValues(List<ItemAddress> addresslist, out List<object> values, out List<ulong> errors)
        {
            return _connection.ReadValues(addresslist, out values, out errors);
        }

        public int WriteValues(List<ItemAddress> addresslist, List<PValue> values, out List<ulong> errors)
        {
            return _connection.WriteValues(addresslist, values, out errors);
        }
    }
}
