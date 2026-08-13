#if UNITY_IOS || UNITY_TVOS
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace LocalizationKit.Editor
{
    /// <summary>
    /// Declares the app's languages to iOS and localizes the name under the icon.
    /// </summary>
    /// <remarks>
    /// The language declaration is not a nicety. iOS reports a device's language to an app only
    /// for languages the app claims in <c>CFBundleLocalizations</c>; for every other language it
    /// reports the development region instead. Without this,
    /// <see cref="UnityEngine.Application.systemLanguage"/> answers "English" on a French phone,
    /// and <see cref="StartupLanguageMode.SystemLanguage"/> never matches — in builds only, never
    /// in the editor, which is the worst way to find a bug. The same list is what the App Store
    /// shows as the app's supported languages.
    /// <para>
    /// The name under the icon is a separate, optional step, driven by
    /// <see cref="LocalizationSettings.AppNameKey"/>.
    /// </para>
    /// </remarks>
    internal static class LocalizationIosPostProcess
    {
        /// <summary>The folder inside the Xcode project the generated .lproj bundles live in.</summary>
        private const string LocalizationFolder = "LocalizationKit";

        /// <summary>The Xcode variant group localized Info.plist entries belong to, by convention.</summary>
        private const string VariantGroup = "InfoPlist.strings";

        [PostProcessBuild(1000)]
        internal static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS && target != BuildTarget.tvOS) return;

            var settings = LocalizationEditorCatalog.Settings;
            var catalog = settings != null ? settings.Catalog : null;
            if (catalog == null) return;

            var codes = LocalizationBuildData.LanguageCodes(catalog);
            if (codes.Count == 0) return;

            if (settings.DeclareLanguagesToOS)
                DeclareLanguages(pathToBuiltProject, catalog, codes);

            var names = LocalizationBuildData.AppNames(settings, catalog);
            if (names.Count > 0) LocalizeAppName(pathToBuiltProject, names);
        }

        private static void DeclareLanguages(string projectPath, LocalizationCatalog catalog, System.Collections.Generic.List<string> codes)
        {
            var plistPath = Path.Combine(projectPath, "Info.plist");

            if (!File.Exists(plistPath))
            {
                Debug.LogWarning($"[LocalizationKit] No Info.plist at {plistPath}; languages were not declared to iOS.");
                return;
            }

            try
            {
                var plist = new PlistDocument();
                plist.ReadFromFile(plistPath);

                // CreateArray replaces any existing value, which is what is wanted: the catalog is
                // the authority on what the app supports, and a stale list from a previous build
                // would claim languages that are no longer there.
                var languages = plist.root.CreateArray("CFBundleLocalizations");

                foreach (var code in codes)
                    languages.AddString(code);

                var development = catalog.DefaultLanguageCode;

                if (!string.IsNullOrEmpty(development))
                    plist.root.SetString("CFBundleDevelopmentRegion", development);

                plist.WriteToFile(plistPath);

                Debug.Log($"[LocalizationKit] Declared {codes.Count} languages to iOS in Info.plist.");
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"[LocalizationKit] Could not declare languages in Info.plist: {exception.Message}");
            }
        }

        /// <summary>
        /// Writes a localized <c>InfoPlist.strings</c> per language and registers each with the
        /// Xcode project.
        /// </summary>
        /// <remarks>
        /// Best effort by design. Editing the pbxproj is the one part of this that can go wrong in
        /// ways the kit cannot see, so a failure warns and leaves the app with its default name
        /// rather than failing the build or leaving a half-edited project behind.
        /// </remarks>
        private static void LocalizeAppName(string projectPath, System.Collections.Generic.List<LocalizationBuildData.AppName> names)
        {
            try
            {
                var pbxPath = PBXProject.GetPBXProjectPath(projectPath);

                var project = new PBXProject();
                project.ReadFromFile(pbxPath);

                foreach (var app in names)
                {
                    var relative = Path.Combine(LocalizationFolder, app.Code + ".lproj", VariantGroup)
                        .Replace('\\', '/');

                    var absolute = Path.Combine(projectPath, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(absolute));

                    var name = Escape(LocalizationBuildData.OneLine(app.Name));

                    // Both keys: CFBundleDisplayName is the label under the icon, CFBundleName the
                    // shorter name iOS falls back to in places the display name will not fit.
                    var contents = new StringBuilder()
                        .Append("\"CFBundleDisplayName\" = \"").Append(name).Append("\";\n")
                        .Append("\"CFBundleName\" = \"").Append(name).Append("\";\n")
                        .ToString();

                    File.WriteAllText(absolute, contents, new UTF8Encoding(false));

                    project.AddLocaleVariantFile(VariantGroup, app.Code, relative);
                }

                project.WriteToFile(pbxPath);

                Debug.Log($"[LocalizationKit] Localized the iOS app name into {names.Count} languages.");
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning(
                    "[LocalizationKit] Could not localize the iOS app name; the app keeps its product "
                    + $"name. {exception.Message}");
            }
        }

        /// <summary>Escapes a value for a <c>.strings</c> file, which is neither XML nor JSON.</summary>
        private static string Escape(string value) =>
            value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
#endif
