namespace LocalizationKit
{
    /// <summary>
    /// What a key with no text in any language resolves to. Applied while the table is built,
    /// so the choice costs nothing at read time.
    /// </summary>
    public enum MissingKeyBehavior
    {
        /// <summary>Yield the key itself. Readable in a screenshot and searchable in the catalog.</summary>
        ReturnKey = 0,

        /// <summary>Yield an empty string. For UI where a visible key would be worse than a gap.</summary>
        ReturnEmpty = 1,

        /// <summary>Yield <c>#Category/Key#</c>. Unmissable during a translation pass.</summary>
        ReturnMarker = 2,
    }

    /// <summary>How the language is chosen the first time the runtime comes up.</summary>
    public enum StartupLanguageMode
    {
        /// <summary>Use the last language the player chose; fall back to the device, then the default.</summary>
        RememberThenSystem = 0,

        /// <summary>Match <see cref="UnityEngine.Application.systemLanguage"/>; fall back to the default.</summary>
        SystemLanguage = 1,

        /// <summary>Always start on the catalog's default language.</summary>
        DefaultLanguage = 2,
    }
}
