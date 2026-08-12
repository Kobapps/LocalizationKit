using Microsoft.CodeAnalysis;

namespace LocalizationKit.SourceGenerator;

/// <summary>
/// Every way a <c>[Localized]</c> field can be wrong, stated at compile time.
/// </summary>
/// <remarks>
/// A generator that gives up quietly is worse than no generator: the field stays null, the label
/// stays blank, and nothing says why. So every bail-out here has a diagnostic attached.
/// </remarks>
internal static class Diagnostics
{
    private const string Category = "LocalizationKit";

    internal static readonly DiagnosticDescriptor LK001_ClassNotPartial = new(
        "LK001",
        "Class with [Localized] fields must be partial",
        "'{0}' has [Localized] fields but is not declared partial, so no binding can be generated for it",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The generator adds the binding as a second half of the class. Mark the class partial.");

    internal static readonly DiagnosticDescriptor LK002_FieldNotString = new(
        "LK002",
        "[Localized] fields must be of type string",
        "Field '{0}' is of type '{1}'; [Localized] only binds string fields",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A localized value is text. To drive a component, use a LocalizedText/LocalizedTMPText component or LocalizedStringEvent.");

    internal static readonly DiagnosticDescriptor LK003_LifecycleAlreadyDeclared = new(
        "LK003",
        "Localization binding is not wired up automatically",
        "'{0}' already declares {1}, so the generator did not add lifecycle wiring; call EnableLocalization() and DisableLocalization() from your own OnEnable/OnDisable",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The generator cannot merge with a method you wrote. It emits EnableLocalization/DisableLocalization for you to call at the right moment.");

    internal static readonly DiagnosticDescriptor LK004_EmptyKey = new(
        "LK004",
        "[Localized] key must not be empty",
        "Field '{0}' has a [Localized] attribute with no key",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The key is what the field is looked up by; an empty one can never resolve.");

    internal static readonly DiagnosticDescriptor LK005_FieldNotAssignable = new(
        "LK005",
        "[Localized] fields must be writable instance fields",
        "Field '{0}' is {1}; [Localized] cannot assign to it",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The generated code assigns the field on every language change, which a const, static or readonly field cannot accept.");

    internal static readonly DiagnosticDescriptor LK006_ContainingTypeNotPartial = new(
        "LK006",
        "Containing type must be partial",
        "'{0}' is nested in '{1}', which is not declared partial",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A nested partial can only be reopened through partial declarations of every type it is nested in.");
}
