using S7CommPlusDriver.Alarming;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    public class AlarmTextFormatterTests
    {
        [Fact]
        public void FormatReplacesAllTiaTagDisplayTypes()
        {
            var values = new S7CommPlusAlarmAssociatedValues();
            values.SD_1 = CreateInt(123);
            values.SD_2 = CreateUInt(0x1Au);
            values.SD_3 = CreateReal(12.345);
            values.SD_4 = CreateString("Motor A");

            var formatted = S7CommPlusAlarmTextFormatter.Format(
                "d=@1%d@ u=@2%u@ x=@2%04X@ b=@2%08b@ f=@3%.2f@ s=@4%s@",
                values,
                1033);

            Assert.Equal("d=123 u=26 x=16#001A b=2#00011010 f=12.35 s=Motor A", formatted);
        }

        [Fact]
        public void FormatSupportsSystemDiagnosisWidthPrefixBeforePercent()
        {
            var values = new S7CommPlusAlarmAssociatedValues();
            values.SD_2 = CreateInt(31);

            var formatted = S7CommPlusAlarmTextFormatter.Format(
                "Kanal @2W%d@",
                values,
                1031);

            Assert.Equal("Kanal 31", formatted);
        }

        [Fact]
        public void FormatLeavesUnsupportedTextListPlaceholdersUnchanged()
        {
            var values = new S7CommPlusAlarmAssociatedValues();
            values.SD_1 = CreateInt(5);

            var formatted = S7CommPlusAlarmTextFormatter.Format(
                "State @1%t#AlarmStates@",
                values,
                1033);

            Assert.Equal("State @1%t#AlarmStates@", formatted);
        }

        [Fact]
        public void FormatResolvesTextListPlaceholdersWhenResolverIsProvided()
        {
            var values = new S7CommPlusAlarmAssociatedValues();
            values.SD_1 = CreateInt(5);

            var formatted = S7CommPlusAlarmTextFormatter.Format(
                "State @1%t#AlarmStates@",
                values,
                1033,
                (listName, value, languageId) =>
                    listName == "AlarmStates" && value == 5 && languageId == 1033
                        ? "Running"
                        : null);

            Assert.Equal("State Running", formatted);
        }

        [Fact]
        public void FormatLeavesOtherTemplateTokensUnchanged()
        {
            var values = new S7CommPlusAlarmAssociatedValues();
            values.SD_1 = CreateInt(5);

            var formatted = S7CommPlusAlarmTextFormatter.Format(
                "Block @BlockName@ operand $$Motor.Speed$$ value @1%d@",
                values,
                1033);

            Assert.Equal("Block @BlockName@ operand $$Motor.Speed$$ value 5", formatted);
        }

        [Fact]
        public void FormatUsesLanguageCultureForFloatingPointValues()
        {
            var values = new S7CommPlusAlarmAssociatedValues();
            values.SD_1 = CreateReal(12.5);

            var formatted = S7CommPlusAlarmTextFormatter.Format(
                "Wert @1%.1f@",
                values,
                1031);

            Assert.Equal("Wert 12,5", formatted);
        }

        private static S7CommPlusAlarmAssociatedValue CreateInt(long value)
        {
            var associatedValue = new S7CommPlusAlarmAssociatedValue(Ids.TI_DINT);
            associatedValue.SetInt(value);
            return associatedValue;
        }

        private static S7CommPlusAlarmAssociatedValue CreateUInt(long value)
        {
            var associatedValue = new S7CommPlusAlarmAssociatedValue(Ids.TI_UDINT);
            associatedValue.SetInt(value);
            return associatedValue;
        }

        private static S7CommPlusAlarmAssociatedValue CreateReal(double value)
        {
            var associatedValue = new S7CommPlusAlarmAssociatedValue(Ids.TI_LREAL);
            associatedValue.SetReal(value);
            return associatedValue;
        }

        private static S7CommPlusAlarmAssociatedValue CreateString(string value)
        {
            var associatedValue = new S7CommPlusAlarmAssociatedValue(Ids.TI_STRING);
            associatedValue.SetString(value);
            return associatedValue;
        }
    }
}
