# LocalizationKit

Localization for Unity that costs nothing per frame and almost nothing to adopt.

Mark a field, drop a component, or call one method. Keys live in a catalog asset you edit in a
proper window; a language change reaches every bound field and label on its own.

Open **Tools ▸ LocalizationKit ▸ Localization Manager** to create a catalog and start.

---

## Install

Add from a git URL in the Package Manager:

```
https://github.com/Kobapps/LocalizationKit.git?path=/Packages/com.kobapps.localizationkit
```

The editor window is built on [EditorCoreKit](https://github.com/Kobapps/EditorCoreKit), which is
**not** resolved automatically — UPM does not resolve git dependencies transitively, so it is
declared under `relatedPackages` rather than `dependencies`. Add it yourself:

```
https://github.com/Kobapps/EditorCoreKit.git?path=Packages/com.kobapps.editorcorekit
```

The runtime does not need it. A player build contains no editor code.

## Quick start

**Tools ▸ LocalizationKit ▸ Set Up Localization** creates the catalog and the settings asset and
links them. Add a language and a few keys in the manager, then:

**Tools ▸ LocalizationKit ▸ Generate Key Constants** writes a `LocKeys` class, so keys are code
rather than strings:

```csharp
buyLabel.Localize(LocKeys.Store.BuyButton);   // binds a label — follows every language change
scoreLabel.text = L.T(LocKeys.Hud.Score);     // one-off read, allocates nothing
L.Set("fr");                                  // change language
```

That is the whole surface most projects need. `L` is a shorthand over `Localization`; both are
public and interchangeable.

Regenerate `LocKeys` after adding or renaming keys — a renamed entry then becomes a **compile
error** instead of a label quietly showing its key.

## The three ways in

**A field, bound by the source generator.** The class must be `partial`; nothing else is required.

```csharp
public partial class ShopPanel : MonoBehaviour
{
    [Localized("Store/BuyButton")] private string m_BuyLabel;
    [Localized("Store/Price")]     private string m_PriceFormat;

    // Both fields are filled before OnEnable returns, and refilled on every language change.
}
```

**A component, for text that already exists in a scene.** Add `LocalizedText` (uGUI) or
`LocalizedTMPText` (TextMeshPro), then pick a key from the dropdown. If the label already has text
in it, the inspector offers to turn that text into a catalog entry in one click.

**A call, for anything else.**

```csharp
label.text = Localization.Get("Popups/Quit/Title");
score.text = Localization.Format("Hud/Score", points);

Localization.SetLanguage("fr");
```

## Keys and categories

A key is `Category/Key`. Categories are how a catalog stays navigable once it has a few thousand
entries — `Default`, `Popups`, `Store`, `Tutorials` are created for you, and categories nest, so
`Popups/Quit/Title` is a key named `Title` in a category named `Popups/Quit`.

To let a designer choose a key from the inspector, mark the string:

```csharp
[SerializeField, LocalizationKey] private string m_TitleKey;
[SerializeField, LocalizationKey("Popups")] private string m_BodyKey;   // scoped to one category
```

That draws a searchable picker grouped by category, with the resolved text underneath and a red
field if the key no longer exists — which is the failure this attribute exists to catch. A renamed
entry otherwise leaves a string field pointing at nothing, and nothing else in the editor says so.

## What it costs

The design goal was that using this in a per-frame path should not be a mistake.

| Operation | Cost |
| --- | --- |
| `Localization.Get(key)` | One ordinal dictionary probe, one array index. No allocation. |
| `Localization.GetValue(ref handle)` | One `int` compare, one array index. No allocation. |
| Changing language | One reference assignment, then one callback per registered object. |
| `Localization.Format(key, …)` | Allocates — composing a string is the point. Keep it off hot paths. |

This falls out of building a **table** from the catalog once, at load:

- Keys are flattened to `Category/Key` and interned into a single map, so a lookup does no
  substring work and no concatenation.
- Text is stored as one `string[]` per language, all the same length. Changing language points an
  `m_Active` field at a different array; nothing is copied or rehashed.
- Gaps are filled at build time — a missing translation becomes the fallback language's text, or
  the configured marker. So a read never branches on null and never returns one.

Strings come back by reference from the table, not as copies. Nothing on the read path allocates,
so nothing on the read path contributes to a GC spike.

`LocalizationHandle` is the fastest form: resolve once, read forever.

```csharp
private LocalizationHandle m_Handle;

private void Awake() => m_Handle = Localization.Resolve("Hud/Score");
private void Update() => m_Label.text = Localization.GetValue(ref m_Handle);
```

A handle remembers which table build it came from. If the table is replaced underneath it — a
remote refresh, say — the version stops matching and the next read re-resolves instead of reading a
stale row. That is the difference between a handle and a raw index, and it is why handles are safe
to hold indefinitely.

## The generator, and the one case it cannot handle

`[Localized]` emits a second half of your class implementing `ILocalizedObject`, plus
`EnableLocalization()` and `DisableLocalization()`. For a `MonoBehaviour` it also emits `OnEnable`
and `OnDisable` that call them.

**Unless your class already declares one of those.** A partial class cannot supply a second body for
a method you wrote, so in that case the generator emits the two methods and raises **LK003**, and
you call them yourself:

```csharp
public partial class ShopPanel : MonoBehaviour
{
    [Localized("Store/BuyButton")] private string m_BuyLabel;

    private void OnEnable()
    {
        EnableLocalization();   // LK003 told you to add this line
        Refresh();
    }

    private void OnDisable() => DisableLocalization();
}
```

The alternative was to bind nothing and say nothing, which would show up as a permanently blank
label with no explanation. A warning naming the fix is better than a silent failure.

There is also a hook that runs after every refresh:

```csharp
partial void OnLocalizationApplied() => RebuildLayout();
```

### Diagnostics

| Id | Meaning |
| --- | --- |
| `LK001` | The class has `[Localized]` fields but is not `partial`. |
| `LK002` | A `[Localized]` field is not a `string`. |
| `LK003` | The class declares `OnEnable`/`OnDisable`; call `EnableLocalization`/`DisableLocalization` yourself. |
| `LK004` | `[Localized]` with an empty key. |
| `LK005` | A `[Localized]` field is `static`, `const` or `readonly`, so it cannot be assigned. |
| `LK006` | A containing type of a bound nested class is not `partial`. |

## Setup, and the failure worth knowing about

The runtime finds its catalog through a settings asset at
`Assets/Resources/LocalizationKitSettings.asset`. **Without it nothing is localized in a build and
there is no runtime error to say why** — every lookup simply returns its key. The window and the
settings page both check for this and offer to create it, because it is the one mistake that
produces no symptom until someone opens the game in another language.

Configure it under **Project Settings ▸ LocalizationKit**:

| Setting | What it decides |
| --- | --- |
| Catalog | Which catalog the table is built from. |
| Initialize on load | Whether the kit comes up before the first scene. Turn off to control timing. |
| Startup language | Remembered choice → device language → default; or device; or always default. |
| Remember language | Whether a change is written to `PlayerPrefs`. |
| Missing key behavior | Return the key, return empty, or return `#Category/Key#`. |
| Log missing keys | Warn on an unknown key. Editor and development builds only. |

## Right-to-left, and fonts

A language can be marked right-to-left. `Localization.IsRightToLeft` reports it, and the shipped
components will flip their alignment if you ask them to — off by default, because a layout already
mirrored by its parent would otherwise be flipped twice.

`LocalizedTMPText` also takes per-language font assets. This matters more than it sounds: a TMP font
asset carries a baked glyph set, so a Latin font has nothing to draw Hebrew or Japanese with and
renders squares without complaining. Naming a font per language fixes that at the only moment
anything knows it needs to.

## Translators work in spreadsheets

**Import & Export** in the window reads and writes one row per key, one column per language — the
shape a Google Sheet or Excel file already has.

```
Key,en,fr,he
Store/BuyButton,Buy,Acheter,קנה
Popups/Quit/Title,"Quit, really?","Quitter, vraiment ?",לצאת?
```

The parser is RFC 4180: quoted fields may contain commas, doubled quotes and newlines. That is not
pedantry — a translation containing a comma is not an edge case.

Import merges. Nothing is deleted, and three switches decide the rest: whether unknown keys are
added, whether unknown languages are added, and whether existing text is overwritten. Turning
overwrite **off** is how you take a translation pass back without losing edits made in the editor
since.

Exports are UTF-8 with a BOM, because without one Excel opens the file in the system codepage and
mangles every non-ASCII string.

## Remote catalogs

Not shipped, but the seam is real rather than aspirational.

`Localization` reads a `LocalizationTable`, never a catalog asset. Anything that can produce a table
can be the source:

```csharp
using var request = UnityWebRequest.Get(publishedSheetUrl);
await request.SendWebRequest();

var table = LocalizationTableBuilder.FromCsv(request.downloadHandler.text, defaultLanguage: "en");
Localization.SetTable(table, Localization.LanguageCode);
```

Every `[Localized]` field and every localized component refreshes itself. No calling code changes.
In Sheets, the URL comes from **File ▸ Share ▸ Publish to web ▸ Comma-separated values**.

`ILocalizationSource` is the interface to implement for something more involved; `LocalCatalogSource`
is the shipped one.

## Objects the kit ships no component for

```csharp
// LocalizedStringEvent raises a UnityEvent<string> — wire it in the inspector.
```

Or implement the interface directly:

```csharp
public sealed class Billboard : MonoBehaviour, ILocalizedObject
{
    private LocalizationSubscription m_Subscription;
    private LocalizationHandle m_Handle;

    public void ApplyLocalization() => Render(Localization.GetValue(ref m_Handle));

    private void OnEnable()
    {
        m_Handle = Localization.Resolve("Signs/Welcome");
        m_Subscription = LocalizationBinder.Register(this);   // applies immediately
    }

    private void OnDisable() => LocalizationBinder.Unregister(ref m_Subscription);
}
```

`LocalizationBinder` is used instead of a plain C# event for three reasons that only show up at
scale: registering allocates nothing where subscribing a delegate allocates one per object;
unregistering is O(1) because a subscription carries its slot, so tear-down is not quadratic; and a
handler that throws is caught and logged against the object that threw rather than aborting the
whole invocation list.

## AI assistants

**Tools ▸ LocalizationKit ▸ Install AI Skill** writes a `localizationkit` skill into the
project's `.claude/skills/`, so Claude Code and the Agent SDK get accurate guidance instead of
guessing: how keys compose, which of the four binding styles fits a given situation, the
recipe for bulk-localizing a project that is full of hard-coded strings, and — most usefully —
the list of failure modes that produce no error at all.

That last part is why the skill exists. Most of what goes wrong here is silent: a missing
settings asset localizes nothing in a build, a label assigned once instead of bound looks
correct until the language changes, and a TMP font without the right glyphs renders blank
squares. None of it logs anything.

## Conventions worth keeping

- **Key by meaning, not by text.** `Store/BuyButton`, not `Store/Buy`. The copy will be rewritten;
  the key should survive it.
- **Author in one language and let fallback do its job.** A gap resolves to the default language,
  which reads better in a screenshot than a key does.
- **Use `ReturnMarker` during a translation pass.** `#Store/BuyButton#` is unmissable; a key that
  merely looks like a key is not.
- **Renaming a key breaks every field pointing at it.** The window warns; the picker turns red. Do
  it early or not at all.
- **Don't put a formatted lookup in `Update`.** `Get` is free, `Format` is not.

## License

MIT.
