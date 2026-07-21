#region License
/******************************************************************************
 * S7CommPlusDriver
 *
 * Copyright (C) 2023 Thomas Wiens, th.wiens@gmx.de
 *
 * This file is part of S7CommPlusDriver.
 *
 * S7CommPlusDriver is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as
 * published by the Free Software Foundation, either version 3 of the
 * License, or (at your option) any later version.
 /****************************************************************************/
#endregion

using S7CommPlusDriver.Alarming;
using S7CommPlusDriver.Internal;
using System;
using System.Collections.Generic;

namespace S7CommPlusDriver
{
    internal sealed class S7CommPlusAlarmSubscriptionService
    {
        private readonly IS7CommPlusProtocolSession _session;
        private readonly S7CommPlusProtocolRequests _requests;

        public string LastDiagnostic { get; private set; } = "";

        public S7CommPlusAlarmSubscriptionService(IS7CommPlusProtocolSession session)
        {
            _session = session;
            _requests = new S7CommPlusProtocolRequests(session);
        }

        // Example code for testing:
        // CultureInfo ci = new CultureInfo("en-US");
        // client.SubscribeAlarmsAsync(...);
        // await subscription.Completion;
        // await subscription.DisposeAsync();

        private uint _alarmSubscriptionRelationId = S7CommPlusProtocolConstants.SubscriptionRelationIdStart;
        private readonly Dictionary<uint, AlarmSubscriptionState> _subscriptions = new Dictionary<uint, AlarmSubscriptionState>();

        private sealed class AlarmSubscriptionState
        {
            public short NextCreditLimit { get; set; }
        }

        public int Create(uint[] languageIds)
        {
            return Create(languageIds, S7CommPlusProtocolConstants.DefaultSubscriptionCreditLimit, out _);
        }

        public int Create(uint[] languageIds, short initialCreditLimit, out uint subscriptionObjectId)
        {
            subscriptionObjectId = 0;
            LastDiagnostic = "";
            int res;
            languageIds ??= Array.Empty<uint>();
            var state = new AlarmSubscriptionState { NextCreditLimit = initialCreditLimit };
            var subscriptionRelationId = _alarmSubscriptionRelationId++;
            PObject subsobj = new PObject();
            subsobj.ClassId = Ids.ClassSubscription;
            subsobj.RelationId = subscriptionRelationId;
            subsobj.AddAttribute(Ids.ObjectVariableTypeName, new ValueWString("Subscription_" + subscriptionRelationId.ToString()));
            subsobj.AddAttribute(Ids.SubscriptionFunctionClassId, new ValueUSInt((byte)SubscriptionFunctionClass.Alarms));
            subsobj.AddAttribute(Ids.SubscriptionMissedSendings, new ValueUInt(0));
            subsobj.AddAttribute(Ids.SubscriptionSubsystemError, new ValueLInt(0));
            subsobj.AddAttribute(Ids.SubscriptionRouteMode, new ValueUSInt((byte)SubscriptionRouteMode.Alarm));
            subsobj.AddAttribute(Ids.SubscriptionActive, new ValueBool(true));
            subsobj.AddAttribute(Ids.SubscriptionReferenceList, new ValueUDIntArray(new uint[3] { S7CommPlusProtocolConstants.AlarmSubscriptionReferenceListHeader, 0, 0 }, S7CommPlusProtocolConstants.ValueAddressArrayFlag));
            subsobj.AddAttribute(Ids.SubscriptionCycleTime, new ValueUDInt(0));
            subsobj.AddAttribute(Ids.SubscriptionDelayTime, new ValueUDInt(0));
            subsobj.AddAttribute(Ids.SubscriptionDisabled, new ValueUSInt(0));
            subsobj.AddAttribute(Ids.SubscriptionCount, new ValueUSInt(0));
            subsobj.AddAttribute(Ids.SubscriptionCreditLimit, new ValueInt(state.NextCreditLimit)); // -1=unlimited, 255 = max
            subsobj.AddAttribute(Ids.SubscriptionTicks, new ValueUInt(S7CommPlusProtocolConstants.SubscriptionTicksUnlimited));
            PObject asrefsobj = new PObject();
            asrefsobj.ClassId = Ids.AlarmSubscriptionRef_Class_Rid;
            // Alarm-reference relation IDs are global on the PLC rather than scoped to one client session. Asking the
            // PLC to allocate the ID prevents independent HMI clients from repeatedly colliding on a fixed relation ID.
            asrefsobj.RelationId = Ids.GetNewRIDOnServer;
            asrefsobj.AddAttribute(Ids.ObjectVariableTypeName, new ValueWString(S7CommPlusProtocolConstants.AlarmSubscriptionName));
            asrefsobj.AddAttribute(Ids.SubscriptionReferenceMode, new ValueUSInt(S7CommPlusProtocolConstants.AlarmSubscriptionTriggerAndTransmitMode));
            asrefsobj.AddAttribute(Ids.AlarmSubSystem_AlarmDomain, new ValueUIntArray(new ushort[10] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, S7CommPlusProtocolConstants.ValueArrayFlag));
            // Also variant to set explicit the alarm domain as filter, for example:
            // {1, 256, 257, 258, 259, 260, 261, 262, 263, 264, 265, 266, 267, 268, 269, 270, 271, 272}
            // ASOM calls 7731 AlarmDomain2; observed value 65535 subscribes to all alarm domains.
            asrefsobj.AddAttribute(Ids.AlarmSubscr_AlarmDomain2, new ValueUIntArray(new ushort[1] { S7CommPlusProtocolConstants.AlarmDomainAll }, S7CommPlusProtocolConstants.ValueAddressArrayFlag));
            // OPTION:
            // Send text informations with the message, we don't need to browse them in advance.
            // PLCcom names AID 8181 AlarmSubscr_AlarmTextLangIdentifier; an empty array requests all text languages.
            asrefsobj.AddAttribute(Ids.AlarmSubscr_AlarmTextLangIdentifier, new ValueUDIntArray(languageIds, S7CommPlusProtocolConstants.ValueAddressArrayFlag)); // Empty for all languages. Otherwise e.g. 1031 for de-DE or the requested language.
            asrefsobj.AddAttribute(Ids.AlarmSubscr_SendAlarmTextIdentifier, new ValueBool(true));

            asrefsobj.AddRelation(Ids.AlarmSubscriptionRef_itsAlarmSubsystem, Ids.NativeObjects_theAlarmSubsystem_Rid);
            subsobj.AddObject(asrefsobj);
            // Build the request object
            var createObjReq = new CreateObjectRequest(ProtocolVersion.V2, 0, true);
            createObjReq.TransportFlags = S7CommPlusProtocolConstants.RequestWithResponseTransportFlags;
            createObjReq.RequestId = _session.SessionId2;
            createObjReq.RequestValue = new ValueUDInt(0);
            createObjReq.SetRequestObject(subsobj);

            res = _requests.CreateObject(createObjReq, out var createObjRes);
            if (res != 0)
            {
                _session.DisconnectTransport();
                return res;
            }

            if (createObjRes.ReturnValue == 0)
            {
                subscriptionObjectId = createObjRes.ObjectIds[0];
                _subscriptions[subscriptionObjectId] = state;
            }
            else
            {
                // If creating a subscription fails, the object is still created and should be deleted.
                // At least deleting it, gives no error.
                LastDiagnostic = String.Format("Create failed with Returnvalue = 0x{0:X8}", createObjRes.ReturnValue);
                System.Diagnostics.Trace.WriteLine("AlarmSubscription - " + LastDiagnostic);
                res = S7Consts.errCliInvalidParams;
            }

            return res;
        }

