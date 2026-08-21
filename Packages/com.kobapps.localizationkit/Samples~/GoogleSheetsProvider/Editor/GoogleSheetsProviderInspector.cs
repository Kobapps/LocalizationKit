using System;
using EditorCoreKit.Editor;
using LocalizationKit.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LocalizationKit.Samples.Editor
{
    /// <summary>
    /// A guided setup for the Sheets provider: four steps, each one showing whether it is done.
    /// </summary>
    /// <remarks>
    /// Connecting a game to a spreadsheet is four decisions and about six clicks, and nearly every
    /// one of them fails <em>silently</em> when it is wrong — an unshared sheet answers with a login
    /// page and a <c>200</c>, a misspelled tab answers with a different tab, an undeployed script
    /// answers with HTML. Left to a plain row of fields, all of that surfaces hours later as missing
    /// text in a build. So this is a checklist that verifies itself.
    /// <para>
    /// Everything that <em>can</em> be automated is a button: the id and tab come out of a pasted
    /// URL, the tab names come out of the workbook, the secret is generated, the script goes to the
    /// clipboard. What is left is the three things that need a signed-in Google session — sharing
    /// the sheet, pasting the script, deploying it — and each of those has a button that opens the
    /// right page.
    /// </para>
    /// </remarks>
    [CustomEditor(typeof(GoogleSheetsLocalizationProvider))]
    public sealed class GoogleSheetsProviderInspector : UnityEditor.Editor
    {
        private const string ScriptAssetName = "LocalizationEndpoint";

        private VisualElement m_Root;
        private string m_Status;
        private KUITone m_Tone = KUITone.Neutral;
        private bool m_Busy;

        public override VisualElement CreateInspectorGUI()
        {
            m_Root = new VisualElement();

            // Without this every KUI class resolves to nothing and the whole panel renders as
            // unstyled labels stacked against the left edge. An EditorWindow gets the stylesheets
            // from KUIWindowShell; an inspector has no shell, so it has to ask for them itself.
            KUITheme.Apply(m_Root);

            Rebuild();

            return m_Root;
        }

        private void Rebuild()
        {
            if (m_Root == null) return;

            m_Root.Clear();

            var provider = (GoogleSheetsLocalizationProvider)target;

            m_Root.Add(BuildSummary(provider));
            m_Root.Add(BuildStepSheet(provider));
            m_Root.Add(BuildStepSharing(provider));
            m_Root.Add(BuildStepTabs(provider));
            m_Root.Add(BuildStepPublishing(provider));
            m_Root.Add(BuildAdvanced(provider));

            if (string.IsNullOrEmpty(m_Status)) return;

            m_Root.Add(KUILayout.Gap(6f));
            m_Root.Add(new KUIBanner(m_Tone, m_Status));
        }

        // ---------------------------------------------------------------- summary

        /// <summary>
        /// What this provider is wired to right now, and the two buttons worth pressing again.
        /// </summary>
        /// <remarks>
        /// The numbered steps below collapse as they are satisfied, which is right for a setup
        /// walkthrough and wrong for everything after it: a configured provider would show four
        /// closed foldouts and nothing else, hiding the state and the actions behind clicks. This
        /// card is what a provider that is already working should look like.
        /// </remarks>
        private VisualElement BuildSummary(GoogleSheetsLocalizationProvider provider)
        {
            var card = new KUICard("Google Sheets", "Reads a spreadsheet. Writes back, once step 4 is done.");

            card.Add(KUILayout.WrapRow(
                new KUIBadge(provider.CanFetch() ? "Reads" : "Cannot read",
                    provider.CanFetch() ? KUITone.Success : KUITone.Warning),
                new KUIBadge(provider.CanUpload() ? "Writes" : "Read-only",
                    provider.CanUpload() ? KUITone.Success : KUITone.Neutral),
                new KUIBadge(provider.IsMultiSheet ? $"{provider.Sheets.Length} tabs" : "Single tab",
                    KUITone.Neutral)));

            if (!provider.CanFetch())
            {
                card.Add(KUILayout.Gap(8f));
                card.Add(new KUIBanner(
                    KUITone.Warning,
                    "Not connected yet",
                    "Open step 1 below and paste the spreadsheet URL. The rest follows from that."));

                return card;
            }

            card.Add(KUILayout.Gap(8f));
            card.Add(KUIText.KeyValue("Spreadsheet", provider.SpreadsheetId));
            card.Add(KUIText.KeyValue("Tabs", provider.IsMultiSheet
                ? string.Join(", ", provider.Sheets)
                : $"one flat tab, gid {provider.SheetGid}"));

            card.Add(KUILayout.Gap(8f));

            var test = KUIButton.Primary("Test Connection", () => TestFetch(provider));
            test.SetEnabled(!m_Busy);

            var discover = KUIButton.Secondary("Discover Tabs", () => Discover(provider));
            discover.SetEnabled(!m_Busy);

            card.Add(KUILayout.WrapRow(
                test,
                discover,
                KUIButton.Ghost("Open Sheet", () => Application.OpenURL(provider.SpreadsheetUrl))));

            return card;
        }

        // ---------------------------------------------------------------- 1. sheet

        private VisualElement BuildStepSheet(GoogleSheetsLocalizationProvider provider)
        {
            var done = !string.IsNullOrWhiteSpace(provider.SpreadsheetId);
            var section = Step(1, "The spreadsheet", done, "GoogleSheets.Step1");

            section.Add(KUIText.Body("Paste the URL from your browser. The id and tab are picked out of it."));
            section.Add(KUILayout.Gap(4f));

            // Delayed, or the callback fires on every keystroke: the first character fails to parse,
            // Report rebuilds the panel, and the field you are typing into is destroyed under you.
            var field = new TextField("Sheet URL") { multiline = false, isDelayed = true };

            field.RegisterValueChangedCallback(changed =>
            {
                if (string.IsNullOrWhiteSpace(changed.newValue)) return;

                Undo.RecordObject(provider, "Set Localization Sheet");

                if (!provider.ApplySheetUrl(changed.newValue))
                {
                    Report("That does not look like a Google Sheets URL.", KUITone.Error);
                    return;
                }

                Save(provider);
                Report("Spreadsheet set. Step 2 next.", KUITone.Success);
            });

            section.Add(field);

            if (!done)
            {
                section.Add(KUILayout.Gap(4f));
                section.Add(new KUIBanner(KUITone.Warning, "No spreadsheet — a fetch has nowhere to go."));

                return section;
            }

            section.Add(KUILayout.Gap(6f));
            section.Add(KUIText.KeyValue("Id", provider.SpreadsheetId));
            section.Add(KUIText.KeyValue("Reads",
                provider.IsMultiSheet ? "the tabs listed in step 3" : $"gid {provider.SheetGid}"));

            section.Add(KUILayout.Gap(6f));
            section.Add(KUIText.Code(provider.FetchUrl));

            section.Add(KUILayout.Gap(6f));
            section.Add(KUILayout.Row(
                KUIButton.Secondary("Open Spreadsheet", () => Application.OpenURL(provider.SpreadsheetUrl)),
                KUIButton.Ghost("Copy Fetch URL", () => Copy(provider.FetchUrl, "Fetch URL copied."))));

            return section;
        }

        // ---------------------------------------------------------------- 2. sharing

        private VisualElement BuildStepSharing(GoogleSheetsLocalizationProvider provider)
        {
            var section = Step(2, "Share it for reading", provider.CanFetch(), "GoogleSheets.Step2");

            section.Add(KUIText.Body(
                "Reading needs no credential at all, but the sheet has to be readable without a login. "
                + "In the spreadsheet: Share ▸ General access ▸ Anyone with the link ▸ Viewer."));

            section.Add(KUILayout.Gap(6f));
            section.Add(KUIText.Muted(
                "Google answers an unshared sheet with an HTML sign-in page — sometimes as a 401, "
                + "sometimes as a cheerful 200 — rather than an error, so Test Connection checks for "
                + "that by hand and says so plainly."));

            section.Add(KUILayout.Gap(6f));
            section.Add(KUILayout.Row(KUIButton.Secondary("Open Sharing Settings",
                () => Application.OpenURL(provider.SpreadsheetUrl))));

            return section;
        }

        // ---------------------------------------------------------------- 3. tabs

        private VisualElement BuildStepTabs(GoogleSheetsLocalizationProvider provider)
        {
            var section = Step(3, "One tab per category", provider.IsMultiSheet, "GoogleSheets.Step3", optional: true);

            section.Add(KUIText.Body(
                "The tab name is the category, and column A holds the key within it — the Popups tab "
                + "holding Settings/Title means Popups/Settings/Title. Tabs starting with an "
                + "underscore are skipped, so notes can live in the workbook."));

            section.Add(KUILayout.Gap(6f));

            var discover = KUIButton.Primary("Discover Tabs", () => Discover(provider));
            discover.SetEnabled(!m_Busy && !string.IsNullOrWhiteSpace(provider.SpreadsheetId));
            discover.tooltip = "Downloads the workbook and reads the tab names out of it. No credential needed.";

            var row = KUILayout.Row(discover);

            if (provider.IsMultiSheet)
            {
                row.Add(KUIButton.Ghost("Use A Single Tab", () =>
                {
                    Undo.RecordObject(provider, "Clear Localization Tabs");
                    provider.Sheets = Array.Empty<string>();

                    Save(provider);
                    Report("Back to reading one flat tab.", KUITone.Neutral);
                }));
            }

            section.Add(row);
            section.Add(KUILayout.Gap(6f));

            if (!provider.IsMultiSheet)
            {
                section.Add(KUIText.Muted(
                    "No tabs listed — reading the single tab from step 1. That is a perfectly good "
                    + "layout for a small catalog."));

                return section;
            }

            foreach (var sheet in provider.Sheets)
                section.Add(new KUIListRow(sheet).WithDot(KUITone.Success, "Read as a category"));

            return section;
        }

        // ---------------------------------------------------------------- 4. publishing

        private VisualElement BuildStepPublishing(GoogleSheetsLocalizationProvider provider)
        {
            var section = Step(4, "Publishing back", provider.CanUpload(), "GoogleSheets.Step4", optional: true);

            section.Add(KUIText.Body(
                "Google's own API needs an OAuth flow, a consent screen and a refresh token — none of "
                + "which a build machine can complete. The practical route is an Apps Script web app: "
                + "twenty lines living inside the spreadsheet, guarded by a shared secret."));

            section.Add(KUILayout.Gap(6f));
            section.Add(KUIText.SectionTitle("Do this once"));

            section.Add(Instruction(1, "Copy the script, then open the spreadsheet and choose Extensions ▸ Apps Script."));
            section.Add(Instruction(2, "Replace everything there with what you copied."));
            section.Add(Instruction(3, "Generate a secret below and paste it over change-me at the top."));
            section.Add(Instruction(4, "Deploy ▸ New deployment ▸ Web app. Execute as: Me. Who has access: Anyone."));
            section.Add(Instruction(5, "Paste the deployment URL into Web app URL below."));

            section.Add(KUILayout.Gap(6f));

            var script = FindScript();
            var buttons = KUILayout.WrapRow();

            var copy = KUIButton.Primary("Copy Apps Script", () =>
            {
                if (script == null)
                {
                    Report($"Could not find {ScriptAssetName}.gs.txt in the project.", KUITone.Error);
                    return;
                }

                Copy(script.text, "Apps Script copied — paste it into Extensions ▸ Apps Script.");
                Report("Apps Script copied to the clipboard.", KUITone.Success);
            });

            copy.SetEnabled(script != null);
            buttons.Add(copy);

            buttons.Add(KUIButton.Secondary("Open Apps Script", () =>
                Application.OpenURL(string.IsNullOrEmpty(provider.SpreadsheetUrl)
                    ? "https://script.google.com/home"
                    : provider.SpreadsheetUrl)));

            buttons.Add(KUIButton.Secondary("Generate Secret", () =>
            {
                var secret = NewSecret();

                Undo.RecordObject(provider, "Generate Localization Secret");
                SetSecret(provider, secret);
                Save(provider);

                EditorGUIUtility.systemCopyBuffer = secret;
                Report("Secret generated and copied. Paste it over change-me in the Apps Script.", KUITone.Success);
            }));

            section.Add(buttons);
            section.Add(KUILayout.Gap(8f));

            var serialized = new SerializedObject(provider);
            section.Add(KUIProperty.Field(serialized, "m_WebAppUrl", "Web app URL"));
            section.Add(KUIProperty.Field(serialized, "m_SharedSecret", "Shared secret"));

            section.Add(KUILayout.Gap(6f));

            var verify = KUIButton.Primary("Test Publishing", () => TestEndpoint(provider));
            verify.SetEnabled(!m_Busy && provider.CanUpload());
            verify.tooltip = "Asks the web app for the sheet. Reads only — it writes nothing.";

            section.Add(KUILayout.Row(
                verify,
                KUIButton.Secondary("Open Localization Manager", () => LocalizationKitWindow.OpenRemote())));

            section.Add(KUILayout.Gap(6f));

            if (provider.CanUpload())
            {
                section.Add(new KUIBanner(
                    KUITone.Success,
                    "Ready to publish",
                    "Remote ▸ Publish Catalog To Remote. The secret is stored on this asset, which "
                    + "makes the asset as sensitive as a password — keep it out of a public repository."));

                return section;
            }

            section.Add(new KUIBanner(
                KUITone.Neutral,
                "Read-only, which is a fine place to stop",
                !string.IsNullOrWhiteSpace(provider.WebAppUrl)
                    ? "No shared secret yet. It has to match SECRET in the Apps Script."
                    : "No web app URL yet. Nothing else needs this — fetching, merging, builds and the "
                      + "runtime refresh all work without it."));

            return section;
        }

        // ---------------------------------------------------------------- advanced

        private VisualElement BuildAdvanced(GoogleSheetsLocalizationProvider provider)
        {
            var section = new KUISection("All settings", false, "GoogleSheets.Advanced");
            section.Add(KUIProperty.InspectorCard(provider).Flat());

            return section;
        }

        // ---------------------------------------------------------------- operations

        private void TestFetch(GoogleSheetsLocalizationProvider provider)
        {
            m_Busy = true;
            Report("Fetching…", KUITone.Accent);

            provider.Fetch(result =>
            {
                m_Busy = false;

                if (!result.Success)
                {
                    Report(result.Error, KUITone.Error);
                    return;
                }

                var snapshot = result.Snapshot;
                var codes = new string[snapshot.LanguageCount];
                for (var i = 0; i < codes.Length; i++) codes[i] = snapshot.Languages[i].Code;

                var summary = $"{snapshot.RowCount} keys, {snapshot.LanguageCount} languages "
                    + $"({string.Join(", ", codes)}).";

                Report(
                    snapshot.Warnings.Count > 0
                        ? $"{summary} {snapshot.Warnings.Count} warning(s): {snapshot.Warnings[0]}"
                        : summary,
                    snapshot.Warnings.Count > 0 ? KUITone.Warning : KUITone.Success);
            });
        }

        private void Discover(GoogleSheetsLocalizationProvider provider)
        {
            m_Busy = true;
            Report("Reading the workbook…", KUITone.Accent);

            provider.DiscoverSheets((sheets, error) =>
            {
                m_Busy = false;

                if (sheets == null)
                {
                    Report(error, KUITone.Error);
                    return;
                }

                if (sheets.Length == 0)
                {
                    Report("The workbook has no tabs that are not ignored.", KUITone.Warning);
                    return;
                }

                Undo.RecordObject(provider, "Discover Localization Tabs");
                provider.Sheets = sheets;

                Save(provider);
                Report($"Found {sheets.Length} tabs: {string.Join(", ", sheets)}.", KUITone.Success);
            });
        }

        private void TestEndpoint(GoogleSheetsLocalizationProvider provider)
        {
            m_Busy = true;
            Report("Asking the web app…", KUITone.Accent);

            // A GET rather than a publish: this has to be safe to press, and a button that
            // overwrites a spreadsheet to prove it can reach it is not.
            var url = provider.WebAppUrl.Trim()
                + (provider.WebAppUrl.Contains("?") ? "&" : "?")
                + "token=" + UnityEngine.Networking.UnityWebRequest.EscapeURL(provider.Secret);

            LocalizationWeb.Get(url, response =>
            {
                m_Busy = false;

                if (!response.Success)
                {
                    Report(response.Error, KUITone.Error);
                    return;
                }

                var body = (response.Text ?? string.Empty).Trim();

                if (body.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
                {
                    Report(
                        body.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0
                            ? $"{body} — the secret here does not match SECRET in the Apps Script."
                            : body,
                        KUITone.Error);

                    return;
                }

                if (body.StartsWith("<", StringComparison.Ordinal))
                {
                    Report(
                        "The web app answered with HTML. That usually means the deployment is not set "
                        + "to 'Anyone', or this is the editor's URL rather than the deployment's.",
                        KUITone.Error);

                    return;
                }

                var rows = body.Split('\n').Length - 1;
                Report($"The web app answered with {rows} rows. Publishing is wired up.", KUITone.Success);
            }, timeoutSeconds: 30);
        }

        // ---------------------------------------------------------------- internals

        /// <summary>A numbered, collapsible step whose header says whether it is done.</summary>
        /// <remarks>
        /// Expanded while there is something to do and collapsed once there is not, so the panel
        /// gets quieter as the setup gets further along.
        /// </remarks>
        private static KUISection Step(int number, string title, bool done, string key, bool optional = false)
        {
            var section = new KUISection($"{number}. {title}", !done, key);

            section.WithHeaderAction(done
                ? new KUIBadge("Done", KUITone.Success)
                : new KUIBadge(optional ? "Optional" : "To do", optional ? KUITone.Neutral : KUITone.Warning));

            return section;
        }

        private static VisualElement Instruction(int number, string text)
        {
            var row = KUILayout.Row();

            row.Add(new KUIBadge(number.ToString(), KUITone.Neutral));
            row.Add(KUIText.FlexText(text));

            return row;
        }

        private static TextAsset FindScript()
        {
            foreach (var guid in AssetDatabase.FindAssets($"{ScriptAssetName} t:TextAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".gs.txt", StringComparison.OrdinalIgnoreCase)) continue;

                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                if (asset != null) return asset;
            }

            return null;
        }

        /// <summary>
        /// A secret long enough that the unguessable deployment URL stays the weaker of the two.
        /// </summary>
        private static string NewSecret()
        {
            // No l/1/I/O/0: this gets read off a screen and typed into a different browser tab.
            const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

            var bytes = new byte[32];
            using (var random = System.Security.Cryptography.RandomNumberGenerator.Create())
                random.GetBytes(bytes);

            var builder = new System.Text.StringBuilder(bytes.Length);
            foreach (var value in bytes) builder.Append(Alphabet[value % Alphabet.Length]);

            return builder.ToString();
        }

        private static void SetSecret(GoogleSheetsLocalizationProvider provider, string secret)
        {
            var serialized = new SerializedObject(provider);

            serialized.FindProperty("m_SharedSecret").stringValue = secret;
            serialized.ApplyModifiedProperties();
        }

        private static void Save(GoogleSheetsLocalizationProvider provider)
        {
            EditorUtility.SetDirty(provider);
            AssetDatabase.SaveAssetIfDirty(provider);
        }

        private static void Copy(string value, string message)
        {
            EditorGUIUtility.systemCopyBuffer = value ?? string.Empty;
            Debug.Log($"[LocalizationKit] {message}");
        }

        private void Report(string message, KUITone tone)
        {
            m_Status = message;
            m_Tone = tone;

            // Deferred: Report is called from inside change and click callbacks, and rebuilding the
            // tree while UI Toolkit is dispatching an event on it is a good way to lose the event.
            if (m_Root != null && m_Root.panel != null) m_Root.schedule.Execute(Rebuild);
            else Rebuild();
        }
    }
}
