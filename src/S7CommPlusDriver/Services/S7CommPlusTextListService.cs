using S7CommPlusDriver.Internal;
using System;
using System.Collections.Generic;
using System.Linq;

namespace S7CommPlusDriver
{
    internal sealed class S7CommPlusTextListService
    {
        private const uint TextListLibraryBaseRelationId = 0x8a370000;
        private readonly S7CommPlusMetadataService _metadata;
        private readonly S7CommPlusProtocolRequests _requests;

        public S7CommPlusTextListService(IS7CommPlusProtocolSession session, S7CommPlusMetadataService metadata)
        {
            _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _requests = new S7CommPlusProtocolRequests(session);
        }

        public int GetTextLists(IEnumerable<int> languageIds, out S7CommPlusTextListCatalog catalog)
        {
            catalog = S7CommPlusTextListCatalog.Empty;
            var requestedLanguages = languageIds?.Distinct().ToList();
            if (requestedLanguages == null || requestedLanguages.Count == 0)
            {
                var cultureResult = _metadata.GetCpuCultureInfo(out var cultureInfo);
                if (cultureResult != 0)
                {
                    return cultureResult;
                }

                requestedLanguages = cultureInfo.LanguageIds.ToList();
            }

            if (requestedLanguages.Any(languageId => languageId < 0 || languageId > UInt16.MaxValue))
            {
                return S7Consts.errCliInvalidParams;
            }

            var lists = new List<S7CommPlusTextList>();
            var result = TryReadLibrary(0, TextListLibraryBaseRelationId, S7CommPlusTextListScope.LanguageIndependent, lists);
            if (result != 0)
            {
                return result;
            }

            foreach (var languageId in requestedLanguages)
            {
                if (languageId == 0)
                {
                    continue;
                }

                result = TryReadLibrary(
                    languageId,
                    TextListLibraryBaseRelationId + (ushort)languageId,
                    S7CommPlusTextListScope.LanguageSpecific,
                    lists);
                if (result != 0)
                {
                    return result;
                }
            }

            catalog = new S7CommPlusTextListCatalog(requestedLanguages, lists);
            return 0;
        }

        private int TryReadLibrary(int languageId, uint relationId, S7CommPlusTextListScope scope, List<S7CommPlusTextList> lists)
        {
            var result = _requests.Explore(relationId, null, out var response, exploreChildsRecursive: 0);
            if (result != 0)
            {
                return result;
            }

            var textLibrary = response?.Objects?.FirstOrDefault(obj => obj.ClassId == Ids.TextLibraryClassRID);
            if (textLibrary == null)
            {
                return 0;
            }

            if (!textLibrary.Attributes.TryGetValue(Ids.TextLibraryOffsetArea, out var offsetAreaValue) ||
                !textLibrary.Attributes.TryGetValue(Ids.TextLibraryStringArea, out var stringAreaValue) ||
                offsetAreaValue is not ValueBlobArray offsetArea ||
                stringAreaValue is not ValueBlob stringArea)
            {
                return S7Consts.errIsoInvalidPDU;
            }

            var offsets = offsetArea.GetValue();
            var strings = stringArea.GetValue();
            if (offsets == null || offsets.Length < 2 || strings == null)
            {
                return S7Consts.errIsoInvalidPDU;
            }

            return DecodeTextListLibrary(offsets[0].GetValue(), offsets[1].GetValue(), strings, languageId, scope, lists);
        }

        private static int DecodeTextListLibrary(
            byte[] listTable,
            byte[] entryTable,
            byte[] stringTable,
            int languageId,
            S7CommPlusTextListScope scope,
            List<S7CommPlusTextList> lists)
        {
            if (listTable == null || entryTable == null || stringTable == null || listTable.Length < 20)
            {
                return S7Consts.errIsoInvalidPDU;
            }

            var pos = 16u;
            if (!TryReadUInt32(listTable, pos, out var listCount))
            {
                return S7Consts.errIsoInvalidPDU;
            }
            pos += 4;

            for (var i = 0; i < listCount; i++)
            {
                if (!TryReadUInt16(listTable, pos, out var listId) ||
                    !TryReadUInt32(listTable, pos + 2, out var entryOffset))
                {
                    return S7Consts.errIsoInvalidPDU;
                }
                pos += 6;

                if (!TryReadEntries(entryTable, stringTable, entryOffset, out var entries))
                {
                    return S7Consts.errIsoInvalidPDU;
                }

                lists.Add(new S7CommPlusTextList(listId, languageId, scope, ClassifyTextList(listId), entries));
            }

            return 0;
        }

        private static S7CommPlusTextListType ClassifyTextList(int listId)
        {
            // The online text-library payload only carries runtime list ids.
            // Keep the public API unified and expose this as a best-effort
            // classification for callers that want to display/filter it.
            return listId >= 512 && listId < 32768
                ? S7CommPlusTextListType.User
                : S7CommPlusTextListType.System;
        }

        private static bool TryReadEntries(byte[] entryTable, byte[] stringTable, uint entryOffset, out List<S7CommPlusTextListEntry> entries)
        {
            entries = null;
            if (!TryReadUInt32(entryTable, entryOffset, out var entryCount))
            {
                return false;
            }

            entries = new List<S7CommPlusTextListEntry>();
            var pos = entryOffset + 4;
            for (var i = 0; i < entryCount; i++)
            {
                if (!TryReadUInt16(entryTable, pos, out var value) ||
                    !TryReadUInt32(entryTable, pos + 2, out var stringOffset) ||
                    !TryReadText(stringTable, stringOffset, out var text))
                {
                    return false;
                }
                pos += 6;
                entries.Add(new S7CommPlusTextListEntry(value, value, text));
            }

            return true;
        }

        private static bool TryReadText(byte[] stringTable, uint offset, out string text)
        {
            text = null;
            if (!TryReadUInt16(stringTable, offset, out var length))
            {
                return false;
            }

            var start = offset + 2;
            if (start > stringTable.Length || start + length > stringTable.Length)
            {
                return false;
            }

            text = Utils.GetUtfString(stringTable, start, length);
            return true;
        }

        private static bool TryReadUInt16(byte[] data, uint offset, out ushort value)
        {
            value = 0;
            if (data == null || offset + 2 > data.Length)
            {
                return false;
            }

            value = Utils.GetUInt16LE(data, offset);
            return true;
        }

        private static bool TryReadUInt32(byte[] data, uint offset, out uint value)
        {
            value = 0;
            if (data == null || offset + 4 > data.Length)
            {
                return false;
            }

            value = Utils.GetUInt32LE(data, offset);
            return true;
        }
    }
}
