# LocalizationKit API

Every public type, with the exact signature. Namespace is `LocalizationKit` for runtime and
`LocalizationKit.Editor` for editor-only types.

---

## `L` — the short facade

All of it forwards to `Localization`. Use whichever reads better.

```csharp
string  L.T(string key)                                  // no allocation
string  L.T(string key, object a0)                       // {0} — allocates
string  L.T(string key, object a0, object a1)
string  L.T(string key, params object[] args)
string  L.Of(string languageCode, string key)            // read a non-active language
bool    L.Has(string key)

LocalizationHandle L.Bind(string key)                    // pre-resolve
string  L.Read(ref LocalizationHandle handle)            // fastest read

string  L.Language          { get; }                     // "fr", or null
LanguageInfo L.Current      { get; }
IReadOnlyList<LanguageInfo> L.Languages { get; }
bool    L.IsRtl             { get; }
bool    L.Ready             { get; }

bool    L.Set(string languageCode)
bool    L.Set(int languageIndex)
bool    L.Next()                                         // cycle, wrapping
event Action L.Changed
```

There is **no** `L.T(category, key)` overload — it would be ambiguous with `L.T(key, arg0)`.
Use a full `"Category/Key"`.

## `Localization` — the full facade

```csharp
// lifecycle
void Localization.Initialize()                           // runs automatically before first scene
void Localization.Initialize(LocalizationSettings settings)
void Localization.SetTable(LocalizationTable table, string languageCode = null)
void Localization.Reload()
void Localization.Reset()                                // tests / disabled domain reload

// state
LocalizationTable Table { get; }
bool     IsInitialized  { get; }
string   LanguageCode   { get; }
LanguageInfo Language   { get; }
int      LanguageIndex  { get; }
IReadOnlyList<LanguageInfo> Languages { get; }
bool     IsRightToLeft  { get; }

// language
bool SetLanguage(string code)
bool SetLanguage(int languageIndex)
bool HasLanguage(string code)

// lookup
string Get(string fullKey)                               // no allocation
string Get(string category, string key)
string GetIn(string languageCode, string fullKey)
bool   HasKey(string fullKey)
LocalizationHandle Resolve(string fullKey)
string GetValue(ref LocalizationHandle handle)           // re-resolves if the table was swapped

// formatting — all allocate
string Format(string fullKey, object arg0)
string Format(string fullKey, object arg0, object arg1)
string Format(string fullKey, params object[] args)

event Action LanguageChanged                             // after every bound object refreshed
event Action TableChanged                                // the table itself was replaced
```

## Attributes

```csharp
[Localized(string key)]                  // on a string field; class must be partial
[LocalizationKey]                        // on a string field; inspector picker
[LocalizationKey(string category, bool allowMissing = false)]
```

`[Localized]` fields must be instance, writable and of type `string`.

Generated onto the class: `ApplyLocalization()`, `EnableLocalization()`,
`DisableLocalization()`, and `partial void OnLocalizationApplied()` you may implement.
`OnEnable`/`OnDisable` are generated **only** for a `MonoBehaviour` that declares neither.

## Components

```csharp
// abstract base — both components below share these
abstract class LocalizedTextBase : MonoBehaviour, ILocalizedObject
    string             Key   { get; set; }        // assigning re-resolves and reapplies
    LocalizedTextCase  Case  { get; set; }        // AsAuthored | Upper | Lower
    bool ApplyRightToLeftAlignment { get; set; }
    string ReadCurrentText()
    void   ApplyLocalization()

sealed class LocalizedText    : LocalizedTextBase      // uGUI; Text Target { get; }
sealed class LocalizedTMPText : LocalizedTextBase      // TMP;  TMP_Text Target { get; set; }
    FontOverride[] FontOverrides { get; set; }         // { string LanguageCode; TMP_FontAsset Font; }

sealed class LocalizedStringEvent : MonoBehaviour, ILocalizedObject
    string Key { get; set; }
    UnityEvent<string> OnLocalized { get; }
```

