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

namespace S7CommPlusDriver.Alarming
{
    public class S7CommPlusAlarmStateChange
    {
        public enum SubtypeIds
        {
            Coming = Ids.DAI_Coming,
            Going = Ids.DAI_Going
        }
        public uint SubtypeId; // This is not part of the class, but is neccessary to store the information: 2673 = DAI.Coming, 2677 = DAI.Going

        public byte AllStatesInfo;
        public DateTime Timestamp;
        public S7CommPlusAlarmAssociatedValues AssociatedValues;
        public DateTime AckTimestamp;

        public override string ToString()
        {
            string s = "<S7CommPlusAlarmStateChange>" + Environment.NewLine;
            s += "<SubtypeId>" + SubtypeId.ToString() + "</SubtypeId>" + Environment.NewLine;
            s += "<SubtypeIdName>" + ((SubtypeIds)SubtypeId).ToString() + "</SubtypeIdName>" + Environment.NewLine;
            s += "<AllStatesInfo>" + AllStatesInfo.ToString() + "</AllStatesInfo>" + Environment.NewLine;
            s += "<AssociatedValues>" + Environment.NewLine + AssociatedValues.ToString() + "</AssociatedValues>" + Environment.NewLine;
            s += "<Timestamp>" + Timestamp.ToString() + "</Timestamp>" + Environment.NewLine;
            s += "<AckTimestamp>" + AckTimestamp.ToString() + "</AckTimestamp>" + Environment.NewLine;
            s += "</S7CommPlusAlarmStateChange>" + Environment.NewLine;
            return s;
        }

        internal static S7CommPlusAlarmStateChange FromValueStruct(ValueStruct str)
        {
            var asCgs = new S7CommPlusAlarmStateChange();
            asCgs.AllStatesInfo = ((ValueUSInt)str.GetStructElement(Ids.AS_CGS_AllStatesInfo)).GetValue();
            asCgs.Timestamp = Utils.DtFromValueTimestamp(((ValueTimestamp)str.GetStructElement(Ids.AS_CGS_Timestamp)).GetValue());
            asCgs.AssociatedValues = S7CommPlusAlarmAssociatedValues.FromValueBlob(((ValueBlobArray)str.GetStructElement(Ids.AS_CGS_AssociatedValues)));
            asCgs.AckTimestamp = Utils.DtFromValueTimestamp(((ValueTimestamp)str.GetStructElement(Ids.AS_CGS_AckTimestamp)).GetValue());
            return asCgs;
        }
    }
}
