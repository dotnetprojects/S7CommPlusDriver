using S7CommPlusDriver.Internal;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace S7CommPlusDriver
{
    internal sealed class S7CommPlusTisWatchSubscriptionService
    {
        private const uint TisResultReferenceId = 9;
        private const uint TisNotificationCreditReferenceId = 10;
        private const uint TisEnabledActualReferenceId = 11;

        private readonly IS7CommPlusProtocolSession _session;
        private readonly S7CommPlusProtocolRequests _requests;
        private S7CommPlusTisResultModel _resultModel = new S7CommPlusTisResultModel();
        private uint _jobObjectId;
        private uint _subscriptionObjectId;
        private uint _subscriptionRefObjectId;
        private uint _pollSequenceNumber;

        public S7CommPlusTisWatchSubscriptionService(IS7CommPlusProtocolSession session)
        {
            _session = session;
            _requests = new S7CommPlusProtocolRequests(session);
        }

        public string LastDiagnostic { get; private set; } = "";

        public int Create(S7CommPlusTisWatchRequest request)
        {
            if (request == null)
                return S7Consts.errCliInvalidParams;

            request.LastLifecycleStage = "create TIS watch job";
            LastDiagnostic = "";
            _resultModel = request.ResultModel?.Clone() ?? new S7CommPlusTisResultModel();
            _pollSequenceNumber = 0;

            var job = new PObject
            {
                ClassId = Ids.TisWatchJob_Class_Rid,
                RelationId = Ids.GetNewRIDOnServer
            };
            job.AddAttribute(Ids.ObjectVariableTypeName, new ValueWString(request.JobName));
            job.AddAttribute(Ids.AbstractTisJob_Request, new ValueBlob(0, request.RequestBlob));
            job.AddAttribute(Ids.AbstractTisJob_Trigger, new ValueBlob(0, request.TriggerBlob));
            job.AddAttribute(Ids.AbstractTisJob_ModifyingJob, new ValueBool(false));

            var createJob = new CreateObjectRequest(ProtocolVersion.V2, 0, true)
            {
                TransportFlags = S7CommPlusProtocolConstants.RequestWithResponseTransportFlags,
                RequestId = _session.SessionId,
                RequestValue = new ValueUDInt(0)
            };
            createJob.SetRequestObject(job);

            var res = _requests.CreateObject(createJob, out var createJobResponse);
            if (res != 0)
            {
                _session.DisconnectTransport();
                return res;
            }

            if (createJobResponse.ReturnValue != 0 || createJobResponse.ObjectIds.Count == 0)
            {
                return S7Consts.errCliInvalidParams;
            }

            _jobObjectId = createJobResponse.ObjectIds[0];

            request.LastLifecycleStage = "create TIS watch subscription";
            res = CreateSubscription(request.JobName);
            if (res != 0)
            {
                CleanupAfterFailedCreate();
                return res;
            }

            request.LastLifecycleStage = "enable TIS watch job and add notification credit";
            res = _requests.SetMultiVariablesRaw(
                0,
                new uint[]
                {
                    0, _jobObjectId, 1, Ids.AbstractTisJob_TisJobEnabledConf,
                    0, _subscriptionRefObjectId, 1, Ids.TisSubscriptionRef_IncrementNotificationCredit
                },
                new PValue[]
                {
                    new ValueBool(true),
                    new ValueUSInt(1)
                });
            if (res != 0)
            {
                CleanupAfterFailedCreate();
                return res;
            }

            request.LastLifecycleStage = "started";
            return 0;
        }

        private void CleanupAfterFailedCreate()
        {
            if (_subscriptionObjectId != 0)
            {
                _session.DeleteObject(_subscriptionObjectId);
                _subscriptionObjectId = 0;
                _subscriptionRefObjectId = 0;
            }

            if (_jobObjectId != 0)
            {
                _session.DeleteObject(_jobObjectId);
                _jobObjectId = 0;
            }
        }

        private int CreateSubscription(string jobName)
        {
            var subscription = new PObject
            {
                ClassId = Ids.ClassSubscription,
                RelationId = S7CommPlusProtocolConstants.SubscriptionRelationIdStart
            };
            subscription.AddAttribute(Ids.ObjectVariableTypeName, new ValueWString("Subscription_" + jobName));
            subscription.AddAttribute(Ids.SubscriptionFunctionClassId, new ValueUSInt((byte)SubscriptionFunctionClass.Tis));
            subscription.AddAttribute(Ids.SubscriptionMissedSendings, new ValueUInt(0));
            subscription.AddAttribute(Ids.SubscriptionSubsystemError, new ValueLInt(0));
            subscription.AddAttribute(Ids.SubscriptionRouteMode, new ValueUSInt((byte)SubscriptionRouteMode.Tis));
            subscription.AddAttribute(Ids.SubscriptionActive, new ValueBool(true));
            subscription.AddAttribute(Ids.SubscriptionReferenceList, CreateSubscriptionReferenceList());
            subscription.AddAttribute(Ids.SubscriptionCycleTime, new ValueUDInt(0));
            subscription.AddAttribute(Ids.SubscriptionDisabled, new ValueUSInt(0));
            subscription.AddAttribute(Ids.SubscriptionCount, new ValueUSInt(0));
            subscription.AddAttribute(Ids.SubscriptionCreditLimit, new ValueInt(-1));
            subscription.AddAttribute(Ids.SubscriptionTicks, new ValueUInt(S7CommPlusProtocolConstants.SubscriptionTicksUnlimited));
            subscription.AddAttribute(S7CommPlusProtocolConstants.SubscriptionDefaultAttribute1055, new ValueUSInt(0));

            var subscriptionRef = new PObject
            {
                ClassId = Ids.TisSubscriptionRef_Class_Rid,
                RelationId = Ids.GetNewRIDOnServer
            };
            subscriptionRef.AddAttribute(Ids.ObjectVariableTypeName, new ValueWString("TisSubscriptionRef_" + jobName));
            subscriptionRef.AddAttribute(Ids.SubscriptionReferenceMode, new ValueUSInt(1));
            subscriptionRef.AddAttribute(Ids.TisSubscriptionRef_IncrementNotificationCredit, new ValueUSInt(0));
            subscriptionRef.AddRelation(Ids.TisSubscriptionRef_itsAssumingJob, _jobObjectId);
            subscription.AddObject(subscriptionRef);

            var createSubscription = new CreateObjectRequest(ProtocolVersion.V2, 0, true)
            {
                TransportFlags = S7CommPlusProtocolConstants.RequestWithResponseTransportFlags,
                RequestId = _session.SessionId2,
                RequestValue = new ValueUDInt(0)
            };
            createSubscription.SetRequestObject(subscription);

            var res = _requests.CreateObject(createSubscription, out var response);
            if (res != 0)
            {
                _session.DisconnectTransport();
                return res;
            }

            if (response.ReturnValue != 0 || response.ObjectIds.Count == 0)
            {
                return S7Consts.errCliInvalidParams;
            }

            _subscriptionObjectId = response.ObjectIds[0];
            _subscriptionRefObjectId = response.ObjectIds.Count > 1 ? response.ObjectIds[1] : 0;
            return _subscriptionRefObjectId == 0 ? S7Consts.errCliInvalidParams : 0;
        }

        private ValueUDIntArray CreateSubscriptionReferenceList()
        {
            var values = new[]
            {
                0x80010000u, 0u, 3u,
                0x80120001u, TisResultReferenceId, _session.SessionId, _jobObjectId, 0u, (uint)Ids.AbstractTisJob_Result,
                0x80120001u, TisNotificationCreditReferenceId, _session.SessionId, _jobObjectId, 0u, (uint)Ids.AbstractTisJob_NotificationCredit,
                0x80120001u, TisEnabledActualReferenceId, _session.SessionId, _jobObjectId, 0u, (uint)Ids.AbstractTisJob_TisJobEnabledActual,
            };
            return new ValueUDIntArray(values, S7CommPlusProtocolConstants.ValueAddressArrayFlag);
        }

        public int WaitForNotifications(int waitTimeout, out List<S7CommPlusTisWatchNotification> notifications)
        {
            notifications = new List<S7CommPlusTisWatchNotification>();
            LastDiagnostic = $"waiting for TIS notification timeout={waitTimeout}ms";

            var result = _requests.WaitNotification(waitTimeout, out var notification);
            LastDiagnostic = $"WaitNotification returned {result}";
            if (result != 0)
            {
                if (TryPollResultBuffer(out var polledNotification))
                {
                    notifications.Add(polledNotification);
                    if (_subscriptionRefObjectId != 0)
                    {
                        _requests.SetVariable(_subscriptionRefObjectId, Ids.TisSubscriptionRef_IncrementNotificationCredit, new ValueUSInt(1));
                    }
                    return 0;
                }

                return result;
            }

            notifications.Add(ConvertNotification(notification));
            return _requests.SetVariable(_subscriptionRefObjectId, Ids.TisSubscriptionRef_IncrementNotificationCredit, new ValueUSInt(1));
        }

        private bool TryPollResultBuffer(out S7CommPlusTisWatchNotification notification)
        {
            notification = null;
            if (_jobObjectId == 0)
            {
                LastDiagnostic = "poll skipped: no TIS job RID";
                return false;
            }

            LastDiagnostic = $"polling result buffer with GetVariable(job={_jobObjectId}, aid={Ids.AbstractTisJob_Result})";
            var result = _requests.GetVariable(_jobObjectId, (uint)Ids.AbstractTisJob_Result, out var value);
            if (result != 0)
            {
                LastDiagnostic = $"GetVariable(job={_jobObjectId}, aid={Ids.AbstractTisJob_Result}) failed with {result}";
                result = _requests.GetVarSubstreamed(_jobObjectId, Ids.AbstractTisJob_Result, out value);
            }

            if (result != 0)
            {
                LastDiagnostic += $"; GetVarSubstreamed fallback failed with {result}";
                return false;
            }

            var rawResult = ExtractBlob(value);
            if (rawResult.Length == 0)
            {
                LastDiagnostic = $"poll returned {value?.GetType().Name ?? "<null>"} with empty result blob";
                return false;
            }

            LastDiagnostic = $"poll returned {rawResult.Length} result bytes";
            notification = new S7CommPlusTisWatchNotification(
                DateTime.UtcNow,
                ++_pollSequenceNumber,
                0,
                null,
                null,
                rawResult,
                S7CommPlusTisResultParser.Parse(rawResult, _resultModel));
            return true;
        }

        private S7CommPlusTisWatchNotification ConvertNotification(Notification notification)
        {
            notification.Values.TryGetValue(TisEnabledActualReferenceId, out var enabledValue);
            notification.Values.TryGetValue(TisNotificationCreditReferenceId, out var creditValue);
            notification.Values.TryGetValue(TisResultReferenceId, out var resultValue);

            var rawResult = ExtractBlob(resultValue);
            var watchPoints = S7CommPlusTisResultParser.Parse(rawResult, _resultModel);
            return new S7CommPlusTisWatchNotification(
                notification.Add1Timestamp,
                notification.NotificationSequenceNumber,
                notification.NotificationCreditTick,
                ExtractBool(enabledValue),
                ExtractByte(creditValue),
                rawResult,
                watchPoints);
        }

        public int Delete()
        {
            var result = 0;
            if (_subscriptionObjectId != 0)
            {
                _requests.SetVariable(_subscriptionObjectId, Ids.SubscriptionDisabled, new ValueUSInt(1));
                var deleteSubscriptionResult = _session.DeleteObject(_subscriptionObjectId);
                if (result == 0)
                    result = deleteSubscriptionResult;
                _subscriptionObjectId = 0;
                _subscriptionRefObjectId = 0;
            }

            if (_jobObjectId != 0)
            {
                var deleteJobResult = _session.DeleteObject(_jobObjectId);
                if (result == 0)
                    result = deleteJobResult;
                _jobObjectId = 0;
            }

            return result;
        }

        private static bool? ExtractBool(PValue value)
        {
            return value is ValueBool boolValue ? boolValue.GetValue() : null;
        }

        private static byte? ExtractByte(PValue value)
        {
            return value is ValueUSInt byteValue ? byteValue.GetValue() : null;
        }

        private static byte[] ExtractBlob(PValue value)
        {
            if (value is ValueBlob blob)
                return blob.GetValue() ?? Array.Empty<byte>();

            if (value is ValueBlobSparseArray sparseArray)
            {
                var sparse = sparseArray.GetValue();
                foreach (var key in sparse.Keys.OrderBy(x => x))
                {
                    var data = sparse[key].value;
                    if (data != null && data.Length > 0)
                        return data;
                }
            }

            return Array.Empty<byte>();
        }
    }

    internal static class S7CommPlusTisResultParser
    {
        public static IReadOnlyList<S7CommPlusTisWatchPointResult> Parse(byte[] result, S7CommPlusTisResultModel model)
        {
            var output = new List<S7CommPlusTisWatchPointResult>();
            if (result == null || model == null)
                return output;

            foreach (var watchPoint in model.WatchPoints)
            {
                var rawWord = TryReadUInt32(result, watchPoint.RloOffset, out var word) ? word : 0;
                var executed = rawWord != 0;
                var rlo = executed ? (bool?)((rawWord & 0x80000000u) != 0) : null;
                var executionCount = rawWord & 0x3fffffffu;
                var values = watchPoint.Values
                    .Select(value => ParseValue(result, value))
                    .ToList();

                output.Add(new S7CommPlusTisWatchPointResult(
                    watchPoint.NetworkId,
                    watchPoint.Uid,
                    watchPoint.Pin,
                    rlo,
                    rawWord,
                    executionCount,
                    values));
            }

            return output;
        }

        private static S7CommPlusTisValueResult ParseValue(byte[] result, S7CommPlusTisValueModel model)
        {
            var length = Math.Max(0, model.ByteLength);
            var raw = new byte[length];
            if (model.ValueOffset >= 0 && model.ValueOffset + length <= result.Length)
                Array.Copy(result, model.ValueOffset, raw, 0, length);

            byte? validity = model.ValidityOffset >= 0 && model.ValidityOffset < result.Length
                ? result[model.ValidityOffset]
                : null;
            ushort? validCount = TryReadUInt16(result, model.ValidCountOffset, out var count) ? count : null;
            bool? boolValue = IsBoolDataType(model.DataType) || (String.IsNullOrWhiteSpace(model.DataType) && length == 1)
                ? raw.Any(item => item != 0)
                : null;
            if (boolValue.HasValue && model.InvertBool)
                boolValue = !boolValue.Value;

            return new S7CommPlusTisValueResult(
                model.NetworkId,
                model.Uid,
                model.Pin,
                model.DataType,
                raw,
                boolValue,
                validity,
                validCount);
        }

        private static bool IsBoolDataType(string type)
        {
            if (String.IsNullOrWhiteSpace(type))
                return false;

            var decoded = type.Replace("&quot;", "\"", StringComparison.OrdinalIgnoreCase);
            return decoded.IndexOf("Bool", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   decoded.IndexOf("Boolean", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryReadUInt32(byte[] data, int offset, out uint value)
        {
            if (offset >= 0 && offset + 4 <= data.Length)
            {
                value = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
                return true;
            }

            value = 0;
            return false;
        }

        private static bool TryReadUInt16(byte[] data, int offset, out ushort value)
        {
            if (offset >= 0 && offset + 2 <= data.Length)
            {
                value = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));
                return true;
            }

            value = 0;
            return false;
        }
    }
}