Extension methods:

```csharp
LocalizedText    Text.Localize(string key)          // attaches the component and binds
LocalizedTMPText TMP_Text.Localize(string key)
void Text.SetLocalized(string key, params object[] args)      // one-shot, no binding
void TMP_Text.SetLocalized(string key, params object[] args)
```

`LocalizedText` lives in assembly `LocalizationKit.uGUI`, `LocalizedTMPText` in
`LocalizationKit.TextMeshPro`. Both are gated on `com.unity.ugui` — reference the right
assembly from your asmdef.

## Catalog (authoring — editor and tools, never a hot path)

```csharp
sealed class LocalizationCatalog : ScriptableObject
    IReadOnlyList<LanguageInfo>        Languages  { get; }
    IReadOnlyList<LocalizationCategory> Categories { get; }
    string DefaultLanguageCode { get; set; }
    int    EntryCount          { get; }

    int  IndexOfLanguage(string code)
    int  AddLanguage(LanguageInfo language)        // widens every entry
    bool RemoveLanguage(string code)               // drops that column from every entry
    void MoveLanguage(int from, int to)            // carries the column with it
    void SetLanguage(int index, LanguageInfo language)

    LocalizationCategory FindCategory(string name)
    LocalizationCategory GetOrAddCategory(string name)
    bool RemoveCategory(string name)
    void MoveCategory(int from, int to)

    LocalizationEntry AddEntry(string category, string key)
    bool RemoveEntry(string category, string key)
    LocalizationEntry FindByFullKey(string fullKey)
    List<string> GetAllKeys()
    void ResizeEntries()                           // repairs ragged arrays; idempotent

sealed class LocalizationCategory
    string Name, Description
    List<LocalizationEntry> Entries
    LocalizationEntry Find(string key)

sealed class LocalizationEntry
    string   Key, Description
    string[] Values                                // positional against catalog languages
    string GetValue(int languageIndex)
    void   SetValue(int languageIndex, string value)
    bool   IsMissing(int languageIndex)

struct LanguageInfo
    string Code, DisplayName
    SystemLanguage SystemLanguage
    bool RightToLeft
    LanguageInfo(string code, string displayName = null,
                 SystemLanguage systemLanguage = SystemLanguage.Unknown, bool rightToLeft = false)
```

## Table (runtime)

```csharp
sealed class LocalizationTable
    static LocalizationTable Build(LocalizationCatalog catalog,
                                   MissingKeyBehavior missingBehavior = MissingKeyBehavior.ReturnKey,
                                   string fallbackLanguageCode = null)
    static LocalizationTable Empty()

    IReadOnlyList<string>       Keys      { get; }
    IReadOnlyList<LanguageInfo> Languages { get; }
    int KeyCount, ActiveLanguageIndex, Version

    int    IndexOfLanguage(string code)
    bool   SelectLanguage(int languageIndex)
    int    IndexOf(string fullKey)
    bool   Contains(string fullKey)
    string GetValue(int keyIndex)
    string GetValue(int keyIndex, int languageIndex)
    string GetKey(int keyIndex)
```

## Binder

```csharp
interface ILocalizedObject { void ApplyLocalization(); }

static class LocalizationBinder
    LocalizationSubscription Register(ILocalizedObject target)   // applies immediately
    void Unregister(ref LocalizationSubscription subscription)   // O(1); safe twice
    void ApplyAll()
    void Clear()
    int  Count { get; }
```

## Remote providers

```csharp
interface ILocalizationProvider
    string DisplayName { get; }
    LocalizationProviderCapabilities Capabilities { get; }   // None | Fetch | Upload | Both
    void Fetch(Action<LocalizationFetchResult> onCompleted)  // callback runs EXACTLY once
    void Upload(LocalizationSnapshot snapshot, Action<LocalizationUploadResult> onCompleted)

abstract class LocalizationProviderAsset : ScriptableObject, ILocalizationProvider
    // derive from this for a provider configured in the inspector

readonly struct LocalizationFetchResult
    bool Success; LocalizationSnapshot Snapshot; string Error
    static Ok(snapshot) / Failed(error)

readonly struct LocalizationUploadResult
    bool Success; int RowsWritten; string Error
    static Ok(rowsWritten = 0) / Failed(error)

extension methods:  provider.CanFetch()   provider.CanUpload()

sealed class LocalizationProviderSource : ILocalizationSource   // adapter
```

