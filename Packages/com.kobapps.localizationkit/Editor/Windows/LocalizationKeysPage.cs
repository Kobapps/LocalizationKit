using System;
using System.Collections.Generic;
using EditorCoreKit.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LocalizationKit.Editor
{
    /// <summary>
    /// Browse, search, add and translate keys: categories on the left, keys in the middle, the
    /// selected key's text per language below.
    /// </summary>
    /// <remarks>
    /// The key list is a <see cref="KUIVirtualList{T}"/> because a shipped game's catalog runs to
    /// thousands of entries and building a row per key would make the window unusable at exactly
    /// the size where the window matters most.
    /// <para>
    /// Rows are recycled, so <see cref="BindRow"/> resets every field it can set. A custom row
    /// element is used rather than <c>KUIListRow</c> for the same reason: <c>WithBadge</c> appends,
    /// and appending during a rebind would grow badges without limit as the user scrolls.
    /// </para>
    /// </remarks>
    internal sealed class LocalizationKeysPage
    {
        private readonly LocalizationKitWindow m_Window;

        private readonly List<EntryRef> m_Visible = new List<EntryRef>();
        private string m_Category;              // null = all categories
        private string m_Search = string.Empty;
        private bool m_OnlyMissing;
        private string m_SelectedKey;
        private string m_PendingSelectKey;

        private KUIVirtualList<EntryRef> m_List;
        private VisualElement m_Detail;

        internal LocalizationKeysPage(LocalizationKitWindow window)
        {
            m_Window = window;
        }

        /// <summary>One entry, plus the category it came from — the pair every operation needs.</summary>
        private readonly struct EntryRef
        {
            internal readonly LocalizationCategory Category;
            internal readonly LocalizationEntry Entry;

            internal string FullKey => LocalizationKeys.Compose(Category.Name, Entry.Key);

            internal EntryRef(LocalizationCategory category, LocalizationEntry entry)
            {
                Category = category;
                Entry = entry;
            }
        }

        /// <summary>Selects a key, switching category filters if needed. Applied on the next build.</summary>
        internal void SelectKey(string fullKey)
        {
            m_PendingSelectKey = fullKey;
            m_Category = null;
            m_Search = string.Empty;
        }

        internal VisualElement Build()
        {
            var catalog = LocalizationEditorCatalog.Catalog;
            if (catalog == null) return m_Window.BuildNoCatalogState();

            if (catalog.Languages.Count == 0)
            {
                return KUILayout.Page(new KUIEmptyState(
                    "No languages yet",
                    "Keys need at least one language to hold text.",
                    "Add a Language",
                    () => m_Window.ShowPage(1),
                    "🌐"));
            }

            if (!string.IsNullOrEmpty(m_PendingSelectKey))
            {
                m_SelectedKey = m_PendingSelectKey;
                m_PendingSelectKey = null;
            }

            var split = new KUISplitView(200f, false, "LocalizationKit.KeysSplit");
            split.style.flexGrow = 1;

            split.First.Add(BuildCategoryList(catalog));
            split.Second.Add(BuildKeyPane(catalog));

            return split;
        }

        // ---------------------------------------------------------------- categories

        private VisualElement BuildCategoryList(LocalizationCatalog catalog)
        {
            var column = new VisualElement();
            column.style.flexGrow = 1;
            column.style.minHeight = 0;
            column.style.marginRight = 4f;

            var toolbar = new KUIToolbar();
            toolbar.With(KUIText.SectionTitle("Categories"));
            toolbar.PushRight();
            toolbar.With(KUIButton.Icon(KUIIcons.Plus, () => AddCategory(catalog), "New category"));
            column.Add(toolbar);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.style.minHeight = 0;

            scroll.Add(BuildCategoryRow(catalog, null, "All keys", LocalizationStats.For(catalog).EntryCount));

            for (var i = 0; i < catalog.Categories.Count; i++)
            {
                var category = catalog.Categories[i];
                scroll.Add(BuildCategoryRow(catalog, category, category.Name, category.Entries.Count));
            }

            column.Add(scroll);
            return column;
        }

        private VisualElement BuildCategoryRow(
            LocalizationCatalog catalog,
            LocalizationCategory category,
            string label,
            int count)
        {
            var name = category?.Name;

            var row = new KUIListRow(label, () =>
            {
                m_Category = name;
                m_Window.Refresh();
            });

            row.Selected = string.Equals(m_Category, name, StringComparison.OrdinalIgnoreCase);
            row.WithBadge(count.ToString());

            if (category != null)
            {
                row.WithAction(KUIDropdownButton.Overflow(menu =>
                {
                    menu.Item("Rename…", () => RenameCategory(catalog, category));
                    menu.Separator();
                    menu.Item("Delete", () => DeleteCategory(catalog, category));
                }));
            }

            return row;
        }

        // ---------------------------------------------------------------- keys

        private VisualElement BuildKeyPane(LocalizationCatalog catalog)
        {
            var column = new VisualElement();
            column.style.flexGrow = 1;
            column.style.minHeight = 0;

            var toolbar = new KUIToolbar();

            var search = new KUISearchField("Search keys and text…", value =>
            {
                m_Search = value ?? string.Empty;
                RefreshList(catalog);
            }, 0f);

            toolbar.With(search.Grow());
            toolbar.With(new KUIToggleSwitch("Only missing", m_OnlyMissing, value =>
            {
                m_OnlyMissing = value;
                RefreshList(catalog);
            }));
            toolbar.With(KUIButton.Secondary("+ Key", () => BeginNewKey()));

            column.Add(toolbar);

            RebuildVisible(catalog);

            m_List = new KUIVirtualList<EntryRef>(
                m_Visible,
                makeRow: () => new KeyRow(),
                bindRow: (element, item, index) => BindRow(catalog, (KeyRow)element, item, index),
                rowHeight: 34f);

            m_List.EmptyMessage = m_OnlyMissing
                ? "Every key in this view is translated."
                : "No keys match.";

            m_List.SelectionChanged += index =>
            {
                if ((uint)index >= (uint)m_Visible.Count) return;

                m_SelectedKey = m_Visible[index].FullKey;
                RebuildDetail(catalog);
            };

            m_List.style.flexGrow = 1;
            m_List.style.minHeight = 0;
            column.Add(m_List);

            m_Detail = new VisualElement();
            m_Detail.style.maxHeight = 300f;
            m_Detail.style.minHeight = 0;
            column.Add(m_Detail);

            RebuildDetail(catalog);

            return column;
        }

        /// <summary>
        /// A recycled row. Every visual it owns is created once and only ever reassigned, which is
        /// what makes it safe to rebind while scrolling.
        /// </summary>
        private sealed class KeyRow : VisualElement
        {
            internal readonly Label Key = new Label();
            internal readonly Label Preview = new Label();
            internal readonly KUIBadge Badge = new KUIBadge(string.Empty);

            internal KeyRow()
            {
                AddToClassList(KUIClass.ListItem);
                style.flexDirection = FlexDirection.Row;
                style.alignItems = Align.Center;

                var text = new VisualElement();
                text.style.flexGrow = 1;
                text.style.minWidth = 0;

                Key.AddToClassList(KUIClass.ListItemLabel);
                Preview.AddToClassList(KUIClass.ListItemSublabel);

                text.Add(Key);
                text.Add(Preview);

                Add(text);
                Add(Badge);
            }
        }

        private void BindRow(LocalizationCatalog catalog, KeyRow row, EntryRef item, int index)
        {
            var fullKey = item.FullKey;
            var defaultLanguage = catalog.IndexOfLanguage(catalog.DefaultLanguageCode);
            var preview = defaultLanguage >= 0 ? item.Entry.GetValue(defaultLanguage) : null;

            row.Key.text = fullKey;
            row.Preview.text = string.IsNullOrEmpty(preview) ? "—" : preview;

            var missing = CountMissing(catalog, item.Entry);
            row.Badge.text = missing == 0 ? "✓" : missing.ToString();
            row.Badge.Tone = missing == 0 ? KUITone.Success : KUITone.Warning;

            // Reset, not toggle: this element showed a different key a moment ago.
            row.EnableInClassList(KUIClass.ListItemOdd, index % 2 == 1);
            row.EnableInClassList(KUIClass.ListItemSelected, string.Equals(fullKey, m_SelectedKey, StringComparison.Ordinal));
        }

        private static int CountMissing(LocalizationCatalog catalog, LocalizationEntry entry)
        {
            var missing = 0;
            for (var i = 0; i < catalog.Languages.Count; i++)
                if (entry.IsMissing(i)) missing++;

            return missing;
        }

        private void RebuildVisible(LocalizationCatalog catalog)
        {
            m_Visible.Clear();

            var search = m_Search?.Trim();
            var hasSearch = !string.IsNullOrEmpty(search);

            for (var c = 0; c < catalog.Categories.Count; c++)
            {
                var category = catalog.Categories[c];

                if (m_Category != null && !string.Equals(category.Name, m_Category, StringComparison.OrdinalIgnoreCase))
                    continue;

                for (var e = 0; e < category.Entries.Count; e++)
                {
                    var entry = category.Entries[e];
                    if (entry == null || string.IsNullOrEmpty(entry.Key)) continue;

                    if (m_OnlyMissing && CountMissing(catalog, entry) == 0) continue;

                    if (hasSearch && !Matches(catalog, category, entry, search)) continue;

                    m_Visible.Add(new EntryRef(category, entry));
                }
            }
        }

        /// <summary>Search covers the key and every translation, so you can find a key by its English.</summary>
        private static bool Matches(
            LocalizationCatalog catalog,
            LocalizationCategory category,
            LocalizationEntry entry,
            string search)
        {
            var fullKey = LocalizationKeys.Compose(category.Name, entry.Key);
            if (fullKey.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) return true;

            for (var i = 0; i < catalog.Languages.Count; i++)
            {
                var value = entry.GetValue(i);
                if (!string.IsNullOrEmpty(value) && value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private void RefreshList(LocalizationCatalog catalog)
        {
            RebuildVisible(catalog);
            m_List?.SetItems(m_Visible);
        }

        // ---------------------------------------------------------------- detail

        private void RebuildDetail(LocalizationCatalog catalog)
        {
            if (m_Detail == null) return;

            m_Detail.Clear();

            var selected = FindSelected(catalog);
            if (selected == null)
            {
                m_Detail.Add(KUIText.Muted("Select a key to translate it."));
                return;
            }

            var reference = selected.Value;
            var entry = reference.Entry;

            var card = new KUICard(reference.FullKey, entry.Description);

            card.WithHeaderAction(KUIDropdownButton.Overflow(menu =>
            {
                menu.Item("Rename Key…", () => RenameEntry(catalog, reference));
                menu.Item("Move to Category…", () => MoveEntry(catalog, reference));
                menu.Item("Copy Key", () => EditorGUIUtility.systemCopyBuffer = reference.FullKey);
                menu.Separator();
                menu.Item("Delete Key", () => DeleteEntry(catalog, reference));
            }));

            var description = new TextField("Note for translators") { value = entry.Description ?? string.Empty };
            description.RegisterValueChangedCallback(e =>
            {
                LocalizationEditorCatalog.RecordUndo(catalog, "Edit Description");
                entry.Description = e.newValue;
                MarkDirty(catalog);
            });
            card.Add(description);

            card.Add(KUILayout.Separator());

            for (var i = 0; i < catalog.Languages.Count; i++)
            {
                var languageIndex = i;
                var language = catalog.Languages[i];

                var field = new TextField(language.DisplayName)
                {
                    value = entry.GetValue(languageIndex) ?? string.Empty,
                    multiline = true,
                };

                field.RegisterValueChangedCallback(e =>
                {
                    LocalizationEditorCatalog.RecordUndo(catalog, "Edit Translation");
                    entry.SetValue(languageIndex, e.newValue);
                    MarkDirty(catalog);

                    // The row's missing badge and preview are now stale.
                    m_List?.RefreshVisible();
                });

                var row = KUILayout.Row();
                row.Add(field.Grow());

                if (entry.IsMissing(languageIndex))
                    row.Add(new KUIBadge("missing", KUITone.Warning));

                card.Add(row);
            }

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.minHeight = 0;
            scroll.Add(card);

            m_Detail.Add(scroll);
        }

        private EntryRef? FindSelected(LocalizationCatalog catalog)
        {
            if (string.IsNullOrEmpty(m_SelectedKey)) return null;

            for (var c = 0; c < catalog.Categories.Count; c++)
            {
                var category = catalog.Categories[c];

                for (var e = 0; e < category.Entries.Count; e++)
                {
                    var entry = category.Entries[e];
                    if (entry == null) continue;

                    if (string.Equals(LocalizationKeys.Compose(category.Name, entry.Key), m_SelectedKey, StringComparison.Ordinal))
                        return new EntryRef(category, entry);
                }
            }

            return null;
        }

        // ---------------------------------------------------------------- operations

        /// <summary>Saves without rebuilding the page, so a text field keeps focus while typing.</summary>
        private void MarkDirty(LocalizationCatalog catalog)
        {
            EditorUtility.SetDirty(catalog);
        }

        internal void BeginNewKey()
        {
            var catalog = LocalizationEditorCatalog.Catalog;
            if (catalog == null) return;

            var startingCategory = m_Category ?? LocalizationKeys.DefaultCategory;

            LocalizationKeyDialog.Open(
                "New Key",
                startingCategory,
                string.Empty,
                CategoryNames(catalog),
                (category, key) =>
                {
                    if (!LocalizationKeys.IsValidName(key))
                    {
                        m_Window.Toast("A key cannot be blank or contain '/'.", KUITone.Error);
                        return;
                    }

                    var full = LocalizationKeys.Compose(category, key);
                    if (catalog.FindByFullKey(full) != null)
                    {
                        m_Window.Toast($"'{full}' already exists.", KUITone.Warning);
                        return;
                    }

                    LocalizationEditorCatalog.RecordUndo(catalog, "Add Key");
                    catalog.AddEntry(category, key);

                    m_SelectedKey = full;
                    m_Category = null;

                    m_Window.SaveCatalog();
                    m_Window.Toast($"Added {full}.");
                    m_Window.Refresh();
                });
        }

        private void RenameEntry(LocalizationCatalog catalog, EntryRef reference)
        {
            LocalizationKeyDialog.Open(
                "Rename Key",
                reference.Category.Name,
                reference.Entry.Key,
                CategoryNames(catalog),
                (category, key) =>
                {
                    if (!LocalizationKeys.IsValidName(key))
                    {
                        m_Window.Toast("A key cannot be blank or contain '/'.", KUITone.Error);
                        return;
                    }

                    var full = LocalizationKeys.Compose(category, key);
                    if (!string.Equals(full, reference.FullKey, StringComparison.Ordinal) && catalog.FindByFullKey(full) != null)
                    {
                        m_Window.Toast($"'{full}' already exists.", KUITone.Warning);
                        return;
                    }

                    LocalizationEditorCatalog.RecordUndo(catalog, "Rename Key");

                    // A rename is a move when the category changed: the entry object carries its
                    // translations, so it is relocated rather than copied.
                    if (!string.Equals(category, reference.Category.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        reference.Category.Entries.Remove(reference.Entry);
                        catalog.GetOrAddCategory(category).Entries.Add(reference.Entry);
                    }

                    reference.Entry.Key = key;
                    m_SelectedKey = full;

                    m_Window.SaveCatalog();
                    m_Window.Toast("Key renamed. Fields referencing the old key now point at nothing.", KUITone.Warning);
                    m_Window.Refresh();
                });
        }

        private void MoveEntry(LocalizationCatalog catalog, EntryRef reference)
        {
            var menu = KUIMenu.New();

            foreach (var name in CategoryNames(catalog))
            {
                var target = name;
                menu.Item(
                    target,
                    () =>
                    {
                        LocalizationEditorCatalog.RecordUndo(catalog, "Move Key");
                        reference.Category.Entries.Remove(reference.Entry);
                        catalog.GetOrAddCategory(target).Entries.Add(reference.Entry);

                        m_SelectedKey = LocalizationKeys.Compose(target, reference.Entry.Key);
                        m_Window.SaveCatalog();
                        m_Window.Refresh();
                    },
                    on: string.Equals(target, reference.Category.Name, StringComparison.OrdinalIgnoreCase));
            }

            menu.ShowAtCursor();
        }

        private void DeleteEntry(LocalizationCatalog catalog, EntryRef reference)
        {
            var confirmed = EditorUtility.DisplayDialog(
                $"Delete {reference.FullKey}?",
                "Every translation of this key goes with it, and any field pointing at it will stop resolving.",
                "Delete",
                "Cancel");

            if (!confirmed) return;

            LocalizationEditorCatalog.RecordUndo(catalog, "Delete Key");
            reference.Category.Entries.Remove(reference.Entry);

            if (string.Equals(m_SelectedKey, reference.FullKey, StringComparison.Ordinal))
                m_SelectedKey = null;

            m_Window.SaveCatalog();
            m_Window.Toast("Key deleted.", KUITone.Warning);
            m_Window.Refresh();
        }

        private void AddCategory(LocalizationCatalog catalog)
        {
            LocalizationTextDialog.Open("New Category", "Name", string.Empty, name =>
            {
                if (!LocalizationKeys.IsValidName(name))
                {
                    m_Window.Toast("A category name cannot be blank or contain '/'.", KUITone.Error);
                    return;
                }

                if (catalog.FindCategory(name) != null)
                {
                    m_Window.Toast($"'{name}' already exists.", KUITone.Warning);
                    return;
                }

                LocalizationEditorCatalog.RecordUndo(catalog, "Add Category");
                catalog.GetOrAddCategory(name);

                m_Category = name;
                m_Window.SaveCatalog();
                m_Window.Refresh();
            });
        }

        private void RenameCategory(LocalizationCatalog catalog, LocalizationCategory category)
        {
            LocalizationTextDialog.Open("Rename Category", "Name", category.Name, name =>
            {
                if (!LocalizationKeys.IsValidName(name))
                {
                    m_Window.Toast("A category name cannot be blank or contain '/'.", KUITone.Error);
                    return;
                }

                if (!string.Equals(name, category.Name, StringComparison.OrdinalIgnoreCase) &&
                    catalog.FindCategory(name) != null)
                {
                    m_Window.Toast($"'{name}' already exists.", KUITone.Warning);
                    return;
                }

                LocalizationEditorCatalog.RecordUndo(catalog, "Rename Category");
                category.Name = name;

                if (string.Equals(m_Category, category.Name, StringComparison.OrdinalIgnoreCase))
                    m_Category = name;

                m_Window.SaveCatalog();
                m_Window.Toast("Category renamed. Every key in it changed too.", KUITone.Warning);
                m_Window.Refresh();
            });
        }

        private void DeleteCategory(LocalizationCatalog catalog, LocalizationCategory category)
        {
            var confirmed = EditorUtility.DisplayDialog(
                $"Delete {category.Name}?",
                category.Entries.Count == 0
                    ? "The category is empty."
                    : $"This deletes {category.Entries.Count} key{(category.Entries.Count == 1 ? string.Empty : "s")} and every translation in them.",
                "Delete",
                "Cancel");

            if (!confirmed) return;

            LocalizationEditorCatalog.RecordUndo(catalog, "Delete Category");
            catalog.RemoveCategory(category.Name);

            if (string.Equals(m_Category, category.Name, StringComparison.OrdinalIgnoreCase))
                m_Category = null;

            m_Window.SaveCatalog();
            m_Window.Toast("Category deleted.", KUITone.Warning);
            m_Window.Refresh();
        }

        private static List<string> CategoryNames(LocalizationCatalog catalog)
        {
            var names = new List<string>(catalog.Categories.Count);
            for (var i = 0; i < catalog.Categories.Count; i++) names.Add(catalog.Categories[i].Name);

            if (names.Count == 0) names.Add(LocalizationKeys.DefaultCategory);

            return names;
        }
    }
}
