---
name: localizationkit
description: >-
  Add, wire and use LocalizationKit in a Unity project — the Kobapps localization system.
  Use this skill whenever localization, translation, languages or multi-language text come
  up in a Unity project that has LocalizationKit installed: creating a catalog, adding
  languages or keys, marking fields with [Localized] / [LocalizationKey], attaching
  LocalizedText / LocalizedTMPText, reading strings with L.T or Localization.Get,
  generating LocKeys constants, importing and exporting CSV for translators, handling
  right-to-left languages and per-language fonts, or loading a catalog from a remote
  Google Sheet. Trigger it even when the user says only "translate this game", "add
  Hebrew", "make this text multi-language", "extract the hard-coded strings", "support
  another language", or "hook the UI up to translations" — LocalizationKit has
  non-obvious rules (partial classes, a settings asset in Resources, Category/Key
  composition, keys that resolve to themselves when missing) that fail silently when
  guessed at.
---

# Using LocalizationKit

LocalizationKit is a source-generated localization system for Unity 6. Keys live in a
catalog asset; a `[Localized]` field or a `LocalizedText` component binds to a key and
follows every language change without being told to.

Read this file top to bottom for a normal integration. For depth:

- `references/api.md` — the full API surface. **Open it before inventing a method name.**
- `references/patterns.md` — recipes: localizing an existing project, CSV round-trips,
  remote catalogs, RTL and fonts, formatted and pluralized strings.
- `references/pitfalls.md` — every way this fails *silently*. Read it before debugging.

## The mental model (read this first)

Three parts. Keeping them straight prevents nearly every bug:

1. **The catalog** — a `LocalizationCatalog` asset. Languages, categories, entries. This is
   authoring data: lists, positional arrays, edited in a window. **Nothing at runtime should
   read a catalog.**
2. **The table** — a `LocalizationTable`, built once from the catalog at load. Flat, interned,
   one `string[]` per language. This is what every lookup actually reads. Changing language
   swaps one array reference.
3. **The binder** — `LocalizationBinder` holds every live localized object. On a language
   change it calls `ApplyLocalization()` on each. `[Localized]` fields and the shipped
   components register themselves; you rarely touch this directly.

The single most important fact: **`Localization` reads a table, never a catalog.** That is
why a remote source is a drop-in later, and why the settings asset matters — see below.

**A key is `Category/Key`.** Categories nest, so `Popups/Quit/Title` is a key named `Title`
in a category named `Popups/Quit`. There is no separate category argument at the call site.

## Step 1 — Set the project up

If the project has no catalog yet, the whole of setup is one menu item:

**Tools ▸ LocalizationKit ▸ Set Up Localization**

It creates `Assets/Localization/LocalizationCatalog.asset` seeded with English and the
Default / Popups / Store / Tutorials categories, plus
`Assets/Resources/LocalizationKitSettings.asset`, and links them.

**The settings asset is not optional.** The runtime finds its catalog by loading
`LocalizationKitSettings` from `Resources`. Without it, nothing is localized in a build,
every lookup returns its key, and **there is no error at runtime saying why**. If you ever
create a catalog by hand, create the settings asset too and assign the catalog to it.

Configure the rest under **Project Settings ▸ LocalizationKit** (catalog, startup language
mode, remembered language, missing-key behaviour).

## Step 2 — Add languages and keys

Use **Tools ▸ LocalizationKit ▸ Localization Manager**. Languages page to add a language;
Keys page to add categories and entries and type translations.

From a script (editor-side, e.g. a migration tool):

```csharp
using LocalizationKit;
using LocalizationKit.Editor;

var catalog = LocalizationEditorCatalog.Catalog;

catalog.AddLanguage(new LanguageInfo("fr", "Français", SystemLanguage.French));
catalog.AddLanguage(new LanguageInfo("he", "עברית", SystemLanguage.Hebrew, rightToLeft: true));

var entry = catalog.AddEntry("Store", "BuyButton");     // category, key
entry.SetValue(catalog.IndexOfLanguage("en"), "Buy now");
entry.SetValue(catalog.IndexOfLanguage("fr"), "Acheter");

LocalizationEditorCatalog.Save(catalog);                 // writes + refreshes every editor surface
```

**Always go through the catalog's own methods** (`AddLanguage`, `RemoveLanguage`,
`MoveLanguage`, `AddEntry`). An entry's translations are stored positionally against the
language list; these methods keep every entry in step. Editing the serialized lists directly
shifts every translation by one language, silently, across the whole project.

## Step 3 — Generate the key constants

**Tools ▸ LocalizationKit ▸ Generate Key Constants** writes a `LocKeys` class:

```csharp
buyLabel.text = L.T(LocKeys.Store.BuyButton);   // not L.T("Store/BuyButton")
```

Prefer this in all generated code. A magic string is unverifiable — a typo compiles and
fails as a label showing a key. A constant gives autocomplete and turns a renamed entry into
a **compile error**. Regenerate after adding or renaming keys.

## Step 4 — Consume the strings

Four ways. Pick by where the string has to end up:

