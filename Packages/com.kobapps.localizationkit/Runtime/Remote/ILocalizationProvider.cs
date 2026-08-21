using System;

namespace LocalizationKit
{
    /// <summary>What a provider is able to do. Asked before a button is drawn, not after it is pressed.</summary>
    [Flags]
    public enum LocalizationProviderCapabilities
    {
        /// <summary>Neither direction — a provider that is not configured yet.</summary>
        None = 0,

        /// <summary>Can produce a snapshot from the remote.</summary>
        Fetch = 1,

        /// <summary>Can write a snapshot back to the remote.</summary>
        Upload = 1 << 1,

        /// <summary>Both directions.</summary>
        Both = Fetch | Upload
    }

    /// <summary>How a fetch ended.</summary>
    public readonly struct LocalizationFetchResult
    {
        /// <summary>True when <see cref="Snapshot"/> is usable.</summary>
        public readonly bool Success;

        /// <summary>What the remote had, or null on failure.</summary>
        public readonly LocalizationSnapshot Snapshot;

        /// <summary>Why it failed, when it did. Written for a human to read in a console.</summary>
        public readonly string Error;

        private LocalizationFetchResult(bool success, LocalizationSnapshot snapshot, string error)
        {
            Success = success;
            Snapshot = snapshot;
            Error = error;
        }

        /// <summary>A fetch that produced data.</summary>
        public static LocalizationFetchResult Ok(LocalizationSnapshot snapshot) =>
            new LocalizationFetchResult(true, snapshot, null);

        /// <summary>A fetch that did not.</summary>
        public static LocalizationFetchResult Failed(string error) =>
            new LocalizationFetchResult(false, null, error);
    }

    /// <summary>How an upload ended.</summary>
    public readonly struct LocalizationUploadResult
    {
        /// <summary>True when the remote accepted the write.</summary>
        public readonly bool Success;

        /// <summary>Rows the remote says it wrote, or 0 when it does not say.</summary>
        public readonly int RowsWritten;

        /// <summary>Why it failed, when it did.</summary>
        public readonly string Error;

        private LocalizationUploadResult(bool success, int rowsWritten, string error)
        {
            Success = success;
            RowsWritten = rowsWritten;
            Error = error;
        }

        /// <summary>An upload the remote accepted.</summary>
        public static LocalizationUploadResult Ok(int rowsWritten = 0) =>
            new LocalizationUploadResult(true, rowsWritten, null);

        /// <summary>An upload the remote refused, or that never arrived.</summary>
        public static LocalizationUploadResult Failed(string error) =>
            new LocalizationUploadResult(false, 0, error);
    }

    /// <summary>
    /// Somewhere translations live that is not this project: a spreadsheet, a CDN blob, a
    /// translation-management service, a live-ops endpoint.
    /// </summary>
    /// <remarks>
    /// The contract is two verbs and a snapshot. A provider fetches
    /// <see cref="LocalizationSnapshot"/>s and — if it can — accepts them back. It does not know
    /// what a catalog is, does not decide what happens to what it fetched, and never calls
    /// <see cref="Localization.SetTable"/> itself. Merging is
    /// <see cref="LocalizationMerge"/>'s job and applying is <see cref="LocalizationRemote"/>'s,
    /// which is what lets the same provider serve a runtime refresh, an editor sync and a test
    /// without behaving differently in each.
    /// <para>
    /// <b>Completion is a callback, not a <c>Task</c>.</b> A provider that touches Unity objects
    /// has to finish on the main thread, and a callback leaves that choice with the implementation
    /// instead of forcing a synchronisation context on it. <paramref name="onCompleted"/> is called
    /// exactly once, always — including on failure, including when the provider fails before doing
    /// any work at all. A provider that can return early must still call back rather than returning
    /// silently, or every caller ends up writing a timeout.
    /// </para>
    /// <para>
    /// Implementations are expected to be usable from the editor as well as from a player.
    /// <see cref="LocalizationWeb"/> handles that: it drives a request from
    /// <c>EditorApplication.update</c> outside play mode and from a hidden behaviour inside it, so
    /// one code path serves both.
    /// </para>
    /// </remarks>
    public interface ILocalizationProvider
    {
        /// <summary>Name for logs and the editor window. Not an identity.</summary>
        string DisplayName { get; }

        /// <summary>
        /// Which directions this provider supports, as configured right now. A provider missing its
        /// URL should report <see cref="LocalizationProviderCapabilities.None"/> rather than fail
        /// on use.
        /// </summary>
        LocalizationProviderCapabilities Capabilities { get; }

        /// <summary>
        /// Asks the remote for everything it has. <paramref name="onCompleted"/> runs exactly once.
        /// </summary>
        void Fetch(Action<LocalizationFetchResult> onCompleted);

        /// <summary>
        /// Writes a snapshot to the remote, replacing what is there.
        /// <paramref name="onCompleted"/> runs exactly once.
        /// </summary>
        /// <remarks>
        /// Providers that cannot write should report their capabilities honestly and fail here with
        /// a reason, rather than throwing: callers check capabilities to decide what to offer, but
        /// a stale UI is always possible.
        /// </remarks>
        void Upload(LocalizationSnapshot snapshot, Action<LocalizationUploadResult> onCompleted);
    }

    /// <summary>
    /// Convenience over <see cref="ILocalizationProvider"/>.
    /// </summary>
    public static class LocalizationProviderExtensions
    {
        /// <summary>True when this provider is configured well enough to fetch.</summary>
        public static bool CanFetch(this ILocalizationProvider provider) =>
            provider != null && (provider.Capabilities & LocalizationProviderCapabilities.Fetch) != 0;

        /// <summary>True when this provider is configured well enough to upload.</summary>
        public static bool CanUpload(this ILocalizationProvider provider) =>
            provider != null && (provider.Capabilities & LocalizationProviderCapabilities.Upload) != 0;
    }

    /// <summary>
    /// Presents a provider as an <see cref="ILocalizationSource"/>, so anything already written
    /// against the source interface can be pointed at a remote without changing.
    /// </summary>
    public sealed class LocalizationProviderSource : ILocalizationSource
    {
        private readonly ILocalizationProvider m_Provider;
        private readonly MissingKeyBehavior m_MissingBehavior;

        /// <inheritdoc />
        public string DisplayName => m_Provider != null ? m_Provider.DisplayName : "Remote (none)";

        public LocalizationProviderSource(
            ILocalizationProvider provider,
            MissingKeyBehavior missingBehavior = MissingKeyBehavior.ReturnKey)
        {
            m_Provider = provider;
            m_MissingBehavior = missingBehavior;
        }

        /// <inheritdoc />
        public void Load(Action<LocalizationTable> onCompleted, Action<string> onFailed)
        {
            if (m_Provider == null)
            {
                onFailed?.Invoke("No provider assigned.");
                onCompleted?.Invoke(null);
                return;
            }

            m_Provider.Fetch(result =>
            {
                if (!result.Success)
                {
                    onFailed?.Invoke(result.Error);
                    onCompleted?.Invoke(null);
                    return;
                }

                onCompleted?.Invoke(result.Snapshot.ToTable(m_MissingBehavior));
            });
        }
    }
}
