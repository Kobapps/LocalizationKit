using UnityEngine;
using UnityEngine.UI;

namespace LocalizationKit
{
    /// <summary>
    /// Drives a uGUI <see cref="Text"/> from a catalog key.
    /// </summary>
    /// <remarks>
    /// Lives in its own assembly, gated on <c>com.unity.ugui</c> being installed. A project without
    /// uGUI simply does not compile this file, rather than failing to compile the kit — which is
    /// why the core runtime assembly references no UI package at all.
    /// </remarks>
    [AddComponentMenu("LocalizationKit/Localized Text")]
    [RequireComponent(typeof(Text))]
    public sealed class LocalizedText : LocalizedTextBase
    {
        private Text m_Target;

        /// <summary>The Text this writes into. Cached on first use.</summary>
        public Text Target
        {
            get
            {
                if (m_Target == null) m_Target = GetComponent<Text>();
                return m_Target;
            }
        }

        /// <inheritdoc />
        protected override void ApplyText(string value)
        {
            var target = Target;
            if (target == null) return;

            // uGUI rebuilds its mesh on any assignment, equal or not. Comparing first turns a
            // language change on an unchanged label into nothing at all.
            if (string.Equals(target.text, value, System.StringComparison.Ordinal)) return;

            target.text = value;
        }

        /// <inheritdoc />
        protected override string ReadText() => Target != null ? Target.text : null;

        /// <inheritdoc />
        protected override void ApplyDirection(bool rightToLeft)
        {
            var target = Target;
            if (target == null) return;

            switch (target.alignment)
            {
                case TextAnchor.UpperLeft:
                case TextAnchor.UpperRight:
                    target.alignment = rightToLeft ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
                    break;
                case TextAnchor.MiddleLeft:
                case TextAnchor.MiddleRight:
                    target.alignment = rightToLeft ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
                    break;
                case TextAnchor.LowerLeft:
                case TextAnchor.LowerRight:
                    target.alignment = rightToLeft ? TextAnchor.LowerRight : TextAnchor.LowerLeft;
                    break;
            }
        }
    }
}
