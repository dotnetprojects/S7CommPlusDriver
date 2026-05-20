using S7CommPlusDriver.Alarming;
using System;
using System.IO;

namespace S7CommPlusDriver
{
    internal sealed class AlarmData
    {
        public AlarmData(uint relationid)
        {
            RelationId = relationid;
        }

        public uint RelationId;
        public S7CommPlusConfiguredAlarmInfo MultipleStai;
        public S7CommPlusAlarmTexts AlText = new S7CommPlusAlarmTexts();

        public ulong GetCpuAlarmId()
        {
            return ((ulong)RelationId << 32) | ((ulong)MultipleStai.Alid << 16);
        }

        public int Deserialize(Stream buffer)
        {
            var ret = 0;
            MultipleStai = new S7CommPlusConfiguredAlarmInfo();
            ret += MultipleStai.Deserialize(buffer);
            return ret;
        }

        public override string ToString()
        {
            var s = string.Empty;
            s += "<AlarmData>" + Environment.NewLine;
            s += "<CpuAlarmId>" + GetCpuAlarmId().ToString() + "</CpuAlarmId>" + Environment.NewLine;
            s += "<RelationId>" + RelationId.ToString() + Environment.NewLine + "</RelationId>" + Environment.NewLine;
            s += "<MultipleStai>" + Environment.NewLine + MultipleStai.ToString() + "</MultipleStai>" + Environment.NewLine;
            s += "<AlText>" + Environment.NewLine;
            s += "<Infotext>" + AlText.Infotext + "</Infotext>" + Environment.NewLine;
            s += "<AlarmText>" + AlText.AlarmText + "</AlarmText>" + Environment.NewLine;
            s += "<AdditionalText1>" + AlText.AdditionalText1 + "</AdditionalText1>" + Environment.NewLine;
            s += "<AdditionalText2>" + AlText.AdditionalText2 + "</AdditionalText2>" + Environment.NewLine;
            s += "<AdditionalText3>" + AlText.AdditionalText3 + "</AdditionalText3>" + Environment.NewLine;
            s += "<AdditionalText4>" + AlText.AdditionalText4 + "</AdditionalText4>" + Environment.NewLine;
            s += "<AdditionalText5>" + AlText.AdditionalText5 + "</AdditionalText5>" + Environment.NewLine;
            s += "<AdditionalText6>" + AlText.AdditionalText6 + "</AdditionalText6>" + Environment.NewLine;
            s += "<AdditionalText7>" + AlText.AdditionalText7 + "</AdditionalText7>" + Environment.NewLine;
            s += "<AdditionalText8>" + AlText.AdditionalText8 + "</AdditionalText8>" + Environment.NewLine;
            s += "<AdditionalText9>" + AlText.AdditionalText9 + "</AdditionalText9>" + Environment.NewLine;
            s += "<UnknownValue1>" + AlText.UnknownValue1.ToString() + "</UnknownValue1>" + Environment.NewLine;
            s += "<UnknownValue2>" + AlText.UnknownValue2.ToString() + "</UnknownValue2>" + Environment.NewLine;
            s += "</AlText>" + Environment.NewLine;
            s += "</AlarmData>" + Environment.NewLine;
            return s;
        }
    }
}
