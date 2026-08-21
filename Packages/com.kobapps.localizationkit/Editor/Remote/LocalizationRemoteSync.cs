using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LocalizationKit.Editor
{
    /// <summary>
    /// Pulling the remote into the catalog asset, and pushing the catalog back out — from a menu
    /// item, from a build, or from a command line on a machine with no one sitting at it.
    /// </summary>
    /// <remarks>
    /// The catalog asset is what ships inside a player, so a runtime fetch is not enough on its
    /// own: a build made on CI from a checkout that is a week old ships week-old text, and the
    /// runtime refresh does not help the first frame or a player who is offline. Something has to
    /// pull before the build starts, and that something has to work with no editor window open.
    /// <para>
    /// Everything here is therefore synchronous. <see cref="LocalizationWeb.Blocking"/> is turned
    /// on for the duration, which routes the request through <c>System.Net</c> instead of
    /// <c>UnityWebRequest</c> — in <c>-batchmode -executeMethod</c> there is no update loop to
    /// drive an asynchronous request, so waiting for one would wait forever.
    /// </para>
    /// <para>
    /// The configuration all lives in the settings asset — provider, merge policy, whether builds
    /// sync — rather than in <c>EditorPrefs</c>, so a build machine behaves the way the repository
    /// says it should rather than the way somebody's local editor happens to be set up.
    /// </para>
    /// </remarks>
    public static class LocalizationRemoteSync
    {
        /// <summary>Seconds a command-line or build-time sync waits before giving up.</summary>
        public const int SyncTimeoutSeconds = 60;

        /// <summary>
        /// Fetches the remote and merges it into the catalog, saving the asset. Blocks until done.
        /// </summary>
        /// <returns>True when the catalog is now current — including when nothing had changed.</returns>
        public static bool Pull(
            LocalizationCatalog catalog,
            ILocalizationProvider provider,
            LocalizationMergeOptions options,
            out LocalizationMergeReport report,
            out string error)
        {
            report = null;
            error = null;

            if (catalog == null)
            {
                error = "No catalog to merge into.";
                return false;
            }

            if (provider == null)
            {
                error = "No localization provider is configured.";
                return false;
            }

            if (!provider.CanFetch())
            {
                error = $"{provider.DisplayName} is not configured to fetch.";
                return false;
            }

            LocalizationFetchResult fetched = default;

            Blocking(() => provider.Fetch(result => fetched = result));

            if (!fetched.Success)
            {
                error = fetched.Error ?? "The fetch produced nothing.";
                return false;
            }

            if (fetched.Snapshot.IsEmpty)
            {
                // Far more often a permissions page or the wrong sheet id than a genuinely empty
                // remote — and merging it with RemoveKeysNotIncoming on would empty the catalog.
                error = "The remote returned no rows. Refusing to merge an empty document.";
                return false;
            }

            LocalizationEditorCatalog.RecordUndo(catalog, "Sync Localization From Remote");

            report = LocalizationMerge.Into(catalog, fetched.Snapshot, options);

            LocalizationEditorCatalog.Save(catalog);
            AssetDatabase.SaveAssets();

            return true;
        }

        /// <summary>Pulls using the project's settings asset. The form a build or a CLI run uses.</summary>
        public static bool Pull(out LocalizationMergeReport report, out string error)
        {
            report = null;

            var settings = LocalizationEditorCatalog.Settings;
            if (settings == null)
            {
                error = "No settings asset — nothing says which provider to use.";
                return false;
            }

            var catalog = settings.Catalog != null ? settings.Catalog : LocalizationEditorCatalog.Catalog;

            return Pull(catalog, settings.RemoteProvider, settings.RemoteMergeOptions, out report, out error);
        }

        /// <summary>
        /// Fetches, merges the catalog over what the remote has, and writes the result back —
        /// so a column somebody added remotely survives a publish from here. Blocks until done.
        /// </summary>
        public static bool Push(
            LocalizationCatalog catalog,
            ILocalizationProvider provider,
            out int rowsWritten,
            out string error)
        {
            rowsWritten = 0;
            error = null;

            if (catalog == null)
            {
                error = "No catalog to publish.";
                return false;
            }

            if (!provider.CanUpload())
            {
                error = provider == null
                    ? "No localization provider is configured."
                    : $"{provider.DisplayName} cannot upload.";

                return false;
            }

            LocalizationUploadResult uploaded = default;

            Blocking(() => LocalizationRemote.MergeAndUpload(
                provider,
                catalog,
                LocalizationMergeOptions.Default,
                result => uploaded = result));

            if (!uploaded.Success)
            {
                error = uploaded.Error ?? "The upload produced no answer.";
                return false;
            }

            rowsWritten = uploaded.RowsWritten;
            return true;
        }

        // ---------------------------------------------------------------- entry points

        /// <summary>Menu item: pull the remote into the catalog, and say what changed.</summary>
        [MenuItem("Tools/LocalizationKit/Sync From Remote", priority = 130)]
        public static void SyncFromRemoteMenu()
        {
            var settings = LocalizationEditorCatalog.Settings;

            if (settings == null || settings.RemoteProvider == null)
            {
                EditorUtility.DisplayDialog(
                    "No provider",
                    "Assign a localization provider in Project Settings ▸ LocalizationKit, or on the "
                    + "Remote page of the Localization Manager.",
                    "OK");

                return;
            }

            try
            {
                EditorUtility.DisplayProgressBar("LocalizationKit", $"Fetching from {settings.RemoteProvider.DisplayName}…", 0.5f);

                if (!Pull(out var report, out var error))
                {
                    EditorUtility.ClearProgressBar();
                    EditorUtility.DisplayDialog("Sync failed", error, "OK");
                    return;
                }

                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Sync complete", report.Summary(), "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem("Tools/LocalizationKit/Sync From Remote", validate = true)]
        private static bool SyncFromRemoteMenuEnabled() =>
            LocalizationEditorCatalog.Settings != null && LocalizationEditorCatalog.Settings.RemoteProvider != null;

        /// <summary>
        /// Command-line entry point for a build machine.
        /// </summary>
        /// <remarks>
        /// <code>
        /// Unity -batchmode -quit -projectPath . \
        ///       -executeMethod LocalizationKit.Editor.LocalizationRemoteSync.SyncFromRemote
        /// </code>
        /// Exits non-zero when the fetch fails, so a pipeline stops rather than quietly building
        /// last week's text. <c>-quit</c> is safe to combine with it: the failure path calls
        /// <c>EditorApplication.Exit</c> itself.
        /// </remarks>
        public static void SyncFromRemote()
        {
            if (!Pull(out var report, out var error))
            {
                Debug.LogError($"[LocalizationKit] Sync failed: {error}");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"[LocalizationKit] Sync complete. {report.ShortSummary()}.");
        }

        /// <summary>
        /// Runs the sync before a build, when the settings asset asks for it.
        /// </summary>
        /// <remarks>
        /// Ordered ahead of <see cref="LocalizationBuildValidator"/> — the validator asks whether
        /// the build would ship sound data, and it should be asked after the data has been brought
        /// up to date, not before.
        /// </remarks>
        internal sealed class BuildStep : IPreprocessBuildWithReport
        {
            public int callbackOrder => -2000;

            public void OnPreprocessBuild(BuildReport report)
            {
                var settings = LocalizationEditorCatalog.Settings;

                if (settings == null || !settings.SyncRemoteBeforeBuild) return;
                if (settings.RemoteProvider == null) return;

                Debug.Log($"[LocalizationKit] Syncing localization from {settings.RemoteProvider.DisplayName} before the build…");

                if (!Pull(out var merge, out var error))
                {
                    // Failing the build is the point of the setting. A build that silently ships
                    // stale strings is the outcome it exists to prevent.
                    throw new BuildFailedException(
                        $"[LocalizationKit] Could not sync localization before the build: {error}"
                        + "\n\nTurn off \"Sync remote before build\" in Project Settings ▸ LocalizationKit "
                        + "to build against the catalog as it stands.");
                }

                Debug.Log($"[LocalizationKit] Localization synced. {merge.ShortSummary()}.");
            }
        }

        // ---------------------------------------------------------------- internals

        /// <summary>
        /// Runs an action with web requests forced to complete inline, restoring the previous mode
        /// afterwards.
        /// </summary>
        private static void Blocking(Action action)
        {
            var previous = LocalizationWeb.Blocking;
            LocalizationWeb.Blocking = true;

            try
            {
                action();

                // Belt and braces: a provider that started an asynchronous request anyway — one
                // that does not go through LocalizationWeb, say — still gets a chance to land.
                if (LocalizationWeb.HasPendingRequests)
                    LocalizationWeb.WaitForPendingRequests(SyncTimeoutSeconds);
            }
            finally
            {
                LocalizationWeb.Blocking = previous;
            }
        }
    }
}
