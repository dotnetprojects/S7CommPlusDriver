using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace S7CommPlusDriver
{
    /// <summary>
    /// Contains the multilingual PLC engineering comments retrieved for one data block or absolute I/Q/M area.
    /// </summary>
    /// <remarks>
    /// Data-block comments are indexed by normalized symbolic names because TIA comment paths use engineering model IDs rather
    /// than runtime access IDs. Absolute-area comments are indexed by their exact S7CommPlus access sequence.
    /// </remarks>
    public sealed class S7CommPlusSymbolCommentCatalog
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>> _commentsBySymbol;
        private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>> _commentsByAccessSequence;

        /// <summary>
        /// Initializes an immutable catalog produced from one PLC comment response.
        /// </summary>
        /// <param name="relationId">The data-block or absolute-area relation ID.</param>
        /// <param name="name">The PLC block or area name when supplied by the CPU.</param>
        /// <param name="commentsBySymbol">Comments indexed by normalized PLC symbol.</param>
        /// <param name="commentsByAccessSequence">Comments indexed by exact access sequence.</param>
        internal S7CommPlusSymbolCommentCatalog(
            uint relationId,
            string name,
            IDictionary<string, IReadOnlyDictionary<int, string>> commentsBySymbol,
            IDictionary<string, IReadOnlyDictionary<int, string>> commentsByAccessSequence)
        {
            RelationId = relationId;
            Name = name ?? string.Empty;
            _commentsBySymbol = new ReadOnlyDictionary<string, IReadOnlyDictionary<int, string>>(
                new Dictionary<string, IReadOnlyDictionary<int, string>>(
                    commentsBySymbol ?? new Dictionary<string, IReadOnlyDictionary<int, string>>(),
                    StringComparer.Ordinal));
            _commentsByAccessSequence = new ReadOnlyDictionary<string, IReadOnlyDictionary<int, string>>(
                new Dictionary<string, IReadOnlyDictionary<int, string>>(
                    commentsByAccessSequence ?? new Dictionary<string, IReadOnlyDictionary<int, string>>(),
                    StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets the relation ID whose comments are represented by this catalog.
        /// </summary>
        public uint RelationId { get; }

        /// <summary>
        /// Gets the PLC block or area name returned with the comment metadata.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the number of distinct PLC declarations that contain at least one usable localized comment.
        /// </summary>
        public int Count => _commentsBySymbol.Count + _commentsByAccessSequence.Count;

        /// <summary>
        /// Looks up all available localized comments for one variable returned by <see cref="S7CommPlusClient.BrowseAsync(System.Threading.CancellationToken)"/>.
        /// </summary>
        /// <param name="variable">The browsed variable whose original symbol and access sequence identify its declaration.</param>
        /// <param name="comments">Receives comments indexed by Windows locale identifier (LCID).</param>
        /// <returns><see langword="true"/> when the PLC supplied at least one comment for the declaration.</returns>
        /// <remarks>
        /// Array indices are ignored for data-block lookup because TIA stores one engineering comment on the array declaration,
        /// not a separate comment for every runtime element. Absolute I/Q/M access remains exact so a comment on a containing
        /// structure is not incorrectly inherited by each child signal.
        /// </remarks>
        public bool TryGetComments(VarInfo variable, out IReadOnlyDictionary<int, string> comments)
        {
            if (variable == null)
            {
                throw new ArgumentNullException(nameof(variable));
            }

            if (!string.IsNullOrEmpty(variable.Name)
                && _commentsBySymbol.TryGetValue(RemoveArrayIndices(variable.Name), out comments))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(variable.AccessSequence)
                && _commentsByAccessSequence.TryGetValue(variable.AccessSequence, out comments))
            {
                return true;
            }

            comments = null;
            return false;
        }

        /// <summary>
        /// Removes runtime array indices from a symbolic name while retaining every declaration member.
        /// </summary>
        /// <param name="symbol">A scalar, aggregate-array, or indexed-array PLC symbol.</param>
        /// <returns>The declaration-level symbol used by TIA comment metadata.</returns>
        private static string RemoveArrayIndices(string symbol)
        {
            var normalized = new StringBuilder(symbol.Length);
            var bracketDepth = 0;
            foreach (var character in symbol)
            {
                if (character == '[')
                {
                    bracketDepth++;
                }
                else if (character == ']' && bracketDepth > 0)
                {
                    bracketDepth--;
                }
                else if (bracketDepth == 0)
                {
                    normalized.Append(character);
                }
            }
            return normalized.ToString();
        }
    }

    /// <summary>
    /// Converts Siemens comment and block-interface XML into lookup keys used by <see cref="S7CommPlusSymbolCommentCatalog"/>.
    /// </summary>
    internal static class S7CommPlusSymbolCommentParser
    {
        /// <summary>
        /// Parses all localized declaration comments for one relation ID.
        /// </summary>
        /// <param name="relationId">The data-block or I/Q/M area relation ID.</param>
        /// <param name="name">The block or area name supplied by the PLC.</param>
        /// <param name="lineCommentsXml">The decompressed TIA line-comment XML.</param>
        /// <param name="interfaceDescriptionXml">The decompressed block interface used to translate DB model paths.</param>
        /// <returns>An immutable comment catalog; malformed individual entries are skipped.</returns>
        internal static S7CommPlusSymbolCommentCatalog Parse(
            uint relationId,
            string name,
            string lineCommentsXml,
            string interfaceDescriptionXml)
        {
            var commentsBySymbol = new Dictionary<string, IReadOnlyDictionary<int, string>>(StringComparer.Ordinal);
            var commentsByAccessSequence = new Dictionary<string, IReadOnlyDictionary<int, string>>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(lineCommentsXml))
            {
                return new S7CommPlusSymbolCommentCatalog(relationId, name, commentsBySymbol, commentsByAccessSequence);
            }

            var commentsDocument = XDocument.Parse(lineCommentsXml, LoadOptions.None);
            var interfaceModel = BlockInterfaceModel.TryCreate(interfaceDescriptionXml);
            foreach (var commentElement in commentsDocument.Descendants().Where(element => element.Name.LocalName == "Comment"))
            {
                var localizedComments = ReadLocalizedComments(commentElement);
                if (localizedComments.Count == 0)
                {
                    continue;
                }

                var modelPath = ReadAttribute(commentElement, "Path");
                if (!string.IsNullOrWhiteSpace(modelPath)
                    && interfaceModel != null
                    && interfaceModel.TryResolveMemberPath(modelPath, out var memberPath))
                {
                    var symbol = string.IsNullOrEmpty(name) ? memberPath : name + "." + memberPath;
                    commentsBySymbol[symbol] = localizedComments;
                    continue;
                }

                var referenceIdText = ReadAttribute(commentElement, "RefID");
                if (TryParseUInt32(referenceIdText, out var referenceId))
                {
                    commentsByAccessSequence[$"{relationId:X}.{referenceId:X}"] = localizedComments;
                }
            }

            return new S7CommPlusSymbolCommentCatalog(relationId, name, commentsBySymbol, commentsByAccessSequence);
        }

        /// <summary>
        /// Reads culture names and text values from one Siemens comment element.
        /// </summary>
        /// <param name="commentElement">The XML element containing one or more <c>DictEntry</c> children.</param>
        /// <returns>An immutable LCID-to-text map containing valid, non-empty entries.</returns>
        private static IReadOnlyDictionary<int, string> ReadLocalizedComments(XElement commentElement)
        {
            var comments = new Dictionary<int, string>();
            foreach (var entry in commentElement.Descendants().Where(element => element.Name.LocalName == "DictEntry"))
            {
                var language = ReadAttribute(entry, "Language") ?? ReadAttribute(entry, "Lanuage");
                if (!TryGetLanguageId(language, out var languageId) || string.IsNullOrWhiteSpace(entry.Value))
                {
                    continue;
                }
                comments[languageId] = entry.Value;
            }
            return new ReadOnlyDictionary<int, string>(comments);
        }

        /// <summary>
        /// Converts either a culture name or a numeric LCID into the identifier used by MCC and alarm APIs.
        /// </summary>
        /// <param name="language">A value such as <c>de-DE</c> or <c>1031</c>.</param>
        /// <param name="languageId">Receives the resolved LCID.</param>
        /// <returns><see langword="true"/> when the language can be represented as an LCID.</returns>
        private static bool TryGetLanguageId(string language, out int languageId)
        {
            if (Int32.TryParse(language, NumberStyles.Integer, CultureInfo.InvariantCulture, out languageId))
            {
                return true;
            }
            try
            {
                languageId = CultureInfo.GetCultureInfo(language ?? string.Empty).LCID;
                return true;
            }
            catch (CultureNotFoundException)
            {
                languageId = 0;
                return false;
            }
        }

        /// <summary>
        /// Reads an attribute without depending on XML namespace or attribute-name casing.
        /// </summary>
        /// <param name="element">The XML element whose attributes are searched.</param>
        /// <param name="name">The local attribute name.</param>
        /// <returns>The attribute value, or <see langword="null"/> when absent.</returns>
        private static string ReadAttribute(XElement element, string name)
        {
            return element.Attributes()
                .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
                ?.Value;
        }

        /// <summary>
        /// Parses decimal IDs and conventional hexadecimal values prefixed with <c>0x</c>.
        /// </summary>
        /// <param name="value">The serialized Siemens identifier.</param>
        /// <param name="result">Receives the numeric identifier.</param>
        /// <returns><see langword="true"/> when parsing succeeds.</returns>
        private static bool TryParseUInt32(string value, out uint result)
        {
            if (value?.StartsWith("0x", StringComparison.OrdinalIgnoreCase) == true)
            {
                return UInt32.TryParse(value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
            }
            return UInt32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }

        /// <summary>
        /// Represents the ordered TIA interface parts needed to translate comment model IDs into member names.
        /// </summary>
        private sealed class BlockInterfaceModel
        {
            private readonly XElement _rootPart;
            private readonly IReadOnlyList<XElement> _subParts;

            /// <summary>
            /// Initializes a model with the root DB part and its zero-based subpart table.
            /// </summary>
            /// <param name="rootPart">The DB source part containing top-level members.</param>
            /// <param name="subParts">The structure and external-datatype parts referenced through <c>SubPartIndex</c>.</param>
            private BlockInterfaceModel(XElement rootPart, IReadOnlyList<XElement> subParts)
            {
                _rootPart = rootPart;
                _subParts = subParts;
            }

            /// <summary>
            /// Parses the optional block interface while treating absent interface metadata as a valid empty model.
            /// </summary>
            /// <param name="interfaceDescriptionXml">The decompressed TIA block interface.</param>
            /// <returns>A usable model, or <see langword="null"/> when the response has no root part.</returns>
            internal static BlockInterfaceModel TryCreate(string interfaceDescriptionXml)
            {
                if (string.IsNullOrWhiteSpace(interfaceDescriptionXml))
                {
                    return null;
                }

                var document = XDocument.Parse(interfaceDescriptionXml, LoadOptions.None);
                var parts = document.Descendants()
                    .Where(element => element.Name.LocalName == "Part")
                    .Where(element => !string.Equals(ReadAttribute(element, "Kind"), "Comments", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var rootPartIndex = parts.FindIndex(part =>
                    string.Equals(ReadAttribute(part, "Kind"), "DBSource", StringComparison.OrdinalIgnoreCase));
                if (rootPartIndex < 0)
                {
                    return null;
                }

                return new BlockInterfaceModel(parts[rootPartIndex], parts.Skip(rootPartIndex + 1).ToList());
            }

            /// <summary>
            /// Resolves a colon-separated TIA model-ID path by following each member's subpart reference.
            /// </summary>
            /// <param name="modelPath">A path such as <c>51:65</c>.</param>
            /// <param name="memberPath">Receives a declaration path such as <c>General.FinePosScreenActive</c>.</param>
            /// <returns><see langword="true"/> when every model ID maps to a named member.</returns>
            internal bool TryResolveMemberPath(string modelPath, out string memberPath)
            {
                var currentPart = _rootPart;
                var names = new List<string>();
                var identifiers = modelPath.Split(':');
                for (var index = 0; index < identifiers.Length; index++)
                {
                    var member = currentPart.Descendants()
                        .FirstOrDefault(element => element.Name.LocalName == "Member"
                            && string.Equals(ReadAttribute(element, "ID"), identifiers[index], StringComparison.Ordinal));
                    var name = member == null ? null : ReadAttribute(member, "Name");
                    if (string.IsNullOrEmpty(name))
                    {
                        memberPath = null;
                        return false;
                    }
                    names.Add(name);

                    if (index + 1 < identifiers.Length)
                    {
                        var subPartIndexText = ReadAttribute(member, "SubPartIndex");
                        if (!Int32.TryParse(subPartIndexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var subPartIndex)
                            || subPartIndex < 0
                            || subPartIndex >= _subParts.Count)
                        {
                            memberPath = null;
                            return false;
                        }
                        currentPart = _subParts[subPartIndex];
                    }
                }

                memberPath = string.Join(".", names);
                return names.Count > 0;
            }
        }
    }
}
