using System;
using System.Collections.Generic;
using System.IO;

namespace S7CommPlusDriver
{
    /// <summary>Decodes a bulk read into ordered staging buffers without per-response dictionaries.</summary>
    internal sealed class GetMultiVariablesReadResponse : IS7pResponse
    {
        public byte TransportFlags;
        public ulong ReturnValue;
        public byte ProtocolVersion { get; set; }
        public ushort FunctionCode => Functioncode.GetMultiVariables;
        public ushort SequenceNumber { get; set; }
        public uint IntegrityId { get; set; }
        public bool WithIntegrityId { get; set; } = true;

        internal static GetMultiVariablesReadResponse ReadHeader(Stream pdu)
        {
            if (pdu.Length - pdu.Position < 11) return null;
            S7p.DecodeByte(pdu, out var version);
            S7p.DecodeByte(pdu, out var opcode);
            S7p.DecodeUInt16(pdu, out _);
            S7p.DecodeUInt16(pdu, out var function);
            S7p.DecodeUInt16(pdu, out _);
            S7p.DecodeUInt16(pdu, out var sequence);
            S7p.DecodeByte(pdu, out var flags);
            if (opcode != Opcode.Response || function != Functioncode.GetMultiVariables) return null;
            return new GetMultiVariablesReadResponse
            {
                ProtocolVersion = version, SequenceNumber = sequence, TransportFlags = flags
            };
        }

        // Each request owns a disjoint, initially empty range. The owner publishes
        // tag values only after the complete logical read succeeds.
        internal int ReadItems(Stream pdu, IList<object> values, IList<ulong> errors, int start, int count)
        {
            if (start < 0 || count < 0 || start > values.Count - count || start > errors.Count - count)
                throw new ArgumentOutOfRangeException(nameof(count));
            S7p.DecodeUInt64Vlq(pdu, out ReturnValue);
            S7p.DecodeUInt32Vlq(pdu, out var item);
            while (item != 0)
            {
                if (item > count) return S7Consts.errIsoInvalidPDU;
                var index = start + (int)item - 1;
                if (values[index] != null) return S7Consts.errIsoInvalidPDU;
                values[index] = PValue.Deserialize(pdu);
                errors[index] = 0;
                S7p.DecodeUInt32Vlq(pdu, out item);
            }

            S7p.DecodeUInt32Vlq(pdu, out item);
            HashSet<uint> seenErrors = null;
            while (item != 0)
            {
                if (item > count) return S7Consts.errIsoInvalidPDU;
                if (seenErrors == null) seenErrors = new HashSet<uint>();
                if (!seenErrors.Add(item)) return S7Consts.errIsoInvalidPDU;
                S7p.DecodeUInt64Vlq(pdu, out var error);
                errors[start + (int)item - 1] = error;
                S7p.DecodeUInt32Vlq(pdu, out item);
            }
            S7p.DecodeUInt32Vlq(pdu, out var integrity);
            IntegrityId = integrity;
            return 0;
        }
    }
}