| Situation | Use |
| --- | --- |
| A UI label in a scene or prefab | `LocalizedText` / `LocalizedTMPText` component |
| A string field on your own `MonoBehaviour` | `[Localized("Category/Key")]` |
| A one-off read, or a formatted string | `L.T(key)` / `L.T(key, arg)` |
| A designer should choose the key in the inspector | `[SerializeField, LocalizationKey] string m_Key` |

**Component** — no code at all. Add `LocalizedText` (uGUI) or `LocalizedTMPText` (TMP) to
the label's GameObject and pick a key. From code:

```csharp
buyLabel.Localize(LocKeys.Store.BuyButton);   // extension on Text / TMP_Text; attaches + binds
```

**Attribute** — the class must be `partial`:

```csharp
public partial class ShopPanel : MonoBehaviour
{
    [Localized(LocKeys.Store.BuyButton)] private string m_Buy;
    [Localized(LocKeys.Store.Price)]     private string m_PriceFormat;

    partial void OnLocalizationApplied() => Refresh();   // optional hook, runs after every refresh
}
```

The generator fills both fields before `OnEnable` returns and refills them on every language
change. **If the class already declares `OnEnable` or `OnDisable`**, the generator cannot add
its own — it emits `EnableLocalization()` / `DisableLocalization()` and raises **LK003**
telling you to call them yourself:

```csharp
private void OnEnable()  { EnableLocalization(); Refresh(); }
private void OnDisable() => DisableLocalization();
```

**Direct read** — `L.T` is the short form of `Localization.Get`:

```csharp
label.text = L.T(LocKeys.Popups.Quit.Title);
label.text = L.T(LocKeys.Store.Price, coins);   // {0} substituted — this one allocates
L.Set("fr");                                    // change language
```

`L.T` allocates nothing and is safe to call per frame. `L.T(key, args)` composes a string and
is not — format once, or format only when the value changes.

## Step 5 — Localizing a project that already has hard-coded text

This is the common request ("translate this game"). Do it in this order — the order matters,
because renaming keys later breaks every reference:

1. **Find the strings.** Grep for `.text = "`, `SetText("`, and `Text`/`TMP_Text` components
   with authored text in scenes and prefabs. Do not start editing yet.
2. **Agree the key scheme first.** Key by *meaning and location*, not by the English:
   `Store/BuyButton`, not `Store/BuyNow`. The copy will be rewritten; the key must survive it.
   Group into categories that match screens — `Popups`, `Store`, `Tutorials`, `Hud`.
3. **Create every entry, filling the default language with the existing English.** Bulk-add
   via CSV import (`references/patterns.md`) rather than one at a time.
4. **Regenerate `LocKeys`.**
5. **Replace the usages** — component for scene labels, `[Localized]` or `L.T` for code.
6. **Verify** (Step 6), then export CSV for the translators.

For a scene label that already has text, the inspector on `LocalizedText` /
`LocalizedTMPText` offers **"Create a Key From This Text…"**, which makes the entry, seeds the
default language with the existing string, and assigns the key — prefer it over doing those
three steps by hand.

## Step 6 — Verify in the editor (use the Unity MCP)

A source generator and a `Resources` lookup both fail *silently* in a text editor. When a
Unity MCP is connected (`mcp__UnityMCP__*` / `mcp__unity-mcp__*`), verify rather than assume:

1. Trigger a refresh/compile and **read the console** for errors. Look for:
   - `LK001`–`LK006` generator diagnostics (see `references/pitfalls.md`).
   - `[LocalizationKit] No entry for key '…'` — a key that is not in the catalog.
2. Open **Tools ▸ LocalizationKit ▸ Localization Manager**. The Overview page reports every
   setup problem it can detect — missing settings asset, unassigned catalog, no languages,
   duplicate keys — and offers a button that fixes each.
3. **Change the language and look.** Use **Tools ▸ LocalizationKit ▸ Preview Language** to
   switch in edit mode; scene labels update live without entering play mode. This is the
   fastest way to catch a label that was assigned once instead of bound.
4. Set missing-key behaviour to `ReturnMarker` during a pass — untranslated text shows as
   `#Category/Key#`, which is unmissable, where a bare key can be mistaken for real copy.

## Best-practice checklist

- Key by meaning, not by the English text.
- Use `LocKeys` constants in code; never a raw string literal.
- One category per screen or system; nest for sub-flows (`Popups/Quit`).
- Author fully in one language and let fallback fill the rest — a gap falls back to the
  default language, which reads better than a key.
- `L.T` in `Update` is fine. `L.T(key, args)` in `Update` is not.
- Mark right-to-left languages as such, and give `LocalizedTMPText` a per-language font —
  a Latin TMP font renders Hebrew or Japanese as blank squares, without complaining.
- Never rename a key after it is referenced unless you are ready to fix every reference; the
  inspector marks a dead key in red but nothing else will.
- Never edit the catalog's language list except through its own methods.
- Don't add a `dependencies` entry for EditorCoreKit — it is intentionally a
  `relatedPackages` entry; add the git URL to the project manifest instead.

When unsure about an API shape, open `references/api.md` — do not invent method names.
