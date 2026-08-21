# LocalizationKit pitfalls

Nearly everything below fails **without an error**. That is what makes this list worth
reading before debugging rather than after.

---

## "Nothing is localized in the build, but it worked in the editor"

**No settings asset.** The runtime loads `LocalizationKitSettings` from `Resources`. In the
editor a preview table is installed by editor code, so the scene looks right; in a build
there is no such help and every lookup returns its key.

Fix: **Tools ▸ LocalizationKit ▸ Set Up Localization**, or create
`Assets/Resources/LocalizationKitSettings.asset` and assign the catalog. The Overview page
of the manager detects this and offers a button.

Related: the settings asset exists but its **Catalog field is empty**. Same symptom.

## "Every label shows its key"

In order of likelihood:

1. No settings asset, or no catalog assigned (above).
2. The keys genuinely are not in the catalog — check the console for
   `[LocalizationKit] No entry for key '…'`.
3. The catalog has no languages, so there is no column to read.
4. A stale `LocKeys` file referencing entries that were renamed or deleted.

A key that resolves to itself is the *designed* behaviour for a miss. To make misses obvious
during a pass, set **Missing key behavior** to `ReturnMarker` — they render as
`#Category/Key#`.

## "The label is right at first and then stops changing"

The text was **assigned once instead of bound**:

```csharp
buyLabel.text = L.T(LocKeys.Store.BuyButton);   // correct now, wrong after a language change
buyLabel.Localize(LocKeys.Store.BuyButton);     // binds — follows every change
```

`SetLocalized(key, args)` is also one-shot by design, because formatted arguments go stale.
If a formatted label must survive a language switch, re-format it from `L.Changed`.

## "`[Localized]` fields are always null"

- **The class is not `partial`.** Diagnostic **LK001**. The generator adds a second half of
  the class and cannot do so otherwise.
- **The class declares `OnEnable` or `OnDisable`.** Diagnostic **LK003**. The generator
  cannot supply a second body for a method you wrote, so it emits `EnableLocalization()` /
  `DisableLocalization()` and expects you to call them. This is a *warning*, so the build
  succeeds and the fields stay null until you add the two calls.
- The field is `static`, `const` or `readonly` (**LK005**), or not a `string` (**LK002**).
- A containing type of a nested class is not `partial` (**LK006**).

## "The generator does not run at all"

No generated members exist anywhere — every `EnableLocalization()` call is a compile error.

The analyzer DLL at `Runtime/Plugins/LocalizationKit.SourceGenerator.dll` must carry the
**`RoslynAnalyzer` asset label**. Without it Unity treats it as an ordinary runtime assembly
and never runs it — **with no error to say so**. The committed `.meta` declares the label; if
that `.meta` is deleted, Unity regenerates it without the label.

Fix: select the DLL in the Project window and add the `RoslynAnalyzer` label, or restore the
`.meta`. Rebuild the generator with `SourceGenerator~/build.ps1`.

The generator is pinned to Roslyn **4.3.1** deliberately. Bumping it past Unity 6's bundled
Roslyn produces CS9057 and Unity silently skips the analyzer.

## "Every translation shifted by one language"

The catalog's language list was edited directly instead of through
`AddLanguage` / `RemoveLanguage` / `MoveLanguage`. An entry's translations are stored
**positionally** against that list, and only those methods remap every entry in step.

This is silent and project-wide. `catalog.ResizeEntries()` repairs a *ragged* array but
cannot recover an order that was scrambled — restore from version control.

## "Hebrew / Arabic / Japanese renders as blank squares"

The TMP font asset has no glyphs for that script. Nothing logs. Add a per-language
`FontOverride` on `LocalizedTMPText`, pointing at a font asset built with that character set.

Marking the language right-to-left is a separate concern — it fixes direction, not glyphs.

## "Text is mirrored twice"

`Apply Right To Left Alignment` is on for a label inside a layout that already mirrors. Turn
it off on the label; let the parent own the mirroring.

## "Renaming a key broke things quietly"

Renaming an entry does **not** update anything referencing it. `[LocalizationKey]` fields
turn red in the inspector and `[Localized]`/`LocKeys` usages keep the old string. Rename
early, or not at all. Regenerate `LocKeys` after any rename so stale references become
compile errors instead of runtime misses.

## "Duplicate keys"

