using System;
using System.Collections.Generic;

namespace LocalizationKit
{
    /// <summary>
    /// The short way to say everything. <c>L.T("Store/Buy")</c> instead of
    /// <c>Localization.Get("Store/Buy")</c>.
    /// </summary>
    /// <remarks>
    /// A one-letter type is a deliberate exception to normal naming. A localized string appears
    /// dozens of times in a line of UI code, and at that density the call has to disappear into the
    /// expression — <c>L.T(k)</c> reads as "the text", where a longer name reads as a function call
    /// and crowds out the thing being said.
    /// <para>
    /// Nothing is hidden behind it: every member forwards straight to <see cref="Localization"/>,
    /// which stays the documented API. Use whichever suits the file. If <c>L</c> collides with
    /// something in your own code, just don't <c>using LocalizationKit</c> there — nothing else
    /// depends on it.
    /// </para>
    /// <code>
    /// buyLabel.text   = L.T(LocKeys.Store.BuyButton);
    /// priceLabel.text = L.T(LocKeys.Store.Price, coins);
    ///
    /// if (L.Set("fr")) { /* switched */ }
    /// L.Next();                     // cycle, for a debug button
    /// </code>
    /// </remarks>
    public static class L
    {
        /// <summary>Text for a key in the active language. No allocation.</summary>
        public static string T(string key) => Localization.Get(key);

        /// <summary>Text for a key with one <c>{0}</c> argument. Allocates, like any format.</summary>
        public static string T(string key, object arg0) => Localization.Format(key, arg0);

        /// <inheritdoc cref="T(string, object)"/>
        public static string T(string key, object arg0, object arg1) => Localization.Format(key, arg0, arg1);

        /// <inheritdoc cref="T(string, object)"/>
        public static string T(string key, params object[] args) => Localization.Format(key, args);

        // There is deliberately no T(category, key) overload: it is indistinguishable from
        // T(key, arg0) at the call site, and the compiler would silently pick the format one.
        // Use a full "Category/Key" — which is what LocKeys hands you anyway.

        /// <summary>Text for a key in a language other than the active one. For side-by-side previews.</summary>
        public static string Of(string languageCode, string key) => Localization.GetIn(languageCode, key);

        /// <summary>True when the catalog carries this key. Worth checking before showing a key to a player.</summary>
        public static bool Has(string key) => Localization.HasKey(key);

        /// <summary>
        /// Pre-resolves a key so later reads skip the dictionary. Store the result and read it with
        /// <see cref="Read"/> — this is the form to use in <c>Update</c>.
        /// </summary>
        public static LocalizationHandle Bind(string key) => Localization.Resolve(key);

        /// <summary>Text through a pre-resolved handle: a version check and an array index.</summary>
        public static string Read(ref LocalizationHandle handle) => Localization.GetValue(ref handle);

        /// <summary>Code of the active language, e.g. <c>fr</c>. Null when no catalog is loaded.</summary>
        public static string Language => Localization.LanguageCode;

        /// <summary>Metadata for the active language — display name, direction, device mapping.</summary>
        public static LanguageInfo Current => Localization.Language;

        /// <summary>Every language the loaded catalog carries.</summary>
        public static IReadOnlyList<LanguageInfo> Languages => Localization.Languages;

        /// <summary>True when the active language reads right to left.</summary>
        public static bool IsRtl => Localization.IsRightToLeft;

        /// <summary>True once a catalog has been loaded.</summary>
        public static bool Ready => Localization.IsInitialized;

        /// <summary>Switches language by code. False for an unknown code or the current one.</summary>
        public static bool Set(string languageCode) => Localization.SetLanguage(languageCode);

        /// <summary>Switches language by position in <see cref="Languages"/>.</summary>
        public static bool Set(int languageIndex) => Localization.SetLanguage(languageIndex);

        /// <summary>
        /// Moves to the next language, wrapping. For a debug key or a "try it" button — a real
        /// language picker should list the languages rather than make people cycle.
        /// </summary>
        public static bool Next()
        {
            var count = Localization.Languages.Count;
            if (count == 0) return false;

            return Localization.SetLanguage((Localization.LanguageIndex + 1) % count);
        }

        /// <summary>
        /// Raised after the language changes and every bound object has been refreshed.
        /// </summary>
        /// <remarks>
        /// Only needed for things the binder does not cover — a font swap, a mirrored layout, an
        /// analytics ping. Fields marked <c>[Localized]</c> and the shipped components already
        /// update themselves; subscribing to re-read them would be redundant work.
        /// </remarks>
        public static event Action Changed
        {
            add => Localization.LanguageChanged += value;
            remove => Localization.LanguageChanged -= value;
        }
    }
}
