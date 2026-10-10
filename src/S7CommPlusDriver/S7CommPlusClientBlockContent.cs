using System.Collections.Generic;

namespace S7CommPlusDriver
{
    public sealed record S7CommPlusClientBlockContent(
        uint RelationId,
        string Name,
        S7CommPlusProgrammingLanguage Language,
        uint Number,
        S7CommPlusBlockType Type,
        string XmlLineComment,
        IReadOnlyDictionary<uint, string> XmlComments,
        string InterfaceDescription,
        IReadOnlyList<string> BlockBody,
        string FunctionalObjectCode,
        string FunctionalObjectDebugInfo,
        IReadOnlyList<string> InternalReferences,
        IReadOnlyList<string> ExternalReferences,
        byte[] FunctionalObjectCodeBytes = null,
        byte[] CodeModifiedTimestampBytes = null,
        IReadOnlyDictionary<uint, byte[]> BinaryArtifacts = null,
        IReadOnlyDictionary<uint, string> OnlineMetadata = null,
        IReadOnlyDictionary<uint, string> NetworkComments = null,
        IReadOnlyDictionary<uint, string> NetworkTitles = null)
    {
        /// <summary>Native DB type-information metadata, including instance-specific layout and retention.</summary>
        public string TypeInformation { get; init; }
        /// <summary>Safety compliance reported by the PLC engineering structure, when available.</summary>
        public bool? IsFailsafeCompliant { get; init; }
        /// <summary>TIA compatibility version from the CPU's engineering block header, when available.</summary>
        public string EngineeringVersion { get; init; }
    }
}
