# Google Sheets provider

A working `ILocalizationProvider` that reads a Google Sheet, and — with a twenty-line Apps Script
living inside the spreadsheet — writes back to it.

| File | What it is |
|---|---|
| `Runtime/GoogleSheetsLocalizationProvider.cs` | The provider. |
| `Editor/GoogleSheetsProviderInspector.cs` | A four-step guided setup that verifies itself. |
| `AppsScript/LocalizationEndpoint.gs.txt` | The write endpoint. Only needed for publishing. |

## Start here

Select the provider asset. The inspector is a checklist, and it tells you which step you are on.

**Reading takes two steps and no credential.** Steps 3 and 4 are optional.

### What is automated, and what cannot be

| Step | |
|---|---|
| Spreadsheet id and tab | **Automated** — paste the browser URL, it is picked apart for you |
| Tab names | **Automated** — *Discover Tabs* reads them out of the workbook |
| Shared secret | **Automated** — *Generate Secret* makes one and copies it |
| Apps Script source | **Automated** — *Copy Apps Script* puts it on the clipboard |
| Verifying any of it | **Automated** — *Test Connection* and *Test Publishing* |
| Sharing the sheet | **You** — needs a signed-in Google session |
| Pasting and deploying the script | **You** — same |

Those last two are the honest limit. Everything Google exposes without an authenticated session is
done for you; sharing a document and deploying a script are actions Google will only accept from a
signed-in human, and no amount of code on this side changes that. The buttons open the right page so
the manual part is a paste and two clicks.

> **Tab discovery has no API behind it, and does not need one.** `/export?format=xlsx` hands back the
> whole workbook as an OOXML zip, and `xl/workbook.xml` inside it lists every tab by name. No
> credential, one request. It is editor-only — pulling a whole document down to read nine strings is
> a fine trade for a button pressed once and an indefensible one at runtime.

## The sheet

Two layouts work. Both use one row per key and one column per language, headed by its code.

### One tab per category (recommended)

The **tab name is the category**, and column A holds the key *within* it. Nobody wants to scroll
past four hundred rows to reach the store copy.

`Popups` tab:

```
Key,en,fr,he
Settings/Title,SETTINGS,PARAMÈTRES,הגדרות
Quit/Title,"Quit, really?","Quitter, vraiment ?",לצאת?
```

Those become `Popups/Settings/Title` and `Popups/Quit/Title`. List the tabs in the provider's
**Sheets** array and it reads them all, filing each tab's rows under its own name.

- A key that already carries its category is left alone, so writing the full `Popups/Title` in the
  Popups tab does the right thing rather than producing `Popups/Popups/Title`.
- **Tabs starting with `_` are ignored** — put notes, instructions and scratch work in one of those.
- Tabs may disagree about which languages they carry; the result is the union, and a tab missing a
  language contributes blanks for it.

### One flat tab

Leave **Sheets** empty and the provider reads the single tab named by the gid. Column A holds full
keys:

```
Key,en,fr,he
Store/BuyButton,Buy,Acheter,קנה
Popups/Quit/Title,"Quit, really?","Quitter, vraiment ?",לצאת?
```

Either way a key is `Category/Key`, nested as deep as you like — `Popups/Quit Level/Title` is the
key `Title` in the category `Popups/Quit Level`. A key with no slash lands in `Default`.

The quickest way to start is **Import & Export ▸ Export CSV** in the Localization Manager, then
`File ▸ Import` that into a new sheet. The round trip is lossless.

> **A misspelled tab name does not fail — it lies.** Asked for a tab that does not exist, Google's
> `gviz` endpoint returns the *first* tab of the workbook with a cheerful `200`, so a tab renamed in
> the spreadsheet and not in the provider would file the first category's rows under the missing
> category's name. The provider notices two tabs answering with identical documents, skips the
> duplicate and says so in the fetch warnings. If you see that warning, fix the spelling — do not
> merge.

## Reading (no credentials at all)

1. Create the provider: **Assets ▸ Create ▸ LocalizationKit ▸ Google Sheets Provider**.
2. Paste the spreadsheet URL from your browser into the **Sheet** field at the top of the
   inspector. The id and tab are picked out of it for you.
3. Share the sheet so the request can succeed without a login. Either:
   - **Share ▸ General access ▸ Anyone with the link ▸ Viewer**, or
   - **File ▸ Share ▸ Publish to web ▸ Comma-separated values**, and paste *that* URL instead.
4. Press **Test Fetch**. It reports the keys and languages it found.

