using System;
using System.Collections.Generic;
using EditorCoreKit.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LocalizationKit.Editor
{
    /// <summary>
    /// Shared furniture for the kit's small modals: where they open and how they are laid out.
    /// </summary>
    /// <remarks>
    /// A <c>CreateInstance</c>-d <see cref="EditorWindow"/> has no position until one is given, so
    /// it opens wherever the last window of its type happened to be — in practice the top-left
    /// corner of the display. A modal the user has to hunt for is a modal that interrupts twice.
    /// </remarks>
    internal static class LocalizationDialog
    {
        /// <summary>Width of the label column in a dialog's fields.</summary>
        private const float k_LabelWidth = 72f;

        /// <summary>
        /// Sizes a window and centres it over the editor, slightly above the middle — the point
        /// the eye lands on, and clear of the mouse if the dialog was opened from a menu.
        /// </summary>
        internal static void PlaceCentered(EditorWindow window, float width, float height)
        {
            window.minSize = new Vector2(width, height);

            // Not locked to one size: a long key or a long category name is exactly the case where
            // the user wants to widen the dialog, and a fixed maxSize takes that away.
            window.maxSize = new Vector2(Mathf.Max(width * 2.5f, 900f), Mathf.Max(height * 2.5f, 700f));

            var main = EditorGUIUtility.GetMainWindowPosition();

            // A main window rect is unavailable in a few configurations (a detached editor on a
            // second display, most often) and comes back empty. Anywhere sensible beats zero.
            if (main.width < 1f || main.height < 1f)
                main = new Rect(120f, 120f, 1280f, 720f);

            var x = main.x + ((main.width - width) * 0.5f);
            var y = main.y + ((main.height - height) * 0.4f);

            window.position = new Rect(Mathf.Round(x), Mathf.Round(y), width, height);
        }

        /// <summary>The padded column a dialog's contents live in.</summary>
        internal static VisualElement Body(VisualElement root, string message)
        {
            KUITheme.Apply(root);

            var body = new VisualElement();
            body.style.flexGrow = 1;
            body.style.minHeight = 0;
            body.style.paddingTop = 12f;
            body.style.paddingBottom = 10f;
            body.style.paddingLeft = 14f;
            body.style.paddingRight = 14f;

            if (!string.IsNullOrEmpty(message))
            {
                var muted = KUIText.Muted(message);
                muted.style.marginBottom = 8f;
                body.Add(muted);
            }

            root.Add(body);
            return body;
        }

        /// <summary>One labelled field, with every label the same width so the controls line up.</summary>
        internal static VisualElement Field(string label, VisualElement control)
        {
            var text = new Label(label);
            text.AddToClassList(KUIClass.KeyValueKey);
            text.style.width = k_LabelWidth;
            text.style.minWidth = k_LabelWidth;
            text.style.marginTop = 2f;

            control.style.flexGrow = 1;
            control.style.flexShrink = 1;
            control.style.minWidth = 0;
            control.style.marginLeft = 0f;
            control.style.marginRight = 0f;

            var row = KUILayout.Row(text, control);
            row.style.alignItems = Align.FlexStart;
            row.style.marginBottom = 6f;
            row.style.minWidth = 0;

            return row;
        }

        /// <summary>
        /// The error strip under a dialog's fields. Always in the hierarchy, hidden when there is
        /// nothing wrong, so the controls above it do not jump as the user types.
        /// </summary>
        internal static KUIBanner ErrorBanner()
        {
            var banner = KUIBanner.Error(string.Empty);
            banner.style.display = DisplayStyle.None;
            banner.style.marginTop = 2f;
            return banner;
        }

        /// <summary>Shows or hides an error strip and reports whether the dialog may be confirmed.</summary>
        internal static bool ShowError(KUIBanner banner, string error)
        {
            var hasError = !string.IsNullOrEmpty(error);

            banner.style.display = hasError ? DisplayStyle.Flex : DisplayStyle.None;
            if (hasError) banner.Message = error;

            return !hasError;
        }

        /// <summary>
        /// Where a category sits in a nested menu.
        /// </summary>
        /// <remarks>
        /// A <c>GenericMenu</c> path builds submenus from its separators, which is exactly what a
        /// nested category wants — but the same path cannot be both a command and a submenu. A
        /// category that also has children is therefore listed as the first entry inside its own
        /// submenu, rather than being silently unreachable.
        /// </remarks>
        internal static string CategoryMenuPath(List<string> categories, string name)
        {
            foreach (var other in categories)
            {
                if (LocalizationKeys.IsUnder(other, name) &&
                    !string.Equals(other, name, StringComparison.OrdinalIgnoreCase))
                {
                    return name + LocalizationKeys.Separator + "(this category)";
                }
            }

            return name;
        }

        /// <summary>The confirm/cancel pair, pinned to the bottom-right of a dialog.</summary>
        internal static VisualElement Buttons(VisualElement body, Button confirm, Action cancel)
        {
            body.Add(KUILayout.Spacer());

            var separator = KUILayout.Separator();
            separator.style.marginTop = 8f;
            separator.style.marginBottom = 8f;
            body.Add(separator);

            var row = KUILayout.Row();
            row.Add(KUILayout.Spacer());
            row.Add(KUIButton.Secondary("Cancel", cancel));
            row.Add(confirm);

            body.Add(row);
            return row;
        }
    }

    /// <summary>
    /// A one-field prompt — category names, mostly.
    /// </summary>
    /// <remarks>
    /// <c>EditorUtility.DisplayDialog</c> cannot take text input, and a whole inspector for one
    /// string is worse than a small modal. Enter confirms, Escape cancels, because a dialog that
    /// needs the mouse for a single word is a dialog people stop using.
    /// <para>
    /// The name is checked as it is typed rather than after the modal closes: reporting a clash in
    /// a toast, once the dialog is gone and the typing with it, makes the user start over.
    /// </para>
    /// </remarks>
    internal sealed class LocalizationTextDialog : EditorWindow
    {
        private string m_Message;
        private string m_Label;
        private string m_Value;
        private Func<string, string> m_Validate;
        private Action<string> m_OnConfirm;

        private KUIBanner m_Error;
        private Button m_Confirm;

        /// <summary>Opens the prompt.</summary>
        /// <param name="title">Window title.</param>
        /// <param name="message">One line explaining what the value is for.</param>
        /// <param name="label">Label beside the field.</param>
        /// <param name="value">Starting value.</param>
        /// <param name="validate">Why the current value cannot be used, or null when it can.</param>
        /// <param name="onConfirm">Called with the value, which has already passed validation.</param>
        internal static void Open(
            string title,
            string message,
            string label,
            string value,
            Func<string, string> validate,
            Action<string> onConfirm)
        {
            var window = CreateInstance<LocalizationTextDialog>();

            window.titleContent = new GUIContent(title);
            window.m_Message = message;
            window.m_Label = label;
            window.m_Value = value ?? string.Empty;
            window.m_Validate = validate;
            window.m_OnConfirm = onConfirm;

            LocalizationDialog.PlaceCentered(window, 400f, 176f);
            window.ShowModalUtility();
        }

        private void CreateGUI()
        {
            var body = LocalizationDialog.Body(rootVisualElement, m_Message);

            var field = new TextField { value = m_Value };
            field.RegisterValueChangedCallback(e =>
            {
                m_Value = e.newValue.Trim();
                Revalidate();
            });

            body.Add(LocalizationDialog.Field(m_Label, field));

            m_Error = LocalizationDialog.ErrorBanner();
            body.Add(m_Error);

            m_Confirm = KUIButton.Primary("OK", Confirm);
            LocalizationDialog.Buttons(body, m_Confirm, Close);

            Revalidate();

            rootVisualElement.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Confirm();
                else if (e.keyCode == KeyCode.Escape) Close();
            });

            field.Focus();
            field.SelectAll();
        }

        private void Revalidate()
        {
            var error = m_Validate?.Invoke(m_Value);
            var valid = LocalizationDialog.ShowError(m_Error, error) && !string.IsNullOrWhiteSpace(m_Value);

            m_Confirm.SetEnabled(valid);
        }

        private void Confirm()
        {
            if (string.IsNullOrWhiteSpace(m_Value)) return;
            if (!string.IsNullOrEmpty(m_Validate?.Invoke(m_Value))) return;

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
    /// <para>
    /// Choosing a category updates the controls in place. An earlier version rebuilt the whole
    /// view instead, which re-registered the Enter handler on the root every time — and the root
    /// survives <c>Clear()</c>, so after one category change Enter confirmed the dialog twice.
    /// </para>
    /// </remarks>
    internal sealed class LocalizationKeyDialog : EditorWindow
    {
        private string m_Message;
        private string m_Category;
        private string m_Key;
        private List<string> m_Categories;
        private Func<string, string, string> m_Validate;
        private Action<string, string> m_OnConfirm;

        private VisualElement m_CategoryHost;
        private Label m_Preview;
        private KUIBanner m_Error;
        private Button m_Confirm;

        /// <summary>True while the category is being typed rather than picked.</summary>
        private bool m_NewCategory;

        /// <summary>The picked category to fall back to when the typing is cancelled.</summary>
        private string m_Restore;

        /// <summary>Opens the prompt.</summary>
        /// <param name="title">Window title.</param>
        /// <param name="message">One line explaining what the key is for.</param>
        /// <param name="category">Starting category.</param>
        /// <param name="key">Starting key.</param>
        /// <param name="categories">Categories to choose from.</param>
        /// <param name="validate">Why the pair cannot be used, or null when it can.</param>
        /// <param name="onConfirm">Called with the pair, which has already passed validation.</param>
        internal static void Open(
            string title,
            string message,
            string category,
            string key,
            List<string> categories,
            Func<string, string, string> validate,
            Action<string, string> onConfirm)
        {
            var window = CreateInstance<LocalizationKeyDialog>();

            window.titleContent = new GUIContent(title);
            window.m_Message = message;
            window.m_Category = string.IsNullOrEmpty(category) ? LocalizationKeys.DefaultCategory : category;
            window.m_Key = key ?? string.Empty;
            window.m_Categories = categories ?? new List<string>();
            window.m_Validate = validate;
            window.m_OnConfirm = onConfirm;

            LocalizationDialog.PlaceCentered(window, 480f, 268f);
            window.ShowModalUtility();
        }

        private void CreateGUI()
        {
            var body = LocalizationDialog.Body(rootVisualElement, m_Message);

            body.Add(LocalizationDialog.Field("Category", BuildCategoryPicker()));

            var keyField = new TextField { value = m_Key };
            keyField.RegisterValueChangedCallback(e =>
            {
                m_Key = e.newValue.Trim();
                Revalidate();
            });

            body.Add(LocalizationDialog.Field("Key", keyField));

            m_Preview = KUIText.Code(string.Empty);
            m_Preview.style.marginTop = 0f;
            body.Add(LocalizationDialog.Field("Full key", m_Preview));

            m_Error = LocalizationDialog.ErrorBanner();
            body.Add(m_Error);

            m_Confirm = KUIButton.Primary("OK", Confirm);
            LocalizationDialog.Buttons(body, m_Confirm, Close);

            Revalidate();

            rootVisualElement.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Confirm();
                else if (e.keyCode == KeyCode.Escape) Close();
            });

            keyField.Focus();
            keyField.SelectAll();
        }

        /// <summary>
        /// The category control: a dropdown of what already exists, which swaps to a text field
        /// when "New Category…" is chosen.
        /// </summary>
        /// <remarks>
        /// The new-category prompt is inline rather than a second window. A modal opened from
        /// inside a modal is a fight with Unity's window stack that Unity wins — the child shows
        /// up blank — and nothing about typing one name needs a window of its own.
        /// </remarks>
        private VisualElement BuildCategoryPicker()
        {
            m_CategoryHost = KUILayout.Row();
            m_CategoryHost.style.flexGrow = 1;
            m_CategoryHost.style.minWidth = 0;

            ShowCategoryDropdown();
            return m_CategoryHost;
        }

        private void ShowCategoryDropdown()
        {
            m_NewCategory = false;
            m_CategoryHost.Clear();

            var button = KUIDropdownButton.Create(
                string.IsNullOrEmpty(m_Category) ? LocalizationKeys.DefaultCategory : m_Category,
                BuildCategoryMenu);

            button.style.flexGrow = 1;
            button.style.minWidth = 0;

            m_CategoryHost.Add(button);
        }

        private void BuildCategoryMenu(KUIMenu menu)
        {
            // Nested categories nest in the menu too: the separators in "Popups/Quit Level" are
            // what GenericMenu builds its submenus from, so this needs no special handling beyond
            // keeping a category that is also a parent reachable.
            foreach (var name in m_Categories)
            {
                var target = name;
                menu.Item(
                    LocalizationDialog.CategoryMenuPath(m_Categories, target),
                    () => SetCategory(target),
                    on: string.Equals(target, m_Category, StringComparison.OrdinalIgnoreCase));
            }

            menu.Separator();
            menu.Item("New Category…", () => ShowCategoryEntry(string.Empty));

            if (!string.IsNullOrEmpty(m_Category))
            {
                var parent = m_Category;

                // The separators are swapped out of the label: a '/' in a GenericMenu item is a
                // submenu, so the path would turn this one command into a chain of empty menus.
                menu.Item(
                    $"New Subcategory of {parent.Replace(LocalizationKeys.Separator, '›')}…",
                    () => ShowCategoryEntry(parent + LocalizationKeys.Separator));
            }
        }

        private void ShowCategoryEntry(string seed)
        {
            m_NewCategory = true;
            m_Restore = m_Category;
            m_Category = (seed ?? string.Empty).Trim();

            m_CategoryHost.Clear();

            var field = new TextField { value = seed ?? string.Empty };
            field.style.flexGrow = 1;
            field.style.minWidth = 0;
            field.style.marginLeft = 0f;
            field.style.marginRight = 0f;
            field.tooltip = "Nest with '/' — Popups/Quit Level.";

            field.RegisterValueChangedCallback(e =>
            {
                m_Category = (e.newValue ?? string.Empty).Trim();
                Revalidate();
            });

            m_CategoryHost.Add(field);
            m_CategoryHost.Add(KUIButton.Icon(KUIIcons.Close, () =>
            {
                m_Category = m_Restore;
                ShowCategoryDropdown();
                Revalidate();
            }, "Pick an existing category instead."));

            Revalidate();

            // Deferred: the field is not in a panel yet on the frame the menu item fires, and
            // focusing an element that has no panel does nothing at all.
            field.schedule.Execute(() =>
            {
                field.Focus();

                // Past the seeded "Parent/" prefix, which is there to be built on, not retyped.
                field.SelectRange(field.value.Length, field.value.Length);
            });
        }

        /// <summary>Why the typed category cannot be used, or null when it can.</summary>
        private string CategoryError()
        {
            if (!m_NewCategory) return null;
            if (string.IsNullOrWhiteSpace(m_Category)) return null;   // Nothing typed yet is not an error.

            if (!LocalizationKeys.IsValidCategory(m_Category))
                return "A category cannot start or end with '/', or have an empty part.";

            return null;
        }

        private void SetCategory(string category)
        {
            m_Category = category;
            ShowCategoryDropdown();
            Revalidate();
        }

        private void Revalidate()
        {
            var full = LocalizationKeys.Compose(m_Category, m_Key);
            m_Preview.text = string.IsNullOrEmpty(full) ? KUIIcons.EmDash : full;

            var error = CategoryError() ?? m_Validate?.Invoke(m_Category, m_Key);

            var valid = LocalizationDialog.ShowError(m_Error, error)
                && !string.IsNullOrWhiteSpace(m_Key)
                && !string.IsNullOrWhiteSpace(m_Category);

            m_Confirm.SetEnabled(valid);
        }

        private void Confirm()
        {
            if (string.IsNullOrWhiteSpace(m_Key) || string.IsNullOrWhiteSpace(m_Category)) return;
            if (CategoryError() != null) return;
            if (!string.IsNullOrEmpty(m_Validate?.Invoke(m_Category, m_Key))) return;

            var confirm = m_OnConfirm;
            var category = string.IsNullOrEmpty(m_Category) ? LocalizationKeys.DefaultCategory : m_Category;
            var key = m_Key;

            Close();
            confirm?.Invoke(category, key);
        }
    }
}
