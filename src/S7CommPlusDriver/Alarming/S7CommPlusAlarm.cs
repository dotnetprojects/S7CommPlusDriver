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

namespace S7CommPlusDriver.Alarming
{
    public class S7CommPlusAlarm
    {
        public string ObjectVariableTypeName;

        public ulong CpuAlarmId;
        public byte AllStatesInfo;
        public ushort AlarmDomain;
        public int MessageType;
        public uint SequenceCounter;
        public S7CommPlusAlarmTexts AlarmTexts;
        /// <summary>
        /// Alarm texts keyed by LCID when the PLC returned more than one language.
        /// </summary>
        public IReadOnlyDictionary<int, S7CommPlusAlarmTexts> AlarmTextsByLanguage { get; internal set; } = new Dictionary<int, S7CommPlusAlarmTexts>();
        public S7CommPlusAlarmHmiInfo HmiInfo;
        public S7CommPlusAlarmStateChange StateChange;

        public override string ToString()
        {
            string s = "<S7CommPlusAlarm>" + Environment.NewLine;
            s += "<ObjectVariableTypeName>" + ObjectVariableTypeName.ToString() + "</ObjectVariableTypeName>" + Environment.NewLine;
            s += "<CpuAlarmId>" + CpuAlarmId.ToString() + "</CpuAlarmId>" + Environment.NewLine;
            s += "<AllStatesInfo>" + AllStatesInfo.ToString() + "</AllStatesInfo>" + Environment.NewLine;
            s += "<AlarmDomain>" + AlarmDomain.ToString() + "</AlarmDomain>" + Environment.NewLine;
            s += "<MessageType>" + MessageType.ToString() + "</MessageType>" + Environment.NewLine;
            s += "<MessageTypeName>" + SiemensOmsEnumNames.AlarmMessageTypeName(MessageType) + "</MessageTypeName>" + Environment.NewLine;
            s += "<HmiInfo>" + Environment.NewLine + HmiInfo.ToString() + "</HmiInfo>" + Environment.NewLine;
            s += "<StateChange>" + Environment.NewLine + StateChange.ToString() + "</StateChange>" + Environment.NewLine;
            s += "<SequenceCounter>" + SequenceCounter.ToString() + "</SequenceCounter>" + Environment.NewLine;
            if (AlarmTexts != null)
            {
                s += "<AlarmTexts>" + Environment.NewLine + AlarmTexts.ToString() + "</AlarmTexts>" + Environment.NewLine;
            }
            else
            {
                s += "<AlarmTexts></AlarmTexts>" + Environment.NewLine;
            }
            s += "</S7CommPlusAlarm>" + Environment.NewLine;
            return s;
        }

        internal static S7CommPlusAlarm FromNotificationObject(PObject pobj, int alarmtextsLanguageId)
        {
            var dai = new S7CommPlusAlarm();
            dai.ObjectVariableTypeName = ((ValueWString)pobj.GetAttribute(Ids.ObjectVariableTypeName)).GetValue();
            dai.CpuAlarmId = ((ValueLWord)pobj.GetAttribute(Ids.DAI_CPUAlarmID)).GetValue();
            dai.AllStatesInfo = ((ValueUSInt)pobj.GetAttribute(Ids.DAI_AllStatesInfo)).GetValue();
            dai.AlarmDomain = ((ValueUInt)pobj.GetAttribute(Ids.DAI_AlarmDomain)).GetValue();
            dai.MessageType = ((ValueDInt)pobj.GetAttribute(Ids.DAI_MessageType)).GetValue();
            dai.HmiInfo = S7CommPlusAlarmHmiInfo.FromValueBlob(((ValueBlob)pobj.GetAttribute(Ids.DAI_HmiInfo)));
            // Additional-value blobs are protocol metadata for alarm formatting; keep the raw attribute available on pobj.
            dai.SequenceCounter = ((ValueUDInt)pobj.GetAttribute(Ids.DAI_SequenceCounter)).GetValue();
            ValueStruct str = null;
            uint dai_id = 0;
            if (pobj.Attributes.ContainsKey(Ids.DAI_Coming))
            {
                str = (ValueStruct)pobj.GetAttribute(Ids.DAI_Coming);
                dai_id = Ids.DAI_Coming;
            }
            else if (pobj.Attributes.ContainsKey(Ids.DAI_Going))
            {
                str = (ValueStruct)pobj.GetAttribute(Ids.DAI_Going);
                dai_id = Ids.DAI_Going;
            }
            if (dai_id == 0)
            {
                return null;
            }
            dai.StateChange = S7CommPlusAlarmStateChange.FromValueStruct(str);
            dai.StateChange.SubtypeId = dai_id;
            var alarmTextsByLanguage = S7CommPlusAlarmTexts.FromNotificationBlobAllLanguages((ValueBlobSparseArray)pobj.GetAttribute(Ids.DAI_AlarmTexts_Rid));
            foreach (var alarmTexts in alarmTextsByLanguage.Values)
            {
                alarmTexts.ApplyAssociatedValues(dai.StateChange.AssociatedValues);
            }

            dai.AlarmTextsByLanguage = alarmTextsByLanguage;
            if (alarmtextsLanguageId == 0)
            {
                dai.AlarmTexts = S7CommPlusAlarmTexts.FirstOrEmpty(alarmTextsByLanguage);
            }
            else if (!alarmTextsByLanguage.TryGetValue(alarmtextsLanguageId, out dai.AlarmTexts))
            {
                dai.AlarmTexts = new S7CommPlusAlarmTexts { LanguageId = alarmtextsLanguageId };
            }

            return dai;
        }
    }
}
