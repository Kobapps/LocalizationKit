using System;
using UnityEngine;

namespace LocalizationKit
{
    /// <summary>
    /// One localized string, in every language the catalog carries.
    /// </summary>
    /// <remarks>
    /// <see cref="Values"/> is positional: index <c>i</c> is the text for the catalog's language
    /// <c>i</c>. That keeps the serialized asset small — a language code is stored once per catalog
    /// rather than once per entry per language — at the cost of the array having to be kept in step
    /// when languages are added, removed or reordered. Nothing outside the catalog does that
    /// bookkeeping: go through <see cref="LocalizationCatalog"/>'s editing methods, which repair the
    /// arrays as part of the operation.
    /// </remarks>
    [Serializable]
    public sealed class LocalizationEntry
    {
        [SerializeField] private string m_Key;
        [SerializeField] private string m_Description;
        [SerializeField] private string[] m_Values = Array.Empty<string>();

        /// <summary>
        /// Identifier within the owning category. The full key a lookup takes is
        /// <c>Category/Key</c> — see <see cref="LocalizationKeys.Compose"/>.
        /// </summary>
        public string Key
        {
            get => m_Key;
            set => m_Key = value;
        }

        /// <summary>Free-text note for whoever translates this. Never shown to players.</summary>
        public string Description
        {
            get => m_Description;
            set => m_Description = value;
        }

        /// <summary>Text per language, positional against the catalog's language list.</summary>
        public string[] Values
        {
            get => m_Values;
            internal set => m_Values = value ?? Array.Empty<string>();
        }

        public LocalizationEntry() { }

        public LocalizationEntry(string key, int languageCount)
        {
            m_Key = key;
            m_Values = new string[Mathf.Max(0, languageCount)];
        }

        /// <summary>Text for <paramref name="languageIndex"/>, or null when out of range or unset.</summary>
        public string GetValue(int languageIndex) =>
            (uint)languageIndex < (uint)m_Values.Length ? m_Values[languageIndex] : null;

        /// <summary>Assigns text for <paramref name="languageIndex"/>. Out-of-range indices are ignored.</summary>
        public void SetValue(int languageIndex, string value)
        {
            if ((uint)languageIndex < (uint)m_Values.Length)
                m_Values[languageIndex] = value;
        }

        /// <summary>True when this entry has no text for the given language.</summary>
        public bool IsMissing(int languageIndex) => string.IsNullOrEmpty(GetValue(languageIndex));

        /// <summary>Grows, shrinks or reorders <see cref="Values"/> to match a new language layout.</summary>
        /// <param name="remap">
        /// For each slot in the new layout, the index it takes its value from in the old layout,
        /// or -1 for a language that did not exist before.
        /// </param>
        internal void Remap(int[] remap)
        {
            var next = new string[remap.Length];
            for (var i = 0; i < remap.Length; i++)
            {
                var from = remap[i];
                next[i] = (uint)from < (uint)m_Values.Length ? m_Values[from] : null;
            }

            m_Values = next;
        }
    }
}
