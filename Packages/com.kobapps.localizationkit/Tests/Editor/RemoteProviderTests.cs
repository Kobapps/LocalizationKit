using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace LocalizationKit.Tests
{
    /// <summary>
    /// The remote seam: snapshots, the merge policy, and what a fetch does to the active table.
    /// </summary>
    /// <remarks>
    /// Nothing here touches the network. A provider is an interface precisely so the interesting
    /// half — what happens to what came back — can be tested against a fixture, and a test that
    /// needs a Google account is a test that fails on a build machine.
    /// </remarks>
    public sealed class RemoteProviderTests
    {
        private const string Csv =
            "Key,en,fr\n"
            + "Store/Buy,Buy,Acheter\n"
            + "Popups/Quit/Title,\"Quit, really?\",\n";

        [TearDown]
        public void TearDown()
        {
            Localization.Reset();
            LocalizationRemote.Reset();
        }

        // ---------------------------------------------------------------- snapshot

        [Test]
        public void Snapshot_ReadsCsv()
        {
            var snapshot = LocalizationSnapshot.FromCsv(Csv);

            Assert.AreEqual(2, snapshot.RowCount);
            Assert.AreEqual(2, snapshot.LanguageCount);
            Assert.AreEqual("Acheter", snapshot.GetValue("Store/Buy", "fr"));
            Assert.AreEqual("Quit, really?", snapshot.GetValue("Popups/Quit/Title", "en"));
        }

        [Test]
        public void Snapshot_RoundTripsThroughCsv()
        {
            var first = LocalizationSnapshot.FromCsv(Csv);
            var second = LocalizationSnapshot.FromCsv(first.ToCsv());

            Assert.AreEqual(first.RowCount, second.RowCount);
            Assert.AreEqual("Quit, really?", second.GetValue("Popups/Quit/Title", "en"),
                "A quoted comma has to survive being written back out.");
        }

        [Test]
        public void Snapshot_RoundTripsThroughCatalog()
        {
            var catalog = LocalizationSnapshot.FromCsv(Csv).ToCatalog();

            try
            {
                var snapshot = LocalizationSnapshot.FromCatalog(catalog);

                Assert.AreEqual(2, snapshot.RowCount);
                Assert.AreEqual("Acheter", snapshot.GetValue("Store/Buy", "fr"));
            }
            finally
            {
                LocalizationSnapshot.DestroyTransient(catalog);
            }
        }

        [Test]
        public void Snapshot_BareKeyLandsInDefaultCategory()
        {
            var catalog = LocalizationSnapshot.FromCsv("Key,en\nGreeting,Hello\n").ToCatalog();

            try
            {
                Assert.IsNotNull(catalog.FindByFullKey("Default/Greeting"));
            }
            finally
            {
                LocalizationSnapshot.DestroyTransient(catalog);
            }
        }

        [Test]
        public void Snapshot_AddingLanguageWidensExistingRows()
        {
            var snapshot = new LocalizationSnapshot();
            snapshot.AddLanguage(new LanguageInfo("en", "English"));
            snapshot.SetValue("A/B", "en", "Hello");

            snapshot.AddLanguage(new LanguageInfo("fr", "French"));

            Assert.IsTrue(snapshot.SetValue("A/B", "fr", "Bonjour"),
                "A row created before a language existed still needs a slot for it.");

            Assert.AreEqual("Hello", snapshot.GetValue("A/B", "en"));
            Assert.AreEqual("Bonjour", snapshot.GetValue("A/B", "fr"));
        }

        [Test]
        public void Snapshot_ToTableResolvesKeys()
        {
            var table = LocalizationSnapshot.FromCsv(Csv).ToTable();

            Assert.AreEqual("Acheter", table.GetValue(table.IndexOf("Store/Buy"), table.IndexOfLanguage("fr")));
        }

        // ---------------------------------------------------------------- merge

        [Test]
        public void Merge_AddsKeysAndOverwritesText()
        {
            var catalog = Catalog("en", ("Store/Buy", "Buy"));

            try
            {
                var incoming = LocalizationSnapshot.FromCsv("Key,en\nStore/Buy,Purchase\nStore/Sell,Sell\n");
                var report = LocalizationMerge.Into(catalog, incoming, LocalizationMergeOptions.Default);

                Assert.AreEqual(1, report.AddedKeys);
                Assert.AreEqual(2, report.UpdatedValues);
                Assert.AreEqual("Purchase", catalog.FindByFullKey("Store/Buy").GetValue(0));
            }
            finally
            {
                LocalizationSnapshot.DestroyTransient(catalog);
            }
        }

        [Test]
        public void Merge_FillBlanksLeavesExistingTextAlone()
        {
            var catalog = Catalog("en", ("Store/Buy", "Buy"), ("Store/Sell", null));

            try
            {
                var incoming = LocalizationSnapshot.FromCsv("Key,en\nStore/Buy,Purchase\nStore/Sell,Sell\n");
                var report = LocalizationMerge.Into(catalog, incoming, LocalizationMergeOptions.FillBlanks);

                Assert.AreEqual("Buy", catalog.FindByFullKey("Store/Buy").GetValue(0),
                    "Overwrite is off, so an edit made since the last export must survive.");

                Assert.AreEqual("Sell", catalog.FindByFullKey("Store/Sell").GetValue(0));
                Assert.AreEqual(1, report.UpdatedValues);
            }
            finally
            {
                LocalizationSnapshot.DestroyTransient(catalog);
            }
        }

        [Test]
        public void Merge_IgnoresUnknownLanguagesUnlessAsked()
        {
            var catalog = Catalog("en", ("Store/Buy", "Buy"));

            try
            {
                var incoming = LocalizationSnapshot.FromCsv("Key,en,fr\nStore/Buy,Buy,Acheter\n");

                var ignored = LocalizationMerge.Into(catalog, incoming, LocalizationMergeOptions.Default);
                CollectionAssert.Contains(ignored.IgnoredLanguages, "fr");
                Assert.AreEqual(1, catalog.Languages.Count);

                var added = LocalizationMerge.Into(catalog, incoming, LocalizationMergeOptions.Mirror);
                CollectionAssert.Contains(added.AddedLanguages, "fr");
                Assert.AreEqual("Acheter", catalog.FindByFullKey("Store/Buy").GetValue(1));
            }
            finally
            {
                LocalizationSnapshot.DestroyTransient(catalog);
            }
        }

        [Test]
        public void Merge_SkipsUnknownKeysWhenAddingIsOff()
        {
            var catalog = Catalog("en", ("Store/Buy", "Buy"));

            try
            {
                var incoming = LocalizationSnapshot.FromCsv("Key,en\nStore/Sell,Sell\n");

                var report = LocalizationMerge.Into(catalog, incoming, new LocalizationMergeOptions
                {
                    AddNewKeys = false,
                    OverwriteExisting = true
                });

                Assert.AreEqual(1, report.SkippedKeys);
                Assert.IsNull(catalog.FindByFullKey("Store/Sell"));
            }
            finally
            {
                LocalizationSnapshot.DestroyTransient(catalog);
            }
        }

        [Test]
        public void Merge_RemovesAbsentKeysOnlyWhenAsked()
        {
            var catalog = Catalog("en", ("Store/Buy", "Buy"), ("Store/Gone", "Gone"));

            try
            {
                var incoming = LocalizationSnapshot.FromCsv("Key,en\nStore/Buy,Buy\n");

                var kept = LocalizationMerge.Into(catalog, incoming, LocalizationMergeOptions.Default);
                Assert.AreEqual(0, kept.RemovedKeys);
                Assert.IsNotNull(catalog.FindByFullKey("Store/Gone"));

                var mirrored = LocalizationMerge.Into(catalog, incoming, LocalizationMergeOptions.Mirror);
                Assert.AreEqual(1, mirrored.RemovedKeys);
                Assert.IsNull(catalog.FindByFullKey("Store/Gone"));
            }
            finally
            {
                LocalizationSnapshot.DestroyTransient(catalog);
            }
        }

        [Test]
        public void Merge_PreviewChangesNothing()
        {
            var catalog = Catalog("en", ("Store/Buy", "Buy"));

            try
            {
                var incoming = LocalizationSnapshot.FromCsv("Key,en\nStore/Buy,Purchase\nStore/Sell,Sell\n");
                var preview = LocalizationMerge.Preview(catalog, incoming, LocalizationMergeOptions.Default);

                Assert.AreEqual(1, preview.AddedKeys);
                Assert.AreEqual(2, preview.UpdatedValues);

                Assert.AreEqual("Buy", catalog.FindByFullKey("Store/Buy").GetValue(0),
                    "A preview that writes to the catalog is not a preview.");

                Assert.IsNull(catalog.FindByFullKey("Store/Sell"));
            }
            finally
            {
                LocalizationSnapshot.DestroyTransient(catalog);
            }
        }

        [Test]
        public void Merge_OfTwoSnapshotsLeavesInputsAlone()
        {
            var baseline = LocalizationSnapshot.FromCsv("Key,en\nStore/Buy,Buy\n");
            var incoming = LocalizationSnapshot.FromCsv("Key,en\nStore/Sell,Sell\n");

            var merged = LocalizationMerge.Merge(baseline, incoming, LocalizationMergeOptions.Default, out var report);

            Assert.AreEqual(2, merged.RowCount);
            Assert.AreEqual(1, report.AddedKeys);
            Assert.AreEqual(1, baseline.RowCount, "The baseline must come out of a merge unmodified.");
            Assert.AreEqual(1, incoming.RowCount);
        }

        // ---------------------------------------------------------------- qualifying

        [Test]
        public void Qualify_FilesABareKeyUnderItsCategory()
        {
            Assert.AreEqual("Boot/Connecting", LocalizationKeys.Qualify("Boot", "Connecting"));
            Assert.AreEqual("Popups/Settings/Title", LocalizationKeys.Qualify("Popups", "Settings/Title"));
        }

        [Test]
        public void Qualify_LeavesAnAlreadyFiledKeyAlone()
        {
            Assert.AreEqual("Popups/Title", LocalizationKeys.Qualify("Popups", "Popups/Title"),
                "Prefixing twice yields a key that exists nowhere and renders as its own name.");

            Assert.AreEqual("Popups/Settings/Title", LocalizationKeys.Qualify("Popups", "Popups/Settings/Title"));
        }

        [Test]
        public void Qualify_DoesNotMistakeAPrefixForACategory()
        {
            Assert.AreEqual("Store/StoreFront/Title", LocalizationKeys.Qualify("Store", "StoreFront/Title"),
                "StoreFront is not inside Store, so it still needs filing.");
        }

        [Test]
        public void Qualify_IsIdempotent()
        {
            var once = LocalizationKeys.Qualify("Popups", "Settings/Title");

            Assert.AreEqual(once, LocalizationKeys.Qualify("Popups", once),
                "A tab read twice, or a merge run twice, must not deepen the category.");
        }

        // ---------------------------------------------------------------- providers

        [Test]
        public void Provider_FetchAndApplyReplacesTheActiveTable()
        {
            Localization.SetTable(LocalizationSnapshot.FromCsv("Key,en\nStore/Buy,Old\n").ToTable(), "en");
            Assert.AreEqual("Old", Localization.Get("Store/Buy"));

            var provider = new FakeProvider(LocalizationSnapshot.FromCsv("Key,en\nStore/Buy,New\n"));

            LocalizationRemote.FetchAndApply(provider, cache: false);

            Assert.AreEqual("New", Localization.Get("Store/Buy"),
                "Everything bound reads through the table, so replacing it is the whole update.");
        }

        [Test]
        public void Provider_FetchAndApplyKeepsTheActiveLanguage()
        {
            Localization.SetTable(LocalizationSnapshot.FromCsv("Key,en,fr\nStore/Buy,Buy,Acheter\n").ToTable(), "fr");

            var provider = new FakeProvider(LocalizationSnapshot.FromCsv("Key,en,fr\nStore/Buy,Purchase,Acheter!\n"));
            LocalizationRemote.FetchAndApply(provider, cache: false);

            Assert.AreEqual("fr", Localization.LanguageCode);
            Assert.AreEqual("Acheter!", Localization.Get("Store/Buy"));
        }

        [Test]
        public void Provider_AFailedFetchLeavesTheTableAlone()
        {
            Localization.SetTable(LocalizationSnapshot.FromCsv("Key,en\nStore/Buy,Old\n").ToTable(), "en");

            var reported = (string)null;
            LocalizationRemote.FetchFailed += error => reported = error;

            LocalizationRemote.FetchAndApply(FakeProvider.Failing("offline"), cache: false);

            Assert.AreEqual("Old", Localization.Get("Store/Buy"));
            Assert.AreEqual("offline", reported);
        }

        [Test]
        public void Provider_AnEmptyAnswerIsTreatedAsAFailure()
        {
            Localization.SetTable(LocalizationSnapshot.FromCsv("Key,en\nStore/Buy,Old\n").ToTable(), "en");

            LocalizationRemote.FetchAndApply(new FakeProvider(new LocalizationSnapshot()), cache: false);

            Assert.AreEqual("Old", Localization.Get("Store/Buy"),
                "An empty document is nearly always a permissions page, and applying it blanks the game.");
        }

        [Test]
        public void Provider_UploadIsRefusedWhenItCannot()
        {
            var provider = new FakeProvider(new LocalizationSnapshot());
            var result = default(LocalizationUploadResult);

            LocalizationRemote.Upload(provider, LocalizationSnapshot.FromCsv(Csv), r => result = r);

            Assert.IsFalse(result.Success);
            Assert.IsNotNull(result.Error, "A refusal has to say why, or a caller has nothing to show.");
        }

        [Test]
        public void Provider_SourceAdapterProducesATable()
        {
            var source = new LocalizationProviderSource(new FakeProvider(LocalizationSnapshot.FromCsv(Csv)));
            LocalizationTable table = null;

            source.Load(t => table = t, error => Assert.Fail(error));

            Assert.IsNotNull(table);
            Assert.AreEqual(2, table.KeyCount);
        }

        // ---------------------------------------------------------------- cache

        [Test]
        public void Cache_RoundTripsAndClears()
        {
            LocalizationRemote.ClearCache();

            try
            {
                LocalizationRemote.WriteCache(LocalizationSnapshot.FromCsv(Csv));

                Assert.IsTrue(LocalizationRemote.TryLoadCache(out var cached));
                Assert.AreEqual("Acheter", cached.GetValue("Store/Buy", "fr"));

                LocalizationRemote.ClearCache();

                Assert.IsFalse(LocalizationRemote.TryLoadCache(out _));
                Assert.IsFalse(File.Exists(LocalizationRemote.CachePath));
            }
            finally
            {
                LocalizationRemote.ClearCache();
            }
        }

        // ---------------------------------------------------------------- helpers

        private static LocalizationCatalog Catalog(string language, params (string Key, string Text)[] entries)
        {
            var catalog = ScriptableObject.CreateInstance<LocalizationCatalog>();
            catalog.name = "Test Catalog";
            catalog.AddLanguage(new LanguageInfo(language, language));
            catalog.DefaultLanguageCode = language;

            foreach (var (key, text) in entries)
            {
                var category = LocalizationKeys.TrySplit(key, out var categoryName, out var name)
                    ? categoryName
                    : LocalizationKeys.DefaultCategory;

                catalog.AddEntry(category, name).SetValue(0, text);
            }

            return catalog;
        }

        /// <summary>A provider that answers from memory. Everything interesting is downstream of it.</summary>
        private sealed class FakeProvider : ILocalizationProvider
        {
            private readonly LocalizationSnapshot m_Snapshot;
            private readonly string m_Error;

            public string DisplayName => "Fake";

            public LocalizationProviderCapabilities Capabilities => LocalizationProviderCapabilities.Fetch;

            public FakeProvider(LocalizationSnapshot snapshot)
            {
                m_Snapshot = snapshot;
            }

            private FakeProvider(string error)
            {
                m_Error = error;
            }

            public static FakeProvider Failing(string error) => new FakeProvider(error);

            public void Fetch(Action<LocalizationFetchResult> onCompleted) =>
                onCompleted(m_Error != null
                    ? LocalizationFetchResult.Failed(m_Error)
                    : LocalizationFetchResult.Ok(m_Snapshot));

            public void Upload(LocalizationSnapshot snapshot, Action<LocalizationUploadResult> onCompleted) =>
                onCompleted(LocalizationUploadResult.Failed("Fake cannot upload."));
        }
    }
}
