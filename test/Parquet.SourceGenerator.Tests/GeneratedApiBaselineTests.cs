using System.Globalization;
using Parquet.SourceGenerator.ApiGates;
using Shouldly;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

/// <summary>
/// Unit tests for the signature-only baseline renderer that backs the <c>.api.txt</c> files sitting
/// next to the golden generated sources (issue #215). The golden tests prove the baselines match
/// the emitters; these prove the renderer's own contract — visibility, format and, above all,
/// determinism — on inputs small enough to read.
/// </summary>
public sealed class GeneratedApiBaselineTests
{
    private const string Sample = """
        #nullable enable
        namespace Sample.Space;

        public static partial class Widget
        {
            public static readonly global::Parquet.Schema.ParquetSchema Schema = null!;

            private struct Hidden
            {
                public int NotApi => 1;
            }

            internal const int AlsoNotApi = 3;

            public const int Limit = 512;

            public static async global::System.Threading.Tasks.Task<
                global::System.Collections.Generic.List<int>
            > ReadAsync(
                global::System.IO.Stream stream,
                int maxDegreeOfParallelism = -1,
                global::System.Threading.CancellationToken cancellationToken = default
            )
            {
                await global::System.Threading.Tasks.Task.Yield();
                return new();
            }

            public static void Write(this global::System.IO.Stream stream, string? label) { }

            public readonly struct Batch
            {
                public int RowCount { get; }

                public string? Label { get; init; }

                internal Batch(int rowCount) => RowCount = rowCount;
            }
        }
        """;

    [Fact]
    public void BaselineStartsWithTheNullableHeaderAndListsOneMemberPerLine()
    {
        string baseline = GeneratedApiBaseline.Create(Sample);

        string[] lines = baseline.Split('\n');
        lines[0].ShouldBe(GeneratedApiBaseline.NullableHeader);
        lines[^1].ShouldBe(string.Empty);
        lines[1..^1].ShouldAllBe(line => !string.IsNullOrWhiteSpace(line));
    }

    [Fact]
    public void SignaturesUseThePublicApiGrammarWithGlobalQualifiersStripped()
    {
        string baseline = GeneratedApiBaseline.Create(Sample);

        baseline.ShouldContain(
            "static Sample.Space.Widget.ReadAsync(System.IO.Stream stream, int maxDegreeOfParallelism = -1, "
                + "System.Threading.CancellationToken cancellationToken = default) -> "
                + "System.Threading.Tasks.Task<System.Collections.Generic.List<int>>\n",
            Case.Sensitive
        );
        baseline.ShouldContain(
            "static Sample.Space.Widget.Write(this System.IO.Stream stream, string? label) -> void\n",
            Case.Sensitive
        );
        baseline.ShouldContain(
            "static readonly Sample.Space.Widget.Schema -> Parquet.Schema.ParquetSchema\n",
            Case.Sensitive
        );
        baseline.ShouldContain("const Sample.Space.Widget.Limit = 512 -> int\n", Case.Sensitive);
    }

    [Fact]
    public void NestedTypesAreListedAndAccessorsSplitIntoOneLineEach()
    {
        string baseline = GeneratedApiBaseline.Create(Sample);

        baseline.ShouldContain("Sample.Space.Widget.Batch\n", Case.Sensitive);
        baseline.ShouldContain("Sample.Space.Widget.Batch.RowCount.get -> int\n", Case.Sensitive);
        baseline.ShouldContain("Sample.Space.Widget.Batch.Label.get -> string?\n", Case.Sensitive);
        baseline.ShouldContain("Sample.Space.Widget.Batch.Label.init -> void\n", Case.Sensitive);
    }

    [Fact]
    public void NonPublicDeclarationsAndPublicMembersOfPrivateTypesAreExcluded()
    {
        string baseline = GeneratedApiBaseline.Create(Sample);

        baseline.ShouldNotContain("Hidden", Case.Sensitive);
        baseline.ShouldNotContain("NotApi", Case.Sensitive);
        baseline.ShouldNotContain("AlsoNotApi", Case.Sensitive);
        // The internal constructor of a public struct is not public surface either.
        baseline.ShouldNotContain("Widget.Batch.Batch(", Case.Sensitive);
    }

