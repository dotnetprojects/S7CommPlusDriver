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
using System.Diagnostics;
using System.IO;
using System.Linq;
using S7CommPlusDriver.Alarming;
using S7CommPlusDriver.Internal;

namespace S7CommPlusDriver
{
    internal sealed class S7CommPlusAlarmBrowseService
    {
        private readonly S7CommPlusProtocolRequests _requests;

        public S7CommPlusAlarmBrowseService(IS7CommPlusProtocolSession session)
        {
            _requests = new S7CommPlusProtocolRequests(session);
        }

        /// <summary>
        /// Explores the AS and the User program alarms, gets the corresponding texts usind the language id (e.g.1031 = de-DE)
        ///
        /// Call example:
        /// CultureInfo ci = new CultureInfo("de-DE");
        /// Dictionary<ulong, AlarmData> Alarms = new Dictionary<ulong, AlarmData>();
        /// conn.ExploreASAlarms(ref Alarms, ci.LCID);
        /// foreach (var al in Alarms)
        /// {
        ///     System.Diagnostics.Trace.WriteLine(al.Value.ToString());
        /// }
        /// </summary>
        /// <param name="Alarms">Dictionary <ulong, AlarmData> where the results are written to. Key is used as address.</param>
        /// <param name="languageId">Language id for retrieving the text entries, use language code e.g. 1031 for german</param>
        /// <returns></returns>
        public int ExploreASAlarms(ref Dictionary<ulong, AlarmData> Alarms, int languageId)
        {
            int res;

            #region Explore all other than Alarm AP (AnwenderProgramAlarme)
            res = _requests.Explore(0x8a7e0000, null, out var exploreRes); // ASAlarms.0
            if (res != 0)
            {
                return res;
            }

            var alarmobjs = exploreRes.Objects.First(o => o.RelationId == 0x8a7e0000).GetObjects();
            var staiclass = alarmobjs.First(o => o.ClassId == Ids.MultipleSTAI_Class_Rid);

            if (staiclass != null)
            {
                PValue stais = staiclass.GetAttribute(Ids.MultipleSTAI_STAIs);
                if (stais != null)
                {
                    if (stais.GetType() == typeof(ValueBlobSparseArray))
                    {
                        var dict = ((ValueBlobSparseArray)stais).GetValue();
                        foreach (var entry in dict)
                        {
                            var alarm = new AlarmData(0x8a7e0000); // ASAlarms.0 relation.
                            Stream buffer = new MemoryStream(entry.Value.value);
                            alarm.Deserialize(buffer);
                            Alarms.Add(alarm.GetCpuAlarmId(), alarm);
                        }
                    }
                    else
                    {
                        System.Diagnostics.Trace.WriteLine("ExploreASAlarms(): stais is not ValueBlobSparseArray");
                    }
                }
                else
                {
                    System.Diagnostics.Trace.WriteLine("ExploreASAlarms(): stais = null");
                }
            }
            else
            {
                System.Diagnostics.Trace.WriteLine("ExploreASAlarms(): staiclass = null");
            }
            #endregion

            #region Explore Alarm AP
            // Add the requested attributes.
            var alarmProgramAttributes = new uint[] { Ids.ObjectVariableTypeParentObject, Ids.MultipleSTAI_STAIs };
            res = _requests.Explore(Ids.NativeObjects_thePLCProgram_Rid, alarmProgramAttributes, out exploreRes);
            if (res != 0)
            {
                return res;
            }

            // All objects which have Alarm AP inside, have a sub-Object with ID 7854 = MultipleSTAI.Class_Rid
            var obj = exploreRes.Objects.First(o => o.ClassId == Ids.PLCProgram_Class_Rid);

            foreach (var ob in obj.GetObjects())
            {
                var staiclasses = ob.GetObjectsByClassId(Ids.MultipleSTAI_Class_Rid);
                if (staiclasses != null && staiclasses.Count > 0)
                {
                    PValue stais = staiclasses[0].GetAttribute(Ids.MultipleSTAI_STAIs);
                    if (stais != null)
                    {
                        if (stais.GetType() == typeof(ValueBlobSparseArray))
                        {
                            var dict = ((ValueBlobSparseArray)stais).GetValue();
                            foreach (var entry in dict)
                            {
                                var alarm = new AlarmData(ob.RelationId);
                                Stream buffer = new MemoryStream(entry.Value.value);

                                alarm.Deserialize(buffer);
                                Alarms.Add(alarm.GetCpuAlarmId(), alarm);
                            }
                        }
                        else
                        {
                            System.Diagnostics.Trace.WriteLine("ExploreASAlarms(): stais is not ValueBlobSparseArray");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Trace.WriteLine("ExploreASAlarms(): stais = null");
                    }
                }
            }
            #endregion

            #region Explore AlarmTextLists

            var textLibraryRelationId = 0x8a360000 + (ushort)languageId; // There may be several languages, add language ID (e.g. 1031 = german / de-DE)
            res = _requests.Explore(textLibraryRelationId, null, out exploreRes, exploreChildsRecursive: 0);
            if (res != 0)
            {
                return res;
            }

            // TextLibraryOffsetArea is an array[3] of Blob, which contains offset information.
            // [0] contains informations of how to get infos from [1], which has infos about offsets in [2].
            // [2] contains the resulting offsets for the strings in TextLibraryStringArea.
            var textlib = exploreRes.Objects.FirstOrDefault(o => o.ClassId == Ids.TextLibraryClassRID);
            if (textlib == null ||
                textlib.GetAttribute(Ids.TextLibraryOffsetArea) is not ValueBlobArray offsetArea ||
                textlib.GetAttribute(Ids.TextLibraryStringArea) is not ValueBlob stringArea)
            {
                return S7Consts.errIsoInvalidPDU;
            }

            var tloa = offsetArea.GetValue();
            var tlsa = stringArea.GetValue();
            if (tloa.Length < 3 || tlsa == null)
            {
                return S7Consts.errIsoInvalidPDU;
            }

            var tloa_1 = tloa[0].GetValue();
            var tloa_2 = tloa[1].GetValue();
            var tloa_3 = tloa[2].GetValue();

            GetTexts(tloa_1, tloa_2, tloa_3, tlsa, ref Alarms, languageId);

            #endregion

            return 0;
        }

        private void GetTexts(byte[] tloa_1, byte[] tloa_2, byte[] tloa_3, byte[] tlsa, ref Dictionary<ulong, AlarmData> Alarms, int languageId)
        {
            uint pos1, pos2, pos3;
            uint t1_count, t1_relid, t1_relid_off;
            uint t2_count, t2_off;
            ushort t2_alid;
            uint t3_count, t3_off;
            byte t3_typeindex;

            ushort ts_len;
            string ts_s;

            ulong cpualarmid;

            // Step 1: Get RelationId from table 1
            pos1 = 16; // What is in the bytes before 16 is not known.

            t1_count = Utils.GetUInt32LE(tloa_1, pos1);
            pos1 += 4;

            for (int i = 0; i < t1_count; i++)
            {
                t1_relid = Utils.GetUInt32LE(tloa_1, pos1);
                pos1 += 4;
                t1_relid_off = Utils.GetUInt32LE(tloa_1, pos1);
                pos1 += 4;

                // Step 2: Start offset in table 3
                pos2 = t1_relid_off;
                t2_count = Utils.GetUInt32LE(tloa_2, pos2);
                pos2 += 4;
                for (int j = 0; j < t2_count; j++)
                {
                    t2_alid = Utils.GetUInt16LE(tloa_2, pos2);
                    pos2 += 2;
                    t2_off = Utils.GetUInt32LE(tloa_2, pos2);
                    pos2 += 4;
                    // This ID is used as unique identifier (address), which is also transferred later on alarm notification
                    cpualarmid = (ulong)t1_relid << 32 | (ulong)t2_alid << 16;

                    // Check if we have the key stored from response data before, to have storage where we can put the Text information in
                    if (Alarms.ContainsKey(cpualarmid) == false)
                    {
                        Trace.WriteLine(String.Format("BrowseAlarms GetTexts(): CPU Alarm Id {0:X} is not in dictionary!", cpualarmid));
                        continue;
                    }
                    Alarms[cpualarmid].AlText.LanguageId = languageId;

                    // Step 3: Get offsets to text array from table 3
                    pos3 = t2_off;
                    t3_count = Utils.GetUInt32LE(tloa_3, pos3);
                    pos3 += 4;
                    for (int k = 0; k < t3_count; k++)
                    {
                        // t3_typeindex:
                        // 0 = Infotext, 1 = AlarmText, 2..10 = AdditionalText1..AdditionalText9, 255 = Unknown 1 or 2 values
                        t3_typeindex = Utils.GetUInt8(tloa_3, pos3);
                        pos3 += 1;
                        t3_off = Utils.GetUInt32LE(tloa_3, pos3);
                        pos3 += 4;

                        // Step 4: Finally get the text and store the data
                        if (t3_typeindex == 255)
                        {
                            Alarms[cpualarmid].AlText.UnknownValue1 = Utils.GetUInt16LE(tlsa, t3_off);
                            Alarms[cpualarmid].AlText.UnknownValue2 = Utils.GetUInt16LE(tlsa, t3_off + 2);
                        }
                        else
                        {
                            ts_len = Utils.GetUInt16LE(tlsa, t3_off);
                            ts_s = Utils.GetUtfString(tlsa, t3_off + 2, ts_len);
                            switch (t3_typeindex)
                            {
                                case 0:
                                    Alarms[cpualarmid].AlText.Infotext = ts_s;
                                    break;
                                case 1:
                                    Alarms[cpualarmid].AlText.AlarmText = ts_s;
                                    break;
                                case 2:
                                    Alarms[cpualarmid].AlText.AdditionalText1 = ts_s;
                                    break;
                                case 3:
                                    Alarms[cpualarmid].AlText.AdditionalText2 = ts_s;
                                    break;
                                case 4:
                                    Alarms[cpualarmid].AlText.AdditionalText3 = ts_s;
                                    break;
                                case 5:
                                    Alarms[cpualarmid].AlText.AdditionalText4 = ts_s;
                                    break;
                                case 6:
                                    Alarms[cpualarmid].AlText.AdditionalText5 = ts_s;
                                    break;
                                case 7:
                                    Alarms[cpualarmid].AlText.AdditionalText6 = ts_s;
                                    break;
                                case 8:
                                    Alarms[cpualarmid].AlText.AdditionalText7 = ts_s;
                                    break;
                                case 9:
                                    Alarms[cpualarmid].AlText.AdditionalText8 = ts_s;
                                    break;
                                case 10:
                                    Alarms[cpualarmid].AlText.AdditionalText9 = ts_s;
                                    break;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Reads the active program alarms from the Plc (single poll).
        ///
        /// Call example:
        /// CultureInfo ci = new CultureInfo("de-DE");
        /// var alarmList = new List<S7CommPlusAlarm>();
        /// conn.GetActiveAlarms(out alarmList, ci.LCID);
        /// foreach (var a in alarmList)
        /// {
        ///     System.Diagnostics.Trace.WriteLine(a.ToString());
        /// }
        /// </summary>
        /// <param name="alarmList">Contains the alarms, empty if there is no active alarm</param>
        /// <param name="languageId">Language id for retrieving the text entries, use language code e.g. 1031 for german</param>
        /// <returns>0 on success</returns>
        public int GetActiveAlarms(out List<S7CommPlusAlarm> alarmList, int languageId, Func<string, long, int, string> textListResolver)
        {
            int res;

            alarmList = new List<S7CommPlusAlarm>();

            var exploreReq = new ExploreRequest(ProtocolVersion.V2);
            exploreReq.ExploreId = Ids.AlarmSubSystem;
            exploreReq.ExploreRequestId = Ids.AlarmSubSystem_Update_DAI;
            exploreReq.ExploreChildsRecursive = 1;
            exploreReq.ExploreParents = 0;

            // Add the requestes attributes.
            // Request the same attributes we get from an alarm notification, so we can reuse other methods.
            exploreReq.AddressList.Add(Ids.DynamicAlarmInstance_DAI_AlarmId);
            exploreReq.AddressList.Add(Ids.DynamicAlarmInstance_DAI_AllStateInformation);
            exploreReq.AddressList.Add(Ids.DynamicAlarmInstance_DAI_AlarmDomain);
            exploreReq.AddressList.Add(Ids.DynamicAlarmInstance_DAI_Coming);
            exploreReq.AddressList.Add(Ids.DynamicAlarmInstance_DAI_Going);
            exploreReq.AddressList.Add(Ids.DynamicAlarmInstance_DAI_Messagetype);
            exploreReq.AddressList.Add(Ids.DynamicAlarmInstance_DAI_HMIInformation);
            // Extra ones which we only need for compatibility with notification.
            exploreReq.AddressList.Add(Ids.ObjectVariableTypeName);
            exploreReq.AddressList.Add(Ids.DynamicAlarmInstance_DAI_SequenceCounter);
            exploreReq.AddressList.Add(Ids.DynamicAlarmInstance_DAI_Alarmtext);

            res = _requests.SendExplore(exploreReq, out var exploreRes);
            if (res != 0)
            {
                return res;
            }

            foreach (var obj in exploreRes.Objects)
            {
                alarmList.Add(S7CommPlusAlarm.FromNotificationObject(obj, languageId, textListResolver));
            }

            return 0;
        }
    }

}
