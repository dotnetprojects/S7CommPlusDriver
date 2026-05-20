using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace S7CommPlusDriver.Internal
{
    internal static class PlcStructureXmlParser
    {
        private static readonly Regex EscapedCharRegex = new Regex("_x([0-9A-F]{4})_", RegexOptions.Compiled);

        public static S7CommPlusPlcStructureSnapshot CreateSnapshot(string xml)
        {
            xml ??= string.Empty;
            var structure = Parse(xml);
            var marker = CreateProgramChangeMarker(xml, structure);
            return new S7CommPlusPlcStructureSnapshot(xml, marker, structure);
        }

        public static IReadOnlyList<S7CommPlusPlcStructureNode> Parse(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
            {
                return Array.Empty<S7CommPlusPlcStructureNode>();
            }

            var document = XDocument.Parse(xml);
            var entities = document
                .Descendants()
                .Where(element => element.Name.LocalName == "Entity")
                .Select(element => new { Element = element, Rid = ReadUIntAttribute(element, "Rid") })
                .Where(item => item.Rid.HasValue)
                .GroupBy(item => item.Rid.Value)
                .ToDictionary(group => group.Key, group => group.First().Element);

            var tagTables = document
                .Descendants()
                .Where(element => element.Name.LocalName == "TagTable")
                .Select(element => new TagTableInfo(ReadAttribute(element, "Name"), ReadDateAttribute(element, "LoadRelevantModifiedTime")))
                .Where(table => !string.IsNullOrWhiteSpace(table.Name))
                .GroupBy(table => table.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            var target = document
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "Entity"
                    && string.Equals(ReadAttribute(element, "Id"), "Target", StringComparison.OrdinalIgnoreCase));
            var unitElement = target?
                .Elements()
                .FirstOrDefault(element => element.Name.LocalName == "GroupStructureV2")?
                .Elements()
                .FirstOrDefault(element => element.Name.LocalName == "Unit");
            if (unitElement == null)
            {
                return Array.Empty<S7CommPlusPlcStructureNode>();
            }

            var unitChildren = new List<S7CommPlusPlcStructureNode>
            {
                ParseSection("Program blocks", unitElement.Element(unitElement.Name.Namespace + "ProgramBlocks"), entities, tagTables),
                ParseSection("PLC tags", unitElement.Element(unitElement.Name.Namespace + "PlcTagTables"), entities, tagTables),
                ParseSection("PLC data types", unitElement.Element(unitElement.Name.Namespace + "PlcDataTypes"), entities, tagTables)
            };

            return new[]
            {
                new S7CommPlusPlcStructureNode(
                    S7CommPlusPlcStructureNodeKind.Unit,
                    string.IsNullOrWhiteSpace(ReadAttribute(unitElement, "Name")) ? "Default-Unit" : ReadAttribute(unitElement, "Name"),
                    children: unitChildren)
            };
        }

        private static S7CommPlusPlcStructureNode ParseSection(
            string name,
            XElement section,
            IReadOnlyDictionary<uint, XElement> entities,
            IReadOnlyDictionary<string, TagTableInfo> tagTables)
        {
            var children = section == null
                ? Array.Empty<S7CommPlusPlcStructureNode>()
                : section.Elements().Where(element => element.Name.LocalName == "Group").Select(group => ParseGroup(group, entities, tagTables)).ToArray();
            return new S7CommPlusPlcStructureNode(S7CommPlusPlcStructureNodeKind.Folder, name, children: children);
        }

        private static S7CommPlusPlcStructureNode ParseGroup(
            XElement group,
            IReadOnlyDictionary<uint, XElement> entities,
            IReadOnlyDictionary<string, TagTableInfo> tagTables)
        {
            var children = new List<S7CommPlusPlcStructureNode>();
            foreach (var child in group.Elements())
            {
                switch (child.Name.LocalName)
                {
                    case "Group":
                        children.Add(ParseGroup(child, entities, tagTables));
                        break;
                    case "OnlineId":
                        children.Add(ParseOnlineId(child.Value, entities, tagTables));
                        break;
                }
            }

            return new S7CommPlusPlcStructureNode(
                S7CommPlusPlcStructureNodeKind.Folder,
                string.IsNullOrWhiteSpace(ReadAttribute(group, "Name")) ? "Folder" : ReadAttribute(group, "Name"),
                children: children);
        }

        private static S7CommPlusPlcStructureNode ParseOnlineId(
            string onlineId,
            IReadOnlyDictionary<uint, XElement> entities,
            IReadOnlyDictionary<string, TagTableInfo> tagTables)
        {
            var decoded = ReplaceEscapedChars(onlineId ?? string.Empty);
            if (uint.TryParse(decoded, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rid)
                && entities.TryGetValue(rid, out var entity))
            {
                var header = entity.Elements().FirstOrDefault(element => element.Name.LocalName == "Header");
                if (header != null)
                {
                    return new S7CommPlusPlcStructureNode(
                        S7CommPlusPlcStructureNodeKind.Block,
                        ReadAttribute(header, "Name") ?? $"Block {ReadIntAttribute(header, "Number") ?? 0}",
                        relationId: rid,
                        number: ReadIntAttribute(header, "Number"),
                        blockType: NormalizeBlockType(ReadAttribute(header, "Type")),
                        blockLanguage: NormalizeBlockLanguage(ReadAttribute(header, "ProgrammingLanguage")),
                        subType: ReadAttribute(header, "SubType"),
                        lastModified: ReadDateAttribute(header, "LastModified"));
                }
            }

            if (tagTables.TryGetValue(decoded, out var tagTable))
            {
                return new S7CommPlusPlcStructureNode(
                    S7CommPlusPlcStructureNodeKind.Item,
                    tagTable.Name,
                    blockType: "TagTable",
                    lastModified: tagTable.LastModified);
            }

            return new S7CommPlusPlcStructureNode(S7CommPlusPlcStructureNodeKind.Item, decoded);
        }

        private static S7CommPlusProgramChangeMarker CreateProgramChangeMarker(string xml, IReadOnlyList<S7CommPlusPlcStructureNode> structure)
        {
            var nodes = EnumerateNodes(structure ?? Array.Empty<S7CommPlusPlcStructureNode>()).ToArray();
            var lastModified = nodes
                .Where(node => node.LastModified.HasValue)
                .Select(node => node.LastModified.Value)
                .DefaultIfEmpty()
                .Max();
            var hasLastModified = nodes.Any(node => node.LastModified.HasValue);
            return new S7CommPlusProgramChangeMarker(
                ComputeSha256Hex(xml ?? string.Empty),
                hasLastModified ? lastModified : (DateTime?)null,
                nodes.Count(node => node.Kind == S7CommPlusPlcStructureNodeKind.Block),
                nodes.Count(node => string.Equals(node.BlockType, "TagTable", StringComparison.OrdinalIgnoreCase)));
        }

        private static IEnumerable<S7CommPlusPlcStructureNode> EnumerateNodes(IEnumerable<S7CommPlusPlcStructureNode> nodes)
        {
            foreach (var node in nodes)
            {
                yield return node;
                foreach (var child in EnumerateNodes(node.Children))
                {
                    yield return child;
                }
            }
        }

        private static string ComputeSha256Hex(string value)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
            return BitConverter.ToString(hash).Replace("-", string.Empty);
        }

        private static string ReplaceEscapedChars(string text)
        {
            return EscapedCharRegex.Replace(text, match => ((char)int.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString());
        }

        private static string ReadAttribute(XElement element, string name)
        {
            return element.Attribute(name)?.Value;
        }

        private static uint? ReadUIntAttribute(XElement element, string name)
        {
            return uint.TryParse(ReadAttribute(element, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : (uint?)null;
        }

        private static int? ReadIntAttribute(XElement element, string name)
        {
            return int.TryParse(ReadAttribute(element, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : (int?)null;
        }

        private static DateTime? ReadDateAttribute(XElement element, string name)
        {
            return DateTime.TryParse(ReadAttribute(element, name), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value) ? value : (DateTime?)null;
        }

        private static string NormalizeBlockType(string value)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
            {
                return value;
            }

            return numeric switch
            {
                1 => "UDT",
                2 => "Enum",
                8 => "OB",
                10 => "DB",
                11 => "SDB",
                12 => "FC",
                13 => "SFC",
                14 => "FB",
                15 => "SFB",
                16 => "CB",
                17 => "MTH",
                257 => "FBT",
                258 => "SDT",
                _ => value
            };
        }

        private static string NormalizeBlockLanguage(string value)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
            {
                return value;
            }

            return Enum.IsDefined(typeof(S7CommPlusProgrammingLanguage), numeric)
                ? ((S7CommPlusProgrammingLanguage)numeric).ToString()
                : value;
        }

        private sealed class TagTableInfo
        {
            public TagTableInfo(string name, DateTime? lastModified)
            {
                Name = name;
                LastModified = lastModified;
            }

            public string Name { get; }
            public DateTime? LastModified { get; }
        }
    }
}
