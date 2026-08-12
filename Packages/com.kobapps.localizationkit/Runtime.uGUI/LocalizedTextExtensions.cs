using UnityEngine.UI;

namespace LocalizationKit
{
    /// <summary>
    /// One-line localization for a uGUI <see cref="Text"/> you already have a reference to.
    /// </summary>
    public static class LocalizedTextExtensions
    {
        /// <summary>
        /// Localizes this label and keeps it localized. Safe to call more than once — calling it
        /// again just re-points the same binding at a new key.
        /// </summary>
        /// <remarks>
        /// This adds a <see cref="LocalizedText"/> component rather than assigning
        /// <c>text</c> once, which is the difference between a label that is localized and one that
        /// merely was. A plain assignment is correct until the player changes language, at which
        /// point it silently keeps the old string — the single most common way hand-rolled
        /// localization breaks.
        /// <code>
        /// buyLabel.Localize(LocKeys.Store.BuyButton);
        /// </code>
        /// </remarks>
        /// <returns>The component doing the work, for further configuration.</returns>
        public static LocalizedText Localize(this Text label, string key)
        {
            if (label == null) return null;

            var localized = label.GetComponent<LocalizedText>();
            if (localized == null) localized = label.gameObject.AddComponent<LocalizedText>();

            localized.Key = key;
            return localized;
        }

        /// <summary>
        /// Sets this label to a formatted localized string <b>once</b>.
        /// </summary>
        /// <remarks>
        /// Deliberately does not attach a component: the arguments would be stale the moment
        /// anything they came from changed, and a binding that silently shows an old score is worse
        /// than none. Call this again whenever the values change — including from
        /// <see cref="L.Changed"/> if the label must survive a language switch.
        /// </remarks>
        public static void SetLocalized(this Text label, string key, params object[] args)
        {
            if (label == null) return;

            label.text = args == null || args.Length == 0
                ? Localization.Get(key)
                : Localization.Format(key, args);
        }
    }
}
