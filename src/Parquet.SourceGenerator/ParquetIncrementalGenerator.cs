using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Parquet.SourceGenerator.Emitter;
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
        IncrementalValuesProvider<TargetParserResult> targets = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsTargetSyntax(s),
                transform: static (ctx, _) => TargetParser.GetTargetModel(ctx));

        // 2. Register source output emission & diagnostic reporting
        context.RegisterSourceOutput(targets, static (spc, result) =>
        {
            // Report compiler diagnostics (PARQ001, PARQ002, PARQ003)
            for (int i = 0; i < result.Diagnostics.Length; i++)
            {
                spc.ReportDiagnostic(result.Diagnostics[i].ToDiagnostic());
            }

            // Emit generated source code if target model is valid
            if (result.Model is not null)
            {
                string hintName = $"{result.Model.ClassName}.ParquetSerializer.g.cs";
                string sourceCode = CodeEmitter.EmitSource(result.Model);
                spc.AddSource(hintName, sourceCode);
            }
        });
    }

    private static bool IsTargetSyntax(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax { AttributeLists.Count: > 0 }
            || node is RecordDeclarationSyntax { AttributeLists.Count: > 0 }
            || node is StructDeclarationSyntax { AttributeLists.Count: > 0 };
    }
}
