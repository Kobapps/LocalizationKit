using NUnit.Framework;
using UnityEngine;

namespace LocalizationKit.Tests
{
    /// <summary>
    /// Proves the source generator actually ran inside Unity.
    /// </summary>
    /// <remarks>
    /// This is the test worth having above all the others here, because the way a Unity source
    /// generator fails is silent: if the analyzer DLL loses its <c>RoslynAnalyzer</c> asset label,
    /// or is built against a Roslyn newer than Unity's, the compiler simply does not run it. There
    /// is no error — the generated members just are not there. That turns into a compile error on
    /// <c>EnableLocalization</c> below, which is exactly the signal wanted.
    /// </remarks>
    /// <summary>
    /// A plain class, so the generator emits binding methods but no Unity lifecycle. Declared at
    /// namespace level rather than nested in the fixture because the kit's own LK006 rule requires
    /// every containing type of a bound class to be partial too — and a test fixture that has to be
    /// partial to hold its subject would be a poor advertisement for the rule.
    /// </summary>
    internal partial class BindingSubject
    {
        [Localized("Store/Buy")] private string m_Buy;
        [Localized("Store/Sell")] private string m_Sell;

        internal string Buy => m_Buy;
        internal string Sell => m_Sell;
        internal int AppliedCount;

        partial void OnLocalizationApplied() => AppliedCount++;
    }

    public sealed class GeneratedBindingTests
    {
        private LocalizationCatalog m_Catalog;

        [SetUp]
        public void SetUp()
        {
            Localization.Reset();

            m_Catalog = ScriptableObject.CreateInstance<LocalizationCatalog>();
            m_Catalog.AddLanguage(new LanguageInfo("en", "English"));
            m_Catalog.AddLanguage(new LanguageInfo("fr", "Français"));
            m_Catalog.DefaultLanguageCode = "en";

            var buy = m_Catalog.AddEntry("Store", "Buy");
            buy.SetValue(0, "Buy");
            buy.SetValue(1, "Acheter");

            var sell = m_Catalog.AddEntry("Store", "Sell");
            sell.SetValue(0, "Sell");
            sell.SetValue(1, "Vendre");

            Localization.SetTable(LocalizationTable.Build(m_Catalog), "en");
        }

        [TearDown]
        public void TearDown()
        {
            Localization.Reset();
            Object.DestroyImmediate(m_Catalog);
        }

        [Test]
        public void Generator_ImplementsTheInterface()
        {
            Assert.IsInstanceOf<ILocalizedObject>(new BindingSubject(), "The generated partial should implement ILocalizedObject.");
        }

        [Test]
        public void Generator_FillsFieldsOnEnable()
        {
            var subject = new BindingSubject();
            subject.EnableLocalization();

            try
            {
                Assert.AreEqual("Buy", subject.Buy);
                Assert.AreEqual("Sell", subject.Sell);
                Assert.AreEqual(1, subject.AppliedCount);
            }
            finally
            {
                subject.DisableLocalization();
            }
        }

        [Test]
        public void Generator_FollowsLanguageChanges()
        {
            var subject = new BindingSubject();
            subject.EnableLocalization();

            try
            {
                Localization.SetLanguage("fr");

                Assert.AreEqual("Acheter", subject.Buy);
                Assert.AreEqual("Vendre", subject.Sell);
                Assert.AreEqual(2, subject.AppliedCount);
            }
            finally
            {
                subject.DisableLocalization();
            }
        }

        [Test]
        public void Generator_StopsAfterDisable()
        {
            var subject = new BindingSubject();
            subject.EnableLocalization();
            subject.DisableLocalization();

            Localization.SetLanguage("fr");

            Assert.AreEqual("Buy", subject.Buy, "A disabled object must not be updated.");
        }
    }
}
