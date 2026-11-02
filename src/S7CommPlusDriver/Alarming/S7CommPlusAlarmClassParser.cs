using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace S7CommPlusDriver.Alarming
{
    internal static class S7CommPlusAlarmClassParser
    {
        internal static int Parse(IEnumerable<PObject> objects, out List<S7CommPlusAlarmClass> classes)
        {
            classes = new List<S7CommPlusAlarmClass>();
            var definitions = new SortedDictionary<ushort, S7CommPlusAlarmClass>();
            var pending = new Stack<PObject>(objects ?? Array.Empty<PObject>());
            try
            {
                while (pending.Count > 0)
                {
                    var obj = pending.Pop();
                    foreach (var child in obj.GetObjects()) pending.Push(child);
                    if (!obj.Attributes.TryGetValue(Ids.Block_AlarmDescription, out var value)) continue;
                    if (value is not ValueBlobSparseArray blobs || blobs.Value == null)
                        throw new FormatException("Expected alarm description blobs.");
                    // OMS array index zero is encoded as sparse wire key one. Remaining entries describe alarms.
                    if (!blobs.Value.TryGetValue(1, out var entry)) continue;
                    if (entry.value == null) throw new FormatException("Missing alarm class XML.");
                    using var stream = new MemoryStream(entry.value, false);
                    using var reader = XmlReader.Create(stream, new XmlReaderSettings
                    {
                        DtdProcessing = DtdProcessing.Prohibit,
                        XmlResolver = null
                    });
                    var root = XDocument.Load(reader).Root;
                    if (root?.Name != "AlarmClassList") throw new FormatException("Expected AlarmClassList.");
                    foreach (var element in root.Elements("AlarmClass"))
                    {
                        var idValue = (int?)element.Attribute("Id");
                        var name = (string)element.Attribute("Name");
                        var priority = (int?)element.Attribute("Prio");
                        if (!idValue.HasValue || idValue < 0 || idValue > ushort.MaxValue ||
                            string.IsNullOrWhiteSpace(name) || priority < 0)
                            throw new FormatException("Invalid alarm class definition.");
                        var id = (ushort)idValue.Value;
                        var ack = (bool?)element.Attribute("Ack");
                        var displayNames = ReadTexts(element, "DisplayName");
                        var shortNames = ReadTexts(element, "ShortName");
                        if (definitions.TryGetValue(id, out var previous))
                        {
                            if (previous.Name != name ||
                                (previous.Priority.HasValue && priority.HasValue && previous.Priority != priority) ||
                                (previous.AcknowledgementRequired.HasValue && ack.HasValue && previous.AcknowledgementRequired != ack))
                                throw new FormatException("Conflicting alarm class definitions.");
                            priority ??= previous.Priority;
                            ack ??= previous.AcknowledgementRequired;
                            MergeTexts(displayNames, previous.DisplayNames);
                            MergeTexts(shortNames, previous.ShortNames);
                        }
                        definitions[id] = new S7CommPlusAlarmClass(id, name, priority, ack, displayNames, shortNames);
                    }
                }
                classes = definitions.Values.ToList();
                return 0;
            }
            catch (Exception ex) when (ex is XmlException || ex is FormatException || ex is OverflowException)
            {
                return S7Consts.errIsoInvalidPDU;
            }
        }

        private static Dictionary<int, string> ReadTexts(XElement element, string name)
        {
            var texts = new Dictionary<int, string>();
            foreach (var text in element.Elements(name))
            {
                var lcid = (int?)text.Attribute("LCID");
                if (!lcid.HasValue || lcid < 0 || lcid > ushort.MaxValue)
                    throw new FormatException("Invalid alarm class language.");
                MergeTexts(texts, new[] { new KeyValuePair<int, string>(lcid.Value, text.Value) });
            }
            return texts;
        }

        private static void MergeTexts(Dictionary<int, string> target, IEnumerable<KeyValuePair<int, string>> source)
        {
            foreach (var text in source)
            {
                if (target.TryGetValue(text.Key, out var previous) && previous != text.Value)
                    throw new FormatException("Conflicting alarm class translations.");
                target[text.Key] = text.Value;
            }
        }
    }
}
