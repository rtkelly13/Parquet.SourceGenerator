using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Parquet.SourceGenerator.Tools.Regression;

/// <summary>
/// Inputs that make the plan deterministic. Everything that varies by machine is passed in rather
/// than read from the environment inside the plan, so a test can assert the exact command lines.
/// </summary>
/// <param name="RuntimeIdentifier">RID used for the Native AOT diagnostic in deep mode.</param>
/// <param name="PropertySeeds">Seed count handed to the property-based suite in deep mode.</param>
/// <param name="LargeRowCount">Row count handed to the large-dataset suite in deep mode.</param>
public sealed record PlanContext(
    string RuntimeIdentifier,
    int PropertySeeds = 512,
    long LargeRowCount = 500_000
)
{
    /// <summary>
    /// Creates a context for the current machine.
    /// </summary>
    /// <returns>A context whose RID matches the host.</returns>
    public static PlanContext ForHost() =>
        new(System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier);
}

/// <summary>
/// The static definition of what each regression tier runs.
/// </summary>
/// <remarks>
/// This type deliberately contains no I/O. The plan is data; <see cref="RegressionExecutor"/> is
/// the only thing that runs it. That split is what lets the suite's wiring — tier nesting, tool
/// prerequisites, artifact routing, fixture safety — be unit-tested without a PyArrow install.
/// </remarks>
public static class RegressionPlan
{
    /// <summary>The solution file name, typo and all.</summary>
    public const string SolutionFile = "Parquet.SourceGenertor.sln";

    /// <summary>Categories excluded from the fast unit slice because a dedicated step owns them.</summary>
    public static readonly IReadOnlyList<string> SpecialCategories = new[]
    {
        "DatasetIntegrity",
        "ExternalInterop",
        "Property",
        "Corruption",
        "LargeDataset",
    };

    /// <summary>
    /// Gets the xUnit filter that selects the ordinary unit slice: everything that is not owned by
    /// a dedicated step. Required PR checks use this, which is how "PR checks do not run the deep
    /// suite" is enforced in one place instead of in every workflow.
    /// </summary>
    public static string DefaultTestFilter =>
        string.Join("&", SpecialCategories.Select(category => $"Category!={category}"));

    /// <summary>Time budgets per tier, in seconds.</summary>
    /// <param name="mode">The tier.</param>
    /// <returns>The agreed budget for that tier.</returns>
    public static int BudgetSeconds(RegressionMode mode) =>
        mode switch
        {
            RegressionMode.Quick => 300,
            RegressionMode.Full => 2_400,
            RegressionMode.Deep => 10_800,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

    /// <summary>
    /// Parses a mode name. An empty or absent name means <see cref="RegressionMode.Quick"/>, which
    /// is what makes bare <c>/regression</c> the fast path.
    /// </summary>
    /// <param name="value">The user-supplied mode token.</param>
    /// <param name="mode">The parsed mode.</param>
    /// <returns>True when the token was recognised.</returns>
    public static bool TryParseMode(string? value, out RegressionMode mode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            mode = RegressionMode.Quick;
            return true;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "quick":
            case "fast":
                mode = RegressionMode.Quick;
                return true;
            case "full":
                mode = RegressionMode.Full;
                return true;
            case "deep":
                mode = RegressionMode.Deep;
                return true;
            default:
                mode = RegressionMode.Quick;
                return false;
        }
    }

    /// <summary>All external tools any tier can require.</summary>
    public static IReadOnlyList<ToolRequirement> AllTools { get; } =
        new[]
        {
            new ToolRequirement(
                "dotnet",
                "dotnet",
                new[] { "--version" },
                null,
                "Builds the solution and runs every test slice."
            ),
            new ToolRequirement(
                "uv",
                "uv",
                new[] { "--version" },
                "UV_BIN",
                "Runs scripts/generate_test_data.py against pinned PyArrow."
            ),
            new ToolRequirement(
                "duckdb",
                "duckdb",
                new[] { "--version" },
                "DUCKDB_BIN",
                "Reads and writes Parquet for the DuckDB bidirectional interop check."
            ),
        };

    /// <summary>
    /// Builds the ordered step list for a tier.
    /// </summary>
    /// <param name="mode">The tier to plan.</param>
    /// <param name="context">Deterministic plan inputs.</param>
    /// <returns>The steps to run, in order.</returns>
    public static IReadOnlyList<RegressionStep> Steps(RegressionMode mode, PlanContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return AllSteps(context).Where(step => step.MinimumMode <= mode).ToList();
    }

    /// <summary>
    /// Gets the tools a tier actually needs, in declaration order.
    /// </summary>
    /// <param name="mode">The tier.</param>
    /// <param name="context">Deterministic plan inputs.</param>
    /// <returns>The required tool requirements.</returns>
    public static IReadOnlyList<ToolRequirement> RequiredTools(
        RegressionMode mode,
        PlanContext context
    )
    {
        // The .NET SDK is required by every tier and is not tagged on individual steps: probing it
        // unconditionally is what turns "command not found" into a named prerequisite failure.
        HashSet<string> ids = Steps(mode, context)
            .SelectMany(step => step.RequiredTools)
            .Append("dotnet")
            .ToHashSet(StringComparer.Ordinal);
        return AllTools.Where(tool => ids.Contains(tool.Id)).ToList();
    }

