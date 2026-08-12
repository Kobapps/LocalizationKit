using System.Collections.Generic;

namespace LocalizationKit.Editor
{
    /// <summary>
    /// Translation coverage, counted the same way everywhere it is shown.
    /// </summary>
    /// <remarks>
    /// Coverage is deliberately measured against the whole grid — entries × languages — rather than
    /// per language, because "94% translated" answers the question a producer actually asks. The
    /// per-language figures are there for the person who has to fill the gaps.
    /// </remarks>
    internal readonly struct LocalizationStats
    {
        internal readonly int LanguageCount;
        internal readonly int EntryCount;
        internal readonly int CategoryCount;
        internal readonly int FilledCells;
        internal readonly int TotalCells;

        /// <summary>Fraction of the grid that has text. 1 when there is nothing to translate.</summary>
        internal float Coverage => TotalCells == 0 ? 1f : (float)FilledCells / TotalCells;

        private LocalizationStats(int languages, int entries, int categories, int filled, int total)
        {
            LanguageCount = languages;
            EntryCount = entries;
            CategoryCount = categories;
            FilledCells = filled;
            TotalCells = total;
        }

        internal static LocalizationStats For(LocalizationCatalog catalog)
        {
            if (catalog == null) return new LocalizationStats(0, 0, 0, 0, 0);

            var languages = catalog.Languages.Count;
            var entries = 0;
            var filled = 0;

            for (var c = 0; c < catalog.Categories.Count; c++)
            {
                var list = catalog.Categories[c].Entries;

                for (var e = 0; e < list.Count; e++)
                {
                    var entry = list[e];
                    if (entry == null || string.IsNullOrEmpty(entry.Key)) continue;

                    entries++;
                    for (var lang = 0; lang < languages; lang++)
                        if (!entry.IsMissing(lang)) filled++;
                }
            }

            return new LocalizationStats(languages, entries, catalog.Categories.Count, filled, entries * languages);
        }

        /// <summary>Number of entries with no text, per language index.</summary>
        internal static int[] MissingPerLanguage(LocalizationCatalog catalog)
        {
            if (catalog == null) return System.Array.Empty<int>();

            var missing = new int[catalog.Languages.Count];

            for (var c = 0; c < catalog.Categories.Count; c++)
            {
                var list = catalog.Categories[c].Entries;

                for (var e = 0; e < list.Count; e++)
                {
                    var entry = list[e];
                    if (entry == null || string.IsNullOrEmpty(entry.Key)) continue;

                    for (var lang = 0; lang < missing.Length; lang++)
                        if (entry.IsMissing(lang)) missing[lang]++;
                }
            }

            return missing;
        }

        /// <summary>
        /// Full keys that appear in more than one place. A duplicate resolves to whichever entry
        /// the table built last, which is arbitrary from the author's point of view — so it is
        /// worth surfacing rather than letting it decide itself.
        /// </summary>
        internal static List<string> FindDuplicateKeys(LocalizationCatalog catalog)
        {
            var duplicates = new List<string>();
            if (catalog == null) return duplicates;

            var seen = new HashSet<string>(System.StringComparer.Ordinal);

            for (var c = 0; c < catalog.Categories.Count; c++)
            {
                var category = catalog.Categories[c];

                for (var e = 0; e < category.Entries.Count; e++)
                {
                    var entry = category.Entries[e];
                    if (entry == null || string.IsNullOrEmpty(entry.Key)) continue;

                    var full = LocalizationKeys.Compose(category.Name, entry.Key);
                    if (!seen.Add(full)) duplicates.Add(full);
                }
            }

            return duplicates;
        }
    }
}
