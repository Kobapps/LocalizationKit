using TMPro;

namespace LocalizationKit
{
    /// <summary>
    /// One-line localization for a <see cref="TMP_Text"/> you already have a reference to.
    /// </summary>
    public static class LocalizedTMPExtensions
    {
        /// <summary>
        /// Localizes this label and keeps it localized. Safe to call more than once — calling it
        /// again just re-points the same binding at a new key.
        /// </summary>
        /// <remarks>
        /// Attaches a <see cref="LocalizedTMPText"/> rather than assigning <c>text</c> once, so the
        /// label follows a language change instead of quietly keeping the string it was given.
        /// <code>
        /// titleLabel.Localize(LocKeys.Popups.Quit.Title);
        /// </code>
        /// </remarks>
        /// <returns>The component doing the work, for further configuration — per-language fonts, case.</returns>
        public static LocalizedTMPText Localize(this TMP_Text label, string key)
        {
            if (label == null) return null;

            var localized = label.GetComponent<LocalizedTMPText>();
            if (localized == null) localized = label.gameObject.AddComponent<LocalizedTMPText>();

            localized.Target = label;
            localized.Key = key;

            return localized;
        }

        /// <summary>
        /// Sets this label to a formatted localized string <b>once</b>.
        /// </summary>
        /// <remarks>
        /// No component is attached, because formatted arguments go stale as soon as the values
        /// behind them change. Call it again when they do.
        /// </remarks>
        public static void SetLocalized(this TMP_Text label, string key, params object[] args)
        {
            if (label == null) return;

            label.SetText(args == null || args.Length == 0
                ? Localization.Get(key)
                : Localization.Format(key, args));
        }
    }
}
