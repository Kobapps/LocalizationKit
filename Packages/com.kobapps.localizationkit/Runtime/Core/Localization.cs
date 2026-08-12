using System;
using System.Collections.Generic;
using UnityEngine;

namespace LocalizationKit
{
    /// <summary>
    /// The entry point: what language is active, what a key says, and when that changes.
    /// </summary>
    /// <remarks>
    /// Static because there is exactly one active language in a process, and threading a service
    /// through every label that wants a string buys nothing. The state behind it is swappable —
    /// <see cref="SetTable"/> takes a table from anywhere, which is how a remote catalog will drop
    /// in without a line of calling code changing.
    /// <para>
    /// <b>Cost.</b> <see cref="Get(string)"/> is one dictionary probe and one array index.
    /// <see cref="GetValue(ref LocalizationHandle)"/> skips the probe. Neither allocates: the
    /// returned string is the one already sitting in the table, not a copy.
    /// </para>
    /// </remarks>
    public static class Localization
    {
        private static LocalizationTable s_Table = LocalizationTable.Empty();
        private static LocalizationSettings s_Settings;
        private static bool s_Initialized;

        /// <summary>
        /// Raised after the active language changes and after every registered object has been
        /// refreshed. Subscribe for things the binder does not cover — a font swap, a layout
        /// mirror for a right-to-left language, an analytics ping.
        /// </summary>
        public static event Action LanguageChanged;

        /// <summary>Raised when the table itself is replaced, which invalidates raw key indices.</summary>
        public static event Action TableChanged;

        /// <summary>The table currently being read. Never null.</summary>
        public static LocalizationTable Table => s_Table;

        /// <summary>True once a table has been installed, whether or not it carries anything.</summary>
        public static bool IsInitialized => s_Initialized;

        /// <summary>Code of the active language, or null when there is none.</summary>
        public static string LanguageCode
        {
            get
            {
                var index = s_Table.ActiveLanguageIndex;
                return (uint)index < (uint)s_Table.Languages.Count ? s_Table.Languages[index].Code : null;
            }
        }

        /// <summary>Metadata for the active language. Default when there is none.</summary>
        public static LanguageInfo Language
        {
            get
            {
                var index = s_Table.ActiveLanguageIndex;
                return (uint)index < (uint)s_Table.Languages.Count ? s_Table.Languages[index] : default;
            }
        }

        /// <summary>Position of the active language in <see cref="Languages"/>, or -1.</summary>
        public static int LanguageIndex => s_Table.ActiveLanguageIndex;

        /// <summary>Every language the active table carries.</summary>
        public static IReadOnlyList<LanguageInfo> Languages => s_Table.Languages;

        /// <summary>True when the active language reads right to left.</summary>
        public static bool IsRightToLeft => Language.RightToLeft;

        // ---------------------------------------------------------------- lifecycle

        /// <summary>
        /// Builds the table from the settings asset and picks the starting language. Runs before
        /// the first scene loads unless the settings asset opts out; calling it again is a no-op.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Initialize()
        {
            if (s_Initialized) return;

            var settings = LocalizationSettings.Load();
            if (settings == null || !settings.InitializeOnLoad) return;

            Initialize(settings);
        }

        /// <summary>
        /// Brings the kit up against explicit settings. Use this when the catalog is not available
        /// before the first scene, having turned <c>InitializeOnLoad</c> off.
        /// </summary>
        public static void Initialize(LocalizationSettings settings)
        {
            if (settings == null) return;

            s_Settings = settings;

            var table = LocalizationTable.Build(settings.Catalog, settings.MissingKeyBehavior);
            SetTable(table, ResolveStartupLanguage(settings, table));
        }

