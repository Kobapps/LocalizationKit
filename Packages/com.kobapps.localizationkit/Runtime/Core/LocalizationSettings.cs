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
        [SerializeField, LocalizationKey] private string m_AppNameKey;
        [SerializeField] private bool m_DeclareLanguagesToOS = true;
        [SerializeField] private LocalizationProviderAsset m_RemoteProvider;
        [SerializeField] private bool m_FetchRemoteOnStartup;
        [SerializeField] private bool m_UseRemoteCache = true;
        [SerializeField] private bool m_SyncRemoteBeforeBuild;
        [SerializeField] private LocalizationMergeOptions m_RemoteMergeOptions = LocalizationMergeOptions.Default;

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

        /// <summary>
        /// Key whose text is used as the application's name on the device — the label under the
        /// icon. Blank leaves the platform's own product name in place.
        /// </summary>
        /// <remarks>
        /// Read at build time, not at run time: the home-screen label belongs to the OS and is
        /// fixed when the app is packaged. Android takes it from <c>res/values-&lt;code&gt;</c> and
        /// iOS from <c>&lt;code&gt;.lproj/InfoPlist.strings</c>, both written by the kit's build
        /// post-processors.
        /// </remarks>
        public string AppNameKey
        {
            get => m_AppNameKey;
            set => m_AppNameKey = value;
        }

        /// <summary>
        /// Whether a build tells the OS which languages the app supports.
        /// </summary>
        /// <remarks>
        /// This is not cosmetic on iOS. The system reports a device's language to an app only for
        /// languages the app declares in <c>CFBundleLocalizations</c>; for anything else it reports
        /// the development region. Without the declaration <see cref="Application.systemLanguage"/>
        /// answers "English" on a French phone, and
        /// <see cref="StartupLanguageMode.SystemLanguage"/> silently never matches — in a build
        /// only, which is the worst place to find out.
        /// <para>
        /// It also drives what the App Store and Google Play list as the app's languages.
        /// </para>
        /// </remarks>
        public bool DeclareLanguagesToOS
        {
            get => m_DeclareLanguagesToOS;
            set => m_DeclareLanguagesToOS = value;
        }

        /// <summary>
        /// Where translations come from when they do not come from the catalog asset — a
        /// spreadsheet, a CDN, a translation service. Null for a project that ships its strings.
        /// </summary>
        /// <remarks>
        /// A provider referenced here ships in the build, which is what makes a runtime refresh
        /// possible and also what makes write credentials in one a bad idea. See
        /// <see cref="LocalizationProviderAsset"/>.
        /// </remarks>
        public LocalizationProviderAsset RemoteProvider
        {
            get => m_RemoteProvider;
            set => m_RemoteProvider = value;
        }

        /// <summary>
        /// Whether the kit asks <see cref="RemoteProvider"/> for fresh strings as it starts.
        /// </summary>
        /// <remarks>
        /// The fetch does not block startup. The catalog — or the cached copy of the last fetch —
        /// is installed first and the game runs on it; the remote's answer replaces it whenever it
        /// arrives, and every bound field and component refreshes on its own. A fetch that fails
        /// leaves what was already there.
        /// </remarks>
        public bool FetchRemoteOnStartup
        {
            get => m_FetchRemoteOnStartup;
            set => m_FetchRemoteOnStartup = value;
        }

        /// <summary>
        /// Whether a successful fetch is written to disk and used on the next launch before the
        /// network answers.
        /// </summary>
        /// <remarks>
        /// Worth leaving on. Without it, every cold start that begins offline begins in whatever
        /// the build shipped with, however long ago that was.
        /// </remarks>
        public bool UseRemoteCache
        {
            get => m_UseRemoteCache;
            set => m_UseRemoteCache = value;
        }

        /// <summary>
        /// Whether a build fetches the remote into the catalog before it starts.
        /// </summary>
        /// <remarks>
        /// This is what makes a build machine produce current strings. The catalog asset is what
        /// ships inside the player, so a build made on CI from a checkout that is a week old ships
        /// week-old text unless something pulls first — and a runtime fetch does not help the first
        /// frame, or a player who is offline. The build stops if the fetch fails, on the grounds
        /// that shipping stale text silently is the thing this setting exists to prevent.
        /// </remarks>
        public bool SyncRemoteBeforeBuild
        {
            get => m_SyncRemoteBeforeBuild;
            set => m_SyncRemoteBeforeBuild = value;
        }

        /// <summary>
        /// What a fetch is allowed to do to the catalog when it is merged in.
        /// </summary>
        /// <remarks>
        /// Kept here, in a version-controlled asset, rather than in the editor window, so that a
        /// sync run on a build machine applies the same policy as one run by a person. A merge
        /// policy that differs per machine is a merge policy nobody can reason about.
        /// </remarks>
        public LocalizationMergeOptions RemoteMergeOptions
        {
            get => m_RemoteMergeOptions;
            set => m_RemoteMergeOptions = value;
        }

        /// <summary>Loads the settings asset from <c>Resources</c>, or null when there is none.</summary>
        public static LocalizationSettings Load() => Resources.Load<LocalizationSettings>(ResourcePath);
    }
}
