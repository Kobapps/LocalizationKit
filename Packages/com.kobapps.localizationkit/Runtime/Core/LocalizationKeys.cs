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
        /// True when a key name is usable — non-blank and free of the separator, which would
        /// otherwise make the composed key ambiguous.
        /// </summary>
        /// <remarks>
        /// This is the rule for the <em>last</em> segment only. A category may contain separators;
        /// see <see cref="IsValidCategory"/>.
        /// </remarks>
        public static bool IsValidName(string name) =>
            !string.IsNullOrWhiteSpace(name) && name.IndexOf(Separator) < 0;

        /// <summary>
        /// True when a category path is usable: one or more non-blank segments joined by the
        /// separator, e.g. <c>Popups</c> or <c>Popups/Quit Level</c>.
        /// </summary>
        /// <remarks>
        /// Categories nest, keys do not. <see cref="TrySplit"/> divides a full key at its
        /// <em>last</em> separator, so <c>Popups/Quit Level/Title</c> is the key <c>Title</c> in
        /// the category <c>Popups/Quit Level</c> — which is what makes arbitrary nesting work
        /// without the lookup ever having to know how deep a catalog goes.
        /// <para>
        /// A leading, trailing or doubled separator is rejected: each produces an empty segment,
        /// and two categories that differ only by one — <c>Popups</c> and <c>Popups/</c> — would
        /// compose the same full keys while being different categories.
        /// </para>
        /// </remarks>
        public static bool IsValidCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category)) return false;

            var start = 0;

            for (var i = 0; i <= category.Length; i++)
            {
                if (i < category.Length && category[i] != Separator) continue;

                // Walking to Length as well as to each separator means the final segment is
                // checked by the same code as the rest, with no trailing special case.
                var length = i - start;
                if (length == 0) return false;

                var blank = true;
                for (var c = start; c < i; c++)
                {
                    if (char.IsWhiteSpace(category[c])) continue;

                    blank = false;
                    break;
                }

                if (blank) return false;
                start = i + 1;
            }

            return true;
        }

        /// <summary>
        /// Files a key under a category, unless it is already filed there.
        /// </summary>
        /// <remarks>
        /// This is what a source that carries its category out of band needs — a spreadsheet with a
        /// tab per category, a folder of per-category files, an endpoint returning one category at
        /// a time. Inside the <c>Popups</c> tab, <c>Settings/Title</c> means
        /// <c>Popups/Settings/Title</c>.
        /// <para>
        /// A key that already names its category is returned untouched, and that is the whole
        /// reason this is a named operation rather than a string concatenation at each call site.
        /// Somebody will eventually write the full <c>Popups/Title</c> inside the Popups tab
        /// instead of the bare <c>Title</c>; blindly prefixing turns that into
        /// <c>Popups/Popups/Title</c>, a key that exists nowhere, resolves to itself, and shows up
        /// on screen as its own name.
        /// </para>
        /// </remarks>
        public static string Qualify(string category, string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            if (string.IsNullOrEmpty(category)) return key;

            return IsUnder(CategoryOf(key), category) ? key : Compose(category, key);
        }

        /// <summary>
        /// True when <paramref name="category"/> is <paramref name="root"/> or sits underneath it,
        /// which is what selecting a group in a category tree has to mean.
        /// </summary>
        public static bool IsUnder(string category, string root)
        {
            if (string.IsNullOrEmpty(root)) return true;
            if (string.IsNullOrEmpty(category)) return false;

            if (string.Equals(category, root, StringComparison.OrdinalIgnoreCase)) return true;

            // The separator has to be part of the comparison: without it "Store" would claim
            // "StoreFront" as a child.
            return category.Length > root.Length
                && category[root.Length] == Separator
                && category.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
    }
}
