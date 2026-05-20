using System;
using System.Collections.Generic;

namespace S7CommPlusDriver
{
    public sealed class S7CommPlusProgramChangeMarker
    {
        public S7CommPlusProgramChangeMarker(
            string structureHash,
            DateTime? lastModified,
            int blockCount,
            int tagTableCount,
            string hashAlgorithm = "SHA-256")
        {
            HashAlgorithm = string.IsNullOrWhiteSpace(hashAlgorithm) ? "SHA-256" : hashAlgorithm;
            StructureHash = structureHash ?? string.Empty;
            LastModified = lastModified;
            BlockCount = blockCount;
            TagTableCount = tagTableCount;
        }

        public string HashAlgorithm { get; }
        public string StructureHash { get; }
        public DateTime? LastModified { get; }
        public int BlockCount { get; }
        public int TagTableCount { get; }
    }

    public sealed class S7CommPlusPlcStructureSnapshot
    {
        public S7CommPlusPlcStructureSnapshot(
            string xml,
            S7CommPlusProgramChangeMarker programChangeMarker,
            IReadOnlyList<S7CommPlusPlcStructureNode> structure)
        {
            Xml = xml ?? string.Empty;
            ProgramChangeMarker = programChangeMarker ?? throw new ArgumentNullException(nameof(programChangeMarker));
            Structure = structure ?? Array.Empty<S7CommPlusPlcStructureNode>();
        }

        public string Xml { get; }
        public S7CommPlusProgramChangeMarker ProgramChangeMarker { get; }
        public IReadOnlyList<S7CommPlusPlcStructureNode> Structure { get; }
    }
}