Transport shape — plain data, no ScriptableObject, safe to build off the main thread:

```csharp
sealed class LocalizationSnapshot
    IReadOnlyList<LanguageInfo> Languages;  IReadOnlyList<Row> Rows
    int LanguageCount, RowCount;  bool IsEmpty
    string DefaultLanguageCode { get; set; }
    string SourceName { get; set; };  List<string> Warnings

    int  IndexOfLanguage(string code)
    int  AddLanguage(LanguageInfo language)          // widens every existing row
    Row  Find(string fullKey) / GetOrAddRow(string fullKey)
    string GetValue(string fullKey, string languageCode)
    bool   SetValue(string fullKey, string languageCode, string value)

    static LocalizationSnapshot FromCatalog(LocalizationCatalog catalog)
    static LocalizationSnapshot FromCsv(string csv, char delimiter = ',')       // null on failure
    static bool TryFromCsv(string csv, out snapshot, out error, char delimiter = ',')

    string             ToCsv(char delimiter = ',')
    LocalizationCatalog ToCatalog(string name = null)   // TRANSIENT — DestroyTransient it
    LocalizationTable   ToTable(MissingKeyBehavior = ReturnKey, string fallbackLanguageCode = null)

    static void DestroyTransient(LocalizationCatalog catalog)

    class Row
        string Key, Description; string[] Values
        string GetValue(int languageIndex);  void SetValue(int, string);  bool IsMissing(int)
```

Merging — one implementation, shared by CSV import, the Remote page and the build step:

```csharp
struct LocalizationMergeOptions
    bool AddNewKeys, AddNewLanguages, OverwriteExisting, RemoveKeysNotIncoming
    static Default     // add keys, overwrite, ignore unknown languages, delete nothing
    static FillBlanks  // never overwrite — the safe way to accept a translation pass back
    static Mirror      // make the target match exactly, deletions included

sealed class LocalizationMergeReport
    int RowsRead, AddedKeys, UpdatedValues, SkippedKeys, RemovedKeys
    List<string> AddedLanguages, IgnoredLanguages, Warnings
    bool ChangedAnything;  string Summary();  string ShortSummary()

static class LocalizationMerge
    LocalizationMergeReport Into(LocalizationCatalog target, snapshot, options)     // mutates
    LocalizationMergeReport Preview(LocalizationCatalog target, snapshot, options)  // does not
    LocalizationSnapshot    Merge(baseline, incoming, options, out report)
```

Fetching, caching, applying:

```csharp
static class LocalizationRemote
    ILocalizationProvider Provider { get }        // from the settings asset
    bool IsFetching;  DateTime LastFetchUtc;  string CachePath
    event Action<LocalizationSnapshot> Fetched;  event Action<string> FetchFailed

    void Fetch(provider, Action<LocalizationFetchResult> onCompleted)
    void FetchAndApply(provider = null, onCompleted = null, bool cache = true)
    void Apply(LocalizationSnapshot snapshot)     // keeps the active language when it exists
    bool ApplyCached();  bool TryLoadCache(out snapshot);  void WriteCache(s);  void ClearCache()
    void Upload(provider, snapshot, onCompleted);  void UploadCatalog(provider, catalog, onCompleted)
    void MergeAndUpload(provider, catalog, options, onCompleted)   // fetch, merge, then write
    void Reset()
```

HTTP that works in a player, in the editor, and under `-batchmode`:

