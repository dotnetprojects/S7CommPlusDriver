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
using System.Collections.Generic;
using System.IO;

namespace S7CommPlusDriver
{
    // Array variants intentionally keep their own serializers because PLC wire flags differ per collection shape.
    internal abstract class PValue : IS7pSerialize
    {
        protected static byte FLAGS_ARRAY = 0x10;
        protected static byte FLAGS_ADDRESSARRAY = 0x20;
        protected static byte FLAGS_SPARSEARRAY = 0x40;

        protected byte DatatypeFlags;
        public abstract int Serialize(Stream buffer);

        public bool IsArray()
        {
            return ((DatatypeFlags & FLAGS_ARRAY) != 0);
        }

        public bool IsAddressArray()
        {
            return ((DatatypeFlags & FLAGS_ADDRESSARRAY) != 0);
        }

        public bool IsSparseArray()
        {
            return ((DatatypeFlags & FLAGS_SPARSEARRAY) != 0);
        }

        /// <summary>
        /// Deserializes the buffer to the protocol values
        /// </summary>
        /// <param name="buffer">Stream of bytes from the network</param>
        /// <param name="disableVlq">If true, the variable length encoding is disabled for all underlying values (so far only neccessary on SystemEvent)</param>
        /// <returns>The protocol value</returns>
        public static PValue Deserialize(Stream buffer, bool disableVlq = false)
        {
            byte flags;
            byte datatype;

            if (!disableVlq)
            {
                S7p.DecodeByte(buffer, out flags);
                S7p.DecodeByte(buffer, out datatype);
            }
            else
            {
                // If VLQ is disabled, there are two additional bytes we just skip here.
                S7p.DecodeByte(buffer, out _);
                S7p.DecodeByte(buffer, out flags);
                S7p.DecodeByte(buffer, out _);
                S7p.DecodeByte(buffer, out datatype);
            }

            // Controllers may combine the collection-shape bit with additional transport flags (for example 0x80).
            // Test the shape bits instead of requiring byte equality so lifecycle notifications remain decodable.
            if ((flags & (FLAGS_ARRAY | FLAGS_ADDRESSARRAY)) != 0)
            {
                switch (datatype)
                {
                    case Datatype.Null:
                        return ValueNullArray.Deserialize(buffer, flags, disableVlq);
                    case Datatype.Bool:
                        return ValueBoolArray.Deserialize(buffer, flags, disableVlq);
                    case Datatype.USInt:
                        return ValueUSIntArray.Deserialize(buffer, flags, disableVlq);
                    case Datatype.UInt:
                        return ValueUIntArray.Deserialize(buffer, flags, disableVlq);
                    case Datatype.UDInt:
                        return ValueUDIntArray.Deserialize(buffer, flags, disableVlq);
                    case Datatype.ULInt:
                        return ValueULIntArray.Deserialize(buffer, flags, disableVlq);
                    case Datatype.SInt:
                        return ValueSIntArray.Deserialize(buffer, flags, disableVlq);
                    case Datatype.Int:
                        return ValueIntArray.Deserialize(buffer, flags, disableVlq);
                    case Datatype.DInt:
                        return ValueDIntArray.Deserialize(buffer, flags, disableVlq);
                    case Datatype.LInt:
                        return ValueLIntArray.Deserialize(buffer, flags, disableVlq);
                    case Datatype.Byte:
                        return ValueByteArray.Deserialize(buffer, flags, disableVlq);
                    case Datatype.Word:
                        return ValueWordArray.Deserialize(buffer, flags, disableVlq);
                    case Datatype.DWord:
                        return ValueDWordArray.Deserialize(buffer, flags, disableVlq);
                    case Datatype.LWord:
                        return ValueLWordArray.Deserialize(buffer, flags, disableVlq);
                    case Datatype.Real:
                        return ValueRealArray.Deserialize(buffer, flags, disableVlq);
                    case Datatype.LReal:
                        return ValueLRealArray.Deserialize(buffer, flags, disableVlq);
                    case Datatype.Timestamp:
                        return ValueTimestampArray.Deserialize(buffer, flags, disableVlq);
                    case Datatype.Timespan:
                        return ValueTimespanArray.Deserialize(buffer, flags, disableVlq);
                    case Datatype.RID:
                        return ValueRIDArray.Deserialize(buffer, flags, disableVlq);
                    case Datatype.AID:
                        return ValueAIDArray.Deserialize(buffer, flags, disableVlq);
                    case Datatype.Blob:
                        return ValueBlobArray.Deserialize(buffer, flags, disableVlq);
                    case Datatype.WString:
                        return ValueWStringArray.Deserialize(buffer, flags, disableVlq);
                    case Datatype.Variant:
                        return ValueVariantArray.Deserialize(buffer, flags, disableVlq);
                    case Datatype.Struct:
                        return ValueStructArray.Deserialize(buffer, flags, disableVlq);
                    case Datatype.S7String:
                        return ValueS7StringArray.Deserialize(buffer, flags, disableVlq);
                    default:
                        throw CreateUnsupportedDatatypeException(buffer, flags, datatype);
                }
            }
            else if ((flags & FLAGS_SPARSEARRAY) != 0)
            {
                switch (datatype)
                {
                    case Datatype.DInt:
                        return ValueDIntSparseArray.Deserialize(buffer, flags, disableVlq);
                    case Datatype.UDInt:
                        return ValueUDIntSparseArray.Deserialize(buffer, flags, disableVlq);
                    case Datatype.Blob:
                        return ValueBlobSparseArray.Deserialize(buffer, flags, disableVlq);
                    case Datatype.WString:
                        return ValueWStringSparseArray.Deserialize(buffer, flags, disableVlq);
                    case Datatype.Variant:
                        return ValueVariantSparseArray.Deserialize(buffer, flags, disableVlq);
                    default:
                        throw CreateUnsupportedDatatypeException(buffer, flags, datatype);
                }
            }
            else
            {
                switch (datatype)
                {
                    case Datatype.Null:
                        return ValueNull.Deserialize(buffer, flags);
                    case Datatype.Bool:
                        return ValueBool.Deserialize(buffer, flags);
                    case Datatype.USInt:
                        return ValueUSInt.Deserialize(buffer, flags);
                    case Datatype.UInt:
                        return ValueUInt.Deserialize(buffer, flags);
                    case Datatype.UDInt:
                        return ValueUDInt.Deserialize(buffer, flags, disableVlq);
                    case Datatype.ULInt:
                        return ValueULInt.Deserialize(buffer, flags, disableVlq);
                    case Datatype.SInt:
                        return ValueSInt.Deserialize(buffer, flags);
                    case Datatype.Int:
                        return ValueInt.Deserialize(buffer, flags);
                    case Datatype.DInt:
                        return ValueDInt.Deserialize(buffer, flags, disableVlq);
                    case Datatype.LInt:
                        return ValueLInt.Deserialize(buffer, flags, disableVlq);
                    case Datatype.Byte:
                        return ValueByte.Deserialize(buffer, flags);
                    case Datatype.Word:
                        return ValueWord.Deserialize(buffer, flags);
                    case Datatype.DWord:
                        return ValueDWord.Deserialize(buffer, flags);
                    case Datatype.LWord:
                        return ValueLWord.Deserialize(buffer, flags);
                    case Datatype.Real:
                        return ValueReal.Deserialize(buffer, flags);
                    case Datatype.LReal:
                        return ValueLReal.Deserialize(buffer, flags);
                    case Datatype.Timestamp:
                        return ValueTimestamp.Deserialize(buffer, flags);
                    case Datatype.Timespan:
                        return ValueTimespan.Deserialize(buffer, flags, disableVlq);
                    case Datatype.RID:
                        return ValueRID.Deserialize(buffer, flags);
                    case Datatype.AID:
                        return ValueAID.Deserialize(buffer, flags, disableVlq);
                    case Datatype.Blob:
                        return ValueBlob.Deserialize(buffer, flags, disableVlq);
                    case Datatype.WString:
                        return ValueWString.Deserialize(buffer, flags, disableVlq);
                    case Datatype.Variant:
                        return ValueVariant.Deserialize(buffer, flags, disableVlq);
                    case Datatype.Struct:
                        return ValueStruct.Deserialize(buffer, flags, disableVlq);
                    case Datatype.S7String:
                        return ValueS7String.Deserialize(buffer, flags, disableVlq);
                    default:
                        throw CreateUnsupportedDatatypeException(buffer, flags, datatype);
                }
            }
        }

        /// <summary>
        /// Creates a diagnostic exception that preserves the wire flags, datatype, and stream location of an unsupported value.
        /// </summary>
        /// <param name="buffer">The PDU stream positioned at the unsupported value payload.</param>
        /// <param name="flags">The datatype collection-shape flags read from the PDU.</param>
        /// <param name="datatype">The unsupported S7CommPlus datatype identifier.</param>
        /// <returns>An exception suitable for propagating through the connection diagnostics.</returns>
        private static NotSupportedException CreateUnsupportedDatatypeException(Stream buffer, byte flags, byte datatype)
        {
            return new NotSupportedException(
                $"Unsupported S7CommPlus value datatype 0x{datatype:X2} with flags 0x{flags:X2} " +
                $"at PDU offset {buffer.Position} of {buffer.Length}.");
        }
    }
    internal class ValueNull : PValue
    {
        public ValueNull() : this(0)
        {
        }

        public ValueNull(byte flags)
        {
            DatatypeFlags = flags;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.Null);
            return ret;
        }

        public override string ToString()
        {
            return "<Value type=\"Null\"></Value>";
        }

