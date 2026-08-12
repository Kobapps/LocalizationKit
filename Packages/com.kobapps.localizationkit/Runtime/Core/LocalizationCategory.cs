using System;
using System.Collections.Generic;
using UnityEngine;

namespace LocalizationKit
{
    /// <summary>
    /// A named group of entries — <c>Default</c>, <c>Popups</c>, <c>Store</c>, <c>Tutorials</c>.
    /// Categories are an authoring convenience and a namespace for keys; they carry no runtime
    /// cost, because the table flattens them into <c>Category/Key</c> at build time.
    /// </summary>
    [Serializable]
    public sealed class LocalizationCategory
    {
        [SerializeField] private string m_Name;
        [SerializeField] private string m_Description;
        [SerializeField] private List<LocalizationEntry> m_Entries = new List<LocalizationEntry>();

        /// <summary>Category name. Unique within a catalog, compared case-insensitively.</summary>
        public string Name
        {
            get => m_Name;
            set => m_Name = value;
        }

        /// <summary>What belongs in here. Shown in the editor window only.</summary>
        public string Description
        {
            get => m_Description;
            set => m_Description = value;
        }

        /// <summary>The entries in this category, in authoring order.</summary>
        public List<LocalizationEntry> Entries => m_Entries;

        public LocalizationCategory() { }

        public LocalizationCategory(string name)
        {
            m_Name = name;
        }

        /// <summary>Finds an entry by key within this category, or null.</summary>
        public LocalizationEntry Find(string key)
        {
            for (var i = 0; i < m_Entries.Count; i++)
            {
                if (string.Equals(m_Entries[i].Key, key, StringComparison.Ordinal))
                    return m_Entries[i];
            }

            return null;
        }
    }
}