    [Fact]
    public void OrderingIsOrdinalAndIndependentOfTheAmbientCulture()
    {
        // A culture-sensitive sort reorders these; StringComparer.Ordinal does not. The repository
        // has been bitten by exactly this before (the MA0002 fix in tools/BenchmarkSummaryGenerator).
        const string CaseSensitive = """
            namespace N;

            public static class T
            {
                public static void Item() { }

                public static void ITEM() { }

                public static void iTem() { }

                public static void Ítem() { }
            }
            """;

        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            string turkish = GeneratedApiBaseline.Create(CaseSensitive);

            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string invariant = GeneratedApiBaseline.Create(CaseSensitive);

            turkish.ShouldBe(invariant);

            string[] members = turkish.Split('\n').Skip(1).Where(line => line.Length > 0).ToArray();
            members.OrderBy(line => line, StringComparer.Ordinal).ToArray().ShouldBe(members);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void RenderingIsStableAcrossRepeatedRuns()
    {
        GeneratedApiBaseline.Create(Sample).ShouldBe(GeneratedApiBaseline.Create(Sample));
    }

    [Fact]
    public void ShapeSummaryCountsPublicCallableParameterSlots()
    {
        string summary = GeneratedApiBaseline.CreateShapeSummary(Sample);

        summary.ShouldBe(GeneratedApiBaseline.ShapeSummaryHeader + "\nMEMBERS=9 PARAMETERS=5\n");
        GeneratedApiBaseline.CountParameterSlots(Sample).ShouldBe(5);
    }

    [Fact]
    public void ParameterMetricMakesAChangedSignatureNumericWithoutChangingMemberCount()
    {
        const string Before = """
            namespace N;

            public static class Reader
            {
                public static void Read(System.IO.Stream stream, int max = -1) { }
            }
            """;
        const string After = """
            namespace N;

            public static class Reader
            {
                public static void Read(System.IO.Stream stream) { }
            }
            """;

        GeneratedApiBaseline
            .CountMembers(GeneratedApiBaseline.Create(Before))
            .ShouldBe(GeneratedApiBaseline.CountMembers(GeneratedApiBaseline.Create(After)));
        GeneratedApiBaseline
            .CreateShapeSummary(Before)
            .ShouldBe(GeneratedApiBaseline.ShapeSummaryHeader + "\nMEMBERS=2 PARAMETERS=2\n");
        GeneratedApiBaseline
            .CreateShapeSummary(After)
            .ShouldBe(GeneratedApiBaseline.ShapeSummaryHeader + "\nMEMBERS=2 PARAMETERS=1\n");
    }

    [Fact]
    public void ParameterMetricCountsSyntaxParametersRatherThanCommasInGenericTypes()
    {
        const string GenericParameter = """
            namespace N;

            public static class Reader
            {
                public static void Read(System.Collections.Generic.Dictionary<string, int> values) { }
            }
            """;

        GeneratedApiBaseline.CountParameterSlots(GenericParameter).ShouldBe(1);
    }

    [Fact]
    public void PartialTypeSplitAcrossTwoDeclarationsYieldsASingleTypeEntry()
    {
        const string Partials = """
            namespace N;

            public static partial class T
            {
                public static void A() { }
            }

            public static partial class T
            {
                public static void B() { }
            }
            """;

        string baseline = GeneratedApiBaseline.Create(Partials);

        GeneratedApiBaseline.CountMembers(baseline).ShouldBe(3);
        baseline
            .Split('\n')
            .Count(l => string.Equals(l, "N.T", StringComparison.Ordinal))
            .ShouldBe(1);
    }

    [Fact]
    public void EnumMembersCarryTheirValueAndImplicitOrdinalsAreResolved()
    {
        const string Enums = """
            namespace N;

            public enum E
            {
                Zero,
                One,
                Ten = 10,
                Eleven,
            }
            """;

        string baseline = GeneratedApiBaseline.Create(Enums);

        baseline.ShouldContain("N.E.Zero = 0 -> N.E\n", Case.Sensitive);
        baseline.ShouldContain("N.E.One = 1 -> N.E\n", Case.Sensitive);
        baseline.ShouldContain("N.E.Ten = 10 -> N.E\n", Case.Sensitive);
        baseline.ShouldContain("N.E.Eleven = 11 -> N.E\n", Case.Sensitive);
    }
}