        /// <summary>
        /// Installs a table, optionally selecting a language as part of the same swap.
        /// </summary>
        /// <remarks>
        /// This is the seam a remote source plugs into: build a table from wherever the strings
        /// came from, hand it over, and every bound object refreshes. Existing handles notice the
        /// version change and re-resolve themselves on their next read.
        /// </remarks>
        public static void SetTable(LocalizationTable table, string languageCode = null)
        {
            s_Table = table ?? LocalizationTable.Empty();
            s_Initialized = true;

            if (!string.IsNullOrEmpty(languageCode))
            {
                var index = s_Table.IndexOfLanguage(languageCode);
                if (index >= 0) s_Table.SelectLanguage(index);
            }

            TableChanged?.Invoke();
            LocalizationBinder.ApplyAll();
            LanguageChanged?.Invoke();
        }

        /// <summary>Rebuilds the table from the current settings. For a catalog edited at runtime.</summary>
        public static void Reload()
        {
            var settings = s_Settings != null ? s_Settings : LocalizationSettings.Load();
            if (settings == null) return;

            var keep = LanguageCode;
            s_Settings = settings;

            var table = LocalizationTable.Build(settings.Catalog, settings.MissingKeyBehavior);
            SetTable(table, keep ?? ResolveStartupLanguage(settings, table));
        }

        /// <summary>
        /// Drops all state. For tests, and for play mode with domain reload disabled — statics
        /// otherwise carry a previous session's table and binder registrations into the next one.
        /// </summary>
        public static void Reset()
        {
            s_Table = LocalizationTable.Empty();
            s_Settings = null;
            s_Initialized = false;
            LanguageChanged = null;
            TableChanged = null;
            LocalizationBinder.Clear();
        }

        // ---------------------------------------------------------------- language

        /// <summary>
        /// Switches language, refreshes every registered object and raises
        /// <see cref="LanguageChanged"/>. Returns false for an unknown code or the current one.
        /// </summary>
        public static bool SetLanguage(string code)
        {
            var index = s_Table.IndexOfLanguage(code);
            if (index < 0)
            {
                WarnUnknownLanguage(code);
                return false;
            }

            return SetLanguage(index);
        }

        /// <summary>Switches language by position. Returns false when out of range or unchanged.</summary>
        public static bool SetLanguage(int languageIndex)
        {
            if (!s_Table.SelectLanguage(languageIndex)) return false;

            if (s_Settings != null && s_Settings.RememberLanguage)
            {
                PlayerPrefs.SetString(LocalizationSettings.LanguagePrefsKey, LanguageCode);
                PlayerPrefs.Save();
            }

            LocalizationBinder.ApplyAll();
            LanguageChanged?.Invoke();

            return true;
        }

        /// <summary>True when the active table carries this language.</summary>
        public static bool HasLanguage(string code) => s_Table.IndexOfLanguage(code) >= 0;

        // ---------------------------------------------------------------- lookup

        /// <summary>
        /// Text for a full <c>Category/Key</c> in the active language. Allocates nothing; a key the
        /// table does not carry resolves per the configured <see cref="MissingKeyBehavior"/>.
        /// </summary>
        public static string Get(string fullKey)
        {
            var index = s_Table.IndexOf(fullKey);
            if (index < 0)
            {
                WarnMissingKey(fullKey);
                return MissingValue(fullKey);
            }

            return s_Table.GetValue(index);
        }

        /// <summary>Text for a category and key, without composing the full key yourself.</summary>
        public static string Get(string category, string key) => Get(LocalizationKeys.Compose(category, key));

        /// <summary>
        /// Looks a key up once so later reads can skip the dictionary. Safe to call before the
        /// table is ready — the handle re-resolves itself on first use.
        /// </summary>
        public static LocalizationHandle Resolve(string fullKey) =>
            new LocalizationHandle(fullKey, s_Table.IndexOf(fullKey), s_Table.Version);

