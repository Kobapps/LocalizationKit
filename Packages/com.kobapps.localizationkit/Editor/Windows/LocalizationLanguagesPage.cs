using System;
using System.Collections.Generic;
using EditorCoreKit.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LocalizationKit.Editor
{
    /// <summary>
    /// Add, remove, reorder and describe the languages a catalog carries.
    /// </summary>
    /// <remarks>
    /// Every operation here goes through the catalog's own editing methods, never through the
    /// serialized lists directly. A language column is positional across every entry, so adding one
    /// means widening thousands of arrays in step — doing it by hand is how translations end up
    /// shifted by one language, silently, everywhere.
    /// </remarks>
    internal sealed class LocalizationLanguagesPage
    {
        private readonly LocalizationKitWindow m_Window;

        internal LocalizationLanguagesPage(LocalizationKitWindow window)
        {
            m_Window = window;
        }

        internal VisualElement Build()
        {
            var catalog = LocalizationEditorCatalog.Catalog;
            if (catalog == null) return m_Window.BuildNoCatalogState();

            var page = KUILayout.Page();

            var card = new KUICard("Languages", "The first column of every entry follows this order.");

            if (catalog.Languages.Count == 0)
            {
                card.Add(new KUIEmptyState(
                    "No languages",
                    "Add the language your team authors in first; it becomes the fallback for the rest.",
                    "Add Language…",
                    () => AddLanguage(catalog),
                    "🌐"));
            }
            else
            {
                var missing = LocalizationStats.MissingPerLanguage(catalog);
                var entries = LocalizationStats.For(catalog).EntryCount;

                // A snapshot: the reorderable list mutates the list it is given, and the catalog's
                // is behind a read-only view precisely so reordering goes through MoveLanguage.
                var snapshot = new List<LanguageInfo>(catalog.Languages);

                var list = new KUIReorderableList<LanguageInfo>(
                    snapshot,
                    (language, index) => BuildRow(catalog, language, index, missing, entries),
                    title: null,
                    onChanged: () => ApplyOrder(catalog, snapshot));

                list.EmptyMessage = "No languages yet.";
                card.Add(list);

                // Not KUIReorderableList.AddButton: that appends whatever its factory returns, and
                // a new language has to go through AddLanguage so every entry gains a column.
                card.Add(KUILayout.Gap(6f));
                card.Add(KUIButton.Secondary("+ Add Language", () => AddLanguage(catalog)));
            }

            page.Add(card);
            page.Add(BuildDefaultCard(catalog));

            return page;
        }

        private VisualElement BuildRow(
            LocalizationCatalog catalog,
            LanguageInfo language,
            int index,
            int[] missing,
            int entries)
        {
            var isDefault = string.Equals(language.Code, catalog.DefaultLanguageCode, StringComparison.OrdinalIgnoreCase);
            var gaps = index < missing.Length ? missing[index] : 0;

            var row = new KUIListRow(language.DisplayName)
                .WithSublabel(language.Code + (language.RightToLeft ? "  ·  right-to-left" : string.Empty));

            if (isDefault) row.WithBadge("default", KUITone.Accent);

            row.WithBadge(
                gaps == 0 ? "complete" : $"{gaps} missing",
                gaps == 0 ? KUITone.Success : KUITone.Warning);

            row.WithAction(KUIDropdownButton.Overflow(menu =>
            {
                menu.Item("Edit…", () => EditLanguage(catalog, index));
                menu.Item("Set as Default", () => SetDefault(catalog, language.Code), enabled: !isDefault, on: isDefault);
                menu.Separator();
                menu.Item("Remove", () => RemoveLanguage(catalog, language));
            }));

            return row;
        }

        private VisualElement BuildDefaultCard(LocalizationCatalog catalog)
        {
            var card = new KUICard(
                "Fallback",
                "Text missing in one language falls back to the default before it falls back to the key.");

            if (catalog.Languages.Count == 0)
            {
                card.Add(KUIText.Muted("Add a language first."));
                return card;
            }

            var codes = new List<string>(catalog.Languages.Count);
            for (var i = 0; i < catalog.Languages.Count; i++) codes.Add(catalog.Languages[i].Code);

            var current = Mathf.Max(0, codes.IndexOf(catalog.DefaultLanguageCode));

            card.Add(KUIText.KeyValue("Default language", KUIDropdownButton.Create(
                codes[current],
                menu =>
                {
                    for (var i = 0; i < codes.Count; i++)
                    {
                        var code = codes[i];
                        menu.Item(code, () => SetDefault(catalog, code), on: i == current);
                    }
                })));

            return card;
        }

        // ---------------------------------------------------------------- operations

        private void ApplyOrder(LocalizationCatalog catalog, List<LanguageInfo> reordered)
        {
            // The list hands back the new order; turn it into the sequence of moves the catalog
            // needs so each one carries its column of text with it.
            LocalizationEditorCatalog.RecordUndo(catalog, "Reorder Languages");

            for (var target = 0; target < reordered.Count; target++)
            {
                var from = catalog.IndexOfLanguage(reordered[target].Code);
                if (from < 0 || from == target) continue;

                catalog.MoveLanguage(from, target);
            }

            m_Window.SaveCatalog();
            m_Window.Refresh();
        }

        private void AddLanguage(LocalizationCatalog catalog)
        {
            LocalizationLanguageDialog.Open(default, true, language =>
            {
                if (!LocalizationKeys.IsValidName(language.Code))
                {
                    m_Window.Toast("A language code cannot be blank or contain '/'.", KUITone.Error);
                    return;
                }

                if (catalog.IndexOfLanguage(language.Code) >= 0)
                {
                    m_Window.Toast($"'{language.Code}' is already in the catalog.", KUITone.Warning);
                    return;
                }

                LocalizationEditorCatalog.RecordUndo(catalog, "Add Language");
                catalog.AddLanguage(language);

                if (string.IsNullOrEmpty(catalog.DefaultLanguageCode))
                    catalog.DefaultLanguageCode = language.Code;

                m_Window.SaveCatalog();
                m_Window.Toast($"Added {language.DisplayName}.");
                m_Window.Refresh();
            });
        }

        private void EditLanguage(LocalizationCatalog catalog, int index)
        {
            if ((uint)index >= (uint)catalog.Languages.Count) return;

            var original = catalog.Languages[index];

            LocalizationLanguageDialog.Open(original, false, edited =>
            {
                LocalizationEditorCatalog.RecordUndo(catalog, "Edit Language");

                // The code is the identity every saved preference and every SetLanguage call uses,
                // so it is fixed once created — the dialog disables the field to say so.
                edited.Code = original.Code;
                catalog.SetLanguage(index, edited);

                m_Window.SaveCatalog();
                m_Window.Refresh();
            });
        }

        private void SetDefault(LocalizationCatalog catalog, string code)
        {
            LocalizationEditorCatalog.RecordUndo(catalog, "Set Default Language");
            catalog.DefaultLanguageCode = code;

            m_Window.SaveCatalog();
            m_Window.Refresh();
        }

        private void RemoveLanguage(LocalizationCatalog catalog, LanguageInfo language)
        {
            var stats = LocalizationStats.For(catalog);
            var index = catalog.IndexOfLanguage(language.Code);
            var missing = LocalizationStats.MissingPerLanguage(catalog);
            var written = index >= 0 && index < missing.Length ? stats.EntryCount - missing[index] : 0;

            // Deleting a language deletes every translation in it. That is not recoverable from the
            // asset, so say how much is about to go.
            var confirmed = EditorUtility.DisplayDialog(
                $"Remove {language.DisplayName}?",
                written > 0
                    ? $"This deletes {written} translated string{(written == 1 ? string.Empty : "s")}. This cannot be undone from the asset."
                    : "This language has no text in it.",
                "Remove",
                "Cancel");

            if (!confirmed) return;

            LocalizationEditorCatalog.RecordUndo(catalog, "Remove Language");
            catalog.RemoveLanguage(language.Code);

            m_Window.SaveCatalog();
            m_Window.Toast($"Removed {language.DisplayName}.", KUITone.Warning);
            m_Window.Refresh();
        }
    }
}
