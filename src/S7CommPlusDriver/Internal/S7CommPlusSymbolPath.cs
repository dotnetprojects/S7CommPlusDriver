using System;
using System.Collections.Generic;

namespace S7CommPlusDriver.Internal
{
    /// <summary>
    /// Splits and normalizes Siemens symbolic paths that may contain double-quoted identifiers.
    /// </summary>
    /// <remarks>
    /// A quoted segment starts with '"' and runs to the next '"', so dots inside a quoted DB or UDT name are not treated as
    /// hierarchy separators (for example <c>"SC.ServoAxis.db_AlarmList"</c>).
    /// </remarks>
    internal static class S7CommPlusSymbolPath
    {
        /// <summary>Splits a symbol path at dots while respecting double-quoted identifiers.</summary>
        /// <param name="symbolPath">The symbolic path to split.</param>
        /// <returns>The path segments in declaration order.</returns>
        internal static List<string> Split(string symbolPath)
        {
            var segments = new List<string>();
            if (symbolPath == null)
            {
                return segments;
            }

            var i = 0;
            while (i < symbolPath.Length)
            {
                if (symbolPath[i] == '"')
                {
                    var endQuote = symbolPath.IndexOf('"', i + 1);
                    if (endQuote < 0)
                    {
                        segments.Add(symbolPath.Substring(i));
                        break;
                    }

                    segments.Add(symbolPath.Substring(i, endQuote - i + 1));
                    i = endQuote + 1;
                    if (i < symbolPath.Length && symbolPath[i] == '.')
                    {
                        i++;
                    }
                }
                else
                {
                    var nextDot = symbolPath.IndexOf('.', i);
                    if (nextDot < 0)
                    {
                        segments.Add(symbolPath.Substring(i));
                        break;
                    }

                    segments.Add(symbolPath.Substring(i, nextDot - i));
                    i = nextDot + 1;
                }
            }

            return segments;
        }

        /// <summary>Removes surrounding double quotes from a segment, if present.</summary>
        /// <param name="segment">One path segment.</param>
        /// <returns>The unquoted segment.</returns>
        internal static string Unquote(string segment)
        {
            return segment.Length >= 2 && segment[0] == '"' && segment[segment.Length - 1] == '"'
                ? segment.Substring(1, segment.Length - 2)
                : segment;
        }

        /// <summary>Wraps an identifier in double quotes when it contains a dot.</summary>
        /// <param name="name">A member or block name.</param>
        /// <returns>The name quoted only when <see cref="Split"/> would otherwise split it.</returns>
        internal static string QuoteIfContainsDot(string name)
        {
            return name.IndexOf('.') >= 0 ? "\"" + name + "\"" : name;
        }

        /// <summary>Removes all quoting from a symbol path so it matches catalog names.</summary>
        /// <param name="symbolPath">A possibly quoted symbolic path.</param>
        /// <returns>The path rebuilt from unquoted segments.</returns>
        internal static string Normalize(string symbolPath)
        {
            var segments = Split(symbolPath);
            for (var i = 0; i < segments.Count; i++)
            {
                segments[i] = Unquote(segments[i]);
            }

            return string.Join(".", segments);
        }
    }
}
