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
        private static readonly string[] RemoteFields =
        {
            "m_RemoteProvider",
            "m_FetchRemoteOnStartup",
            "m_UseRemoteCache",
            "m_SyncRemoteBeforeBuild",
            "m_RemoteMergeOptions"
        };

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

            // The remote fields get their own card below; drawing them twice would leave two
            // controls editing one value, which is a reliable way to make an edit look lost.
            root.Add(KUIProperty.InspectorCard(settings, "Runtime", RemoteFields));

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
            root.Add(BuildRemoteCard(settings));
            root.Add(BuildPlatformCard(settings));
        }

        /// <summary>
        /// Where translations come from when they do not come from the catalog asset.
        /// </summary>
        /// <remarks>
        /// Here rather than only in the editor window because a build machine reads exactly these
        /// fields, and a setting a build depends on belongs where a Unity user goes looking for it.
        /// </remarks>
        private static VisualElement BuildRemoteCard(LocalizationSettings settings)
        {
            var card = new KUICard(
                "Remote",
                "A provider fetches translations from wherever they actually live.");

            // One SerializedObject for the card: separate ones would each bind independently and
            // the last to be written would win.
            var serialized = new SerializedObject(settings);

            card.Add(KUIProperty.Field(serialized, "m_RemoteProvider"));

            if (settings.RemoteProvider == null)
            {
                card.Add(KUILayout.Gap(6f));
                card.Add(KUIText.Muted(
                    "None assigned — the catalog asset is the only source. Import the Google Sheets "
                    + "sample from the Package Manager for a working provider."));
            }
            else
            {
                card.Add(KUIProperty.Field(serialized, "m_SyncRemoteBeforeBuild"));
                card.Add(KUIProperty.Field(serialized, "m_FetchRemoteOnStartup"));
                card.Add(KUIProperty.Field(serialized, "m_UseRemoteCache"));
                card.Add(KUIProperty.Field(serialized, "m_RemoteMergeOptions"));

                card.Add(KUILayout.Gap(6f));
                card.Add(KUIText.Muted(
                    "The catalog asset is what ships inside a player, so a build machine has to pull "
                    + "before it builds. A runtime fetch fixes text after release, not the first "
                    + "frame of a cold start."));
            }

            card.Add(KUILayout.Gap(6f));
            card.Add(KUIButton.Secondary("Open Remote Page", () => LocalizationKitWindow.OpenRemote()));

            return card;
        }

        /// <summary>
        /// What a mobile build will carry, checked here rather than discovered on a device.
        /// </summary>
        private static VisualElement BuildPlatformCard(LocalizationSettings settings)
        {
            var card = new KUICard(
                "Platform builds",
                "Android and iOS are told which languages the app supports when it is packaged.");

            var problem = LocalizationBuildValidator.Check();

            card.Add(problem == null
                ? new KUIBanner(KUITone.Success, "A build would ship this catalog.")
                : new KUIBanner(KUITone.Error, "A build would ship unlocalized", problem));

            var catalog = settings.Catalog;

            if (catalog != null)
            {
                var codes = LocalizationBuildData.LanguageCodes(catalog);
                card.Add(KUIText.KeyValue("Declared to the OS", settings.DeclareLanguagesToOS
                    ? string.Join(", ", codes)
                    : "off"));

                var names = LocalizationBuildData.AppNames(settings, catalog);

                card.Add(KUIText.KeyValue(
                    "App name",
                    names.Count > 0
                        ? names[0].Name
                        : string.IsNullOrWhiteSpace(settings.AppNameKey)
                            ? "the product name — no key set"
                            : $"'{settings.AppNameKey}' has no text in the default language"));
            }

            card.Add(KUILayout.Gap(6f));
            card.Add(KUIButton.Secondary("Validate Build Setup", () =>
                EditorApplication.ExecuteMenuItem("Tools/LocalizationKit/Validate Build Setup")));

            return card;
        }
    }
}
