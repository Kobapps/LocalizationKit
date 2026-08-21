using System;
using System.IO;
using UnityEngine;

namespace LocalizationKit
{
    /// <summary>
    /// Fetch, cache and apply — the part that turns a provider into localized text on screen.
    /// </summary>
    /// <remarks>
    /// A provider deliberately knows nothing about what happens to what it fetched. This is what
    /// happens to it: build a table, hand it to <see cref="Localization.SetTable"/>, and write a
    /// copy to disk so the next cold start is not at the mercy of the network.
    /// <para>
    /// <b>The cache is the point, not a nicety.</b> A game whose strings come from a remote has a
    /// first frame either way; without a cache that frame is either blank or in the wrong language
    /// on every launch that starts offline. So the order is: local catalog immediately, cache over
    /// it if there is one, remote over that when it arrives. Each step is a table swap, each swap
    /// refreshes everything bound, and a step that fails simply leaves the previous one standing.
    /// </para>
    /// <para>
    /// Nothing here throws. A remote refresh failing is an ordinary event, and the correct
    /// behaviour when it happens is that the game carries on in the language it already had.
    /// </para>
    /// </remarks>
    public static class LocalizationRemote
    {
        /// <summary>File the last successful fetch is kept in, under <c>Application.persistentDataPath</c>.</summary>
        public const string CacheFileName = "localizationkit-remote.csv";

        private static bool s_Fetching;

        /// <summary>Raised after a fetch has been applied, with the snapshot that was applied.</summary>
        public static event Action<LocalizationSnapshot> Fetched;

        /// <summary>Raised when a fetch fails, with the reason. The active table is left alone.</summary>
        public static event Action<string> FetchFailed;

        /// <summary>Where <see cref="WriteCache"/> writes and <see cref="TryLoadCache"/> reads.</summary>
        public static string CachePath => Path.Combine(Application.persistentDataPath, CacheFileName);

        /// <summary>True while a fetch started here is in flight.</summary>
        public static bool IsFetching => s_Fetching;

        /// <summary>When the last successful fetch completed, or <c>default</c> if none has.</summary>
        public static DateTime LastFetchUtc { get; private set; }

        /// <summary>
        /// The provider the project is configured with, from the settings asset. Null when there is
        /// none — which is the normal state for a project that ships its catalog.
        /// </summary>
        public static ILocalizationProvider Provider
        {
            get
            {
                var settings = LocalizationSettings.Load();
                return settings != null ? settings.RemoteProvider : null;
            }
        }

        // ---------------------------------------------------------------- fetching

        /// <summary>
        /// Fetches without applying anything, for a caller that wants to look before it leaps —
        /// the editor's merge preview, or a provider that reconciles two remotes.
        /// </summary>
        public static void Fetch(ILocalizationProvider provider, Action<LocalizationFetchResult> onCompleted)
        {
            if (provider == null)
            {
                onCompleted?.Invoke(LocalizationFetchResult.Failed("No provider."));
                return;
            }

            if (!provider.CanFetch())
            {
                onCompleted?.Invoke(LocalizationFetchResult.Failed($"{provider.DisplayName} is not configured to fetch."));
                return;
            }

            s_Fetching = true;

            provider.Fetch(result =>
            {
                s_Fetching = false;

                if (result.Success) LastFetchUtc = DateTime.UtcNow;

                onCompleted?.Invoke(result);
            });
        }

        /// <summary>
        /// Fetches and installs the result as the active table, keeping the language currently
        /// selected when the remote still carries it.
        /// </summary>
        /// <param name="provider">Defaults to the one in the settings asset.</param>
        /// <param name="onCompleted">Optional; receives the same result the provider produced.</param>
        /// <param name="cache">Whether a successful fetch is written to disk for the next launch.</param>
        public static void FetchAndApply(
            ILocalizationProvider provider = null,
            Action<LocalizationFetchResult> onCompleted = null,
            bool cache = true)
        {
            provider ??= Provider;

            Fetch(provider, result =>
            {
                if (!result.Success)
                {
                    Debug.LogWarning($"[LocalizationKit] Remote fetch failed: {result.Error}");
                    FetchFailed?.Invoke(result.Error);
                    onCompleted?.Invoke(result);
                    return;
                }

                if (result.Snapshot.IsEmpty)
                {
                    // An empty document is far more often a permissions page or a wrong sheet id
                    // than a genuinely empty catalog, and applying it would blank the game.
                    const string reason = "The remote returned no rows; the active table was left alone.";

                    Debug.LogWarning($"[LocalizationKit] {reason}");
                    FetchFailed?.Invoke(reason);
                    onCompleted?.Invoke(LocalizationFetchResult.Failed(reason));
                    return;
                }

                Apply(result.Snapshot);

                if (cache) WriteCache(result.Snapshot);

                onCompleted?.Invoke(result);
            });
        }

