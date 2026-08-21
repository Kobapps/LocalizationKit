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
            var card = new KUICard(
                "Remote catalogs",
                "The same merge, with the network in front of it.");

            card.Add(KUIText.Body(
                "A provider fetches the shape above from wherever translations actually live — a Google "
                + "Sheet, a CDN, a translation service — and hands it to the same merge this page uses. "
                + "The Remote page has the buttons; the Automation section there covers builds and "
                + "runtime refreshes."));

            card.Add(KUILayout.Gap(6f));
            card.Add(KUIButton.Secondary("Open Remote Page", () => m_Window.ShowPage(4)));

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

            if (!LocalizationSnapshot.TryFromCsv(text, out var incoming, out var error, delimiter))
            {
                EditorUtility.DisplayDialog("Import failed", error, "OK");
                return;
            }

            var report = Apply(catalog, incoming);

            LocalizationEditorCatalog.Save(catalog);
            m_Window.Refresh();

            EditorUtility.DisplayDialog("Import complete", report, "OK");
            m_Window.Toast("Import complete.");
        }

        private string Apply(LocalizationCatalog catalog, LocalizationSnapshot incoming)
        {
            LocalizationEditorCatalog.RecordUndo(catalog, "Import Localization");

            var report = LocalizationMerge.Into(catalog, incoming, new LocalizationMergeOptions
            {
                AddNewKeys = m_AddNewKeys,
                AddNewLanguages = m_AddNewLanguages,
                OverwriteExisting = m_OverwriteExisting
            });

            return report.Summary();
        }
    }
}
