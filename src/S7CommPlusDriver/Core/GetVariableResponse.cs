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
using System.IO;

namespace S7CommPlusDriver
{
    internal class GetVariableResponse : IS7pResponse
    {
        public byte TransportFlags;
        public UInt64 ReturnValue;
        public PValue Value;

        public byte ProtocolVersion { get; set; }
        public ushort FunctionCode { get => Functioncode.GetVariable; }
        public ushort SequenceNumber { get; set; }
        public uint IntegrityId { get; set; }
        public bool WithIntegrityId { get; set; }

        public GetVariableResponse(byte protocolVersion)
        {
            ProtocolVersion = protocolVersion;
            WithIntegrityId = true;
        }

        public int Deserialize(Stream buffer)
        {
            int ret = 0;

            ret += S7p.DecodeUInt16(buffer, out ushort seqnr);
            SequenceNumber = seqnr;
            ret += S7p.DecodeByte(buffer, out TransportFlags);

            ret += S7p.DecodeUInt64Vlq(buffer, out ReturnValue);

            var valueStart = buffer.Position;
            if (!TryDeserializeValue(buffer, out Value))
            {
                buffer.Position = valueStart;
                S7p.DecodeByte(buffer, out _);
                ret++;
                if (!TryDeserializeValue(buffer, out Value))
                {
                    Value = null;
                }
            }

            ret += S7p.DecodeUInt32Vlq(buffer, out uint iid);
            IntegrityId = iid;
            return ret;
        }

        private static bool TryDeserializeValue(Stream buffer, out PValue value)
        {
            value = null;
            var start = buffer.Position;
            try
            {
                value = PValue.Deserialize(buffer);
                return value != null;
            }
            catch
            {
                buffer.Position = start;
                return false;
            }
        }

        public override string ToString()
        {
            string s = "";
            s += "<GetVariableResponse>" + Environment.NewLine;
            s += "<ProtocolVersion>" + ProtocolVersion.ToString() + "</ProtocolVersion>" + Environment.NewLine;
            s += "<SequenceNumber>" + SequenceNumber.ToString() + "</SequenceNumber>" + Environment.NewLine;
            s += "<TransportFlags>" + TransportFlags.ToString() + "</TransportFlags>" + Environment.NewLine;
            s += "<ResponseSet>" + Environment.NewLine;
            s += "<ReturnValue>" + ReturnValue.ToString() + "</ReturnValue>" + Environment.NewLine;
            if (Value != null)
            {
                s += Value.ToString() + Environment.NewLine;
            }
            s += "</ResponseSet>" + Environment.NewLine;
            s += "<IntegrityId>" + IntegrityId.ToString() + "</IntegrityId>" + Environment.NewLine;
            s += "</GetVariableResponse>" + Environment.NewLine;
            return s;
        }

        public static GetVariableResponse DeserializeFromPdu(Stream pdu)
        {
            S7p.DecodeByte(pdu, out byte protocolVersion);
            S7p.DecodeByte(pdu, out byte opcode);
            if (opcode != Opcode.Response)
            {
                return null;
            }

            S7p.DecodeUInt16(pdu, out _);
            S7p.DecodeUInt16(pdu, out ushort function);
            S7p.DecodeUInt16(pdu, out _);
            if (function != Functioncode.GetVariable)
            {
                return null;
            }

            GetVariableResponse resp = new GetVariableResponse(protocolVersion);
            resp.Deserialize(pdu);

            return resp;
        }
    }
}
