using System;
using System.Collections.Generic;
using System.Text;

namespace LocalizationKit
{
    /// <summary>
    /// Reads and writes the tabular form translators actually work in: one row per key, one column
    /// per language.
    /// </summary>
    /// <remarks>
    /// This lives in the runtime assembly rather than the editor one on purpose. It is what a
    /// remote source will parse when the catalog starts coming from a published Google Sheet —
    /// <c>File ▸ Share ▸ Publish to web ▸ Comma-separated values</c> returns exactly this shape —
    /// and a runtime source cannot reference editor code.
    /// <para>
    /// The parser is RFC 4180: quoted fields may contain commas, newlines and doubled quotes.
    /// Hand-rolled rather than split-on-comma because a translation containing a comma is not an
    /// edge case, it is Tuesday.
    /// </para>
    /// Expected layout — the first column is the key, every further column is a language code:
    /// <code>
    /// Key,en,fr,he
    /// Store/BuyButton,Buy,Acheter,קנה
    /// Popups/Quit/Title,"Quit, really?","Quitter, vraiment ?",...
    /// </code>
    /// </remarks>
    public static class LocalizationCsv
    {
        /// <summary>One parsed row: a full key and its text per language column.</summary>
        public struct Row
        {
            /// <summary>The full <c>Category/Key</c> from the first column.</summary>
            public string Key;

            /// <summary>Text per language, positional against <see cref="ParseResult.LanguageCodes"/>.</summary>
            public string[] Values;
        }

        /// <summary>What a parse produced, plus anything questionable found along the way.</summary>
        public sealed class ParseResult
        {
            /// <summary>Language codes taken from the header row, after the key column.</summary>
            public string[] LanguageCodes = Array.Empty<string>();

            /// <summary>Every data row, in file order.</summary>
            public List<Row> Rows = new List<Row>();

            /// <summary>Non-fatal problems: blank keys, short rows, duplicates.</summary>
            public List<string> Warnings = new List<string>();

            /// <summary>True when the text could not be read as a table at all.</summary>
            public bool Failed;

            /// <summary>Why it failed, when it did.</summary>
            public string Error;
        }

        /// <summary>
        /// Parses CSV text into rows. Never throws: a malformed document comes back with
        /// <see cref="ParseResult.Failed"/> set, because this runs against files people edited by
        /// hand and a stack trace helps nobody.
        /// </summary>
        public static ParseResult Parse(string text, char delimiter = ',')
        {
            var result = new ParseResult();

            if (string.IsNullOrWhiteSpace(text))
            {
                result.Failed = true;
                result.Error = "The document is empty.";
                return result;
            }

            var grid = ReadGrid(text, delimiter);
            if (grid.Count == 0)
            {
                result.Failed = true;
                result.Error = "No rows found.";
                return result;
            }

            var header = grid[0];
            if (header.Count < 2)
            {
                result.Failed = true;
                result.Error = "The header needs a key column and at least one language column.";
                return result;
            }

            var codes = new string[header.Count - 1];
            for (var i = 1; i < header.Count; i++)
                codes[i - 1] = header[i].Trim();

            result.LanguageCodes = codes;

            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (var r = 1; r < grid.Count; r++)
            {
                var row = grid[r];
                if (row.Count == 0) continue;

                var key = row[0].Trim();
                if (key.Length == 0)
                {
                    // A wholly blank line is spacing, not an error worth reporting.
                    if (row.Count == 1 || IsAllBlank(row))
                        continue;

                    result.Warnings.Add($"Row {r + 1} has text but no key; skipped.");
                    continue;
                }

                if (!seen.Add(key))
                {
                    result.Warnings.Add($"Row {r + 1} repeats key '{key}'; the later row wins.");
                    result.Rows.RemoveAll(existing => string.Equals(existing.Key, key, StringComparison.Ordinal));
                }

                var values = new string[codes.Length];
                for (var c = 0; c < codes.Length; c++)
                {
                    var column = c + 1;
                    values[c] = column < row.Count ? row[column] : null;
                }

                if (row.Count - 1 < codes.Length)
                    result.Warnings.Add($"Row {r + 1} ('{key}') has fewer columns than the header; the rest are blank.");

                result.Rows.Add(new Row { Key = key, Values = values });
            }

            return result;
        }

