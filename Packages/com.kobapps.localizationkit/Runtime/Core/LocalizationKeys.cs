using System;

namespace LocalizationKit
{
    /// <summary>
    /// The one place that knows how a category and a key become the single string a lookup takes.
    /// </summary>
    public static class LocalizationKeys
    {
        /// <summary>The character between a category and a key in a full key.</summary>
        public const char Separator = '/';

        /// <summary>The category an entry belongs to when none is given.</summary>
        public const string DefaultCategory = "Default";

        /// <summary>
        /// Joins a category and a key into the form the runtime table is indexed by.
        /// A blank category yields the bare key, which is what the default category stores under.
        /// </summary>
        public static string Compose(string category, string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            if (string.IsNullOrEmpty(category)) return key;

            return string.Concat(category, Separator.ToString(), key);
        }

        /// <summary>Splits a full key back into its parts. Returns false when it carries no category.</summary>
        public static bool TrySplit(string fullKey, out string category, out string key)
        {
            var slash = fullKey == null ? -1 : fullKey.LastIndexOf(Separator);
            if (slash <= 0 || slash == fullKey.Length - 1)
            {
                category = null;
                key = fullKey;
                return false;
            }

            category = fullKey.Substring(0, slash);
            key = fullKey.Substring(slash + 1);
            return true;
        }

        /// <summary>The category part of a full key, or <see cref="DefaultCategory"/> when it has none.</summary>
        public static string CategoryOf(string fullKey) =>
            TrySplit(fullKey, out var category, out _) ? category : DefaultCategory;

        /// <summary>
        /// True when a category or key name is usable — non-blank and free of the separator,
        /// which would otherwise make the composed key ambiguous.
        /// </summary>
        public static bool IsValidName(string name) =>
            !string.IsNullOrWhiteSpace(name) && name.IndexOf(Separator) < 0;
    }
}
