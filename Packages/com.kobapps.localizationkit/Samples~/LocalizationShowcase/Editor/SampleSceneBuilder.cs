// Deliberately no `using System;` — it would make the bare `Object` below ambiguous between
// System.Object and UnityEngine.Object, which is why System.Type is spelled out.
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace LocalizationKit.Samples.Editor
{
    /// <summary>
    /// Builds the showcase scene, its catalog and its settings asset in one command.
    /// </summary>
    /// <remarks>
    /// The scene is generated rather than shipped as a <c>.unity</c> file so it cannot arrive with
    /// broken references: a sample scene carries GUIDs pointing at the sample's own scripts and
    /// assets, and those break the moment anything is renamed or the sample is imported into a
    /// project whose GUIDs differ. Generating it means the sample is correct by construction.
    /// </remarks>
    internal static class SampleSceneBuilder
    {
        // Deliberately NOT under Assets/Samples/. That tree belongs to the Package Manager, which
        // owns Assets/Samples/<package>/<version>/<sample> and will overwrite or remove it on the
        // next import. Generated output that a user may then edit must not live somewhere another
        // tool treats as disposable.
        private const string Folder = "Assets/LocalizationShowcase";
        private const string CatalogPath = Folder + "/ShowcaseCatalog.asset";
        private const string ScenePath = Folder + "/LocalizationShowcase.unity";

        [MenuItem("Tools/LocalizationKit/Samples/Build Showcase Scene", priority = 300)]
        private static void Build()
        {
            Directory.CreateDirectory(Folder);
            Directory.CreateDirectory("Assets/Resources");
            AssetDatabase.Refresh();

            var catalog = BuildCatalog();
            BuildSettings(catalog);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            BuildUI();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[LocalizationKit] Showcase built at {ScenePath}. Press Play, then use the button to change language.");
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(ScenePath));
        }

        private static LocalizationCatalog BuildCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LocalizationCatalog>(CatalogPath);

            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<LocalizationCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            catalog.AddLanguage(new LanguageInfo("en", "English", SystemLanguage.English));
            catalog.AddLanguage(new LanguageInfo("fr", "Français", SystemLanguage.French));
            catalog.AddLanguage(new LanguageInfo("he", "עברית", SystemLanguage.Hebrew, rightToLeft: true));
            catalog.DefaultLanguageCode = "en";

            // The keys the generated scene actually binds to. Everything after this block is
            // filler — a catalog with six entries tells you nothing about how the manager behaves
            // at the size a real game reaches, so the sample ships enough rows to scroll, enough
            // categories to filter, and enough gaps to make the coverage column mean something.
            Set(catalog, "Default", "AppName", "LocalizationKit Showcase", "Vitrine LocalizationKit", "תצוגת LocalizationKit");
            Set(catalog, "Default", "Language", "Language", "Langue", "שפה");
            Set(catalog, "Store", "BuyButton", "Buy now", "Acheter", "קנה עכשיו");

            // Deliberately untranslated, to show fallback to the default language in the scene.
            Set(catalog, "Store", "Price", "Price: {0}", null, null);

            Set(catalog, "Popups/Quit", "Title", "Quit, really?", "Quitter, vraiment ?", "לצאת באמת?");
            Set(catalog, "Tutorials", "Step1", "Tap anywhere to begin", "Touchez pour commencer", "הקש כדי להתחיל");

            BuildFillerKeys(catalog);

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            return catalog;
        }

        private static void Set(LocalizationCatalog catalog, string category, string key, string en, string fr, string he)
        {
            var entry = catalog.AddEntry(category, key);
            entry.SetValue(0, en);
            entry.SetValue(1, fr);
            entry.SetValue(2, he);
        }

        private static void Note(LocalizationCatalog catalog, string category, string key, string note)
        {
            catalog.AddEntry(category, key).Description = note;
        }

        /// <summary>
        /// The rest of a plausible game's UI strings. Nothing in the scene reads these; they exist
        /// so the Localization Manager opens onto something worth looking at.
        /// </summary>
        /// <remarks>
        /// The gaps are deliberate and are not all the same shape: some keys have no translations
        /// at all, some are missing one language, and the long ones are long on purpose. Between
        /// them they cover the cases a key list has to survive — an empty term column, a partial
        /// coverage badge, and a string far wider than the column it is shown in.
        /// </remarks>
        private static void BuildFillerKeys(LocalizationCatalog catalog)
        {
            // ---------------------------------------------------------------- Default
            Set(catalog, "Default", "Yes", "Yes", "Oui", "כן");
            Set(catalog, "Default", "No", "No", "Non", "לא");
            Set(catalog, "Default", "Ok", "OK", "OK", "אישור");
            Set(catalog, "Default", "Cancel", "Cancel", "Annuler", "ביטול");
            Set(catalog, "Default", "Back", "Back", "Retour", "חזרה");
            Set(catalog, "Default", "Continue", "Continue", "Continuer", "המשך");
            Set(catalog, "Default", "Retry", "Try again", "Réessayer", "נסה שוב");
            Set(catalog, "Default", "Loading", "Loading…", "Chargement…", "טוען…");
            Set(catalog, "Default", "Close", "Close", "Fermer", null);
            Set(catalog, "Default", "Settings", "Settings", "Paramètres", "הגדרות");

            // ---------------------------------------------------------------- Menu
            Set(catalog, "Menu", "Play", "Play", "Jouer", "שחק");
            Set(catalog, "Menu", "Continue", "Continue your run", "Reprendre la partie", "המשך את המשחק");
            Set(catalog, "Menu", "Leaderboards", "Leaderboards", "Classements", null);
            Set(catalog, "Menu", "Credits", "Credits", "Crédits", null);
            Set(catalog, "Menu", "Quit", "Quit", "Quitter", "יציאה");

            // Three levels deep, to show that categories nest as far as you like: a full key splits
            // at its LAST separator, so Menu/Options/Audio/Master is the key "Master" in the
            // category "Menu/Options/Audio".
            Set(catalog, "Menu/Options", "Title", "Options", "Options", "אפשרויות");
            Set(catalog, "Menu/Options", "Apply", "Apply", "Appliquer", null);
            Set(catalog, "Menu/Options/Audio", "Master", "Master volume", "Volume général", "עוצמה כללית");
            Set(catalog, "Menu/Options/Audio", "Music", "Music", "Musique", "מוזיקה");
            Set(catalog, "Menu/Options/Audio", "Sfx", "Sound effects", "Effets sonores", null);
            Set(catalog, "Menu/Options/Video", "Quality", "Quality", "Qualité", null);
            Set(catalog, "Menu/Options/Video", "Fullscreen", "Fullscreen", "Plein écran", null);

            // ---------------------------------------------------------------- HUD
            Set(catalog, "HUD", "Score", "Score: {0}", "Score : {0}", "ניקוד: {0}");
            Set(catalog, "HUD", "Lives", "Lives: {0}", "Vies : {0}", "חיים: {0}");
            Set(catalog, "HUD", "Wave", "Wave {0} of {1}", "Vague {0} sur {1}", "גל {0} מתוך {1}");
            Set(catalog, "HUD", "Combo", "{0}× combo!", "Combo ×{0} !", null);
            Set(catalog, "HUD", "TimeLeft", "{0}s left", "{0} s restantes", null);

            // ---------------------------------------------------------------- Store
            Set(catalog, "Store", "Title", "Shop", "Boutique", "חנות");
            Set(catalog, "Store", "Restore", "Restore purchases", "Restaurer les achats", null);
            Set(catalog, "Store", "Owned", "Owned", "Acheté", "נרכש");
            Set(catalog, "Store", "NotEnoughCoins", "Not enough coins", "Pas assez de pièces", "אין מספיק מטבעות");
            Set(catalog, "Store", "BestValue", "Best value", "Meilleure offre", null);
            Set(catalog, "Store/Bundles", "Starter", "Starter bundle", "Pack de départ", null);
            Set(catalog, "Store/Bundles", "Season", "Season pass", "Passe saisonnier", null);

            // A deliberately long string: it must be clipped with an ellipsis in the key list's
            // term column rather than wrap and push the rows out of alignment.
            Set(
                catalog,
                "Store",
                "SubscriptionTerms",
                "Your subscription renews automatically unless it is cancelled at least 24 hours "
                + "before the end of the current period. You can manage it from your store account "
                + "at any time.",
                "Votre abonnement se renouvelle automatiquement sauf s'il est annulé au moins 24 "
                + "heures avant la fin de la période en cours. Vous pouvez le gérer depuis votre "
                + "compte à tout moment.",
                null);

            // ---------------------------------------------------------------- Popups
            Set(catalog, "Popups/Quit", "Body", "Your run will not be saved.", "Votre partie ne sera pas sauvegardée.", "המשחק לא יישמר.");
            Set(catalog, "Popups/Quit", "Confirm", "Quit", "Quitter", "צא");
            Set(catalog, "Popups/Rate", "Title", "Enjoying the game?", "Vous aimez le jeu ?", null);
            Set(catalog, "Popups/Rate", "Body", "A rating helps other players find it.", "Une note aide les autres joueurs à le découvrir.", null);
            Set(catalog, "Popups/Offline", "Title", "You are offline", "Vous êtes hors ligne", "אתה במצב לא מקוון");
            Set(catalog, "Popups/Offline", "Body", "Progress is saved on this device and syncs when you reconnect.", null, null);

            // ---------------------------------------------------------------- Tutorials
            Set(catalog, "Tutorials", "Step2", "Drag to aim", "Glissez pour viser", "גרור כדי לכוון");
            Set(catalog, "Tutorials", "Step3", "Release to fire", "Relâchez pour tirer", "שחרר כדי לירות");
            Set(catalog, "Tutorials", "Step4", "Collect coins to upgrade", "Ramassez des pièces pour améliorer", null);
            Set(catalog, "Tutorials", "Skip", "Skip tutorial", "Passer le tutoriel", null);

            // ---------------------------------------------------------------- Errors
            // An entire category with nothing but the default language, which is what a freshly
            // imported CSV looks like before anyone has translated it.
            Set(catalog, "Errors", "Generic", "Something went wrong.", null, null);
            Set(catalog, "Errors", "Network", "Check your connection and try again.", null, null);
            Set(catalog, "Errors", "PurchaseFailed", "The purchase could not be completed.", null, null);
            Set(catalog, "Errors", "SaveCorrupt", "Your save file could not be read.", null, null);

            // Notes are the one field a translator reads before anything else, so the sample shows
            // what a useful one looks like on the keys that need context.
            Note(catalog, "HUD", "Combo", "{0} is the combo multiplier, always 2 or more.");
            Note(catalog, "Store", "Price", "{0} is the localized price string from the store, already formatted.");
            Note(catalog, "Store", "BestValue", "Badge over the largest coin pack. Keep it under 12 characters.");
            Note(catalog, "Menu", "Continue", "Only shown when a saved run exists.");
        }

        private static void BuildSettings(LocalizationCatalog catalog)
        {
            var path = $"Assets/Resources/{LocalizationSettings.AssetName}.asset";
            var settings = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(path);

            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<LocalizationSettings>();
                AssetDatabase.CreateAsset(settings, path);
            }

            settings.Catalog = catalog;
            settings.StartupLanguage = StartupLanguageMode.DefaultLanguage;

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        private static void BuildUI()
        {
            var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasObject.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1280f, 720f);

            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                CreateEventSystem();

            // A LocalizedText component: no script involved, key picked in the inspector.
            var title = CreateText(canvasObject.transform, "Title", new Vector2(0f, -60f), new Vector2(900f, 60f), 34, TextAnchor.MiddleCenter);
            var titleLocalized = title.gameObject.AddComponent<LocalizedText>();
            titleLocalized.Key = "Default/AppName";

            var buy = CreateText(canvasObject.transform, "BuyLabel", new Vector2(0f, -120f), new Vector2(900f, 40f), 22, TextAnchor.MiddleCenter);
            var buyLocalized = buy.gameObject.AddComponent<LocalizedText>();
            buyLocalized.Key = "Store/BuyButton";
            buyLocalized.Case = LocalizedTextCase.Upper;

            // The attribute-driven panel writes into this one.
            var output = CreateText(canvasObject.transform, "Output", new Vector2(0f, 60f), new Vector2(900f, 320f), 18, TextAnchor.UpperLeft);

            var switcherObject = new GameObject("LanguageSwitcher", typeof(RectTransform));
            switcherObject.transform.SetParent(canvasObject.transform, false);

            var buttonObject = new GameObject("NextLanguage", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(switcherObject.transform, false);
            Place(buttonObject.GetComponent<RectTransform>(), new Vector2(0f, -280f), new Vector2(260f, 44f));
            buttonObject.GetComponent<Image>().color = new Color(0.22f, 0.45f, 0.85f);

            var buttonLabel = CreateText(buttonObject.transform, "Label", Vector2.zero, new Vector2(260f, 44f), 18, TextAnchor.MiddleCenter);
            buttonLabel.color = Color.white;

            // Localized rather than hard-coded, because a hard-coded label in a localization
            // sample teaches the wrong thing — and this one visibly changes with the language
            // it switches, which is the point being demonstrated.
            buttonLabel.Localize("Default/Language");

            var current = CreateText(switcherObject.transform, "Current", new Vector2(0f, -324f), new Vector2(400f, 30f), 16, TextAnchor.MiddleCenter);

            var switcher = switcherObject.AddComponent<LanguageSwitcher>();
            Assign(switcher, "m_NextButton", buttonObject.GetComponent<Button>());
            Assign(switcher, "m_CurrentLabel", current);

            var panelObject = new GameObject("ShowcasePanel");
            var panel = panelObject.AddComponent<ShowcasePanel>();
            Assign(panel, "m_Output", output);
            Assign(panel, "m_ExtraKey", "Popups/Quit/Title");
        }

        /// <summary>
        /// Adds an EventSystem with an input module that matches the project's active input
        /// handling.
        /// </summary>
        /// <remarks>
        /// <c>StandaloneInputModule</c> reads <c>UnityEngine.Input</c>, which <b>throws</b> when
        /// Active Input Handling is set to "Input System Package (New)" — so a sample that
        /// hard-codes it builds a scene whose buttons do nothing, in exactly the projects most
        /// likely to be on Unity 6.
        /// <para>
        /// The new module is found by name rather than referenced, deliberately. An assembly
        /// definition resolves its references regardless of <c>#if</c>, so naming
        /// <c>Unity.InputSystem</c> would make this sample fail to compile in any project without
        /// that package installed. Reflection costs one lookup, once, and works in all three
        /// configurations.
        /// </para>
        /// </remarks>
        private static void CreateEventSystem()
        {
            var eventSystem = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));

            var newModule = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (newModule != null)
            {
                eventSystem.AddComponent(newModule);
                return;
            }

            eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        private static Text CreateText(
            Transform parent,
            string name,
            Vector2 position,
            Vector2 size,
            int fontSize,
            TextAnchor anchor)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);

            Place(textObject.GetComponent<RectTransform>(), position, size);

            var text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = new Color(0.92f, 0.92f, 0.94f);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            return text;
        }

        private static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        /// <summary>
        /// Assigns a private <c>[SerializeField]</c> through SerializedObject rather than
        /// reflection, so the write goes through Unity's own serialization and survives the scene
        /// save exactly as an inspector edit would.
        /// </summary>
        private static void Assign(Object target, string field, object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);

            if (property == null)
            {
                Debug.LogWarning($"[LocalizationKit] Sample builder could not find '{field}' on {target.GetType().Name}.");
                return;
            }

            if (value is string text) property.stringValue = text;
            else property.objectReferenceValue = value as Object;

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
