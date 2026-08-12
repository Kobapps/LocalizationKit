using System;
using System.Collections.Generic;

namespace LocalizationKit
{
    /// <summary>
    /// The flat, read-optimised form of a catalog. Built once; read for the rest of the session.
    /// </summary>
    /// <remarks>
    /// The whole design is one idea: <b>pay at build time so reads cost nothing</b>.
    /// <list type="bullet">
    /// <item>Keys are flattened to <c>Category/Key</c> and interned into a single ordinal map, so a
    /// lookup is one hash and one array index — no substring work, no concatenation, no boxing.</item>
    /// <item>Text is stored as one <c>string[]</c> per language, all the same length. Switching
    /// language swaps a single reference; it does not copy, rehash or rebuild anything.</item>
    /// <item>Missing text is resolved <i>here</i> — a gap is filled with the fallback language's
    /// text, or with the configured marker. <see cref="GetValue"/> therefore never branches on
    /// null and never returns one.</item>
    /// </list>
    /// The consequence worth stating plainly: a read is an array index into an already-selected
    /// array, and it allocates nothing at all.
    /// </remarks>
    public sealed class LocalizationTable
    {
        private readonly string[] m_Keys;
        private readonly string[][] m_Values;
        private readonly LanguageInfo[] m_Languages;
        private readonly Dictionary<string, int> m_Index;
        private readonly int m_Version;

        private string[] m_Active;
        private int m_ActiveLanguage;

        private static int s_NextVersion = 1;

        /// <summary>Full keys, in build order. Index into this is what a handle carries.</summary>
        public IReadOnlyList<string> Keys => m_Keys;

        /// <summary>Languages this table was built with, in catalog order.</summary>
        public IReadOnlyList<LanguageInfo> Languages => m_Languages;

        /// <summary>Number of distinct keys.</summary>
        public int KeyCount => m_Keys.Length;

        /// <summary>Index of the language currently selected, or -1 when the table has none.</summary>
        public int ActiveLanguageIndex => m_ActiveLanguage;

        /// <summary>
        /// Identity of this build. A handle carries the version it was resolved against so it can
        /// tell that the table underneath it has been replaced — by a remote refresh, say — and
        /// re-resolve itself instead of silently reading the wrong row.
        /// </summary>
        public int Version => m_Version;

        private LocalizationTable(string[] keys, string[][] values, LanguageInfo[] languages, Dictionary<string, int> index)
        {
            m_Keys = keys;
            m_Values = values;
            m_Languages = languages;
            m_Index = index;
            m_Version = s_NextVersion++;
            m_ActiveLanguage = -1;
            m_Active = Array.Empty<string>();
        }

        /// <summary>
        /// Flattens a catalog into a table. Runs once per catalog load; cost is linear in
        /// languages × entries.
        /// </summary>
        /// <param name="catalog">Source catalog. Null yields an empty table rather than throwing.</param>
        /// <param name="missingBehavior">What fills a gap the fallback language cannot fill either.</param>
        /// <param name="fallbackLanguageCode">
        /// Language consulted when an entry has no text in the requested one. Defaults to the
        /// catalog's own default language.
        /// </param>
        public static LocalizationTable Build(
            LocalizationCatalog catalog,
            MissingKeyBehavior missingBehavior = MissingKeyBehavior.ReturnKey,
            string fallbackLanguageCode = null)
        {
            if (catalog == null)
                return Empty();

            catalog.ResizeEntries();

            var languages = new LanguageInfo[catalog.Languages.Count];
            for (var i = 0; i < languages.Length; i++)
                languages[i] = catalog.Languages[i];

            var capacity = catalog.EntryCount;
            var keys = new List<string>(capacity);
            var index = new Dictionary<string, int>(capacity, StringComparer.Ordinal);
            var rows = new List<string[]>(capacity);

            for (var c = 0; c < catalog.Categories.Count; c++)
            {
                var category = catalog.Categories[c];
                var entries = category.Entries;

                for (var e = 0; e < entries.Count; e++)
                {
                    var entry = entries[e];
                    if (entry == null || string.IsNullOrEmpty(entry.Key)) continue;

                    var fullKey = LocalizationKeys.Compose(category.Name, entry.Key);

                    // A duplicate is authoring error, not a runtime condition. Last writer wins so
                    // the table stays well-formed; the editor's validation surfaces the collision.
                    if (index.TryGetValue(fullKey, out var existing))
                    {
                        rows[existing] = entry.Values;
                        continue;
                    }

                    index.Add(fullKey, keys.Count);
                    keys.Add(fullKey);
                    rows.Add(entry.Values);
                }
            }

            var fallback = catalog.IndexOfLanguage(fallbackLanguageCode);
            if (fallback < 0) fallback = catalog.IndexOfLanguage(catalog.DefaultLanguageCode);

            var keyArray = keys.ToArray();
            var values = new string[languages.Length][];

            for (var lang = 0; lang < languages.Length; lang++)
            {
                var column = new string[keyArray.Length];

                for (var k = 0; k < keyArray.Length; k++)
                {
                    var row = rows[k];
                    var text = (uint)lang < (uint)row.Length ? row[lang] : null;

                    if (string.IsNullOrEmpty(text) && fallback >= 0 && fallback != lang)
                        text = (uint)fallback < (uint)row.Length ? row[fallback] : null;

                    column[k] = string.IsNullOrEmpty(text) ? Missing(missingBehavior, keyArray[k]) : text;
                }

                values[lang] = column;
            }

            var table = new LocalizationTable(keyArray, values, languages, index);
            if (languages.Length > 0)
            {
                var start = catalog.IndexOfLanguage(catalog.DefaultLanguageCode);
                table.SelectLanguage(start < 0 ? 0 : start);
            }

            return table;
        }