        public static ValueNull Deserialize(Stream buffer, byte flags)
        {
            return new ValueNull(flags);
        }
    }

    /// <summary>
    /// Represents an array whose elements intentionally carry no payload, as emitted by PLC lifecycle notifications.
    /// </summary>
    internal sealed class ValueNullArray : PValue
    {
        private readonly uint _count;

        /// <summary>
        /// Initializes a null array while preserving its protocol collection shape.
        /// </summary>
        /// <param name="count">Number of logical null elements.</param>
        /// <param name="flags">Regular-array or address-array datatype flags.</param>
        internal ValueNullArray(uint count, byte flags)
        {
            _count = count;
            DatatypeFlags = flags;
        }

        /// <summary>
        /// Gets the number of logical elements even though no per-element bytes exist on the wire.
        /// </summary>
        internal uint Count => _count;

        /// <summary>
        /// Writes the array header and element count; null elements themselves have no payload.
        /// </summary>
        /// <param name="buffer">Destination protocol stream.</param>
        /// <returns>Number of bytes written.</returns>
        public override int Serialize(Stream buffer)
        {
            var ret = S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.Null);
            ret += S7p.EncodeUInt32Vlq(buffer, _count);
            return ret;
        }

        /// <summary>
        /// Reads the shared array count used by regular and address arrays of null values.
        /// </summary>
        /// <param name="buffer">Source protocol stream positioned at the array count.</param>
        /// <param name="flags">Collection-shape flags already read from the value header.</param>
        /// <param name="disableVlq">Uses fixed-width counters for system-event payloads when set.</param>
        /// <returns>The decoded null array.</returns>
        internal static ValueNullArray Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            if (disableVlq)
            {
                S7p.DecodeUInt32(buffer, out var fixedCount);
                return new ValueNullArray(fixedCount, flags);
            }

            S7p.DecodeUInt32Vlq(buffer, out var count);
            return new ValueNullArray(count, flags);
        }
    }

    internal class ValueBool : PValue
    {
        bool Value;

        public ValueBool(bool value) : this(value, 0)
        {
        }

        public ValueBool(bool value, byte flags)
        {
            DatatypeFlags = flags;
            Value = value;
        }

        public bool GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.Bool);
            ret += S7p.EncodeByte(buffer, Convert.ToByte(Value));
            return ret;
        }

        public override string ToString()
        {
            return "<Value type=\"Bool\">" + Value.ToString() + "</Value>";
        }

        public static ValueBool Deserialize(Stream buffer, byte flags)
        {
            byte value;
            S7p.DecodeByte(buffer, out value);
            return new ValueBool(Convert.ToBoolean(value), flags);
        }
    }

    /// <summary>
    /// ValueBoolArray: Important: The length of the array is always a multiple of 8.
    /// E.g. reading an Array [0..2] of Bool will be transmitted as 8 elements with actual values at index 0, 1, 2.
    /// An Array[0..9] will be transmitted as 16 elements and so on.
    /// At this time, serialize doesn't respect the padding elements, must be done on a higher level.
    /// </summary>
    internal class ValueBoolArray : PValue
    {
        bool[] Value;

        public ValueBoolArray(bool[] value) : this(value, FLAGS_ARRAY)
        {
        }

        public ValueBoolArray(bool[] value, byte flags)
        {
            DatatypeFlags = flags;
            if (value != null)
            {
                Value = new bool[value.Length];
                Array.Copy(value, Value, value.Length);
            }
        }

        public bool[] GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.Bool);
            // Bool-array padding is handled by the caller because packed/byte-wise layout depends on the PLC offset type.
            ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
            for (int i = 0; i < Value.Length; i++)
            {
                ret += S7p.EncodeByte(buffer, Convert.ToByte(Value[i]));
            }
            return ret;
        }

        public override string ToString()
        {
            string s = "<Value type =\"BoolArray\" size=\"" + Value.Length.ToString() + "\">";
            for (int i = 0; i < Value.Length; i++)
            {
                s += String.Format("<Value>{0}</Value>", Value[i].ToString());
            }
            s += "</Value>";
            return s;
        }

        public static ValueBoolArray Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            bool[] value;
            byte bv;
            UInt32 size = 0;
            if (!disableVlq)
            {
                S7p.DecodeUInt32Vlq(buffer, out size);
            }
            else
            {
                S7p.DecodeUInt32(buffer, out size);
            }
            value = new bool[size];
            for (int i = 0; i < size; i++)
            {
                S7p.DecodeByte(buffer, out bv);
                value[i] = Convert.ToBoolean(bv);
            }
            return new ValueBoolArray(value, flags);
        }
    }
    internal class ValueUSInt : PValue
    {
        byte Value;

        public ValueUSInt(byte value) : this(value, 0)
        {
        }

        public ValueUSInt(byte value, byte flags)
        {
            DatatypeFlags = flags;
            Value = value;
        }

        public byte GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.USInt);
            ret += S7p.EncodeByte(buffer, Value);
            return ret;
        }

        public override string ToString()
        {
            return "<Value type=\"USInt\">" + Value.ToString() + "</Value>";
        }

        public static ValueUSInt Deserialize(Stream buffer, byte flags)
        {
            byte value;
            S7p.DecodeByte(buffer, out value);
            return new ValueUSInt(value, flags);
        }
    }
    internal class ValueUSIntArray : PValue
    {
        byte[] Value;

        public ValueUSIntArray(byte[] value) : this(value, FLAGS_ARRAY)
        {
        }

        public ValueUSIntArray(byte[] value, byte flags)
        {
            DatatypeFlags = flags;
            if (value != null)
            {
                Value = new byte[value.Length];
                Array.Copy(value, Value, value.Length);
            }
        }

        public byte[] GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.USInt);
            ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
            for (int i = 0; i < Value.Length; i++)
            {
                ret += S7p.EncodeByte(buffer, Value[i]);
            }
            return ret;
        }

        public override string ToString()
        {
            string s = "<Value type =\"USIntArray\" size=\"" + Value.Length.ToString() + "\">";
            for (int i = 0; i < Value.Length; i++)
            {
                s += String.Format("<Value>{0}</Value>", Value[i]);
            }
            s += "</Value>";
            return s;
        }

        public static ValueUSIntArray Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            byte[] value;
            UInt32 size = 0;
            if (!disableVlq)
            {
                S7p.DecodeUInt32Vlq(buffer, out size);
            }
            else
            {
                S7p.DecodeUInt32(buffer, out size);
            }
            value = new byte[size];
            for (int i = 0; i < size; i++)
            {
                S7p.DecodeByte(buffer, out value[i]);
            }
            return new ValueUSIntArray(value, flags);
        }
    }
    internal class ValueUInt : PValue
    {
        UInt16 Value;

        public ValueUInt(UInt16 value) : this(value, 0)
        {
        }

        public ValueUInt(UInt16 value, byte flags)
        {
            DatatypeFlags = flags;
            Value = value;
        }

        public UInt16 GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.UInt);
            ret += S7p.EncodeUInt16(buffer, Value);
            return ret;
        }

        public override string ToString()
        {
            return "<Value type=\"UInt\">" + Value.ToString() + "</Value>";
        }

        public static ValueUInt Deserialize(Stream buffer, byte flags)
        {
            UInt16 value;
            S7p.DecodeUInt16(buffer, out value);
            return new ValueUInt(value, flags);
        }
    }
    internal class ValueUIntArray : PValue
    {
        UInt16[] Value;

        public ValueUIntArray(UInt16[] value) : this(value, FLAGS_ARRAY)
        {
        }

        public ValueUIntArray(UInt16[] value, byte flags)
        {
            DatatypeFlags = flags;
            if (value != null)
            {
                Value = new UInt16[value.Length];
                Array.Copy(value, Value, value.Length);
            }
        }

        public UInt16[] GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.UInt);
            ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
            for (int i = 0; i < Value.Length; i++)
            {
                ret += S7p.EncodeUInt16(buffer, Value[i]);
            }
            return ret;
        }

        public override string ToString()
        {
            string s = "<Value type =\"UIntArray\" size=\"" + Value.Length.ToString() + "\">";
            for (int i = 0; i < Value.Length; i++)
            {
                s += String.Format("<Value>{0}</Value>", Value[i]);
            }
            s += "</Value>";
            return s;
        }

        public static ValueUIntArray Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            UInt16[] value;
            UInt32 size = 0;
            if (!disableVlq)
            {
                S7p.DecodeUInt32Vlq(buffer, out size);
            }
            else
            {
                S7p.DecodeUInt32(buffer, out size);
            }
            value = new UInt16[size];
            for (int i = 0; i < size; i++)
            {
                S7p.DecodeUInt16(buffer, out value[i]);
            }
            return new ValueUIntArray(value, flags);
        }
    }
    internal class ValueUDInt : PValue
    {
        UInt32 Value;

        public ValueUDInt(UInt32 value) : this(value, 0)
        {
        }

        public ValueUDInt(UInt32 value, byte flags)
        {
            DatatypeFlags = flags;
            Value = value;
        }

        public UInt32 GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.UDInt);
            ret += S7p.EncodeUInt32Vlq(buffer, Value);
            return ret;
        }

        public override string ToString()
        {
            return "<Value type=\"UDInt\">" + Value.ToString() + "</Value>";
        }

        public static ValueUDInt Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            UInt32 value;
            if (!disableVlq)
            {
                S7p.DecodeUInt32Vlq(buffer, out value);
            }
            else
            {
                S7p.DecodeUInt32(buffer, out value);
            }
            return new ValueUDInt(value, flags);
        }
    }
    internal class ValueUDIntArray : PValue
    {
        UInt32[] Value;

        public ValueUDIntArray(UInt32[] value) : this(value, FLAGS_ARRAY)
        {
        }

        public ValueUDIntArray(UInt32[] value, byte flags)
        {
            DatatypeFlags = flags;
            if (value != null)
            {
                Value = new UInt32[value.Length];
                Array.Copy(value, Value, value.Length);
            }
        }

        public UInt32[] GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.UDInt);
            ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
            for (int i = 0; i < Value.Length; i++)
            {
                ret += S7p.EncodeUInt32Vlq(buffer, Value[i]);
            }
            return ret;
        }

        public override string ToString()
        {
            string s = "<Value type =\"UDIntArray\" size=\"" + Value.Length.ToString() + "\">";
            for (int i = 0; i < Value.Length; i++)
            {
                s += String.Format("<Value>{0}</Value>", Value[i]);
            }
            s += "</Value>";
            return s;
        }

        public static ValueUDIntArray Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            UInt32[] value;
            UInt32 size = 0;
            if (!disableVlq)
            {
                S7p.DecodeUInt32Vlq(buffer, out size);
                value = new UInt32[size];
                for (int i = 0; i < size; i++)
                {
                    S7p.DecodeUInt32Vlq(buffer, out value[i]);
                }
            }
            else
            {
                S7p.DecodeUInt32(buffer, out size);
                value = new UInt32[size];
                for (int i = 0; i < size; i++)
                {
                    S7p.DecodeUInt32(buffer, out value[i]);
                }
            }
            return new ValueUDIntArray(value, flags);
        }
    }

    // The construction of Sparsearray is almost similar to reading a struct.
    // All elements are kind of key,value. And Value is of the selected type.
    // The list is terminated by Null.
    // E.g.: Reading 1037 (SystemLimits) via GetVarSubStreamed
    internal class ValueUDIntSparseArray : PValue
    {
        Dictionary<UInt32, UInt32> Value;

        public ValueUDIntSparseArray(Dictionary<UInt32, UInt32> value) : this(value, FLAGS_SPARSEARRAY)
        {
        }

        public ValueUDIntSparseArray(Dictionary<UInt32, UInt32> value, byte flags)
        {
            DatatypeFlags = flags;
            if (value != null)
            {
                Value = new Dictionary<UInt32, UInt32>(value);
            }
        }

        public Dictionary<UInt32, UInt32> GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.UDInt);
            foreach (var v in Value)
            {
                ret += S7p.EncodeUInt32Vlq(buffer, v.Key);
                ret += S7p.EncodeUInt32Vlq(buffer, v.Value);
            }
            ret += S7p.EncodeByte(buffer, 0);
            return ret;
        }

        public override string ToString()
        {
            string s = "<Value type =\"UDIntSparseArray\">";
            foreach (var v in Value)
            {
                s += String.Format("<Value key=\"{0}\">{1}</Value>", v.Key, v.Value);
            }
            s += "</Value>";
            return s;
        }

        public static ValueUDIntSparseArray Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            Dictionary<UInt32, UInt32> value = new Dictionary<uint, uint>();
            UInt32 k = 0;
            UInt32 v = 0;
            if (!disableVlq)
            {
                S7p.DecodeUInt32Vlq(buffer, out k);
                while (k > 0)
                {
                    S7p.DecodeUInt32Vlq(buffer, out v);
                    value.Add(k, v);
                    S7p.DecodeUInt32Vlq(buffer, out k);
                }
            }
            else
            {
                S7p.DecodeUInt32(buffer, out k);
                while (k > 0)
                {
                    S7p.DecodeUInt32(buffer, out v);
                    value.Add(k, v);
                    S7p.DecodeUInt32(buffer, out k);
                }
            }
            return new ValueUDIntSparseArray(value, flags);
        }
    }
    internal class ValueULInt : PValue
    {
        UInt64 Value;

        public ValueULInt(UInt64 value) : this(value, 0)
        {
        }

        public ValueULInt(UInt64 value, byte flags)
        {
            DatatypeFlags = flags;
            Value = value;
        }

        public UInt64 GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.ULInt);
            ret += S7p.EncodeUInt64Vlq(buffer, Value);
            return ret;
        }

        public override string ToString()
        {
            return "<Value type=\"ULInt\">" + Value.ToString() + "</Value>";
        }

        public static ValueULInt Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            UInt64 value;
            if (!disableVlq)
            {
                S7p.DecodeUInt64Vlq(buffer, out value);
            }
            else
            {
                S7p.DecodeUInt64(buffer, out value);
            }
            return new ValueULInt(value, flags);
        }
    }
    internal class ValueULIntArray : PValue
    {
        UInt64[] Value;

        public ValueULIntArray(UInt64[] value) : this(value, FLAGS_ARRAY)
        {
        }

        public ValueULIntArray(UInt64[] value, byte flags)
        {
            DatatypeFlags = flags;
            if (value != null)
            {
                Value = new UInt64[value.Length];
                Array.Copy(value, Value, value.Length);
            }
        }

        public UInt64[] GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.ULInt);
            ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
            for (int i = 0; i < Value.Length; i++)
            {
                ret += S7p.EncodeUInt64Vlq(buffer, Value[i]);
            }
            return ret;
        }

        public override string ToString()
        {
            string s = "<Value type =\"ULIntArray\" size=\"" + Value.Length.ToString() + "\">";
            for (int i = 0; i < Value.Length; i++)
            {
                s += String.Format("<Value>{0}</Value>", Value[i]);
            }
            s += "</Value>";
            return s;
        }

        public static ValueULIntArray Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            UInt64[] value;
            UInt32 size = 0;
            if (!disableVlq)
            {
                S7p.DecodeUInt32Vlq(buffer, out size);
                value = new UInt64[size];
                for (int i = 0; i < size; i++)
                {
                    S7p.DecodeUInt64Vlq(buffer, out value[i]);
                }
            }
            else
            {
                S7p.DecodeUInt32(buffer, out size);
                value = new UInt64[size];
                for (int i = 0; i < size; i++)
                {
                    S7p.DecodeUInt64(buffer, out value[i]);
                }
            }
            return new ValueULIntArray(value, flags);
        }
    }
    internal class ValueSInt : PValue
    {
        sbyte Value;

        public ValueSInt(sbyte value) : this(value, 0)
        {
        }

        public ValueSInt(sbyte value, byte flags)
        {
            DatatypeFlags = flags;
            Value = value;
        }

        public sbyte GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.SInt);
            ret += S7p.EncodeByte(buffer, (byte)Value);
            return ret;
        }

        public override string ToString()
        {
            return "<Value type=\"SInt\">" + Value.ToString() + "</Value>";
        }

        public static ValueSInt Deserialize(Stream buffer, byte flags)
        {
            byte value;
            S7p.DecodeByte(buffer, out value);
            return new ValueSInt((sbyte)value, flags);
        }
    }
    internal class ValueSIntArray : PValue
    {
        sbyte[] Value;

        public ValueSIntArray(sbyte[] value) : this(value, FLAGS_ARRAY)
        {
        }

        public ValueSIntArray(sbyte[] value, byte flags)
        {
            DatatypeFlags = flags;
            if (value != null)
            {
                Value = new sbyte[value.Length];
                Array.Copy(value, Value, value.Length);
            }
        }

        public sbyte[] GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.SInt);
            ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
            for (int i = 0; i < Value.Length; i++)
            {
                ret += S7p.EncodeByte(buffer, (byte)Value[i]);
            }
            return ret;
        }

        public override string ToString()
        {
            string s = "<Value type =\"SIntArray\" size=\"" + Value.Length.ToString() + "\">";
            for (int i = 0; i < Value.Length; i++)
            {
                s += String.Format("<Value>{0}</Value>", Value[i]);
            }
            s += "</Value>";
            return s;
        }

        public static ValueSIntArray Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            sbyte[] value;
            UInt32 size = 0;
            if (!disableVlq)
            {
                S7p.DecodeUInt32Vlq(buffer, out size);
            }
            else
            {
                S7p.DecodeUInt32(buffer, out size);
            }
            value = new sbyte[size];
            byte b;
            for (int i = 0; i < size; i++)
            {
                S7p.DecodeByte(buffer, out b);
                value[i] = (sbyte)b;
            }
            return new ValueSIntArray(value, flags);
        }
    }
    internal class ValueInt : PValue
    {
        Int16 Value;

        public ValueInt(Int16 value) : this(value, 0)
        {
        }

        public ValueInt(Int16 value, byte flags)
        {
            DatatypeFlags = flags;
            Value = value;
        }

        public Int16 GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.Int);
            ret += S7p.EncodeInt16(buffer, Value);
            return ret;
        }

        public override string ToString()
        {
            return "<Value type=\"Int\">" + Value.ToString() + "</Value>";
        }

        public static ValueInt Deserialize(Stream buffer, byte flags)
        {
            Int16 value;
            S7p.DecodeInt16(buffer, out value);
            return new ValueInt(value, flags);
        }
    }
    internal class ValueIntArray : PValue
    {
        Int16[] Value;

        public ValueIntArray(Int16[] value) : this(value, FLAGS_ARRAY)
        {
        }

        public ValueIntArray(Int16[] value, byte flags)
        {
            DatatypeFlags = flags;
            if (value != null)
            {
                Value = new Int16[value.Length];
                Array.Copy(value, Value, value.Length);
            }
        }

        public Int16[] GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.Int);
            ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
            for (int i = 0; i < Value.Length; i++)
            {
                ret += S7p.EncodeInt16(buffer, Value[i]);
            }
            return ret;
        }

        public override string ToString()
        {
            string s = "<Value type =\"IntArray\" size=\"" + Value.Length.ToString() + "\">";
            for (int i = 0; i < Value.Length; i++)
            {
                s += String.Format("<Value>{0}</Value>", Value[i]);
            }
            s += "</Value>";
            return s;
        }

        public static ValueIntArray Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            Int16[] value;
            UInt32 size = 0;
            if (!disableVlq)
            {
                S7p.DecodeUInt32Vlq(buffer, out size);
            }
            else
            {
                S7p.DecodeUInt32(buffer, out size);
            }
            value = new Int16[size];
            for (int i = 0; i < size; i++)
            {
                S7p.DecodeInt16(buffer, out value[i]);
            }
            return new ValueIntArray(value, flags);
        }
    }
    internal class ValueDInt : PValue
    {
        Int32 Value;

        public ValueDInt(Int32 value) : this(value, 0)
        {
        }

        public ValueDInt(Int32 value, byte flags)
        {
            DatatypeFlags = flags;
            Value = value;
        }

        public Int32 GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.DInt);
            ret += S7p.EncodeInt32Vlq(buffer, Value);
            return ret;
        }

        public override string ToString()
        {
            return "<Value type=\"DInt\">" + Value.ToString() + "</Value>";
        }

        public static ValueDInt Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            Int32 value;
            if (!disableVlq)
            {
                S7p.DecodeInt32Vlq(buffer, out value);
            }
            else
            {
                S7p.DecodeInt32(buffer, out value);
            }
            return new ValueDInt(value, flags);
        }
    }
    internal class ValueDIntArray : PValue
    {
        Int32[] Value;

        public ValueDIntArray(Int32[] value) : this(value, FLAGS_ARRAY)
        {
        }

        public ValueDIntArray(Int32[] value, byte flags)
        {
            DatatypeFlags = flags;
            if (value != null)
            {
                Value = new Int32[value.Length];
                Array.Copy(value, Value, value.Length);
            }
        }

        public Int32[] GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.DInt);
            ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
            for (int i = 0; i < Value.Length; i++)
            {
                ret += S7p.EncodeInt32Vlq(buffer, Value[i]);
            }
            return ret;
        }

        public override string ToString()
        {
            string s = "<Value type =\"DIntArray\" size=\"" + Value.Length.ToString() + "\">";
            for (int i = 0; i < Value.Length; i++)
            {
                s += String.Format("<Value>{0}</Value>", Value[i]);
            }
            s += "</Value>";
            return s;
        }

        public static ValueDIntArray Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            Int32[] value;
            UInt32 size = 0;
            if (!disableVlq)
            {
                S7p.DecodeUInt32Vlq(buffer, out size);
                value = new Int32[size];
                for (int i = 0; i < size; i++)
                {
                    S7p.DecodeInt32Vlq(buffer, out value[i]);
                }
            }
            else
            {
                S7p.DecodeUInt32(buffer, out size);
                value = new Int32[size];
                for (int i = 0; i < size; i++)
                {
                    S7p.DecodeInt32(buffer, out value[i]);
                }
            }
            return new ValueDIntArray(value, flags);
        }
    }
    internal class ValueDIntSparseArray : PValue
    {
        Dictionary<UInt32, Int32> Value;

        public ValueDIntSparseArray(Dictionary<UInt32, Int32> value) : this(value, FLAGS_SPARSEARRAY)
        {
        }

        public ValueDIntSparseArray(Dictionary<UInt32, Int32> value, byte flags)
        {
            DatatypeFlags = flags;
            if (value != null)
            {
                Value = new Dictionary<UInt32, Int32>(value);
            }
        }

        public Dictionary<UInt32, Int32> GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.DInt);
            foreach (var v in Value)
            {
                ret += S7p.EncodeUInt32Vlq(buffer, v.Key);
                ret += S7p.EncodeInt32Vlq(buffer, v.Value);
            }
            ret += S7p.EncodeByte(buffer, 0);
            return ret;
        }

        public override string ToString()
        {
            string s = "<Value type =\"DIntSparseArray\">";
            foreach (var v in Value)
            {
                s += String.Format("<Value key=\"{0}\">{1}</Value>", v.Key, v.Value);
            }
            s += "</Value>";
            return s;
        }

        public static ValueDIntSparseArray Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            Dictionary<UInt32, Int32> value = new Dictionary<UInt32, Int32>();
            UInt32 k = 0;
            Int32 v = 0;
            if (!disableVlq)
            {
                S7p.DecodeUInt32Vlq(buffer, out k);
                while (k > 0)
                {
                    S7p.DecodeInt32Vlq(buffer, out v);
                    value.Add(k, v);
                    S7p.DecodeUInt32Vlq(buffer, out k);
                }
            }
            else
            {
                S7p.DecodeUInt32(buffer, out k);
                while (k > 0)
                {
                    S7p.DecodeInt32(buffer, out v);
                    value.Add(k, v);
                    S7p.DecodeUInt32(buffer, out k);
                }
            }
            return new ValueDIntSparseArray(value, flags);
        }
    }
    internal class ValueLInt : PValue
    {
        Int64 Value;

        public ValueLInt(Int64 value) : this(value, 0)
        {
        }

        public ValueLInt(Int64 value, byte flags)
        {
            DatatypeFlags = flags;
            Value = value;
        }

        public Int64 GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.LInt);
            ret += S7p.EncodeInt64Vlq(buffer, Value);
            return ret;
        }

        public override string ToString()
        {
            return "<Value type=\"LInt\">" + Value.ToString() + "</Value>";
        }

        public static ValueLInt Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            Int64 value;
            if (!disableVlq)
            {
                S7p.DecodeInt64Vlq(buffer, out value);
            }
            else
            {
                S7p.DecodeInt64(buffer, out value);
            }
            return new ValueLInt(value, flags);
        }
    }
    internal class ValueLIntArray : PValue
    {
        Int64[] Value;

        public ValueLIntArray(Int64[] value) : this(value, FLAGS_ARRAY)
        {
        }

        public ValueLIntArray(Int64[] value, byte flags)
        {
            DatatypeFlags = flags;
            if (value != null)
            {
                Value = new Int64[value.Length];
                Array.Copy(value, Value, value.Length);
            }
        }

        public Int64[] GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.LInt);
            ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
            for (int i = 0; i < Value.Length; i++)
            {
                ret += S7p.EncodeInt64Vlq(buffer, Value[i]);
            }
            return ret;
        }

        public override string ToString()
        {
            string s = "<Value type =\"LIntArray\" size=\"" + Value.Length.ToString() + "\">";
            for (int i = 0; i < Value.Length; i++)
            {
                s += String.Format("<Value>{0}</Value>", Value[i]);
            }
            s += "</Value>";
            return s;
        }

        public static ValueLIntArray Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            Int64[] value;
            UInt32 size = 0;
            if (!disableVlq)
            {
                S7p.DecodeUInt32Vlq(buffer, out size);
                value = new Int64[size];
                for (int i = 0; i < size; i++)
                {
                    S7p.DecodeInt64Vlq(buffer, out value[i]);
                }
            }
            else
            {
                S7p.DecodeUInt32(buffer, out size);
                value = new Int64[size];
                for (int i = 0; i < size; i++)
                {
                    S7p.DecodeInt64(buffer, out value[i]);
                }
            }
            return new ValueLIntArray(value, flags);
        }
    }
    internal class ValueByte : PValue
    {
        byte Value;

        public ValueByte(byte value) : this(value, 0)
        {
        }

        public ValueByte(byte value, byte flags)
        {
            DatatypeFlags = flags;
            Value = value;
        }

        public byte GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.Byte);
            ret += S7p.EncodeByte(buffer, Value);
            return ret;
        }

        public override string ToString()
        {
            return String.Format("<Value type=\"Byte\">{0}</Value>", Value);
        }

        public static ValueByte Deserialize(Stream buffer, byte flags)
        {
            byte value;
            S7p.DecodeByte(buffer, out value);
            return new ValueByte(value, flags);
        }
    }
    internal class ValueByteArray : PValue
    {
        byte[] Value;

        public ValueByteArray(byte[] value) : this(value, FLAGS_ARRAY)
        {
        }

        public ValueByteArray(byte[] value, byte flags)
        {
            DatatypeFlags = flags;
            if (value != null)
            {
                Value = new byte[value.Length];
                Array.Copy(value, Value, value.Length);
            }
        }

        public byte[] GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.Byte);
            ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
            for (int i = 0; i < Value.Length; i++)
            {
                ret += S7p.EncodeByte(buffer, Value[i]);
            }
            return ret;
        }

        public override string ToString()
        {
            string s = "<Value type =\"ByteArray\" size=\"" + Value.Length.ToString() + "\">";
            for (int i = 0; i < Value.Length; i++)
            {
                s += String.Format("<Value>{0}</Value>", Value[i]);
            }
            s += "</Value>";
            return s;
        }

        public static ValueByteArray Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            byte[] value;
            UInt32 size = 0;
            if (!disableVlq)
            {
                S7p.DecodeUInt32Vlq(buffer, out size);
            }
            else
            {
                S7p.DecodeUInt32(buffer, out size);
            }
            value = new byte[size];
            for (int i = 0; i < size; i++)
            {
                S7p.DecodeByte(buffer, out value[i]);
            }
            return new ValueByteArray(value, flags);
        }
    }
    internal class ValueWord : PValue
    {
        UInt16 Value;

        public ValueWord(UInt16 value) : this(value, 0)
        {
        }

        public ValueWord(UInt16 value, byte flags)
        {
            DatatypeFlags = flags;
            Value = value;
        }

        public UInt16 GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.Word);
            ret += S7p.EncodeUInt16(buffer, Value);
            return ret;
        }

        public override string ToString()
        {
            return String.Format("<Value type=\"Word\">{0}</Value>", Value);
        }

        public static ValueWord Deserialize(Stream buffer, byte flags)
        {
            UInt16 value;
            S7p.DecodeUInt16(buffer, out value);
            return new ValueWord(value, flags);
        }
    }
    internal class ValueWordArray : PValue
    {
        UInt16[] Value;

        public ValueWordArray(UInt16[] value) : this(value, FLAGS_ARRAY)
        {
        }

        public ValueWordArray(UInt16[] value, byte flags)
        {
            DatatypeFlags = flags;
            if (value != null)
            {
                Value = new UInt16[value.Length];
                Array.Copy(value, Value, value.Length);
            }
        }

        public UInt16[] GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.Word);
            ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
            for (int i = 0; i < Value.Length; i++)
            {
                ret += S7p.EncodeUInt16(buffer, Value[i]);
            }
            return ret;
        }

        public override string ToString()
        {
            string s = "<Value type =\"WordArray\" size=\"" + Value.Length.ToString() + "\">";
            for (int i = 0; i < Value.Length; i++)
            {
                s += String.Format("<Value>{0}</Value>", Value[i]);
            }
            s += "</Value>";
            return s;
        }

        public static ValueWordArray Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            UInt16[] value;
            UInt32 size = 0;
            if (!disableVlq)
            {
                S7p.DecodeUInt32Vlq(buffer, out size);
            }
            else
            {
                S7p.DecodeUInt32(buffer, out size);
            }
            value = new UInt16[size];
            for (int i = 0; i < size; i++)
            {
                S7p.DecodeUInt16(buffer, out value[i]);
            }
            return new ValueWordArray(value, flags);
        }
    }
    internal class ValueDWord : PValue
    {
        UInt32 Value;

        public ValueDWord(UInt32 value) : this(value, 0)
        {
        }

        public ValueDWord(UInt32 value, byte flags)
        {
            DatatypeFlags = flags;
            Value = value;
        }

        public UInt32 GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.DWord);
            ret += S7p.EncodeUInt32(buffer, Value);
            return ret;
        }

        public override string ToString()
        {
            return String.Format("<Value type=\"DWord\">{0}</Value>", Value);
        }

        public static ValueDWord Deserialize(Stream buffer, byte flags)
        {
            UInt32 value;
            S7p.DecodeUInt32(buffer, out value);
            return new ValueDWord(value, flags);
        }
    }
    internal class ValueDWordArray : PValue
    {
        UInt32[] Value;

        public ValueDWordArray(UInt32[] value) : this(value, FLAGS_ARRAY)
        {
        }

        public ValueDWordArray(UInt32[] value, byte flags)
        {
            DatatypeFlags = flags;
            if (value != null)
            {
                Value = new UInt32[value.Length];
                Array.Copy(value, Value, value.Length);
            }
        }

        public UInt32[] GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.DWord);
            ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
            for (int i = 0; i < Value.Length; i++)
            {
                ret += S7p.EncodeUInt32(buffer, Value[i]);
            }
            return ret;
        }

        public override string ToString()
        {
            string s = "<Value type =\"DWordArray\" size=\"" + Value.Length.ToString() + "\">";
            for (int i = 0; i < Value.Length; i++)
            {
                s += String.Format("<Value>{0}</Value>", Value[i]);
            }
            s += "</Value>";
            return s;
        }

        public static ValueDWordArray Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            UInt32[] value;
            UInt32 size = 0;
            if (!disableVlq)
            {
                S7p.DecodeUInt32Vlq(buffer, out size);
            }
            else
            {
                S7p.DecodeUInt32(buffer, out size);
            }
            value = new UInt32[size];
            for (int i = 0; i < size; i++)
            {
                S7p.DecodeUInt32(buffer, out value[i]);
            }
            return new ValueDWordArray(value, flags);
        }
    }
    internal class ValueLWord : PValue
    {
        UInt64 Value;

        public ValueLWord(UInt64 value) : this(value, 0)
        {
        }

        public ValueLWord(UInt64 value, byte flags)
        {
            DatatypeFlags = flags;
            Value = value;
        }

        public UInt64 GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.LWord);
            ret += S7p.EncodeUInt64(buffer, Value);
            return ret;
        }

        public override string ToString()
        {
            return String.Format("<Value type=\"LWord\">{0}</Value>", Value);
        }

        public static ValueLWord Deserialize(Stream buffer, byte flags)
        {
            UInt64 value;
            S7p.DecodeUInt64(buffer, out value);
            return new ValueLWord(value, flags);
        }
    }
    internal class ValueLWordArray : PValue
    {
        UInt64[] Value;

        public ValueLWordArray(UInt64[] value) : this(value, FLAGS_ARRAY)
        {
        }

        public ValueLWordArray(UInt64[] value, byte flags)
        {
            DatatypeFlags = flags;
            if (value != null)
            {
                Value = new UInt64[value.Length];
                Array.Copy(value, Value, value.Length);
            }
        }

        public UInt64[] GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.LWord);
            ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
            for (int i = 0; i < Value.Length; i++)
            {
                ret += S7p.EncodeUInt64(buffer, Value[i]);
            }
            return ret;
        }

        public override string ToString()
        {
            string s = "<Value type =\"LWordArray\" size=\"" + Value.Length.ToString() + "\">";
            for (int i = 0; i < Value.Length; i++)
            {
                s += String.Format("<Value>{0}</Value>", Value[i]);
            }
            s += "</Value>";
            return s;
        }

        public static ValueLWordArray Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            UInt64[] value;
            UInt32 size = 0;
            if (!disableVlq)
            {
                S7p.DecodeUInt32Vlq(buffer, out size);
            }
            else
            {
                S7p.DecodeUInt32(buffer, out size);
            }
            value = new UInt64[size];
            for (int i = 0; i < size; i++)
            {
                S7p.DecodeUInt64(buffer, out value[i]);
            }
            return new ValueLWordArray(value, flags);
        }
    }
    internal class ValueReal : PValue
    {
        float Value;

        public ValueReal(float value) : this(value, 0)
        {
        }

        public ValueReal(float value, byte flags)
        {
            DatatypeFlags = flags;
            Value = value;
        }

        public float GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.Real);
            ret += S7p.EncodeFloat(buffer, Value);
            return ret;
        }

        public override string ToString()
        {
            return "<Value type=\"Real\">" + Value.ToString() + "</Value>";
        }

        public static ValueReal Deserialize(Stream buffer, byte flags)
        {
            float value;
            S7p.DecodeFloat(buffer, out value);
            return new ValueReal(value, flags);
        }
    }
    internal class ValueRealArray : PValue
    {
        float[] Value;

        public ValueRealArray(float[] value) : this(value, FLAGS_ARRAY)
        {
        }

        public ValueRealArray(float[] value, byte flags)
        {
            DatatypeFlags = flags;
            if (value != null)
            {
                Value = new float[value.Length];
                Array.Copy(value, Value, value.Length);
            }
        }

        public float[] GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.Real);
            ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
            for (int i = 0; i < Value.Length; i++)
            {
                ret += S7p.EncodeFloat(buffer, Value[i]);
            }
            return ret;
        }

        public override string ToString()
        {
            string s = "<Value type =\"RealArray\" size=\"" + Value.Length.ToString() + "\">";
            for (int i = 0; i < Value.Length; i++)
            {
                s += String.Format("<Value>{0}</Value>", Value[i]);
            }
            s += "</Value>";
            return s;
        }

        public static ValueRealArray Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            float[] value;
            UInt32 size = 0;
            if (!disableVlq)
            {
                S7p.DecodeUInt32Vlq(buffer, out size);
            }
            else
            {
                S7p.DecodeUInt32(buffer, out size);
            }
            value = new float[size];
            for (int i = 0; i < size; i++)
            {
                S7p.DecodeFloat(buffer, out value[i]);
            }
            return new ValueRealArray(value, flags);
        }
    }
    internal class ValueLReal : PValue
    {
        double Value;

        public ValueLReal(double value) : this(value, 0)
        {
        }

        public ValueLReal(double value, byte flags)
        {
            DatatypeFlags = flags;
            Value = value;
        }

        public double GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.LReal);
            ret += S7p.EncodeDouble(buffer, Value);
            return ret;
        }

        public override string ToString()
        {
            return "<Value type=\"LReal\">" + Value.ToString() + "</Value>";
        }

        public static ValueLReal Deserialize(Stream buffer, byte flags)
        {
            double value;
            S7p.DecodeDouble(buffer, out value);
            return new ValueLReal(value, flags);
        }
    }
    internal class ValueLRealArray : PValue
    {
        double[] Value;

        public ValueLRealArray(double[] value) : this(value, FLAGS_ARRAY)
        {
        }

        public ValueLRealArray(double[] value, byte flags)
        {
            DatatypeFlags = flags;
            if (value != null)
            {
                Value = new double[value.Length];
                Array.Copy(value, Value, value.Length);
            }
        }

        public double[] GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.LReal);
            ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
            for (int i = 0; i < Value.Length; i++)
            {
                ret += S7p.EncodeDouble(buffer, Value[i]);
            }
            return ret;
        }

        public override string ToString()
        {
            string s = "<Value type =\"LRealArray\" size=\"" + Value.Length.ToString() + "\">";
            for (int i = 0; i < Value.Length; i++)
            {
                s += String.Format("<Value>{0}</Value>", Value[i]);
            }
            s += "</Value>";
            return s;
        }

        public static ValueLRealArray Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            double[] value;
            UInt32 size = 0;
            if (!disableVlq)
            {
                S7p.DecodeUInt32Vlq(buffer, out size);
            }
            else
            {
                S7p.DecodeUInt32(buffer, out size);
            }
            value = new double[size];
            for (int i = 0; i < size; i++)
            {
                S7p.DecodeDouble(buffer, out value[i]);
            }
            return new ValueLRealArray(value, flags);
        }
    }
    internal class ValueTimestamp : PValue
    {
        UInt64 Value;

        public ValueTimestamp(UInt64 value) : this(value, 0)
        {
        }

        public ValueTimestamp(UInt64 value, byte flags)
        {
            DatatypeFlags = flags;
            Value = value;
        }

        public UInt64 GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.Timestamp);
            ret += S7p.EncodeUInt64(buffer, Value);
            return ret;
        }

        public static string ToString(UInt64 Value)
        {
            DateTime dt = new DateTime(1970, 1, 1);
            ulong v, ns;
            string fmt;
            v = Value;
            ns = v % 1000000000;
            v /= 1000000000;

            dt = dt.AddSeconds(v);

            if ((ns % 1000) > 0)
            {
                fmt = "{0}.{1:D09}";
            }
            else if ((ns % 1000000) > 0)
            {
                fmt = "{0}.{1:D06}";
                ns /= 1000;
            }
            else if ((ns % 1000000000) > 0)
            {
                fmt = "{0}.{1:D03}";
                ns /= 1000000;
            }
            else
            {
                return dt.ToString();
            }
            return String.Format(fmt, dt.ToString(), ns);
        }

        public override string ToString()
        {
            string str = ToString(Value);
            return "<Value type=\"Timestamp\">" + str + "</Value>";
        }

        public static ValueTimestamp Deserialize(Stream buffer, byte flags)
        {
            UInt64 value;
            S7p.DecodeUInt64(buffer, out value);
            return new ValueTimestamp(value, flags);
        }
    }
    internal class ValueTimestampArray : PValue
    {
        UInt64[] Value;

        public ValueTimestampArray(UInt64[] value) : this(value, 0)
        {
        }

        public ValueTimestampArray(UInt64[] value, byte flags)
        {
            DatatypeFlags = flags;
            Value = value;
        }

        public UInt64[] GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.Timestamp);
            ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
            for (int i = 0; i < Value.Length; i++)
            {
                ret += S7p.EncodeUInt64(buffer, Value[i]);
            }
            return ret;
        }

        public override string ToString()
        {
            string s = "<Value type =\"TimestampArray\" size=\"" + Value.Length.ToString() + "\">";
            for (int i = 0; i < Value.Length; i++)
            {
                s += String.Format("<Value>{0}</Value>", ValueTimestamp.ToString(Value[i]));
            }
            s += "</Value>";
            return s;
        }

        public static ValueTimestampArray Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            UInt64[] value;
            UInt32 size = 0;
            if (!disableVlq)
            {
                S7p.DecodeUInt32Vlq(buffer, out size);
            }
            else
            {
                S7p.DecodeUInt32(buffer, out size);
            }
            value = new UInt64[size];
            for (int i = 0; i < size; i++)
            {
                S7p.DecodeUInt64(buffer, out value[i]);
            }
            return new ValueTimestampArray(value, flags);
        }
    }
    internal class ValueTimespan : PValue
    {
        Int64 Value;

        public ValueTimespan(Int64 value) : this(value, 0)
        {
        }

        public ValueTimespan(Int64 value, byte flags)
        {
            DatatypeFlags = flags;
            Value = value;
        }

        public Int64 GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.Timespan);
            ret += S7p.EncodeInt64Vlq(buffer, Value);
            return ret;
        }

        public static string ToString(Int64 Value)
        {
            string str;
            long[] divs = { 86400000000000, 3600000000000, 60000000000, 1000000000, 1000000, 1000, 1 };
            string[] vfmt = { "{0}d", "{0:00}h", "{0:00}m", "{0:00}s", "{0:000}ms", "{0:000}us", "{0:000}ns" };
            long val;
            long timespan = Value;
            bool time_negative = false;
            if (timespan == 0)
            {
                str = "LT#000ns";
            }
            else
            {
                if (timespan < 0)
                {
                    str = "LT#-";
                    time_negative = true;
                    for (int i = 0; i < 7; i++)
                    {
                        divs[i] = -divs[i];
                    }
                }
                else
                {
                    str = "LT#";
                }

                for (int i = 0; i < 7; i++)
                {
                    val = timespan / divs[i];
                    timespan -= val * divs[i];
                    if (val > 0)
                    {
                        str += String.Format(vfmt[i], (Int32)val);
                        if ((!time_negative && timespan > 0) || (time_negative && timespan < 0))
                        {
                            str += "_";
                        }
                    }
                }
            }
            return str;
        }

        public override string ToString()
        {
            string str = ToString(Value);
            return ("<Value type=\"Timespan\">" + str + "</Value>");
        }

        public static ValueTimespan Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            Int64 value;
            if (!disableVlq)
            {
                S7p.DecodeInt64Vlq(buffer, out value);
            }
            else
            {
                S7p.DecodeInt64(buffer, out value);
            }
            return new ValueTimespan(value, flags);
        }
    }
    internal class ValueTimespanArray : PValue
    {
        Int64[] Value;

        public ValueTimespanArray(Int64[] value) : this(value, FLAGS_ARRAY)
        {
        }

        public ValueTimespanArray(Int64[] value, byte flags)
        {
            DatatypeFlags = flags;
            if (value != null)
            {
                Value = new Int64[value.Length];
                Array.Copy(value, Value, value.Length);
            }
        }

        public Int64[] GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.LReal);
            ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
            for (int i = 0; i < Value.Length; i++)
            {
                ret += S7p.EncodeInt64Vlq(buffer, Value[i]);
            }
            return ret;
        }

        public override string ToString()
        {
            string s = "<Value type =\"ValueTimespanArray\" size=\"" + Value.Length.ToString() + "\">";
            for (int i = 0; i < Value.Length; i++)
            {
                s += String.Format("<Value>{0}</Value>", ValueTimespan.ToString(Value[i]));
            }
            s += "</Value>";
            return s;
        }

        public static ValueTimespanArray Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            Int64[] value;
            UInt32 size = 0;
            if (!disableVlq)
            {
                S7p.DecodeUInt32Vlq(buffer, out size);
            }
            else
            {
                S7p.DecodeUInt32(buffer, out size);
            }
            value = new Int64[size];
            for (int i = 0; i < size; i++)
            {
                if (!disableVlq)
                {
                    S7p.DecodeInt64Vlq(buffer, out value[i]);
                }
                else
                {
                    S7p.DecodeInt64(buffer, out value[i]);
                }
            }
            return new ValueTimespanArray(value, flags);
        }
    }
    internal class ValueRID : PValue
    {
        UInt32 Value;

        public ValueRID(UInt32 rid) : this (rid, 0)
        {
        }

        public ValueRID(UInt32 rid, byte flags)
        {
            DatatypeFlags = flags;
            Value = rid;
        }

        public UInt32 GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.RID);
            ret += S7p.EncodeUInt32(buffer, Value);
            return ret;
        }

        public override string ToString()
        {
            return "<Value type=\"RID\">" + Value.ToString() + "</Value>";
        }

        public static ValueRID Deserialize(Stream buffer, byte flags)
        {
            UInt32 value;
            S7p.DecodeUInt32(buffer, out value);
            return new ValueRID(value, flags);
        }
    }
    internal class ValueRIDArray : PValue
    {
        UInt32[] Value;

        public ValueRIDArray(UInt32[] value) : this(value, FLAGS_ARRAY)
        {
        }

        public ValueRIDArray(UInt32[] value, byte flags)
        {
            DatatypeFlags = flags;
            if (value != null)
            {
                Value = new UInt32[value.Length];
                Array.Copy(value, Value, value.Length);
            }
        }

        public UInt32[] GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.RID);
            ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
            for (int i = 0; i < Value.Length; i++)
            {
                ret += S7p.EncodeUInt32(buffer, Value[i]);
            }
            return ret;
        }

        public override string ToString()
        {
            string s = "<Value type =\"RIDArray\" size=\"" + Value.Length.ToString() + "\">";
            for (int i = 0; i < Value.Length; i++)
            {
                s += String.Format("<Value>{0}</Value>", Value[i]);
            }
            s += "</Value>";
            return s;
        }

        public static ValueRIDArray Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            UInt32[] value;
            UInt32 size = 0;
            if (!disableVlq)
            {
                S7p.DecodeUInt32Vlq(buffer, out size);
            }
            else
            {
                S7p.DecodeUInt32(buffer, out size);
            }
            value = new UInt32[size];
            for (int i = 0; i < size; i++)
            {
                S7p.DecodeUInt32(buffer, out value[i]);
            }
            return new ValueRIDArray(value, flags);
        }
    }
    internal class ValueAID : PValue
    {
        UInt32 Value;

        public ValueAID(UInt32 value) : this(value, 0)
        {
        }

        public ValueAID(UInt32 value, byte flags)
        {
            DatatypeFlags = flags;
            Value = value;
        }

        public UInt32 GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.AID);
            ret += S7p.EncodeUInt32Vlq(buffer, Value);
            return ret;
        }

        public override string ToString()
        {
            return "<Value type=\"AID\">" + Value.ToString() + "</Value>";
        }

        public static ValueAID Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            UInt32 value;
            if (!disableVlq)
            {
                S7p.DecodeUInt32Vlq(buffer, out value);
            }
            else
            {
                S7p.DecodeUInt32(buffer, out value);
            }
            return new ValueAID(value, flags);
        }
    }
    internal class ValueAIDArray : PValue
    {
        UInt32[] Value;

        public ValueAIDArray(UInt32[] value) : this(value, FLAGS_ARRAY)
        {
        }

        public ValueAIDArray(UInt32[] value, byte flags)
        {
            DatatypeFlags = flags;
            if (value != null)
            {
                Value = new UInt32[value.Length];
                Array.Copy(value, Value, value.Length);
            }
        }

        public UInt32[] GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.AID);
            ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
            for (int i = 0; i < Value.Length; i++)
            {
                ret += S7p.EncodeUInt32Vlq(buffer, Value[i]);
            }
            return ret;
        }

        public override string ToString()
        {
            string s = "<Value type =\"AIDArray\" size=\"" + Value.Length.ToString() + "\">";
            for (int i = 0; i < Value.Length; i++)
            {
                s += String.Format("<Value>{0}</Value>", Value[i]);
            }
            s += "</Value>";
            return s;
        }

        public static ValueAIDArray Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            UInt32[] value;
            UInt32 size = 0;
            if (!disableVlq)
            {
                S7p.DecodeUInt32Vlq(buffer, out size);
                value = new UInt32[size];
                for (int i = 0; i < size; i++)
                {
                    S7p.DecodeUInt32Vlq(buffer, out value[i]);
                }
            }
            else
            {
                S7p.DecodeUInt32(buffer, out size);
                value = new UInt32[size];
                for (int i = 0; i < size; i++)
                {
                    S7p.DecodeUInt32(buffer, out value[i]);
                }
            }
            return new ValueAIDArray(value, flags);
        }
    }
    internal class ValueBlob : PValue
    {
        public UInt32 BlobRootId;
        byte[] Value;

        public bool HasBlobType; // Special
        public byte BlobType;    // Special

        public ValueBlob(UInt32 blobRootId, byte[] value) : this(blobRootId, value, 0)
        {
        }

        public ValueBlob(UInt32 blobRootId, byte[] value, byte flags)
        {
            BlobRootId = blobRootId;
            DatatypeFlags = flags;
            // A blob with size zero is allowed and no error.
            if (value != null)
            {
                Value = new byte[value.Length];
                Array.Copy(value, Value, value.Length);
            }
        }

        public byte[] GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.Blob);
            ret += S7p.EncodeUInt32Vlq(buffer, BlobRootId);
            ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
            ret += S7p.EncodeOctets(buffer, Value);
            return ret;
        }

        public override string ToString()
        {
            string s;
            if (!HasBlobType)
            {
                s = "<Value type=\"Blob\" BlobRootId=\"" + BlobRootId.ToString() + "\">";
            }
            else
            {
                s = "<Value type=\"Blob\" BlobRootId=\"" + BlobRootId.ToString() + "\" BlobType=\"" + BlobType.ToString() + "\">";
            }
            if (Value != null)
            {
                s += BitConverter.ToString(Value);
            }
            s += "</Value>";
            return s;
        }

        public static ValueBlob Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            UInt32 blobRootId;
            UInt32 blobSize;
            bool hasBlobType = false;
            byte blobType = 0;
            byte[] value;
            if (!disableVlq)
            {
                S7p.DecodeUInt32Vlq(buffer, out blobRootId);
            }
            else
            {
                S7p.DecodeUInt32(buffer, out blobRootId);
            }
            // Special handling:
            // If first value > 1 then special format with 8 additional bytes + 1 type-id + value.
            // On HMI project transfer this occurs with ID=1 (as SubStream) but without the extra bytes.
            // Used for example in Alarm Notifications for the AssociatedValues.
            if (blobRootId > 1)
            {
                hasBlobType = true;
                S7p.DecodeUInt64(buffer, out _); // Don't use it for now. All bytes were zero so far.
                S7p.DecodeByte(buffer, out blobType);
                // - If BlobType value == 0x02 or 0x03, then follows a length specification and the number of bytes.
                //   This is used in alarms and the associated values inside the blob-array.
                // - If BlobType value == 0x00, then follows an ID/value list.
                //   This is used in program transfer.
                switch (blobType)
                {
                    case 0x02:
                    case 0x03:
                        // handling below is the same from here
                        break;
                    default:
                        // can't handle this for now, this is completely different...
                        throw new NotSupportedException(
                            $"Unsupported S7CommPlus blob type 0x{blobType:X2} for root id 0x{blobRootId:X8} " +
                            $"at PDU offset {buffer.Position} of {buffer.Length}.");
                }
            }

            if (!disableVlq)
            {
                S7p.DecodeUInt32Vlq(buffer, out blobSize);
            }
            else
            {
                S7p.DecodeUInt32(buffer, out blobSize);
            }
            value = new byte[blobSize];
            S7p.DecodeOctets(buffer, (int)blobSize, out value);
            var blob = new ValueBlob(blobRootId, value, flags);
            blob.HasBlobType = hasBlobType;
            blob.BlobType = blobType;
            return blob;
        }
    }
    internal class ValueBlobArray : PValue
    {
        ValueBlob[] Value;

        public ValueBlobArray(ValueBlob[] value) : this(value, FLAGS_ADDRESSARRAY)
        {
        }

        public ValueBlobArray(ValueBlob[] value, byte flags)
        {
            DatatypeFlags = flags;
            if (value != null)
            {
                Value = new ValueBlob[value.Length];
                Array.Copy(value, Value, value.Length);
            }
        }

        public ValueBlob[] GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.Blob);
            ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
            for (int i = 0; i < Value.Length; i++)
            {
                ret += Value[i].Serialize(buffer);
            }
            return ret;
        }

        public override string ToString()
        {
            string s = "<Value type =\"ValueBlobArray\" size=\"" + Value.Length.ToString() + "\">";
            for (int i = 0; i < Value.Length; i++)
            {
                s += String.Format("<Value>{0}</Value>", Value[i]);
            }
            s += "</Value>";
            return s;
        }

        public static ValueBlobArray Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            UInt32 size = 0;
            if (!disableVlq)
            {
                S7p.DecodeUInt32Vlq(buffer, out size);
            }
            else
            {
                S7p.DecodeUInt32(buffer, out size);
            }
            ValueBlob[] value = new ValueBlob[size];
            for (int i = 0; i < size; i++)
            {
                value[i] = ValueBlob.Deserialize(buffer, flags, disableVlq);
            }
            return new ValueBlobArray(value, flags);
        }
    }
    internal class ValueBlobSparseArray : PValue
    {
        public struct BlobEntry
        {
            public UInt32 blobRootId;
            public byte[] value;
        }

        public Dictionary<UInt32, BlobEntry> Value;

        public ValueBlobSparseArray(Dictionary<UInt32, BlobEntry> value) : this(value, FLAGS_SPARSEARRAY)
        {
        }

        public ValueBlobSparseArray(Dictionary<UInt32, BlobEntry> value, byte flags)
        {
            DatatypeFlags = flags;
            if (value != null)
            {
                Value = new Dictionary<UInt32, BlobEntry>(value);
            }
        }

        public Dictionary<UInt32, BlobEntry> GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.Blob);
            foreach (var v in Value)
            {
                ret += S7p.EncodeUInt32Vlq(buffer, v.Key);
                ret += S7p.EncodeUInt32Vlq(buffer, v.Value.blobRootId);
                ret += S7p.EncodeUInt32Vlq(buffer, (uint)v.Value.value.Length);
                ret += S7p.EncodeOctets(buffer, v.Value.value);
            }
            ret += S7p.EncodeByte(buffer, 0);
            return ret;
        }

        public override string ToString()
        {
            string s = "<Value type=\"BlobSparseArray\">";
            foreach (var v in Value)
            {
                s += String.Format("<Value key=\"{0}\" BlobRootId=\"{1}\">", v.Key, v.Value.blobRootId);
                if (Value != null && v.Value.value != null)
                {
                    s += BitConverter.ToString(v.Value.value);
                }
                s += "</Value>";
            }
            s += "</Value>";
            return s;
        }

        public static ValueBlobSparseArray Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            Dictionary<UInt32, BlobEntry> value = new Dictionary<UInt32, BlobEntry>();
            UInt32 k = 0;
            BlobEntry v = new BlobEntry();
            UInt32 blobSize = 0;
            if (!disableVlq)
            {
                S7p.DecodeUInt32Vlq(buffer, out k);
                while (k > 0)
                {
                    S7p.DecodeUInt32Vlq(buffer, out v.blobRootId);
                    S7p.DecodeUInt32Vlq(buffer, out blobSize);
                    v.value = new byte[blobSize];
                    S7p.DecodeOctets(buffer, (int)blobSize, out v.value);
                    value.Add(k, v);

                    S7p.DecodeUInt32Vlq(buffer, out k);
                }
            }
            else
            {
                S7p.DecodeUInt32(buffer, out k);
                while (k > 0)
                {
                    S7p.DecodeUInt32(buffer, out v.blobRootId);
                    S7p.DecodeUInt32(buffer, out blobSize);
                    v.value = new byte[blobSize];
                    S7p.DecodeOctets(buffer, (int)blobSize, out v.value);
                    value.Add(k, v);

                    S7p.DecodeUInt32(buffer, out k);
                }
            }
            return new ValueBlobSparseArray(value, flags);
        }
    }
    internal class ValueWString : PValue
    {
        string Value;

        public ValueWString(string value) : this(value, 0)
        {
        }

        public ValueWString(string value, byte flags)
        {
            DatatypeFlags = flags;
            Value = value;
        }

        public string GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.WString);
            ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
            ret += S7p.EncodeWString(buffer, Value);
            return ret;
        }

        public override string ToString()
        {
            return "<Value type=\"WString\">" + Value + "</Value>";
        }

        public static ValueWString Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            string value;
            UInt32 stringlen;
            if (!disableVlq)
            {
                S7p.DecodeUInt32Vlq(buffer, out stringlen);
            }
            else
            {
                S7p.DecodeUInt32(buffer, out stringlen);
            }
            S7p.DecodeWString(buffer, (int)stringlen, out value);
            return new ValueWString(value, flags);
        }
    }
    internal class ValueWStringArray : PValue
    {
        string[] Value;

        public ValueWStringArray(string[] value) : this(value, FLAGS_ADDRESSARRAY)
        {
        }

        public ValueWStringArray(string[] value, byte flags)
        {
            DatatypeFlags = flags;
            Value = value;
        }

        public string[] GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.WString);
            ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value.Length);
            for (int i = 0; i < Value.Length; i++)
            {
                ret += S7p.EncodeUInt32Vlq(buffer, (uint)Value[i].Length);
                ret += S7p.EncodeWString(buffer, Value[i]);
            }
            return ret;
        }

        public override string ToString()
        {
            string s = "<Value type =\"WStringArray\" size=\"" + Value.Length.ToString() + "\">";
            for (int i = 0; i < Value.Length; i++)
            {
                s += String.Format("<Value>{0}</Value>", Value[i]);
            }
            s += "</Value>";
            return s;
        }

        public static ValueWStringArray Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            string[] value;
            UInt32 stringlen;
            UInt32 arraySize;
            if (!disableVlq)
            {
                S7p.DecodeUInt32Vlq(buffer, out arraySize);
                value = new string[arraySize];
                for (int i = 0; i < arraySize; i++)
                {
                    S7p.DecodeUInt32Vlq(buffer, out stringlen);
                    S7p.DecodeWString(buffer, (int)stringlen, out value[i]);
                }
            }
            else
            {
                S7p.DecodeUInt32(buffer, out arraySize);
                value = new string[arraySize];
                for (int i = 0; i < arraySize; i++)
                {
                    S7p.DecodeUInt32(buffer, out stringlen);
                    S7p.DecodeWString(buffer, (int)stringlen, out value[i]);
                }
            }
            return new ValueWStringArray(value, flags);
        }
    }
    internal class ValueWStringSparseArray : PValue
    {
        Dictionary<UInt32, string> Value;

        public ValueWStringSparseArray(Dictionary<UInt32, string> value) : this(value, FLAGS_SPARSEARRAY)
        {
        }

        public ValueWStringSparseArray(Dictionary<UInt32, string> value, byte flags)
        {
            DatatypeFlags = flags;
            if (value != null)
            {
                Value = new Dictionary<UInt32, string>(value);
            }
        }

        public Dictionary<UInt32, string> GetValue()
        {
            return Value;
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;
            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.WString);
            foreach (var v in Value)
            {
                ret += S7p.EncodeUInt32Vlq(buffer, v.Key);
                ret += S7p.EncodeUInt32Vlq(buffer, (uint)v.Value.Length);
                ret += S7p.EncodeWString(buffer, v.Value);
            }
            ret += S7p.EncodeByte(buffer, 0);
            return ret;
        }

        public override string ToString()
        {
            string s = "<Value type =\"WStringSparseArray\">";
            foreach (var v in Value)
            {
                s += String.Format("<Value key=\"{0}\">{1}</Value>", v.Key, v.Value);
            }
            s += "</Value>";
            return s;
        }

        public static ValueWStringSparseArray Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            Dictionary<UInt32, string> value = new Dictionary<UInt32, string>();
            UInt32 k = 0;
            UInt32 stringlen;
            string v = String.Empty;
            if (!disableVlq)
            {
                S7p.DecodeUInt32Vlq(buffer, out k);
                while (k > 0)
                {
                    S7p.DecodeUInt32Vlq(buffer, out stringlen);
                    S7p.DecodeWString(buffer, (int)stringlen, out v);
                    value.Add(k, v);
                    S7p.DecodeUInt32Vlq(buffer, out k);
                }
            }
            else
            {
                S7p.DecodeUInt32(buffer, out k);
                while (k > 0)
                {
                    S7p.DecodeUInt32(buffer, out stringlen);
                    S7p.DecodeWString(buffer, (int)stringlen, out v);
                    value.Add(k, v);
                    S7p.DecodeUInt32(buffer, out k);
                }
            }
            return new ValueWStringSparseArray(value, flags);
        }
    }
    /// <summary>
    /// Represents the S7String descriptor payload used in tag descriptions.
    /// </summary>
    /// <remarks>
    /// The wire value is the VLQ-encoded maximum S7String length, not the bytes of a runtime PLC string.
    /// Runtime strings are transported through their resolved primitive storage representation.
    /// </remarks>
    internal sealed class ValueS7String : PValue
    {
        private readonly uint _maximumLength;

        /// <summary>
        /// Initializes an S7String descriptor while preserving its datatype flags.
        /// </summary>
        /// <param name="maximumLength">Maximum character count declared by the PLC tag description.</param>
        /// <param name="flags">Datatype flags from the value header.</param>
        internal ValueS7String(uint maximumLength, byte flags = 0)
        {
            _maximumLength = maximumLength;
            DatatypeFlags = flags;
        }

        /// <summary>
        /// Gets the maximum S7String character count declared by the PLC.
        /// </summary>
        internal uint MaximumLength => _maximumLength;

        /// <summary>
        /// Writes the descriptor header and its maximum-length payload.
        /// </summary>
        /// <param name="buffer">Destination protocol stream.</param>
        /// <returns>Number of bytes written.</returns>
        public override int Serialize(Stream buffer)
        {
            var ret = S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.S7String);
            ret += SerializePayload(buffer, disableVlq: false);
            return ret;
        }

        /// <summary>
        /// Writes only the maximum-length payload for use in homogeneous arrays.
        /// </summary>
        /// <param name="buffer">Destination protocol stream.</param>
        /// <param name="disableVlq">Uses fixed-width encoding for system-event payloads when set.</param>
        /// <returns>Number of bytes written.</returns>
        internal int SerializePayload(Stream buffer, bool disableVlq)
        {
            return disableVlq
                ? S7p.EncodeUInt32(buffer, _maximumLength)
                : S7p.EncodeUInt32Vlq(buffer, _maximumLength);
        }

        /// <summary>
        /// Reads an S7String maximum-length descriptor after its header has been consumed.
        /// </summary>
        /// <param name="buffer">Source protocol stream.</param>
        /// <param name="flags">Datatype flags from the value header.</param>
        /// <param name="disableVlq">Uses fixed-width decoding for system-event payloads when set.</param>
        /// <returns>The decoded descriptor.</returns>
        internal static ValueS7String Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            if (disableVlq)
            {
                S7p.DecodeUInt32(buffer, out var fixedLength);
                return new ValueS7String(fixedLength, flags);
            }

            S7p.DecodeUInt32Vlq(buffer, out var maximumLength);
            return new ValueS7String(maximumLength, flags);
        }
    }

    /// <summary>
    /// Represents a regular or address array of S7String maximum-length descriptors.
    /// </summary>
    internal sealed class ValueS7StringArray : PValue
    {
        private readonly ValueS7String[] _values;

        /// <summary>
        /// Initializes an S7String descriptor array while preserving its collection shape.
        /// </summary>
        /// <param name="values">Decoded S7String descriptors.</param>
        /// <param name="flags">Regular-array or address-array datatype flags.</param>
        internal ValueS7StringArray(ValueS7String[] values, byte flags)
        {
            _values = values ?? Array.Empty<ValueS7String>();
            DatatypeFlags = flags;
        }

        /// <summary>
        /// Gets a defensive copy of the decoded descriptors.
        /// </summary>
        internal ValueS7String[] GetValue() => (ValueS7String[])_values.Clone();

        /// <summary>
        /// Writes the array header, count, and bare maximum-length payloads.
        /// </summary>
        /// <param name="buffer">Destination protocol stream.</param>
        /// <returns>Number of bytes written.</returns>
        public override int Serialize(Stream buffer)
        {
            var ret = S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.S7String);
            ret += S7p.EncodeUInt32Vlq(buffer, (uint)_values.Length);
            foreach (var value in _values)
            {
                ret += value.SerializePayload(buffer, disableVlq: false);
            }
            return ret;
        }

        /// <summary>
        /// Reads a counted sequence of S7String maximum-length descriptors.
        /// </summary>
        /// <param name="buffer">Source protocol stream positioned at the element count.</param>
        /// <param name="flags">Collection-shape flags already read from the value header.</param>
        /// <param name="disableVlq">Uses fixed-width counters and payloads for system events when set.</param>
        /// <returns>The decoded descriptor array.</returns>
        internal static ValueS7StringArray Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            uint count;
            if (disableVlq)
            {
                S7p.DecodeUInt32(buffer, out count);
            }
            else
            {
                S7p.DecodeUInt32Vlq(buffer, out count);
            }

            var values = new ValueS7String[count];
            for (var index = 0; index < count; index++)
            {
                values[index] = ValueS7String.Deserialize(buffer, flags, disableVlq);
            }
            return new ValueS7StringArray(values, flags);
        }
    }

    /// <summary>
    /// Represents the protocol Variant payload, which carries a single VLQ-encoded unsigned identifier/value.
    /// </summary>
    internal sealed class ValueVariant : PValue
    {
        private readonly uint _value;

        /// <summary>
        /// Initializes a Variant value while preserving its datatype flags.
        /// </summary>
        /// <param name="value">Unsigned Variant payload decoded from the PLC.</param>
        /// <param name="flags">Datatype flags from the value header.</param>
        internal ValueVariant(uint value, byte flags = 0)
        {
            _value = value;
            DatatypeFlags = flags;
        }

        /// <summary>
        /// Gets the raw Variant payload used by the PLC protocol.
        /// </summary>
        internal uint GetValue() => _value;

        /// <summary>
        /// Writes the Variant header and VLQ payload.
        /// </summary>
        /// <param name="buffer">Destination protocol stream.</param>
        /// <returns>Number of bytes written.</returns>
        public override int Serialize(Stream buffer)
        {
            var ret = S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.Variant);
            ret += SerializePayload(buffer, disableVlq: false);
            return ret;
        }

        /// <summary>
        /// Writes only the payload for use inside homogeneous arrays.
        /// </summary>
        /// <param name="buffer">Destination protocol stream.</param>
        /// <param name="disableVlq">Uses fixed-width encoding for system-event payloads when set.</param>
        /// <returns>Number of bytes written.</returns>
        internal int SerializePayload(Stream buffer, bool disableVlq)
        {
            return disableVlq
                ? S7p.EncodeUInt32(buffer, _value)
                : S7p.EncodeUInt32Vlq(buffer, _value);
        }

        /// <summary>
        /// Reads a Variant payload after its header has already been consumed.
        /// </summary>
        /// <param name="buffer">Source protocol stream.</param>
        /// <param name="flags">Datatype flags from the value header.</param>
        /// <param name="disableVlq">Uses fixed-width decoding for system-event payloads when set.</param>
        /// <returns>The decoded Variant value.</returns>
        internal static ValueVariant Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            if (disableVlq)
            {
                S7p.DecodeUInt32(buffer, out var fixedValue);
                return new ValueVariant(fixedValue, flags);
            }

            S7p.DecodeUInt32Vlq(buffer, out var value);
            return new ValueVariant(value, flags);
        }
    }

    /// <summary>
    /// Represents a regular or address array of homogeneous Variant payloads.
    /// </summary>
    internal sealed class ValueVariantArray : PValue
    {
        private readonly ValueVariant[] _values;

        /// <summary>
        /// Initializes a Variant array while retaining the PLC collection shape.
        /// </summary>
        /// <param name="values">Decoded Variant elements.</param>
        /// <param name="flags">Regular-array or address-array datatype flags.</param>
        internal ValueVariantArray(ValueVariant[] values, byte flags)
        {
            _values = values ?? Array.Empty<ValueVariant>();
            DatatypeFlags = flags;
        }

        /// <summary>
        /// Gets a defensive copy of the decoded Variant elements.
        /// </summary>
        internal ValueVariant[] GetValue() => (ValueVariant[])_values.Clone();

        /// <summary>
        /// Writes the homogeneous array without repeating a datatype header for each element.
        /// </summary>
        /// <param name="buffer">Destination protocol stream.</param>
        /// <returns>Number of bytes written.</returns>
        public override int Serialize(Stream buffer)
        {
            var ret = S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.Variant);
            ret += S7p.EncodeUInt32Vlq(buffer, (uint)_values.Length);
            foreach (var value in _values)
            {
                ret += value.SerializePayload(buffer, disableVlq: false);
            }
            return ret;
        }

        /// <summary>
        /// Reads a counted sequence of bare Variant payloads.
        /// </summary>
        /// <param name="buffer">Source protocol stream positioned at the element count.</param>
        /// <param name="flags">Collection-shape flags already read from the value header.</param>
        /// <param name="disableVlq">Uses fixed-width counters and payloads for system events when set.</param>
        /// <returns>The decoded Variant array.</returns>
        internal static ValueVariantArray Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            uint count;
            if (disableVlq)
            {
                S7p.DecodeUInt32(buffer, out count);
            }
            else
            {
                S7p.DecodeUInt32Vlq(buffer, out count);
            }

            var values = new ValueVariant[count];
            for (var index = 0; index < count; index++)
            {
                values[index] = ValueVariant.Deserialize(buffer, flags, disableVlq);
            }
            return new ValueVariantArray(values, flags);
        }
    }

    /// <summary>
    /// Represents a null-terminated map of Variant type identifiers to Variant payloads.
    /// </summary>
    internal sealed class ValueVariantSparseArray : PValue
    {
        private readonly Dictionary<uint, ValueVariant> _values;

        /// <summary>
        /// Initializes a sparse Variant collection.
        /// </summary>
        /// <param name="values">Variant payloads keyed by the PLC type identifier carried in the sparse key.</param>
        internal ValueVariantSparseArray(Dictionary<uint, ValueVariant> values)
        {
            _values = values ?? new Dictionary<uint, ValueVariant>();
            DatatypeFlags = FLAGS_SPARSEARRAY;
        }

        /// <summary>
        /// Gets a copy of the sparse Variant mapping.
        /// </summary>
        internal Dictionary<uint, ValueVariant> GetValue() => new Dictionary<uint, ValueVariant>(_values);

        /// <summary>
        /// Writes key/payload pairs followed by the protocol's zero-key terminator.
        /// </summary>
        /// <param name="buffer">Destination protocol stream.</param>
        /// <returns>Number of bytes written.</returns>
        public override int Serialize(Stream buffer)
        {
            var ret = S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.Variant);
            foreach (var value in _values)
            {
                ret += S7p.EncodeUInt32Vlq(buffer, value.Key);
                ret += value.Value.SerializePayload(buffer, disableVlq: false);
            }
            ret += S7p.EncodeByte(buffer, 0);
            return ret;
        }

        /// <summary>
        /// Reads sparse Variant pairs until the zero-key terminator is reached.
        /// </summary>
        /// <param name="buffer">Source protocol stream.</param>
        /// <param name="flags">Sparse-array flags already read from the value header.</param>
        /// <param name="disableVlq">Uses fixed-width keys and payloads for system events when set.</param>
        /// <returns>The decoded sparse Variant collection.</returns>
        internal static ValueVariantSparseArray Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            var values = new Dictionary<uint, ValueVariant>();
            while (true)
            {
                uint key;
                if (disableVlq)
                {
                    S7p.DecodeUInt32(buffer, out key);
                }
                else
                {
                    S7p.DecodeUInt32Vlq(buffer, out key);
                }
                if (key == 0)
                {
                    return new ValueVariantSparseArray(values) { DatatypeFlags = flags };
                }
                values.Add(key, ValueVariant.Deserialize(buffer, flags, disableVlq));
            }
        }
    }

    /// <summary>
    /// Represents a counted sequence of Struct payloads used by PLC lifecycle and alarm notifications.
    /// </summary>
    internal sealed class ValueStructArray : PValue
    {
        private readonly ValueStruct[] _values;

        /// <summary>
        /// Initializes a Struct array while preserving its collection-shape flags.
        /// </summary>
        /// <param name="values">Decoded Struct values.</param>
        /// <param name="flags">Regular-array or address-array datatype flags.</param>
        internal ValueStructArray(ValueStruct[] values, byte flags)
        {
            _values = values ?? Array.Empty<ValueStruct>();
            DatatypeFlags = flags;
        }

        /// <summary>
        /// Gets a defensive copy of the decoded Struct values.
        /// </summary>
        internal ValueStruct[] GetValue() => (ValueStruct[])_values.Clone();

        /// <summary>
        /// Writes a single array header followed by each bare Struct payload.
        /// </summary>
        /// <param name="buffer">Destination protocol stream.</param>
        /// <returns>Number of bytes written.</returns>
        public override int Serialize(Stream buffer)
        {
            var ret = S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.Struct);
            ret += S7p.EncodeUInt32Vlq(buffer, (uint)_values.Length);
            foreach (var value in _values)
            {
                ret += value.SerializePayload(buffer, disableVlq: false);
            }
            return ret;
        }

        /// <summary>
        /// Reads a counted sequence of Struct payloads, including nested and packed structs.
        /// </summary>
        /// <param name="buffer">Source protocol stream positioned at the element count.</param>
        /// <param name="flags">Collection-shape flags already read from the value header.</param>
        /// <param name="disableVlq">Uses fixed-width counters inside system-event payloads when set.</param>
        /// <returns>The decoded Struct array.</returns>
        internal static ValueStructArray Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            uint count;
            if (disableVlq)
            {
                S7p.DecodeUInt32(buffer, out count);
            }
            else
            {
                S7p.DecodeUInt32Vlq(buffer, out count);
            }

            var values = new ValueStruct[count];
            for (var index = 0; index < count; index++)
            {
                values[index] = ValueStruct.Deserialize(buffer, flags, disableVlq);
            }
            return new ValueStructArray(values, flags);
        }
    }

    internal class ValueStruct : PValue
    {
        UInt32 Value;
        private Dictionary<uint, PValue> Elements = new Dictionary<uint, PValue>();
        /// <summary>
        /// InterfaceTimestamp: Only relevant if Value is transmitted as Packed Struct.
        /// Used on transmitting Systemdatatypes in a compact way (e.g. DTL).
        /// </summary>
        public UInt64 PackedStructInterfaceTimestamp;
        public UInt32 PackedStructTransportFlags = (uint)PackedStructTransportFlagBits.AlwaysSet; // Use 2 as standard value (probably a bitfield)

        [Flags]
        public enum PackedStructTransportFlagBits
        {
            None = 0,
            ClassicNonoptimizedOffsets = 1 << 0,    // Is set when a struct is read from non-optimized datablock
            AlwaysSet = 1 << 1,                     // Is (so far) always set
            Count2Present = 1 << 10                 // If this bit is set, then there's a 2nd counter present. Which if for a rare case you can read an array of struct, if the complete size, the 1st for one element.
        }

        public ValueStruct(UInt32 value) : this (value, 0)
        {
        }

        public ValueStruct(UInt32 value, byte flags)
        {
            DatatypeFlags = flags;
            Value = value;
            Elements = new Dictionary<uint, PValue>();
        }

        public UInt32 GetValue()
        {
            return Value;
        }

        public void AddStructElement(uint id,  PValue elem)
        {
            Elements.Add(id, elem);
        }

        public PValue GetStructElement(uint id)
        {
            return Elements[id];
        }

        public override int Serialize(Stream buffer)
        {
            int ret = 0;

            ret += S7p.EncodeByte(buffer, DatatypeFlags);
            ret += S7p.EncodeByte(buffer, Datatype.Struct);
            ret += SerializePayload(buffer, disableVlq: false);
            return ret;
        }

        /// <summary>
        /// Writes the Struct body without a datatype header so it can be embedded in a homogeneous Struct array.
        /// </summary>
        /// <param name="buffer">Destination protocol stream.</param>
        /// <param name="disableVlq">Uses fixed-width counters for system-event payloads when set.</param>
        /// <returns>Number of bytes written.</returns>
        internal int SerializePayload(Stream buffer, bool disableVlq)
        {
            int ret = 0;
            ret += S7p.EncodeUInt32(buffer, Value);
            // Packed Struct, see comment in Deserialize
            if ((Value > 0x90000000 && Value < 0x9fffffff) || (Value > 0x02000000 && Value < 0x02ffffff))
            {
                // There should be only one Element? The key from the dictionary element is not used.
                // It's somewhat all hacked into the Struct variant...
                foreach (var elem in Elements)
                {
                    // The timestamp must be exactly the same as from browsing the Plc, otherwise we
                    // get an Error "InvalidTimestampInTypeSafeBlob"
                    ret += S7p.EncodeUInt64(buffer, PackedStructInterfaceTimestamp);

                    ret += disableVlq
                        ? S7p.EncodeUInt32(buffer, PackedStructTransportFlags)
                        : S7p.EncodeUInt32Vlq(buffer, PackedStructTransportFlags);

                    if (elem.Value.GetType() == typeof(ValueByteArray))
                    {
                        var barr = ((ValueByteArray)elem.Value).GetValue();
                        UInt32 elementcount = (UInt32)barr.Length;
                        ret += disableVlq
                            ? S7p.EncodeUInt32(buffer, elementcount)
                            : S7p.EncodeUInt32Vlq(buffer, elementcount);
                        if ((PackedStructTransportFlags & (uint)PackedStructTransportFlagBits.Count2Present) != 0)
                        {
                            ret += disableVlq
                                ? S7p.EncodeUInt32(buffer, elementcount)
                                : S7p.EncodeUInt32Vlq(buffer, elementcount);
                        }
                        // Don't use the Serialize method of ValueByteArray, because there is an additional header we don't want here.
                        for (int i = 0; i < barr.Length; i++)
                        {
                            ret += S7p.EncodeByte(buffer, barr[i]);
                        }
                    }
                    else
                    {
                        System.Diagnostics.Trace.WriteLine("ValueStruct.Serialize(): Elements[0] is not of type ValueByteArray");
                    }
                }
            }
            else
            {
                foreach (var elem in Elements)
                {
                    ret += disableVlq
                        ? S7p.EncodeUInt32(buffer, elem.Key)
                        : S7p.EncodeUInt32Vlq(buffer, elem.Key);
                    ret += elem.Value.Serialize(buffer);
                }
                ret += disableVlq
                    ? S7p.EncodeUInt32(buffer, 0)
                    : S7p.EncodeByte(buffer, 0); // List Terminator
            }
            return ret;
        }

        public override string ToString()
        {
            string s = "";
            s += "<Value type =\"Struct\">" + Environment.NewLine;
            s += "<ID>" + Value.ToString() + "</ID>" + Environment.NewLine;
            if ((Value > 0x90000000 && Value < 0x9fffffff) || (Value > 0x02000000 && Value < 0x02ffffff))
            {
                s += "<PackedStructInterfaceTimestamp>" + PackedStructInterfaceTimestamp.ToString() + "</PackedStructInterfaceTimestamp>" + Environment.NewLine;
                s += "<PackedStructTransportFlags>" + PackedStructTransportFlags.ToString() + "</PackedStructTransportFlags>" + Environment.NewLine;
            }
            foreach (var elem in Elements)
            {
                s += "<Element>" + Environment.NewLine;
                s += "<ID>" + elem.Key.ToString() + "</ID>" + Environment.NewLine;
                s += elem.Value.ToString() + Environment.NewLine;
                s += "</Element>" + Environment.NewLine;
            }
            s += "</Value>" + Environment.NewLine;
            return s;
        }

        public static ValueStruct Deserialize(Stream buffer, byte flags, bool disableVlq)
        {
            UInt32 value;
            ValueStruct stru;

            S7p.DecodeUInt32(buffer, out value);
            // Special handling of datatype struct and some specific ID ranges:
            // Some struct elements aren't transmitted as single elements. Instead they are packed (e.g. DTL-Struct).
            // The ID number range where this is used is only guessed (Type Info).
            if ((value > 0x90000000 && value < 0x9fffffff) || (value > 0x02000000 && value < 0x02ffffff))
            {
                // Packed Struct
                // These are system datatypes. Either the information about them must be read out of the CPU before,
                // or must be known before. As the data are transmitted as Bytearrays, return them in this type. Interpretation must be done later.
                stru = new ValueStruct(value, flags);

                S7p.DecodeUInt64(buffer, out stru.PackedStructInterfaceTimestamp);
                UInt32 transp_flags;
                UInt32 elementcount;
                if (!disableVlq)
                {
                    S7p.DecodeUInt32Vlq(buffer, out transp_flags);
                    S7p.DecodeUInt32Vlq(buffer, out elementcount);
                    if ((transp_flags & (uint)PackedStructTransportFlagBits.Count2Present) != 0)
                    {
                        // Here's an additional counter value, for whatever reason...
                        S7p.DecodeUInt32Vlq(buffer, out elementcount);
                    }
                }
                else
                {
                    S7p.DecodeUInt32(buffer, out transp_flags);
                    S7p.DecodeUInt32(buffer, out elementcount);
                    if ((transp_flags & (uint)PackedStructTransportFlagBits.Count2Present) != 0)
                    {
                        // Here's an additional counter value, for whatever reason...
                        S7p.DecodeUInt32(buffer, out elementcount);
                    }
                }
                stru.PackedStructTransportFlags = transp_flags;
                byte[] barr = new byte[elementcount];
                for (int i = 0; i < elementcount; i++)
                {
                    S7p.DecodeByte(buffer, out barr[i]);
                }
                ValueByteArray elem = new ValueByteArray(barr);
                stru.AddStructElement(value, elem);
            }
            else
            {
                PValue elem;
                stru = new ValueStruct(value, flags);
                if (!disableVlq)
                {
                    S7p.DecodeUInt32Vlq(buffer, out value);
                    while (value > 0)
                    {
                        elem = PValue.Deserialize(buffer, disableVlq);
                        stru.AddStructElement(value, elem);
                        S7p.DecodeUInt32Vlq(buffer, out value);
                    }
                }
                else
                {
                    S7p.DecodeUInt32(buffer, out value);
                    while (value > 0)
                    {
                        elem = PValue.Deserialize(buffer, disableVlq);
                        stru.AddStructElement(value, elem);
                        S7p.DecodeUInt32(buffer, out value);
                    }
                }
            }
            return stru;
        }
    }
}