```csharp
static class LocalizationWeb
    bool Blocking { get; set; }        // defaults to Application.isBatchMode; editor-only effect
    void Get(url, Action<LocalizationWebResponse> onCompleted, headers = null, timeoutSeconds = 30)
    void Post(url, body, contentType, onCompleted, headers = null, timeoutSeconds = 30)
    bool WaitForPendingRequests(float timeoutSeconds = 120f)
    const string FormContentType = "application/x-www-form-urlencoded"

sealed class LocalizationWebResponse
    bool Success; long StatusCode; string Text; string Error
```

Editor / CI (assembly `LocalizationKit.Editor`, namespace `LocalizationKit.Editor`):

```csharp
static class LocalizationRemoteSync
    bool Pull(catalog, provider, options, out report, out error)   // blocking
    bool Pull(out report, out error)                               // uses the settings asset
    bool Push(catalog, provider, out rowsWritten, out error)       // blocking; fetch-merge-upload
    void SyncFromRemote()          // -executeMethod entry point; EditorApplication.Exit(1) on failure
```

## Sources, CSV, settings

```csharp
interface ILocalizationSource
    string DisplayName { get; }
    void Load(Action<LocalizationTable> onCompleted, Action<string> onFailed)

sealed class LocalCatalogSource : ILocalizationSource
    LocalCatalogSource(LocalizationCatalog catalog, MissingKeyBehavior missingBehavior = ReturnKey)

static class LocalizationTableBuilder
    LocalizationTable  FromCsv(string csv, string defaultLanguage = null,
                               MissingKeyBehavior missingBehavior = ReturnKey, char delimiter = ',')
    LocalizationCatalog CatalogFromCsv(string csv, string defaultLanguage = null, char delimiter = ',')

static class LocalizationCsv
    ParseResult Parse(string text, char delimiter = ',')     // never throws; check .Failed
    string      Write(LocalizationCatalog catalog, char delimiter = ',')
    string      Write(LocalizationSnapshot snapshot, char delimiter = ',')

static class LocalizationKeys
    const char   Separator = '/'
    const string DefaultCategory = "Default"
    string Compose(string category, string key)
    bool   TrySplit(string fullKey, out string category, out string key)
    string CategoryOf(string fullKey)
    bool   IsValidName(string name)

sealed class LocalizationSettings : ScriptableObject
    const string ResourcePath = "LocalizationKitSettings"
    LocalizationCatalog Catalog { get; set; }
    bool InitializeOnLoad, RememberLanguage, LogMissingKeys
    StartupLanguageMode StartupLanguage        // RememberThenSystem | SystemLanguage | DefaultLanguage
    MissingKeyBehavior  MissingKeyBehavior     // ReturnKey | ReturnEmpty | ReturnMarker
    static LocalizationSettings Load()
```

## Editor-only

```csharp
static class LocalizationEditorCatalog          // LocalizationKit.Editor
    LocalizationCatalog Catalog  { get; }
    LocalizationSettings Settings { get; }
    string[] Keys { get; }
    event Action Changed

    void Invalidate()
    void InvalidateKeys()
    bool HasKey(string fullKey)
    List<string> KeysInCategory(string category)
    void Save(LocalizationCatalog catalog)       // SetDirty + save + refresh every surface
    void RecordUndo(LocalizationCatalog catalog, string what)
    LocalizationCatalog  CreateCatalog(string path)
    LocalizationSettings CreateSettings(LocalizationCatalog catalog)

class LocalizationKitWindow : EditorWindow
    static LocalizationKitWindow Open()
    static void OpenAt(string fullKey)
```

## Menu items

| Path | Does |
| --- | --- |
| Tools ▸ LocalizationKit ▸ Set Up Localization | Creates catalog + settings and links them |
| Tools ▸ LocalizationKit ▸ Localization Manager | The main window |
| Tools ▸ LocalizationKit ▸ Generate Key Constants | Writes the `LocKeys` class |
| Tools ▸ LocalizationKit ▸ Preview Language | Switch language in edit mode |
| Project Settings ▸ LocalizationKit | Runtime configuration |
