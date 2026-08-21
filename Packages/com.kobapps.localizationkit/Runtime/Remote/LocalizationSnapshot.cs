using System;
using System.Collections.Generic;
using UnityEngine;

namespace LocalizationKit
{
    /// <summary>
    /// A whole catalog's worth of text as plain data: languages, keys and values, with no asset
    /// behind it and no index built for reading.
    /// </summary>
    /// <remarks>
    /// This is what crosses the wire. A provider fetches one, a merge consumes one, and neither
    /// ever touches the asset database — which matters more than it looks. A
    /// <see cref="LocalizationCatalog"/> is a Unity object, and Unity objects may only be created
    /// and read on the main thread, at times Unity chooses; a download completing on an arbitrary
    /// frame in a player, where there is no asset database at all, is exactly the case those rules
    /// exist for. A snapshot is a <c>List</c> and some strings, so none of that applies to it.
    /// <para>
    /// Its shape — one row per key, one column per language — is the shape translators already work
    /// in and the only shape every remote worth talking to agrees on. Values are positional against
    /// <see cref="Languages"/>, the same invariant a catalog entry keeps and for the same reason: a
    /// language code stored once per snapshot rather than once per row per language.
    /// </para>
    /// </remarks>
    public sealed class LocalizationSnapshot
    {
        /// <summary>One key and its text in every language the snapshot carries.</summary>
        public sealed class Row
        {
            /// <summary>The full <c>Category/Key</c>.</summary>
            public string Key;

            /// <summary>Translator-facing note, when the remote carries one. Never shown to players.</summary>
            public string Description;

            /// <summary>Text per language, positional against <see cref="Languages"/>.</summary>
            public string[] Values = Array.Empty<string>();

            /// <summary>Text for a language position, or null when out of range or unset.</summary>
            public string GetValue(int languageIndex) =>
                (uint)languageIndex < (uint)Values.Length ? Values[languageIndex] : null;

            /// <summary>Assigns text for a language position. Out-of-range indices are ignored.</summary>
            public void SetValue(int languageIndex, string value)
            {
                if ((uint)languageIndex < (uint)Values.Length)
                    Values[languageIndex] = value;
            }

            /// <summary>True when this row has no text for the given language.</summary>
            public bool IsMissing(int languageIndex) => string.IsNullOrEmpty(GetValue(languageIndex));

            internal void Resize(int count)
            {
                if (Values.Length == count) return;

                var next = new string[count];
                Array.Copy(Values, next, Mathf.Min(count, Values.Length));
                Values = next;
            }
        }

        private readonly List<LanguageInfo> m_Languages = new List<LanguageInfo>();
        private readonly List<Row> m_Rows = new List<Row>();
        private readonly Dictionary<string, int> m_Index = new Dictionary<string, int>(StringComparer.Ordinal);

        private string m_DefaultLanguageCode;

        /// <summary>Languages in column order. Positional against every row's values.</summary>
        public IReadOnlyList<LanguageInfo> Languages => m_Languages;

        /// <summary>Rows in the order the source produced them.</summary>
        public IReadOnlyList<Row> Rows => m_Rows;

        /// <summary>Number of language columns.</summary>
        public int LanguageCount => m_Languages.Count;

        /// <summary>Number of keys.</summary>
        public int RowCount => m_Rows.Count;

        /// <summary>True when there is nothing here to merge or build from.</summary>
        public bool IsEmpty => m_Rows.Count == 0 || m_Languages.Count == 0;

        /// <summary>
        /// Language used to fill gaps and as the startup language when nothing else applies.
        /// Falls back to the first column.
        /// </summary>
        public string DefaultLanguageCode
        {
            get => !string.IsNullOrEmpty(m_DefaultLanguageCode) && IndexOfLanguage(m_DefaultLanguageCode) >= 0
                ? m_DefaultLanguageCode
                : m_Languages.Count > 0 ? m_Languages[0].Code : null;
            set => m_DefaultLanguageCode = value;
        }

        /// <summary>Where this came from, for logs and merge reports. Not an identity.</summary>
        public string SourceName { get; set; }

        /// <summary>Anything questionable the source noticed while producing this.</summary>
        public List<string> Warnings { get; } = new List<string>();

        // ---------------------------------------------------------------- languages