    /// <summary>
    /// Gets the advertised coverage that is not implemented yet for a tier.
    /// </summary>
    /// <param name="mode">The tier.</param>
    /// <returns>Pending coverage entries in scope for that tier.</returns>
    public static IReadOnlyList<PendingCoverage> Pending(RegressionMode mode) =>
        AllPending.Where(entry => entry.MinimumMode <= mode).ToList();

    /// <summary>Every declared-but-unimplemented slice, with the issue that will deliver it.</summary>
    public static IReadOnlyList<PendingCoverage> AllPending { get; } =
        new[]
        {
            new PendingCoverage(
                "apache-tooling-conformance",
                "Validate generated files with Apache parquet-tools / parquet-cli.",
                RegressionMode.Full,
                "#167"
            ),
            new PendingCoverage(
                "duckdb-bidirectional-matrix",
                "Widen DuckDB interop past the single smoke file to the full type matrix.",
                RegressionMode.Full,
                "#166"
            ),
            new PendingCoverage(
                "producer-version-and-schema-evolution",
                "Parquet format version, producer-version and schema-evolution matrix.",
                RegressionMode.Full,
                "#168"
            ),
            new PendingCoverage(
                "broad-property-and-corruption-corpus",
                "Full supported-schema property corpus and the generated corrupted-file corpus.",
                RegressionMode.Deep,
                "#169"
            ),
        };

