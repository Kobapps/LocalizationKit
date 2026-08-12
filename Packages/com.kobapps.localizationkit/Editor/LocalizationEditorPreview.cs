using UnityEditor;
using UnityEngine;

namespace LocalizationKit.Editor
{
    /// <summary>
    /// Keeps a table loaded while the editor is in edit mode, so localized components show real
    /// text in the scene rather than their keys.
    /// </summary>
    /// <remarks>
    /// <see cref="Localization"/> normally comes up from <c>RuntimeInitializeOnLoadMethod</c>, which
    /// does not run in edit mode. Without this, every <c>[ExecuteAlways]</c> text component would
    /// resolve against an empty table and write its own key into the scene — turning the Game view
    /// into a list of key names and, worse, replacing text a designer had already typed.
    /// <para>
    /// <b>Play mode.</b> The table is torn down on the way into play mode so the runtime builds its
    /// own from the settings asset, with the settings asset's startup-language rules. Without that
    /// step, a project with domain reload disabled would carry the editor's table and the editor's
    /// chosen language straight into the play session — and the language you happened to be
    /// previewing would silently become the language the game starts in.
    /// </para>
    /// </remarks>
    [InitializeOnLoad]
    internal static class LocalizationEditorPreview
    {
        static LocalizationEditorPreview()
        {
            LocalizationEditorCatalog.Changed += Refresh;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;

            // The asset database is not queryable this early during a domain reload.
            EditorApplication.delayCall += Refresh;
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.ExitingEditMode:
                    Localization.Reset();
                    break;

                case PlayModeStateChange.EnteredEditMode:
                    Refresh();
                    break;
            }
        }

        /// <summary>
        /// Rebuilds the preview table from the catalog, keeping whichever language was being
        /// previewed. Does nothing while playing — the running game owns the table then.
        /// </summary>
        internal static void Refresh()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            var catalog = LocalizationEditorCatalog.Catalog;
            if (catalog == null)
            {
                Localization.Reset();
                return;
            }

            var settings = LocalizationEditorCatalog.Settings;
            var behavior = settings != null ? settings.MissingKeyBehavior : MissingKeyBehavior.ReturnKey;

            var keep = Localization.LanguageCode;
            var table = LocalizationTable.Build(catalog, behavior);

            Localization.SetTable(table, keep ?? catalog.DefaultLanguageCode);
        }

        /// <summary>
        /// Switches the language the scene is previewed in, from
        /// <b>Tools ▸ LocalizationKit ▸ Preview Language</b>.
        /// </summary>
        internal static void SetPreviewLanguage(string code)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            Localization.SetLanguage(code);
            SceneView.RepaintAll();
        }

        [MenuItem("Tools/LocalizationKit/Preview Language", priority = 120)]
        private static void ShowLanguageMenu()
        {
            var catalog = LocalizationEditorCatalog.Catalog;

            if (catalog == null || catalog.Languages.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "No languages",
                    "Create a catalog with at least one language first.",
                    "OK");
                return;
            }

            var menu = new GenericMenu();
            var current = Localization.LanguageCode;

            for (var i = 0; i < catalog.Languages.Count; i++)
            {
                var language = catalog.Languages[i];
                menu.AddItem(
                    new GUIContent($"{language.DisplayName} ({language.Code})"),
                    string.Equals(language.Code, current, System.StringComparison.OrdinalIgnoreCase),
                    () => SetPreviewLanguage(language.Code));
            }

            menu.ShowAsContext();
        }
    }
}
