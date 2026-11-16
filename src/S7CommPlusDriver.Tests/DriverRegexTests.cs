using S7CommPlusDriver.Internal;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    public sealed class DriverRegexTests
    {
        [Theory]
        [InlineData("1;6ES7 214-1AG40-0XB0 ;V4.5", "214", "4.5")]
        [InlineData("1;6ES7 510-1DJ01-0AB0;v2.9", "510", "2.9")]
        [InlineData("1;6ES7 672-7FC01-0YA0;S21.9", "672", "21.9")]
        [InlineData("1;6ES7 SIM-01500-APLC;r4.1", "500", "4.1")]
        public void SessionVersionPreservesDeviceAndFirmwareCaptures(string text, string device, string firmware)
        {
            var match = DriverRegex.SessionVersion().Match(text);

            Assert.True(match.Success);
            Assert.Equal(device, match.Groups[1].Value);
            Assert.Equal(firmware, match.Groups[2].Value);
        }

        [Theory]
        [InlineData("[-2].Member", "-2")]
        [InlineData("[1][2]", "1")]
        public void SingleDimensionIndexLeavesMemberAndNestedArraySuffixes(string text, string index)
        {
            var match = DriverRegex.SingleDimensionArrayIndex().Match(text);

            Assert.True(match.Success);
            Assert.Equal(index, match.Groups[1].Value);
            Assert.Equal(index.Length + 2, match.Length);
        }

        [Fact]
        public void XmlEscapesPreserveHexadecimalCapturesAndCaseSensitivity()
        {
            var matches = DriverRegex.EscapedXmlCharacter().Matches("A_x0020_B_x00C4__x00c4_");

            Assert.Equal(2, matches.Count);
            Assert.Equal("0020", matches[0].Groups[1].Value);
            Assert.Equal("00C4", matches[1].Groups[1].Value);
        }
    }
}
