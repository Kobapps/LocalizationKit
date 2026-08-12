# LocalizationKit patterns

Recipes for the things people actually ask for.

---

## Bulk-localize a project that has hard-coded strings

The fastest correct route is **CSV in, then replace usages** — not adding entries one at a
time through the window.

**1. Collect the strings and decide keys.** Key by meaning and screen, never by the English:

| Found | Key |
| --- | --- |
| `"Buy now"` on the shop button | `Store/BuyButton` |
| `"Quit, really?"` in the quit dialog | `Popups/Quit/Title` |
| `"Tap anywhere to begin"` | `Tutorials/Step1` |

**2. Write a CSV and import it** (Localization Manager ▸ Import & Export ▸ Import CSV).
First column is the key; every other column is a language code:

```
Key,en
Store/BuyButton,Buy now
Popups/Quit/Title,"Quit, really?"
Tutorials/Step1,Tap anywhere to begin
```

Leave the other languages out entirely at this stage — add the columns when translations
come back. Import merges; it never deletes.

**3. Generate `LocKeys`.** Tools ▸ LocalizationKit ▸ Generate Key Constants.

**4. Replace the usages.**

```csharp
// before
buyLabel.text = "Buy now";

// after — binds, so it follows a language change
buyLabel.Localize(LocKeys.Store.BuyButton);
```

For scene labels with authored text, the `LocalizedText` inspector's **"Create a Key From
This Text…"** does entry creation, seeding and assignment in one click.

**5. Export CSV and send it to the translators.** They fill new columns; import it back with
**Overwrite existing text** off so editor-side fixes survive.

### Doing step 2 from a script

When there are hundreds of strings, generate the catalog entries in an editor script:

```csharp
using LocalizationKit;
using LocalizationKit.Editor;
using UnityEditor;

internal static class SeedLocalization
{
    [MenuItem("Tools/Project/Seed Localization")]
    private static void Run()
    {
        var catalog = LocalizationEditorCatalog.Catalog;
        var en = catalog.IndexOfLanguage("en");

        void Add(string category, string key, string english)
        {
            var entry = catalog.AddEntry(category, key);
            entry.SetValue(en, english);
        }

        Add("Store", "BuyButton", "Buy now");
        Add("Popups/Quit", "Title", "Quit, really?");

        LocalizationEditorCatalog.Save(catalog);
    }
}
```

## A language picker

```csharp
public sealed class LanguageMenu : MonoBehaviour
{
    [SerializeField] private Dropdown m_Dropdown;

    private void Start()
    {
        m_Dropdown.ClearOptions();

        foreach (var language in L.Languages)
            m_Dropdown.options.Add(new Dropdown.OptionData(language.DisplayName));

        m_Dropdown.SetValueWithoutNotify(L.Languages.Count == 0 ? 0 : Localization.LanguageIndex);
        m_Dropdown.onValueChanged.AddListener(index => L.Set(index));
    }
}
```

The choice persists on its own when **Remember language** is on (the default) — don't write
your own PlayerPrefs key for it.

## Formatted and counted strings

`Format` allocates, so call it when the value changes, not every frame:

```csharp
// catalog: Hud/Score = "Score: {0}"
private void OnScoreChanged(int score) => m_Label.text = L.T(LocKeys.Hud.Score, score);
```

There is no built-in pluralization. For the common English/French case, use one key per form
and pick between them — explicit, and it survives translation better than a rule engine:

```csharp
// catalog: Hud/LivesOne = "1 life"   Hud/LivesMany = "{0} lives"
m_Label.text = lives == 1
    ? L.T(LocKeys.Hud.LivesOne)
    : L.T(LocKeys.Hud.LivesMany, lives);
```

Languages with more plural forms need one key per form for that language; keep the branch in
one helper rather than at every call site.

## Right-to-left and fonts

Mark the language right-to-left when you add it:

```csharp
catalog.AddLanguage(new LanguageInfo("he", "עברית", SystemLanguage.Hebrew, rightToLeft: true));
```

Then, per label that needs it, tick **Apply Right To Left Alignment**. Leave it off when a
parent layout is already mirrored, or the label gets flipped twice.

**Fonts matter more than the flag.** A TMP font asset carries a baked glyph set, so a Latin
font renders Hebrew, Arabic or Japanese as blank squares and logs nothing. On
`LocalizedTMPText`, add a `FontOverrides` entry per language that needs a different face:

```csharp
localized.FontOverrides = new[]
{
    new LocalizedTMPText.FontOverride { LanguageCode = "he", Font = hebrewFont },
    new LocalizedTMPText.FontOverride { LanguageCode = "ja", Font = japaneseFont },
};
```

Languages not listed keep the authored font.

## Remote catalog (Google Sheets)

Publish the sheet: **File ▸ Share ▸ Publish to web ▸ Comma-separated values**. Then:

```csharp
using UnityEngine.Networking;

private IEnumerator LoadRemote(string url)
{
    using var request = UnityWebRequest.Get(url);
    yield return request.SendWebRequest();

    if (request.result != UnityWebRequest.Result.Success)
        yield break;                                   // keep the local table

    var table = LocalizationTableBuilder.FromCsv(request.downloadHandler.text, defaultLanguage: "en");
    if (table.KeyCount == 0) yield break;              // never install an empty table

    Localization.SetTable(table, L.Language);          // keep the player's language
}
```

Every `[Localized]` field and every localized component refreshes itself. Handles held
anywhere re-resolve on their next read. **No calling code changes.**

Two rules: keep the local catalog as the shipped fallback so a failed fetch degrades to
working text rather than to keys, and validate before installing — an empty or truncated
download that becomes the live table turns the whole UI into key names.

## Reading in a hot path

```csharp
private LocalizationHandle m_Handle;

private void Awake()   => m_Handle = L.Bind(LocKeys.Hud.Score);
private void Update()  => m_Label.text = L.Read(ref m_Handle);
```

`L.T(key)` is already just a dictionary probe and an array index; `L.Bind`/`L.Read` removes
the probe. Neither allocates. Use the handle form when the same key is read every frame.

## Localizing something the kit ships no component for

Inspector-wired, no code:

```
LocalizedStringEvent → OnLocalized(string) → YourComponent.SetCaption
```

Or implement the interface:

```csharp
public sealed class Billboard : MonoBehaviour, ILocalizedObject
{
    private LocalizationSubscription m_Subscription;
    private LocalizationHandle m_Handle;

    public void ApplyLocalization() => Render(L.Read(ref m_Handle));

    private void OnEnable()
    {
        m_Handle = L.Bind(LocKeys.Signs.Welcome);
        m_Subscription = LocalizationBinder.Register(this);   // applies immediately
    }

    private void OnDisable() => LocalizationBinder.Unregister(ref m_Subscription);
}
```

## Tests

`Localization` is static, so reset it between tests or one test's table leaks into the next:

```csharp
[SetUp]    public void SetUp()    => Localization.Reset();
[TearDown] public void TearDown() => Localization.Reset();
```

Build a table without any assets on disk:

```csharp
var table = LocalizationTableBuilder.FromCsv("Key,en,fr\nStore/Buy,Buy,Acheter\n");
Localization.SetTable(table, "en");
```
