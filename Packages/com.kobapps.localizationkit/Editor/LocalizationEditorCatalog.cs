using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LocalizationKit.Editor
{
    /// <summary>
    /// Finds the project's catalog and settings, and caches the key list every editor surface reads.
    /// </summary>
    /// <remarks>
    /// The key picker is drawn once per inspector row per repaint. Rebuilding a key list on each of
    /// those would be quadratic in the size of the catalog and visible as lag while scrolling an
    /// inspector, so the list is built once and invalidated by an asset postprocessor when the
    /// catalog actually changes.
    /// </remarks>
    public static class LocalizationEditorCatalog
    {
        private static LocalizationCatalog s_Catalog;
        private static LocalizationSettings s_Settings;
        private static string[] s_Keys = Array.Empty<string>();
        private static bool s_KeysValid;

        /// <summary>Raised whenever the catalog or its contents change, so open windows can refresh.</summary>
        public static event Action Changed;

        /// <summary>
        /// The catalog editor tooling works against: the one named by the settings asset, or the
        /// only one in the project when there are no settings yet.
        /// </summary>
        public static LocalizationCatalog Catalog
        {
            get
            {
                if (s_Catalog != null) return s_Catalog;

                var settings = Settings;
                if (settings != null && settings.Catalog != null)
                {
                    s_Catalog = settings.Catalog;
                    return s_Catalog;
                }

                s_Catalog = FindFirstAsset<LocalizationCatalog>();
                return s_Catalog;
            }
        }

        /// <summary>The settings asset in <c>Resources</c>, or null when the project has none yet.</summary>
        public static LocalizationSettings Settings
        {
            get
            {
                if (s_Settings != null) return s_Settings;

                s_Settings = FindFirstAsset<LocalizationSettings>();
                return s_Settings;
            }
        }

        /// <summary>Every full key in the catalog, cached. Empty when there is no catalog.</summary>
        public static string[] Keys
        {
            get
            {
                if (s_KeysValid) return s_Keys;

                var catalog = Catalog;
                s_Keys = catalog != null ? catalog.GetAllKeys().ToArray() : Array.Empty<string>();
                s_KeysValid = true;

                return s_Keys;
            }
        }

        /// <summary>Drops every cache and tells listeners to rebuild. Call after editing a catalog.</summary>
        public static void Invalidate()
        {
            s_Catalog = null;
            s_Settings = null;
            s_KeysValid = false;

            Changed?.Invoke();
        }

        /// <summary>Marks only the key list stale — for edits that changed entries but not which asset is in play.</summary>
        public static void InvalidateKeys()
        {
            s_KeysValid = false;
            Changed?.Invoke();
        }

        /// <summary>True when the catalog carries this full key.</summary>
        public static bool HasKey(string fullKey)
        {
            if (string.IsNullOrEmpty(fullKey)) return false;

            var keys = Keys;
            for (var i = 0; i < keys.Length; i++)
            {
                if (string.Equals(keys[i], fullKey, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        /// <summary>Keys within one category, or every key when the category is blank.</summary>
        public static List<string> KeysInCategory(string category)
        {
            var all = Keys;
            var result = new List<string>(all.Length);

            for (var i = 0; i < all.Length; i++)
            {
                if (string.IsNullOrEmpty(category) ||
                    string.Equals(LocalizationKeys.CategoryOf(all[i]), category, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(all[i]);
                }
            }

            return result;
        }

        /// <summary>Writes a catalog change to disk and refreshes every editor surface.</summary>
        public static void Save(LocalizationCatalog catalog)
        {
            if (catalog == null) return;

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
            InvalidateKeys();
        }

        /// <summary>Registers an undoable change about to be made to the catalog.</summary>
        public static void RecordUndo(LocalizationCatalog catalog, string what)
        {
            if (catalog == null) return;

            Undo.RecordObject(catalog, what);
        }

        // ---------------------------------------------------------------- creation

        /// <summary>
        /// Creates a catalog asset seeded with English and the four categories most projects end up
        /// with anyway, so a new user lands on something to edit rather than an empty screen.
        /// </summary>
        public static LocalizationCatalog CreateCatalog(string path)
        {
            var catalog = ScriptableObject.CreateInstance<LocalizationCatalog>();

            catalog.AddLanguage(new LanguageInfo("en", "English", SystemLanguage.English));
            catalog.DefaultLanguageCode = "en";

            catalog.GetOrAddCategory(LocalizationKeys.DefaultCategory).Description = "Strings that do not belong anywhere more specific.";
            catalog.GetOrAddCategory("Popups").Description = "Dialogs, confirmations and alerts.";
            catalog.GetOrAddCategory("Store").Description = "Shop, offers and purchase flows.";
            catalog.GetOrAddCategory("Tutorials").Description = "Onboarding and instructional copy.";

            EnsureDirectory(path);
            AssetDatabase.CreateAsset(catalog, path);
            AssetDatabase.SaveAssets();

            Invalidate();
            return catalog;
        }

        /// <summary>
        /// Creates the settings asset under <c>Assets/Resources</c>, which is the only place the
        /// runtime looks for it.
        /// </summary>
        public static LocalizationSettings CreateSettings(LocalizationCatalog catalog)
        {
            var path = $"Assets/Resources/{LocalizationSettings.AssetName}.asset";

            var settings = ScriptableObject.CreateInstance<LocalizationSettings>();
            settings.Catalog = catalog;

            EnsureDirectory(path);
            AssetDatabase.CreateAsset(settings, path);
            AssetDatabase.SaveAssets();

            Invalidate();
            return settings;
        }

        private static void EnsureDirectory(string assetPath)
        {
            var directory = Path.GetDirectoryName(assetPath);
            if (string.IsNullOrEmpty(directory) || Directory.Exists(directory)) return;

            Directory.CreateDirectory(directory);
            AssetDatabase.Refresh();
        }

        private static T FindFirstAsset<T>() where T : ScriptableObject
        {
            var guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");

            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null) return asset;
            }

            return null;
        }

        /// <summary>
        /// Invalidates the caches when a catalog or settings asset is written by anything —
        /// the window, a script, a version-control update, an external merge.
        /// </summary>
        private sealed class Watcher : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(
                string[] imported,
                string[] deleted,
                string[] movedTo,
                string[] movedFrom)
            {
                if (Touches(imported) || Touches(deleted) || Touches(movedTo))
                    Invalidate();
            }

            private static bool Touches(string[] paths)
            {
                for (var i = 0; i < paths.Length; i++)
                {
                    if (!paths[i].EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) continue;

                    var type = AssetDatabase.GetMainAssetTypeAtPath(paths[i]);
                    if (type == typeof(LocalizationCatalog) || type == typeof(LocalizationSettings))
                        return true;
                }

                return false;
            }
        }
    }
}
