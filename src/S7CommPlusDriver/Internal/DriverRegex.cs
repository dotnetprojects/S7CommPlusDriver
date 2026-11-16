using System.Text.RegularExpressions;

namespace S7CommPlusDriver.Internal
{
    internal static partial class DriverRegex
    {
        private const string SingleDimensionArrayIndexPattern = @"^\[(-?\d+)\]";
        private const string MultiDimensionArrayIndexPattern = @"^\[( ?-?\d+ ?(, ?-?\d+ ?)*)\]";
        private const string SessionVersionPattern = @"^[^;]*;[^;]*[17]\s?(\d{3}).*;[VSR](\d{1,2}\.\d+)$";
        private const string EscapedXmlCharacterPattern = "_x([0-9A-F]{4})_";
        private const RegexOptions SessionVersionOptions = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

#if NET7_0_OR_GREATER
        [GeneratedRegex(SingleDimensionArrayIndexPattern)]
        internal static partial Regex SingleDimensionArrayIndex();

        [GeneratedRegex(MultiDimensionArrayIndexPattern)]
        internal static partial Regex MultiDimensionArrayIndex();

        [GeneratedRegex(SessionVersionPattern, SessionVersionOptions)]
        internal static partial Regex SessionVersion();

        [GeneratedRegex(EscapedXmlCharacterPattern)]
        internal static partial Regex EscapedXmlCharacter();
#else
        // Older targets lack the regex source generator. Compile each pattern only once.
        private static readonly Regex SingleDimensionArrayIndexRegex = new Regex(SingleDimensionArrayIndexPattern, RegexOptions.Compiled);
        private static readonly Regex MultiDimensionArrayIndexRegex = new Regex(MultiDimensionArrayIndexPattern, RegexOptions.Compiled);
        private static readonly Regex SessionVersionRegex = new Regex(SessionVersionPattern, RegexOptions.Compiled | SessionVersionOptions);
        private static readonly Regex EscapedXmlCharacterRegex = new Regex(EscapedXmlCharacterPattern, RegexOptions.Compiled);

        internal static Regex SingleDimensionArrayIndex() => SingleDimensionArrayIndexRegex;
        internal static Regex MultiDimensionArrayIndex() => MultiDimensionArrayIndexRegex;
        internal static Regex SessionVersion() => SessionVersionRegex;
        internal static Regex EscapedXmlCharacter() => EscapedXmlCharacterRegex;
#endif
    }
}