        /// <summary>
        /// Writes a catalog out in the same shape <see cref="Parse"/> reads, so a round trip through
        /// a spreadsheet is lossless for text.
        /// </summary>
        public static string Write(LocalizationCatalog catalog, char delimiter = ',')
        {
            if (catalog == null) return string.Empty;

            catalog.ResizeEntries();

            var builder = new StringBuilder(1024);

            builder.Append("Key");
            for (var i = 0; i < catalog.Languages.Count; i++)
            {
                builder.Append(delimiter);
                Escape(builder, catalog.Languages[i].Code, delimiter);
            }

            builder.Append('\n');

            for (var c = 0; c < catalog.Categories.Count; c++)
            {
                var category = catalog.Categories[c];

                for (var e = 0; e < category.Entries.Count; e++)
                {
                    var entry = category.Entries[e];
                    if (entry == null || string.IsNullOrEmpty(entry.Key)) continue;

                    Escape(builder, LocalizationKeys.Compose(category.Name, entry.Key), delimiter);

                    for (var lang = 0; lang < catalog.Languages.Count; lang++)
                    {
                        builder.Append(delimiter);
                        Escape(builder, entry.GetValue(lang), delimiter);
                    }

                    builder.Append('\n');
                }
            }

            return builder.ToString();
        }

        private static bool IsAllBlank(List<string> row)
        {
            for (var i = 0; i < row.Count; i++)
                if (!string.IsNullOrWhiteSpace(row[i])) return false;

            return true;
        }

        private static void Escape(StringBuilder builder, string value, char delimiter)
        {
            if (string.IsNullOrEmpty(value)) return;

            var needsQuotes = value.IndexOf(delimiter) >= 0
                || value.IndexOf('"') >= 0
                || value.IndexOf('\n') >= 0
                || value.IndexOf('\r') >= 0;

            if (!needsQuotes)
            {
                builder.Append(value);
                return;
            }

            builder.Append('"');
            for (var i = 0; i < value.Length; i++)
            {
                var ch = value[i];
                if (ch == '"') builder.Append('"');
                builder.Append(ch);
            }

            builder.Append('"');
        }

        /// <summary>
        /// RFC 4180 scan. One pass, character by character, tracking whether we are inside quotes —
        /// which is the only way a field containing a newline can be read correctly.
        /// </summary>
        private static List<List<string>> ReadGrid(string text, char delimiter)
        {
            var grid = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder(64);
            var inQuotes = false;
            var i = 0;

            // A UTF-8 BOM survives a spreadsheet export and would otherwise become part of the
            // first header cell, making the key column unrecognisable.
            if (text.Length > 0 && text[0] == '﻿') i = 1;

            for (; i < text.Length; i++)
            {
                var ch = text[i];

                if (inQuotes)
                {
                    if (ch != '"')
                    {
                        field.Append(ch);
                        continue;
                    }

                    // A doubled quote is a literal quote; a lone one closes the field.
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                        continue;
                    }

                    inQuotes = false;
                    continue;
                }

                if (ch == '"' && field.Length == 0)
                {
                    inQuotes = true;
                    continue;
                }

                if (ch == delimiter)
                {
                    row.Add(field.ToString());
                    field.Length = 0;
                    continue;
                }

                if (ch == '\r') continue;

                if (ch == '\n')
                {
                    row.Add(field.ToString());
                    field.Length = 0;
                    grid.Add(row);
                    row = new List<string>();
                    continue;
                }

                field.Append(ch);
            }

            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                grid.Add(row);
            }

            return grid;
        }
    }
}
