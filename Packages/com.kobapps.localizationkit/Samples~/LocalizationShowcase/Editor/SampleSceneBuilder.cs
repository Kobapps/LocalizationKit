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

            Set(catalog, "Default", "AppName", "LocalizationKit Showcase", "Vitrine LocalizationKit", "תצוגת LocalizationKit");
            Set(catalog, "Default", "Language", "Language", "Langue", "שפה");
            Set(catalog, "Store", "BuyButton", "Buy now", "Acheter", "קנה עכשיו");

            // Deliberately untranslated, to show fallback to the default language in the scene.
            Set(catalog, "Store", "Price", "Price: {0}", null, null);

            Set(catalog, "Popups/Quit", "Title", "Quit, really?", "Quitter, vraiment ?", "לצאת באמת?");
            Set(catalog, "Tutorials", "Step1", "Tap anywhere to begin", "Touchez pour commencer", "הקש כדי להתחיל");

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
