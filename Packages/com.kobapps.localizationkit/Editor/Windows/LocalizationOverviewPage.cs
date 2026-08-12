using EditorCoreKit.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LocalizationKit.Editor
{
    /// <summary>
    /// What state the project's localization is in, and what to do about it.
    /// </summary>
    /// <remarks>
    /// Every problem that makes localization silently not work at runtime is checked here, because
    /// each one produces no error on its own: no settings asset, a settings asset pointing at
    /// nothing, a catalog with no languages, duplicate keys.
    /// </remarks>
    internal sealed class LocalizationOverviewPage
    {
        private readonly LocalizationKitWindow m_Window;

        internal LocalizationOverviewPage(LocalizationKitWindow window)
        {
            m_Window = window;
        }

        internal VisualElement Build()
        {
            var catalog = LocalizationEditorCatalog.Catalog;
            if (catalog == null) return m_Window.BuildNoCatalogState();

            var page = KUILayout.Page();

            foreach (var problem in BuildProblems(catalog))
                page.Add(problem);

            page.Add(BuildSummary(catalog));
            page.Add(BuildCoverage(catalog));
            page.Add(BuildCategories(catalog));

            return page;
        }

        private System.Collections.Generic.List<VisualElement> BuildProblems(LocalizationCatalog catalog)
        {
            var problems = new System.Collections.Generic.List<VisualElement>();
            var settings = LocalizationEditorCatalog.Settings;

            if (settings == null)
            {
                problems.Add(new KUIBanner(
                        KUITone.Error,
                        "Nothing will be localized at runtime",
                        "The runtime finds its catalog through a settings asset in Resources. This project has none, "
                        + "so every lookup returns its key.")
                    .WithAction("Create Settings", () =>
                    {
                        LocalizationEditorCatalog.CreateSettings(catalog);
                        m_Window.Toast("Settings asset created.");
                        m_Window.Refresh();
                    }));
            }
            else if (settings.Catalog == null)
            {
                problems.Add(new KUIBanner(
                        KUITone.Error,
                        "The settings asset has no catalog",
                        "Assign this catalog to the settings asset, or nothing resolves at runtime.")
                    .WithAction("Assign This Catalog", () =>
                    {
                        Undo.RecordObject(settings, "Assign Localization Catalog");
                        settings.Catalog = catalog;
                        EditorUtility.SetDirty(settings);
                        AssetDatabase.SaveAssetIfDirty(settings);
                        m_Window.Toast("Catalog assigned.");
                        m_Window.Refresh();
                    }));
            }

            if (catalog.Languages.Count == 0)
            {
                problems.Add(new KUIBanner(
                        KUITone.Warning,
                        "No languages",
                        "A catalog with no languages has nowhere to put text.")
                    .WithAction("Add a Language", () => m_Window.ShowPage(1)));
            }

            var duplicates = LocalizationStats.FindDuplicateKeys(catalog);
            if (duplicates.Count > 0)
            {
                var sample = string.Join(", ", duplicates.GetRange(0, Mathf.Min(3, duplicates.Count)));
                problems.Add(new KUIBanner(
                    KUITone.Warning,
                    $"{duplicates.Count} duplicate key{(duplicates.Count == 1 ? string.Empty : "s")}",
                    $"Which entry wins is arbitrary. Affected: {sample}{(duplicates.Count > 3 ? "…" : string.Empty)}"));
            }

            return problems;
        }

        private VisualElement BuildSummary(LocalizationCatalog catalog)
        {
            var stats = LocalizationStats.For(catalog);

            var card = new KUICard("Catalog", AssetDatabase.GetAssetPath(catalog));
            card.Add(KUIText.KeyValue("Languages", stats.LanguageCount.ToString()));
            card.Add(KUIText.KeyValue("Categories", stats.CategoryCount.ToString()));
            card.Add(KUIText.KeyValue("Keys", stats.EntryCount.ToString()));
            card.Add(KUIText.KeyValue("Default language", string.IsNullOrEmpty(catalog.DefaultLanguageCode)
                ? "—"
                : catalog.DefaultLanguageCode));

            card.Add(KUILayout.Gap(6f));
            card.Add(KUIButton.Secondary("Select Catalog Asset", () =>
            {
                Selection.activeObject = catalog;
                EditorGUIUtility.PingObject(catalog);
            }));

            return card;
        }

        private VisualElement BuildCoverage(LocalizationCatalog catalog)
        {
            var card = new KUICard("Coverage", "How much of each language is written.");

            if (catalog.Languages.Count == 0)
            {
                card.Add(KUIText.Muted("No languages yet."));
                return card;
            }

            var stats = LocalizationStats.For(catalog);
            var missing = LocalizationStats.MissingPerLanguage(catalog);

            for (var i = 0; i < catalog.Languages.Count; i++)
            {
                var language = catalog.Languages[i];
                var done = stats.EntryCount - missing[i];
                var ratio = stats.EntryCount == 0 ? 1f : (float)done / stats.EntryCount;

                var bar = new KUIProgressBar(showPercentage: false);
                bar.Progress = ratio;
                bar.Tone = missing[i] == 0 ? KUITone.Success : ratio > 0.9f ? KUITone.Accent : KUITone.Warning;
                bar.style.flexGrow = 1;

                var row = KUILayout.Row();
                row.Add(bar);
                row.Add(new KUIBadge(
                    $"{done}/{stats.EntryCount}",
                    missing[i] == 0 ? KUITone.Success : ratio > 0.9f ? KUITone.Neutral : KUITone.Warning));

                var label = language.DisplayName;
                if (string.Equals(language.Code, catalog.DefaultLanguageCode, System.StringComparison.OrdinalIgnoreCase))
                    label += "  (default)";

                card.Add(KUIText.KeyValue(label, row));
            }

            return card;
        }

        private VisualElement BuildCategories(LocalizationCatalog catalog)
        {
            var card = new KUICard("Categories", "Keys are grouped by category; the group is part of the key.");

            if (catalog.Categories.Count == 0)
            {
                card.Add(KUIText.Muted("No categories yet."));
                return card;
            }

            for (var i = 0; i < catalog.Categories.Count; i++)
            {
                var category = catalog.Categories[i];
                card.Add(KUIText.KeyValue(
                    category.Name,
                    $"{category.Entries.Count} key{(category.Entries.Count == 1 ? string.Empty : "s")}"));
            }

            card.Add(KUILayout.Gap(6f));

            var actions = KUILayout.Row();
            actions.Add(KUIButton.Secondary("Manage Keys", () => m_Window.ShowPage(2)));
            actions.Add(KUIButton.Secondary("Generate Key Constants…", LocalizationKeyConstants.GenerateInteractive)
                .Tip("Writes a LocKeys class so keys are code, not magic strings — a renamed entry then breaks the build instead of a label."));
            card.Add(actions);

            return card;
        }
    }
}
