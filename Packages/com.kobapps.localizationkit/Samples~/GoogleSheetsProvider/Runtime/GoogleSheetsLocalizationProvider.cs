using System;
using UnityEngine;
using UnityEngine.Networking;

namespace LocalizationKit.Samples
{
    /// <summary>
    /// A provider that reads — and, with a little help, writes — a Google Sheet.
    /// </summary>
    /// <remarks>
    /// Google offers two very different doors, and which one you need depends entirely on whether
    /// you want to write.
    /// <list type="number">
    /// <item><b>Reading</b> is a plain HTTP GET. A sheet shared as <i>anyone with the link can
    /// view</i>, or published with <b>File ▸ Share ▸ Publish to web</b>, answers
    /// <c>/export?format=csv</c> with exactly the shape <see cref="LocalizationCsv"/> reads. No
    /// key, no library, no OAuth.</item>
    /// <item><b>Writing</b> through Google's own API needs an OAuth2 flow, a consent screen and a
    /// refresh token — none of which belongs in a game or in a build script. The way people
    /// actually do this is a <b>Google Apps Script web app</b>: twenty lines of JavaScript living
    /// inside the spreadsheet, deployed as a URL, guarded by a shared secret. One is included next
    /// to this file.</item>
    /// </list>
    /// <para>
    /// <b>Upload is editor-only, on purpose.</b> An asset a build references ships inside that
    /// build, where anyone can read it out. A game has no reason to write translations back, so
    /// <see cref="Capabilities"/> simply does not offer
    /// <see cref="LocalizationProviderCapabilities.Upload"/> outside the editor, so the secret is
    /// never read in one. It does live on this asset, though, which means the asset is as
    /// sensitive as a password: keep it out of a public repository.
    /// </para>
    /// <para>
    /// It is a sample in the sense that it is small enough to read, not in the sense that it is a
    /// toy: it is the provider a project can ship with.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "GoogleSheetsLocalization",
        menuName = "LocalizationKit/Google Sheets Provider",
        order = 210)]
    public sealed class GoogleSheetsLocalizationProvider : LocalizationProviderAsset
    {
        [Header("Reading")]
        [Tooltip("The long id from the sheet's URL: docs.google.com/spreadsheets/d/<THIS>/edit")]
        [SerializeField] private string m_SpreadsheetId;

        [Tooltip("The gid of the tab to read, from #gid=<THIS> in the URL. The first tab is 0.")]
        [SerializeField] private string m_SheetGid = "0";

        [Tooltip("Tabs to read, one per category. Empty reads the single tab named by the gid above.")]
        [SerializeField] private string[] m_Sheets = Array.Empty<string>();

        [Tooltip("Optional. A 'Publish to web ▸ CSV' URL, used instead of the id above when set. Single tab only.")]
        [SerializeField] private string m_PublishedCsvUrl;

        [Header("Writing (editor only)")]
        [Tooltip("Deployment URL of the Apps Script web app. Leave empty for a read-only provider.")]
        [SerializeField] private string m_WebAppUrl;

        [Tooltip("Must match SECRET in the Apps Script. Saved into this asset — keep it out of a public repository.")]
        [SerializeField] private string m_SharedSecret;

        [Tooltip("Name of the tab the web app reads and writes.")]
        [SerializeField] private string m_SheetName = "Localization";

        [Header("Behaviour")]
        [Tooltip("Tab-separated instead of comma-separated. Only matters for a hand-built URL.")]
        [SerializeField] private bool m_UseTabs;

        [SerializeField] private int m_TimeoutSeconds = 30;

        /// <summary>The spreadsheet id, from its URL.</summary>
        public string SpreadsheetId
        {
            get => m_SpreadsheetId;
            set => m_SpreadsheetId = value;
        }

        /// <summary>The gid of the tab being read, in single-tab mode.</summary>
        public string SheetGid
        {
            get => m_SheetGid;
            set => m_SheetGid = value;
        }

        /// <summary>
        /// Tabs to read, each one a category. Empty means single-tab mode.
        /// </summary>
        /// <remarks>
        /// A workbook with a tab per category is the layout translators ask for — nobody wants to
        /// scroll past four hundred rows to reach the store copy — and it costs one HTTP request
        /// per tab, which is why it is opt-in rather than the default.
        /// </remarks>
        public string[] Sheets
        {
            get => m_Sheets ?? Array.Empty<string>();
            set => m_Sheets = value ?? Array.Empty<string>();
        }

        /// <summary>The spreadsheet in a browser, for the buttons that open it.</summary>
        public string SpreadsheetUrl =>
            string.IsNullOrWhiteSpace(m_SpreadsheetId)
                ? null
                : $"https://docs.google.com/spreadsheets/d/{m_SpreadsheetId.Trim()}/edit";

        /// <summary>True when this provider reads a tab per category rather than one flat tab.</summary>
        public bool IsMultiSheet => UsableSheets().Length > 0;

        /// <summary>
        /// A tab whose name starts with this is skipped — notes, instructions, scratch work.
        /// </summary>
        public const char IgnoredSheetPrefix = '_';

        /// <summary>Deployment URL of the Apps Script web app, when there is one.</summary>
        public string WebAppUrl
        {
            get => m_WebAppUrl;
            set => m_WebAppUrl = value;
        }

        /// <inheritdoc />
        public override string DisplayName =>
            string.IsNullOrWhiteSpace(m_SpreadsheetId) && string.IsNullOrWhiteSpace(m_PublishedCsvUrl)
                ? $"{name} (no sheet)"
                : $"{name} (Google Sheets)";

        /// <inheritdoc />
        public override LocalizationProviderCapabilities Capabilities
        {
            get
            {
                var capabilities = LocalizationProviderCapabilities.None;

                if (!string.IsNullOrWhiteSpace(FetchUrl))
                    capabilities |= LocalizationProviderCapabilities.Fetch;

#if UNITY_EDITOR
                if (HasWriteCredentials) capabilities |= LocalizationProviderCapabilities.Upload;
#endif

                return capabilities;
            }
        }

        /// <summary>
        /// The URL a fetch will actually hit — the first tab's, in multi-sheet mode. Shown in the
        /// inspector, because "it did not work" is nearly always answered by looking at this.
        /// </summary>
        public string FetchUrl
        {
            get
            {
                var sheets = UsableSheets();
                if (sheets.Length > 0) return SheetUrl(sheets[0]);

                if (!string.IsNullOrWhiteSpace(m_PublishedCsvUrl)) return m_PublishedCsvUrl.Trim();
                if (string.IsNullOrWhiteSpace(m_SpreadsheetId)) return null;

                var gid = string.IsNullOrWhiteSpace(m_SheetGid) ? "0" : m_SheetGid.Trim();

                return $"https://docs.google.com/spreadsheets/d/{m_SpreadsheetId.Trim()}"
                    + $"/export?format={(m_UseTabs ? "tsv" : "csv")}&gid={gid}";
            }
        }

        /// <summary>
        /// URL for one named tab.
        /// </summary>
        /// <remarks>
        /// The <c>gviz</c> endpoint rather than <c>/export</c>, because it takes a tab <em>name</em>
        /// and <c>/export</c> only takes a numeric gid. A name is what someone can type into an
        /// inspector and what survives a tab being moved; a gid has to be dug out of a URL and is a
        /// different number in a copied spreadsheet.
        /// </remarks>
        public string SheetUrl(string sheetName)
        {
            if (string.IsNullOrWhiteSpace(m_SpreadsheetId) || string.IsNullOrWhiteSpace(sheetName))
                return null;

            return $"https://docs.google.com/spreadsheets/d/{m_SpreadsheetId.Trim()}"
                + $"/gviz/tq?tqx=out:csv&sheet={UnityWebRequest.EscapeURL(sheetName.Trim())}";
        }

        /// <summary>The configured tabs, minus blanks and the ignored ones.</summary>
        private string[] UsableSheets()
        {
            if (m_Sheets == null || m_Sheets.Length == 0) return Array.Empty<string>();

            var usable = new System.Collections.Generic.List<string>(m_Sheets.Length);

            for (var i = 0; i < m_Sheets.Length; i++)
            {
                var name = m_Sheets[i];
                if (string.IsNullOrWhiteSpace(name)) continue;

                name = name.Trim();
                if (name[0] == IgnoredSheetPrefix) continue;

                usable.Add(name);
            }

            return usable.ToArray();
        }

        private char Delimiter => m_UseTabs ? '\t' : ',';

        /// <summary>The shared secret the web app checks, or null when the field is blank.</summary>
        public string Secret =>
            string.IsNullOrWhiteSpace(m_SharedSecret) ? null : m_SharedSecret.Trim();

        /// <summary>True when both halves of the write path are present.</summary>
        /// <remarks>
        /// Declared outside the editor-only branch that uses it so that a player build does not
        /// warn about a serialized field nothing reads.
        /// </remarks>
        private bool HasWriteCredentials =>
            !string.IsNullOrWhiteSpace(m_WebAppUrl) && Secret != null;

        // ---------------------------------------------------------------- fetch

        /// <inheritdoc />
        public override void Fetch(Action<LocalizationFetchResult> onCompleted)
        {
            var sheets = UsableSheets();

            if (sheets.Length > 0)
            {
                FetchSheets(sheets, onCompleted);
                return;
            }

            var url = FetchUrl;

            if (string.IsNullOrWhiteSpace(url))
            {
                onCompleted?.Invoke(LocalizationFetchResult.Failed(
                    "No spreadsheet id and no published CSV URL."));

                return;
            }

            FetchOne(url, null, (snapshot, _, error) =>
            {
                if (snapshot == null)
                {
                    onCompleted?.Invoke(LocalizationFetchResult.Failed(error));
                    return;
                }

                snapshot.SourceName = DisplayName;
                onCompleted?.Invoke(LocalizationFetchResult.Ok(snapshot));
            });
        }

        /// <summary>
        /// Reads every configured tab and folds them into one snapshot.
        /// </summary>
        /// <remarks>
        /// The tabs are read one after another rather than all at once. It is slower, and it is the
        /// right trade: a dozen simultaneous requests to the same document is how Google starts
        /// answering with 429s, and a localization fetch is not on anybody's critical path.
        /// <para>
        /// A tab that fails is recorded as a warning rather than failing the whole fetch — one
        /// renamed tab should not cost you the other seven categories — but a fetch where
        /// <em>every</em> tab failed is a failure, because that is a wrong id or a sharing problem
        /// and reporting it as an empty success would be a lie.
        /// </para>
        /// <para>
        /// <b>The duplicate check is not paranoia.</b> Asked for a tab that does not exist, the
        /// <c>gviz</c> endpoint does not answer 404 — it cheerfully returns the <em>first</em> tab
        /// of the workbook, with a 200. So a tab renamed in the spreadsheet and not in the provider
        /// would silently file the first category's rows under the missing category's name, and the
        /// merge would write real-looking translations against keys that do not exist. Two tabs
        /// answering with byte-identical documents is the signature of that, and it is worth
        /// refusing rather than importing.
        /// </para>
        /// </remarks>
        private void FetchSheets(string[] sheets, Action<LocalizationFetchResult> onCompleted)
        {
            var merged = new LocalizationSnapshot { SourceName = DisplayName };
            var failures = new System.Collections.Generic.List<string>();
            var warnings = new System.Collections.Generic.List<string>();
            var seen = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
            var index = 0;

            void Next()
            {
                if (index >= sheets.Length)
                {
                    if (failures.Count >= sheets.Length)
                    {
                        onCompleted?.Invoke(LocalizationFetchResult.Failed(
                            $"Every tab failed. First: {failures[0]}"));

                        return;
                    }

                    merged.Warnings.AddRange(failures);
                    merged.Warnings.AddRange(warnings);

                    onCompleted?.Invoke(LocalizationFetchResult.Ok(merged));
                    return;
                }

                var sheet = sheets[index++];

                FetchOne(SheetUrl(sheet), sheet, (snapshot, raw, error) =>
                {
                    if (snapshot == null)
                    {
                        failures.Add($"Tab '{sheet}': {error}");
                        Next();
                        return;
                    }

                    if (raw != null && seen.TryGetValue(raw, out var twin))
                    {
                        warnings.Add(
                            $"Tab '{sheet}' returned exactly what '{twin}' did, which is what Google "
                            + $"does when a tab does not exist. Skipped it — check that '{sheet}' is "
                            + "spelled the way the spreadsheet spells it.");

                        Next();
                        return;
                    }

                    if (raw != null) seen[raw] = sheet;

                    Absorb(merged, snapshot, sheet);
                    Next();
                });
            }

            Next();
        }

        /// <summary>
        /// Copies one tab's rows into the combined snapshot, filing them under the tab's category.
        /// </summary>
        /// <remarks>
        /// The filing rule itself lives in <see cref="LocalizationKeys.Qualify"/>, because a tab
        /// per category is only one of the shapes that carries the category out of band.
        /// </remarks>
        private static void Absorb(LocalizationSnapshot merged, LocalizationSnapshot tab, string category)
        {
            for (var i = 0; i < tab.LanguageCount; i++)
                merged.AddLanguage(tab.Languages[i]);

            for (var r = 0; r < tab.RowCount; r++)
            {
                var source = tab.Rows[r];
                if (string.IsNullOrEmpty(source.Key)) continue;

                var row = merged.GetOrAddRow(LocalizationKeys.Qualify(category, source.Key));
                row.Description = source.Description;

                for (var c = 0; c < tab.LanguageCount; c++)
                {
                    var language = merged.IndexOfLanguage(tab.Languages[c].Code);
                    if (language < 0) continue;

                    row.SetValue(language, source.GetValue(c));
                }
            }

            merged.Warnings.AddRange(tab.Warnings);
        }

        /// <summary>
        /// Fetches and parses one document. Hands back null plus a reason on failure, and the raw
        /// text alongside the snapshot so tabs can be compared for the fallback described in
        /// <see cref="FetchSheets"/>.
        /// </summary>
        private void FetchOne(string url, string sheet, Action<LocalizationSnapshot, string, string> onCompleted)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                onCompleted(null, null, "No URL — the provider has no spreadsheet id.");
                return;
            }

            LocalizationWeb.Get(url, response =>
            {
                if (!response.Success)
                {
                    onCompleted(null, null, response.Error);
                    return;
                }

                if (LooksLikeSignInPage(response.Text))
                {
                    // The single most common failure, and it arrives as a cheerful 200 OK: Google
                    // answers an unshared sheet with a sign-in page rather than a 403, so without
                    // this check it surfaces as "the header needs a key column".
                    onCompleted(null, null,
                        "Google returned a sign-in page instead of the sheet. Share it as "
                        + "'Anyone with the link ▸ Viewer', or use File ▸ Share ▸ Publish to web.");

                    return;
                }

                // The gviz endpoint always answers comma-separated, whatever the export format
                // setting says; only a hand-built /export URL can be tab-separated.
                var delimiter = sheet == null ? Delimiter : ',';

                onCompleted(
                    LocalizationSnapshot.TryFromCsv(response.Text, out var snapshot, out var error, delimiter)
                        ? snapshot
                        : null,
                    response.Text,
                    error);
            }, timeoutSeconds: m_TimeoutSeconds);
        }

        // ---------------------------------------------------------------- upload

        /// <inheritdoc />
        public override void Upload(LocalizationSnapshot snapshot, Action<LocalizationUploadResult> onCompleted)
        {
#if UNITY_EDITOR
            if (!HasWriteCredentials)
            {
                onCompleted?.Invoke(LocalizationUploadResult.Failed(
                    string.IsNullOrWhiteSpace(m_WebAppUrl)
                        ? "No Apps Script web app URL. See GoogleSheets.md."
                        : "No shared secret. Fill it in on the provider asset — it has to match "
                          + "SECRET in the Apps Script."));

                return;
            }

            if (snapshot == null || snapshot.IsEmpty)
            {
                onCompleted?.Invoke(LocalizationUploadResult.Failed("Nothing to upload."));
                return;
            }

            // Form-encoded rather than JSON: an Apps Script web app answers with a redirect, and a
            // request carrying a JSON content type is refused when it follows one.
            //
            // The payload is always one flat CSV of full keys. In multi-sheet mode the web app
            // splits it into a tab per category on arrival, rather than this end sending a request
            // per tab: eight writes that can half-succeed leave the document in a state nobody can
            // reason about, and one write either lands or does not.
            var body = "token=" + UnityWebRequest.EscapeURL(Secret)
                + "&sheet=" + UnityWebRequest.EscapeURL(m_SheetName ?? string.Empty)
                + "&split=" + (IsMultiSheet ? "1" : "0")
                + "&csv=" + UnityWebRequest.EscapeURL(snapshot.ToCsv());

            LocalizationWeb.Post(
                m_WebAppUrl.Trim(),
                body,
                LocalizationWeb.FormContentType,
                response =>
                {
                    if (!response.Success)
                    {
                        onCompleted?.Invoke(LocalizationUploadResult.Failed(response.Error));
                        return;
                    }

                    onCompleted?.Invoke(ReadUploadAnswer(response.Text));
                },
                timeoutSeconds: m_TimeoutSeconds);
#else
            onCompleted?.Invoke(LocalizationUploadResult.Failed(
                "Uploading is editor-only: the credential it needs must not ship in a player."));
#endif
        }

        /// <summary>
        /// Reads the web app's answer: <c>OK &lt;rows&gt;</c>, or <c>ERROR &lt;why&gt;</c>.
        /// </summary>
        internal static LocalizationUploadResult ReadUploadAnswer(string text)
        {
            var answer = (text ?? string.Empty).Trim();

            if (!answer.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
            {
                // An Apps Script that threw answers with an HTML error page, which is unreadable in
                // a dialog; the first line of it is usually the whole story.
                var firstLine = answer.Split('\n')[0];

                return LocalizationUploadResult.Failed(
                    string.IsNullOrWhiteSpace(firstLine)
                        ? "The web app returned nothing."
                        : firstLine);
            }

            var parts = answer.Split(' ');
            var rows = parts.Length > 1 && int.TryParse(parts[1], out var parsed) ? parsed : 0;

            return LocalizationUploadResult.Ok(rows);
        }

        // ---------------------------------------------------------------- helpers

        // ---------------------------------------------------------------- discovery

        /// <summary>
        /// Asks Google what the tabs are called, so nobody has to type them.
        /// </summary>
        /// <remarks>
        /// There is no anonymous "list the tabs" endpoint, but there is something better: the
        /// workbook export. <c>/export?format=xlsx</c> hands back the whole document as an OOXML
        /// zip, and <c>xl/workbook.xml</c> inside it lists every tab by name — no credential, no
        /// Apps Script, one request.
        /// <para>
        /// Tabs beginning with <see cref="IgnoredSheetPrefix"/> are left out, because a workbook
        /// that documents itself usually has a notes tab and importing it as a category would file
        /// a paragraph of instructions under a key.
        /// </para>
        /// <para>
        /// Editor-only: it pulls the entire document down to read nine strings out of it, which is
        /// a fine trade for a button somebody presses once and an indefensible one at runtime.
        /// </para>
        /// </remarks>
        public void DiscoverSheets(Action<string[], string> onCompleted)
        {
#if UNITY_EDITOR
            if (string.IsNullOrWhiteSpace(m_SpreadsheetId))
            {
                onCompleted?.Invoke(null, "No spreadsheet id yet.");
                return;
            }

            var url = $"https://docs.google.com/spreadsheets/d/{m_SpreadsheetId.Trim()}/export?format=xlsx";

            LocalizationWeb.Get(url, response =>
            {
                if (!response.Success)
                {
                    onCompleted?.Invoke(null, response.Error);
                    return;
                }

                if (response.Data == null || response.Data.Length == 0)
                {
                    onCompleted?.Invoke(null, "The workbook download was empty.");
                    return;
                }

                try
                {
                    onCompleted?.Invoke(ReadSheetNames(response.Data), null);
                }
                catch (Exception exception)
                {
                    // A sign-in page is HTML, not a zip, and arrives with a 200. Opening it as an
                    // archive is how that gets noticed.
                    onCompleted?.Invoke(null,
                        LooksLikeSignInPage(response.Text)
                            ? "Google returned a sign-in page instead of the workbook. Share the sheet "
                              + "as 'Anyone with the link ▸ Viewer'."
                            : $"Could not read the workbook: {exception.Message}");
                }
            }, timeoutSeconds: m_TimeoutSeconds);
#else
            onCompleted?.Invoke(null, "Tab discovery is editor-only.");
#endif
        }

#if UNITY_EDITOR
        /// <summary>Reads the tab names out of an OOXML workbook, in document order.</summary>
        private static string[] ReadSheetNames(byte[] workbook)
        {
            using var buffer = new System.IO.MemoryStream(workbook);
            using var archive = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Read);

            var entry = archive.GetEntry("xl/workbook.xml");
            if (entry == null) throw new InvalidOperationException("No xl/workbook.xml in the archive.");

            using var stream = entry.Open();

            // XDocument rather than a regex: a tab called "Q1 & Q2" is stored escaped, and a name
            // read back as "Q1 &amp; Q2" would match no tab at all.
            var document = System.Xml.Linq.XDocument.Load(stream);
            var names = new System.Collections.Generic.List<string>();

            foreach (var element in document.Descendants())
            {
                if (element.Name.LocalName != "sheet") continue;

                var name = (string)element.Attribute("name");
                if (string.IsNullOrWhiteSpace(name)) continue;

                name = name.Trim();
                if (name[0] == IgnoredSheetPrefix) continue;

                names.Add(name);
            }

            return names.ToArray();
        }
