using S7CommPlusDriver.Alarming;
using S7CommPlusDriver.ClientApi;
using S7CommPlusDriver.Internal;
using System;
using System.Collections.Generic;
using System.IO;

namespace S7CommPlusDriver
{
    internal partial class S7CommPlusProtocolSession : IS7CommPlusSession
    {
        private IS7CommPlusProtocolSession _protocolSession;
        private S7CommPlusTagSubscriptionService _tagSubscriptions;
        private S7CommPlusAlarmSubscriptionService _alarmSubscriptions;
        private S7CommPlusTisWatchSubscriptionService _tisWatchSubscriptions;
        private S7CommPlusAlarmBrowseService _alarmBrowser;
        private S7CommPlusMetadataService _metadata;

        private IS7CommPlusProtocolSession ProtocolSession => _protocolSession ??= new ProtocolSessionAdapter(this);
        private S7CommPlusTagSubscriptionService TagSubscriptions => _tagSubscriptions ??= new S7CommPlusTagSubscriptionService(ProtocolSession);
        private S7CommPlusAlarmSubscriptionService AlarmSubscriptions => _alarmSubscriptions ??= new S7CommPlusAlarmSubscriptionService(ProtocolSession);
        private S7CommPlusTisWatchSubscriptionService TisWatchSubscriptions => _tisWatchSubscriptions ??= new S7CommPlusTisWatchSubscriptionService(ProtocolSession);
        private S7CommPlusAlarmBrowseService AlarmBrowser => _alarmBrowser ??= new S7CommPlusAlarmBrowseService(ProtocolSession);
        private S7CommPlusMetadataService Metadata => _metadata ??= new S7CommPlusMetadataService(ProtocolSession);

        int IS7CommPlusSession.Connect(S7CommPlusClientOptions options)
        {
            return Connect(options);
        }

        string IS7CommPlusSession.LastErrorDetail => m_LastErrorDetail;

        int IS7CommPlusSession.Disconnect(int timeoutMilliseconds)
        {
            return TryDisconnect(timeoutMilliseconds);
        }

        int IS7CommPlusSession.CloseTransport(int timeoutMilliseconds)
        {
            return CloseTransport(timeoutMilliseconds);
        }

        int IS7CommPlusSession.Legitimate(string password, string username)
        {
            return Legitimate(password, username);
        }

        int IS7CommPlusSession.BrowseVariables(out List<VarInfo> variables)
        {
            return Browse(out variables);
        }

        int IS7CommPlusSession.BrowseBlocks(out List<S7CommPlusBlockInfo> blocks)
        {
            return Metadata.BrowseBlocks(out blocks);
        }

        int IS7CommPlusSession.GetPlcStructureXml(out S7CommPlusPlcStructureSnapshot plcStructure)
        {
            return Metadata.GetPlcStructureXml(out plcStructure);
        }

        int IS7CommPlusSession.GetBlockContent(uint relationId, out S7CommPlusClientBlockContent blockContent)
        {
            return Metadata.GetBlockContent(relationId, out blockContent);
        }

        PlcTag IS7CommPlusSession.GetPlcTagBySymbol(string symbol)
        {
            return getPlcTagBySymbol(symbol);
        }

        int IS7CommPlusSession.GetCpuInfo(out S7CommPlusCpuInfo cpuInfo)
        {
            return Metadata.GetCpuInfo(out cpuInfo);
        }

        int IS7CommPlusSession.GetCpuCultureInfo(out S7CommPlusCpuCultureInfo cultureInfo)
        {
            return Metadata.GetCpuCultureInfo(out cultureInfo);
        }

        int IS7CommPlusSession.GetCommunicationResources(out S7CommPlusCommunicationResourceSnapshot resources)
        {
            return GetCommunicationResources(out resources);
        }

        int IS7CommPlusSession.GetActiveAlarms(out List<S7CommPlusAlarm> alarmList, int languageId)
        {
            return AlarmBrowser.GetActiveAlarms(out alarmList, languageId);
        }

