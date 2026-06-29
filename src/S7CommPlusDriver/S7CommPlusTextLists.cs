using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace S7CommPlusDriver
{
    public enum S7CommPlusTextListScope
    {
        LanguageIndependent,
        LanguageSpecific
    }

    public enum S7CommPlusTextListType
    {
        Unknown,
        User,
        System
    }

    public sealed class S7CommPlusTextListEntry
    {
        public S7CommPlusTextListEntry(int from, int to, string text)
        {
            From = from;
            To = to;
            Text = text ?? String.Empty;
        }

        public int From { get; }
        public int To { get; }
        public string Text { get; }
        public bool IsRange => From != To;
    }

    public sealed class S7CommPlusTextList
    {
        private readonly Dictionary<int, S7CommPlusTextListEntry> _entriesByValue;

        public S7CommPlusTextList(int listId, int languageId, S7CommPlusTextListScope scope, IEnumerable<S7CommPlusTextListEntry> entries)
            : this(listId, languageId, scope, S7CommPlusTextListType.Unknown, entries)
        {
        }

        public S7CommPlusTextList(int listId, int languageId, S7CommPlusTextListScope scope, S7CommPlusTextListType textListType, IEnumerable<S7CommPlusTextListEntry> entries)
        {
            ListId = listId;
            LanguageId = languageId;
            Scope = scope;
            TextListType = textListType;
            Entries = (entries ?? Enumerable.Empty<S7CommPlusTextListEntry>()).ToList().AsReadOnly();
            _entriesByValue = Entries
                .Where(entry => !entry.IsRange)
                .GroupBy(entry => entry.From)
                .ToDictionary(group => group.Key, group => group.First());
        }

        public int ListId { get; }
        public int LanguageId { get; }
        public S7CommPlusTextListScope Scope { get; }
        public S7CommPlusTextListType TextListType { get; }
        public IReadOnlyList<S7CommPlusTextListEntry> Entries { get; }

        public bool TryResolve(long value, out string text)
        {
            text = null;
            if (value < Int32.MinValue || value > Int32.MaxValue)
            {
                return false;
            }

            var intValue = (int)value;
            if (_entriesByValue.TryGetValue(intValue, out var entry))
            {
                text = entry.Text;
                return true;
            }

            entry = Entries.FirstOrDefault(item => item.IsRange && intValue >= item.From && intValue <= item.To);
            if (entry == null)
            {
                return false;
            }

            text = entry.Text;
            return true;
        }
    }

    public sealed class S7CommPlusTextListCatalog
    {
        public static readonly S7CommPlusTextListCatalog Empty = new S7CommPlusTextListCatalog(Array.Empty<int>(), Array.Empty<S7CommPlusTextList>());

        private readonly Dictionary<Tuple<int, int>, S7CommPlusTextList> _listsByLanguageAndId;

        public S7CommPlusTextListCatalog(IEnumerable<int> languageIds, IEnumerable<S7CommPlusTextList> textLists)
        {
            LanguageIds = (languageIds ?? Enumerable.Empty<int>()).Distinct().ToList().AsReadOnly();
            TextLists = (textLists ?? Enumerable.Empty<S7CommPlusTextList>()).ToList().AsReadOnly();
            _listsByLanguageAndId = TextLists
                .GroupBy(list => Tuple.Create(list.LanguageId, list.ListId))
                .ToDictionary(group => group.Key, group => group.First());
        }

        public IReadOnlyList<int> LanguageIds { get; }
        public IReadOnlyList<S7CommPlusTextList> TextLists { get; }

        public string ResolveText(string textListName, long value, int languageId)
        {
            return TryResolve(textListName, value, languageId, out var text) ? text : null;
        }

        public bool TryResolve(string textListName, long value, int languageId, out string text)
        {
            text = null;
            if (!TryParseTextListId(textListName, out var listId, out var suffix))
            {
                return false;
            }

            if (TryResolve(listId, value, languageId, out text))
            {
                return true;
            }

            // Some CPU system-diagnostic placeholders use the TIA display name (for example 7W)
            // while the runtime table stores the previous numeric id.
            if (StringComparer.OrdinalIgnoreCase.Equals(suffix, "W") && listId > 0)
            {
                return TryResolve(listId - 1, value, languageId, out text);
            }

            return false;
        }

        public bool TryResolve(int listId, long value, int languageId, out string text)
        {
            text = null;
            if (TryResolveInLanguage(listId, value, languageId, out text))
            {
                return true;
            }

            if (languageId != 0 && TryResolveInLanguage(listId, value, 0, out text))
            {
                return true;
            }

            return false;
        }

        private bool TryResolveInLanguage(int listId, long value, int languageId, out string text)
        {
            text = null;
            if (!_listsByLanguageAndId.TryGetValue(Tuple.Create(languageId, listId), out var list))
            {
                return false;
            }

            return list.TryResolve(value, out text);
        }

        private static bool TryParseTextListId(string textListName, out int listId, out string suffix)
        {
            listId = 0;
            suffix = String.Empty;
            if (String.IsNullOrWhiteSpace(textListName))
            {
                return false;
            }

            var pos = 0;
            while (pos < textListName.Length && Char.IsDigit(textListName[pos]))
            {
                pos++;
            }

            if (pos == 0)
            {
                return false;
            }

            suffix = textListName.Substring(pos);
            return Int32.TryParse(textListName.Substring(0, pos), NumberStyles.None, CultureInfo.InvariantCulture, out listId);
        }
    }
}
