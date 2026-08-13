using System;
using System.Collections.Generic;

namespace LocalizationKit.Editor
{
    /// <summary>
    /// What the platform post-processors need out of the catalog: the list of languages the app
    /// supports, and the app's name in each of them.
    /// </summary>
    /// <remarks>
    /// Read from the catalog asset directly rather than through <see cref="Localization"/>. The
    /// runtime table is built by a player that is not running yet; at build time the catalog is
    /// simply an asset, and reading it as one keeps the build path independent of whether the
    /// editor happens to have initialised the runtime.
    /// </remarks>
    internal static class LocalizationBuildData
    {
        /// <summary>One language's name for the application.</summary>
        internal readonly struct AppName
        {
            internal readonly string Code;
            internal readonly string Name;

            internal AppName(string code, string name)
            {
                Code = code;
                Name = name;
            }
        }

        /// <summary>
        /// Every language code the app should declare, with base codes added for regional
        /// variants.
        /// </summary>
        /// <remarks>
        /// A catalog carrying only <c>pt-BR</c> should still be offered to a device set to
        /// Portuguese-Portugal: matching a base code is how every platform falls back, and
        /// declaring only the variant means such a device gets the development language instead.
        /// <para>
        /// The base is added only when exactly one variant of it exists. With both <c>pt-BR</c>
        /// and <c>pt-PT</c> present there is no honest answer to what plain <c>pt</c> means, and
        /// picking one silently would be worse than leaving the platform to choose.
        /// </para>
        /// </remarks>
        internal static List<string> LanguageCodes(LocalizationCatalog catalog, bool includeBaseCodes = true)
        {
            var codes = new List<string>();
            if (catalog == null) return codes;

            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < catalog.Languages.Count; i++)
            {
                var code = catalog.Languages[i].Code;
                if (string.IsNullOrWhiteSpace(code)) continue;

                code = code.Trim();
                if (present.Add(code)) codes.Add(code);
            }

            if (!includeBaseCodes) return codes;

            var bases = new List<string>();

            foreach (var code in codes)
            {
                var dash = code.IndexOf('-');
                if (dash <= 0) continue;

                var root = code.Substring(0, dash);
                if (present.Contains(root)) continue;
                if (CountVariants(codes, root) != 1) continue;
                if (!present.Add(root)) continue;

                bases.Add(root);
            }

            codes.AddRange(bases);
            return codes;
        }

        private static int CountVariants(List<string> codes, string root)
        {
            var count = 0;

            foreach (var code in codes)
            {
                if (code.Length > root.Length
                    && code[root.Length] == '-'
                    && code.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// The app's name per language, or an empty list when the feature is not in use.
        /// </summary>
        /// <remarks>
        /// A language with no text for the key falls back to the default language rather than
        /// being skipped: a missing entry in a platform's string table shows the raw resource name
        /// under the icon on some devices, which is a far worse outcome than an untranslated name.
        /// </remarks>
        internal static List<AppName> AppNames(LocalizationSettings settings, LocalizationCatalog catalog)
        {
            var names = new List<AppName>();

            if (settings == null || catalog == null) return names;
            if (string.IsNullOrWhiteSpace(settings.AppNameKey)) return names;

            var entry = catalog.FindByFullKey(settings.AppNameKey.Trim());
            if (entry == null) return names;

            var fallbackIndex = catalog.IndexOfLanguage(catalog.DefaultLanguageCode);
            var fallback = fallbackIndex >= 0 ? entry.GetValue(fallbackIndex) : null;

            // With nothing in the default language there is no safe value to fall back to, so the
            // whole feature stays off rather than half-applied across the languages that do have one.
            if (string.IsNullOrWhiteSpace(fallback)) return names;

            foreach (var code in LanguageCodes(catalog))
            {
                var index = catalog.IndexOfLanguage(code);
                var value = index >= 0 ? entry.GetValue(index) : null;

                names.Add(new AppName(code, string.IsNullOrWhiteSpace(value) ? fallback.Trim() : value.Trim()));
            }

            return names;
        }

        /// <summary>Strips line breaks, which no platform's app-name field can carry.</summary>
        internal static string OneLine(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            return value
                .Replace("\r\n", " ")
                .Replace('\n', ' ')
                .Replace('\r', ' ')
                .Trim();
        }
    }
}