        /// <summary>A table with no languages and no keys. Every lookup misses.</summary>
        public static LocalizationTable Empty() => new LocalizationTable(
            Array.Empty<string>(),
            Array.Empty<string[]>(),
            Array.Empty<LanguageInfo>(),
            new Dictionary<string, int>(0, StringComparer.Ordinal));

        private static string Missing(MissingKeyBehavior behavior, string key)
        {
            switch (behavior)
            {
                case MissingKeyBehavior.ReturnEmpty: return string.Empty;
                case MissingKeyBehavior.ReturnMarker: return string.Concat("#", key, "#");
                default: return key;
            }
        }

        // ---------------------------------------------------------------- selection

        /// <summary>Index of a language by code, or -1.</summary>
        public int IndexOfLanguage(string code)
        {
            if (string.IsNullOrEmpty(code)) return -1;

            for (var i = 0; i < m_Languages.Length; i++)
            {
                if (string.Equals(m_Languages[i].Code, code, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// Points the table at a language. This is the entire cost of changing language:
        /// one bounds check and one reference assignment.
        /// </summary>
        public bool SelectLanguage(int languageIndex)
        {
            if ((uint)languageIndex >= (uint)m_Values.Length) return false;
            if (languageIndex == m_ActiveLanguage) return false;

            m_ActiveLanguage = languageIndex;
            m_Active = m_Values[languageIndex];

            return true;
        }

        // ---------------------------------------------------------------- reads

        /// <summary>Index of a full key, or -1. One dictionary probe; allocates nothing.</summary>
        public int IndexOf(string fullKey)
        {
            if (string.IsNullOrEmpty(fullKey)) return -1;

            return m_Index.TryGetValue(fullKey, out var index) ? index : -1;
        }

        /// <summary>True when the table carries this key.</summary>
        public bool Contains(string fullKey) => IndexOf(fullKey) >= 0;

        /// <summary>
        /// Text at a key index in the active language. Out-of-range yields
        /// <see cref="string.Empty"/>, so callers never have to null-check.
        /// </summary>
        public string GetValue(int keyIndex) =>
            (uint)keyIndex < (uint)m_Active.Length ? m_Active[keyIndex] : string.Empty;

        /// <summary>Text at a key index in a specific language, regardless of the active one.</summary>
        public string GetValue(int keyIndex, int languageIndex)
        {
            if ((uint)languageIndex >= (uint)m_Values.Length) return string.Empty;

            var column = m_Values[languageIndex];
            return (uint)keyIndex < (uint)column.Length ? column[keyIndex] : string.Empty;
        }

        /// <summary>The full key at an index, or null.</summary>
        public string GetKey(int keyIndex) =>
            (uint)keyIndex < (uint)m_Keys.Length ? m_Keys[keyIndex] : null;
    }
}
