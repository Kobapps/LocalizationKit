using System;
using System.Collections.Generic;
using System.Text;

namespace LocalizationKit
{
    /// <summary>What a merge is allowed to do to the thing it is merging into.</summary>
    /// <remarks>
    /// The defaults are chosen so that the common case — someone sent back a translation pass —
    /// cannot destroy work. Nothing is ever deleted unless <see cref="RemoveKeysNotIncoming"/> is
    /// asked for explicitly, and a language the target does not carry is ignored rather than
    /// invented, because a typo in a column header should not silently add a language to a shipped
    /// catalog.
    /// </remarks>
    [Serializable]
    public struct LocalizationMergeOptions
    {
        /// <summary>Whether a key the target does not have is added rather than skipped.</summary>
        public bool AddNewKeys;

        /// <summary>Whether a language column the target does not have is added rather than ignored.</summary>
        public bool AddNewLanguages;

        /// <summary>
        /// Whether incoming text replaces text the target already has. With this off only blank
        /// cells are filled — the way to take a translation pass back without losing edits made
        /// since.
        /// </summary>
        public bool OverwriteExisting;

        /// <summary>
        /// Whether keys the incoming data does not carry are deleted from the target. Off by
        /// default and worth leaving off: a partial fetch would empty the catalog.
        /// </summary>
        public bool RemoveKeysNotIncoming;

        /// <summary>Add keys, fill and overwrite text, ignore unknown languages, delete nothing.</summary>
        public static LocalizationMergeOptions Default => new LocalizationMergeOptions
        {
            AddNewKeys = true,
            AddNewLanguages = false,
            OverwriteExisting = true
        };

        /// <summary>Fill blanks only. The safe way to accept anything from a source you do not control.</summary>
        public static LocalizationMergeOptions FillBlanks => new LocalizationMergeOptions
        {
            AddNewKeys = true,
            AddNewLanguages = false,
            OverwriteExisting = false
        };

        /// <summary>Make the target match the incoming data exactly, deletions included.</summary>
        public static LocalizationMergeOptions Mirror => new LocalizationMergeOptions
        {
            AddNewKeys = true,
            AddNewLanguages = true,
            OverwriteExisting = true,
            RemoveKeysNotIncoming = true
        };
    }

    /// <summary>What a merge did, or would do. Shown to whoever pressed the button.</summary>
    /// <remarks>
    /// A merge is the one operation in the kit that can quietly lose work, so it reports in
    /// numbers rather than a success toast. The same report backs
    /// <see cref="LocalizationMerge.Preview"/>, which is how a user can see the damage before
    /// agreeing to it.
    /// </remarks>
    public sealed class LocalizationMergeReport
    {
        /// <summary>Rows the incoming snapshot carried.</summary>
        public int RowsRead;

        /// <summary>Keys that did not exist in the target and were created.</summary>
        public int AddedKeys;

        /// <summary>Individual translations written — cells whose text actually changed.</summary>
        public int UpdatedValues;

        /// <summary>Keys skipped because the target did not have them and adding was off.</summary>
        public int SkippedKeys;

        /// <summary>Keys deleted because the incoming data did not carry them.</summary>
        public int RemovedKeys;

        /// <summary>Language codes added to the target.</summary>
        public List<string> AddedLanguages = new List<string>();

        /// <summary>Language columns ignored because the target has no such language.</summary>
        public List<string> IgnoredLanguages = new List<string>();

        /// <summary>Anything questionable, including warnings carried over from the source.</summary>
        public List<string> Warnings = new List<string>();

        /// <summary>True when applying this would change something.</summary>
        public bool ChangedAnything =>
            AddedKeys > 0 || UpdatedValues > 0 || RemovedKeys > 0 || AddedLanguages.Count > 0;

        /// <summary>A few lines fit for a dialog.</summary>
        public string Summary()
        {
            var text = new StringBuilder();

            text.AppendLine($"{RowsRead} rows read.");
            text.AppendLine($"{AddedKeys} keys added, {UpdatedValues} translations written.");

            if (RemovedKeys > 0)
                text.AppendLine($"{RemovedKeys} keys removed (not present in the incoming data).");

            if (SkippedKeys > 0)
                text.AppendLine($"{SkippedKeys} unknown keys skipped (\"add keys\" is off).");

            if (AddedLanguages.Count > 0)
                text.AppendLine($"Languages added: {string.Join(", ", AddedLanguages)}.");

            if (IgnoredLanguages.Count > 0)
                text.AppendLine($"Columns ignored (no such language): {string.Join(", ", IgnoredLanguages)}.");

            for (var i = 0; i < Warnings.Count; i++)
                text.AppendLine($"• {Warnings[i]}");

            return text.ToString();
        }

        /// <summary>One line, for a toast or a log.</summary>
        public string ShortSummary() =>
            $"{AddedKeys} added, {UpdatedValues} updated"
            + (RemovedKeys > 0 ? $", {RemovedKeys} removed" : string.Empty);
    }