Two entries composing to the same `Category/Key` — usually after renaming a category. Which
one wins is whichever the table built last, which is arbitrary from the author's point of
view. The Overview page reports them.

## "Play mode starts in the wrong language"

The editor keeps a preview table so scene labels read correctly in edit mode. It is torn down
on entering play mode precisely so the runtime builds its own with the settings asset's
startup rules. If you disable that teardown — or reimplement it — a project with **domain
reload disabled** will carry the previewed language into play.

## "Tests fail with leaked state"

`Localization` and `LocalizationBinder` are static. Call `Localization.Reset()` in `SetUp`
and `TearDown`. This matters doubly with *Enter Play Mode Options ▸ Disable Domain Reload*,
where statics survive between play sessions.

Also: `LocalizationBinder.Register` **applies immediately**, so an object that throws in
`ApplyLocalization` logs during `Register`, not only during a language change. A test
provoking that needs its `LogAssert` guard around the registration too.

## "EditorCoreKit could not be found"

The editor window is built on EditorCoreKit, declared under **`relatedPackages`**, not
`dependencies` — UPM does not resolve git dependencies transitively, so a `dependencies`
entry would fail to resolve. Add it to the project manifest yourself:

```
"com.kobapps.editorcorekit": "https://github.com/Kobapps/EditorCoreKit.git?path=Packages/com.kobapps.editorcorekit"
```

The runtime does not need it; a player build contains no editor code.

## "`LocalizedText` does not exist"

It lives in `LocalizationKit.uGUI`, and `LocalizedTMPText` in `LocalizationKit.TextMeshPro`.
Both are separate assemblies gated on `com.unity.ugui` so neither UI package is a hard
dependency of the kit. Reference the right assembly from your own asmdef.

## "The fetch button in an editor window never comes back"

`UnityWebRequest` is driven by the player loop, which does not run in the editor outside play
mode, and does not run at all inside `-batchmode -executeMethod`. A provider that sends one and
waits waits forever — in exactly the two places a translator and a build machine use it.

Use `LocalizationWeb.Get` / `LocalizationWeb.Post` instead of `UnityWebRequest` directly. They
poll `EditorApplication.update` outside play mode, use a hidden behaviour inside it, and switch
to a synchronous `System.Net` call in batch mode so the answer lands before the method returns.

## "The remote fetch says the header needs a key column"

Google answers a sheet that is not shared with an HTML sign-in page — sometimes as a `401`,
sometimes as a cheerful `200 OK` — rather than an error. Parsed as CSV, that becomes a
confusing complaint about the header.

Share the sheet as *Anyone with the link ▸ Viewer*, or use **File ▸ Share ▸ Publish to web**.
The shipped Sheets provider checks for this and says so plainly; a provider you write yourself
should too.

## "A CI build shipped last week's translations"

A runtime fetch does not help: the catalog asset is what ships inside the player, and it is
whatever the checkout contained. The runtime refresh also never reaches the first frame, or a
player who is offline.

Turn on *Sync remote before build*, or run
`-executeMethod LocalizationKit.Editor.LocalizationRemoteSync.SyncFromRemote` before building.
Both fail loudly rather than building stale text.

## "A fetch blanked every label"

Something applied an empty document — nearly always a permissions page or a wrong sheet id
rather than a genuinely empty remote.

`LocalizationRemote.FetchAndApply` refuses an empty answer and leaves the active table alone,
and `LocalizationRemoteSync` refuses to merge one. If you call `Localization.SetTable` yourself
you are on your own: check `KeyCount` first.

## "Merging from the remote deleted keys"

`RemoveKeysNotIncoming` — *Remove keys the remote does not have* on the Remote page — is off by
default for this reason. With it on, a partial fetch deletes everything it did not carry. Turn
it on only when the remote is genuinely the source of truth, and use
`LocalizationMerge.Preview` first: it reports what would change without writing anything.

## Performance traps

- `L.T(key, args)` allocates — it composes a string. Fine on an event, wrong in `Update`.
- `L.T(key)` does not allocate and is fine in `Update`. Use `L.Bind`/`L.Read` to skip even
  the dictionary probe.
- Do not read a `LocalizationCatalog` at runtime. It is authoring data; the table is the
  runtime form.
- Do not subscribe to `L.Changed` merely to re-read a `[Localized]` field or a localized
  component — they already updated themselves before the event fired.
