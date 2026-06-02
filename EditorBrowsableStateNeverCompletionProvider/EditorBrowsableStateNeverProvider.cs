using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Tags;

namespace EditorBrowsableStateNeverCompletionProvider;

[ExportCompletionProvider(nameof(EditorBrowsableNeverProvider), LanguageNames.CSharp), Shared]
public sealed class EditorBrowsableNeverProvider : CompletionProvider
{
    public override async Task ProvideCompletionsAsync(CompletionContext context)
    {
        var doc = context.Document;
        var ct = context.CancellationToken;

        var root = await doc.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var token = root.FindToken(context.Position == 0 ? 0 : context.Position - 1);
        if (!token.IsKind(SyntaxKind.DotToken))
            return;

        SyntaxNode left = token.Parent switch
        {
            MemberAccessExpressionSyntax m => m.Expression,
            QualifiedNameSyntax q => q.Left,
            _ => null
        };
        if (left is null)
            return;

        var sm = await doc.GetSemanticModelAsync(ct).ConfigureAwait(false);
        var fromAsm = sm.Compilation.Assembly;
        var seen = new HashSet<(string, SymbolKind)>();

        foreach (var s in Resolve(sm, left, ct))
        {
            if (s.IsImplicitlyDeclared)
                continue;
            if (!IsHidden(s))
                continue;
            if (!IsAccessible(s, fromAsm))
                continue;
            if (!seen.Add((s.Name, s.Kind)))
                continue;

            context.AddItem(CompletionItem.Create(
                displayText: s.Name,
                filterText: s.Name,
                sortText: "~" + s.Name,
                tags: Tags(s),
                rules: CompletionItemRules.Default,
                inlineDescription: "(hidden)"));
        }
    }

    static IEnumerable<ISymbol> Resolve(SemanticModel sm, SyntaxNode left, CancellationToken ct)
    {
        var sym = sm.GetSymbolInfo(left, ct).Symbol;

        if (sym is INamespaceSymbol ns)
        {
            foreach (var t in ns.GetTypeMembers())
                yield return t;
            foreach (var n in ns.GetNamespaceMembers())
                yield return n;
            yield break;
        }

        ITypeSymbol type = sym as ITypeSymbol ?? sm.GetTypeInfo(left, ct).Type;
        if (type is null)
            yield break;

        for (var t = type; t is not null; t = t.BaseType)
            foreach (var m in t.GetMembers())
                yield return m;

        foreach (var i in type.AllInterfaces)
            foreach (var m in i.GetMembers())
                yield return m;
    }

    static bool IsHidden(ISymbol s)
    {
        foreach (var a in s.GetAttributes())
            if (a.AttributeClass?.Name == "EditorBrowsableAttribute"
                && a.ConstructorArguments.Length == 1
                && a.ConstructorArguments[0].Value is int v
                && v == (int)EditorBrowsableState.Never)
                return true;
        return false;
    }

    static bool IsAccessible(ISymbol s, IAssemblySymbol from) => s.DeclaredAccessibility switch
    {
        Accessibility.Public => true,
        Accessibility.ProtectedOrInternal => true,
        Accessibility.Internal => SymbolEqualityComparer.Default.Equals(s.ContainingAssembly, from)
                                  || from.GivesAccessTo(s.ContainingAssembly),
        _ => false
    };

    static ImmutableArray<string> Tags(ISymbol s) => s switch
    {
        IMethodSymbol => ImmutableArray.Create(WellKnownTags.Method, WellKnownTags.Public),
        IPropertySymbol => ImmutableArray.Create(WellKnownTags.Property, WellKnownTags.Public),
        IFieldSymbol => ImmutableArray.Create(WellKnownTags.Field, WellKnownTags.Public),
        IEventSymbol => ImmutableArray.Create(WellKnownTags.Event, WellKnownTags.Public),
        INamespaceSymbol => ImmutableArray.Create(WellKnownTags.Namespace),
        INamedTypeSymbol n => ImmutableArray.Create(n.TypeKind switch
        {
            TypeKind.Interface => WellKnownTags.Interface,
            TypeKind.Struct => WellKnownTags.Structure,
            TypeKind.Enum => WellKnownTags.Enum,
            TypeKind.Delegate => WellKnownTags.Delegate,
            _ => WellKnownTags.Class
        }, WellKnownTags.Public),
        _ => ImmutableArray<string>.Empty
    };
}