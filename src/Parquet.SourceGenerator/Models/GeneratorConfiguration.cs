using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Parquet.SourceGenerator.Models;

internal enum GeneratorFeatureLevel
{
    Level1Flat = 1,
    Level2CompoundPreview = 2,
    Level3ModernCSharp = 3,
}

/// <summary>
/// Value-equatable project configuration carried through the incremental pipeline.
/// </summary>
internal sealed record GeneratorConfiguration(
    GeneratorFeatureLevel FeatureLevel,
    string GeneratorVersion
)
{
    private const string OptionsAttributeName =
        "Parquet.SourceGenerator.ParquetGeneratorOptionsAttribute";

    public static GeneratorConfiguration Default { get; } =
        new(GeneratorFeatureLevel.Level2CompoundPreview, GetGeneratorVersion());

    public static GeneratorConfiguration From(
        Compilation compilation,
        AnalyzerConfigOptionsProvider optionsProvider
    )
    {
        if (
            optionsProvider.GlobalOptions.TryGetValue(
                "build_property.ParquetGeneratorFeatureLevel",
                out string? featureLevel
            )
            && Enum.TryParse(featureLevel, ignoreCase: true, out GeneratorFeatureLevel parsed)
            && Enum.IsDefined(typeof(GeneratorFeatureLevel), parsed)
        )
        {
            return new GeneratorConfiguration(parsed, GetGeneratorVersion());
        }

        AttributeData? assemblyOptions = compilation
            .Assembly.GetAttributes()
            .FirstOrDefault(attribute =>
                attribute.AttributeClass?.ToDisplayString() == OptionsAttributeName
            );
        if (assemblyOptions is not null)
        {
            foreach (
                KeyValuePair<string, TypedConstant> namedArgument in assemblyOptions.NamedArguments
            )
            {
                if (
                    namedArgument.Key == "FeatureLevel"
                    && namedArgument.Value.Value is int value
                    && Enum.IsDefined(typeof(GeneratorFeatureLevel), value)
                )
                {
                    return new GeneratorConfiguration(
                        (GeneratorFeatureLevel)value,
                        GetGeneratorVersion()
                    );
                }
            }
        }

        return Default;
    }

    private static string GetGeneratorVersion() =>
        typeof(GeneratorConfiguration).Assembly.GetName().Version?.ToString(3)
        ?? typeof(GeneratorConfiguration)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? "unknown";
}
