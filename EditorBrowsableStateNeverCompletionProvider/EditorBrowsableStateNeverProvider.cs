using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Tags;

namespace EditorBrowsableStateNeverCompletionProvider;

[ExportCompletionProvider(nameof(EditorBrowsableNeverProvider), LanguageNames.CSharp), Shared]
public sealed class EditorBrowsableNeverProvider : CompletionProvider
{
    private const string DocIdProp = "DeclarationId";
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

            // Stash the symbol's declaration id so GetDescriptionAsync can re-resolve it and
            // build the quick-info panel (signature + XML doc) shown for normal items.
            var props = ImmutableDictionary<string, string>.Empty;
            var declId = DocumentationCommentId.CreateDeclarationId(s);
            if (declId is not null)
                props = props.Add(DocIdProp, declId);

            context.AddItem(CompletionItem.Create(
                displayText: s.Name,
                filterText: s.Name,
                sortText: "~" + s.Name,
                properties: props,
                tags: Tags(s),
                rules: CompletionItemRules.Default,
                inlineDescription: "(hidden)"));
        }
    }

    private static IEnumerable<ISymbol> Resolve(SemanticModel sm, SyntaxNode left, CancellationToken ct)
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

        var type = sym as ITypeSymbol ?? sm.GetTypeInfo(left, ct).Type;
        if (type is null)
            yield break;

        for (var t = type; t is not null; t = t.BaseType)
            foreach (var m in t.GetMembers())
                yield return m;

        foreach (var i in type.AllInterfaces)
            foreach (var m in i.GetMembers())
                yield return m;
    }

    public override async Task<CompletionDescription> GetDescriptionAsync(Document document, CompletionItem item, CancellationToken cancellationToken)
    {
        if (!item.Properties.TryGetValue(DocIdProp, out var declId))
            return await base.GetDescriptionAsync(document, item, cancellationToken).ConfigureAwait(false);

        var comp = await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        if (comp is null)
            return CompletionDescription.Empty;

        var symbol = DocumentationCommentId.GetFirstSymbolForDeclarationId(declId, comp);
        if (symbol is null)
            return CompletionDescription.Empty;

        return CompletionDescription.Create(Describe(symbol, cancellationToken));
    }

    private static ImmutableArray<TaggedText> Describe(ISymbol symbol, CancellationToken ct)
    {
        var parts = ImmutableArray.CreateBuilder<TaggedText>();

        foreach (var p in symbol.ToDisplayParts(SymbolDisplayFormat.MinimallyQualifiedFormat))
            parts.Add(new TaggedText(TagFor(p.Kind), p.ToString()));

        var summary = Summary(symbol.GetDocumentationCommentXml(expandIncludes: true, cancellationToken: ct));
        if (!string.IsNullOrEmpty(summary))
        {
            parts.Add(new TaggedText(TextTags.LineBreak, "\r\n"));
            parts.Add(new TaggedText(TextTags.Text, summary));
        }

        return parts.ToImmutable();
    }

    private static string Summary(string docXml)
    {
        if (string.IsNullOrWhiteSpace(docXml))
            return null;

        XElement root;
        try
        {
            root = XElement.Parse(docXml);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        var summary = root.Name.LocalName == "summary" ? root : root.Descendants("summary").FirstOrDefault();
        if (summary is null)
            return null;

        var sb = new StringBuilder();
        AppendText(sb, summary);
        // Collapse the doc-comment indentation/newlines into a single line of text.
        return string.Join(" ", sb.ToString().Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
    }

    // Flattens doc-comment content to text. Inline elements such as <see cref="..."/> and
    // <paramref name="..."/> carry their text in an attribute rather than as a child text
    // node, so XElement.Value alone silently drops those words.
    private static void AppendText(StringBuilder sb, XElement element)
    {
        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XText text:
                    sb.Append(text.Value);
                    break;

                case XElement e:
                    switch (e.Name.LocalName)
                    {
                        case "see":
                        case "seealso":
                            if (!string.IsNullOrEmpty(e.Value))
                                AppendText(sb, e); // explicit inner text, e.g. <see cref="X">label</see>
                            else if (e.Attribute("cref")?.Value is { } cref)
                                sb.Append(SimpleName(cref));
                            else if (e.Attribute("langword")?.Value is { } langword)
                                sb.Append(langword);
                            else if (e.Attribute("href")?.Value is { } href)
                                sb.Append(href);
                            break;

                        case "paramref":
                        case "typeparamref":
                            sb.Append(e.Attribute("name")?.Value);
                            break;

                        default:
                            AppendText(sb, e); // <c>, <para>, <b>, <i>, etc.
                            break;
                    }
                    break;
            }
        }
    }

    // "T:System.Windows.Threading.Dispatcher" -> "Dispatcher"; strips the kind prefix,
    // any parameter list, and the namespace/containing-type qualification.
    private static string SimpleName(string cref)
    {
        var s = cref;
        var colon = s.IndexOf(':');
        if (colon >= 0)
            s = s.Substring(colon + 1);
        var paren = s.IndexOf('(');
        if (paren >= 0)
            s = s.Substring(0, paren);
        var dot = s.LastIndexOf('.');
        if (dot >= 0)
            s = s.Substring(dot + 1);
        return s;
    }

    private static string TagFor(SymbolDisplayPartKind kind) => kind switch
    {
        SymbolDisplayPartKind.ClassName => TextTags.Class,
        SymbolDisplayPartKind.StructName => TextTags.Struct,
        SymbolDisplayPartKind.InterfaceName => TextTags.Interface,
        SymbolDisplayPartKind.EnumName => TextTags.Enum,
        SymbolDisplayPartKind.DelegateName => TextTags.Delegate,
        SymbolDisplayPartKind.TypeParameterName => TextTags.TypeParameter,
        SymbolDisplayPartKind.Keyword => TextTags.Keyword,
        SymbolDisplayPartKind.MethodName => TextTags.Method,
        SymbolDisplayPartKind.ExtensionMethodName => TextTags.ExtensionMethod,
        SymbolDisplayPartKind.PropertyName => TextTags.Property,
        SymbolDisplayPartKind.FieldName => TextTags.Field,
        SymbolDisplayPartKind.EnumMemberName => TextTags.EnumMember,
        SymbolDisplayPartKind.ConstantName => TextTags.Constant,
        SymbolDisplayPartKind.EventName => TextTags.Event,
        SymbolDisplayPartKind.LocalName => TextTags.Local,
        SymbolDisplayPartKind.ParameterName => TextTags.Parameter,
        SymbolDisplayPartKind.NamespaceName => TextTags.Namespace,
        SymbolDisplayPartKind.Punctuation => TextTags.Punctuation,
        SymbolDisplayPartKind.Operator => TextTags.Operator,
        SymbolDisplayPartKind.Space or SymbolDisplayPartKind.LineBreak => TextTags.Space,
        SymbolDisplayPartKind.NumericLiteral => TextTags.NumericLiteral,
        SymbolDisplayPartKind.StringLiteral => TextTags.StringLiteral,
        _ => TextTags.Text
    };

    private static bool IsHidden(ISymbol s)
    {
        foreach (var a in s.GetAttributes())
            if (a.AttributeClass?.Name == "EditorBrowsableAttribute"
                && a.ConstructorArguments.Length == 1
                && a.ConstructorArguments[0].Value is int v
                && v == (int)EditorBrowsableState.Never)
                return true;
        return false;
    }

    private static bool IsAccessible(ISymbol s, IAssemblySymbol from) => s.DeclaredAccessibility switch
    {
        Accessibility.Public => true,
        // protected-internal is accessible if either half grants access. The protected
        // half is gated separately by the member-access context, so here we only check
        // the internal half — the same containing-assembly / IVT check used for Internal.
        Accessibility.ProtectedOrInternal or Accessibility.Internal
            => SymbolEqualityComparer.Default.Equals(s.ContainingAssembly, from)
               || from.GivesAccessTo(s.ContainingAssembly),
        _ => false
    };

    private static ImmutableArray<string> Tags(ISymbol s) => s switch
    {
        IMethodSymbol => [WellKnownTags.Method, WellKnownTags.Public],
        IPropertySymbol => [WellKnownTags.Property, WellKnownTags.Public],
        IFieldSymbol => [WellKnownTags.Field, WellKnownTags.Public],
        IEventSymbol => [WellKnownTags.Event, WellKnownTags.Public],
        INamespaceSymbol => [WellKnownTags.Namespace],
        INamedTypeSymbol n =>
        [
            n.TypeKind switch
                {
                    TypeKind.Interface => WellKnownTags.Interface,
                    TypeKind.Struct => WellKnownTags.Structure,
                    TypeKind.Enum => WellKnownTags.Enum,
                    TypeKind.Delegate => WellKnownTags.Delegate,
                    _ => WellKnownTags.Class
                },
            WellKnownTags.Public,
        ],
        _ => ImmutableArray<string>.Empty
    };
}