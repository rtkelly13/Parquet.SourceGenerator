using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Parquet.SourceGenerator.Emitter;
using Parquet.SourceGenerator.Models;
using Parquet.SourceGenerator.Parser;

namespace Parquet.SourceGenerator;

/// <summary>
/// Roslyn 4.0 Incremental Source Generator for Parquet.Net.
/// Emits zero-reflection schema definitions, column serializers, and deserializers at compile time.
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
        // 1. Filter syntax nodes decorated with attributes
        IncrementalValuesProvider<TargetClassModel?> targets = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsTargetSyntax(s),
                transform: static (ctx, _) => TargetParser.GetTargetModel(ctx))
            .Where(static m => m is not null);

        // 2. Register source output emission
        context.RegisterSourceOutput(targets, static (spc, target) =>
        {
            if (target is null) return;

            string hintName = $"{target.ClassName}.ParquetSerializer.g.cs";
            string sourceCode = CodeEmitter.EmitSource(target);
            spc.AddSource(hintName, sourceCode);
        });
    }

    private static bool IsTargetSyntax(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax { AttributeLists.Count: > 0 }
            || node is RecordDeclarationSyntax { AttributeLists.Count: > 0 }
            || node is StructDeclarationSyntax { AttributeLists.Count: > 0 };
    }
}
