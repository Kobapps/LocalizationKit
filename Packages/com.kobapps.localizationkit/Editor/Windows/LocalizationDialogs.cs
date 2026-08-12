using System;
using System.Collections.Generic;
using EditorCoreKit.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LocalizationKit.Editor
{
    /// <summary>
    /// A one-field prompt — category names, mostly.
    /// </summary>
    /// <remarks>
    /// <c>EditorUtility.DisplayDialog</c> cannot take text input, and a whole inspector for one
    /// string is worse than a small modal. Enter confirms, Escape cancels, because a dialog that
    /// needs the mouse for a single word is a dialog people stop using.
    /// </remarks>
    internal sealed class LocalizationTextDialog : EditorWindow
    {
        private string m_Label;
        private string m_Value;
        private Action<string> m_OnConfirm;

        internal static void Open(string title, string label, string value, Action<string> onConfirm)
        {
            var window = CreateInstance<LocalizationTextDialog>();

            window.titleContent = new GUIContent(title);
            window.m_Label = label;
            window.m_Value = value ?? string.Empty;
            window.m_OnConfirm = onConfirm;
            window.minSize = new Vector2(320f, 110f);
            window.maxSize = new Vector2(320f, 110f);

            window.ShowModalUtility();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            KUITheme.Apply(root);

            var field = new TextField(m_Label) { value = m_Value };
            field.RegisterValueChangedCallback(e => m_Value = e.newValue.Trim());

            var buttons = KUILayout.Row();
            buttons.Add(KUILayout.Spacer());
            buttons.Add(KUIButton.Secondary("Cancel", Close));
            buttons.Add(KUIButton.Primary("OK", Confirm));

            root.Add(KUILayout.Page(field, buttons));

            root.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Confirm();
                else if (e.keyCode == KeyCode.Escape) Close();
            });

            field.Focus();
        }

        private void Confirm()
        {
            var confirm = m_OnConfirm;
            var value = m_Value;

            Close();
            confirm?.Invoke(value);
        }
    }

    /// <summary>
    /// Category plus key, which is how every key is actually named.
    /// </summary>
    /// <remarks>
    /// The two are asked for separately rather than as one <c>Category/Key</c> string so the
    /// category can be picked from what already exists. Free-typing the whole thing is how a
    /// catalog ends up with <c>Popups</c> and <c>popups</c> as separate categories.
    /// </remarks>
    internal sealed class LocalizationKeyDialog : EditorWindow
    {
        private string m_Category;
        private string m_Key;
        private List<string> m_Categories;
        private Action<string, string> m_OnConfirm;

        internal static void Open(
            string title,
            string category,
            string key,
            List<string> categories,
            Action<string, string> onConfirm)
        {
            var window = CreateInstance<LocalizationKeyDialog>();

            window.titleContent = new GUIContent(title);
            window.m_Category = category;
            window.m_Key = key ?? string.Empty;
            window.m_Categories = categories ?? new List<string>();
            window.m_OnConfirm = onConfirm;
            window.minSize = new Vector2(360f, 150f);
            window.maxSize = new Vector2(360f, 150f);

            window.ShowModalUtility();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            KUITheme.Apply(root);

            var preview = KUIText.Muted(string.Empty);

            void UpdatePreview() => preview.text = $"Full key:  {LocalizationKeys.Compose(m_Category, m_Key)}";

            // The menu is built on each open, so the checkmark tracks the current choice.
            var categoryRow = KUIText.KeyValue("Category", KUIDropdownButton.Create(
                string.IsNullOrEmpty(m_Category) ? LocalizationKeys.DefaultCategory : m_Category,
                menu =>
                {
                    foreach (var name in m_Categories)
                    {
                        var target = name;
                        menu.Item(target, () =>
                        {
                            m_Category = target;
                            RebuildAfterCategoryChange();
                        }, on: string.Equals(target, m_Category, StringComparison.OrdinalIgnoreCase));
                    }

                    menu.Separator();
                    menu.Item("New Category…", () => LocalizationTextDialog.Open(
                        "New Category",
                        "Name",
                        string.Empty,
                        created =>
                        {
                            if (!LocalizationKeys.IsValidName(created)) return;

                            m_Category = created;
                            if (!m_Categories.Contains(created)) m_Categories.Add(created);

                            RebuildAfterCategoryChange();
                        }));
                }));

            var keyField = new TextField("Key") { value = m_Key };
            keyField.RegisterValueChangedCallback(e =>
            {
                m_Key = e.newValue.Trim();
                UpdatePreview();
            });

            var buttons = KUILayout.Row();
            buttons.Add(KUILayout.Spacer());
            buttons.Add(KUIButton.Secondary("Cancel", Close));
            buttons.Add(KUIButton.Primary("OK", Confirm));

            UpdatePreview();

            root.Add(KUILayout.Page(categoryRow, keyField, preview, buttons));

            root.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Confirm();
                else if (e.keyCode == KeyCode.Escape) Close();
            });

            keyField.Focus();
        }

        private void RebuildAfterCategoryChange()
        {
            rootVisualElement.Clear();
            CreateGUI();
        }

        private void Confirm()
        {
            var confirm = m_OnConfirm;
            var category = string.IsNullOrEmpty(m_Category) ? LocalizationKeys.DefaultCategory : m_Category;
            var key = m_Key;

            Close();
            confirm?.Invoke(category, key);
        }
    }
}
