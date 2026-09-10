extern alias LegacyGenerator;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Parquet.SourceGenerator.Diagnostics;
using Parquet.SourceGenerator.Emitter;
using Parquet.SourceGenerator.Models;
using Xunit;
using IODirectory = System.IO.Directory;
using IOFile = System.IO.File;
using IOPath = System.IO.Path;
using LegacyEmitter = LegacyGenerator::Parquet.SourceGenerator.Legacy.Emitter.LegacyCodeEmitter;
using LegacyModels = LegacyGenerator::Parquet.SourceGenerator.Models;

namespace Parquet.SourceGenerator.Tests;

/// <summary>
/// Golden-master code generation regression suite.
/// Emulates the source generator across diverse canonical models and asserts:
/// 1. Emitted source exactly matches the source-controlled golden .cs file.
/// 2. Full C# syntax validity (parses with 0 diagnostics).
/// 3. In-memory Roslyn compilation with 0 compile errors against Parquet.Net.
/// </summary>
public sealed class GoldenCodeGenRegressionTests
{
    private static readonly string GoldenFilesDir = IOPath.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "..",
        "..",
        "..",
        "GoldenFiles"
    );

    private static bool UpdateGolden =>
        string.Equals(
            Environment.GetEnvironmentVariable("UPDATE_GOLDEN_FILES"),
            "true",
            StringComparison.OrdinalIgnoreCase
        );

    private static void AssertGoldenMatch(string fileName, string emittedSource)
    {
        string filePath = IOPath.Combine(GoldenFilesDir, fileName);
        bool updateGolden = UpdateGolden;
        string normalizedEmitted = emittedSource.Replace("\r\n", "\n").TrimEnd();

        if (updateGolden || !IOFile.Exists(filePath))
        {
            IODirectory.CreateDirectory(GoldenFilesDir);
            IOFile.WriteAllText(filePath, normalizedEmitted + "\n");
        }

        string expectedSource = IOFile.ReadAllText(filePath).Replace("\r\n", "\n").TrimEnd();

        // 1. Exact string-level consistency against source-controlled golden file
        Assert.Equal(expectedSource, normalizedEmitted);

        // 2. Verify Roslyn parses emitted source without syntax errors
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(emittedSource);
        IEnumerable<Diagnostic> syntaxDiagnostics = syntaxTree
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(syntaxDiagnostics);

        // 3. Signature-only public API baseline, from the same emitted string as the golden file
        //    above, so the two physically cannot drift (issue #215).
        AssertApiBaselineMatch(fileName, emittedSource, updateGolden);
    }

    /// <summary>
    /// Asserts the signature-only baseline that sits next to each golden file. Format and its
    /// rationale are documented on <see cref="GeneratedApiBaseline"/> and in
    /// <c>docs/17-GENERATED-API-BASELINES.md</c>.
    /// </summary>
    private static void AssertApiBaselineMatch(
        string goldenFileName,
        string emittedSource,
        bool updateGolden
    )
    {
        string baselineFileName = BaselineFileNameFor(goldenFileName);
        string baselinePath = IOPath.Combine(GoldenFilesDir, baselineFileName);
        string actual = GeneratedApiBaseline.Create(emittedSource);

        if (updateGolden || !IOFile.Exists(baselinePath))
        {
            IODirectory.CreateDirectory(GoldenFilesDir);
            IOFile.WriteAllText(baselinePath, actual);
        }

        string expected = IOFile.ReadAllText(baselinePath).Replace("\r\n", "\n");

        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            Assert.Fail(DescribeBaselineDrift(baselineFileName, expected, actual));
        }
    }

    /// <summary>Maps <c>Name.g.cs</c> to its baseline companion <c>Name.api.txt</c>.</summary>
    private static string BaselineFileNameFor(string goldenFileName)
    {
        const string GeneratedSuffix = ".g.cs";
        string stem = goldenFileName.EndsWith(GeneratedSuffix, StringComparison.Ordinal)
            ? goldenFileName.Substring(0, goldenFileName.Length - GeneratedSuffix.Length)
            : IOPath.GetFileNameWithoutExtension(goldenFileName);
        return stem + ".api.txt";
    }

    /// <summary>
    /// Builds a failure message that names the baseline file and the exact members that appeared or
    /// disappeared, rather than dumping two multi-thousand-character strings at the reader.
    /// </summary>
    private static string DescribeBaselineDrift(
        string baselineFileName,
        string expected,
        string actual
    )
    {
        string[] expectedLines = SplitBaseline(expected);
        string[] actualLines = SplitBaseline(actual);

        List<string> removed = expectedLines.Except(actualLines, StringComparer.Ordinal).ToList();
        List<string> added = actualLines.Except(expectedLines, StringComparer.Ordinal).ToList();

        var message = new System.Text.StringBuilder();
        message
            .Append("Generated public API baseline drifted: GoldenFiles/")
            .Append(baselineFileName)
            .Append('\n');
        message.Append(
            "The emitted public surface no longer matches the checked-in baseline. If the change is "
        );
        message.Append(
            "intended, refresh it with UPDATE_GOLDEN_FILES=true (or comment /update-golden on the PR) "
        );
        message.Append("and commit the result.\n");

        foreach (string line in removed)
        {
            message.Append("  - removed: ").Append(line).Append('\n');
        }

        foreach (string line in added)
        {
            message.Append("  + added:   ").Append(line).Append('\n');
        }

        if (removed.Count == 0 && added.Count == 0)
        {
            message.Append(
                "  (no member differences; the file differs only in ordering or whitespace — "
            );
            message.Append("regenerate it rather than hand-editing.)\n");
        }
        else
        {
            message
                .Append("  ")
                .Append(removed.Count)
                .Append(" removed, ")
                .Append(added.Count)
                .Append(" added, ")
                .Append(actualLines.Length)
                .Append(" public members emitted in total.\n");
        }

        return message.ToString();
    }

    private static string[] SplitBaseline(string baseline) =>
        baseline
            .Split('\n')
            .Where(line =>
                line.Length > 0
                && !string.Equals(
                    line,
                    GeneratedApiBaseline.NullableHeader,
                    StringComparison.Ordinal
                )
            )
            .ToArray();

    [Fact]
    public void GoldenMasterComprehensiveModernV6Model()
    {
        var properties = new[]
        {
            new PropertyModel(
                "Id",
                "id",
                "int",
                null,
                null,
                1,
                null,
                null,
                PropertyKind.Primitive,
                false
            ),
            new PropertyModel(
                "Name",
                "name",
                "string?",
                null,
                null,
                2,
                null,
                null,
                PropertyKind.Primitive,
                true
            ),
            new PropertyModel(
                "Score",
                "score",
                "double",
                null,
                null,
                3,
                null,
                null,
                PropertyKind.Primitive,
                false
            ),
            new PropertyModel(
                "Price",
                "price",
                "decimal",
                null,
                null,
                4,
                18,
                4,
                PropertyKind.Decimal,
                false
            ),
            new PropertyModel(
                "CreatedAt",
                "created_at",
                "System.DateTime",
                "Microseconds",
                null,
                5,
                null,
                null,
                PropertyKind.DateTime,
                false
            ),
            new PropertyModel(
                "Duration",
                "duration",
                "System.TimeSpan",
                null,
                null,
                6,
                null,
                null,
                PropertyKind.TimeSpan,
                false
            ),
            new PropertyModel(
                "CorrelationId",
                "correlation_id",
                "System.Guid",
                null,
                null,
                7,
                null,
                null,
                PropertyKind.Guid,
                false
            ),
            new PropertyModel(
                "OptionalGuid",
                "optional_guid",
                "System.Guid?",
                null,
                null,
                8,
                null,
                null,
                PropertyKind.Guid,
                true
            ),
            new PropertyModel(
                "Payload",
                "payload",
                "byte[]",
                null,
                null,
                9,
                null,
                null,
                PropertyKind.ByteArray,
                true
            ),
        };

        var model = new TargetClassModel(
            Namespace: "SampleDomain.Models",
            ClassName: "OrderEvent",
            Properties: new EquatableArray<PropertyModel>(properties)
        );

        string emittedSource = CodeEmitter.EmitSource(model);
        AssertGoldenMatch("OrderEventParquetExtensions.g.cs", emittedSource);
    }

    [Fact]
    public void GoldenMasterScalarsAndEnumsModel()
    {
        var properties = new[]
        {
            new PropertyModel(
                "RowId",
                "row_id",
                "long",
                null,
                null,
                1,
                null,
                null,
                PropertyKind.Primitive,
                false
            ),
            new PropertyModel(
                "Flag",
                "is_valid",
                "bool",
                null,
                null,
                2,
                null,
                null,
                PropertyKind.Primitive,
                false
            ),
            new PropertyModel(
                "NullableFlag",
                "maybe_flag",
                "bool?",
                null,
                null,
                3,
                null,
                null,
                PropertyKind.Primitive,
                true
            ),
            new PropertyModel(
                "StatusCode",
                "status",
                "SampleDomain.Models.ProcessStatus",
                null,
                "int",
                4,
                null,
                null,
                PropertyKind.Enum,
                false
            ),
            new PropertyModel(
                "OptionalStatus",
                "opt_status",
                "SampleDomain.Models.ProcessStatus?",
                null,
                "int",
                5,
                null,
                null,
                PropertyKind.Enum,
                true
            ),
            new PropertyModel(
                "TinyNum",
                "tiny_num",
                "byte",
                null,
                null,
                6,
                null,
                null,
                PropertyKind.Primitive,
                false
            ),
            new PropertyModel(
                "ShortNum",
                "short_num",
                "short",
                null,
                null,
                7,
                null,
                null,
                PropertyKind.Primitive,
                false
            ),
            new PropertyModel(
                "FloatVal",
                "float_val",
                "float",
                null,
                null,
                8,
                null,
                null,
                PropertyKind.Primitive,
                false
            ),
        };

        var model = new TargetClassModel(
            Namespace: "SampleDomain.Models",
            ClassName: "ScalarMetric",
            Properties: new EquatableArray<PropertyModel>(properties)
        );

        string emittedSource = CodeEmitter.EmitSource(model);
        AssertGoldenMatch("ScalarMetricParquetExtensions.g.cs", emittedSource);
    }

    [Fact]
    public void GoldenMasterLegacyV4V5DataColumnModel()
    {
        var properties = new[]
        {
            new LegacyModels.PropertyModel(
                "Id",
                "id",
                "int",
                null,
                null,
                1,
                null,
                null,
                LegacyModels.PropertyKind.Primitive,
                false
            ),
            new LegacyModels.PropertyModel(
                "Description",
                "desc",
                "string",
                null,
                null,
                2,
                null,
                null,
                LegacyModels.PropertyKind.Primitive,
                true
            ),
            new LegacyModels.PropertyModel(
                "RawData",
                "raw_data",
                "byte[]",
                null,
                null,
                3,
                null,
                null,
                LegacyModels.PropertyKind.ByteArray,
                true
            ),
            new LegacyModels.PropertyModel(
                "Level",
                "level",
                "SampleDomain.Models.AccessLevel",
                null,
                "int",
                4,
                null,
                null,
                LegacyModels.PropertyKind.Enum,
                false
            ),
        };

        var model = new LegacyModels.TargetClassModel(
            Namespace: "SampleDomain.Models",
            ClassName: "LegacyRecord",
            Properties: new LegacyModels.EquatableArray<LegacyModels.PropertyModel>(properties)
        );

        string emittedSource = LegacyEmitter.EmitSource(model);
        AssertGoldenMatch("LegacyRecordParquetLegacyExtensions.g.cs", emittedSource);
    }

    [Fact]
    public void EmittedSourceCompilesCleanlyWithRoslyn()
    {
        string modelSource = """
            namespace GoldenTest;

            using System;
            using Parquet.SourceGenerator;

            [ParquetSerializable]
            public partial record GoldenModel
            {
                [ParquetColumn("id")]
                public int Id { get; init; }

                [ParquetColumn("title")]
                public string Title { get; init; } = string.Empty;

                [ParquetColumn("is_active")]
                public bool IsActive { get; init; }

                [ParquetColumn("created_utc")]
                public DateTime CreatedUtc { get; init; }
            }
            """;

        var (diagnostics, outputTrees) = RunGenerator(modelSource);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.True(
            outputTrees.Count >= 2,
            "Expected generator to emit at least 1 syntax tree besides the input."
        );

        // Grab emitted extensions source
        SyntaxTree emittedTree = outputTrees[outputTrees.Count - 1];
        string emittedCode = emittedTree.ToString();

        Assert.Contains("public static partial class GoldenModelParquetExtensions", emittedCode);
        Assert.Contains("WriteParquetAsync", emittedCode);
        Assert.Contains("ReadParquetParallelAsync", emittedCode);
    }

    [Fact]
    public void GoldenMasterNestedStructModel()
    {
        // Compound members through the REAL pipeline (issue #176 M2): driver-generated
        // source is golden-matched and must compile clean against Parquet.Net. Covers the
        // value-type-struct ladder branches no reference-type round-trip exercises.
        string source = """
            using Parquet.SourceGenerator;

            namespace SampleDomain.Models;

            [ParquetSerializable]
            public partial record Address
            {
                public string? City { get; init; }
                public int? Zip { get; init; }
            }

            [ParquetSerializable]
            public partial struct Point
            {
                public int X { get; init; }
                public int Y { get; init; }
            }

            [ParquetSerializable]
            public partial record NestedOrder
            {
                public int Id { get; init; }
                public Address? Ship { get; init; }
                public Address Bill { get; init; } = new();
                public Point Origin { get; init; }
                public Point? Start { get; init; }
            }
            """;

        var (diagnostics, outputTrees) = RunGenerator(source);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        string generated = outputTrees[outputTrees.Count - 1].ToString();
        AssertGoldenMatch("NestedOrderParquetExtensions.g.cs", generated);
    }

    [Fact]
    public void GoldenMasterRowLevelListsModel()
    {
        // M3a (#176): row-level lists/arrays with leaf elements through the real pipeline.
        string source = """
            using Parquet.SourceGenerator;
            using System;
            using System.Collections.Generic;

            namespace SampleDomain.Models;

            [ParquetSerializable]
            public partial record ListOrder
            {
                public int Id { get; init; }
                public List<string?>? Tags { get; init; }
                public List<int> Scores { get; init; } = new();
                public Guid[]? Keys { get; init; }
            }
            """;

        var (diagnostics, outputTrees) = RunGenerator(source);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        string generated = outputTrees[outputTrees.Count - 1].ToString();
        Assert.Contains("ListField(", generated);
        Assert.Contains("repLevels_", generated);
        AssertGoldenMatch("ListOrderParquetExtensions.g.cs", generated);
    }

    private static (
        IReadOnlyList<Diagnostic> Diagnostics,
        IReadOnlyList<SyntaxTree> OutputTrees
    ) RunGenerator(string source)
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
            MetadataReference.CreateFromFile(
                typeof(ParquetSerializableAttribute).Assembly.Location
            ),
            MetadataReference.CreateFromFile(typeof(Parquet.ParquetReader).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Collections").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Threading").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Threading.Tasks").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Linq").Location),
        };

        CSharpCompilation compilation = CSharpCompilation.Create(
            "GoldenTestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );

        var generator = new ParquetIncrementalGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out var diagnostics
        );

        return (diagnostics, outputCompilation.SyntaxTrees.ToList());
    }
}
