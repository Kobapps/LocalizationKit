using System;
using System.Collections.Generic;
using UnityEngine;

namespace LocalizationKit
{
    /// <summary>
    /// The authored source of truth: every language, every category, every entry.
    /// </summary>
    /// <remarks>
    /// This asset is shaped for editing and version control, not for lookups — entries live in
    /// lists and text is addressed positionally. <see cref="LocalizationTable.Build"/> turns it into
    /// the flat form the runtime actually reads. Nothing on a hot path should touch a catalog.
    /// <para>
    /// Every method that changes the language list repairs each entry's value array in the same
    /// operation, so the positional invariant holds after any single public call.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(fileName = "LocalizationCatalog", menuName = "LocalizationKit/Localization Catalog", order = 200)]
    public sealed class LocalizationCatalog : ScriptableObject
    {
        [SerializeField] private List<LanguageInfo> m_Languages = new List<LanguageInfo>();
        [SerializeField] private List<LocalizationCategory> m_Categories = new List<LocalizationCategory>();
        [SerializeField] private string m_DefaultLanguageCode;

        /// <summary>Languages in catalog order. Positional against every entry's values.</summary>
        public IReadOnlyList<LanguageInfo> Languages => m_Languages;

        /// <summary>Categories in authoring order.</summary>
        public IReadOnlyList<LocalizationCategory> Categories => m_Categories;

        /// <summary>
        /// The language used to fill gaps and as the startup language when nothing else applies.
        /// Falls back to the first language in the catalog.
        /// </summary>
        public string DefaultLanguageCode
        {
            get
            {
                if (!string.IsNullOrEmpty(m_DefaultLanguageCode) && IndexOfLanguage(m_DefaultLanguageCode) >= 0)
                    return m_DefaultLanguageCode;

                return m_Languages.Count > 0 ? m_Languages[0].Code : null;
            }
            set => m_DefaultLanguageCode = value;
        }

        /// <summary>Total entries across every category.</summary>
        public int EntryCount
        {
            get
            {
                var total = 0;
                for (var i = 0; i < m_Categories.Count; i++)
                    total += m_Categories[i].Entries.Count;

                return total;
            }
        }

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
        /// Adds a language and gives every existing entry an empty slot for it.
        /// Returns the index of the language, existing or new.
        /// </summary>
        public int AddLanguage(LanguageInfo language)
        {
            var existing = IndexOfLanguage(language.Code);
            if (existing >= 0) return existing;

            m_Languages.Add(language);
            ResizeEntries();

            return m_Languages.Count - 1;
        }

        /// <summary>Removes a language and drops its column from every entry. Returns false when absent.</summary>
        public bool RemoveLanguage(string code)
        {
            var index = IndexOfLanguage(code);
            if (index < 0) return false;

            m_Languages.RemoveAt(index);

            // Every slot keeps its value except the removed one, whose column collapses.
            var remap = new int[m_Languages.Count];
            for (var i = 0; i < remap.Length; i++)
                remap[i] = i < index ? i : i + 1;

            RemapEntries(remap);
            return true;
        }

        /// <summary>Moves a language within the list, carrying its column with it.</summary>
        public void MoveLanguage(int from, int to)
        {
            if ((uint)from >= (uint)m_Languages.Count) return;
            to = Mathf.Clamp(to, 0, m_Languages.Count - 1);
            if (from == to) return;

            var moved = m_Languages[from];
            m_Languages.RemoveAt(from);
            m_Languages.Insert(to, moved);

            // Rebuild the remap by replaying the same move over an identity ordering.
            var order = new List<int>(m_Languages.Count);
            for (var i = 0; i < m_Languages.Count; i++) order.Add(i);
            var carried = order[from];
            order.RemoveAt(from);
            order.Insert(to, carried);

            RemapEntries(order.ToArray());
        }

        /// <summary>Replaces the metadata of an existing language, keyed by its position.</summary>
        public void SetLanguage(int index, LanguageInfo language)
        {
            if ((uint)index >= (uint)m_Languages.Count) return;
            m_Languages[index] = language;
        }

        // ---------------------------------------------------------------- categories

