using EditorCoreKit.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LocalizationKit.Editor
{
    /// <summary>
    /// The inspector for every shipped localized text component.
    /// </summary>
    /// <remarks>
    /// It adds two things the default inspector cannot: a live preview of the selected key in each
    /// language, and — the reason it exists — a one-click path from a label someone already typed
    /// into the scene to a catalog entry. That flow is where localization is usually abandoned:
    /// the text is already in the scene, moving it into a catalog by hand is tedious, so it stays
    /// hard-coded.
    /// </remarks>
    [CustomEditor(typeof(LocalizedTextBase), editorForChildClasses: true)]
    [CanEditMultipleObjects]
    internal sealed class LocalizedTextInspector : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            KUITheme.Apply(root);

            KUIProperty.DrawAll(root, serializedObject, null);

            root.Add(KUILayout.Gap(6f));
            root.Add(BuildPreview());

            return root;
        }

        private VisualElement BuildPreview()
        {
            var component = target as LocalizedTextBase;
            var catalog = LocalizationEditorCatalog.Catalog;

            if (catalog == null)
            {
                return new KUIBanner(KUITone.Warning, "No localization catalog in this project.")
                    .WithAction("Create One", () => LocalizationKitWindow.Open());
            }

            if (component == null) return new VisualElement();

            var key = component.Key;

            if (string.IsNullOrEmpty(key))
                return BuildAdoptCard(component, catalog);

            var entry = catalog.FindByFullKey(key);

            if (entry == null)
            {
                return new KUIBanner(
                        KUITone.Error,
                        "Key not in the catalog",
                        $"'{key}' does not exist, so this shows the key itself at runtime.")
                    .WithAction("Open Catalog", () => LocalizationKitWindow.OpenAt(key));
            }

            var card = new KUICard(key, "How this reads in each language.");

            card.WithHeaderAction(KUIButton.Secondary("Edit…", () => LocalizationKitWindow.OpenAt(key)));

            for (var i = 0; i < catalog.Languages.Count; i++)
            {
                var text = entry.GetValue(i);
                var language = catalog.Languages[i];

                if (string.IsNullOrEmpty(text))
                {
                    var row = KUILayout.Row();
                    row.Add(KUIText.FlexText("—"));
                    row.Add(new KUIBadge("missing", KUITone.Warning));
                    card.Add(KUIText.KeyValue(language.DisplayName, row));
                }
                else
                {
                    card.Add(KUIText.KeyValue(language.DisplayName, text));
                }
            }

            return card;
        }

        /// <summary>
        /// Offers to turn whatever the widget currently says into a catalog entry — the single
        /// most common thing someone wants when they add this component to an existing label.
        /// </summary>
        private VisualElement BuildAdoptCard(LocalizedTextBase component, LocalizationCatalog catalog)
        {
            var existing = component.ReadCurrentText();

            if (string.IsNullOrWhiteSpace(existing))
                return new KUIBanner(KUITone.Accent, "Pick a key above to localize this text.");

            var trimmed = existing.Trim();
            var preview = trimmed.Length > 40 ? trimmed.Substring(0, 40) + "…" : trimmed;

            var card = new KUICard("Not localized yet", $"This currently reads “{preview}”.");

            card.Add(KUIButton.Primary("Create a Key From This Text…", () =>
            {
                var suggested = SuggestKey(component, trimmed);

                LocalizationKeyDialog.Open(
                    "Create Key",
                    "The text below becomes this key's default-language value.",
                    LocalizationKeys.DefaultCategory,
                    suggested,
                    CategoryNames(catalog),

                    // Only the character check: unlike the manager's "New Key", naming a key that
                    // already exists is a valid outcome here — it points this component at it.
                    (_, key) => string.IsNullOrWhiteSpace(key) || LocalizationKeys.IsValidName(key)
                        ? null
                        : "A key cannot contain '/'.",
                    (category, key) =>
                    {
                        var full = LocalizationKeys.Compose(category, key);

                        if (catalog.FindByFullKey(full) == null)
                        {
                            Undo.RecordObject(catalog, "Create Localization Key");

                            var entry = catalog.AddEntry(category, key);

                            // Seed the default language with the text that was already there, which
                            // is what makes this one click instead of two.
                            var defaultLanguage = catalog.IndexOfLanguage(catalog.DefaultLanguageCode);
                            if (defaultLanguage >= 0) entry.SetValue(defaultLanguage, trimmed);

                            LocalizationEditorCatalog.Save(catalog);
                        }

                        Undo.RecordObject(component, "Assign Localization Key");
                        component.Key = full;
                        EditorUtility.SetDirty(component);
                    });
            }));

            return card;
        }

        private static string SuggestKey(LocalizedTextBase component, string text)
        {
            // The GameObject's name is almost always a better key than the text — it survives the
            // copy being rewritten, which is the whole point of a key.
            var name = component.gameObject.name;
            var candidate = string.IsNullOrWhiteSpace(name) ? text : name;

            var builder = new System.Text.StringBuilder(candidate.Length);

            foreach (var ch in candidate)
            {
                if (char.IsLetterOrDigit(ch) || ch == '_') builder.Append(ch);
                else if (ch == ' ' || ch == '-') continue;
            }

            return builder.Length == 0 ? "NewKey" : builder.ToString();
        }

        /// <summary>
        /// Every category a key can go in — the stored ones and the base categories they imply.
        /// </summary>
        private static System.Collections.Generic.List<string> CategoryNames(LocalizationCatalog catalog)
        {
            var names = LocalizationEditorCatalog.CategoryPaths(catalog);

            if (names.Count == 0) names.Add(LocalizationKeys.DefaultCategory);

            return names;
        }
    }
}
