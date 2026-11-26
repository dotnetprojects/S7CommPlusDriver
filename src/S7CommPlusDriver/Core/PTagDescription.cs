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
    /// <summary>
    /// Decodes legacy S7-1200 FW2.x TagDescription elements (0xA7..0xA8) into the
    /// VartypeList/VarnameList shape used by <see cref="Browser"/>.
    /// </summary>
    internal static class PTagDescription
    {
        // Wireshark S7COMMP_TAGDESCR_OFFSETINFOTYPE_* (wire byte after 0xA7)
        private const byte OffsetInfoLibElement = 0x00;
        private const byte OffsetInfoBoolInUdt = 0x01;
        private const byte OffsetInfoArrayInStruct = 0x02;
        private const byte OffsetInfoPlainStatic = 0x04;
        private const byte OffsetInfoBool = 0x05;
        private const byte OffsetInfoArray = 0x06;
        private const byte OffsetInfoMultiDimArray = 0x07;

        private const uint AttrHmiVisible = 0x0800000;
        private const uint AttrHmiReadonly = 0x0400000;
        private const uint AttrHmiAccessible = 0x0100000;
        private const uint AttrRetain = 0x2000000;
        private const uint AttrClassic = 0x1000000;

        public static int Deserialize(Stream buffer, out string name, out PVartypeListElement element)
        {
            int ret = 0;
            name = string.Empty;
            element = new PVartypeListElement();

            ret += S7p.DecodeByte(buffer, out byte offsetInfoType);
            ret += S7p.DecodeUInt32Vlq(buffer, out uint nameLen);
            ret += S7p.DecodeWString(buffer, (int)nameLen, out name);
            ret += S7p.DecodeByte(buffer, out _); // unknown2
            ret += S7p.DecodeByte(buffer, out byte datatype);
            ret += S7p.DecodeUInt32Vlq(buffer, out uint softDatatype);
            ret += S7p.DecodeUInt32(buffer, out uint attributeFlags);
            ret += S7p.DecodeUInt32Vlq(buffer, out uint lid);
            ret += S7p.DecodeUInt32Vlq(buffer, out uint typeSpecific); // S7String length or Struct relation

            element.LID = lid;
            element.Softdatatype = softDatatype;
            element.SymbolCrc = 0;
            element.AttributeFlags = MapAttributeFlags(attributeFlags, offsetInfoType);
            element.BitoffsetinfoFlags = MapBitoffsetFlags(attributeFlags);
            element.OffsetInfoType = DecodeOffsetInfo(buffer, ref ret, offsetInfoType, datatype, typeSpecific);

            // Caller leaves 0xA8 for DecodeObject; consume it here so a single case handles the pair.
            if (buffer.Position < buffer.Length)
            {
                var next = buffer.ReadByte();
                if (next == ElementID.TerminatingTagDescription)
                {
                    ret += 1;
                }
                else if (next >= 0)
                {
                    buffer.Position -= 1;
                }
            }

            return ret;
        }

        private static ushort MapAttributeFlags(uint wireFlags, byte offsetInfoType)
        {
            // Pack visibility bits into the VartypeList AttributeFlags layout used by Browser.
            ushort flags = 0;
            if ((wireFlags & AttrHmiVisible) != 0) flags |= 0x0800;
            if ((wireFlags & AttrHmiReadonly) != 0) flags |= 0x0400;
            if ((wireFlags & AttrHmiAccessible) != 0) flags |= 0x0200;

            // Store a synthetic modern offsetinfotype in bits 12..15 for debugging only;
            // Browser uses OffsetInfoType object methods, not these bits, after deserialize.
            int modernType = offsetInfoType switch
            {
                OffsetInfoPlainStatic => (int)POffsetInfoType.OffsetInfoType.Std,
                OffsetInfoBool => (int)POffsetInfoType.OffsetInfoType.Std,
                OffsetInfoArray => (int)POffsetInfoType.OffsetInfoType.Array1Dim,
                OffsetInfoMultiDimArray => (int)POffsetInfoType.OffsetInfoType.ArrayMDim,
                OffsetInfoLibElement => (int)POffsetInfoType.OffsetInfoType.FbSfb,
                _ => (int)POffsetInfoType.OffsetInfoType.Std
            };
            flags = (ushort)((flags & 0x0fff) | (modernType << 12));
            return flags;
        }

        private static byte MapBitoffsetFlags(uint wireFlags)
        {
            byte flags = 0;
            if ((wireFlags & AttrRetain) != 0) flags |= 0x80;
            if ((wireFlags & AttrClassic) != 0) flags |= 0x08;
            return flags;
        }

        private static POffsetInfoType DecodeOffsetInfo(
            Stream buffer, ref int ret, byte offsetInfoType, byte datatype, uint typeSpecific)
        {
            switch (offsetInfoType)
            {
                case OffsetInfoLibElement:
                {
                    ret += S7p.DecodeUInt32Vlq(buffer, out uint o1);
                    ret += S7p.DecodeUInt32Vlq(buffer, out uint o2);
                    var oi = new POffsetInfoType_FbSfb
                    {
                        OptimizedAddress = o1,
                        NonoptimizedAddress = o2,
                        RelationId = typeSpecific
                    };
                    return oi;
                }
                case OffsetInfoBoolInUdt:
                {
                    ret += S7p.DecodeUInt32Vlq(buffer, out uint o1);
                    ret += S7p.DecodeUInt32Vlq(buffer, out uint o2);
                    ret += S7p.DecodeUInt32Vlq(buffer, out _); // bit offset type1
                    ret += S7p.DecodeUInt32Vlq(buffer, out _); // bit offset type2
                    return new POffsetInfoType_Std
                    {
                        OptimizedAddress = o1,
                        NonoptimizedAddress = o2
                    };
                }
                case OffsetInfoArrayInStruct:
                {
                    ret += S7p.DecodeUInt32Vlq(buffer, out uint o1);
                    ret += S7p.DecodeUInt32Vlq(buffer, out uint o2);
                    ret += S7p.DecodeInt32Vlq(buffer, out int lower);
                    ret += S7p.DecodeUInt32Vlq(buffer, out uint count);
                    ret += S7p.DecodeUInt32Vlq(buffer, out uint pad1);
                    ret += S7p.DecodeUInt32Vlq(buffer, out uint pad2);
                    if (datatype == Datatype.Struct)
                    {
                        return new POffsetInfoType_Struct1Dim
                        {
                            OptimizedAddress = o1,
                            NonoptimizedAddress = o2,
                            ArrayLowerBounds = lower,
                            ArrayElementCount = count,
                            UnspecifiedOffsetinfo1 = (ushort)Math.Min(pad1, ushort.MaxValue),
                            UnspecifiedOffsetinfo2 = (ushort)Math.Min(pad2, ushort.MaxValue),
                            RelationId = typeSpecific
                        };
                    }
                    return new POffsetInfoType_Array1Dim
                    {
                        OptimizedAddress = o1,
                        NonoptimizedAddress = o2,
                        ArrayLowerBounds = lower,
                        ArrayElementCount = count,
                        UnspecifiedOffsetinfo1 = (ushort)Math.Min(pad1, ushort.MaxValue),
                        UnspecifiedOffsetinfo2 = (ushort)Math.Min(pad2, ushort.MaxValue)
                    };
                }
                case OffsetInfoPlainStatic:
                {
                    ret += S7p.DecodeUInt32Vlq(buffer, out _); // accessability
                    ret += S7p.DecodeUInt32Vlq(buffer, out _); // section
                    ret += S7p.DecodeUInt32Vlq(buffer, out uint o1);
                    ret += S7p.DecodeUInt32Vlq(buffer, out uint o2);
                    if (datatype == Datatype.Struct)
                    {
                        return new POffsetInfoType_Struct
                        {
                            OptimizedAddress = o1,
                            NonoptimizedAddress = o2,
                            RelationId = typeSpecific
                        };
                    }
                    if (datatype == Datatype.S7String || datatype == Datatype.WString)
                    {
                        return new POffsetInfoType_String
                        {
                            OptimizedAddress = o1,
                            NonoptimizedAddress = o2,
                            UnspecifiedOffsetinfo1 = (ushort)Math.Min(typeSpecific, ushort.MaxValue),
                            UnspecifiedOffsetinfo2 = (ushort)Math.Min(typeSpecific + 2, ushort.MaxValue)
                        };
                    }
                    return new POffsetInfoType_Std
                    {
                        OptimizedAddress = o1,
                        NonoptimizedAddress = o2
                    };
                }
                case OffsetInfoBool:
                {
                    ret += S7p.DecodeUInt32Vlq(buffer, out _); // accessability
                    ret += S7p.DecodeUInt32Vlq(buffer, out _); // section
                    ret += S7p.DecodeUInt32Vlq(buffer, out uint o1);
                    ret += S7p.DecodeUInt32Vlq(buffer, out uint o2);
                    ret += S7p.DecodeUInt32Vlq(buffer, out _); // bit offset type1
                    ret += S7p.DecodeUInt32Vlq(buffer, out _); // bit offset type2
                    return new POffsetInfoType_Std
                    {
                        OptimizedAddress = o1,
                        NonoptimizedAddress = o2
                    };
                }
                case OffsetInfoArray:
                {
                    ret += S7p.DecodeUInt32Vlq(buffer, out _); // accessability
                    ret += S7p.DecodeUInt32Vlq(buffer, out _); // section
                    ret += S7p.DecodeUInt32Vlq(buffer, out uint o1);
                    ret += S7p.DecodeUInt32Vlq(buffer, out uint o2);
                    ret += S7p.DecodeInt32Vlq(buffer, out int lower);
                    ret += S7p.DecodeUInt32Vlq(buffer, out uint count);
                    ret += S7p.DecodeUInt32Vlq(buffer, out uint pad1);
                    ret += S7p.DecodeUInt32Vlq(buffer, out uint pad2);
                    if (datatype == Datatype.Struct)
                    {
                        return new POffsetInfoType_Struct1Dim
                        {
                            OptimizedAddress = o1,
                            NonoptimizedAddress = o2,
                            ArrayLowerBounds = lower,
                            ArrayElementCount = count,
                            UnspecifiedOffsetinfo1 = (ushort)Math.Min(pad1, ushort.MaxValue),
                            UnspecifiedOffsetinfo2 = (ushort)Math.Min(pad2, ushort.MaxValue),
                            RelationId = typeSpecific
                        };
                    }
                    if (datatype == Datatype.S7String || datatype == Datatype.WString)
                    {
                        return new POffsetInfoType_Array1Dim
                        {
                            OptimizedAddress = o1,
                            NonoptimizedAddress = o2,
                            ArrayLowerBounds = lower,
                            ArrayElementCount = count,
                            UnspecifiedOffsetinfo1 = (ushort)Math.Min(typeSpecific, ushort.MaxValue),
                            UnspecifiedOffsetinfo2 = (ushort)Math.Min(pad2, ushort.MaxValue)
                        };
                    }
                    return new POffsetInfoType_Array1Dim
                    {
                        OptimizedAddress = o1,
                        NonoptimizedAddress = o2,
                        ArrayLowerBounds = lower,
                        ArrayElementCount = count,
                        UnspecifiedOffsetinfo1 = (ushort)Math.Min(pad1, ushort.MaxValue),
                        UnspecifiedOffsetinfo2 = (ushort)Math.Min(pad2, ushort.MaxValue)
                    };
                }
                case OffsetInfoMultiDimArray:
                {
                    ret += S7p.DecodeUInt32Vlq(buffer, out _); // accessability
                    ret += S7p.DecodeUInt32Vlq(buffer, out _); // section
                    ret += S7p.DecodeUInt32Vlq(buffer, out uint o1);
                    ret += S7p.DecodeUInt32Vlq(buffer, out uint o2);
                    ret += S7p.DecodeUInt32Vlq(buffer, out uint pad1);
                    ret += S7p.DecodeUInt32Vlq(buffer, out uint pad2);
                    ret += S7p.DecodeUInt32Vlq(buffer, out uint dimensions);
                    var lowerBounds = new int[6];
                    var elementCounts = new uint[6];
                    uint total = 1;
                    for (uint d = 0; d < dimensions && d < 6; d++)
                    {
                        ret += S7p.DecodeInt32Vlq(buffer, out lowerBounds[d]);
                        ret += S7p.DecodeUInt32Vlq(buffer, out elementCounts[d]);
                        total *= Math.Max(1u, elementCounts[d]);
                    }
                    if (datatype == Datatype.Struct)
                    {
                        return new POffsetInfoType_StructMDim
                        {
                            OptimizedAddress = o1,
                            NonoptimizedAddress = o2,
                            UnspecifiedOffsetinfo1 = (ushort)Math.Min(pad1, ushort.MaxValue),
                            UnspecifiedOffsetinfo2 = (ushort)Math.Min(pad2, ushort.MaxValue),
                            ArrayLowerBounds = dimensions > 0 ? lowerBounds[0] : 0,
                            ArrayElementCount = total,
                            MdimArrayLowerBounds = lowerBounds,
                            MdimArrayElementCount = elementCounts,
                            RelationId = typeSpecific
                        };
                    }
                    return new POffsetInfoType_ArrayMDim
                    {
                        OptimizedAddress = o1,
                        NonoptimizedAddress = o2,
                        UnspecifiedOffsetinfo1 = (ushort)Math.Min(pad1, ushort.MaxValue),
                        UnspecifiedOffsetinfo2 = (ushort)Math.Min(pad2, ushort.MaxValue),
                        ArrayLowerBounds = dimensions > 0 ? lowerBounds[0] : 0,
                        ArrayElementCount = total,
                        MdimArrayLowerBounds = lowerBounds,
                        MdimArrayElementCount = elementCounts
                    };
                }
                default:
                    // Unknown offset-info: skip VLQs until TerminatingTagDescription (Wireshark fallback).
                    while (buffer.Position < buffer.Length)
                    {
                        var peek = buffer.ReadByte();
                        if (peek < 0) break;
                        if (peek == ElementID.TerminatingTagDescription)
                        {
                            buffer.Position -= 1;
                            break;
                        }
                        buffer.Position -= 1;
                        ret += S7p.DecodeUInt32Vlq(buffer, out _);
                    }
                    return new POffsetInfoType_Std();
            }
        }
    }
}
