using System;

namespace LocalizationKit
{
    /// <summary>
    /// Somewhere a table can come from. A local catalog asset today; a Google Sheet, an CDN blob or
    /// a live-ops endpoint later.
    /// </summary>
    /// <remarks>
    /// The point of this interface is that <see cref="Localization"/> never learns where strings
    /// came from. A source produces a <see cref="LocalizationTable"/> and hands it to
    /// <see cref="Localization.SetTable"/>; everything already bound refreshes. Nothing in calling
    /// code changes when the origin does.
    /// <para>
    /// Loading is expressed as a callback rather than a <c>Task</c> deliberately: a remote source
    /// will want to run on Unity's main thread when it touches assets, and a callback keeps that
    /// choice with the implementation instead of forcing a synchronisation context on it.
    /// </para>
    /// </remarks>
    public interface ILocalizationSource
    {
        /// <summary>Name for logs and the editor window. Not an identity.</summary>
        string DisplayName { get; }

        /// <summary>
        /// Produces a table. <paramref name="onCompleted"/> is called exactly once, with null on
        /// failure and <paramref name="onFailed"/> carrying the reason.
        /// </summary>
        void Load(Action<LocalizationTable> onCompleted, Action<string> onFailed);
    }

    /// <summary>
    /// The shipped source: builds a table from a catalog asset already in the project.
    /// Synchronous, because the asset is loaded by the time anything asks.
    /// </summary>
    public sealed class LocalCatalogSource : ILocalizationSource
    {
        private readonly LocalizationCatalog m_Catalog;
        private readonly MissingKeyBehavior m_MissingBehavior;

        /// <inheritdoc />
        public string DisplayName => m_Catalog != null ? m_Catalog.name : "Local catalog (none)";

        public LocalCatalogSource(LocalizationCatalog catalog, MissingKeyBehavior missingBehavior = MissingKeyBehavior.ReturnKey)
        {
            m_Catalog = catalog;
            m_MissingBehavior = missingBehavior;
        }

        /// <inheritdoc />
        public void Load(Action<LocalizationTable> onCompleted, Action<string> onFailed)
        {
            if (m_Catalog == null)
            {
                onFailed?.Invoke("No catalog assigned.");
                onCompleted?.Invoke(null);
                return;
            }

            onCompleted?.Invoke(LocalizationTable.Build(m_Catalog, m_MissingBehavior));
        }
    }
}
