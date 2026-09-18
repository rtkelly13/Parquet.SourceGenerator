using System;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Parquet.SourceGenerator;

namespace Parquet.SourceGenerator.Benchmarks;

/// <summary>
/// Manual throughput/allocation measurement for issue #258. The incremental edit benchmarks
/// measure only the second driver pass; their initial pass is prepared by <see cref="IterationSetup"/>.
/// </summary>
/// <remarks>
/// This intentionally measures the generator driver, not a complete MSBuild build. It isolates
/// the work affected by a source edit and keeps compiler process startup, restore, and unrelated
/// project targets out of the result. BenchmarkDotNet is not a CI gate in this repository.
/// </remarks>
[MemoryDiagnoser]
[InProcess]
[WarmupCount(3)]
[IterationCount(10)]
[Orderer(BenchmarkDotNet.Order.SummaryOrderPolicy.FastestToSlowest)]
public class IncrementalityBenchmark
{
    private const string ModelASource = """
        using Parquet.SourceGenerator;

        namespace Demo;

        [ParquetSerializable]
        public partial class ModelA
        {
            [ParquetColumn("id")]
            public int Id { get; init; }
        }
        """;

    private const string ModelBSource = """
        using Parquet.SourceGenerator;

        namespace Demo;

        [ParquetSerializable]
        public partial class ModelB
        {
            [ParquetColumn("name")]
            public string Name { get; init; } = string.Empty;
        }
        """;

    private const string UnrelatedSource = """
        namespace Demo;

        internal static class Unrelated
        {
            public const int Value = 1;
        }
        """;

    private CSharpCompilation _initialCompilation = null!;
    private CSharpCompilation _unrelatedEdit = null!;
    private CSharpCompilation _modelEdit = null!;
    private GeneratorDriver _preparedDriver = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        MetadataReference[] references =
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
            MetadataReference.CreateFromFile(
                typeof(ParquetSerializableAttribute).Assembly.Location
            ),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Collections").Location),
        ];

        SyntaxTree modelA = CSharpSyntaxTree.ParseText(ModelASource, path: "ModelA.cs");
        SyntaxTree modelB = CSharpSyntaxTree.ParseText(ModelBSource, path: "ModelB.cs");
        SyntaxTree unrelated = CSharpSyntaxTree.ParseText(UnrelatedSource, path: "Unrelated.cs");
        _initialCompilation = CSharpCompilation.Create(
            "IncrementalityBenchmarkAssembly",
            [modelA, modelB, unrelated],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );
        _unrelatedEdit = _initialCompilation.ReplaceSyntaxTree(
            unrelated,
            CSharpSyntaxTree.ParseText(
                UnrelatedSource.Replace("Value = 1", "Value = 2"),
                path: "Unrelated.cs"
            )
        );
        _modelEdit = _initialCompilation.ReplaceSyntaxTree(
            modelA,
            CSharpSyntaxTree.ParseText(
                ModelASource.Replace("public int Id", "public long Id"),
                path: "ModelA.cs"
            )
        );
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _preparedDriver = CreateDriver().RunGenerators(_initialCompilation);
    }

    [Benchmark(Baseline = true, Description = "Initial generation")]
    public int InitialGeneration()
    {
        GeneratorDriver driver = CreateDriver().RunGenerators(_initialCompilation);
        return GeneratedSourceCount(driver);
    }

    [Benchmark(Description = "Unrelated-file edit")]
    public int UnrelatedFileEdit()
    {
        _preparedDriver = _preparedDriver.RunGenerators(_unrelatedEdit);
        return GeneratedSourceCount(_preparedDriver);
    }

    [Benchmark(Description = "Per-model edit")]
    public int PerModelEdit()
    {
        _preparedDriver = _preparedDriver.RunGenerators(_modelEdit);
        return GeneratedSourceCount(_preparedDriver);
    }

    private static int GeneratedSourceCount(GeneratorDriver driver)
    {
        int count = 0;
        foreach (GeneratorRunResult result in driver.GetRunResult().Results)
        {
            count += result.GeneratedSources.Length;
        }

        return count;
    }

    private static CSharpGeneratorDriver CreateDriver() =>
        (CSharpGeneratorDriver)
            CSharpGeneratorDriver.Create(
                [new ParquetIncrementalGenerator().AsSourceGenerator()],
                driverOptions: new GeneratorDriverOptions(
                    IncrementalGeneratorOutputKind.None,
                    trackIncrementalGeneratorSteps: false
                )
            );
}
