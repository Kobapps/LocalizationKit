using System;
using UnityEngine;

namespace LocalizationKit
{
    /// <summary>
    /// Builds a runtime table from data that never was a catalog asset — a downloaded sheet, a
    /// string baked into a build, a test fixture.
    /// </summary>
    /// <remarks>
    /// This is the whole of what a remote source needs. Fetch the bytes however you like, call
    /// <see cref="FromCsv"/>, hand the result to <see cref="Localization.SetTable"/>, and every
    /// <c>[Localized]</c> field and localized component updates itself. No calling code changes.
    /// <code>
    /// using var request = UnityWebRequest.Get(publishedSheetUrl);
    /// await request.SendWebRequest();
    ///
    /// var table = LocalizationTableBuilder.FromCsv(request.downloadHandler.text, defaultLanguage: "en");
    /// Localization.SetTable(table, Localization.LanguageCode);
    /// </code>
    /// It works by filling a transient catalog and building from that, rather than by constructing
    /// a table directly. That costs one throwaway object per load and keeps exactly one
    /// implementation of key composition, fallback and gap filling — two would drift.
    /// </remarks>
    public static class LocalizationTableBuilder
    {
        /// <summary>
        /// Parses CSV in the kit's shape (first column keys, one column per language code) and
        /// builds a table from it.
        /// </summary>
        /// <param name="csv">The document text.</param>
        /// <param name="defaultLanguage">
        /// Language used to fill gaps. Defaults to the first column when null or unknown.
        /// </param>
        /// <param name="missingBehavior">What a key with no text anywhere resolves to.</param>
        /// <param name="delimiter">Field separator. Tab for TSV.</param>
        /// <returns>A table, or an empty one when the document could not be read.</returns>
        public static LocalizationTable FromCsv(
            string csv,
            string defaultLanguage = null,
            MissingKeyBehavior missingBehavior = MissingKeyBehavior.ReturnKey,
            char delimiter = ',')
        {
            var catalog = CatalogFromCsv(csv, defaultLanguage, delimiter);

            return catalog == null
                ? LocalizationTable.Empty()
                : LocalizationTable.Build(catalog, missingBehavior);
        }

        /// <summary>
        /// Parses CSV into a catalog without touching the asset database. Useful when the caller
        /// wants to inspect or merge the result before building a table.
        /// </summary>
        /// <returns>A transient catalog, or null when the document could not be read.</returns>
        public static LocalizationCatalog CatalogFromCsv(string csv, string defaultLanguage = null, char delimiter = ',')
        {
            var snapshot = LocalizationSnapshot.FromCsv(csv, delimiter);
            if (snapshot == null) return null;

            if (!string.IsNullOrEmpty(defaultLanguage))
                snapshot.DefaultLanguageCode = defaultLanguage;

            if (snapshot.Warnings.Count > 0)
                Debug.LogWarning($"[LocalizationKit] Read the document with {snapshot.Warnings.Count} warning(s); first: {snapshot.Warnings[0]}");

            return snapshot.ToCatalog();
        }
    }
}
