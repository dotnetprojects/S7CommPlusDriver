using System;
using System.Collections.Generic;
using System.Text;

namespace S7CommPlusDriver.Internal
{
    internal static class S7CommPlusSymbolCrc
    {
        private const uint Polynomial = 0xF4ACFB13;
        private const byte ArrayTypeCode = 0x10;
        private const byte StructChildDelimiter = 0x89;

        private static readonly uint[] Table = CreateTable();

        internal readonly struct PathSegment
        {
            private PathSegment(string memberName, uint softdatatype, bool isArray, uint elementSoftdatatype, int lowerBound)
            {
                MemberName = memberName ?? string.Empty;
                Softdatatype = softdatatype;
                IsArray = isArray;
                ElementSoftdatatype = elementSoftdatatype;
                LowerBound = lowerBound;
            }

            public string MemberName { get; }
            public uint Softdatatype { get; }
            public bool IsArray { get; }
            public uint ElementSoftdatatype { get; }
            public int LowerBound { get; }

            public static PathSegment Member(string memberName, uint softdatatype)
            {
                return new PathSegment(memberName, softdatatype, false, 0, 0);
            }

            public static PathSegment Array(string memberName, uint elementSoftdatatype, int lowerBound)
            {
                return new PathSegment(memberName, global::S7CommPlusDriver.Softdatatype.S7COMMP_SOFTDATATYPE_ARRAY, true, elementSoftdatatype, lowerBound);
            }
        }

        public static uint ComputeItemCrc(string memberName, uint softdatatype)
        {
            return FinalizeItemCrc(MemberInnerCrc(memberName, MapSoftdatatypeToTypeCode(softdatatype)));
        }

        public static uint ComputeArrayItemCrc(string memberName, uint elementSoftdatatype, int lowerBound)
        {
            return FinalizeItemCrc(MemberInnerCrc(memberName, ArrayTypeCode, MapSoftdatatypeToTypeCode(elementSoftdatatype), lowerBound));
        }

        public static uint ComputeFromSegments(IReadOnlyList<PathSegment> pathSegments)
        {
            if (pathSegments == null || pathSegments.Count == 0)
            {
                return 0;
            }

            if (pathSegments.Count == 1)
            {
                var segment = pathSegments[0];
                return segment.IsArray
                    ? ComputeArrayItemCrc(segment.MemberName, segment.ElementSoftdatatype, segment.LowerBound)
                    : ComputeItemCrc(segment.MemberName, segment.Softdatatype);
            }

            var crc = new Crc32();
            crc.UpdateUInt32LittleEndian(MemberInnerCrc(pathSegments[0]));
            for (var i = 1; i < pathSegments.Count; i++)
            {
                crc.UpdateByte(StructChildDelimiter);
                crc.UpdateUInt32LittleEndian(MemberInnerCrc(pathSegments[i]));
            }
            return crc.Value;
        }

        private static uint MemberInnerCrc(PathSegment segment)
        {
            return segment.IsArray
                ? MemberInnerCrc(segment.MemberName, ArrayTypeCode, MapSoftdatatypeToTypeCode(segment.ElementSoftdatatype), segment.LowerBound)
                : MemberInnerCrc(segment.MemberName, MapSoftdatatypeToTypeCode(segment.Softdatatype));
        }

        private static uint MemberInnerCrc(string memberName, byte typeCode)
        {
            var crc = new Crc32();
            crc.UpdateString(memberName);
            crc.UpdateByte(typeCode);
            return crc.Value;
        }

        private static uint MemberInnerCrc(string memberName, byte typeCode, byte elementTypeCode, int lowerBound)
        {
            var crc = new Crc32();
            crc.UpdateString(memberName);
            crc.UpdateByte(typeCode);
            crc.UpdateByte(elementTypeCode);
            crc.UpdateInt32LittleEndian(lowerBound);
            return crc.Value;
        }

        private static uint FinalizeItemCrc(uint innerCrc)
        {
            var crc = new Crc32();
            crc.UpdateUInt32LittleEndian(innerCrc);
            return crc.Value;
        }

