using UnityEngine;
using UnityEngine.UI;

namespace LocalizationKit.Samples
{
    /// <summary>
    /// Cycles through the catalog's languages, and shows what changing one costs: nothing that
    /// needs measuring.
    /// </summary>
    /// <remarks>
    /// Note what this script does <i>not</i> do. It does not find the labels, hold references to
    /// them, or tell them anything. Every localized field and component in the scene registered
    /// itself; <see cref="Localization.SetLanguage(int)"/> reaches all of them.
    /// </remarks>
    [AddComponentMenu("LocalizationKit/Samples/Language Switcher")]
    public sealed class LanguageSwitcher : MonoBehaviour
    {
        [SerializeField] private Button m_NextButton;
        [SerializeField] private Text m_CurrentLabel;

        private void Awake()
        {
            if (m_NextButton != null) m_NextButton.onClick.AddListener(Next);
        }

        private void OnEnable()
        {
            Localization.LanguageChanged += UpdateLabel;
            UpdateLabel();
        }

        private void OnDisable()
        {
            Localization.LanguageChanged -= UpdateLabel;
        }

        /// <summary>Moves to the next language in the catalog, wrapping at the end.</summary>
        public void Next()
        {
            var count = Localization.Languages.Count;
            if (count == 0) return;

            Localization.SetLanguage((Localization.LanguageIndex + 1) % count);
        }

        private void UpdateLabel()
        {
            if (m_CurrentLabel == null) return;

            var language = Localization.Language;
            m_CurrentLabel.text = string.IsNullOrEmpty(language.Code)
                ? "no catalog"
                : $"{language.DisplayName} ({language.Code})";
        }
    }
}
