#if HARPOS7_LEGACY_AUTH
using System;
using System.Collections.Generic;
using System.Linq;
using HarpoS7.PublicKeys.Impl;

namespace S7CommPlusDriver.Internal
{
    internal static class LegacyPublicKeyCatalog
    {
        private const string ResourcePrefix = "HarpoS7.PublicKeys.Keys._";
        private const string ResourceSuffix = ".bin";

        public static IReadOnlyList<string> GetFingerprints(string familyFingerprint)
        {
            if (string.IsNullOrWhiteSpace(familyFingerprint) || familyFingerprint.Length != 2)
            {
                return Array.Empty<string>();
            }

            var familyResourcePrefix = $"{ResourcePrefix}{familyFingerprint}.";
            return typeof(DefaultPublicKeyStore).Assembly
                .GetManifestResourceNames()
                .Where(name =>
                    name.StartsWith(familyResourcePrefix, StringComparison.Ordinal) &&
                    name.EndsWith(ResourceSuffix, StringComparison.Ordinal))
                .Select(name =>
                    $"{familyFingerprint}:{name.Substring(familyResourcePrefix.Length, name.Length - familyResourcePrefix.Length - ResourceSuffix.Length)}")
                .OrderBy(fingerprint => fingerprint, StringComparer.Ordinal)
                .ToArray();
        }
    }
}
#endif
