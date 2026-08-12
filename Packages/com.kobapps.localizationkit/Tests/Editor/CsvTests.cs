using NUnit.Framework;
using UnityEngine;

namespace LocalizationKit.Tests
{
    /// <summary>
    /// The CSV reader, which has to survive whatever a spreadsheet exports — quoted commas,
    /// doubled quotes, embedded newlines, a BOM, ragged rows.
    /// </summary>
    public sealed class CsvTests
    {
        [Test]
        public void Parse_ReadsHeaderAndRows()
        {
            var result = LocalizationCsv.Parse("Key,en,fr\nStore/Buy,Buy,Acheter\n");

            Assert.IsFalse(result.Failed);
            CollectionAssert.AreEqual(new[] { "en", "fr" }, result.LanguageCodes);
            Assert.AreEqual(1, result.Rows.Count);
            Assert.AreEqual("Store/Buy", result.Rows[0].Key);
            Assert.AreEqual("Acheter", result.Rows[0].Values[1]);
        }

        [Test]
        public void Parse_HandlesQuotedCommas()
        {
            var result = LocalizationCsv.Parse("Key,en\nA/B,\"Hello, world\"\n");

            Assert.AreEqual("Hello, world", result.Rows[0].Values[0]);
        }

        [Test]
        public void Parse_HandlesDoubledQuotes()
        {
            var result = LocalizationCsv.Parse("Key,en\nA/B,\"He said \"\"hi\"\"\"\n");

            Assert.AreEqual("He said \"hi\"", result.Rows[0].Values[0]);
        }

        [Test]
        public void Parse_HandlesEmbeddedNewlines()
        {
            var result = LocalizationCsv.Parse("Key,en\nA/B,\"line1\nline2\"\n");

            Assert.AreEqual(1, result.Rows.Count, "A newline inside quotes must not start a new row.");
            Assert.AreEqual("line1\nline2", result.Rows[0].Values[0]);
        }

        [Test]
        public void Parse_StripsByteOrderMark()
        {
            var result = LocalizationCsv.Parse("﻿Key,en\nA/B,Text\n");

            Assert.IsFalse(result.Failed);
            Assert.AreEqual(1, result.Rows.Count, "A BOM must not make the key column unrecognisable.");
            Assert.AreEqual("A/B", result.Rows[0].Key);
        }

        [Test]
        public void Parse_ShortRowIsPaddedAndReported()
        {
            var result = LocalizationCsv.Parse("Key,en,fr\nA/B,Only English\n");

            Assert.AreEqual("Only English", result.Rows[0].Values[0]);
            Assert.IsNull(result.Rows[0].Values[1]);
            Assert.IsNotEmpty(result.Warnings);
        }

        [Test]
        public void Parse_EmptyDocumentFailsCleanly()
        {
            var result = LocalizationCsv.Parse("   ");

            Assert.IsTrue(result.Failed);
            Assert.IsNotEmpty(result.Error);
        }

        [Test]
        public void Parse_HeaderWithoutLanguagesFails()
        {
            var result = LocalizationCsv.Parse("Key\nA/B\n");

            Assert.IsTrue(result.Failed);
        }

        [Test]
        public void Parse_TabDelimited()
        {
            var result = LocalizationCsv.Parse("Key\ten\nA/B\tText\n", '\t');

            Assert.IsFalse(result.Failed);
            Assert.AreEqual("Text", result.Rows[0].Values[0]);
        }

        [Test]
        public void WriteThenParse_IsLossless()
        {
            var catalog = ScriptableObject.CreateInstance<LocalizationCatalog>();

            try
            {
                catalog.AddLanguage(new LanguageInfo("en", "English"));
                catalog.AddLanguage(new LanguageInfo("fr", "Français"));

                var entry = catalog.AddEntry("Popups", "Quit");
                entry.SetValue(0, "Quit, really?");
                entry.SetValue(1, "Il a dit \"non\"\nsur deux lignes");

                var round = LocalizationCsv.Parse(LocalizationCsv.Write(catalog));

                Assert.IsFalse(round.Failed);
                Assert.AreEqual(1, round.Rows.Count);
                Assert.AreEqual("Popups/Quit", round.Rows[0].Key);
                Assert.AreEqual("Quit, really?", round.Rows[0].Values[0]);
                Assert.AreEqual("Il a dit \"non\"\nsur deux lignes", round.Rows[0].Values[1]);
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void TableBuilder_FromCsvProducesAWorkingTable()
        {
            var table = LocalizationTableBuilder.FromCsv(
                "Key,en,fr\nStore/Buy,Buy,Acheter\nStore/Sell,Sell,\n",
                defaultLanguage: "en");

            Assert.AreEqual(2, table.KeyCount);
            Assert.AreEqual(2, table.Languages.Count);

            Assert.IsTrue(table.SelectLanguage(table.IndexOfLanguage("fr")));
            Assert.AreEqual("Acheter", table.GetValue(table.IndexOf("Store/Buy")));
            Assert.AreEqual("Sell", table.GetValue(table.IndexOf("Store/Sell")), "A blank cell falls back to the default language.");
        }
    }
}