        private static byte MapSoftdatatypeToTypeCode(uint softdatatype)
        {
            switch (softdatatype)
            {
                case Softdatatype.S7COMMP_SOFTDATATYPE_BBOOL:
                    return (byte)Softdatatype.S7COMMP_SOFTDATATYPE_BOOL;

                case Softdatatype.S7COMMP_SOFTDATATYPE_AOMIDENT:
                case Softdatatype.S7COMMP_SOFTDATATYPE_EVENTANY:
                case Softdatatype.S7COMMP_SOFTDATATYPE_EVENTATT:
                case Softdatatype.S7COMMP_SOFTDATATYPE_FOLDER:
                case Softdatatype.S7COMMP_SOFTDATATYPE_AOMAID:
                case Softdatatype.S7COMMP_SOFTDATATYPE_AOMLINK:
                case Softdatatype.S7COMMP_SOFTDATATYPE_EVENTHWINT:
                case Softdatatype.S7COMMP_SOFTDATATYPE_CONNRID:
                    return (byte)Softdatatype.S7COMMP_SOFTDATATYPE_DWORD;

                case Softdatatype.S7COMMP_SOFTDATATYPE_HWANY:
                case Softdatatype.S7COMMP_SOFTDATATYPE_HWIOSYSTEM:
                case Softdatatype.S7COMMP_SOFTDATATYPE_HWDPMASTER:
                case Softdatatype.S7COMMP_SOFTDATATYPE_HWDEVICE:
                case Softdatatype.S7COMMP_SOFTDATATYPE_HWDPSLAVE:
                case Softdatatype.S7COMMP_SOFTDATATYPE_HWIO:
                case Softdatatype.S7COMMP_SOFTDATATYPE_HWMODULE:
                case Softdatatype.S7COMMP_SOFTDATATYPE_HWSUBMODULE:
                case Softdatatype.S7COMMP_SOFTDATATYPE_HWHSC:
                case Softdatatype.S7COMMP_SOFTDATATYPE_HWPWM:
                case Softdatatype.S7COMMP_SOFTDATATYPE_HWPTO:
                case Softdatatype.S7COMMP_SOFTDATATYPE_HWINTERFACE:
                case Softdatatype.S7COMMP_SOFTDATATYPE_HWIEPORT:
                case Softdatatype.S7COMMP_SOFTDATATYPE_CONNANY:
                case Softdatatype.S7COMMP_SOFTDATATYPE_CONNPRG:
                case Softdatatype.S7COMMP_SOFTDATATYPE_CONNOUC:
                    return (byte)Softdatatype.S7COMMP_SOFTDATATYPE_WORD;

                case Softdatatype.S7COMMP_SOFTDATATYPE_OBANY:
                case Softdatatype.S7COMMP_SOFTDATATYPE_OBDELAY:
                case Softdatatype.S7COMMP_SOFTDATATYPE_OBTOD:
                case Softdatatype.S7COMMP_SOFTDATATYPE_OBCYCLIC:
                case Softdatatype.S7COMMP_SOFTDATATYPE_OBATT:
                case Softdatatype.S7COMMP_SOFTDATATYPE_OBPCYCLE:
                case Softdatatype.S7COMMP_SOFTDATATYPE_OBHWINT:
                case Softdatatype.S7COMMP_SOFTDATATYPE_OBDIAG:
                case Softdatatype.S7COMMP_SOFTDATATYPE_OBTIMEERROR:
                case Softdatatype.S7COMMP_SOFTDATATYPE_OBSTARTUP:
                    return (byte)Softdatatype.S7COMMP_SOFTDATATYPE_INT;

                case Softdatatype.S7COMMP_SOFTDATATYPE_PORT:
                case Softdatatype.S7COMMP_SOFTDATATYPE_RTM:
                case Softdatatype.S7COMMP_SOFTDATATYPE_PIP:
                case Softdatatype.S7COMMP_SOFTDATATYPE_DBANY:
                case Softdatatype.S7COMMP_SOFTDATATYPE_DBWWW:
                case Softdatatype.S7COMMP_SOFTDATATYPE_DBDYN:
                    return (byte)Softdatatype.S7COMMP_SOFTDATATYPE_UINT;

                default:
                    return (byte)softdatatype;
            }
        }

        private static uint[] CreateTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < table.Length; i++)
            {
                var crc = i << 24;
                for (var bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 0x80000000) != 0
                        ? unchecked((crc << 1) ^ Polynomial)
                        : crc << 1;
                }
                table[i] = crc;
            }
            return table;
        }

        private struct Crc32
        {
            private uint _state;

            public uint Value => _state;

            public void UpdateString(string value)
            {
                var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
                for (var i = 0; i < bytes.Length; i++)
                {
                    UpdateByte(bytes[i]);
                }
            }

            public void UpdateInt32LittleEndian(int value)
            {
                UpdateByte((byte)value);
                UpdateByte((byte)(value >> 8));
                UpdateByte((byte)(value >> 16));
                UpdateByte((byte)(value >> 24));
            }

            public void UpdateUInt32LittleEndian(uint value)
            {
                UpdateByte((byte)value);
                UpdateByte((byte)(value >> 8));
                UpdateByte((byte)(value >> 16));
                UpdateByte((byte)(value >> 24));
            }

            public void UpdateByte(byte value)
            {
                _state = unchecked(Table[(value ^ (_state >> 24)) & 0xFF] ^ (_state << 8));
            }
        }
    }
}