    private static List<RegressionStep> AllSteps(PlanContext context)
    {
        string aotProject =
            "test/Parquet.SourceGenerator.AotTest/Parquet.SourceGenerator.AotTest.csproj";
        string cliProject = "test/Parquet.SourceGenerator.CLI/Parquet.SourceGenerator.CLI.csproj";

        var steps = new List<RegressionStep>
        {
            // ---- quick -------------------------------------------------------------------
            Step(
                "build",
                "Build the solution (Release).",
                RegressionMode.Quick,
                RegressionStepKind.Build,
                "dotnet",
                new[] { "build", SolutionFile, "--configuration", "Release" }
            ),
            Step(
                "fixture-integrity",
                "Hash every checked-in fixture against test/data/fixture-manifest.json.",
                RegressionMode.Quick,
                RegressionStepKind.Test,
                "dotnet",
                TestArgs("Category=DatasetIntegrity")
            ),
            Step(
                "roundtrip",
                "Deterministic generated round trips and checked-in fixture reads.",
                RegressionMode.Quick,
                RegressionStepKind.Test,
                "dotnet",
                TestArgs(DefaultTestFilter)
            ),
            // ---- full --------------------------------------------------------------------
            Step(
                "pyarrow-datasets",
                "Regenerate the PyArrow v1 and v2 corpora into the run's artifact directory.",
                RegressionMode.Full,
                RegressionStepKind.Generate,
                "uv",
                new[] { "run", "scripts/generate_test_data.py" },
                environment: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["PARQUET_TEST_DATA_OUTPUT_DIR"] = "{artifacts}/data",
                },
                requiredTools: new[] { "uv" },
                artifacts: new[] { "data" }
            ),
            Step(
                "parquetnet-datasets",
                "Regenerate the Parquet.Net corpus into the run's artifact directory.",
                RegressionMode.Full,
                RegressionStepKind.Generate,
                "dotnet",
                new[] { "run", "--project", cliProject, "--configuration", "Release" },
                environment: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["PARQUET_TEST_DATA_CSHARP_OUTPUT_DIR"] = "{artifacts}/data_csharp",
                },
                artifacts: new[] { "data_csharp" }
            ),
            Step(
                "interop-input",
                "Emit the generated-by-this-repo Parquet file the external engines will read.",
                RegressionMode.Full,
                RegressionStepKind.Generate,
                "dotnet",
                new[]
                {
                    "run",
                    "--project",
                    cliProject,
                    "--configuration",
                    "Release",
                    "--",
                    "--pyarrow-output",
                    "{artifacts}/generated-csharp.parquet",
                },
                artifacts: new[] { "generated-csharp.parquet" }
            ),
            Step(
                "pyarrow-verify",
                "Read the generated file back with pinned PyArrow and compare values.",
                RegressionMode.Full,
                RegressionStepKind.Interop,
                "uv",
                new[]
                {
                    "run",
                    "scripts/generate_test_data.py",
                    "--verify-generated",
                    "{artifacts}/generated-csharp.parquet",
                },
                requiredTools: new[] { "uv" }
            ),
            Step(
                "duckdb-interop",
                "Query the generated file with DuckDB and write a DuckDB-produced file back.",
                RegressionMode.Full,
                RegressionStepKind.Interop,
                "dotnet",
                new[]
                {
                    "run",
                    "scripts/VerifyDuckDbInterop.cs",
                    "--",
                    "--duckdb",
                    "{tool:duckdb}",
                    "--generated",
                    "{artifacts}/generated-csharp.parquet",
                    "--output",
                    "{artifacts}/generated-duckdb.parquet",
                },
                requiredTools: new[] { "duckdb" },
                artifacts: new[] { "generated-duckdb.parquet" }
            ),
            Step(
                "external-interop-tests",
                "Read the PyArrow- and DuckDB-produced files through the generated readers.",
                RegressionMode.Full,
                RegressionStepKind.Interop,
                "dotnet",
                TestArgs("Category=ExternalInterop"),
                environment: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["PARQUET_PYARROW_INTEROP_INPUT"] =
                        "{artifacts}/data/pyarrow_interop_input.parquet",
                    ["DUCKDB_INTEROP_INPUT"] = "{artifacts}/generated-duckdb.parquet",
                }
            ),
            Step(
                "version-matrix",
                "Re-run the corpus suite against the freshly generated v1/v2 and Parquet.Net data.",
                RegressionMode.Full,
                RegressionStepKind.Test,
                "dotnet",
                TestArgs(DefaultTestFilter),
                environment: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["PARQUET_TEST_DATA_ROOT"] = "{artifacts}/data",
                    ["PARQUET_TEST_DATA_CSHARP_ROOT"] = "{artifacts}/data_csharp",
                }
            ),
            // ---- deep --------------------------------------------------------------------
            Step(
                "property-seeds",
                "Broad seeded property coverage over supported schemas.",
                RegressionMode.Deep,
                RegressionStepKind.Test,
                "dotnet",
                TestArgs("Category=Property"),
                environment: DeepEnvironment(context)
            ),
            Step(
                "corruption",
                "Truncated, garbled and zero-length Parquet inputs must fail cleanly.",
                RegressionMode.Deep,
                RegressionStepKind.Test,
                "dotnet",
                TestArgs("Category=Corruption"),
                environment: DeepEnvironment(context)
            ),
            Step(
                "large-datasets",
                "Multi-row-group large dataset round trips.",
                RegressionMode.Deep,
                RegressionStepKind.Test,
                "dotnet",
                TestArgs("Category=LargeDataset"),
                environment: DeepEnvironment(context)
            ),
            Step(
                "il-diagnostics",
                "Interrogate emitted IL for boxing regressions.",
                RegressionMode.Deep,
                RegressionStepKind.Diagnostic,
                "dotnet",
                new[]
                {
                    "run",
                    "scripts/InterrogateIL.cs",
                    "--assembly",
                    "test/Parquet.SourceGenerator.CLI/bin/Release/net8.0/Parquet.SourceGenerator.CLI.dll",
                    "--check",
                }
            ),
            Step(
                "aot-publish",
                "Publish the AOT round-trip harness with the ILCompiler.",
                RegressionMode.Deep,
                RegressionStepKind.Diagnostic,
                "dotnet",
                new[]
                {
                    "publish",
                    aotProject,
                    "--configuration",
                    "Release",
                    "-r",
                    context.RuntimeIdentifier,
                    "-o",
                    "{artifacts}/aot",
                },
                artifacts: new[] { "aot" }
            ),
            Step(
                "aot-run",
                "Execute the native binary; it throws on any round-trip mismatch.",
                RegressionMode.Deep,
                RegressionStepKind.Diagnostic,
                "{artifacts}/aot/Parquet.SourceGenerator.AotTest",
                Array.Empty<string>()
            ),
        };

        return steps;
    }

    private static Dictionary<string, string> DeepEnvironment(PlanContext context) =>
        new(StringComparer.Ordinal)
        {
            ["PARQUET_REGRESSION_DEEP"] = "1",
            ["PARQUET_REGRESSION_PROPERTY_SEEDS"] = context.PropertySeeds.ToString(
                CultureInfo.InvariantCulture
            ),
            ["PARQUET_REGRESSION_LARGE_ROWS"] = context.LargeRowCount.ToString(
                CultureInfo.InvariantCulture
            ),
        };

    private static string[] TestArgs(string filter) =>
        new[]
        {
            "test",
            SolutionFile,
            "--configuration",
            "Release",
            "--no-build",
            "--filter",
            filter,
            "--verbosity",
            "normal",
        };

    private static RegressionStep Step(
        string id,
        string title,
        RegressionMode minimumMode,
        RegressionStepKind kind,
        string executable,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment = null,
        IReadOnlyList<string>? requiredTools = null,
        IReadOnlyList<string>? artifacts = null
    ) =>
        new(
            id,
            title,
            minimumMode,
            kind,
            executable,
            arguments,
            environment ?? new Dictionary<string, string>(StringComparer.Ordinal),
            requiredTools ?? Array.Empty<string>(),
            artifacts ?? Array.Empty<string>()
        );
}
