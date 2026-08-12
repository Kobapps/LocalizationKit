using UnityEditor;
using UnityEngine;

namespace LocalizationKit.Editor
{
    /// <summary>
    /// Takes a project from nothing to working localization in one menu item.
    /// </summary>
    /// <remarks>
    /// Setup is three assets that must agree with each other — a catalog, a settings asset in
    /// <c>Resources</c>, and the link between them — and getting any of them wrong fails silently at
    /// runtime rather than loudly at edit time. That is exactly the shape of task worth doing in one
    /// click instead of four steps in a README.
    /// </remarks>
    internal static class LocalizationQuickSetup
    {
        private const string CatalogPath = "Assets/Localization/LocalizationCatalog.asset";

        [MenuItem("Tools/LocalizationKit/Set Up Localization", priority = 90)]
        internal static void Run()
        {
            var catalog = LocalizationEditorCatalog.Catalog;
            var settings = LocalizationEditorCatalog.Settings;

            if (catalog != null && settings != null && settings.Catalog == catalog)
            {
                if (EditorUtility.DisplayDialog(
                        "Already set up",
                        $"'{catalog.name}' is wired to the settings asset and ready to use.\n\n"
                        + "Open the manager to add languages and keys?",
                        "Open Manager",
                        "Close"))
                {
                    LocalizationKitWindow.Open();
                }

                return;
            }

            var proceed = EditorUtility.DisplayDialog(
                "Set up localization",
                "This creates:\n\n"
                + $"  •  {CatalogPath}\n"
                + $"      seeded with English and the Default / Popups / Store / Tutorials categories\n\n"
                + $"  •  Assets/Resources/{LocalizationSettings.AssetName}.asset\n"
                + "      how the runtime finds the catalog — without it nothing is localized in a build\n\n"
                + "Existing assets are reused, not replaced.",
                "Create",
                "Cancel");

            if (!proceed) return;

            if (catalog == null) catalog = LocalizationEditorCatalog.CreateCatalog(CatalogPath);

            if (settings == null)
            {
                LocalizationEditorCatalog.CreateSettings(catalog);
            }
            else if (settings.Catalog == null)
            {
                Undo.RecordObject(settings, "Assign Localization Catalog");
                settings.Catalog = catalog;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssetIfDirty(settings);
            }

            LocalizationEditorCatalog.Invalidate();

            Selection.activeObject = catalog;
            EditorGUIUtility.PingObject(catalog);

            LocalizationKitWindow.Open();

            Debug.Log("[LocalizationKit] Setup complete. Add languages and keys in Tools ▸ LocalizationKit ▸ Localization Manager.");
        }
    }
}
