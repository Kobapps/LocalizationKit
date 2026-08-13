using LocalizationKit.Editor;
using NUnit.Framework;
using UnityEngine;

namespace LocalizationKit.Tests
{
    /// <summary>
    /// The build post-processors' pure logic: which languages a build declares, and how a name is
    /// spelled for each platform's string table.
    /// </summary>
    /// <remarks>
    /// These are the parts that can be wrong without anything failing. A bad Android qualifier
    /// produces a resource folder the device never matches, and a mis-escaped app name either
    /// fails the Gradle build or ships a backslash on the home screen — neither shows up until an
    /// APK is on a phone, which is far too late to find out.
    /// </remarks>
    public sealed class BuildLocalizationTests
    {
        private LocalizationCatalog m_Catalog;

        [SetUp]
        public void SetUp() => m_Catalog = ScriptableObject.CreateInstance<LocalizationCatalog>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(m_Catalog);

        private void AddLanguages(params string[] codes)
        {
            foreach (var code in codes)
                m_Catalog.AddLanguage(new LanguageInfo(code, code));

            m_Catalog.DefaultLanguageCode = codes.Length > 0 ? codes[0] : null;
        }

        // ---------------------------------------------------------------- language codes

        [Test]
        public void LanguageCodes_AddTheBaseOfALoneVariant()
        {
            AddLanguages("en", "pt-BR");

            CollectionAssert.AreEquivalent(
                new[] { "en", "pt-BR", "pt" },
                LocalizationBuildData.LanguageCodes(m_Catalog),
                "A Portugal device should match a catalog that only carries Brazilian Portuguese.");
        }

        [Test]
        public void LanguageCodes_LeaveAnAmbiguousBaseAlone()
        {
            AddLanguages("en", "pt-BR", "pt-PT");

            CollectionAssert.AreEquivalent(
                new[] { "en", "pt-BR", "pt-PT" },
                LocalizationBuildData.LanguageCodes(m_Catalog),
                "With two variants there is no honest answer to what plain 'pt' means.");
        }

        [Test]
        public void LanguageCodes_DoNotDuplicateAnExplicitBase()
        {
            AddLanguages("pt", "pt-BR");

            CollectionAssert.AreEquivalent(new[] { "pt", "pt-BR" }, LocalizationBuildData.LanguageCodes(m_Catalog));
        }

        // ---------------------------------------------------------------- android qualifiers

        [Test]
        public void AndroidQualifier_UsesTheLegacyFormWhenItCan()
        {
            Assert.AreEqual("fr", LocalizationAndroidPostProcess.ResourceQualifier("fr"));
            Assert.AreEqual("pt-rBR", LocalizationAndroidPostProcess.ResourceQualifier("pt-BR"));
            Assert.AreEqual(
                "pt-rBR",
                LocalizationAndroidPostProcess.ResourceQualifier("PT-br"),
                "Android wants a lower-case language and an upper-case region whatever the catalog says.");
        }

        [Test]
        public void AndroidQualifier_FallsBackToBcp47ForAScriptSubtag()
        {
            Assert.AreEqual(
                "b+zh+Hans",
                LocalizationAndroidPostProcess.ResourceQualifier("zh-Hans"),
                "The legacy form cannot carry a script, so this has to use the BCP-47 spelling.");

            Assert.AreEqual("b+zh+Hant+TW", LocalizationAndroidPostProcess.ResourceQualifier("zh-Hant-TW"));
        }

        [Test]
        public void AndroidQualifier_HandlesANumericRegion()
        {
            Assert.AreEqual("es-r419", LocalizationAndroidPostProcess.ResourceQualifier("es-419"));
        }

        [Test]
        public void AndroidQualifier_RejectsMalformedCodes()
        {
            Assert.IsNull(LocalizationAndroidPostProcess.ResourceQualifier(null));
            Assert.IsNull(LocalizationAndroidPostProcess.ResourceQualifier("  "));
            Assert.IsNull(LocalizationAndroidPostProcess.ResourceQualifier("en-"));
            Assert.IsNull(LocalizationAndroidPostProcess.ResourceQualifier("-en"));
        }

        // ---------------------------------------------------------------- escaping

        [Test]
        public void AndroidEscape_CoversBothXmlAndTheResourceParser()
        {
            Assert.AreEqual("Tom &amp; Jerry", LocalizationAndroidPostProcess.EscapeForTests("Tom & Jerry"));

            Assert.AreEqual(
                "L\\'Atelier",
                LocalizationAndroidPostProcess.EscapeForTests("L'Atelier"),
                "An unescaped apostrophe is a resource-parser error, which fails the Gradle build.");

            Assert.AreEqual("&lt;b&gt;", LocalizationAndroidPostProcess.EscapeForTests("<b>"));
            Assert.AreEqual("say \\\"hi\\\"", LocalizationAndroidPostProcess.EscapeForTests("say \"hi\""));
        }

        [Test]
        public void AndroidEscape_OnlyEscapesALeadingAtOrQuestionMark()
        {
            Assert.AreEqual(
                "\\@home",
                LocalizationAndroidPostProcess.EscapeForTests("@home"),
                "A leading @ makes Android read the value as a resource reference.");

            Assert.AreEqual("me@home", LocalizationAndroidPostProcess.EscapeForTests("me@home"));
        }

        [Test]
        public void OneLine_FlattensLineBreaks()
        {
            Assert.AreEqual("a b c", LocalizationBuildData.OneLine("a\r\nb\nc"));
            Assert.AreEqual("trimmed", LocalizationBuildData.OneLine("  trimmed \n"));
        }

        // ---------------------------------------------------------------- app names

        [Test]
        public void AppNames_AreEmptyWhenNoKeyIsSet()
        {
            AddLanguages("en", "fr");

            var settings = ScriptableObject.CreateInstance<LocalizationSettings>();
            settings.Catalog = m_Catalog;

            CollectionAssert.IsEmpty(LocalizationBuildData.AppNames(settings, m_Catalog));

            Object.DestroyImmediate(settings);
        }

        [Test]
        public void AppNames_FallBackToTheDefaultLanguage()
        {
            AddLanguages("en", "fr");

            var entry = m_Catalog.AddEntry("App", "Name");
            entry.SetValue(0, "Merge Miner");
            entry.SetValue(1, null);

            var settings = ScriptableObject.CreateInstance<LocalizationSettings>();
            settings.Catalog = m_Catalog;
            settings.AppNameKey = "App/Name";

            var names = LocalizationBuildData.AppNames(settings, m_Catalog);

            Assert.AreEqual(2, names.Count);
            Assert.AreEqual("Merge Miner", names.Find(n => n.Code == "en").Name);
            Assert.AreEqual(
                "Merge Miner",
                names.Find(n => n.Code == "fr").Name,
                "An untranslated name beats a missing resource, which some launchers render as the raw key.");

            Object.DestroyImmediate(settings);
        }

        [Test]
        public void AppNames_StayOffWhenTheDefaultLanguageHasNoText()
        {
            AddLanguages("en", "fr");

            var entry = m_Catalog.AddEntry("App", "Name");
            entry.SetValue(1, "Mineur Fusion");

            var settings = ScriptableObject.CreateInstance<LocalizationSettings>();
            settings.Catalog = m_Catalog;
            settings.AppNameKey = "App/Name";

            CollectionAssert.IsEmpty(
                LocalizationBuildData.AppNames(settings, m_Catalog),
                "With no value to fall back to, half the languages would get a name and half would not.");

            Object.DestroyImmediate(settings);
        }
    }
}
