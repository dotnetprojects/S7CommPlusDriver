using S7CommPlusDriver.Alarming;
using S7CommPlusDriver.ClientApi;
using System.Collections.Generic;

namespace S7CommPlusDriver
{
    internal interface IS7CommPlusSession
    {
        bool IsConnected { get; }
        string LastErrorDetail { get; }
        int Connect(S7CommPlusClientOptions options);
        int Disconnect(int timeoutMilliseconds);
        int CloseTransport(int timeoutMilliseconds);
        int Legitimate(string password, string username);
        int BrowseVariables(out List<VarInfo> variables);
        int BrowseBlocks(out List<S7CommPlusBlockInfo> blocks);
        int GetPlcStructureXml(out S7CommPlusPlcStructureSnapshot plcStructure);
        int GetBlockContent(uint relationId, out S7CommPlusClientBlockContent blockContent);
        PlcTag GetPlcTagBySymbol(string symbol);
        int GetCpuInfo(out S7CommPlusCpuInfo cpuInfo);
        int GetCpuCultureInfo(out S7CommPlusCpuCultureInfo cultureInfo);
        int GetCommunicationResources(out S7CommPlusCommunicationResourceSnapshot resources);
        int GetActiveAlarms(out List<S7CommPlusAlarm> alarmList, int languageId);
        int ReadValues(List<ItemAddress> addresses, out List<object> values, out List<ulong> errors);
        int WriteValues(List<ItemAddress> addresses, List<PValue> values, out List<ulong> errors);
        int CreateTagSubscription(List<PlcTag> tags, ushort cycleTimeMilliseconds, short initialCreditLimit, out uint subscriptionObjectId);
        int WaitForTagSubscriptionNotifications(uint subscriptionObjectId, int timeoutMilliseconds, short creditLimitStep, out List<Notification> notifications);
        int DeleteTagSubscription(uint subscriptionObjectId);
        int CreateAlarmSubscription(uint[] languageIds, short initialCreditLimit, out uint subscriptionObjectId);
        int WaitForAlarmNotifications(uint subscriptionObjectId, int timeoutMilliseconds, short creditLimitStep, out List<Notification> notifications);
        int DeleteAlarmSubscription(uint subscriptionObjectId);
        int CreateTisWatchSubscription(S7CommPlusTisWatchRequest request, out uint subscriptionObjectId);
        int WaitForTisWatchNotifications(uint subscriptionObjectId, int timeoutMilliseconds, out List<S7CommPlusTisWatchNotification> notifications);
        string LastTisWatchDiagnostic { get; }
        int DeleteTisWatchSubscription(uint subscriptionObjectId);
    }
}
