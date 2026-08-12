using TMPro;
using UnityEngine;

namespace LocalizationKit
{
    /// <summary>
    /// Drives a <see cref="TMP_Text"/> from a catalog key, optionally swapping the font asset per
    /// language.
    /// </summary>
    /// <remarks>
    /// The font override is the reason this is more than a copy of <c>LocalizedText</c>. A TMP font
    /// asset carries a baked glyph set, so a Latin font has nothing to draw Hebrew or Japanese with
    /// and silently renders squares. Naming a per-language font here fixes that at the point the
    /// language changes, which is the only moment anything knows it needs to.
    /// <para>
    /// Gated on <c>com.unity.ugui</c> 2.0.0+, which is where TextMeshPro lives from Unity 6 onward.
    /// </para>
    /// </remarks>
    [AddComponentMenu("LocalizationKit/Localized TMP Text")]
    public sealed class LocalizedTMPText : LocalizedTextBase
    {
        /// <summary>A font asset to use while a given language is active.</summary>
        [System.Serializable]
        public struct FontOverride
        {
            /// <summary>Language code this applies to.</summary>
            public string LanguageCode;

            /// <summary>Font asset to swap in.</summary>
            public TMP_FontAsset Font;
        }

        [SerializeField] private TMP_Text m_Target;
        [SerializeField] private FontOverride[] m_FontOverrides = System.Array.Empty<FontOverride>();

        private TMP_FontAsset m_OriginalFont;
        private bool m_CapturedOriginalFont;

        /// <summary>The TMP component this writes into. Found on this GameObject when unassigned.</summary>
        public TMP_Text Target
        {
            get
            {
                if (m_Target == null) m_Target = GetComponent<TMP_Text>();
                return m_Target;
            }
            set => m_Target = value;
        }

        /// <summary>Per-language font assets. Languages not listed keep the authored font.</summary>
        public FontOverride[] FontOverrides
        {
            get => m_FontOverrides;
            set => m_FontOverrides = value ?? System.Array.Empty<FontOverride>();
        }

        /// <inheritdoc />
        protected override void ApplyText(string value)
        {
            var target = Target;
            if (target == null) return;

            ApplyFont(target);

            // SetText avoids the string comparison TMP's text setter does internally, but assigning
            // an unchanged string still forces a mesh regeneration — so check first.
            if (string.Equals(target.text, value, System.StringComparison.Ordinal)) return;

            target.SetText(value);
        }

        /// <inheritdoc />
        protected override string ReadText() => Target != null ? Target.text : null;

        /// <inheritdoc />
        protected override void ApplyDirection(bool rightToLeft)
        {
            var target = Target;
            if (target == null) return;

            target.isRightToLeftText = rightToLeft;
        }

        private void ApplyFont(TMP_Text target)
        {
            if (m_FontOverrides.Length == 0) return;

            if (!m_CapturedOriginalFont)
            {
                m_OriginalFont = target.font;
                m_CapturedOriginalFont = true;
            }

            var code = Localization.LanguageCode;
            var next = m_OriginalFont;

            for (var i = 0; i < m_FontOverrides.Length; i++)
            {
                var candidate = m_FontOverrides[i];
                if (candidate.Font == null) continue;

                if (string.Equals(candidate.LanguageCode, code, System.StringComparison.OrdinalIgnoreCase))
                {
                    next = candidate.Font;
                    break;
                }
            }

            if (next != null && target.font != next)
                target.font = next;
        }
    }
}
