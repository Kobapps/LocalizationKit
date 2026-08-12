using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace LocalizationKit.Editor
{
    /// <summary>
    /// Turns a <c>[LocalizationKey]</c> string field into a searchable picker over the catalog,
    /// with the resolved text shown underneath.
    /// </summary>
    /// <remarks>
    /// Drawn in IMGUI rather than UI Toolkit on purpose. A <see cref="PropertyDrawer"/> has to work
    /// inside whatever inspector it lands in — including custom IMGUI editors written years ago —
    /// and an IMGUI drawer renders correctly in a UI Toolkit inspector while the reverse is not
    /// true. That makes IMGUI the choice that works everywhere rather than the old choice.
    /// <para>
    /// The dropdown itself is <see cref="AdvancedDropdown"/>, which brings search and a tree for
    /// free — and the tree is exactly the category hierarchy, so a catalog with thousands of keys
    /// stays navigable where a flat popup would not.
    /// </para>
    /// </remarks>
    [CustomPropertyDrawer(typeof(LocalizationKeyAttribute))]
    internal sealed class LocalizationKeyDrawer : PropertyDrawer
    {
        private const float PreviewHeight = 16f;
        private const float Gap = 2f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var baseHeight = EditorGUIUtility.singleLineHeight;
            if (property.propertyType != SerializedPropertyType.String) return baseHeight;

            return baseHeight + Gap + PreviewHeight;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.LabelField(position, label.text, "[LocalizationKey] only applies to string fields.");
                return;
            }

            var key = (LocalizationKeyAttribute)attribute;
            var catalog = LocalizationEditorCatalog.Catalog;

            var row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            var preview = new Rect(position.x, row.yMax + Gap, position.width, PreviewHeight);

            EditorGUI.BeginProperty(row, label, property);

            var fieldRect = EditorGUI.PrefixLabel(row, label);

            if (catalog == null)
            {
                DrawNoCatalog(fieldRect, preview);
                EditorGUI.EndProperty();
                return;
            }

            var current = property.stringValue;
            var known = !string.IsNullOrEmpty(current) && LocalizationEditorCatalog.HasKey(current);

            // A key that no longer exists is the failure this drawer exists to catch — a renamed or
            // deleted entry leaves a field pointing at nothing, and nothing else in the editor says so.
            var content = new GUIContent(
                string.IsNullOrEmpty(current) ? "(none)" : current,
                known || string.IsNullOrEmpty(current) ? null : "This key is not in the catalog.");

            var previousColor = GUI.color;
            if (!known && !string.IsNullOrEmpty(current)) GUI.color = new Color(1f, 0.6f, 0.6f);

            if (EditorGUI.DropdownButton(fieldRect, content, FocusType.Keyboard))
            {
                var keys = LocalizationEditorCatalog.KeysInCategory(key.Category);
                ShowPicker(fieldRect, property, keys, key.AllowMissing, current);
            }

            GUI.color = previousColor;

            EditorGUI.EndProperty();

            DrawPreview(preview, current, known, catalog);
        }

        private static void DrawNoCatalog(Rect fieldRect, Rect preview)
        {
            if (GUI.Button(fieldRect, "Create a catalog…", EditorStyles.miniButton))
                LocalizationKitWindow.Open();

            EditorGUI.LabelField(preview, " ", "No localization catalog in this project.", EditorStyles.miniLabel);
        }

        private static void DrawPreview(Rect rect, string current, bool known, LocalizationCatalog catalog)
        {
            if (string.IsNullOrEmpty(current))
            {
                EditorGUI.LabelField(rect, " ", "No key selected.", EditorStyles.miniLabel);
                return;
            }

            if (!known)
            {
                EditorGUI.LabelField(rect, " ", "Missing from the catalog.", EditorStyles.miniLabel);
                return;
            }

            var entry = catalog.FindByFullKey(current);
            var languageIndex = catalog.IndexOfLanguage(catalog.DefaultLanguageCode);
            var text = entry != null ? entry.GetValue(languageIndex) : null;

            EditorGUI.LabelField(
                rect,
                " ",
                string.IsNullOrEmpty(text) ? "(no text in the default language)" : $"“{text}”",
                EditorStyles.miniLabel);
        }

        private static void ShowPicker(
            Rect anchor,
            SerializedProperty property,
            List<string> keys,
            bool allowMissing,
            string current)
        {
            // The property is captured, so the selection has to be applied against a live
            // SerializedObject — the dropdown closes on a later frame than the one that opened it.
            var serializedObject = property.serializedObject;
            var path = property.propertyPath;

            var dropdown = new KeyDropdown(
                new AdvancedDropdownState(),
                keys,
                current,
                allowMissing,
                selected =>
                {
                    if (serializedObject == null || serializedObject.targetObject == null) return;

                    serializedObject.Update();

                    var live = serializedObject.FindProperty(path);
                    if (live == null) return;

                    live.stringValue = selected;
                    serializedObject.ApplyModifiedProperties();
                });

            dropdown.Show(anchor);
        }

        /// <summary>
        /// The picker itself: keys grouped by category, with Unity's built-in search over the tree.
        /// </summary>
        private sealed class KeyDropdown : AdvancedDropdown
        {
            private readonly List<string> m_Keys;
            private readonly string m_Current;
            private readonly bool m_AllowMissing;
            private readonly Action<string> m_OnSelected;
            private readonly Dictionary<int, string> m_ById = new Dictionary<int, string>();

            internal KeyDropdown(
                AdvancedDropdownState state,
                List<string> keys,
                string current,
                bool allowMissing,
                Action<string> onSelected)
                : base(state)
            {
                m_Keys = keys;
                m_Current = current;
                m_AllowMissing = allowMissing;
                m_OnSelected = onSelected;

                minimumSize = new Vector2(280f, 320f);
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                m_ById.Clear();

                var root = new AdvancedDropdownItem("Localization Keys");
                var nextId = 1;

                var clear = new AdvancedDropdownItem("(none)") { id = 0 };
                m_ById[0] = string.Empty;
                root.AddChild(clear);
                root.AddSeparator();

                // Rebuild the category tree so nested categories ("Popups/Quit") become real
                // sub-menus rather than one long flat name.
                var folders = new Dictionary<string, AdvancedDropdownItem>(StringComparer.OrdinalIgnoreCase);

                m_Keys.Sort(StringComparer.OrdinalIgnoreCase);

                foreach (var fullKey in m_Keys)
                {
                    var hasCategory = LocalizationKeys.TrySplit(fullKey, out var category, out var leaf);
                    var parent = hasCategory ? GetFolder(root, folders, category) : root;

                    var item = new AdvancedDropdownItem(leaf) { id = nextId };
                    m_ById[nextId] = fullKey;
                    nextId++;

                    parent.AddChild(item);
                }

                if (m_AllowMissing && !string.IsNullOrEmpty(m_Current) && !m_Keys.Contains(m_Current))
                {
                    root.AddSeparator();

                    var keep = new AdvancedDropdownItem($"{m_Current}  (not in catalog)") { id = nextId };
                    m_ById[nextId] = m_Current;
                    root.AddChild(keep);
                }

                return root;
            }

            private static AdvancedDropdownItem GetFolder(
                AdvancedDropdownItem root,
                Dictionary<string, AdvancedDropdownItem> folders,
                string path)
            {
                if (folders.TryGetValue(path, out var existing)) return existing;

                var slash = path.LastIndexOf(LocalizationKeys.Separator);
                var parent = slash > 0 ? GetFolder(root, folders, path.Substring(0, slash)) : root;
                var name = slash > 0 ? path.Substring(slash + 1) : path;

                var folder = new AdvancedDropdownItem(name);
                parent.AddChild(folder);
                folders[path] = folder;

                return folder;
            }

            protected override void ItemSelected(AdvancedDropdownItem item)
            {
                if (m_ById.TryGetValue(item.id, out var key))
                    m_OnSelected?.Invoke(key);
            }
        }
    }
}
