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

using System;
using System.Net;
using System.IO;
using System.Security.Cryptography;
using S7CommPlusDriver.Internal;

namespace S7CommPlusDriver
{
    internal class CreateObjectRequest : IS7pRequest
    {
        public byte TransportFlags = S7CommPlusProtocolConstants.CreateObjectTransportFlags;
        public UInt32 RequestId;
        public PValue RequestValue;
        public PObject RequestObject;

        public uint SessionId { get; set; }
        public byte ProtocolVersion { get; set; }
        public ushort FunctionCode { get => Functioncode.CreateObject; }
        public ushort SequenceNumber { get; set; }
        public uint IntegrityId { get; set; }
        public bool WithIntegrityId { get; set; }

        public CreateObjectRequest(byte protocolVersion, UInt16 seqNum, bool withIntegrityId)
        {
            ProtocolVersion = protocolVersion;
            SequenceNumber = seqNum;
            WithIntegrityId = withIntegrityId;
        }

        public void SetRequestIdValue(UInt32 requestId, PValue requestValue)
        {
            RequestId = requestId;
            RequestValue = requestValue;
        }

        public void SetRequestObject(PObject requestObject)
        {
            RequestObject = requestObject;
        }

        public void SetNullServerSessionData()
        {
            // Initializes the data for a Nullserver Session on connection setup.
            // SessionId is set automatically to Ids.ObjectNullServerSession when this object is sent, if there's no session Id.
            TransportFlags = S7CommPlusProtocolConstants.CreateObjectTransportFlags;
            RequestId = Ids.ObjectServerSessionContainer;
            RequestValue = new ValueUDInt(0);

            RequestObject = new PObject(RID: Ids.GetNewRIDOnServer, CLSID: Ids.ClassServerSession, AID: Ids.None);
            RequestObject.AddAttribute(Ids.ServerSessionClientRID, new ValueRID(0x80c3c901));
            RequestObject.AddObject(new PObject(RID: Ids.GetNewRIDOnServer, CLSID: Ids.ClassSubscriptions, AID: Ids.None));
        }

        public void SetTiaServerSessionData(LegacyServerSessionRole role)
        {
            TransportFlags = S7CommPlusProtocolConstants.CreateObjectTransportFlags;
            RequestId = Ids.ObjectServerSessionContainer;
            RequestValue = new ValueUDInt(0);

            var clientRid = CreateClientRid();
            var host = GetHostName();
            var user = GetUserName();
            var clientName = $"{host}_{clientRid:X8}_1600.1.115.1";

            RequestObject = new PObject(RID: Ids.GetNewRIDOnServer, CLSID: Ids.ClassServerSession, AID: Ids.None);
            RequestObject.AddAttribute(Ids.ObjectVariableTypeName, new ValueWString(clientName));
            RequestObject.AddAttribute(Ids.ServerSessionClientConnectionId, new ValueWString("1:::6.0::S7CommPlusDriver.TCPIP.1"));
            RequestObject.AddAttribute(Ids.ServerSessionUser, new ValueWString(user));
            RequestObject.AddAttribute(Ids.ServerSessionApplication, new ValueWString(string.Empty));
            RequestObject.AddAttribute(Ids.ServerSessionHost, new ValueWString(host));
            RequestObject.AddAttribute(Ids.ServerSessionRole, new ValueUDInt((uint)role));
            RequestObject.AddAttribute(Ids.ServerSessionClientRID, new ValueRID(clientRid));
            RequestObject.AddAttribute(Ids.ServerSessionComment, new ValueWString(string.Empty));

            var subscriptions = new PObject(RID: Ids.GetNewRIDOnServer, CLSID: Ids.ClassSubscriptions, AID: Ids.None);
            subscriptions.AddAttribute(Ids.ObjectVariableTypeName, new ValueWString("SubscriptionContainer"));
            RequestObject.AddObject(subscriptions);
        }

        private static uint CreateClientRid()
        {
            Span<byte> bytes = stackalloc byte[4];
            RuntimeCompatibility.FillRandom(bytes);
            var value = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes);
            return value == 0 ? 1u : value;
        }

        private static string GetHostName()
        {
            try
            {
                var host = Dns.GetHostName();
                return string.IsNullOrWhiteSpace(host) ? "S7CommPlusDriver" : host;
            }
            catch
            {
                return "S7CommPlusDriver";
            }
        }

        private static string GetUserName()
        {
            var user = Environment.UserName;
            return string.IsNullOrWhiteSpace(user) ? "---" : user;
        }

        public byte GetProtocolVersion()
        {
            return ProtocolVersion;
        }

        public int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, Opcode.Request);
            ret += S7p.EncodeUInt16(buffer, 0);
            ret += S7p.EncodeUInt16(buffer, FunctionCode);
            ret += S7p.EncodeUInt16(buffer, 0);
            ret += S7p.EncodeUInt16(buffer, SequenceNumber);
            ret += S7p.EncodeUInt32(buffer, SessionId);
            ret += S7p.EncodeByte(buffer, TransportFlags);

            // Request set
            ret += S7p.EncodeUInt32(buffer, RequestId);
            ret += RequestValue.Serialize(buffer);
            ret += S7p.EncodeUInt32(buffer, 0); // Unknown value 1

            if (WithIntegrityId)
            {
                ret += S7p.EncodeUInt32Vlq(buffer, IntegrityId);
            }

            // Object 
            ret += RequestObject.Serialize(buffer);

            // Fill?
            ret += S7p.EncodeUInt32(buffer, 0);
            return ret;
        }

        public override string ToString()
        {
            string s = "";
            s += "<CreateObjectRequest>" + Environment.NewLine;
            s += "<ProtocolVersion>" + ProtocolVersion.ToString() + "</ProtocolVersion>" + Environment.NewLine;
            s += "<SequenceNumber>" + SequenceNumber.ToString() + "</SequenceNumber>" + Environment.NewLine;
            s += "<SessionId>" + SessionId.ToString() + "</SessionId>" + Environment.NewLine;
            s += "<TransportFlags>" + TransportFlags.ToString() + "</TransportFlags>" + Environment.NewLine;
            s += "<RequestSet>" + Environment.NewLine;
            s += "<RequestId>" + RequestId.ToString() + "</RequestId>" + Environment.NewLine;
            s += "<RequestValue>" + RequestValue.ToString() + "</RequestValue>" + Environment.NewLine;
            s += "<RequestObject>" + RequestObject.ToString() + "</RequestObject>" + Environment.NewLine;
            s += "</RequestSet>" + Environment.NewLine;
            s += "</CreateObjectRequest>" + Environment.NewLine;
            return s;
        }
    }
}
