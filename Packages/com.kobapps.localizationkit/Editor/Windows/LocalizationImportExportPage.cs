using System;
using System.Collections.Generic;
using System.IO;
using EditorCoreKit.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LocalizationKit.Editor
{
    /// <summary>
    /// Move the catalog in and out of the spreadsheet translators actually work in.
    /// </summary>
    /// <remarks>
    /// This is also the seam the future remote source arrives through: a Google Sheet published as
    /// CSV has exactly the shape <see cref="LocalizationCsv"/> reads, so the import path built here
    /// is the same one a runtime fetch will use — only the byte source changes.
    /// </remarks>
    internal sealed class LocalizationImportExportPage
    {
        private readonly LocalizationKitWindow m_Window;

        private bool m_AddNewKeys = true;
        private bool m_AddNewLanguages;
        private bool m_OverwriteExisting = true;

        internal LocalizationImportExportPage(LocalizationKitWindow window)
        {
            m_Window = window;
        }

        internal VisualElement Build()
        {
            var catalog = LocalizationEditorCatalog.Catalog;
            if (catalog == null) return m_Window.BuildNoCatalogState();

            return KUILayout.Page(
                BuildExportCard(catalog),
                BuildImportCard(catalog),
                BuildRemoteCard());
        }

        private VisualElement BuildExportCard(LocalizationCatalog catalog)
        {
            var card = new KUICard(
                "Export",
                "One row per key, one column per language — open it in Sheets or Excel as is.");

            card.Add(KUIText.Muted(
                $"{LocalizationStats.For(catalog).EntryCount} keys × {catalog.Languages.Count} languages."));
            card.Add(KUILayout.Gap(6f));

            var row = KUILayout.Row();
            row.Add(KUIButton.Primary("Export CSV…", () => Export(catalog, ',')));
            row.Add(KUIButton.Secondary("Export TSV…", () => Export(catalog, '\t')));
            card.Add(row);

            return card;
        }

        private VisualElement BuildImportCard(LocalizationCatalog catalog)
        {
            var card = new KUICard("Import", "Merged into the catalog; nothing is deleted.");

            card.Add(new KUIToggleSwitch("Add keys not in the catalog", m_AddNewKeys, v => m_AddNewKeys = v));
            card.Add(new KUIToggleSwitch("Add languages not in the catalog", m_AddNewLanguages, v => m_AddNewLanguages = v));
            card.Add(new KUIToggleSwitch("Overwrite existing text", m_OverwriteExisting, v => m_OverwriteExisting = v));

            card.Add(KUILayout.Gap(6f));
            card.Add(KUIText.Muted(
                "With overwrite off, only blank cells are filled — the way to take a translation pass "
                + "back without losing edits made in the editor since."));

            card.Add(KUILayout.Gap(6f));

            var row = KUILayout.Row();
            row.Add(KUIButton.Primary("Import CSV…", () => Import(catalog, ',')));
            row.Add(KUIButton.Secondary("Import TSV…", () => Import(catalog, '\t')));
            card.Add(row);

            return card;
        }

        private VisualElement BuildRemoteCard()
        {
            var card = new KUICard("Remote catalogs", "Not wired up yet — but the seam is in place.");

            card.Add(KUIText.Body(
                "Localization reads a LocalizationTable, not a catalog asset. Anything that can produce "
                + "a table can be the source, so a Google Sheet published to CSV needs a fetch and a "
                + "call to Localization.SetTable — every bound field and component refreshes on its own."));

            card.Add(KUILayout.Gap(6f));
            card.Add(KUIText.Code(
                "var csv = await Download(url);\n"
                + "var table = LocalizationTableBuilder.FromCsv(csv);\n"
                + "Localization.SetTable(table);"));

            card.Add(KUILayout.Gap(6f));
            card.Add(KUIText.Muted(
                "In Sheets: File ▸ Share ▸ Publish to web ▸ Comma-separated values."));

            return card;
        }

        // ---------------------------------------------------------------- operations

        private void Export(LocalizationCatalog catalog, char delimiter)
        {
            var extension = delimiter == '\t' ? "tsv" : "csv";

            var path = EditorUtility.SaveFilePanel(
                "Export Localization",
                string.Empty,
                $"{catalog.name}.{extension}",
                extension);

            if (string.IsNullOrEmpty(path)) return;

            try
            {
                // UTF-8 with a BOM: without it Excel opens the file in the system codepage and every
                // non-ASCII translation comes back mangled.
                File.WriteAllText(path, LocalizationCsv.Write(catalog, delimiter), new System.Text.UTF8Encoding(true));
                m_Window.Toast($"Exported to {Path.GetFileName(path)}.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                m_Window.Toast("Export failed — see the console.", KUITone.Error);
            }
        }

        private void Import(LocalizationCatalog catalog, char delimiter)
        {
            var extension = delimiter == '\t' ? "tsv" : "csv";
            var path = EditorUtility.OpenFilePanel("Import Localization", string.Empty, extension);
            if (string.IsNullOrEmpty(path)) return;

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                m_Window.Toast("Could not read that file.", KUITone.Error);
                return;
            }

            var parsed = LocalizationCsv.Parse(text, delimiter);
            if (parsed.Failed)
            {
                EditorUtility.DisplayDialog("Import failed", parsed.Error, "OK");
                return;
            }

            var report = Apply(catalog, parsed);

            LocalizationEditorCatalog.Save(catalog);
            m_Window.Refresh();

            EditorUtility.DisplayDialog("Import complete", report, "OK");
            m_Window.Toast("Import complete.");
        }

        private string Apply(LocalizationCatalog catalog, LocalizationCsv.ParseResult parsed)
        {
            LocalizationEditorCatalog.RecordUndo(catalog, "Import Localization");

            var addedLanguages = new List<string>();
            var skippedLanguages = new List<string>();
            var addedKeys = 0;
            var updated = 0;
            var skippedKeys = 0;

            // Resolve each CSV column to a catalog language index once, rather than per row.
            var columnToLanguage = new int[parsed.LanguageCodes.Length];

            for (var c = 0; c < parsed.LanguageCodes.Length; c++)
            {
                var code = parsed.LanguageCodes[c];
                var index = catalog.IndexOfLanguage(code);

                if (index < 0 && m_AddNewLanguages && LocalizationKeys.IsValidName(code))
                {
                    index = catalog.AddLanguage(new LanguageInfo(code, code));
                    addedLanguages.Add(code);
                }
                else if (index < 0)
                {
                    skippedLanguages.Add(code);
                }

                columnToLanguage[c] = index;
            }

            foreach (var row in parsed.Rows)
            {
                var hasCategory = LocalizationKeys.TrySplit(row.Key, out var category, out var key);
                if (!hasCategory) category = LocalizationKeys.DefaultCategory;

                var entry = catalog.FindByFullKey(LocalizationKeys.Compose(category, key));

                if (entry == null)
                {
                    if (!m_AddNewKeys)
                    {
                        skippedKeys++;
                        continue;
                    }

                    entry = catalog.AddEntry(category, key);
                    addedKeys++;
                }

                for (var c = 0; c < columnToLanguage.Length; c++)
                {
                    var language = columnToLanguage[c];
                    if (language < 0) continue;

                    var incoming = c < row.Values.Length ? row.Values[c] : null;
                    if (string.IsNullOrEmpty(incoming)) continue;

                    if (!m_OverwriteExisting && !entry.IsMissing(language)) continue;
                    if (string.Equals(entry.GetValue(language), incoming, StringComparison.Ordinal)) continue;

                    entry.SetValue(language, incoming);
                    updated++;
                }
            }

            catalog.ResizeEntries();

            var report = new System.Text.StringBuilder();
            report.AppendLine($"{parsed.Rows.Count} rows read.");
            report.AppendLine($"{addedKeys} keys added, {updated} translations written.");

            if (skippedKeys > 0)
                report.AppendLine($"{skippedKeys} unknown keys skipped (\"add keys\" is off).");

            if (addedLanguages.Count > 0)
                report.AppendLine($"Languages added: {string.Join(", ", addedLanguages)}.");

            if (skippedLanguages.Count > 0)
                report.AppendLine($"Columns ignored (no such language): {string.Join(", ", skippedLanguages)}.");

            foreach (var warning in parsed.Warnings)
                report.AppendLine($"• {warning}");

            return report.ToString();
        }
    }
}
