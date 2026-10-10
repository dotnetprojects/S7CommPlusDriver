using System;
using System.Collections.Generic;
using System.Linq;

namespace S7CommPlusDriver
{
    internal partial class S7CommPlusProtocolSession : IS7CommPlusBlockBatchSession
    {
        // The PLC reports this code for optional attributes that the block does not have.
        private const ulong MissingBlockAttribute = 0xa0278800004effe0UL;

        int IS7CommPlusBlockBatchSession.GetBlockContents(IReadOnlyList<uint> relationIds, bool sourceOnly, out List<S7CommPlusClientBlockContent> contents)
        {
            contents = new List<S7CommPlusClientBlockContent>();
            if (relationIds.Any(id => !Metadata.TryGetBlockClass(id, out _)))
            {
                var browseResult = Metadata.BrowseBlocks(out _);
                if (browseResult != 0) return browseResult;
            }
            uint[] SourceAttributes(uint id)
            {
                if (!sourceOnly || Metadata.GetBlockHeader(id) == null) return S7CommPlusMetadataService.BlockContentAttributes;
                Metadata.TryGetBlockClass(id, out var classId);
                if (classId == Ids.DB_Class_Rid || classId == Ids.UDT_Class_Rid)
                    return new uint[] { Ids.DataInterface_LineComments, Ids.DataInterface_InterfaceDescription };
                return new uint[] { Ids.DataInterface_LineComments, Ids.DataInterface_InterfaceDescription, Ids.Block_BodyDescription,
                    Ids.FunctionalObject_intRefData, Ids.FunctionalObject_extRefData, Ids.FunctionalObject_NetworkComments,
                    Ids.FunctionalObject_NetworkTitles, Ids.FunctionalObject_DebugInfo };
            }
            var requested = relationIds.Select(SourceAttributes).ToArray();
            var addresses = relationIds.SelectMany((id, index) => requested[index].Select(attribute => new ItemAddress(id, attribute))).ToList();
            var result = ReadValues(addresses, out var values, out var errors);
            if (result != 0) return result;
            var offset = 0;
            for (var index = 0; index < relationIds.Count; index++)
            {
                var id = relationIds[index];
                var attributes = requested[index];
                var blockOffset = offset;
                offset += attributes.Length;
                S7CommPlusClientBlockContent content;
                var fallback = !Metadata.TryGetBlockClass(id, out var classId)
                    || errors.Skip(blockOffset).Take(attributes.Length).Any(error => error != 0 && error != MissingBlockAttribute);
                if (!fallback)
                {
                    var obj = new PObject(id, classId, 0);
                    if (sourceOnly && Metadata.GetBlockHeader(id) is PObject header)
                        foreach (var attribute in header.Attributes) obj.Attributes[attribute.Key] = attribute.Value;
                    for (var attribute = 0; attribute < attributes.Length; attribute++)
                        if (errors[blockOffset + attribute] == 0 && values[blockOffset + attribute] is PValue value)
                            obj.Attributes[attributes[attribute]] = value;
                    fallback = !obj.Attributes.ContainsKey(Ids.ObjectVariableTypeName)
                        || !obj.Attributes.ContainsKey(Ids.DataInterface_InterfaceDescription);
                    if (!fallback)
                    {
                        Metadata.ParseBlockContent(id, new[] { obj }, out content);
                        contents.Add(content);
                        continue;
                    }
                }
                result = Metadata.GetBlockContent(id, out content);
                if (result != 0) return result;
                contents.Add(content);
            }
            // Read actual DB type IDs together, then reuse the normal type-information cache.
            var dbs = contents.Select((content, index) => (content, index)).Where(item => item.content.Type == S7CommPlusBlockType.DB).ToArray();
            var typeAddresses = dbs.Select(item => { var address = new ItemAddress(item.content.RelationId, Ids.DB_ValueActual); address.AddLocalId(1); return address; }).ToList();
            result = ReadValues(typeAddresses, out values, out errors);
            if (result != 0) return result;
            for (var index = 0; index < dbs.Length; index++)
                if (errors[index] == 0 && values[index] is ValueRID typeId)
                    contents[dbs[index].index] = dbs[index].content with
                    {
                        TypeInformation = SerializeBlockTypeInformation(getTypeInfoByRelId(typeId.GetValue()))
                    };
            return 0;
        }
    }
}
