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
            wstringTag.Value = "Hi";
            var words = Assert.IsType<ValueUIntArray>(wstringTag.GetWriteValue()).GetValue();
            Assert.Equal(22, words.Length);
            Assert.Equal((ushort)20, words[0]);
            Assert.Equal((ushort)2, words[1]);
            Assert.Equal((ushort)'H', words[2]);
            Assert.Equal((ushort)'i', words[3]);
            Assert.All(System.Linq.Enumerable.Skip(words, 4), word => Assert.Equal((ushort)0, word));
        }

        [Fact]
        public void OriginalFactorySignatureRemainsAvailableToCompiledCallers()
        {
            Assert.NotNull(typeof(PlcTags).GetMethod(nameof(PlcTags.TagFactory),
                new[] { typeof(string), typeof(ItemAddress), typeof(uint), typeof(bool) }));
        }

        [Fact]
        public void Utf8StringChecksBytesOnAssignmentAndAfterEncodingChanges()
        {
            var tag = new PlcTagString("Text", new ItemAddress("8A0E0001.F"), Softdatatype.S7COMMP_SOFTDATATYPE_STRING, 10);
            tag.SetStringEncoding("UTF-8");
            Assert.Throws<System.ArgumentOutOfRangeException>(() => tag.Value = "éééééé");
            tag.Value = "ééééé";
            Assert.Equal(10, Assert.IsType<ValueUSIntArray>(tag.GetWriteValue()).GetValue()[1]);
            tag.SetStringEncoding("ISO-8859-1");
            tag.Value = "éééééé";
            tag.SetStringEncoding("UTF-8");
            Assert.Throws<System.ArgumentOutOfRangeException>(() => tag.GetWriteValue());
        }

        [Fact]
        public void Utf8StringArrayChecksBytesEvenAfterItsValueArrayIsMutated()
        {
            var tag = new PlcTagStringArray("Texts", new ItemAddress("8A0E0001.F"), Softdatatype.S7COMMP_SOFTDATATYPE_STRING, 5);
            tag.SetStringEncoding("UTF-8");
            Assert.Throws<System.ArgumentOutOfRangeException>(() => tag.Value = new[] { "ééé" });
            tag.Value = new[] { "éé", "x" };
            var bytes = Assert.IsType<ValueUSIntArray>(tag.GetWriteValue()).GetValue();
            Assert.Equal(14, bytes.Length);
            Assert.Equal(4, bytes[1]);
            Assert.Equal(0, bytes[6]);
            Assert.Equal(5, bytes[7]);
            tag.Value[0] = "ééé";
            Assert.Throws<System.ArgumentOutOfRangeException>(() => tag.GetWriteValue());
        }

        private static PValue GetWriteValue(PlcTag tag)
        {
            return tag.GetWriteValue();
        }
    }
}
