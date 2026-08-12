# Changelog

All notable changes to LocalizationKit are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.1] - 2026-08-13

### Fixed
- **The bundled sample could not be imported.** `package.json` declared the sample path as
  `Samples/LocalizationShowcase`; Unity resolves that path literally against the package root and
  does not map `Samples` to `Samples~`, so the Package Manager reported
  *"the path … does not exist"*. Now `Samples~/LocalizationShowcase`, matching every first-party
  Unity package.
- Importing the sample then surfaced three faults in code that had never been compiled, because
  `Samples~` is excluded from compilation by design:
  - `LocalizationKit.Samples.Editor` did not reference `LocalizationKit.uGUI`, so `LocalizedText`
    was unresolvable.
  - The generated scene used `StandaloneInputModule`, which throws under *Active Input Handling ▸
    Input System Package (New)* — the sample's language button did nothing. The module is now
    resolved by name, so the scene works under the old, new or both back-ends without making the
    Input System a dependency of the sample.
  - The sample's own button label was hard-coded English. It is localized now.

### Changed
- The source generator no longer ships `.deps.json` or debug symbols alongside its DLL. Neither
  means anything to a Roslyn analyzer, but Unity imported the `.deps.json` as a `TextAsset` in
  every consuming project.

## [1.0.0] - 2026-08-13

First release.

### Added

- **`[Localized("Category/Key")]`** on a string field, backed by a Roslyn source generator. The
  field is filled before `OnEnable` returns and refilled on every language change. `MonoBehaviour`
  lifecycle wiring is emitted automatically unless the class already declares `OnEnable`/`OnDisable`,
  in which case **LK003** names the two methods to call instead — the generator never binds silently.
- **`[LocalizationKey]`** on a string field, drawing a searchable picker grouped by category, with
  the resolved text underneath and a red field when the key is no longer in the catalog. Optionally
  scoped to one category.
- **`LocalizationCatalog`** asset: languages, nested categories and entries, with editing operations
  that keep every entry's positional value array in step when a language is added, removed or
  reordered.
- **`LocalizationTable`**: the flat, read-optimised form built once from a catalog. Keys interned
  into one ordinal map, text stored as one `string[]` per language, gaps filled at build time. A
  read is a dictionary probe and an array index and allocates nothing; changing language is a single
  reference assignment.
- **`LocalizationHandle`**: a pre-resolved key that reads without the dictionary, and re-resolves
  itself if the table is replaced underneath it.
- **`LocalizationBinder`**: allocation-free registration with O(1) removal and per-object exception
  isolation, so one throwing handler cannot stop the rest from updating.
- **`LocalizedText`** (uGUI) and **`LocalizedTMPText`** (TextMeshPro) components, each in its own
  assembly gated behind a version define so neither UI package is a hard dependency. `LocalizedTMPText`
  also swaps font assets per language. **`LocalizedStringEvent`** covers everything else.
- **Localization Manager** window (Tools ▸ LocalizationKit) built on EditorCoreKit: overview with
  per-language coverage and setup problems, language management, a virtualised key browser with
  search across keys and translations, a "only missing" filter, and per-language editing.
- **Project Settings ▸ LocalizationKit**: catalog, startup language mode, remembered language,
  missing-key behaviour and missing-key logging.
- **CSV / TSV import and export** in the kit's row-per-key, column-per-language shape, with an
  RFC 4180 parser that handles quoted commas, doubled quotes and embedded newlines. Import merges,
  with switches for adding keys, adding languages and overwriting existing text. Exports are UTF-8
  with a BOM so Excel reads them correctly.
- **`LocalizationTableBuilder.FromCsv`** and **`ILocalizationSource`**, the seam a remote catalog
  arrives through: produce a table from anywhere, call `Localization.SetTable`, and every bound field
  and component refreshes without a line of calling code changing.
- Inspector for the shipped text components showing the key in every language, and a one-click flow
  turning text already typed into a scene label into a catalog entry.
- **`L`** — a shorthand facade over `Localization`: `L.T(key)`, `L.T(key, arg)`, `L.Set(code)`,
  `L.Next()`, `L.Bind`/`L.Read`, `L.Changed`. Every member forwards to `Localization`, which
  remains the documented API.
- **`Text.Localize(key)` / `TMP_Text.Localize(key)`** extension methods that attach and bind the
  right component in one line, plus one-shot `SetLocalized(key, args)` for formatted strings.
- **Key constants generation** (Tools ▸ LocalizationKit ▸ Generate Key Constants): writes a
  `LocKeys` class where categories become nested classes, so `Popups/Quit/Title` is
  `LocKeys.Popups.Quit.Title`. Turns a renamed or deleted entry into a compile error rather than a
  label showing its key at runtime.
- **One-click setup** (Tools ▸ LocalizationKit ▸ Set Up Localization): creates the catalog and the
  `Resources` settings asset and links them, since a half-finished setup fails silently.
- **Bundled AI skill** (Tools ▸ LocalizationKit ▸ Install AI Skill): installs a `localizationkit`
  skill into the project's `.claude/skills/` covering the API, patterns for bulk-localizing an
  existing project, and the failure modes that produce no error.

### Notes

- Requires Unity 6000.0. The editor tooling needs
  [EditorCoreKit](https://github.com/Kobapps/EditorCoreKit) 2.0.0, declared under `relatedPackages`
  rather than `dependencies` because UPM does not resolve git dependencies transitively — add it to
  your own manifest. The runtime does not need it.
- The source generator ships as a pre-built analyzer DLL in `Runtime/Plugins`. Its `.meta` carries
  the `RoslynAnalyzer` asset label; without that label Unity treats it as a runtime assembly and the
  generator silently never runs. Rebuild it with `SourceGenerator~/build.ps1`.