        /// <summary>
        /// Installs a snapshot as the active table. Keeps the active language when the snapshot has
        /// it, and falls back to the snapshot's default when it does not.
        /// </summary>
        public static void Apply(LocalizationSnapshot snapshot)
        {
            if (snapshot == null) return;

            var settings = LocalizationSettings.Load();
            var behavior = settings != null ? settings.MissingKeyBehavior : MissingKeyBehavior.ReturnKey;

            var keep = Localization.LanguageCode;
            var language = !string.IsNullOrEmpty(keep) && snapshot.IndexOfLanguage(keep) >= 0
                ? keep
                : snapshot.DefaultLanguageCode;

            Localization.SetTable(snapshot.ToTable(behavior, snapshot.DefaultLanguageCode), language);

            Fetched?.Invoke(snapshot);
        }

        // ---------------------------------------------------------------- cache

        /// <summary>
        /// Applies the cached snapshot from a previous session, if there is one. Returns false when
        /// there is nothing cached — which is not an error, only a first launch.
        /// </summary>
        public static bool ApplyCached()
        {
            if (!TryLoadCache(out var snapshot)) return false;

            Apply(snapshot);
            return true;
        }

        /// <summary>Reads the cached snapshot. Never throws; a corrupt cache reads as absent.</summary>
        public static bool TryLoadCache(out LocalizationSnapshot snapshot)
        {
            snapshot = null;

            try
            {
                var path = CachePath;
                if (!File.Exists(path)) return false;

                if (!LocalizationSnapshot.TryFromCsv(File.ReadAllText(path), out snapshot, out var error))
                {
                    Debug.LogWarning($"[LocalizationKit] Ignoring the localization cache: {error}");
                    return false;
                }

                snapshot.SourceName = "cache";
                return !snapshot.IsEmpty;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[LocalizationKit] Could not read the localization cache: {exception.Message}");
                return false;
            }
        }

        /// <summary>Writes a snapshot to the cache. Failure is logged and otherwise ignored.</summary>
        public static void WriteCache(LocalizationSnapshot snapshot)
        {
            if (snapshot == null || snapshot.IsEmpty) return;

            try
            {
                File.WriteAllText(CachePath, snapshot.ToCsv(), new System.Text.UTF8Encoding(true));
            }
            catch (Exception exception)
            {
                // A device with no writable storage is a device that fetches every launch, which
                // is worse but not fatal.
                Debug.LogWarning($"[LocalizationKit] Could not write the localization cache: {exception.Message}");
            }
        }

        /// <summary>Deletes the cache. For a "reset localization" action, and for tests.</summary>
        public static void ClearCache()
        {
            try
            {
                if (File.Exists(CachePath)) File.Delete(CachePath);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[LocalizationKit] Could not clear the localization cache: {exception.Message}");
            }
        }

        // ---------------------------------------------------------------- uploading

        /// <summary>
        /// Sends a snapshot to the remote, replacing what is there.
        /// </summary>
        /// <remarks>
        /// This is a publishing action, not a sync: whatever the remote had is overwritten. Fetch
        /// and merge first if the remote may have moved on — <see cref="LocalizationMerge"/> exists
        /// for exactly that, and the editor's Remote page does it in that order.
        /// </remarks>
        public static void Upload(
            ILocalizationProvider provider,
            LocalizationSnapshot snapshot,
            Action<LocalizationUploadResult> onCompleted)
        {
            if (provider == null)
            {
                onCompleted?.Invoke(LocalizationUploadResult.Failed("No provider."));
                return;
            }

            if (!provider.CanUpload())
            {
                onCompleted?.Invoke(LocalizationUploadResult.Failed($"{provider.DisplayName} cannot upload."));
                return;
            }

            if (snapshot == null || snapshot.IsEmpty)
            {
                onCompleted?.Invoke(LocalizationUploadResult.Failed("Nothing to upload."));
                return;
            }

            provider.Upload(snapshot, onCompleted);
        }

        /// <summary>Sends a catalog to the remote. The usual shape of an upload from the editor.</summary>
        public static void UploadCatalog(
            ILocalizationProvider provider,
            LocalizationCatalog catalog,
            Action<LocalizationUploadResult> onCompleted) =>
            Upload(provider, LocalizationSnapshot.FromCatalog(catalog), onCompleted);

        /// <summary>
        /// Fetches, merges the catalog over what the remote has, and uploads the result — so a
        /// column somebody added remotely survives a publish from the editor.
        /// </summary>
        public static void MergeAndUpload(
            ILocalizationProvider provider,
            LocalizationCatalog catalog,
            LocalizationMergeOptions options,
            Action<LocalizationUploadResult> onCompleted)
        {
            var local = LocalizationSnapshot.FromCatalog(catalog);

            Fetch(provider, result =>
            {
                // A remote that cannot be read is not proof it is empty, so a failed fetch stops
                // the publish rather than overwriting the sheet with local data alone.
                if (!result.Success)
                {
                    onCompleted?.Invoke(LocalizationUploadResult.Failed(
                        $"Could not read the remote before writing to it: {result.Error}"));
                    return;
                }

                var merged = LocalizationMerge.Merge(result.Snapshot, local, options);
                Upload(provider, merged, onCompleted);
            });
        }

        /// <summary>
        /// Drops all state. For tests, and for play mode with domain reload disabled.
        /// </summary>
        public static void Reset()
        {
            s_Fetching = false;
            LastFetchUtc = default;
            Fetched = null;
            FetchFailed = null;
        }
    }
}
