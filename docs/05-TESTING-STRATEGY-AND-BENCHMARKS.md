# 05 - Testing Machinery & Benchmarking Strategy

## Overview

Testing a C# Roslyn Source Generator requires specialized machinery compared to standard application development. Because the generator executes *inside* the compiler host against arbitrary user syntax trees, testing must validate:
1. **Source Generation Correctness**: Emitted C# code structure and syntax.
2. **Roslyn Incremental Performance & Caching**: Cache retention across syntax tree mutations.
3. **Binary Serialization Roundtrips**: End-to-end reading/writing with `Parquet.Net`.
4. **Native AOT & Trimming**: Zero reflection and zero trim warnings.
5. **Runtime Benchmarks**: Execution speed and allocation comparisons against reflection.

---

## 1. Generator Unit & Snapshot Testing (`Verify.SourceGenerators`)

Unit tests inspect the source generator output directly using Roslyn's `CSharpGeneratorDriver` combined with `Verify.Xunit` snapshot testing.

### Test Harness Setup
```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Parquet.SourceGenerator;
using VerifyXunit;

public static class ModuleInitializer
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    public static void Init() => VerifySourceGenerators.Initialize();
}

public class GeneratorSnapshotTests
{
    [Fact]
    public Task GeneratesCorrectSerializerForPoco()
    {
        string source = """
            using System;
            using Parquet.SourceGenerator;

            namespace TestApp;

            [ParquetSerializable]
            public partial record Customer
            {
                [ParquetColumn("customer_id")]
                public Guid Id { get; init; }

                public string Name { get; init; } = string.Empty;
            }
            """;

        Compilation compilation = CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source) },
            references: new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(ParquetSerializableAttribute).Assembly.Location)
            });

        var generator = new ParquetIncrementalGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGenerators(compilation);

        return Verifier.Verify(driver);
    }
}
```

---

## 2. Incremental Cache Testing (`TrackIncrementalSteps`)

To ensure Roslyn doesn't re-run expensive generator transforms on unrelated edits (e.g., adding a comment or editing a method body), we test incremental step caching:

```csharp
[Fact]
public void GeneratorCachesOutputsOnUnrelatedChanges()
{
    string initialCode = "/* initial code */";
    string modifiedCode = "/* modified code with added method */";

    var driver = CSharpGeneratorDriver.Create(
        generators: new[] { new ParquetIncrementalGenerator().AsSourceGenerator() },
        driverOptions: new GeneratorDriverOptions(
            disabledOutputs: IncrementalGeneratorOutputKind.None,
            trackIncrementalSteps: true));

    // Run 1
    driver = driver.RunGenerators(compilation1);
    
    // Run 2 with modified compilation
    driver = driver.RunGenerators(compilation2);

    GeneratorDriverRunResult result = driver.GetRunResult();
    
    // Assert generator steps were cached rather than recalculated
    var stepState = result.Results[0].TrackedSteps["TransformModelStep"];
    Assert.All(stepState, step => Assert.Equal(IncrementalStepRunReason.Cached, step.Outputs[0].Reason));
}
```

---

## 3. End-to-End Binary Data Roundtrip Testing

Integration tests verify that data written with generated code can be parsed seamlessly by standard `ParquetReader` and vice versa.

```csharp
[Fact]
public async Task Roundtrip_Poco_Matches_ParquetNet()
{
    var records = new List<TestRecord>
    {
        new(Guid.NewGuid(), "Alice", 100.50m, DateTime.UtcNow),
        new(Guid.NewGuid(), "Bob", 250.00m, DateTime.UtcNow)
    };

    using var stream = new MemoryStream();

    // 1. Write using generated serializer
    await records.WriteParquetAsync(stream);

    // 2. Read using standard ParquetReader (Parquet.Net)
    stream.Position = 0;
    using var reader = await ParquetReader.CreateAsync(stream);
    Assert.Equal(1, reader.RowGroupCount);
    Assert.Equal(4, reader.Schema.DataFields.Length);

    // 3. Read back using generated deserializer
    stream.Position = 0;
    List<TestRecord> readRecords = await TestRecordParquetExtensions.ReadParquetAsync(stream);

    Assert.Equal(records.Count, readRecords.Count);
    Assert.Equal(records[0].Name, readRecords[0].Name);
}
```

---

## 4. Native AOT & Trimming Verification

A dedicated project target (`test/Parquet.SourceGenerator.AotTest`) tests compilation with `<PublishAot>true</PublishAot>`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <PublishAot>true</PublishAot>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

Testing Command:
```bash
dotnet publish test/Parquet.SourceGenerator.AotTest/Parquet.SourceGenerator.AotTest.csproj -c Release
```
*Result*: Asserts that `dotnet publish` completes without emitting any trimming warnings (`IL2026`, `IL3050`) or reflection errors.

---

## 5. Performance Benchmarks (`BenchmarkDotNet`)

Located in `benchmarks/Parquet.SourceGenerator.Benchmarks`:

```csharp
[MemoryDiagnoser]
[Orderer(BenchmarkDotNet.Order.SummaryOrderPolicy.FastestToSlowest)]
public class SerializationBenchmark
{
    private List<TransactionLog> _data = null!;

    [GlobalSetup]
    public void Setup()
    {
        _data = Enumerable.Range(0, 100_000)
            .Select(i => new TransactionLog(Guid.NewGuid(), $"User_{i}", i * 1.5m, DateTime.UtcNow))
            .ToList();
    }

    [Benchmark(Baseline = true)]
    public async Task Reflection_ParquetConvert()
    {
        using var stream = new MemoryStream();
        await ParquetConvert.SerializeAsync(_data, stream);
    }

    [Benchmark]
    public async Task SourceGenerator_WriteParquetAsync()
    {
        using var stream = new MemoryStream();
        await _data.WriteParquetAsync(stream);
    }
}
```
