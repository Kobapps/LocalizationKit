using System;
using EditorCoreKit.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LocalizationKit.Editor
{
    /// <summary>
    /// The two-way seam to wherever translations actually live: fetch, see what would change,
    /// merge, publish.
    /// </summary>
    /// <remarks>
    /// The order of the buttons is the argument. A fetch shows a report before anything is written
    /// to the catalog, because a merge is the one operation in the kit that can quietly lose work
    /// and a success toast is no way to find out that forty strings were overwritten.
    /// <para>
    /// The merge policy lives in the settings asset rather than in this window, so that a sync run
    /// on a build machine applies the same rules as one run from here.
    /// </para>
    /// </remarks>
    internal sealed class LocalizationRemotePage
    {
        private readonly LocalizationKitWindow m_Window;

        private LocalizationSnapshot m_Fetched;
        private LocalizationMergeReport m_Preview;
        private string m_Status;
        private KUITone m_StatusTone = KUITone.Neutral;
        private bool m_Busy;

        internal LocalizationRemotePage(LocalizationKitWindow window)
        {
            m_Window = window;
        }

        internal VisualElement Build()
        {
            var catalog = LocalizationEditorCatalog.Catalog;
            if (catalog == null) return m_Window.BuildNoCatalogState();

            var settings = LocalizationEditorCatalog.Settings;

            if (settings == null)
            {
                return KUILayout.Page(new KUIEmptyState(
                    "No settings asset",
                    "The provider is configured on the settings asset in Resources, which is also what the "
                    + "runtime loads. Create it to set a remote up.",
                    "Create Settings Asset",
                    () =>
                    {
                        LocalizationEditorCatalog.CreateSettings(catalog);
                        m_Window.Refresh();
                    },
                    "☁"));
            }

            return KUILayout.Page(
                BuildProviderCard(settings),
                BuildSyncCard(settings, catalog),
                BuildPublishCard(settings, catalog),
                BuildAutomationCard(settings));
        }

        // ---------------------------------------------------------------- cards

        private VisualElement BuildProviderCard(LocalizationSettings settings)
        {
            var card = new KUICard(
                "Provider",
                "Where translations live when they do not live in the catalog asset.");

            var field = new ObjectField("Provider")
            {
                objectType = typeof(LocalizationProviderAsset),
                allowSceneObjects = false,
                value = settings.RemoteProvider
            };

            field.RegisterValueChangedCallback(changed =>
            {
                Undo.RecordObject(settings, "Set Localization Provider");
                settings.RemoteProvider = changed.newValue as LocalizationProviderAsset;

                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssetIfDirty(settings);

                m_Fetched = null;
                m_Preview = null;
                m_Window.Refresh();
            });

            card.Add(field);

            var provider = settings.RemoteProvider;

            if (provider == null)
            {
                card.Add(KUILayout.Gap(6f));
                card.Add(KUIText.Body(
                    "A provider is a small ScriptableObject that knows how to fetch a snapshot and, if it "
                    + "can, write one back. Import the Google Sheets sample from the Package Manager for a "
                    + "working one, or derive from LocalizationProviderAsset — it is two methods."));

                return card;
            }

            var capabilities = provider.Capabilities;

            var badges = KUILayout.Row();
            badges.Add(new KUIBadge("Fetch", capabilities.HasFlag(LocalizationProviderCapabilities.Fetch)
                ? KUITone.Success
                : KUITone.Neutral));
            badges.Add(new KUIBadge("Upload", capabilities.HasFlag(LocalizationProviderCapabilities.Upload)
                ? KUITone.Success
                : KUITone.Neutral));

            card.Add(KUILayout.Gap(6f));
            card.Add(KUIText.KeyValue("Name", provider.DisplayName));
            card.Add(KUIText.KeyValue("Can", badges));

            if (capabilities == LocalizationProviderCapabilities.None)
            {
                card.Add(KUILayout.Gap(6f));
                card.Add(new KUIBanner(
                    KUITone.Warning,
                    "Not configured",
                    "The provider reports it can do neither direction yet — usually a URL it still needs."));
            }

            card.Add(KUILayout.Gap(6f));
            card.Add(KUIButton.Secondary("Select Provider Asset", () =>
            {
                EditorGUIUtility.PingObject(provider);
                Selection.activeObject = provider;
            }));

            return card;
        }

        private VisualElement BuildSyncCard(LocalizationSettings settings, LocalizationCatalog catalog)
        {
            var card = new KUICard("Fetch", "Read the remote, then decide what to keep.");
            var provider = settings.RemoteProvider;

            card.Add(BuildMergeOptions(settings));
            card.Add(KUILayout.Gap(6f));

            var row = KUILayout.Row();

            var fetch = KUIButton.Primary("Fetch & Preview", () => Fetch(settings, catalog));
            fetch.SetEnabled(!m_Busy && provider.CanFetch());
            row.Add(fetch);

            var apply = KUIButton.Success("Merge Into Catalog", () => Apply(settings, catalog));
            apply.SetEnabled(!m_Busy && m_Fetched != null);
            row.Add(apply);

            card.Add(row);

            if (!string.IsNullOrEmpty(m_Status))
            {
                card.Add(KUILayout.Gap(6f));
                card.Add(new KUIBanner(m_StatusTone, m_Status));
            }

            if (m_Fetched != null)
            {
                card.Add(KUILayout.Gap(6f));
                card.Add(KUIText.KeyValue("Fetched", $"{m_Fetched.RowCount} keys × {m_Fetched.LanguageCount} languages"));

                if (m_Preview != null)
                {
                    card.Add(KUIText.KeyValue("Would add", $"{m_Preview.AddedKeys} keys"));
                    card.Add(KUIText.KeyValue("Would write", $"{m_Preview.UpdatedValues} translations"));

                    if (m_Preview.RemovedKeys > 0)
                        card.Add(KUIText.KeyValue("Would remove", $"{m_Preview.RemovedKeys} keys"));

                    if (m_Preview.AddedLanguages.Count > 0)
                        card.Add(KUIText.KeyValue("Would add languages", string.Join(", ", m_Preview.AddedLanguages)));

                    if (m_Preview.IgnoredLanguages.Count > 0)
                        card.Add(KUIText.KeyValue("Ignored columns", string.Join(", ", m_Preview.IgnoredLanguages)));

                    if (!m_Preview.ChangedAnything)
                    {
                        card.Add(KUILayout.Gap(6f));
                        card.Add(new KUIBanner(KUITone.Success, "The catalog already matches the remote."));
                    }
                }

                if (m_Fetched.Warnings.Count > 0)
                {
                    card.Add(KUILayout.Gap(6f));
                    card.Add(new KUIBanner(
                        KUITone.Warning,
                        $"{m_Fetched.Warnings.Count} warning(s)",
                        m_Fetched.Warnings[0]));
                }
            }

            return card;
        }

        private VisualElement BuildPublishCard(LocalizationSettings settings, LocalizationCatalog catalog)
        {
            var card = new KUICard("Publish", "Send the catalog back, without flattening the remote.");
            var provider = settings.RemoteProvider;

            card.Add(KUIText.Body(
                "The remote is read first and the catalog merged over it, so a language column or a key "
                + "somebody added there survives being published from here."));

            card.Add(KUILayout.Gap(6f));

            var publish = KUIButton.Secondary("Publish Catalog To Remote", () => Publish(settings, catalog));
            publish.SetEnabled(!m_Busy && provider.CanUpload());
            card.Add(publish);

            if (!provider.CanUpload())
            {
                card.Add(KUILayout.Gap(6f));
                card.Add(KUIText.Muted(
                    "This provider reports no upload capability. Writing usually needs a credential the "
                    + "fetch does not — and one that has no business shipping inside a player."));
            }

            return card;
        }

        private VisualElement BuildAutomationCard(LocalizationSettings settings)
        {
            var card = new KUICard("Automation", "Machines that build this project, and players running it.");

            card.Add(new KUIToggleSwitch(
                "Sync remote before every build",
                settings.SyncRemoteBeforeBuild,
                value => Write(settings, () => settings.SyncRemoteBeforeBuild = value)));

            card.Add(new KUIToggleSwitch(
                "Fetch at runtime on startup",
                settings.FetchRemoteOnStartup,
                value => Write(settings, () => settings.FetchRemoteOnStartup = value)));

            card.Add(new KUIToggleSwitch(
                "Cache the last fetch on device",
                settings.UseRemoteCache,
                value => Write(settings, () => settings.UseRemoteCache = value)));

            card.Add(KUILayout.Gap(6f));
            card.Add(KUIText.Body(
                "The catalog asset is what ships inside a player, so a build machine has to pull before it "
                + "builds or it ships whatever the checkout happened to contain. A runtime fetch is a "
                + "different thing: it fixes text after release, not the first frame of a cold start."));

            card.Add(KUILayout.Gap(6f));
            card.Add(KUIText.Code(
                "Unity -batchmode -quit -projectPath . \\\n"
                + "  -executeMethod LocalizationKit.Editor.LocalizationRemoteSync.SyncFromRemote"));

            card.Add(KUILayout.Gap(6f));
            card.Add(KUIText.Muted(
                "Exits non-zero when the fetch fails, so a pipeline stops rather than quietly building "
                + "last week's text."));

            return card;
        }

        private VisualElement BuildMergeOptions(LocalizationSettings settings)
        {
            var box = KUILayout.Column();
            var options = settings.RemoteMergeOptions;

            box.Add(new KUIToggleSwitch("Add keys not in the catalog", options.AddNewKeys,
                value => WriteOption(settings, o => { o.AddNewKeys = value; return o; })));

            box.Add(new KUIToggleSwitch("Add languages not in the catalog", options.AddNewLanguages,
                value => WriteOption(settings, o => { o.AddNewLanguages = value; return o; })));

            box.Add(new KUIToggleSwitch("Overwrite existing text", options.OverwriteExisting,
                value => WriteOption(settings, o => { o.OverwriteExisting = value; return o; })));

            box.Add(new KUIToggleSwitch("Remove keys the remote does not have", options.RemoveKeysNotIncoming,
                value => WriteOption(settings, o => { o.RemoveKeysNotIncoming = value; return o; })));

            if (options.RemoveKeysNotIncoming)
            {
                box.Add(KUILayout.Gap(4f));
                box.Add(new KUIBanner(
                    KUITone.Warning,
                    "Deletions are on",
                    "A partial fetch will delete every key it did not carry. Worth it only when the remote "
                    + "is genuinely the source of truth."));
            }

            return box;
        }

        // ---------------------------------------------------------------- operations

        private void Fetch(LocalizationSettings settings, LocalizationCatalog catalog)
        {
            var provider = settings.RemoteProvider;
            if (provider == null) return;

            m_Busy = true;
            SetStatus($"Fetching from {provider.DisplayName}…", KUITone.Accent);

            // Rebuild before sending, or the buttons stay live for the length of the request and a
            // second click starts a second fetch.
            m_Window.Refresh();

            LocalizationRemote.Fetch(provider, result =>
            {
                m_Busy = false;

                if (!result.Success)
                {
                    m_Fetched = null;
                    m_Preview = null;

                    SetStatus(result.Error, KUITone.Error);
                    m_Window.Refresh();
                    return;
                }

                m_Fetched = result.Snapshot;
                m_Preview = LocalizationMerge.Preview(catalog, m_Fetched, settings.RemoteMergeOptions);

                SetStatus(
                    m_Preview.ChangedAnything
                        ? $"Fetched. {m_Preview.ShortSummary()} — nothing written yet."
                        : "Fetched. Nothing to change.",
                    KUITone.Success);

                m_Window.Refresh();
            });
        }

        private void Apply(LocalizationSettings settings, LocalizationCatalog catalog)
        {
            if (m_Fetched == null) return;

            var report = LocalizationMerge.Preview(catalog, m_Fetched, settings.RemoteMergeOptions);

            if (report.RemovedKeys > 0)
            {
                var proceed = EditorUtility.DisplayDialog(
                    "Delete keys?",
                    $"This merge removes {report.RemovedKeys} key(s) the remote did not carry, in every "
                    + "language.\n\nThis can be undone, but only until the editor is closed.",
                    "Merge",
                    "Cancel");

                if (!proceed) return;
            }

            LocalizationEditorCatalog.RecordUndo(catalog, "Merge Remote Localization");

            var applied = LocalizationMerge.Into(catalog, m_Fetched, settings.RemoteMergeOptions);

            LocalizationEditorCatalog.Save(catalog);

            m_Preview = null;
            m_Fetched = null;

            SetStatus($"Merged. {applied.ShortSummary()}.", KUITone.Success);

            m_Window.Refresh();
            EditorUtility.DisplayDialog("Merge complete", applied.Summary(), "OK");
            m_Window.Toast("Catalog updated from the remote.");
        }

        private void Publish(LocalizationSettings settings, LocalizationCatalog catalog)
        {
            var provider = settings.RemoteProvider;
            if (provider == null) return;

            var proceed = EditorUtility.DisplayDialog(
                "Publish to the remote?",
                $"{LocalizationStats.For(catalog).EntryCount} keys will be written to "
                + $"{provider.DisplayName}, on top of what is there now.",
                "Publish",
                "Cancel");

            if (!proceed) return;

            m_Busy = true;
            SetStatus($"Publishing to {provider.DisplayName}…", KUITone.Accent);
            m_Window.Refresh();

            LocalizationRemote.MergeAndUpload(
                provider,
                catalog,
                LocalizationMergeOptions.Default,
                result =>
                {
                    m_Busy = false;

                    SetStatus(
                        result.Success
                            ? result.RowsWritten > 0
                                ? $"Published {result.RowsWritten} rows."
                                : "Published."
                            : result.Error,
                        result.Success ? KUITone.Success : KUITone.Error);

                    m_Window.Refresh();

                    if (result.Success) m_Window.Toast("Published to the remote.");
                });
        }

        // ---------------------------------------------------------------- internals

        private void SetStatus(string message, KUITone tone)
        {
            m_Status = message;
            m_StatusTone = tone;
        }

        private void Write(LocalizationSettings settings, Action change)
        {
            Undo.RecordObject(settings, "Edit Localization Settings");
            change();

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);

            m_Window.Refresh();
        }

        private void WriteOption(LocalizationSettings settings, Func<LocalizationMergeOptions, LocalizationMergeOptions> change)
        {
            // The options are a struct on the asset, so a toggle has to read, change and write it
            // back rather than mutating what the getter handed over.
            Write(settings, () => settings.RemoteMergeOptions = change(settings.RemoteMergeOptions));
        }
    }
}
