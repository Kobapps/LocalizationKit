using EditorCoreKit.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LocalizationKit.Editor
{
    /// <summary>
    /// <b>Project Settings ▸ LocalizationKit</b> — the runtime's configuration, in the place a
    /// Unity user looks for it.
    /// </summary>
    /// <remarks>
    /// The settings live in an asset under <c>Resources</c> rather than in <c>ProjectSettings/</c>,
    /// because the runtime has to load them in a build and <c>ProjectSettings/</c> does not ship.
    /// That makes this page a thin editor over a normal asset, which is why it offers to create the
    /// asset rather than assuming it exists.
    /// </remarks>
    internal static class LocalizationSettingsPage
    {
        [SettingsProvider]
        private static SettingsProvider Create() => KUISettingsPage.Create(
            "Project/LocalizationKit",
            SettingsScope.Project,
            "LocalizationKit",
            "How the runtime finds its catalog and picks a language.",
            Build,
            new[] { "localization", "language", "translation", "i18n", "l10n", "catalog" });

        private static void Build(VisualElement root)
        {
            var settings = LocalizationEditorCatalog.Settings;

            if (settings == null)
            {
                root.Add(new KUIBanner(
                        KUITone.Error,
                        "No settings asset",
                        "The runtime loads its configuration from Resources. Without this asset nothing is "
                        + "localized in a build, and there is no error at runtime to say why.")
                    .WithAction("Create Settings Asset", () =>
                    {
                        LocalizationEditorCatalog.CreateSettings(LocalizationEditorCatalog.Catalog);
                        SettingsService.NotifySettingsProviderChanged();
                    }));

                return;
            }

            root.Add(KUIProperty.InspectorCard(settings, "Runtime"));

            var actions = new KUICard("Catalog", AssetDatabase.GetAssetPath(settings));

            if (settings.Catalog == null)
            {
                actions.Add(new KUIBanner(KUITone.Warning, "No catalog assigned — every lookup returns its key."));
            }
            else
            {
                var stats = LocalizationStats.For(settings.Catalog);
                actions.Add(KUIText.KeyValue("Catalog", settings.Catalog.name));
                actions.Add(KUIText.KeyValue("Languages", stats.LanguageCount.ToString()));
                actions.Add(KUIText.KeyValue("Keys", stats.EntryCount.ToString()));
                actions.Add(KUIText.KeyValue("Translated", $"{stats.Coverage:P0}"));
            }

            actions.Add(KUILayout.Gap(6f));
            actions.Add(KUIButton.Primary("Open Localization Manager", () => LocalizationKitWindow.Open()));

            root.Add(actions);
        }
    }
}
