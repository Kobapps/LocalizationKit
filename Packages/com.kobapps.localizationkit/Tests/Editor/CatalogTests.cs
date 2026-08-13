using NUnit.Framework;
using UnityEngine;

namespace LocalizationKit.Tests
{
    /// <summary>
    /// The catalog's editing operations, which have to keep every entry's positional value array in
    /// step with the language list. Getting this wrong shifts every translation by one language —
    /// silently, across the whole project — so it is tested harder than anything else here.
    /// </summary>
    public sealed class CatalogTests
    {
        private LocalizationCatalog m_Catalog;

        [SetUp]
        public void SetUp()
        {
            m_Catalog = ScriptableObject.CreateInstance<LocalizationCatalog>();

            m_Catalog.AddLanguage(new LanguageInfo("en", "English"));
            m_Catalog.AddLanguage(new LanguageInfo("fr", "Français"));
            m_Catalog.AddLanguage(new LanguageInfo("he", "עברית", SystemLanguage.Hebrew, rightToLeft: true));
            m_Catalog.DefaultLanguageCode = "en";

            var entry = m_Catalog.AddEntry("Store", "Buy");
            entry.SetValue(0, "Buy");
            entry.SetValue(1, "Acheter");
            entry.SetValue(2, "קנה");
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(m_Catalog);

        private LocalizationEntry Entry => m_Catalog.FindByFullKey("Store/Buy");

        [Test]
        public void AddLanguage_WidensExistingEntries()
        {
            m_Catalog.AddLanguage(new LanguageInfo("de", "Deutsch"));

            Assert.AreEqual(4, Entry.Values.Length);
            Assert.AreEqual("Buy", Entry.GetValue(0));
            Assert.IsNull(Entry.GetValue(3));
        }

        [Test]
        public void AddLanguage_IsIdempotentByCode()
        {
            var first = m_Catalog.AddLanguage(new LanguageInfo("EN", "English again"));

            Assert.AreEqual(0, first, "A code already present should return its existing index.");
            Assert.AreEqual(3, m_Catalog.Languages.Count);
        }

        [Test]
        public void RemoveLanguage_CollapsesOnlyThatColumn()
        {
            m_Catalog.RemoveLanguage("fr");

            Assert.AreEqual(2, m_Catalog.Languages.Count);
            Assert.AreEqual("Buy", Entry.GetValue(0), "English must not shift.");
            Assert.AreEqual("קנה", Entry.GetValue(1), "Hebrew must move down into the freed slot.");
        }

        [Test]
        public void RemoveLanguage_UnknownCodeChangesNothing()
        {
            Assert.IsFalse(m_Catalog.RemoveLanguage("nope"));
            Assert.AreEqual(3, m_Catalog.Languages.Count);
        }

        [Test]
        public void MoveLanguage_CarriesTextWithIt()
        {
            m_Catalog.MoveLanguage(0, 2);   // en to the end

            Assert.AreEqual("fr", m_Catalog.Languages[0].Code);
            Assert.AreEqual("he", m_Catalog.Languages[1].Code);
            Assert.AreEqual("en", m_Catalog.Languages[2].Code);

            Assert.AreEqual("Acheter", Entry.GetValue(0));
            Assert.AreEqual("קנה", Entry.GetValue(1));
            Assert.AreEqual("Buy", Entry.GetValue(2));
        }

        [Test]
        public void ResizeEntries_RepairsRaggedArrays()
        {
            // What a bad merge or a hand-edited asset leaves behind.
            Entry.Values = new[] { "only-one" };

            m_Catalog.ResizeEntries();

            Assert.AreEqual(3, Entry.Values.Length);
            Assert.AreEqual("only-one", Entry.GetValue(0));
            Assert.IsNull(Entry.GetValue(2));
        }

        [Test]
        public void DefaultLanguageCode_FallsBackWhenUnset()
        {
            m_Catalog.DefaultLanguageCode = "does-not-exist";

            Assert.AreEqual("en", m_Catalog.DefaultLanguageCode, "An unknown default falls back to the first language.");
        }

        [Test]
        public void AddEntry_ReturnsExistingWhenKeyIsTaken()
        {
            var again = m_Catalog.AddEntry("Store", "Buy");

            Assert.AreSame(Entry, again);
            Assert.AreEqual(1, m_Catalog.EntryCount);
        }

        [Test]
        public void AddEntry_CreatesTheCategory()
        {
            m_Catalog.AddEntry("Tutorials", "Step1");

            Assert.IsNotNull(m_Catalog.FindCategory("Tutorials"));
            Assert.AreEqual(3, m_Catalog.FindByFullKey("Tutorials/Step1").Values.Length);
        }

        [Test]
        public void FindByFullKey_HandlesNestedCategories()
        {
            m_Catalog.AddEntry("Popups/Quit", "Title");

            Assert.IsNotNull(m_Catalog.FindByFullKey("Popups/Quit/Title"));
        }

        [Test]
        public void FindByFullKey_HandlesDeeplyNestedCategories()
        {
            m_Catalog.AddEntry("Popups/Quit Level/Confirm", "Title");

            Assert.IsNotNull(
                m_Catalog.FindByFullKey("Popups/Quit Level/Confirm/Title"),
                "A full key splits at its last separator, so nesting has no depth limit.");
        }

        [Test]
        public void IsValidCategory_AcceptsNesting()
        {
            Assert.IsTrue(LocalizationKeys.IsValidCategory("Popups"));
            Assert.IsTrue(LocalizationKeys.IsValidCategory("Popups/Quit Level"));
            Assert.IsTrue(LocalizationKeys.IsValidCategory("A/B/C/D"));
        }

        [Test]
        public void IsValidCategory_RejectsEmptySegments()
        {
            Assert.IsFalse(LocalizationKeys.IsValidCategory(null));
            Assert.IsFalse(LocalizationKeys.IsValidCategory(string.Empty));
            Assert.IsFalse(LocalizationKeys.IsValidCategory("   "));
            Assert.IsFalse(LocalizationKeys.IsValidCategory("/Popups"));
            Assert.IsFalse(LocalizationKeys.IsValidCategory("Popups/"));
            Assert.IsFalse(LocalizationKeys.IsValidCategory("Popups//Quit"));
            Assert.IsFalse(LocalizationKeys.IsValidCategory("Popups/ /Quit"));
        }

        [Test]
        public void IsValidName_StillRejectsSeparators()
        {
            Assert.IsTrue(LocalizationKeys.IsValidName("Title"));
            Assert.IsFalse(
                LocalizationKeys.IsValidName("Quit/Title"),
                "Categories nest; keys do not, or the split would land in the wrong place.");
        }

        [Test]
        public void CategoryPaths_IncludeTheBaseCategoriesTheStoredOnesImply()
        {
            m_Catalog.AddEntry("Menu/Options/Audio", "Master");
            m_Catalog.AddEntry("Menu/Options/Video", "Quality");

            var paths = Editor.LocalizationEditorCatalog.CategoryPaths(m_Catalog);

            CollectionAssert.AreEqual(
                new[] { "Store", "Menu", "Menu/Options", "Menu/Options/Audio", "Menu/Options/Video" },
                paths,
                "Menu and Menu/Options hold no keys of their own, but a new key can still go in them, "
                + "and a parent has to come before its children for a nested menu to read right.");
        }

        [Test]
        public void CategoryPaths_ListEachPathOnce()
        {
            m_Catalog.AddEntry("Popups/Quit", "Title");
            m_Catalog.AddEntry("Popups/Rate", "Title");
            m_Catalog.AddEntry("Popups", "Shared");

            var paths = Editor.LocalizationEditorCatalog.CategoryPaths(m_Catalog);

            CollectionAssert.AreEqual(new[] { "Store", "Popups", "Popups/Quit", "Popups/Rate" }, paths);
        }

        [Test]
        public void IsUnder_MatchesTheSubtreeAndNothingElse()
        {
            Assert.IsTrue(LocalizationKeys.IsUnder("Popups", "Popups"));
            Assert.IsTrue(LocalizationKeys.IsUnder("Popups/Quit", "Popups"));
            Assert.IsTrue(LocalizationKeys.IsUnder("Popups/Quit/Confirm", "Popups"));
            Assert.IsTrue(LocalizationKeys.IsUnder("popups/quit", "Popups"), "Categories compare case-insensitively.");

            Assert.IsFalse(LocalizationKeys.IsUnder("PopupsExtra", "Popups"), "A prefix is not a parent.");
            Assert.IsFalse(LocalizationKeys.IsUnder("Store", "Popups"));
            Assert.IsFalse(LocalizationKeys.IsUnder("Popups", "Popups/Quit"));

            Assert.IsTrue(LocalizationKeys.IsUnder("Anything", null), "No filter means everything matches.");
        }
    }
}
