using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LocalizationKit.Editor
{
    /// <summary>
    /// Generates a <c>LocKeys</c> class of constants from the catalog, so keys are written as code
    /// rather than as strings.
    /// </summary>
    /// <remarks>
    /// This is the single biggest thing that makes the kit pleasant to use. A magic string is
    /// unverifiable: a typo compiles, a renamed entry compiles, and both fail at runtime as a label
    /// showing a key. A constant gives autocomplete, go-to-usages, and — the real prize — a
    /// <b>compile error</b> when an entry is renamed or deleted out from under the code.
    /// <code>
    /// buyLabel.text = L.T(LocKeys.Store.BuyButton);
    /// </code>
    /// Categories become nested classes, so <c>Popups/Quit/Title</c> is
    /// <c>LocKeys.Popups.Quit.Title</c>.
    /// <para>
    /// The file is regenerated on demand rather than on every catalog change: it is source, it goes
    /// through version control, and a file that rewrites itself while you are editing something
    /// else is a file that produces surprising diffs.
    /// </para>
    /// </remarks>
    internal static class LocalizationKeyConstants
    {
        private const string DefaultPath = "Assets/Scripts/Generated/LocKeys.cs";
        private const string PathPrefKey = "LocalizationKit.KeyConstantsPath";

        /// <summary>Where the generated file was last written, so regenerating is one click.</summary>
        internal static string OutputPath
        {
            get => EditorPrefs.GetString(PathPrefKey, DefaultPath);
            set => EditorPrefs.SetString(PathPrefKey, value);
        }

        [MenuItem("Tools/LocalizationKit/Generate Key Constants", priority = 110)]
        internal static void GenerateInteractive()
        {
            var catalog = LocalizationEditorCatalog.Catalog;
            if (catalog == null)
            {
                EditorUtility.DisplayDialog(
                    "No catalog",
                    "Create a localization catalog before generating key constants.",
                    "OK");
                return;
            }

            var existing = OutputPath;

            var path = EditorUtility.SaveFilePanelInProject(
                "Generate Key Constants",
                Path.GetFileNameWithoutExtension(existing),
                "cs",
                "Constants for every key in the catalog. Regenerate after adding or renaming keys.",
                Path.GetDirectoryName(existing));

            if (string.IsNullOrEmpty(path)) return;

            Generate(catalog, path);

            EditorUtility.DisplayDialog(
                "Key constants generated",
                $"{catalog.EntryCount} keys written to {path}.\n\n"
                + "Regenerate after adding or renaming keys — a stale file is the one way this can "
                + "still point at a key that no longer exists.",
                "OK");
        }

        /// <summary>Writes the constants file and imports it. Safe to call from a script.</summary>
        internal static void Generate(LocalizationCatalog catalog, string path)
        {
            if (catalog == null || string.IsNullOrEmpty(path)) return;

            var source = Build(catalog, Path.GetFileNameWithoutExtension(path));

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            File.WriteAllText(path, source, new UTF8Encoding(false));
            OutputPath = path;

            AssetDatabase.ImportAsset(path);
        }

        // ---------------------------------------------------------------- emit

        /// <summary>One node of the category tree while it is being assembled.</summary>
        private sealed class Node
        {
            internal readonly SortedDictionary<string, Node> Children =
                new SortedDictionary<string, Node>(StringComparer.Ordinal);

            /// <summary>Constant name → full key.</summary>
            internal readonly SortedDictionary<string, string> Keys =
                new SortedDictionary<string, string>(StringComparer.Ordinal);
        }

        private static string Build(LocalizationCatalog catalog, string className)
        {
            var root = new Node();
            var skipped = new List<string>();

            for (var c = 0; c < catalog.Categories.Count; c++)
            {
                var category = catalog.Categories[c];

                for (var e = 0; e < category.Entries.Count; e++)
                {
                    var entry = category.Entries[e];
                    if (entry == null || string.IsNullOrEmpty(entry.Key)) continue;

                    var fullKey = LocalizationKeys.Compose(category.Name, entry.Key);
                    var node = root;

                    // Walk down the category path, creating nested classes as needed.
                    if (!string.IsNullOrEmpty(category.Name))
                    {
                        foreach (var part in category.Name.Split(LocalizationKeys.Separator))
                        {
                            var identifier = Identifier(part);
                            if (identifier == null) { node = null; break; }

                            if (!node.Children.TryGetValue(identifier, out var child))
                            {
                                child = new Node();
                                node.Children[identifier] = child;
                            }

                            node = child;
                        }
                    }

                    var name = node == null ? null : Identifier(entry.Key);
                    if (name == null)
                    {
                        // A key that cannot become an identifier is not an error in the catalog —
                        // it still resolves at runtime. It just cannot get a constant, and saying
                        // so beats emitting a file that does not compile.
                        skipped.Add(fullKey);
                        continue;
                    }

                    // A member cannot share its name with its enclosing class.
                    if (node.Children.ContainsKey(name)) name += "Key";

                    node.Keys[name] = fullKey;
                }
            }

            var builder = new StringBuilder(4096);

            builder.AppendLine("// <auto-generated/>");
            builder.AppendLine("// Generated by LocalizationKit from the localization catalog.");
            builder.AppendLine("// Regenerate with Tools > LocalizationKit > Generate Key Constants.");
            builder.AppendLine("// Edits are lost on the next generation.");
            builder.AppendLine();

            if (skipped.Count > 0)
            {
                builder.AppendLine("// These keys have no constant because they do not form C# identifiers:");
                foreach (var key in skipped) builder.AppendLine($"//   {key}");
                builder.AppendLine();
            }

            builder.AppendLine("/// <summary>Every key in the localization catalog, as constants.</summary>");
            builder.AppendLine($"public static class {Identifier(className) ?? "LocKeys"}");
            builder.AppendLine("{");

            Emit(builder, root, "    ");

            builder.AppendLine("}");

            return builder.ToString();
        }

        private static void Emit(StringBuilder builder, Node node, string indent)
        {
            foreach (var pair in node.Keys)
                builder.AppendLine($"{indent}public const string {pair.Key} = \"{Escape(pair.Value)}\";");

            if (node.Keys.Count > 0 && node.Children.Count > 0)
                builder.AppendLine();

            var first = true;
            foreach (var pair in node.Children)
            {
                if (!first) builder.AppendLine();
                first = false;

                builder.AppendLine($"{indent}public static class {pair.Key}");
                builder.AppendLine($"{indent}{{");
                Emit(builder, pair.Value, indent + "    ");
                builder.AppendLine($"{indent}}}");
            }
        }

        /// <summary>
        /// Turns a category or key name into a C# identifier, or null when it cannot be one.
        /// </summary>
        private static string Identifier(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            var builder = new StringBuilder(name.Length + 1);

            foreach (var ch in name)
            {
                if (char.IsLetterOrDigit(ch) || ch == '_') builder.Append(ch);
                else if (builder.Length > 0 && builder[builder.Length - 1] != '_') builder.Append('_');
            }

            while (builder.Length > 0 && builder[builder.Length - 1] == '_')
                builder.Length--;

            if (builder.Length == 0) return null;

            // An identifier cannot start with a digit, and a keyword needs the @ escape.
            if (char.IsDigit(builder[0])) builder.Insert(0, '_');

            var identifier = builder.ToString();
            return IsKeyword(identifier) ? "@" + identifier : identifier;
        }

        private static readonly HashSet<string> s_Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
            "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
            "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
            "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw",
            "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
            "virtual", "void", "volatile", "while",
        };

        private static bool IsKeyword(string identifier) => s_Keywords.Contains(identifier);

        private static string Escape(string value) =>
            value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
