using System;
using UnityEngine;

namespace LocalizationKit
{
    /// <summary>
    /// One language a catalog carries. The <see cref="Code"/> is the identity — it is what
    /// <see cref="Localization.SetLanguage(string)"/> takes and what is persisted between sessions,
    /// so it must stay stable once content ships. Everything else is presentation.
    /// </summary>
    [Serializable]
    public struct LanguageInfo : IEquatable<LanguageInfo>
    {
        [SerializeField] private string m_Code;
        [SerializeField] private string m_DisplayName;
        [SerializeField] private SystemLanguage m_SystemLanguage;
        [SerializeField] private bool m_RightToLeft;

        /// <summary>BCP-47-ish identifier, e.g. <c>en</c>, <c>pt-BR</c>, <c>he</c>. Compared ordinally, case-insensitively.</summary>
        public string Code
        {
            get => m_Code;
            set => m_Code = value;
        }

        /// <summary>What a language picker shows. Falls back to <see cref="Code"/> when blank.</summary>
        public string DisplayName
        {
            get => string.IsNullOrEmpty(m_DisplayName) ? m_Code : m_DisplayName;
            set => m_DisplayName = value;
        }

        /// <summary>
        /// Which <see cref="SystemLanguage"/> this entry answers to when the active language is
        /// auto-detected from the device. <see cref="SystemLanguage.Unknown"/> opts out of detection.
        /// </summary>
        public SystemLanguage SystemLanguage
        {
            get => m_SystemLanguage;
            set => m_SystemLanguage = value;
        }

        /// <summary>True for scripts that read right to left. Consumed by the shipped text components.</summary>
        public bool RightToLeft
        {
            get => m_RightToLeft;
            set => m_RightToLeft = value;
        }

        public LanguageInfo(string code, string displayName = null, SystemLanguage systemLanguage = SystemLanguage.Unknown, bool rightToLeft = false)
        {
            m_Code = code;
            m_DisplayName = displayName;
            m_SystemLanguage = systemLanguage;
            m_RightToLeft = rightToLeft;
        }

        public bool Equals(LocalizationKit.LanguageInfo other) =>
            string.Equals(m_Code, other.m_Code, StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object obj) => obj is LanguageInfo other && Equals(other);

        public override int GetHashCode() =>
            m_Code == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(m_Code);

        public override string ToString() => DisplayName;
    }
}
