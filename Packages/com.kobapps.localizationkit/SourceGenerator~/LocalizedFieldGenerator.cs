using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LocalizationKit.SourceGenerator;

/// <summary>
/// Generates the binding half of any class holding <c>[Localized("key")]</c> fields.
/// </summary>
/// <remarks>
/// For each such class it emits a partial that implements <c>ILocalizedObject</c>, resolves one
/// handle per field, and registers with the binder. For a <see cref="UnityEngine.MonoBehaviour"/>
/// it also emits <c>OnEnable</c>/<c>OnDisable</c> — but only when the class declares neither.
/// <para>
/// That condition is the one design decision worth defending. A partial class cannot contribute a
/// second body for a method the author already wrote, so when they have their own <c>OnEnable</c>
/// the generator has three options: silently skip binding, break the build, or emit the plumbing
/// and say so. It emits <c>EnableLocalization</c>/<c>DisableLocalization</c> and raises LK003, so
/// the fix is one line and the failure is never silent.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class LocalizedFieldGenerator : IIncrementalGenerator
{
    private const string LocalizedAttribute = "LocalizationKit.LocalizedAttribute";
    private const string MonoBehaviourType = "UnityEngine.MonoBehaviour";

    private const string HandlePrefix = "__LocalizationKit_handle_";
    private const string SubscriptionField = "__LocalizationKit_subscription";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var fields = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                LocalizedAttribute,
                predicate: static (node, _) => node is VariableDeclaratorSyntax or PropertyDeclarationSyntax,
                transform: static (ctx, _) => Transform(ctx))
            .Where(static f => f is not null)
            .Collect();

        context.RegisterSourceOutput(fields, static (spc, all) => Emit(spc, all!));
    }

    // ------------------------------------------------------------------ model

    private sealed record FieldModel(
        string FieldName,
        string Key,
        ImmutableArray<DiagnosticInfo> Diagnostics);

    private sealed record TypeModel(
        INamedTypeSymbol Symbol,
        List<FieldModel> Fields);

    private sealed record CapturedField(INamedTypeSymbol Owner, FieldModel Field);

    private readonly record struct DiagnosticInfo(DiagnosticDescriptor Descriptor, Location? Location, string?[] Args);

    private static CapturedField? Transform(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not IFieldSymbol field) return null;

        var owner = field.ContainingType;
        if (owner is null) return null;

        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var location = field.Locations.FirstOrDefault();

        // LK005 — assignability. Checked before the type so the more fundamental problem wins.
        if (field.IsStatic || field.IsConst || field.IsReadOnly)
        {
            var what = field.IsConst ? "const" : field.IsStatic ? "static" : "readonly";
            diagnostics.Add(new DiagnosticInfo(Diagnostics.LK005_FieldNotAssignable, location, new[] { field.Name, what }));
        }

        // LK002 — must be a string.
        if (field.Type.SpecialType != SpecialType.System_String)
        {
            diagnostics.Add(new DiagnosticInfo(
                Diagnostics.LK002_FieldNotString,
                location,
                new[] { field.Name, field.Type.ToDisplayString() }));
        }

        var key = ReadKey(ctx.Attributes);

        // LK004 — a key that can never resolve.
        if (string.IsNullOrWhiteSpace(key))
            diagnostics.Add(new DiagnosticInfo(Diagnostics.LK004_EmptyKey, location, new[] { field.Name }));

        return new CapturedField(owner, new FieldModel(field.Name, key ?? string.Empty, diagnostics.ToImmutable()));
    }

    private static string? ReadKey(ImmutableArray<AttributeData> attributes)
    {
        foreach (var attribute in attributes)
        {
            if (attribute.ConstructorArguments.Length > 0)
                return attribute.ConstructorArguments[0].Value as string;
        }

        return null;
    }

    // ------------------------------------------------------------------ emit

    private static void Emit(SourceProductionContext spc, ImmutableArray<CapturedField?> captured)
    {
        var byType = new Dictionary<INamedTypeSymbol, TypeModel>(SymbolEqualityComparer.Default);

        foreach (var item in captured)
        {
            if (item is null) continue;

            if (!byType.TryGetValue(item.Owner, out var model))
            {
                model = new TypeModel(item.Owner, new List<FieldModel>());
                byType[item.Owner] = model;
            }

            model.Fields.Add(item.Field);
        }

        foreach (var model in byType.Values)
            EmitType(spc, model);
    }

    private static void EmitType(SourceProductionContext spc, TypeModel model)
    {
        var symbol = model.Symbol;
        var fatal = false;

        foreach (var field in model.Fields)
        {
            foreach (var diagnostic in field.Diagnostics)
            {
                spc.ReportDiagnostic(Diagnostic.Create(diagnostic.Descriptor, diagnostic.Location, diagnostic.Args));
                if (diagnostic.Descriptor.DefaultSeverity == DiagnosticSeverity.Error) fatal = true;
            }
        }

        // LK001 — the class itself must be partial.
        if (!IsPartial(symbol))
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.LK001_ClassNotPartial,
                symbol.Locations.FirstOrDefault(),
                symbol.Name));
            return;
        }

        // LK006 — and so must every type it is nested in.
        for (var containing = symbol.ContainingType; containing is not null; containing = containing.ContainingType)
        {
            if (IsPartial(containing)) continue;

            spc.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.LK006_ContainingTypeNotPartial,
                symbol.Locations.FirstOrDefault(),
                symbol.Name,
                containing.Name));
            return;
        }

        // Emitting against fields that failed validation would bury the real diagnostic under a
        // cascade of compiler errors pointing at generated code the user cannot edit.
        if (fatal) return;

        var isMonoBehaviour = InheritsMonoBehaviour(symbol);
        var declaredLifecycle = DeclaredLifecycle(symbol);

        if (isMonoBehaviour && declaredLifecycle is not null)
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.LK003_LifecycleAlreadyDeclared,
                symbol.Locations.FirstOrDefault(),
                symbol.Name,
                declaredLifecycle));
        }

        var emitLifecycle = isMonoBehaviour && declaredLifecycle is null;
        var source = BuildSource(symbol, model.Fields, emitLifecycle);

        spc.AddSource($"{FileNameFor(symbol)}.Localization.g.cs", source);
    }

    private static string BuildSource(INamedTypeSymbol symbol, List<FieldModel> fields, bool emitLifecycle)
    {
        var builder = new StringBuilder(2048);

        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("// Generated by LocalizationKit. Do not edit.");
        builder.AppendLine("#nullable disable");
        builder.AppendLine();

        var indent = string.Empty;
        var hasNamespace = !symbol.ContainingNamespace.IsGlobalNamespace;

        if (hasNamespace)
        {
            builder.AppendLine($"namespace {symbol.ContainingNamespace.ToDisplayString()}");
            builder.AppendLine("{");
            indent = "    ";
        }

        // Reopen each containing type, outermost first, so a nested class can be extended.
        var nesting = new List<INamedTypeSymbol>();
        for (var containing = symbol.ContainingType; containing is not null; containing = containing.ContainingType)
            nesting.Insert(0, containing);

        foreach (var containing in nesting)
        {
            builder.AppendLine($"{indent}partial {KindKeyword(containing)} {NameWithArity(containing)}");
            builder.AppendLine($"{indent}{{");
            indent += "    ";
        }

        builder.AppendLine($"{indent}partial {KindKeyword(symbol)} {NameWithArity(symbol)} : global::LocalizationKit.ILocalizedObject");
        builder.AppendLine($"{indent}{{");

        var inner = indent + "    ";

        for (var i = 0; i < fields.Count; i++)
            builder.AppendLine($"{inner}private global::LocalizationKit.LocalizationHandle {HandlePrefix}{i};");

        builder.AppendLine($"{inner}private global::LocalizationKit.LocalizationSubscription {SubscriptionField};");
        builder.AppendLine();

        // ApplyLocalization
        builder.AppendLine($"{inner}/// <summary>Pulls current text for every [Localized] field. Called by the binder.</summary>");
        builder.AppendLine($"{inner}public void ApplyLocalization()");
        builder.AppendLine($"{inner}{{");

        for (var i = 0; i < fields.Count; i++)
        {
            builder.AppendLine(
                $"{inner}    this.{fields[i].FieldName} = global::LocalizationKit.Localization.GetValue(ref this.{HandlePrefix}{i});");
        }

        builder.AppendLine($"{inner}    OnLocalizationApplied();");
        builder.AppendLine($"{inner}}}");
        builder.AppendLine();

        // EnableLocalization
        builder.AppendLine($"{inner}/// <summary>Resolves every key and starts tracking language changes.</summary>");
        builder.AppendLine($"{inner}public void EnableLocalization()");
        builder.AppendLine($"{inner}{{");

        for (var i = 0; i < fields.Count; i++)
        {
            builder.AppendLine(
                $"{inner}    this.{HandlePrefix}{i} = global::LocalizationKit.Localization.Resolve({Literal(fields[i].Key)});");
        }

        // Register applies immediately, so the fields are correct before this returns.
        builder.AppendLine($"{inner}    this.{SubscriptionField} = global::LocalizationKit.LocalizationBinder.Register(this);");
        builder.AppendLine($"{inner}}}");
        builder.AppendLine();

        // DisableLocalization
        builder.AppendLine($"{inner}/// <summary>Stops tracking language changes. Safe to call twice.</summary>");
        builder.AppendLine($"{inner}public void DisableLocalization()");
        builder.AppendLine($"{inner}{{");
        builder.AppendLine($"{inner}    global::LocalizationKit.LocalizationBinder.Unregister(ref this.{SubscriptionField});");
        builder.AppendLine($"{inner}}}");
        builder.AppendLine();

        builder.AppendLine($"{inner}/// <summary>Runs after every refresh. Implement to react to a language change.</summary>");
        builder.AppendLine($"{inner}partial void OnLocalizationApplied();");

        if (emitLifecycle)
        {
            builder.AppendLine();
            builder.AppendLine($"{inner}private void OnEnable() => EnableLocalization();");
            builder.AppendLine();
            builder.AppendLine($"{inner}private void OnDisable() => DisableLocalization();");
        }

        builder.AppendLine($"{indent}}}");

        for (var i = nesting.Count - 1; i >= 0; i--)
        {
            indent = indent.Substring(0, indent.Length - 4);
            builder.AppendLine($"{indent}}}");
        }

        if (hasNamespace)
            builder.AppendLine("}");

        return builder.ToString();
    }

    // ------------------------------------------------------------------ helpers

    private static bool IsPartial(INamedTypeSymbol symbol) =>
        symbol.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .OfType<TypeDeclarationSyntax>()
            .Any(declaration => declaration.Modifiers.Any(modifier => modifier.Text == "partial"));

    private static bool InheritsMonoBehaviour(INamedTypeSymbol symbol)
    {
        for (var type = symbol.BaseType; type is not null; type = type.BaseType)
        {
            if (type.ToDisplayString() == MonoBehaviourType) return true;
        }

        return false;
    }

    /// <summary>
    /// Names the lifecycle method the author already wrote, or null when both are free.
    /// Only the class's own declarations count — an inherited OnEnable does not block a new one.
    /// </summary>
    private static string? DeclaredLifecycle(INamedTypeSymbol symbol)
    {
        var hasEnable = symbol.GetMembers("OnEnable").OfType<IMethodSymbol>().Any();
        var hasDisable = symbol.GetMembers("OnDisable").OfType<IMethodSymbol>().Any();

        if (hasEnable && hasDisable) return "OnEnable and OnDisable";
        if (hasEnable) return "OnEnable";
        if (hasDisable) return "OnDisable";

        return null;
    }

    private static string KindKeyword(INamedTypeSymbol symbol) => symbol.TypeKind switch
    {
        TypeKind.Struct => symbol.IsRecord ? "record struct" : "struct",
        _ => symbol.IsRecord ? "record" : "class",
    };

    private static string NameWithArity(INamedTypeSymbol symbol)
    {
        if (symbol.TypeParameters.Length == 0) return symbol.Name;

        return $"{symbol.Name}<{string.Join(", ", symbol.TypeParameters.Select(p => p.Name))}>";
    }

    private static string FileNameFor(INamedTypeSymbol symbol) =>
        symbol.ToDisplayString()
            .Replace('<', '_')
            .Replace('>', '_')
            .Replace(',', '_')
            .Replace(' ', '_');

    private static string Literal(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');

        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default: builder.Append(ch); break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }
}