> **If it says Google returned a sign-in page**, the sheet is still private. Google answers an
> unshared sheet with an HTML login page — sometimes as a `401`, sometimes as a cheerful `200` —
> rather than with an error, which is why the provider checks for it by hand and says so plainly.

Then, in **Localization Manager ▸ Remote**: assign the provider, press **Fetch & Preview** to see
what would change, and **Merge Into Catalog** to accept it.

## Writing

Google's own Sheets API needs an OAuth2 flow, a consent screen and a refresh token. None of that
belongs in a game, and a build machine cannot complete a consent screen. The practical route is an
Apps Script web app.

1. On the spreadsheet: **Extensions ▸ Apps Script**.
2. Paste `AppsScript/LocalizationEndpoint.gs.txt` in, and change `SECRET`.
3. **Deploy ▸ New deployment ▸ Web app**, *Execute as: Me*, *Who has access: Anyone*.
4. Copy the deployment URL into **Web app URL** on the provider, and the same secret into
   **Shared secret**.
5. **Remote ▸ Publish Catalog To Remote**.

Publishing always sends one flat CSV of full keys. The web app splits it into a tab per category on
arrival, so Unity never has to know how the workbook is arranged, and a publish is a single write
rather than eight that can each fail on their own. A category whose last key was deleted has its tab
cleared rather than removed, so column formatting and notes survive.

*Who has access: Anyone* is the only setting that works without OAuth, which is why the secret
exists: the URL is unguessable and every request must carry the secret as well. Treat the pair as a
password.

After editing the script, **redeploy** — Apps Script serves the deployed version, not the saved
one, and forgetting this is the most common "my change did nothing".

**Publishing fetches first and merges your catalog over what is there**, so a language column or a
key somebody added in the sheet survives being published from the editor.

### Keep the asset out of a public repository

The secret is stored on the provider asset, which makes that `.asset` file as sensitive as a
password. Either keep it untracked, or keep the repository private.

### Upload is editor-only

`Capabilities` does not report `Upload` outside the editor, so the secret is never read in a player —
and an asset a build references ships inside that build, where anyone can read it out. A game has no
reason to write translations back.

## Build machines

The catalog asset is what ships inside the player, so a build machine has to pull *before* it
builds. Two ways, and they compose:

```bash
Unity -batchmode -quit -projectPath . \
  -executeMethod LocalizationKit.Editor.LocalizationRemoteSync.SyncFromRemote
```

Exits non-zero when the fetch fails, so the pipeline stops instead of quietly shipping last week's
text. Or turn on **Sync remote before build** in Project Settings ▸ LocalizationKit and every build
pulls first, failing the build if it cannot.

Both work under `-batchmode`, where there is no player loop to drive a `UnityWebRequest` — the
request is made through `System.Net` instead and completes before the method returns. Nothing in
the provider has to know about that.

The provider asset and the merge policy both live in version control, so a build machine behaves the
way the repository says rather than the way someone's local editor happens to be set up. **Reading
needs no credential at all**, so a CI pull works whether or not the write secret is filled in.

## Runtime refresh

Turn on **Fetch at runtime on startup** to have players pick up text changes without a new build.
The catalog — or the cached copy of the last fetch — is installed first and the game runs on it; the
remote's answer replaces it whenever it arrives, and every `[Localized]` field and localized
component refreshes on its own.

```csharp
LocalizationRemote.FetchAndApply(provider, result =>
{
    if (!result.Success) Debug.Log($"Still on the shipped strings: {result.Error}");
});
```

A fetch that fails leaves what was already there. An empty answer is treated as a failure rather
than applied, because an empty document is nearly always a permissions page, and applying it would
blank the game.

## Writing your own provider

The Sheets provider is not special. Derive from `LocalizationProviderAsset`, report what you can do,
and produce a `LocalizationSnapshot`:

```csharp
public override LocalizationProviderCapabilities Capabilities => LocalizationProviderCapabilities.Fetch;

public override void Fetch(Action<LocalizationFetchResult> onCompleted)
{
    LocalizationWeb.Get(m_Url, response =>
    {
        if (!response.Success)
        {
            onCompleted(LocalizationFetchResult.Failed(response.Error));
            return;
        }

        onCompleted(LocalizationSnapshot.TryFromCsv(response.Text, out var snapshot, out var error)
            ? LocalizationFetchResult.Ok(snapshot)
            : LocalizationFetchResult.Failed(error));
    });
}
```

Everything else — merging, previewing, caching, the build hook, the editor page — already works
against that.
