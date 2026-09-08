using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Parquet.SourceGenerator.Emitter;
using Parquet.SourceGenerator.Emitter.Arrow;
using Parquet.SourceGenerator.Models;
using Parquet.SourceGenerator.Parser;

namespace Parquet.SourceGenerator;

/// <summary>
/// Roslyn 4.0 Incremental Source Generator for Parquet.Net.
/// Emits zero-reflection schema definitions, column serializers, and deserializers at compile time with compiler diagnostic checks.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ParquetIncrementalGenerator : IIncrementalGenerator
{
    /// <summary>
    /// Initializes the incremental generator pipeline.
    /// </summary>
    /// <param name="context">The incremental generator context.</param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 1. Filter syntax nodes decorated with attributes and extract target model + diagnostics
        IncrementalValuesProvider<TargetParserResult> targets =
            context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (s, _) => IsTargetSyntax(s),
                // Compound dial (#176): the v6 emitter expresses nested POCOs (M2) and
                // root-level lists/arrays of primitives (M3a). Maps (M4), lists of POCOs,
                // and lists nested in structs (M3b) still fall to PARQ006.
                transform: static (ctx, _) =>
                    TargetParser.GetTargetModel(
                        ctx,
                        ParquetApiLevel.V6,
                        compoundKinds: Parser.CompoundKinds.Struct | Parser.CompoundKinds.List
                    )
            );

        // 2. Register source output emission & diagnostic reporting
        context.RegisterSourceOutput(
            targets,
            static (spc, result) =>
            {
                // Report compiler diagnostics (PARQ001, PARQ002, PARQ003)
                for (int i = 0; i < result.Diagnostics.Length; i++)
                {
                    spc.ReportDiagnostic(result.Diagnostics[i].ToDiagnostic());
                }

                // Emit generated source code if target model is valid
                if (result.Model is not null)
                {
                    // Qualify the hint name with the namespace. AddSource requires hint names to be
                    // unique within a generator, and it throws rather than warning — so two
                    // [ParquetSerializable] types sharing a class name in different namespaces (an
                    // Order in Sales and one in Billing, say) took the whole generator down with
                    // CS8785, and every type it would have emitted disappeared with it.
                    string prefix = string.IsNullOrEmpty(result.Model.Namespace)
                        ? result.Model.ClassName
                        : $"{result.Model.Namespace}.{result.Model.ClassName}";
                    string hintName = $"{prefix}.ParquetSerializer.g.cs";
                    string sourceCode = CodeEmitter.EmitSource(result.Model);
                    spc.AddSource(hintName, sourceCode);
                }
            }
        );

        // 3. Apache Arrow bridge (#178), emitted only when the consumer references Apache.Arrow.
        //    The gate is projected down to a bool before it is combined with the targets, so a
        //    compilation change that does not flip the reference leaves the cached Arrow output
        //    alone — and the POCO output above never observes the compilation at all, so flipping
        //    the reference re-runs the gated file and nothing else.
        IncrementalValueProvider<bool> arrowReferenced = context.CompilationProvider.Select(
            static (compilation, _) =>
                compilation.GetTypeByMetadataName(ArrowMappingComponent.GateTypeMetadataName)
                    is not null
        );

        context.RegisterSourceOutput(
            targets.Combine(arrowReferenced),
            static (spc, pair) =>
            {
                (TargetParserResult result, bool arrowAvailable) = pair;
                if (!arrowAvailable || result.Model is null)
                {
                    return;
                }

                string? arrowSource = ArrowBridgeEmitter.EmitSource(result.Model);
                if (arrowSource is null)
                {
                    // Nested members and unmapped leaves simply get no bridge — #176 owns
                    // StructArray/ListArray/MapArray export.
                    return;
                }

                string prefix = string.IsNullOrEmpty(result.Model.Namespace)
                    ? result.Model.ClassName
                    : $"{result.Model.Namespace}.{result.Model.ClassName}";
                spc.AddSource($"{prefix}.Arrow.g.cs", arrowSource);
            }
        );
    }

    private static bool IsTargetSyntax(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax { AttributeLists.Count: > 0 }
            || node is RecordDeclarationSyntax { AttributeLists.Count: > 0 }
            || node is StructDeclarationSyntax { AttributeLists.Count: > 0 };
    }
}
