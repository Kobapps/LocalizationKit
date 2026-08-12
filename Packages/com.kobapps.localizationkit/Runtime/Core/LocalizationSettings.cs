using UnityEngine;

namespace LocalizationKit
{
    /// <summary>
    /// Project-wide runtime configuration, loaded from <c>Resources</c> so the kit can bring itself
    /// up before the first scene without any bootstrap code in the game.
    /// </summary>
    /// <remarks>
    /// The asset lives at <c>Assets/Resources/LocalizationKitSettings.asset</c>. It is optional:
    /// with no settings asset the kit stays dormant and every lookup returns its key, which is a
    /// visible but non-fatal state. The editor window offers to create it.
    /// </remarks>
    public sealed class LocalizationSettings : ScriptableObject
    {
        /// <summary>Path within <c>Resources</c> the runtime loads this from, without an extension.</summary>
        public const string ResourcePath = "LocalizationKitSettings";

        /// <summary>Asset name, used by the editor when it creates the settings asset.</summary>
        public const string AssetName = "LocalizationKitSettings";

        /// <summary>PlayerPrefs key the chosen language is remembered under.</summary>
        public const string LanguagePrefsKey = "LocalizationKit.Language";

        [SerializeField] private LocalizationCatalog m_Catalog;
        [SerializeField] private bool m_InitializeOnLoad = true;
        [SerializeField] private StartupLanguageMode m_StartupLanguage = StartupLanguageMode.RememberThenSystem;
        [SerializeField] private bool m_RememberLanguage = true;
        [SerializeField] private MissingKeyBehavior m_MissingKeyBehavior = MissingKeyBehavior.ReturnKey;
        [SerializeField] private bool m_LogMissingKeys = true;

        /// <summary>The catalog the runtime builds its table from.</summary>
        public LocalizationCatalog Catalog
        {
            get => m_Catalog;
            set => m_Catalog = value;
        }

        /// <summary>
        /// Whether the kit initialises itself before the first scene loads. Turn this off to
        /// control the timing yourself with <see cref="Localization.Initialize"/> — worth doing when
        /// the catalog comes from somewhere that is not ready that early.
        /// </summary>
        public bool InitializeOnLoad
        {
            get => m_InitializeOnLoad;
            set => m_InitializeOnLoad = value;
        }

        /// <summary>How the first language of a session is picked.</summary>
        public StartupLanguageMode StartupLanguage
        {
            get => m_StartupLanguage;
            set => m_StartupLanguage = value;
        }

        /// <summary>Whether a language change is written to <c>PlayerPrefs</c>.</summary>
        public bool RememberLanguage
        {
            get => m_RememberLanguage;
            set => m_RememberLanguage = value;
        }

        /// <summary>What a key with no text anywhere resolves to.</summary>
        public MissingKeyBehavior MissingKeyBehavior
        {
            get => m_MissingKeyBehavior;
            set => m_MissingKeyBehavior = value;
        }

        /// <summary>
        /// Whether a lookup for a key the catalog does not carry writes a warning. On in the editor
        /// and development builds; compiled out of release builds either way.
        /// </summary>
        public bool LogMissingKeys
        {
            get => m_LogMissingKeys;
            set => m_LogMissingKeys = value;
        }

        /// <summary>Loads the settings asset from <c>Resources</c>, or null when there is none.</summary>
        public static LocalizationSettings Load() => Resources.Load<LocalizationSettings>(ResourcePath);
    }
}
