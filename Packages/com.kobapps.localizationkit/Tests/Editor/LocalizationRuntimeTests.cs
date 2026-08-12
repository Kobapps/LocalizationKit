using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LocalizationKit.Tests
{
    /// <summary>
    /// Table building, lookup, fallback and the binder.
    /// </summary>
    /// <remarks>
    /// <see cref="Localization"/> is static, so every test resets it. Without that, one test's
    /// table and registrations leak into the next — and with domain reload disabled they leak into
    /// the next play session too, which is exactly what <see cref="Localization.Reset"/> is for.
    /// </remarks>
    public sealed class LocalizationRuntimeTests
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

            var only = m_Catalog.AddEntry("Store", "EnglishOnly");
            only.SetValue(0, "English only");

            var nothing = m_Catalog.AddEntry("Store", "Nothing");
        }

        [TearDown]
        public void TearDown()
        {
            Localization.Reset();
            Object.DestroyImmediate(m_Catalog);
        }

        private void Install(MissingKeyBehavior behavior = MissingKeyBehavior.ReturnKey, string language = "en") =>
            Localization.SetTable(LocalizationTable.Build(m_Catalog, behavior), language);

        [Test]
        public void Get_ReturnsTextInActiveLanguage()
        {
            Install();
            Assert.AreEqual("Buy", Localization.Get("Store/Buy"));

            Localization.SetLanguage("fr");
            Assert.AreEqual("Acheter", Localization.Get("Store/Buy"));
        }

        [Test]
        public void Get_FallsBackToDefaultLanguage()
        {
            Install(language: "fr");

            Assert.AreEqual("English only", Localization.Get("Store/EnglishOnly"));
        }

        [Test]
        public void Get_UsesMissingBehaviorWhenNoLanguageHasText()
        {
            Install(MissingKeyBehavior.ReturnMarker);
            Assert.AreEqual("#Store/Nothing#", Localization.Get("Store/Nothing"));

            Install(MissingKeyBehavior.ReturnEmpty);
            Assert.AreEqual(string.Empty, Localization.Get("Store/Nothing"));

            Install();
            Assert.AreEqual("Store/Nothing", Localization.Get("Store/Nothing"));
        }

        [Test]
        public void Get_UnknownKeyReturnsTheKey()
        {
            Install();

            // A miss warns, and an unexpected warning does not fail a test — only Error,
            // Exception and Assert do — so this needs no LogAssert guard.
            Assert.AreEqual("No/Such/Key", Localization.Get("No/Such/Key"));
        }

        [Test]
        public void SetLanguage_RejectsUnknownCode()
        {
            Install();

            Assert.IsFalse(Localization.SetLanguage("de"));
            Assert.AreEqual("en", Localization.LanguageCode);
        }

        [Test]
        public void Handle_ReadsWithoutTheDictionary()
        {
            Install();

            var handle = Localization.Resolve("Store/Buy");
            Assert.IsTrue(handle.IsValid);
            Assert.AreEqual("Buy", Localization.GetValue(ref handle));

            Localization.SetLanguage("fr");
            Assert.AreEqual("Acheter", Localization.GetValue(ref handle));
        }

        [Test]
        public void Handle_ReResolvesAfterTheTableIsReplaced()
        {
            Install();
            var handle = Localization.Resolve("Store/Buy");

            // Shift the key's position so a stale index would read the wrong row rather than
            // throwing — the failure this version check exists to prevent.
            var inserted = m_Catalog.AddEntry("Aaa", "First");
            inserted.SetValue(0, "first");
            Install();

            Assert.AreEqual("Buy", Localization.GetValue(ref handle));
        }

        [Test]
        public void Table_FlattensCategoriesIntoFullKeys()
        {
            var table = LocalizationTable.Build(m_Catalog);

            Assert.IsTrue(table.Contains("Store/Buy"));
            Assert.IsFalse(table.Contains("Buy"));
        }

        [Test]
        public void Table_BuildFromNullCatalogIsEmptyNotNull()
        {
            var table = LocalizationTable.Build(null);

            Assert.IsNotNull(table);
            Assert.AreEqual(0, table.KeyCount);
            Assert.AreEqual(string.Empty, table.GetValue(0));
        }

        [Test]
        public void Binder_AppliesOnRegisterAndOnLanguageChange()
        {
            Install();

            var probe = new Probe();
            var subscription = LocalizationBinder.Register(probe);

            Assert.AreEqual(1, probe.Applied, "Registering should apply immediately.");

            Localization.SetLanguage("fr");
            Assert.AreEqual(2, probe.Applied);

            LocalizationBinder.Unregister(ref subscription);
            Localization.SetLanguage("en");
            Assert.AreEqual(2, probe.Applied, "An unregistered object must stop receiving updates.");
        }

        [Test]
        public void Binder_UnregisterTwiceIsHarmless()
        {
            var probe = new Probe();
            var subscription = LocalizationBinder.Register(probe);

            LocalizationBinder.Unregister(ref subscription);
            Assert.DoesNotThrow(() => LocalizationBinder.Unregister(ref subscription));
        }

        [Test]
        public void Binder_RecyclesSlots()
        {
            Install();

            var before = LocalizationBinder.Count;
            var subscriptions = new LocalizationSubscription[50];
            var probes = new Probe[50];

            for (var i = 0; i < probes.Length; i++)
            {
                probes[i] = new Probe();
                subscriptions[i] = LocalizationBinder.Register(probes[i]);
            }

            Assert.AreEqual(before + 50, LocalizationBinder.Count);

            for (var i = 0; i < probes.Length; i++)
                LocalizationBinder.Unregister(ref subscriptions[i]);

            Assert.AreEqual(before, LocalizationBinder.Count);
        }

        [Test]
        public void Binder_OneThrowingObjectDoesNotStopTheRest()
        {
            Install();

            var thrower = new ThrowingProbe();
            var healthy = new Probe();

            // Registered first, so ApplyAll reaches it before the healthy one — which is the
            // ordering that makes this test mean anything.
            var a = default(LocalizationSubscription);
            var b = default(LocalizationSubscription);

            // Register applies immediately, so the binder logs the first exception during
            // Register rather than during SetLanguage. Both have to sit inside the guard, or
            // the test fails on an exception it deliberately provoked.
            LogAssert.ignoreFailingMessages = true;
            try
            {
                a = LocalizationBinder.Register(thrower);
                b = LocalizationBinder.Register(healthy);

                Localization.SetLanguage("fr");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }

            Assert.AreEqual(2, healthy.Applied, "A throwing handler must not abort the loop.");

            LocalizationBinder.Unregister(ref a);
            LocalizationBinder.Unregister(ref b);
        }

        private sealed class Probe : ILocalizedObject
        {
            internal int Applied;
            public void ApplyLocalization() => Applied++;
        }

        private sealed class ThrowingProbe : ILocalizedObject
        {
            public void ApplyLocalization() => throw new System.InvalidOperationException("deliberate");
        }
    }
}