    /// <summary>
    /// The one implementation of "these strings arrived, fold them into what we have".
    /// </summary>
    /// <remarks>
    /// Import from a file, a fetch from a remote provider and a merge of two snapshots are the same
    /// operation with different transports, so they share this code rather than each carrying their
    /// own idea of what overwriting means. Two implementations of a merge policy is two behaviours
    /// that drift, and the way you find out is a translator asking where their text went.
    /// </remarks>
    public static class LocalizationMerge
    {
        /// <summary>
        /// Folds a snapshot into a catalog, in place. The caller is responsible for recording undo
        /// and saving the asset — this touches the data and nothing else.
        /// </summary>
        public static LocalizationMergeReport Into(
            LocalizationCatalog target,
            LocalizationSnapshot incoming,
            LocalizationMergeOptions options)
        {
            var report = new LocalizationMergeReport();
            if (target == null || incoming == null) return report;

            report.RowsRead = incoming.RowCount;
            report.Warnings.AddRange(incoming.Warnings);

            var adoptDefault = target.Languages.Count == 0;

            // Resolve each incoming column to a catalog language once, rather than per row.
            var columnToLanguage = new int[incoming.LanguageCount];

            for (var c = 0; c < incoming.LanguageCount; c++)
            {
                var language = incoming.Languages[c];
                var index = target.IndexOfLanguage(language.Code);

                if (index < 0 && options.AddNewLanguages && LocalizationKeys.IsValidName(language.Code))
                {
                    index = target.AddLanguage(language);
                    report.AddedLanguages.Add(language.Code);
                }
                else if (index < 0)
                {
                    report.IgnoredLanguages.Add(language.Code);
                }

                columnToLanguage[c] = index;
            }

            if (adoptDefault && !string.IsNullOrEmpty(incoming.DefaultLanguageCode))
                target.DefaultLanguageCode = incoming.DefaultLanguageCode;

            for (var r = 0; r < incoming.RowCount; r++)
            {
                var row = incoming.Rows[r];
                if (string.IsNullOrEmpty(row.Key)) continue;

                var hasCategory = LocalizationKeys.TrySplit(row.Key, out var category, out var key);
                if (!hasCategory) category = LocalizationKeys.DefaultCategory;

                var fullKey = LocalizationKeys.Compose(category, key);
                var entry = target.FindByFullKey(fullKey);

                if (entry == null)
                {
                    if (!options.AddNewKeys)
                    {
                        report.SkippedKeys++;
                        continue;
                    }

                    entry = target.AddEntry(category, key);
                    report.AddedKeys++;
                }

                if (!string.IsNullOrEmpty(row.Description)
                    && (options.OverwriteExisting || string.IsNullOrEmpty(entry.Description)))
                {
                    entry.Description = row.Description;
                }

                for (var c = 0; c < columnToLanguage.Length; c++)
                {
                    var language = columnToLanguage[c];
                    if (language < 0) continue;

                    var text = row.GetValue(c);
                    if (string.IsNullOrEmpty(text)) continue;

                    if (!options.OverwriteExisting && !entry.IsMissing(language)) continue;
                    if (string.Equals(entry.GetValue(language), text, StringComparison.Ordinal)) continue;

                    entry.SetValue(language, text);
                    report.UpdatedValues++;
                }
            }

            if (options.RemoveKeysNotIncoming)
                report.RemovedKeys = RemoveAbsent(target, incoming);

            target.ResizeEntries();

            return report;
        }

        /// <summary>
        /// Reports what <see cref="Into"/> would do, without doing it.
        /// </summary>
        /// <remarks>
        /// Implemented by running the real merge against a throwaway copy of the catalog rather
        /// than by a separate "what would happen" routine, so the preview cannot disagree with the
        /// thing it is previewing.
        /// </remarks>
        public static LocalizationMergeReport Preview(
            LocalizationCatalog target,
            LocalizationSnapshot incoming,
            LocalizationMergeOptions options)
        {
            var copy = LocalizationSnapshot.FromCatalog(target).ToCatalog("Merge Preview");

            try
            {
                return Into(copy, incoming, options);
            }
            finally
            {
                LocalizationSnapshot.DestroyTransient(copy);
            }
        }

        /// <summary>
        /// Folds one snapshot into another and returns the result, leaving both inputs untouched.
        /// For providers that reconcile before writing anything down.
        /// </summary>
        public static LocalizationSnapshot Merge(
            LocalizationSnapshot baseline,
            LocalizationSnapshot incoming,
            LocalizationMergeOptions options,
            out LocalizationMergeReport report)
        {
            var working = (baseline ?? new LocalizationSnapshot()).ToCatalog("Merge Working Copy");

            try
            {
                report = Into(working, incoming, options);

                var merged = LocalizationSnapshot.FromCatalog(working);
                merged.SourceName = incoming != null ? incoming.SourceName : baseline?.SourceName;

                return merged;
            }
            finally
            {
                LocalizationSnapshot.DestroyTransient(working);
            }
        }

        /// <inheritdoc cref="Merge(LocalizationSnapshot, LocalizationSnapshot, LocalizationMergeOptions, out LocalizationMergeReport)"/>
        public static LocalizationSnapshot Merge(
            LocalizationSnapshot baseline,
            LocalizationSnapshot incoming,
            LocalizationMergeOptions options) =>
            Merge(baseline, incoming, options, out _);

        private static int RemoveAbsent(LocalizationCatalog target, LocalizationSnapshot incoming)
        {
            var doomed = new List<string>();
            var keys = target.GetAllKeys();

            for (var i = 0; i < keys.Count; i++)
            {
                if (incoming.Find(keys[i]) == null)
                    doomed.Add(keys[i]);
            }

            for (var i = 0; i < doomed.Count; i++)
            {
                var category = LocalizationKeys.TrySplit(doomed[i], out var categoryName, out var key)
                    ? categoryName
                    : LocalizationKeys.DefaultCategory;

                target.RemoveEntry(category, key);
            }

            return doomed.Count;
        }
    }
}