        /// <summary>
        /// Text through a handle: a version check and an array index. Re-resolves transparently
        /// when the table has been replaced since the handle was made.
        /// </summary>
        public static string GetValue(ref LocalizationHandle handle)
        {
            if (handle.m_Version != s_Table.Version)
            {
                handle.m_Index = s_Table.IndexOf(handle.m_Key);
                handle.m_Version = s_Table.Version;
            }

            if (handle.m_Index < 0)
                return MissingValue(handle.m_Key);

            return s_Table.GetValue(handle.m_Index);
        }

        /// <summary>Text for a key in a language other than the active one. For side-by-side previews.</summary>
        public static string GetIn(string languageCode, string fullKey)
        {
            var language = s_Table.IndexOfLanguage(languageCode);
            var key = s_Table.IndexOf(fullKey);

            if (language < 0 || key < 0) return MissingValue(fullKey);

            return s_Table.GetValue(key, language);
        }

        /// <summary>True when the active table carries this key.</summary>
        public static bool HasKey(string fullKey) => s_Table.Contains(fullKey);

        // ---------------------------------------------------------------- formatting

        /// <summary>
        /// Text for a key with <c>{0}</c>-style arguments substituted.
        /// </summary>
        /// <remarks>
        /// This allocates — composing a new string is the whole point. Every other read on this
        /// class does not, so keep formatted lookups off per-frame paths, or format once into a
        /// field and re-use it.
        /// </remarks>
        public static string Format(string fullKey, object arg0) => string.Format(Get(fullKey), arg0);

        /// <inheritdoc cref="Format(string, object)"/>
        public static string Format(string fullKey, object arg0, object arg1) => string.Format(Get(fullKey), arg0, arg1);

        /// <inheritdoc cref="Format(string, object)"/>
        public static string Format(string fullKey, params object[] args) => string.Format(Get(fullKey), args);

        // ---------------------------------------------------------------- internals

        private static string MissingValue(string fullKey)
        {
            var behavior = s_Settings != null ? s_Settings.MissingKeyBehavior : MissingKeyBehavior.ReturnKey;

            switch (behavior)
            {
                case MissingKeyBehavior.ReturnEmpty: return string.Empty;
                case MissingKeyBehavior.ReturnMarker: return string.Concat("#", fullKey, "#");
                default: return fullKey ?? string.Empty;
            }
        }

        private static string ResolveStartupLanguage(LocalizationSettings settings, LocalizationTable table)
        {
            switch (settings.StartupLanguage)
            {
                case StartupLanguageMode.DefaultLanguage:
                    return settings.Catalog != null ? settings.Catalog.DefaultLanguageCode : null;

                case StartupLanguageMode.SystemLanguage:
                    return MatchSystemLanguage(table) ?? DefaultOf(settings);

                default:
                    var remembered = settings.RememberLanguage
                        ? PlayerPrefs.GetString(LocalizationSettings.LanguagePrefsKey, null)
                        : null;

                    if (!string.IsNullOrEmpty(remembered) && table.IndexOfLanguage(remembered) >= 0)
                        return remembered;

                    return MatchSystemLanguage(table) ?? DefaultOf(settings);
            }
        }

        private static string DefaultOf(LocalizationSettings settings) =>
            settings.Catalog != null ? settings.Catalog.DefaultLanguageCode : null;

        private static string MatchSystemLanguage(LocalizationTable table)
        {
            var system = Application.systemLanguage;
            var languages = table.Languages;

            for (var i = 0; i < languages.Count; i++)
            {
                if (languages[i].SystemLanguage == system && system != SystemLanguage.Unknown)
                    return languages[i].Code;
            }

            return null;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private static void WarnMissingKey(string fullKey)
        {
            if (s_Settings != null && !s_Settings.LogMissingKeys) return;
            if (!s_Initialized) return;

            Debug.LogWarning($"[LocalizationKit] No entry for key '{fullKey}'.");
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private static void WarnUnknownLanguage(string code)
        {
            Debug.LogWarning($"[LocalizationKit] No language '{code}' in the active catalog.");
        }
    }
}
