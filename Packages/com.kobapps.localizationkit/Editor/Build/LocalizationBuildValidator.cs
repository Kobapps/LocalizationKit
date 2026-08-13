using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LocalizationKit.Editor
{
    /// <summary>
    /// Refuses to build a player that would ship with no localization data.
    /// </summary>
    /// <remarks>
    /// The editor finds the catalog through the asset database, which a player does not have. The
    /// runtime's only route to it is <c>Resources.Load</c> of the settings asset, so a settings
    /// asset that is missing, misnamed, or outside a <c>Resources</c> folder works perfectly in
    /// the editor and produces a build where every label shows its raw key. Nothing throws, nothing
    /// logs, and the failure is invisible until the build is on a device — or in a store.
    /// <para>
    /// That is worth failing a build over, but only when the project is actually using the kit: a
    /// catalog with entries in it. A project that has the package installed and no content gets no
    /// opinion from this.
    /// </para>
    /// </remarks>
    internal sealed class LocalizationBuildValidator : IPreprocessBuildWithReport
    {
        /// <summary>Runs before the platform post-processors, which assume the data is sound.</summary>
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            var problem = Check();
            if (problem == null) return;

            throw new BuildFailedException(
                "[LocalizationKit] " + problem
                + "\n\nFix it in Project Settings ▸ LocalizationKit, or turn off this check by "
                + "emptying the catalog.");
        }

        /// <summary>
        /// What would make this build ship without localization, or null when it is sound. Also
        /// used by the menu item, so the answer can be had without starting a build.
        /// </summary>
        internal static string Check()
        {
            var catalog = LocalizationEditorCatalog.Catalog;

            // No content means the kit is installed but unused. Not this code's business.
            if (catalog == null || catalog.EntryCount == 0) return null;

            var settings = LocalizationEditorCatalog.Settings;

            if (settings == null)
            {
                return "This project has a localization catalog with " + catalog.EntryCount
                    + " keys but no settings asset. The runtime finds its catalog only through "
                    + "that asset, so the build would resolve every key to its own name.";
            }

            var path = AssetDatabase.GetAssetPath(settings);

            if (settings.Catalog == null)
            {
                return "The settings asset at " + path + " has no catalog assigned, so the build "
                    + "would resolve every key to its own name.";
            }

            if (!path.Contains("/Resources/"))
            {
                return "The settings asset is at " + path + ", which is not inside a Resources "
                    + "folder. Only Resources ships in a player, so the runtime would find nothing.";
            }

            var expected = "/" + LocalizationSettings.ResourcePath + ".asset";

            if (!path.EndsWith(expected, System.StringComparison.Ordinal))
            {
                return "The settings asset is named " + System.IO.Path.GetFileName(path)
                    + ". The runtime loads it by the fixed name '" + LocalizationSettings.ResourcePath
                    + "', so it would find nothing.";
            }

            if (settings.Catalog.Languages.Count == 0)
            {
                return "The catalog carries no languages, so there is nothing for a key to resolve to.";
            }

            return null;
        }

        [MenuItem("Tools/LocalizationKit/Validate Build Setup", priority = 120)]
        private static void Validate()
        {
            var problem = Check();

            if (problem == null)
            {
                EditorUtility.DisplayDialog(
                    "LocalizationKit",
                    "The build setup is sound: the settings asset is where the runtime looks for it "
                    + "and points at a catalog with languages in it.",
                    "OK");

                return;
            }

            Debug.LogError("[LocalizationKit] " + problem);
            EditorUtility.DisplayDialog("LocalizationKit — build would ship unlocalized", problem, "OK");
        }
    }
}
