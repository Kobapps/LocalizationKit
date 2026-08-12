using System.Runtime.CompilerServices;

// The tests reach a handful of internals the public API deliberately does not expose — chiefly
// LocalizationEntry.Values' setter, which exists so the catalog can repair a ragged array and
// must not become a way for game code to resize one behind the catalog's back.
[assembly: InternalsVisibleTo("LocalizationKit.Tests.Editor")]
