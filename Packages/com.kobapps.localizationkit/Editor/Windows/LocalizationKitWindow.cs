using System;
using EditorCoreKit.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LocalizationKit.Editor
{
    /// <summary>
    /// The catalog manager: languages, keys, translation coverage, import and export.
    /// </summary>
    /// <remarks>
    /// Everything here works against the catalog asset through
    /// <see cref="LocalizationEditorCatalog"/>, never against runtime state, so the window behaves
    /// the same whether or not the game is playing.
    /// </remarks>
    public sealed class LocalizationKitWindow : EditorWindow
    {
        internal const string Version = "1.1.0";

        private KUIWindowShell m_Shell;
        private LocalizationOverviewPage m_Overview;
        private LocalizationLanguagesPage m_Languages;
        private LocalizationKeysPage m_Keys;
        private LocalizationImportExportPage m_ImportExport;

        /// <summary>Opens the window, focusing it if it is already up.</summary>
        [MenuItem("Tools/LocalizationKit/Localization Manager", priority = 100)]
        public static LocalizationKitWindow Open()
        {
            var window = GetWindow<LocalizationKitWindow>();
            window.titleContent = new GUIContent("Localization");
            window.minSize = new Vector2(720f, 420f);
            window.Show();

            return window;
        }

        /// <summary>Opens the window and selects a specific key. Used by the inspector's "edit" action.</summary>
        public static void OpenAt(string fullKey)
        {
            var window = Open();
            window.m_Keys?.SelectKey(fullKey);
            window.ShowPage(2);
        }

        private void OnEnable()
        {
            LocalizationEditorCatalog.Changed += Refresh;
        }

        private void OnDisable()
        {
            LocalizationEditorCatalog.Changed -= Refresh;
        }

        private void CreateGUI()
        {
            m_Overview = new LocalizationOverviewPage(this);
            m_Languages = new LocalizationLanguagesPage(this);
            m_Keys = new LocalizationKeysPage(this);
            m_ImportExport = new LocalizationImportExportPage(this);

            m_Shell = new KUIWindowShell("Localization", $"v{Version}").MountInto(rootVisualElement);

            m_Shell.Header.Add(KUIButton.Secondary("Reveal Catalog", RevealCatalog));
            m_Shell.Header.Add(KUIButton.Primary("New Key…", () =>
            {
                ShowPage(2);
                m_Keys.BeginNewKey();
            }));

            m_Shell.Sidebar.Add("Overview", () => m_Shell.SetContent(m_Overview.Build));
            m_Shell.Sidebar.Add("Languages", () => m_Shell.SetContent(m_Languages.Build));
            m_Shell.Sidebar.Add("Keys", () => m_Shell.SetContent(m_Keys.Build));
            m_Shell.Sidebar.Add("Import & Export", () => m_Shell.SetContent(m_ImportExport.Build));
            m_Shell.Sidebar.AddSeparator();
            m_Shell.Sidebar.AddFootnote("Catalog is a project asset. Changes are saved to it directly.");

            ShowPage(0);
            UpdateStatus();
        }

        /// <summary>Selects a sidebar page by index and rebuilds it.</summary>
        internal void ShowPage(int index)
        {
            if (m_Shell?.Sidebar == null) return;

            m_Shell.Sidebar.SelectedIndex = index;

            switch (index)
            {
                case 1: m_Shell.SetContent(m_Languages.Build); break;
                case 2: m_Shell.SetContent(m_Keys.Build); break;
                case 3: m_Shell.SetContent(m_ImportExport.Build); break;
                default: m_Shell.SetContent(m_Overview.Build); break;
            }
        }

        /// <summary>Rebuilds the current page and the status bar. Cheap enough to call after any edit.</summary>
        internal void Refresh()
        {
            if (m_Shell == null) return;

            ShowPage(Mathf.Max(0, m_Shell.Sidebar.SelectedIndex));
            UpdateStatus();
        }

        /// <summary>Shows a transient confirmation over the bottom of the window.</summary>
        internal void Toast(string message, KUITone tone = KUITone.Success)
        {
            KUIToast.Show(rootVisualElement, message, tone);
        }

        /// <summary>Persists the catalog and refreshes every editor surface that reads it.</summary>
        internal void SaveCatalog(string undoLabel = null)
        {
            var catalog = LocalizationEditorCatalog.Catalog;
            if (catalog == null) return;

            LocalizationEditorCatalog.Save(catalog);
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            if (m_Shell?.Status == null) return;

            var catalog = LocalizationEditorCatalog.Catalog;
            if (catalog == null)
            {
                m_Shell.Status.Set("No catalog in this project.", KUITone.Warning);
                return;
            }

            var stats = LocalizationStats.For(catalog);

            var tone = stats.Coverage >= 1f ? KUITone.Success
                : stats.Coverage >= 0.9f ? KUITone.Neutral
                : KUITone.Warning;

            m_Shell.Status.Set(
                $"{stats.LanguageCount} languages · {stats.EntryCount} keys · {stats.Coverage:P0} translated",
                tone);
        }

        private static void RevealCatalog()
        {
            var catalog = LocalizationEditorCatalog.Catalog;
            if (catalog == null) return;

            EditorGUIUtility.PingObject(catalog);
            Selection.activeObject = catalog;
        }

        /// <summary>
        /// The banner every page shows when the project has no catalog yet, with the one button
        /// that fixes it.
        /// </summary>
        internal VisualElement BuildNoCatalogState()
        {
            return KUILayout.Page(new KUIEmptyState(
                "No localization catalog",
                "A catalog holds every language, category and key in the project. Create one to get started.",
                "Create Catalog…",
                CreateCatalogInteractive,
                "🌐"));
        }

        private void CreateCatalogInteractive()
        {
            var path = EditorUtility.SaveFilePanelInProject(
                "Create Localization Catalog",
                "LocalizationCatalog",
                "asset",
                "The catalog holds every language and key. It is a normal project asset — commit it.");

            if (string.IsNullOrEmpty(path)) return;

            var catalog = LocalizationEditorCatalog.CreateCatalog(path);

            // Without the settings asset the runtime has no way to find the catalog, and the
            // failure is silent at play time. Offer to make it now rather than let that happen.
            if (LocalizationEditorCatalog.Settings == null)
            {
                var create = EditorUtility.DisplayDialog(
                    "Create settings asset?",
                    "The runtime finds its catalog through a settings asset in Resources. Without one, "
                    + "nothing is localized at play time.\n\nCreate it now?",
                    "Create",
                    "Later");

                if (create) LocalizationEditorCatalog.CreateSettings(catalog);
            }

            Selection.activeObject = catalog;
            Refresh();
            Toast("Catalog created.");
        }
    }
}
