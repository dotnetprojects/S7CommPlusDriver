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
        IReadOnlyDictionary<uint, string> NetworkTitles = null);
}
