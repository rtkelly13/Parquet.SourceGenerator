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

    [Fact]
    public void NonPublicPropertyTriggersPARQ004()
    {
        string source = """
            using Parquet.SourceGenerator;

            [ParquetSerializable]
            public partial class PrivatePropClass
            {
                [ParquetColumn("secret_id")]
                private int SecretId { get; init; }

                [ParquetColumn("id")]
                public int Id { get; init; }
            }
            """;

        var (diagnostics, _) = RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == DiagnosticDescriptors.NonPublicPropertyIgnored.Id);
    }

    [Fact]
    public void InvalidDecimalPrecisionScaleTriggersPARQ005()
    {
        string source = """
            using Parquet.SourceGenerator;

            [ParquetSerializable]
            public partial class InvalidDecimalClass
            {
                [ParquetColumn("amount")]
                [ParquetDecimal(2, 5)]
                public decimal Amount { get; init; }
            }
            """;

        var (diagnostics, _) = RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == DiagnosticDescriptors.InvalidDecimalPrecisionScale.Id);
    }

    [Theory]
    [InlineData("char", "Initial")]
    [InlineData("System.DateTimeOffset", "OccurredAt")]
    [InlineData("System.Collections.Generic.List<int>", "Tags")]
    [InlineData("int[]", "Values")]
    public void UnsupportedMemberTypeTriggersPARQ006(string typeName, string memberName)
    {
        // Parquet.Net's SchemaEncoder.SupportedTypes has no entry for any of these, so they used to
        // build cleanly and then throw from inside Parquet.Net at write time. DateTimeOffset was the
        // worst of them: it was mapped to PropertyKind.DateTime, so it produced a plausible-looking
        // DateTimeDataField and failed on the type mismatch when writing.
        string source = $$"""
            using Parquet.SourceGenerator;

            [ParquetSerializable]
            public partial class UnsupportedTypeClass
            {
                [ParquetColumn("id")]
                public int Id { get; init; }

                [ParquetColumn("bad")]
                public {{typeName}} {{memberName}} { get; init; }
            }
            """;

        var (diagnostics, _) = RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == DiagnosticDescriptors.UnsupportedPropertyType.Id);
    }

    [Theory]
    [InlineData("bool")]
    [InlineData("byte")]
    [InlineData("sbyte")]
    [InlineData("short")]
    [InlineData("ushort")]
    [InlineData("uint")]
    [InlineData("ulong")]
    [InlineData("float")]
    [InlineData("System.DateOnly")]
    [InlineData("System.TimeOnly")]
    [InlineData("System.Numerics.BigInteger")]
    [InlineData("System.Guid")]
    [InlineData("System.TimeSpan")]
    [InlineData("decimal")]
    [InlineData("byte[]")]
    [InlineData("int?")]
    public void SupportedMemberTypeDoesNotTriggerPARQ006(string typeName)
    {
        // The whole point of aligning the allowlist with Parquet.Net's own SupportedTypes: PARQ006
        // must never fail a build that would otherwise have worked. These are all types Parquet.Net
        // accepts, several of which an over-narrow allowlist would plausibly have missed.
        string source = $$"""
            using Parquet.SourceGenerator;

            [ParquetSerializable]
            public partial class SupportedTypeClass
            {
                [ParquetColumn("value")]
                public {{typeName}} Value { get; init; }
            }
            """;

        var (diagnostics, _) = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == DiagnosticDescriptors.UnsupportedPropertyType.Id);
    }

    [Fact]
    public void GetOnlyPropertyTriggersPARQ007()
    {
        // The read path materialises through an object initializer, so this used to emit CS0200
        // against the generated file with nothing pointing back at the declaration.
        string source = """
            using Parquet.SourceGenerator;

            [ParquetSerializable]
            public partial class GetOnlyClass
            {
                [ParquetColumn("id")]
                public int Id { get; init; }

                [ParquetColumn("computed")]
                public int Computed => Id * 2;
            }
            """;

        var (diagnostics, _) = RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == DiagnosticDescriptors.MemberNotAssignable.Id);
    }

    [Fact]
    public void ReadonlyFieldTriggersPARQ007()
    {
        string source = """
            using Parquet.SourceGenerator;

            [ParquetSerializable]
            public partial class ReadonlyFieldClass
            {
                [ParquetColumn("id")]
                public readonly int Id;
            }
            """;

        var (diagnostics, _) = RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == DiagnosticDescriptors.MemberNotAssignable.Id);
    }

    [Fact]
    public void IgnoredUnassignableMemberIsNotReported()
    {
        // [ParquetIgnore] is the documented escape hatch in both PARQ006 and PARQ007's message, so
        // it has to actually work — the ignore check runs before either rule.
        string source = """
            using Parquet.SourceGenerator;

            [ParquetSerializable]
            public partial class IgnoredComputedClass
            {
                [ParquetColumn("id")]
                public int Id { get; init; }

                [ParquetIgnore]
                public int Computed => Id * 2;

                [ParquetIgnore]
                public System.DateTimeOffset OccurredAt { get; init; }
            }
            """;

        var (diagnostics, _) = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == DiagnosticDescriptors.MemberNotAssignable.Id);
        Assert.DoesNotContain(diagnostics, d => d.Id == DiagnosticDescriptors.UnsupportedPropertyType.Id);
    }

    [Fact]
    public void PositionalRecordTriggersPARQ008()
    {
        // A positional record synthesises a primary constructor and a copy constructor but no
        // parameterless one, so `new Person { Id = ..., Name = ... }` failed with CS7036. This is
        // the exact shape the roadmap document used as its example.
        string source = """
            using Parquet.SourceGenerator;

            [ParquetSerializable]
            public partial record PositionalPerson(int Id, string Name);
            """;

        var (diagnostics, _) = RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == DiagnosticDescriptors.NoParameterlessConstructor.Id);
    }

    [Fact]
    public void ClassWithOnlyParameterisedConstructorTriggersPARQ008()
    {
        string source = """
            using Parquet.SourceGenerator;

            [ParquetSerializable]
            public partial class ConstructedClass
            {
                public ConstructedClass(int id) => Id = id;

                [ParquetColumn("id")]
                public int Id { get; set; }
            }
            """;

        var (diagnostics, _) = RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == DiagnosticDescriptors.NoParameterlessConstructor.Id);
    }

    [Fact]
    public void StructNeedsNoDeclaredParameterlessConstructor()
    {
        // Value types always have one, so PARQ008 must not fire on them.
        string source = """
            using Parquet.SourceGenerator;

            [ParquetSerializable]
            public partial struct PointRow
            {
                [ParquetColumn("x")]
                public int X { get; set; }
            }
            """;

        var (diagnostics, _) = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == DiagnosticDescriptors.NoParameterlessConstructor.Id);
    }

    [Fact]
    public void RejectedMembersSuppressTheMisleadingPARQ003()
    {
        // "no serializable properties" is the wrong explanation when the members were rejected;
        // PARQ006 already gives the real one.
        string source = """
            using Parquet.SourceGenerator;

            [ParquetSerializable]
            public partial class AllUnsupportedClass
            {
                [ParquetColumn("bad")]
                public char Initial { get; init; }
            }
            """;

        var (diagnostics, _) = RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == DiagnosticDescriptors.UnsupportedPropertyType.Id);
        Assert.DoesNotContain(diagnostics, d => d.Id == DiagnosticDescriptors.NoPropertiesFound.Id);
    }

    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<SyntaxTree> OutputTrees) RunGenerator(string source)
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ParquetSerializableAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            // Needed so the types exercised below resolve for real. An unresolved type becomes an
            // error type, which the parser deliberately skips — so without these references the
            // supported-type cases would pass without ever reaching the allowlist.
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime.Numerics").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Collections").Location)
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
