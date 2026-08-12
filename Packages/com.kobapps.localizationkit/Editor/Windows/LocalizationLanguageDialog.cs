using System;
using System.Collections.Generic;
using EditorCoreKit.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LocalizationKit.Editor
{
    /// <summary>
    /// The small modal for creating or editing one language.
    /// </summary>
    /// <remarks>
    /// A separate window rather than an inline row because a language has four fields, one of which
    /// (<see cref="LanguageInfo.SystemLanguage"/>) is a long enum — inline that and the list stops
    /// being scannable, which is the list's whole job.
    /// </remarks>
    internal sealed class LocalizationLanguageDialog : EditorWindow
    {
        private LanguageInfo m_Value;
        private bool m_IsNew;
        private Action<LanguageInfo> m_OnConfirm;

        internal static void Open(LanguageInfo value, bool isNew, Action<LanguageInfo> onConfirm)
        {
            var window = CreateInstance<LocalizationLanguageDialog>();

            window.m_Value = value;
            window.m_IsNew = isNew;
            window.m_OnConfirm = onConfirm;
            window.titleContent = new GUIContent(isNew ? "Add Language" : "Edit Language");
            window.minSize = new Vector2(340f, 210f);
            window.maxSize = new Vector2(340f, 210f);

            window.ShowModalUtility();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            KUITheme.Apply(root);

            var card = new KUICard(
                m_IsNew ? "New language" : m_Value.DisplayName,
                m_IsNew ? "The code is permanent once content ships against it." : m_Value.Code);

            var code = new TextField("Code") { value = m_Value.Code ?? string.Empty };
            code.tooltip = "BCP-47-ish identifier: en, fr, pt-BR, he.";
            code.SetEnabled(m_IsNew);
            code.RegisterValueChangedCallback(e => m_Value.Code = e.newValue.Trim());
            card.Add(code);

            var display = new TextField("Display name") { value = m_Value.DisplayName ?? string.Empty };
            display.RegisterValueChangedCallback(e => m_Value.DisplayName = e.newValue);
            card.Add(display);

            var system = new EnumField("Device language", m_Value.SystemLanguage);
            system.tooltip = "Matched against Application.systemLanguage when the language is auto-detected.";
            system.RegisterValueChangedCallback(e => m_Value.SystemLanguage = (SystemLanguage)e.newValue);
            card.Add(system);

            var rtl = new Toggle("Right to left") { value = m_Value.RightToLeft };
            rtl.RegisterValueChangedCallback(e => m_Value.RightToLeft = e.newValue);
            card.Add(rtl);

            if (m_IsNew)
            {
                // Filling the display name and direction from the code is the difference between
                // typing one field and typing four, for the codes almost everyone actually uses.
                code.RegisterValueChangedCallback(e =>
                {
                    if (!string.IsNullOrEmpty(display.value)) return;

                    if (TryGuess(e.newValue, out var guessed))
                    {
                        display.value = guessed.DisplayName;
                        system.value = guessed.SystemLanguage;
                        rtl.value = guessed.RightToLeft;
                    }
                });
            }

            var buttons = KUILayout.Row();
            buttons.Add(KUILayout.Spacer());
            buttons.Add(KUIButton.Secondary("Cancel", Close));
            buttons.Add(KUIButton.Primary(m_IsNew ? "Add" : "Save", Confirm));

            var page = KUILayout.Page(card, buttons);
            root.Add(page);

            root.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Confirm();
                else if (e.keyCode == KeyCode.Escape) Close();
            });

            code.Focus();
        }

        private void Confirm()
        {
            var confirm = m_OnConfirm;
            var value = m_Value;

            Close();
            confirm?.Invoke(value);
        }

        /// <summary>
        /// The handful of codes that cover most projects. Not a full CLDR table — just enough that
        /// typing "fr" does the obvious thing.
        /// </summary>
        private static readonly Dictionary<string, LanguageInfo> s_Known = new Dictionary<string, LanguageInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = new LanguageInfo("en", "English", SystemLanguage.English),
            ["fr"] = new LanguageInfo("fr", "Français", SystemLanguage.French),
            ["de"] = new LanguageInfo("de", "Deutsch", SystemLanguage.German),
            ["es"] = new LanguageInfo("es", "Español", SystemLanguage.Spanish),
            ["it"] = new LanguageInfo("it", "Italiano", SystemLanguage.Italian),
            ["pt"] = new LanguageInfo("pt", "Português", SystemLanguage.Portuguese),
            ["ru"] = new LanguageInfo("ru", "Русский", SystemLanguage.Russian),
            ["ja"] = new LanguageInfo("ja", "日本語", SystemLanguage.Japanese),
            ["ko"] = new LanguageInfo("ko", "한국어", SystemLanguage.Korean),
            ["zh"] = new LanguageInfo("zh", "中文", SystemLanguage.Chinese),
            ["tr"] = new LanguageInfo("tr", "Türkçe", SystemLanguage.Turkish),
            ["pl"] = new LanguageInfo("pl", "Polski", SystemLanguage.Polish),
            ["nl"] = new LanguageInfo("nl", "Nederlands", SystemLanguage.Dutch),
            ["he"] = new LanguageInfo("he", "עברית", SystemLanguage.Hebrew, rightToLeft: true),
            ["ar"] = new LanguageInfo("ar", "العربية", SystemLanguage.Arabic, rightToLeft: true),
        };

        private static bool TryGuess(string code, out LanguageInfo info)
        {
            info = default;
            if (string.IsNullOrWhiteSpace(code)) return false;

            code = code.Trim();

            if (s_Known.TryGetValue(code, out info)) return true;

            // "pt-BR" should still suggest Portuguese.
            var dash = code.IndexOf('-');
            if (dash > 0 && s_Known.TryGetValue(code.Substring(0, dash), out info))
            {
                info.Code = code;
                return true;
            }

            return false;
        }
    }
}