#endif

        /// <summary>
        /// Pulls the id and tab out of a spreadsheet URL pasted from a browser, so nobody has to
        /// pick them out by hand.
        /// </summary>
        public static bool TryParseSheetUrl(string url, out string spreadsheetId, out string gid)
        {
            spreadsheetId = null;
            gid = "0";

            if (string.IsNullOrWhiteSpace(url)) return false;

            const string marker = "/spreadsheets/d/";

            var start = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return false;

            start += marker.Length;

            // A published URL has an extra "e/" segment before the id.
            if (url.Length > start + 2 && url.Substring(start, 2) == "e/") start += 2;

            var end = start;
            while (end < url.Length && url[end] != '/' && url[end] != '?' && url[end] != '#') end++;

            spreadsheetId = url.Substring(start, end - start);
            if (string.IsNullOrEmpty(spreadsheetId)) return false;

            var gidAt = url.IndexOf("gid=", StringComparison.OrdinalIgnoreCase);
            if (gidAt >= 0)
            {
                gidAt += 4;

                var gidEnd = gidAt;
                while (gidEnd < url.Length && char.IsDigit(url[gidEnd])) gidEnd++;

                if (gidEnd > gidAt) gid = url.Substring(gidAt, gidEnd - gidAt);
            }

            return true;
        }

        /// <summary>Applies a pasted spreadsheet URL to this asset. Returns false when it is not one.</summary>
        public bool ApplySheetUrl(string url)
        {
            if (url != null && url.IndexOf("output=csv", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Already a published-CSV URL; keep it verbatim rather than rebuilding it from
                // parts, because the published form uses a different id from the editing form.
                m_PublishedCsvUrl = url.Trim();
                return true;
            }

            if (!TryParseSheetUrl(url, out var id, out var gid)) return false;

            m_SpreadsheetId = id;
            m_SheetGid = gid;
            m_PublishedCsvUrl = null;

            return true;
        }

        private static bool LooksLikeSignInPage(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            var head = text.Length <= 512 ? text : text.Substring(0, 512);

            return head.IndexOf("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) >= 0
                || head.IndexOf("<html", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