        /// <summary>Finds a category by name, or null. Case-insensitive.</summary>
        public LocalizationCategory FindCategory(string name)
        {
            for (var i = 0; i < m_Categories.Count; i++)
            {
                if (string.Equals(m_Categories[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return m_Categories[i];
            }

            return null;
        }

        /// <summary>Returns the named category, creating it when it does not exist yet.</summary>
        public LocalizationCategory GetOrAddCategory(string name)
        {
            var existing = FindCategory(name);
            if (existing != null) return existing;

            var category = new LocalizationCategory(name);
            m_Categories.Add(category);

            return category;
        }

        /// <summary>Removes a category and every entry in it. Returns false when absent.</summary>
        public bool RemoveCategory(string name)
        {
            var category = FindCategory(name);
            if (category == null) return false;

            m_Categories.Remove(category);
            return true;
        }

        /// <summary>Moves a category within the list. Purely presentational.</summary>
        public void MoveCategory(int from, int to)
        {
            if ((uint)from >= (uint)m_Categories.Count) return;
            to = Mathf.Clamp(to, 0, m_Categories.Count - 1);
            if (from == to) return;

            var moved = m_Categories[from];
            m_Categories.RemoveAt(from);
            m_Categories.Insert(to, moved);
        }

        // ---------------------------------------------------------------- entries

        /// <summary>
        /// Adds an entry to a category — creating the category if needed — sized for the current
        /// language list. Returns the existing entry when the key is already taken.
        /// </summary>
        public LocalizationEntry AddEntry(string category, string key)
        {
            var target = GetOrAddCategory(string.IsNullOrEmpty(category) ? LocalizationKeys.DefaultCategory : category);

            var existing = target.Find(key);
            if (existing != null) return existing;

            var entry = new LocalizationEntry(key, m_Languages.Count);
            target.Entries.Add(entry);

            return entry;
        }

        /// <summary>Removes an entry by category and key. Returns false when absent.</summary>
        public bool RemoveEntry(string category, string key)
        {
            var target = FindCategory(category);
            var entry = target?.Find(key);
            if (entry == null) return false;

            target.Entries.Remove(entry);
            return true;
        }

        /// <summary>Finds an entry by its full <c>Category/Key</c> form, or null.</summary>
        public LocalizationEntry FindByFullKey(string fullKey)
        {
            if (string.IsNullOrEmpty(fullKey)) return null;

            var category = LocalizationKeys.TrySplit(fullKey, out var categoryName, out var key)
                ? FindCategory(categoryName)
                : FindCategory(LocalizationKeys.DefaultCategory);

            return category?.Find(key);
        }

        /// <summary>
        /// Every full key in the catalog, in category order then entry order. Allocates —
        /// this is for editor tooling and validation, not for gameplay.
        /// </summary>
        public List<string> GetAllKeys()
        {
            var keys = new List<string>(EntryCount);

            for (var c = 0; c < m_Categories.Count; c++)
            {
                var category = m_Categories[c];
                for (var e = 0; e < category.Entries.Count; e++)
                    keys.Add(LocalizationKeys.Compose(category.Name, category.Entries[e].Key));
            }

            return keys;
        }

        // ---------------------------------------------------------------- integrity

        /// <summary>
        /// Brings every entry's value array back to the catalog's language count. Cheap and
        /// idempotent; called on deserialize so a hand-edited or merged asset cannot present a
        /// ragged shape to the table builder.
        /// </summary>
        public void ResizeEntries()
        {
            var count = m_Languages.Count;

            for (var c = 0; c < m_Categories.Count; c++)
            {
                var entries = m_Categories[c].Entries;
                for (var e = 0; e < entries.Count; e++)
                {
                    var entry = entries[e];
                    if (entry == null) continue;
                    if (entry.Values.Length == count) continue;

                    var next = new string[count];
                    var copy = Mathf.Min(count, entry.Values.Length);
                    Array.Copy(entry.Values, next, copy);
                    entry.Values = next;
                }
            }
        }

        private void RemapEntries(int[] remap)
        {
            for (var c = 0; c < m_Categories.Count; c++)
            {
                var entries = m_Categories[c].Entries;
                for (var e = 0; e < entries.Count; e++)
                    entries[e]?.Remap(remap);
            }
        }

        private void OnValidate() => ResizeEntries();
    }
}
