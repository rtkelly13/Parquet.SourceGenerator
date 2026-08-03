using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Parquet.SourceGenerator.Diagnostics;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

public sealed class DiagnosticTests
{
    [Fact]
    public void NonPartialClassTriggersPARQ001()
    {
        string source = """
            using Parquet.SourceGenerator;

            [ParquetSerializable]
            public class NonPartialClass
            {
                [ParquetColumn("id")]
                public int Id { get; init; }
            }
            """;

        var (diagnostics, _) = RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == DiagnosticDescriptors.MustBePartial.Id);
    }

    [Fact]
    public void DuplicateColumnNameTriggersPARQ002()
    {
        string source = """
            using Parquet.SourceGenerator;

            [ParquetSerializable]
            public partial class DuplicateColClass
            {
                [ParquetColumn("same_name")]
                public int Id { get; init; }

                [ParquetColumn("same_name")]
                public string Name { get; init; } = string.Empty;
            }
            """;

        var (diagnostics, _) = RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == DiagnosticDescriptors.DuplicateColumnName.Id);
    }

    [Fact]
    public void NoPropertiesTriggersPARQ003()
    {
        string source = """
            using Parquet.SourceGenerator;

            [ParquetSerializable]
            public partial class EmptyClass
            {
            }
            """;

        var (diagnostics, _) = RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == DiagnosticDescriptors.NoPropertiesFound.Id);
    }

    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<SyntaxTree> OutputTrees) RunGenerator(string source)
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ParquetSerializableAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location)
        };

        CSharpCompilation compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ParquetIncrementalGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out var diagnostics);

        return (diagnostics, outputCompilation.SyntaxTrees.ToList());
    }
}
