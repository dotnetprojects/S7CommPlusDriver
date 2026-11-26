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
using S7CommPlusDriver.Internal;

namespace S7CommPlusDriver
{
    internal class GetVarSubstreamedRequest : IS7pRequest
    {
        public byte TransportFlags = S7CommPlusProtocolConstants.RequestWithResponseTransportFlags;

        public UInt32 InObjectId;

        public UInt16 Address;

        public uint SessionId { get; set; }
        public byte ProtocolVersion { get; set; }
        public ushort FunctionCode { get => Functioncode.GetVarSubStreamed; }
        public ushort SequenceNumber { get; set; }
        public uint IntegrityId { get; set; }
        public bool WithIntegrityId { get; set; }
        /// <summary>
        /// Older plaintext V2 firmware (e.g. S7-1200 V2.2) encodes the integrity id as a fixed UInt32
        /// and omits the V3-only unknown1 field. Modern/TLS V2 keeps the previous VLQ framing.
        /// </summary>
        public bool UseOldFirmwareV2IntegrityFraming { get; set; }

        public GetVarSubstreamedRequest(byte protocolVersion)
        {
            ProtocolVersion = protocolVersion;
            WithIntegrityId = true;
        }

        public byte GetProtocolVersion()
        {
            return ProtocolVersion;
        }

        public int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, Opcode.Request);
            ret += S7p.EncodeUInt16(buffer, 0);                               // Reserved
            ret += S7p.EncodeUInt16(buffer, FunctionCode);
            ret += S7p.EncodeUInt16(buffer, 0);                               // Reserved
            ret += S7p.EncodeUInt16(buffer, SequenceNumber);
            ret += S7p.EncodeUInt32(buffer, SessionId);
            ret += S7p.EncodeByte(buffer, TransportFlags);

            // Request set
            ret += S7p.EncodeUInt32(buffer, InObjectId);
            ret += S7p.EncodeByte(buffer, S7CommPlusProtocolConstants.ValueAddressArrayFlag);
            ret += S7p.EncodeByte(buffer, Datatype.UDInt);
            ret += S7p.EncodeByte(buffer, 1); // Array size
            ret += S7p.EncodeUInt32Vlq(buffer, Address);

            // S7-1200 FW2.2 plaintext (TIA): ObjectQualifier uses a fixed UDInt KeyQualifier=1,
            // omits the attribute-list terminator, then a fixed UInt32 integrity id (typically 0).
            if (UseOldFirmwareV2IntegrityFraming && ProtocolVersion == S7CommPlusDriver.ProtocolVersion.V2)
            {
                ret += EncodeOldFirmwareObjectQualifierAndIntegrity(buffer);
                return ret;
            }

            ret += S7p.EncodeObjectQualifier(buffer);
            // 2 Bytes unknown, decoded by the Wireshark S7CommPlus dissector as
            // getvarsubstr.req_unknown1. Captured legacy V3 and modern TLS V2 traffic uses 0x0001.
            ret += S7p.EncodeUInt16(buffer, S7CommPlusProtocolConstants.GetVarSubstreamedRequestUnknown1);

            if (WithIntegrityId)
            {
                ret += S7p.EncodeUInt32Vlq(buffer, IntegrityId);
            }

            // Fill?
            ret += S7p.EncodeUInt32(buffer, 0);
            if (ProtocolVersion == S7CommPlusDriver.ProtocolVersion.V3)
            {
                ret += S7p.EncodeByte(buffer, 0);
            }

            return ret;
        }

        private int EncodeOldFirmwareObjectQualifierAndIntegrity(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeUInt32(buffer, Ids.ObjectQualifier);

            ret += S7p.EncodeUInt32Vlq(buffer, Ids.ParentRID);
            ret += new ValueRID(0).Serialize(buffer);

            ret += S7p.EncodeUInt32Vlq(buffer, Ids.CompositionAID);
            ret += new ValueAID(0).Serialize(buffer);

            ret += S7p.EncodeUInt32Vlq(buffer, Ids.KeyQualifier);
            ret += S7p.EncodeByte(buffer, 0); // flags
            ret += S7p.EncodeByte(buffer, Datatype.UDInt);
            ret += S7p.EncodeUInt32(buffer, 1); // fixed (non-VLQ) KeyQualifier value used by FW2.2

            // No 0x00 attribute-list terminator on this firmware path.
            ret += S7p.EncodeUInt32(buffer, WithIntegrityId ? IntegrityId : 0u);
            return ret;
        }

        public override string ToString()
        {
            string s = "";
            s += "<GetVarSubstreamedRequest>" + Environment.NewLine;
            s += "<ProtocolVersion>" + ProtocolVersion.ToString() + "</ProtocolVersion>" + Environment.NewLine;
            s += "<SequenceNumber>" + SequenceNumber.ToString() + "</SequenceNumber>" + Environment.NewLine;
            s += "<SessionId>" + SessionId.ToString() + "</SessionId>" + Environment.NewLine;
            s += "<TransportFlags>" + TransportFlags.ToString() + "</TransportFlags>" + Environment.NewLine;
            s += "<RequestSet>" + Environment.NewLine;
            s += "<InObjectId>" + InObjectId.ToString() + "</InObjectId>" + Environment.NewLine;
            s += "<AddressList>" + Environment.NewLine;
            s += "<Id>" + Address.ToString() + "</Id>" + Environment.NewLine;
            s += "</AddressList>" + Environment.NewLine;
            s += "<ValueList>" + Environment.NewLine;
            s += "</ValueList>" + Environment.NewLine;
            s += "</RequestSet>" + Environment.NewLine;
            s += "</GetVarSubstreamedRequest>" + Environment.NewLine;
            return s;
        }
    }
}