        /// <summary>Index of a language by code, or -1. Case-insensitive.</summary>
        public int IndexOfLanguage(string code)
        {
            if (string.IsNullOrEmpty(code)) return -1;

            for (var i = 0; i < m_Languages.Count; i++)
            {
                if (string.Equals(m_Languages[i].Code, code, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// Adds a language column and widens every row for it. Returns the index of the language,
        /// existing or new; a blank code is rejected with -1.
        /// </summary>
        public int AddLanguage(LanguageInfo language)
        {
            if (string.IsNullOrWhiteSpace(language.Code)) return -1;

            var existing = IndexOfLanguage(language.Code);
            if (existing >= 0) return existing;

            m_Languages.Add(language);

            for (var i = 0; i < m_Rows.Count; i++)
                m_Rows[i].Resize(m_Languages.Count);

            return m_Languages.Count - 1;
        }

        // ---------------------------------------------------------------- rows

        /// <summary>Finds a row by full key, or null. Keys compare ordinally, as everywhere else.</summary>
        public Row Find(string fullKey)
        {
            if (string.IsNullOrEmpty(fullKey)) return null;

            return m_Index.TryGetValue(fullKey, out var index) ? m_Rows[index] : null;
        }

        /// <summary>Returns the row for a key, adding it — sized for the current columns — when absent.</summary>
        public Row GetOrAddRow(string fullKey)
        {
            var existing = Find(fullKey);
            if (existing != null) return existing;

            var row = new Row { Key = fullKey, Values = new string[m_Languages.Count] };

            m_Index[fullKey] = m_Rows.Count;
            m_Rows.Add(row);

            return row;
        }

        /// <summary>Text for a key in a language, or null when either is unknown.</summary>
        public string GetValue(string fullKey, string languageCode) =>
            Find(fullKey)?.GetValue(IndexOfLanguage(languageCode));

        /// <summary>
        /// Assigns text for a key in a language, creating the row if needed. Returns false when the
        /// snapshot carries no such language — columns are added deliberately, not by writing.
        /// </summary>
        public bool SetValue(string fullKey, string languageCode, string value)
        {
            var language = IndexOfLanguage(languageCode);
            if (language < 0 || string.IsNullOrEmpty(fullKey)) return false;

            GetOrAddRow(fullKey).SetValue(language, value);
            return true;
        }

        // ---------------------------------------------------------------- conversion

        /// <summary>Copies a catalog into transport form. Null yields an empty snapshot.</summary>
        public static LocalizationSnapshot FromCatalog(LocalizationCatalog catalog)
        {
            var snapshot = new LocalizationSnapshot();
            if (catalog == null) return snapshot;

            catalog.ResizeEntries();

            for (var i = 0; i < catalog.Languages.Count; i++)
                snapshot.AddLanguage(catalog.Languages[i]);

            snapshot.DefaultLanguageCode = catalog.DefaultLanguageCode;
            snapshot.SourceName = catalog.name;

            for (var c = 0; c < catalog.Categories.Count; c++)
            {
                var category = catalog.Categories[c];

                for (var e = 0; e < category.Entries.Count; e++)
                {
                    var entry = category.Entries[e];
                    if (entry == null || string.IsNullOrEmpty(entry.Key)) continue;

                    var row = snapshot.GetOrAddRow(LocalizationKeys.Compose(category.Name, entry.Key));
                    row.Description = entry.Description;

                    for (var lang = 0; lang < snapshot.m_Languages.Count; lang++)
                        row.SetValue(lang, entry.GetValue(lang));
                }
            }

            return snapshot;
        }

        /// <summary>
        /// Reads CSV in the kit's shape. Returns null when the document could not be read as a
        /// table at all, logging why; see <see cref="TryFromCsv"/> to handle that yourself.
        /// </summary>
        public static LocalizationSnapshot FromCsv(string csv, char delimiter = ',')
        {
            if (TryFromCsv(csv, out var snapshot, out var error, delimiter)) return snapshot;

            Debug.LogWarning($"[LocalizationKit] Could not read the document: {error}");
            return null;
        }

        /// <summary>
        /// Reads CSV in the kit's shape — first column keys, one column per language code.
        /// Never throws: a malformed document comes back as false with a reason.
        /// </summary>
        public static bool TryFromCsv(string csv, out LocalizationSnapshot snapshot, out string error, char delimiter = ',')
        {
            snapshot = null;

            var parsed = LocalizationCsv.Parse(csv, delimiter);
            if (parsed.Failed)
            {
                error = parsed.Error;
                return false;
            }

            snapshot = new LocalizationSnapshot();

            for (var i = 0; i < parsed.LanguageCodes.Length; i++)
                snapshot.AddLanguage(new LanguageInfo(parsed.LanguageCodes[i], parsed.LanguageCodes[i]));

            foreach (var parsedRow in parsed.Rows)
            {
                if (string.IsNullOrEmpty(parsedRow.Key)) continue;

                var row = snapshot.GetOrAddRow(parsedRow.Key);

                for (var c = 0; c < parsedRow.Values.Length && c < parsed.LanguageCodes.Length; c++)
                {
                    // A column whose header was blank never got a slot, so map by code rather than
                    // assuming the column order survived into the snapshot.
                    var language = snapshot.IndexOfLanguage(parsed.LanguageCodes[c]);
                    if (language < 0) continue;

                    row.SetValue(language, parsedRow.Values[c]);
                }
            }

            snapshot.Warnings.AddRange(parsed.Warnings);

            error = null;
            return true;
        }

        /// <summary>Writes this snapshot in the shape <see cref="TryFromCsv"/> reads.</summary>
        public string ToCsv(char delimiter = ',') => LocalizationCsv.Write(this, delimiter);

        /// <summary>
        /// Builds a transient catalog from this snapshot — not an asset, and not in the project.
        /// </summary>
        /// <remarks>
        /// Useful when something wants catalog-shaped data to work against: merging, previewing a
        /// diff, building a table. The caller owns the result; pass it to
        /// <see cref="DestroyTransient"/> when finished, or it stays in memory for the session.
        /// </remarks>
        public LocalizationCatalog ToCatalog(string name = null)
        {
            var catalog = ScriptableObject.CreateInstance<LocalizationCatalog>();
            catalog.name = string.IsNullOrEmpty(name) ? "Transient Localization Catalog" : name;

            for (var i = 0; i < m_Languages.Count; i++)
                catalog.AddLanguage(m_Languages[i]);

            catalog.DefaultLanguageCode = DefaultLanguageCode;

            for (var r = 0; r < m_Rows.Count; r++)
            {
                var row = m_Rows[r];
                if (string.IsNullOrEmpty(row.Key)) continue;

                var category = LocalizationKeys.TrySplit(row.Key, out var categoryName, out var key)
                    ? categoryName
                    : LocalizationKeys.DefaultCategory;

                var entry = catalog.AddEntry(category, key);
                if (!string.IsNullOrEmpty(row.Description)) entry.Description = row.Description;

                for (var lang = 0; lang < m_Languages.Count; lang++)
                    entry.SetValue(lang, row.GetValue(lang));
            }

            return catalog;
        }

        /// <summary>
        /// Builds the read-optimised table the runtime uses. This is the last step of a remote
        /// load: hand the result to <see cref="Localization.SetTable"/> and everything bound
        /// refreshes itself.
        /// </summary>
        public LocalizationTable ToTable(
            MissingKeyBehavior missingBehavior = MissingKeyBehavior.ReturnKey,
            string fallbackLanguageCode = null)
        {
            var catalog = ToCatalog();

            try
            {
                return LocalizationTable.Build(catalog, missingBehavior, fallbackLanguageCode);
            }
            finally
            {
                // The table copies what it needs, so the catalog has no reason to outlive this
                // call — and a refresh on a timer would otherwise leak one object per fetch.
                DestroyTransient(catalog);
            }
        }

        /// <summary>
        /// Disposes of a catalog produced by <see cref="ToCatalog"/>. Safe on null, and declines to
        /// touch anything that turns out to be a real asset.
        /// </summary>
        public static void DestroyTransient(LocalizationCatalog catalog)
        {
            if (catalog == null) return;

#if UNITY_EDITOR
            if (UnityEditor.AssetDatabase.Contains(catalog)) return;
#endif

            if (Application.isPlaying) UnityEngine.Object.Destroy(catalog);
            else UnityEngine.Object.DestroyImmediate(catalog);
        }
    }
}
