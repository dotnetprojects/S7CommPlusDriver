using System;
using System.Collections.Generic;

namespace S7CommPlusDriver
{
    public enum S7CommPlusPlcStructureNodeKind
    {
        Unit,
        Folder,
        Block,
        Item
    }

    public sealed class S7CommPlusPlcStructureNode
    {
        public S7CommPlusPlcStructureNode(
            S7CommPlusPlcStructureNodeKind kind,
            string name,
            uint? relationId = null,
            int? number = null,
            string blockType = null,
            string blockLanguage = null,
            string subType = null,
            DateTime? lastModified = null,
            IReadOnlyList<S7CommPlusPlcStructureNode> children = null)
        {
            Kind = kind;
            Name = name ?? string.Empty;
            RelationId = relationId;
            Number = number;
            BlockType = blockType;
            BlockLanguage = blockLanguage;
            SubType = subType;
            LastModified = lastModified;
            Children = children ?? Array.Empty<S7CommPlusPlcStructureNode>();
        }

        public S7CommPlusPlcStructureNodeKind Kind { get; }
        public string Name { get; }
        public uint? RelationId { get; }
        public int? Number { get; }
        public string BlockType { get; }
        public string BlockLanguage { get; }
        public string SubType { get; }
        public DateTime? LastModified { get; }
        public IReadOnlyList<S7CommPlusPlcStructureNode> Children { get; }
        public bool IsBlock => Kind == S7CommPlusPlcStructureNodeKind.Block && RelationId.HasValue;
    }
}
