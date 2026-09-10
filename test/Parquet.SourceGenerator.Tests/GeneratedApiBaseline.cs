using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Parquet.SourceGenerator.Tests;

/// <summary>
/// Extracts a signature-only view of generated source (issue #215).
/// <para>
/// Golden <c>.g.cs</c> files contain full method bodies, so a public API addition and a codegen
/// tweak produce diffs of the same shape and a reviewer cannot tell them apart. The generated
/// surface is also invisible to every .NET API-diff tool, because it is emitted into the
/// consumer's compilation and exists in no assembly this repository ships.
/// </para>
/// <para>
/// This produces the missing artefact: public members only, no bodies, deterministically ordered,
/// so that <c>*.api.txt</c> changing in a pull request means the API changed.
/// </para>
/// </summary>
public static class GeneratedApiBaseline
{
    /// <summary>
    /// Renders the public surface of <paramref name="emittedSource"/> as stable baseline text.
    /// </summary>
    public static string Extract(string emittedSource, string header)
    {
        CompilationUnitSyntax root = CSharpSyntaxTree
            .ParseText(emittedSource)
            .GetCompilationUnitRoot();

        var builder = new StringBuilder();
        builder.Append("// Generated API baseline — ").Append(header).Append('\n');
        builder.Append(
            "// Signature-only view of the generated public surface. Bodies are excluded by design;\n"
                + "// a change to this file is a change to the API. Regenerate with UPDATE_GOLDEN_FILES=true.\n"
        );

        foreach (
            BaseNamespaceDeclarationSyntax ns in root.DescendantNodes()
                .OfType<BaseNamespaceDeclarationSyntax>()
        )
        {
            builder.Append('\n').Append("namespace ").Append(ns.Name.ToString()).Append('\n');
        }

        foreach (
            TypeDeclarationSyntax type in root.DescendantNodes()
                .OfType<TypeDeclarationSyntax>()
                .Where(IsPublic)
                .OrderBy(t => t.Identifier.Text, System.StringComparer.Ordinal)
        )
        {
            builder.Append('\n').Append(DescribeType(type)).Append('\n');

            foreach (
                string member in type
                    .Members.Where(IsPublic)
                    .Select(Describe)
                    .Where(s => s.Length > 0)
                    .OrderBy(s => s, System.StringComparer.Ordinal)
            )
            {
                builder.Append("    ").Append(member).Append('\n');
            }
        }

        return builder.ToString().TrimEnd() + "\n";
    }

    private static bool IsPublic(MemberDeclarationSyntax member) =>
        member.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword));

    private static string DescribeType(TypeDeclarationSyntax type)
    {
        string keyword = type switch
        {
            ClassDeclarationSyntax => "class",
            StructDeclarationSyntax => "struct",
            InterfaceDeclarationSyntax => "interface",
            RecordDeclarationSyntax => "record",
            _ => "type",
        };
        return $"{Modifiers(type.Modifiers)} {keyword} {type.Identifier.Text}{type.TypeParameterList}";
    }

    private static string Describe(MemberDeclarationSyntax member) =>
        member switch
        {
            MethodDeclarationSyntax method =>
                $"{Modifiers(method.Modifiers)} {Type(method.ReturnType)} {method.Identifier.Text}"
                    + $"{method.TypeParameterList}({Parameters(method.ParameterList)})",
            PropertyDeclarationSyntax property =>
                $"{Modifiers(property.Modifiers)} {Type(property.Type)} {property.Identifier.Text} "
                    + $"{{ {Accessors(property)} }}",
            FieldDeclarationSyntax field =>
                $"{Modifiers(field.Modifiers)} {Type(field.Declaration.Type)} "
                    + string.Join(", ", field.Declaration.Variables.Select(v => v.Identifier.Text)),
            _ => string.Empty,
        };

    /// <summary>
    /// Renders modifiers in a fixed order, dropping those that are implementation detail rather
    /// than API. <c>async</c> is the important one: whether a method is implemented with a state
    /// machine is invisible to callers, and letting it into the baseline would make an
    /// implementation change look like an API change.
    /// </summary>
    private static string Modifiers(SyntaxTokenList modifiers)
    {
        string[] ordered =
        {
            "public",
            "static",
            "partial",
            "readonly",
            "ref",
            "sealed",
            "abstract",
            "override",
            "virtual",
        };
        var present = modifiers.Select(m => m.Text).ToHashSet();
        return string.Join(" ", ordered.Where(present.Contains));
    }

    private static string Accessors(PropertyDeclarationSyntax property) =>
        property.AccessorList is null
            ? "get;"
            : string.Join(
                " ",
                property.AccessorList.Accessors.Select(a =>
                    $"{Modifiers(a.Modifiers)}{(a.Modifiers.Count > 0 ? " " : string.Empty)}{a.Keyword.Text};"
                )
            );

    private static string Parameters(ParameterListSyntax parameters) =>
        string.Join(", ", parameters.Parameters.Select(Describe));

    private static string Describe(ParameterSyntax parameter)
    {
        // `this` is API — it decides whether the member is reachable from `items.` in IntelliSense,
        // which is exactly the read/write discovery asymmetry issue #216 catalogues. Attributes
        // (EnumeratorCancellation and friends) are not.
        string prefix = parameter.Modifiers.Any(m => m.IsKind(SyntaxKind.ThisKeyword))
            ? "this "
            : string.Empty;
        string @default = parameter.Default is null
            ? string.Empty
            : $" = {parameter.Default.Value}";
        return $"{prefix}{Type(parameter.Type)} {parameter.Identifier.Text}{@default}";
    }

    /// <summary>
    /// Strips the <c>global::</c> prefixes the emitter uses for hygiene. They are noise in a
    /// baseline read by humans, and the fully-qualified name survives without them.
    /// </summary>
    private static string Type(TypeSyntax? type) =>
        type is null ? "void" : type.ToString().Replace("global::", string.Empty);
}
