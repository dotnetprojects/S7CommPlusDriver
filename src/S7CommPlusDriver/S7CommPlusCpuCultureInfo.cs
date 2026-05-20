using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace S7CommPlusDriver
{
    public sealed class S7CommPlusCpuCultureInfo
    {
        internal S7CommPlusCpuCultureInfo(IEnumerable<int> languageIds)
        {
            LanguageIds = (languageIds ?? Enumerable.Empty<int>()).Distinct().ToList();
            Cultures = LanguageIds
                .Select(TryGetCultureInfo)
                .Where(culture => culture != null)
                .ToList();
            UnresolvedLanguageIds = LanguageIds
                .Where(languageId => TryGetCultureInfo(languageId) == null)
                .ToList();
        }

        public IReadOnlyList<int> LanguageIds { get; }
        public IReadOnlyList<CultureInfo> Cultures { get; }
        public IReadOnlyList<int> UnresolvedLanguageIds { get; }
        public int? PrimaryLanguageId => LanguageIds.Count > 0 ? LanguageIds[0] : (int?)null;
        public CultureInfo PrimaryCulture => PrimaryLanguageId.HasValue ? TryGetCultureInfo(PrimaryLanguageId.Value) : null;

        private static CultureInfo TryGetCultureInfo(int languageId)
        {
            try
            {
                return CultureInfo.GetCultureInfo(languageId);
            }
            catch (CultureNotFoundException)
            {
                return null;
            }
        }
    }
}
