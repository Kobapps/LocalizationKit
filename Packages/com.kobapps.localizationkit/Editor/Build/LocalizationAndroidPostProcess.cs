using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor.Android;
using UnityEngine;

namespace LocalizationKit.Editor
{
    /// <summary>
    /// Writes the app's localized name into the generated Gradle project, so the label under the
    /// icon follows the device's language.
    /// </summary>
    /// <remarks>
    /// Android resolves <c>app_name</c> from <c>res/values-&lt;qualifier&gt;</c> folders picked by
    /// the device's locale. Unity generates only the default one from the product name, so the
    /// localized variants have to be added to the Gradle project after it is generated and before
    /// it is built — which is exactly the window
    /// <see cref="IPostGenerateGradleAndroidProject"/> exists for. A <c>PostProcessBuild</c>
    /// callback is too late: by then the APK is packed.
    /// <para>
    /// The files go into the <c>unityLibrary</c> module. Its resources are merged with the
    /// launcher's, and Android picks by qualifier specificity, so a <c>values-fr</c> here still
    /// beats the launcher's unqualified default on a French device.
    /// </para>
    /// </remarks>
    internal sealed class LocalizationAndroidPostProcess : IPostGenerateGradleAndroidProject
    {
        /// <summary>Late, so a project that rewrites its own resources has already done so.</summary>
        public int callbackOrder => 1000;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var settings = LocalizationEditorCatalog.Settings;
            var catalog = settings != null ? settings.Catalog : null;

            var names = LocalizationBuildData.AppNames(settings, catalog);
            if (names.Count == 0) return;

            var resources = Path.Combine(path, "src", "main", "res");
            var written = 0;

            foreach (var app in names)
            {
                var qualifier = ResourceQualifier(app.Code);
                if (qualifier == null) continue;

                var folder = Path.Combine(resources, "values-" + qualifier);

                if (WriteAppName(folder, LocalizationBuildData.OneLine(app.Name)))
                    written++;
            }

            Debug.Log($"[LocalizationKit] Wrote a localized app name into {written} Android resource folders.");
        }

        /// <summary>
        /// The <c>values-</c> suffix Android matches a language tag with.
        /// </summary>
        /// <remarks>
        /// Two spellings exist and the choice is not cosmetic. The legacy <c>fr-rCA</c> form is
        /// understood by every API level but can only carry a language and a region; anything with
        /// a script subtag — <c>zh-Hans</c> — needs the BCP-47 <c>b+zh+Hans</c> form, which is
        /// API 24 and up. Each tag therefore gets the oldest form that can express it.
        /// </remarks>
        internal static string ResourceQualifier(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;

            var parts = code.Trim().Split('-');

            for (var i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) return null;
            }

            if (parts.Length == 1) return parts[0].ToLowerInvariant();

            if (parts.Length == 2 && IsRegion(parts[1]))
                return parts[0].ToLowerInvariant() + "-r" + parts[1].ToUpperInvariant();

            var builder = new StringBuilder("b+");

            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0) builder.Append('+');
                builder.Append(parts[i]);
            }

            return builder.ToString();
        }

        /// <summary>A region subtag is two letters or three digits; everything else is a script or variant.</summary>
        private static bool IsRegion(string part)
        {
            if (part.Length == 2) return char.IsLetter(part[0]) && char.IsLetter(part[1]);

            if (part.Length == 3)
            {
                for (var i = 0; i < 3; i++)
                {
                    if (!char.IsDigit(part[i])) return false;
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Sets <c>app_name</c> in a folder's <c>strings.xml</c>, creating or amending the file.
        /// </summary>
        /// <remarks>
        /// Amending rather than overwriting matters: another package may own the same qualified
        /// folder, and replacing its file would silently drop its strings.
        /// </remarks>
        private static bool WriteAppName(string folder, string appName)
        {
            var file = Path.Combine(folder, "strings.xml");
            var value = Escape(appName);

            try
            {
                Directory.CreateDirectory(folder);

                var element = "<string name=\"app_name\">" + value + "</string>";

                if (!File.Exists(file))
                {
                    File.WriteAllText(
                        file,
                        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<resources>\n    " + element + "\n</resources>\n",
                        new UTF8Encoding(false));

                    return true;
                }

                var xml = File.ReadAllText(file);
                var existing = new Regex("<string\\s+name=\"app_name\"\\s*>.*?</string>", RegexOptions.Singleline);

                if (existing.IsMatch(xml))
                {
                    xml = existing.Replace(xml, element.Replace("$", "$$"), 1);
                }
                else
                {
                    var close = xml.LastIndexOf("</resources>", System.StringComparison.Ordinal);
                    if (close < 0) return false;

                    xml = xml.Insert(close, "    " + element + "\n");
                }

                File.WriteAllText(file, xml, new UTF8Encoding(false));
                return true;
            }
            catch (System.Exception exception)
            {
                // A build must not fall over because one locale's file could not be written; the
                // app just keeps its default name in that language.
                Debug.LogWarning($"[LocalizationKit] Could not write {file}: {exception.Message}");
                return false;
            }
        }

        /// <summary>
        /// Escapes a string for an Android resource value.
        /// </summary>
        /// <remarks>
        /// Two layers, both required. The file is XML, so <c>&amp;</c> and the angle brackets need
        /// entities; and Android's own resource parser then treats an unescaped apostrophe or
        /// quote as a syntax error, which fails the Gradle build rather than degrading.
        /// </remarks>
        private static string Escape(string value)
        {
            var builder = new StringBuilder(value.Length + 8);

            foreach (var character in value)
            {
                switch (character)
                {
                    case '&': builder.Append("&amp;"); break;
                    case '<': builder.Append("&lt;"); break;
                    case '>': builder.Append("&gt;"); break;
                    case '\'': builder.Append("\\'"); break;
                    case '"': builder.Append("\\\""); break;
                    case '@': builder.Append(builder.Length == 0 ? "\\@" : "@"); break;
                    case '?': builder.Append(builder.Length == 0 ? "\\?" : "?"); break;
                    default: builder.Append(character); break;
                }
            }

            return builder.ToString();
        }

        /// <summary>Exposed for tests: the escaping is the part with rules worth pinning down.</summary>
        internal static string EscapeForTests(string value) => Escape(value);
    }
}
