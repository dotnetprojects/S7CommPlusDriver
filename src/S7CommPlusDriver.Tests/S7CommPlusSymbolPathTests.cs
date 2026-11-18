using System.Linq;
using S7CommPlusDriver.Internal;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    /// <summary>
    /// Tests for the symbol-path quoting/splitting helpers used by the browse implementation.
    /// </summary>
    public class S7CommPlusSymbolPathTests
    {
        [Fact]
        public void SplitSymbolPathSimpleDottedPathSplitsOnEveryDot()
        {
            var result = S7CommPlusSymbolPath.Split("DB1.Member1.Member2");

            Assert.Equal(new[] { "DB1", "Member1", "Member2" }, result.ToArray());
        }

        [Fact]
        public void SplitSymbolPathSingleSegmentReturnsSingleSegment()
        {
            var result = S7CommPlusSymbolPath.Split("DB1");

            Assert.Equal(new[] { "DB1" }, result.ToArray());
        }

        [Fact]
        public void SplitSymbolPathQuotedDbNameWithDotsKeepsQuotedSegmentIntact()
        {
            var result = S7CommPlusSymbolPath.Split("\"SC.EX.db_AlarmList\".Member1");

            Assert.Equal(new[] { "\"SC.EX.db_AlarmList\"", "Member1" }, result.ToArray());
        }

        [Fact]
        public void SplitSymbolPathQuotedSegmentInTheMiddleDoesNotSplitInsideQuotes()
        {
            var result = S7CommPlusSymbolPath.Split("DB1.\"Some.Name\".Member2");

            Assert.Equal(new[] { "DB1", "\"Some.Name\"", "Member2" }, result.ToArray());
        }

        [Fact]
        public void SplitSymbolPathArrayIndexSegmentKeepsBracketsInSegment()
        {
            var result = S7CommPlusSymbolPath.Split("DB1.arr[1].Member");

            Assert.Equal(new[] { "DB1", "arr[1]", "Member" }, result.ToArray());
        }

        [Fact]
        public void SplitSymbolPathMultiDimArrayIndexSegmentKeepsBracketsInSegment()
        {
            var result = S7CommPlusSymbolPath.Split("DB1.arr[2,4]");

            Assert.Equal(new[] { "DB1", "arr[2,4]" }, result.ToArray());
        }

        [Fact]
        public void SplitSymbolPathUnterminatedQuoteTakesRemainderAsOneSegment()
        {
            var result = S7CommPlusSymbolPath.Split("DB1.\"Unterminated");

            Assert.Equal(new[] { "DB1", "\"Unterminated" }, result.ToArray());
        }

        [Theory]
        [InlineData("\"SC.EX.db_AlarmList\"", "SC.EX.db_AlarmList")]
        [InlineData("DB1", "DB1")]
        [InlineData("\"\"", "")]
        public void UnquoteRemovesSurroundingQuotesIfPresent(string input, string expected)
        {
            Assert.Equal(expected, S7CommPlusSymbolPath.Unquote(input));
        }

        [Fact]
        public void UnquoteSingleCharacterIsReturnedUnchanged()
        {
            Assert.Equal("\"", S7CommPlusSymbolPath.Unquote("\""));
        }

        [Theory]
        [InlineData("SC.EX.db_AlarmList", "\"SC.EX.db_AlarmList\"")]
        [InlineData("DB1", "DB1")]
        public void QuoteIfContainsDotQuotesOnlyWhenNameContainsDot(string input, string expected)
        {
            Assert.Equal(expected, S7CommPlusSymbolPath.QuoteIfContainsDot(input));
        }

        [Fact]
        public void QuoteThenUnquoteRoundTripsForNameWithDots()
        {
            const string Original = "SC.EX.db_AlarmList";

            var quoted = S7CommPlusSymbolPath.QuoteIfContainsDot(Original);
            var unquoted = S7CommPlusSymbolPath.Unquote(quoted);

            Assert.Equal(Original, unquoted);
        }

        [Fact]
        public void SplitSymbolPathQuotedTopLevelNameRoundTripsThroughUnquote()
        {
            var quotedDbName = S7CommPlusSymbolPath.QuoteIfContainsDot("SC.EX.db_AlarmList");
            var fullPath = quotedDbName + ".Member1";

            var segments = S7CommPlusSymbolPath.Split(fullPath);

            Assert.Equal(2, segments.Count);
            Assert.Equal("SC.EX.db_AlarmList", S7CommPlusSymbolPath.Unquote(segments[0]));
            Assert.Equal("Member1", segments[1]);
        }
    }
}
