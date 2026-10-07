using S7CommPlusDriver.ClientApi;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    /// <summary>
    /// Verifies that <see cref="PlcTags.TagFactory"/> forwards the PLC-declared maximum string length to
    /// <see cref="PlcTagString"/>, <see cref="PlcTagStringArray"/>, and <see cref="PlcTagWString"/>, and that the resulting
    /// write telegram matches the declared length instead of always falling back to the built-in default of 254.
    /// </summary>
    /// <remarks>
    /// Regression test for a bug where <c>TagFactory</c> never received the declared maximum string length, so every
    /// <c>String</c>/<c>WString</c> tag was created with the built-in default of 254, regardless of its actual PLC
    /// declaration (e.g. <c>String[10]</c>). Writing such a tag then sent a telegram of the wrong size and with the wrong
    /// declared-length header byte, which the PLC rejected.
    /// </remarks>
    public sealed class PlcTagStringMaxLengthTests
    {
        [Fact]
        public void TagFactoryAppliesDeclaredMaxStringLengthToScalarStringTag()
        {
            var tag = PlcTags.TagFactory(
                "DB.Text",
                new ItemAddress("8A0E0001.F"),
                Softdatatype.S7COMMP_SOFTDATATYPE_STRING,
                Is1Dim: false,
                maxStringLength: 10);

            var stringTag = Assert.IsType<PlcTagString>(tag);
            stringTag.Value = "0123456789"; // exactly 10 characters, must not throw

            var writeValue = Assert.IsType<ValueUSIntArray>(GetWriteValue(stringTag));
            var bytes = writeValue.GetValue();

            Assert.Equal(12, bytes.Length); // declared max length (10) + 2 header bytes
            Assert.Equal(10, bytes[0]);     // declared max length, not the built-in default of 254
            Assert.Equal(10, bytes[1]);     // actual length
        }

        [Fact]
        public void TagFactoryFallsBackToDefaultMaxStringLengthWhenNotSupplied()
        {
            var tag = PlcTags.TagFactory(
                "DB.Text",
                new ItemAddress("8A0E0001.F"),
                Softdatatype.S7COMMP_SOFTDATATYPE_STRING);

            var stringTag = Assert.IsType<PlcTagString>(tag);
            stringTag.Value = "Hello";

            var writeValue = Assert.IsType<ValueUSIntArray>(GetWriteValue(stringTag));
            var bytes = writeValue.GetValue();

            Assert.Equal(256, bytes.Length); // built-in default of 254 + 2 header bytes
            Assert.Equal(254, bytes[0]);
        }

        [Fact]
        public void TagFactoryAppliesDeclaredMaxStringLengthToStringArrayElementTag()
        {
            var tag = PlcTags.TagFactory(
                "DB.Texts",
                new ItemAddress("8A0E0001.F"),
                Softdatatype.S7COMMP_SOFTDATATYPE_STRING,
                Is1Dim: true,
                maxStringLength: 5);

            Assert.IsType<PlcTagStringArray>(tag);
            Assert.Equal(5, tag.GetMaxStringLength());
        }

        [Fact]
        public void TagFactoryAppliesDeclaredMaxStringLengthToWStringTag()
        {
            var tag = PlcTags.TagFactory(
                "DB.UnicodeText",
                new ItemAddress("8A0E0001.F"),
                Softdatatype.S7COMMP_SOFTDATATYPE_WSTRING,
                Is1Dim: false,
                maxStringLength: 20);

            var wstringTag = Assert.IsType<PlcTagWString>(tag);
            Assert.Equal(20, wstringTag.GetMaxStringLength());
        }

        private static PValue GetWriteValue(PlcTag tag)
        {
            return tag.GetWriteValue();
        }
    }
}