        int IS7CommPlusSession.ReadValues(List<ItemAddress> addresses, out List<object> values, out List<ulong> errors)
        {
            return ReadValues(addresses, out values, out errors);
        }

        int IS7CommPlusSession.WriteValues(List<ItemAddress> addresses, List<PValue> values, out List<ulong> errors)
        {
            return WriteValues(addresses, values, out errors);
        }

        int IS7CommPlusSession.CreateTagSubscription(List<PlcTag> tags, ushort cycleTimeMilliseconds, short initialCreditLimit)
        {
            return TagSubscriptions.Create(tags, cycleTimeMilliseconds, initialCreditLimit);
        }

        int IS7CommPlusSession.WaitForTagSubscriptionNotifications(int timeoutMilliseconds, short creditLimitStep, out List<Notification> notifications)
        {
            return TagSubscriptions.WaitForNotifications(timeoutMilliseconds, creditLimitStep, out notifications);
        }

        int IS7CommPlusSession.DeleteTagSubscription()
        {
            return TagSubscriptions.Delete();
        }

        int IS7CommPlusSession.CreateAlarmSubscription(uint[] languageIds, short initialCreditLimit)
        {
            return AlarmSubscriptions.Create(languageIds, initialCreditLimit);
        }

        int IS7CommPlusSession.WaitForAlarmNotifications(int timeoutMilliseconds, short creditLimitStep, out List<Notification> notifications)
        {
            return AlarmSubscriptions.WaitForNotifications(timeoutMilliseconds, creditLimitStep, out notifications);
        }

        int IS7CommPlusSession.DeleteAlarmSubscription()
        {
            return AlarmSubscriptions.Delete();
        }

        int IS7CommPlusSession.CreateTisWatchSubscription(S7CommPlusTisWatchRequest request)
        {
            return TisWatchSubscriptions.Create(request);
        }

        int IS7CommPlusSession.WaitForTisWatchNotifications(int timeoutMilliseconds, out List<S7CommPlusTisWatchNotification> notifications)
        {
            return TisWatchSubscriptions.WaitForNotifications(timeoutMilliseconds, out notifications);
        }

        string IS7CommPlusSession.LastTisWatchDiagnostic => TisWatchSubscriptions.LastDiagnostic;

        int IS7CommPlusSession.DeleteTisWatchSubscription()
        {
            return TisWatchSubscriptions.Delete();
        }

        private sealed class ProtocolSessionAdapter : IS7CommPlusProtocolSession
        {
            private readonly S7CommPlusProtocolSession _connection;

            public ProtocolSessionAdapter(S7CommPlusProtocolSession connection)
            {
                _connection = connection;
            }

            public int LastError
            {
                get => _connection.m_LastError;
                set => _connection.m_LastError = value;
            }

            public int ReadTimeout => _connection.m_ReadTimeout;
            public uint SessionId => _connection.m_SessionId;
            public uint SessionId2 => _connection.m_SessionId2;
            public MemoryStream ReceivedPdu => _connection.m_ReceivedPDU;

            public int SendFunction(IS7pRequest request)
            {
                return _connection.SendS7plusFunctionObject(request);
            }

            public void WaitForPdu(int timeoutMilliseconds)
            {
                _connection.WaitForNewS7plusReceived(timeoutMilliseconds);
            }

            public int CheckResponse(IS7pRequest request, IS7pResponse response)
            {
                return _connection.checkResponseWithIntegrity(request, response);
            }

            public int DeleteObject(uint objectId)
            {
                return _connection.DeleteObject(objectId);
            }

            public void DisconnectTransport()
            {
                _connection.m_client?.Disconnect();
            }

            public void ClearSessionIds()
            {
                _connection.m_SessionId = 0;
                _connection.m_SessionId2 = 0;
            }
        }
    }
}
