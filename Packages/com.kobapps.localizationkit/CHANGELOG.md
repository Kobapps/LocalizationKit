# Changelog

All notable changes to LocalizationKit are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.1.0] - 2026-08-13

### Fixed
- **Rows in the Keys list overlapped each other.** Each row stacked the key over a preview of its
  text, and the preview wrapped — but a virtual list positions rows at a fixed height, so a row
  that renders taller than that is drawn over the row beneath it rather than pushing it down. A
  row is now a single line and every column clips with an ellipsis instead of wrapping.
- **The translation area was cut off once a catalog had more than a few languages.** It had a
  fixed 300px ceiling below a list that took everything else. The list and the translations are
  now a draggable vertical split whose position is remembered, and the translations scroll inside
  their own pane.
- **Moving a key into a category that already had that key produced two entries with the same full
  key**, the second of which could never be looked up. The move is refused now.
- **Pressing Enter in the new-key dialog could add the key twice.** Choosing a category rebuilt the
  dialog's contents, which re-registered the Enter handler on a root element that `Clear()` does
  not strip callbacks from. Category changes update the controls in place instead.
- Categories differing only in case — `Popups/Quit` beside `popups/Rate` — described one branch to
  every lookup but were drawn and listed as two, including as two separate submenus. One spelling
  now wins for the whole subtree.
- **"New Category…" inside the new-key dialog opened an empty window.** It was a modal opened from
  inside a modal, which Unity's window stack does not survive. The new name is typed inline in the
  dialog's own category field now, with an × to go back to picking an existing one.

### Added
- **Subcategories.** A category is a path, so `Popups/Quit Level/Title` is the key `Title` in the
  category `Popups/Quit Level`, nested as deep as you like. The runtime already split a full key at
  its *last* separator — the editor was the only thing insisting a category be a single word.
  - The sidebar draws the real tree and is managed like a file tree: right-click any row for
    New Subcategory / Rename / Delete, right-click the empty space for New Category, double-click
    a branch to fold it, and expand-all / collapse-all in the header. Creating or renaming a
    category unfolds whatever it is nested inside, so it is on screen when the tree redraws.
  - It shows the intermediate levels a catalog implies but does not store — a project with only
    `Popups/Quit` and `Popups/Rate` gets a `Popups` group — with inherited counts.
  - **Those base categories are selectable everywhere a category is picked**, so a new key can go
    straight into `Popups` even when nothing has ever been filed there. `LocalizationEditorCatalog`
    exposes the full set as `CategoryPaths`, parents before children.
  - Selecting a branch shows **everything under it**, not just keys filed at that exact path. The
    same is true of the inspector key picker's category filter.
  - Every category dropdown nests to match. A category that is *also* a parent is listed as
    `(this category)` inside its own submenu, because one menu path cannot be both a command and a
    submenu.
  - Renaming a branch rewrites the paths beneath it; deleting one takes its subcategories with it,
    and says how many keys that is before it does.
  - `LocalizationKeys.IsValidCategory` and `LocalizationKeys.IsUnder` are the rules, in the runtime
    assembly next to `Compose`/`TrySplit`, and are covered by tests.
- The Keys list is a table: **key on the left, the default language's text beside it**, coverage on
  the right, under column headings that name the language being shown.
- **Duplicate Key**, which copies a key with every translation and its note — the usual answer to a
  second string that is nearly the first.
- **Move between categories without a dialog**: a category dropdown in the selected key's pane, a
  `Move to ▸` submenu on the overflow button, and a right-click menu on any row carrying rename,
  duplicate, move, copy and delete.
- The selected key's pane now **shows the key and its category as editable fields**, so a rename is
  a rename in place rather than a trip through a dialog.
- A **copy source** button beside any empty translation, which seeds it from the default language.
- The sample catalog ships **55 keys across 13 categories** rather than six, three levels deep
  (`Menu/Options/Audio`), with deliberate gaps and one very long string, so the manager opens onto
  something that exercises scrolling, nesting, filtering and the coverage column.

### Changed
- Dialogs open **centred over the editor** instead of in the top-left corner of the display, are
  resizable, and lay their fields out in aligned columns with a live preview of the full key.
- The new-key dialog's category is a **nested dropdown** listing the whole tree, with
  "New Category…" and "New Subcategory of …" at the bottom.
- Names are **validated as they are typed**: a blank or clashing key disables OK and says why,
  rather than closing the dialog and reporting the clash in a toast once the typing is gone.
- Right-to-left languages are edited in a right-aligned field.

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