        public int WaitForNotifications(int waitTimeout, out List<Notification> notifications)
        {
            return WaitForNotifications(0, waitTimeout, S7CommPlusProtocolConstants.DefaultSubscriptionCreditLimitStep, out notifications);
        }

        public int WaitForNotifications(uint subscriptionObjectId, int waitTimeout, short creditLimitStep, out List<Notification> notifications)
        {
            notifications = new List<Notification>();
            if (!_subscriptions.TryGetValue(subscriptionObjectId, out var state))
            {
                return S7Consts.errCliInvalidParams;
            }

            var result = _requests.WaitNotification(subscriptionObjectId, waitTimeout, out var noti);
            if (result != 0)
            {
                return result;
            }

            notifications.Add(noti);

            if (creditLimitStep > 0 && noti.NotificationCreditTick >= state.NextCreditLimit - 1) // Set new limit one tick before it expires, to get a constant flow of data
            {
                // CreditTick in Notification is only one byte
                state.NextCreditLimit = (short)((state.NextCreditLimit + creditLimitStep) % 255);
                if (state.NextCreditLimit == 0)
                {
                    state.NextCreditLimit = creditLimitStep;
                }
                return _requests.SetSubscriptionCreditLimit(subscriptionObjectId, state.NextCreditLimit);
            }

            return 0;
        }

        public int Delete(uint subscriptionObjectId)
        {
            int res;
            if (subscriptionObjectId == 0)
            {
                return 0;
            }

            _subscriptions.Remove(subscriptionObjectId);
            System.Diagnostics.Trace.WriteLine(String.Format("AlarmSubscriptionDelete: Calling DeleteObject for SubscriptionObjectId={0:X8}", subscriptionObjectId));
            res = _session.DeleteObject(subscriptionObjectId);
            return res;
        }
    }
}
