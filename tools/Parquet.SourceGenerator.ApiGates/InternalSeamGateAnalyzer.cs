using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Parquet.SourceGenerator.ApiGates;

/// <summary>
/// <c>PARQAPI002</c> — fails the build when a member in <c>src/</c> is widened past
/// <c>private</c> without a line in <c>src/api/seams.txt</c>.
/// </summary>
/// <remarks>
/// <para><b>What counts as a seam.</b> A <i>member</i> declaration — method, constructor,
/// property or indexer accessor, field, event — whose own declared accessibility is
/// <c>internal</c> or <c>protected internal</c>. That spelling is the deliberate act of widening a
/// member past <c>private</c> so a different component can call it, which is exactly what a seam
/// is and exactly what deserves a name and a rationale.</para>
///
/// <para><b>What does not count, and why.</b></para>
/// <list type="bullet">
///   <item><description><b>Type declarations.</b> An <c>internal</c> type is this repository's
///     ordinary unit of composition — every emitter component is one — and it is already
///     unreachable outside the assembly. Cataloguing all twenty-odd of them would make
///     <c>seams.txt</c> a mirror of the file listing, which is churn, not review. The three
///     entries the file starts with are three real decisions; a list of twenty-three would hide
///     them.</description></item>
///   <item><description><b>Compiler polyfills.</b> <c>IsExternalInit</c> and anything else in
///     <c>System.Runtime.CompilerServices</c>, plus anything carrying
///     <c>[CompilerGenerated]</c> or <c>[GeneratedCode]</c>. These exist only to let a language
///     feature compile on <c>netstandard2.0</c>; they are not seams anyone reuses, and this
///     repository has two copies of <c>IsExternalInit</c> (one per generator assembly) which would
///     otherwise appear as recurring, meaningless churn. They are excluded by rule rather than
///     listed once, so adding a third polyfill needs no catalogue edit.</description></item>
///   <item><description><b>Accessors that merely inherit their member's accessibility.</b> An
///     <c>internal int X { get; set; }</c> is one decision, so it produces one violation, not
///     three. An <c>internal set</c> on a <c>public</c> property is its own decision and is
///     reported.</description></item>
/// </list>
///
/// <para><b>Known limit, stated rather than hidden.</b> A member spelled <c>public</c> inside one
/// of the generator assemblies reaches no further than an <c>internal</c> one does — those
/// assemblies ship as analyzers and are never referenced by a consumer — and the gate does not
/// catch it. Closing that would mean cataloguing every <c>public</c> member of every
/// <c>internal</c> helper type, which is the same churn problem as above. The shipped
/// <c>Attributes</c> assembly, where <c>public</c> genuinely means public, is guarded by
/// <c>RS0016</c> instead.</para>
///
/// <para><b>Scope.</b> The rule short-circuits unless the compilation was handed a
/// <c>seams.txt</c> additional file, so it runs in the three <c>src/</c> projects and nowhere
/// else — never in a consumer's compilation.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InternalSeamGateAnalyzer : DiagnosticAnalyzer
{
    private const string CatalogueFileName = "seams.txt";
    private const string PolyfillNamespace = "System.Runtime.CompilerServices";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(ApiGateDiagnostics.UncataloguedInternalSeam);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        AdditionalText? catalogueFile = null;
        AdditionalText? ledgerFile = null;
        foreach (AdditionalText file in context.Options.AdditionalFiles)
        {
            string name = FileName(file.Path);
            if (name.Equals(CatalogueFileName, StringComparison.Ordinal))
            {
                catalogueFile = file;
            }
            else if (name.Equals("LEDGER.md", StringComparison.Ordinal))
            {
                ledgerFile = file;
            }
        }

        if (catalogueFile is null)
        {
            return;
        }

        ImmutableHashSet<string> catalogue = ApiCatalogue.Read(
            catalogueFile,
            context.CancellationToken
        );
        ApiLedger ledger = ApiLedger.Read(ledgerFile, context.CancellationToken);

        context.RegisterSymbolAction(
            symbolContext => Analyze(symbolContext, catalogue, ledger),
            SymbolKind.Method,
            SymbolKind.Property,
            SymbolKind.Field,
            SymbolKind.Event
        );
    }

    private static void Analyze(
        SymbolAnalysisContext context,
        ImmutableHashSet<string> catalogue,
        ApiLedger ledger
    )
    {
        ISymbol symbol = context.Symbol;

        if (symbol.IsImplicitlyDeclared || symbol.ContainingType is null)
        {
            return;
        }

        if (IsPolyfill(symbol.ContainingType))
        {
            return;
        }

        if (symbol is IMethodSymbol method)
        {
            // Property and event accessors are reported through the member they belong to, so the
            // signature in the message is the one a contributor would paste into the catalogue.
            if (method.AssociatedSymbol is not null)
            {
                return;
            }

            if (method.MethodKind == MethodKind.StaticConstructor)
            {
                return;
            }
        }

        bool widened =
            SeamSignature.IsWidened(symbol)
            || (
                symbol is IPropertySymbol property
                && (
                    SeamSignature.IsWidened(property.GetMethod)
                    || SeamSignature.IsWidened(property.SetMethod)
                )
            );

        if (!widened)
        {
            return;
        }

        foreach (string line in SeamSignature.Render(symbol))
        {
            if (catalogue.Contains(line) || ledger.IsExempt(line))
            {
                continue;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    ApiGateDiagnostics.UncataloguedInternalSeam,
                    symbol.Locations.Length > 0 ? symbol.Locations[0] : Location.None,
                    line
                )
            );
        }
    }

    private static bool IsPolyfill(INamedTypeSymbol type)
    {
        for (
            INamedTypeSymbol? current = type;
            current is not null;
            current = current.ContainingType
        )
        {
            if (current.Name.Equals("IsExternalInit", StringComparison.Ordinal))
            {
                return true;
            }

            if (
                current.ContainingNamespace is { IsGlobalNamespace: false } ns
                && ns.ToDisplayString().Equals(PolyfillNamespace, StringComparison.Ordinal)
            )
            {
                return true;
            }

            foreach (AttributeData attribute in current.GetAttributes())
            {
                string? name = attribute.AttributeClass?.Name;
                if (name is "CompilerGeneratedAttribute" or "GeneratedCodeAttribute")
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static readonly char[] PathSeparators = { '/', '\\' };

    private static string FileName(string path)
    {
        int slash = path.LastIndexOfAny(PathSeparators);
        return slash < 0 ? path : path.Substring(slash + 1);
    }
}
