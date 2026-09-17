using System;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Parquet.SourceGenerator.Models;

/// <summary>
/// Value-equatable project configuration carried through the incremental pipeline.
/// </summary>
internal sealed record GeneratorConfiguration(string? FeatureLevel)
{
    public static GeneratorConfiguration Default { get; } = new((string?)null);

    public static GeneratorConfiguration From(AnalyzerConfigOptionsProvider optionsProvider)
    {
        if (
            optionsProvider.GlobalOptions.TryGetValue(
                "build_property.ParquetGeneratorFeatureLevel",
                out string? featureLevel
            ) && !string.IsNullOrWhiteSpace(featureLevel)
        )
        {
            return new GeneratorConfiguration(featureLevel.Trim());
        }

        return Default;
    }
}
