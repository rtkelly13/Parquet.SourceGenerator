using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Parquet.SourceGenerator.Tools.Regression;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

/// <summary>
/// A scripted <see cref="IProcessRunner"/>. Every command is recorded and the exit code is looked
/// up by a substring of the command line, which lets a test make one specific step fail without
/// running anything.
/// </summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Dictionary<string, int> _failures = new(StringComparer.Ordinal);

    public List<(
        string CommandLine,
        IReadOnlyDictionary<string, string> Environment
    )> Invocations { get; } = new();

    public HashSet<string> MissingExecutables { get; } = new(StringComparer.Ordinal);

    public void FailWhenCommandContains(string fragment, int exitCode = 1) =>
        _failures[fragment] = exitCode;

    public ProcessResult Run(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment
    )
    {
        string commandLine = CommandFormatting.Format(executable, arguments);
        Invocations.Add((commandLine, environment));

        if (MissingExecutables.Contains(executable))
        {
            return new ProcessResult(127, $"{executable}: command not found");
        }

        foreach (KeyValuePair<string, int> failure in _failures)
        {
            if (commandLine.Contains(failure.Key, StringComparison.Ordinal))
            {
                return new ProcessResult(failure.Value, "boom");
            }
        }

        return new ProcessResult(0, $"ok: {commandLine}");
    }
}

/// <summary>
/// Tests for the <c>/regression</c> execution surface: tier composition, prerequisite gating,
/// artifact routing, the fixture guard and the run manifest.
/// </summary>
public sealed class RegressionRunnerTests : IDisposable
{
    private static readonly string[] DotnetOnly = { "dotnet" };
    private static readonly string[] UnknownModeArgs = { "everything" };
    private static readonly string[] MissingValueArgs = { "full", "--artifacts" };
    private static readonly string[] DeepScaleArgs =
    {
        "deep",
        "--seeds",
        "64",
        "--large-rows",
        "1234",
        "--rid",
        "osx-arm64",
    };

    private static readonly PlanContext Context = new(
        "linux-x64",
        PropertySeeds: 7,
        LargeRowCount: 11
    );

    private readonly string _repositoryRoot;
    private readonly string _artifactRoot;

    public RegressionRunnerTests()
    {
        _repositoryRoot = Path.Combine(
            Path.GetTempPath(),
            "parquet-regression-tests",
            Guid.NewGuid().ToString("N")
        );
        _artifactRoot = Path.Combine(_repositoryRoot, "temp", "regression", "run");
        Directory.CreateDirectory(Path.Combine(_repositoryRoot, "test", "data", "v1"));
        System.IO.File.WriteAllText(
            Path.Combine(_repositoryRoot, "test", "data", "fixture-manifest.json"),
            "{\"schemaVersion\":1,\"fixtures\":[]}"
        );
        System.IO.File.WriteAllBytes(
            Path.Combine(_repositoryRoot, "test", "data", "v1", "fixture.parquet"),
            new byte[] { 0x50, 0x41, 0x52, 0x31 }
        );
    }

    public void Dispose()
    {
        if (Directory.Exists(_repositoryRoot))
        {
            Directory.Delete(_repositoryRoot, recursive: true);
        }
    }

    // ---- plan composition --------------------------------------------------------------------

    [Theory]
    [InlineData(null, RegressionMode.Quick)]
    [InlineData("", RegressionMode.Quick)]
    [InlineData("quick", RegressionMode.Quick)]
    [InlineData("QUICK", RegressionMode.Quick)]
    [InlineData(" full ", RegressionMode.Full)]
    [InlineData("deep", RegressionMode.Deep)]
    public void ModeParsingAcceptsTheDocumentedTokens(string? token, RegressionMode expected)
    {
        Assert.True(RegressionPlan.TryParseMode(token, out RegressionMode mode));
        Assert.Equal(expected, mode);
    }

    [Fact]
    public void ModeParsingRejectsUnknownTokens()
    {
        Assert.False(RegressionPlan.TryParseMode("everything", out RegressionMode mode));
        Assert.Equal(RegressionMode.Quick, mode);
    }

    [Fact]
    public void TiersAreStrictlyNested()
    {
        string[] quick = Ids(RegressionMode.Quick);
        string[] full = Ids(RegressionMode.Full);
        string[] deep = Ids(RegressionMode.Deep);

        Assert.NotEmpty(quick);
        Assert.True(quick.Length < full.Length, "full must add steps to quick");
        Assert.True(full.Length < deep.Length, "deep must add steps to full");
        Assert.Equal(quick, full.Take(quick.Length).ToArray());
        Assert.Equal(full, deep.Take(full.Length).ToArray());
    }

    [Fact]
    public void StepIdentifiersAreUniqueAcrossThePlan()
    {
        string[] ids = Ids(RegressionMode.Deep);
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void QuickTierNeedsNoExternalEngine()
    {
        IReadOnlyList<RegressionStep> steps = RegressionPlan.Steps(RegressionMode.Quick, Context);
        Assert.All(steps, step => Assert.Empty(step.RequiredTools));

        // The SDK is still probed, because "dotnet: command not found" should be reported as a
        // prerequisite failure rather than as fifteen mysterious step failures.
        Assert.Equal(
            DotnetOnly,
            RegressionPlan.RequiredTools(RegressionMode.Quick, Context).Select(tool => tool.Id)
        );
    }

    [Fact]
    public void FullTierRequiresThePinnedExternalEngines()
    {
        string[] tools = RegressionPlan
            .RequiredTools(RegressionMode.Full, Context)
            .Select(tool => tool.Id)
            .ToArray();
        Assert.Contains("uv", tools, StringComparer.Ordinal);
        Assert.Contains("duckdb", tools, StringComparer.Ordinal);
    }

    [Fact]
    public void DeepTierCoversPropertyCorruptionAndLargeDatasets()
    {
        IReadOnlyList<RegressionStep> deep = RegressionPlan.Steps(RegressionMode.Deep, Context);
        string joined = string.Join(" ", deep.Select(step => step.CommandLine));

        Assert.Contains("Category=Property", joined, StringComparison.Ordinal);
        Assert.Contains("Category=Corruption", joined, StringComparison.Ordinal);
        Assert.Contains("Category=LargeDataset", joined, StringComparison.Ordinal);

        RegressionStep property = deep.Single(step => step.Id == "property-seeds");
        Assert.Equal("7", property.Environment["PARQUET_REGRESSION_PROPERTY_SEEDS"]);
        Assert.Equal("11", property.Environment["PARQUET_REGRESSION_LARGE_ROWS"]);
    }

    [Fact]
    public void DefaultTestFilterExcludesEveryDedicatedCategory()
    {
        string filter = RegressionPlan.DefaultTestFilter;
        foreach (string category in RegressionPlan.SpecialCategories)
        {
            Assert.Contains($"Category!={category}", filter, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void QuickTierDoesNotRunTheDeepSuite()
    {
        string joined = string.Join(
            " ",
            RegressionPlan.Steps(RegressionMode.Quick, Context).Select(step => step.CommandLine)
        );

        Assert.DoesNotContain("Category=Property", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("Category=Corruption", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("Category=LargeDataset", joined, StringComparison.Ordinal);
    }

    [Fact]
    public void BudgetsIncreaseWithTierAndQuickStaysADevelopmentLoopBudget()
    {
        Assert.True(RegressionPlan.BudgetSeconds(RegressionMode.Quick) <= 300);
        Assert.True(
            RegressionPlan.BudgetSeconds(RegressionMode.Quick)
                < RegressionPlan.BudgetSeconds(RegressionMode.Full)
        );
        Assert.True(
            RegressionPlan.BudgetSeconds(RegressionMode.Full)
                < RegressionPlan.BudgetSeconds(RegressionMode.Deep)
        );
    }

    [Fact]
    public void PendingCoverageNamesTheIssueThatWillDeliverIt()
    {
        Assert.NotEmpty(RegressionPlan.Pending(RegressionMode.Full));
        Assert.All(
            RegressionPlan.AllPending,
            entry => Assert.StartsWith("#", entry.TrackingIssue, StringComparison.Ordinal)
        );
        Assert.True(
            RegressionPlan.Pending(RegressionMode.Deep).Count
                >= RegressionPlan.Pending(RegressionMode.Full).Count
        );
    }

    // ---- command line ------------------------------------------------------------------------

    [Fact]
    public void CommandLineDefaultsToQuickWithAModeStampedArtifactDirectory()
    {
        Assert.True(
            RegressionCommandLine.TryParse(
                Array.Empty<string>(),
                _repositoryRoot,
                new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
                out RegressionOptions? options,
                out string? error
            )
        );
        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Equal(RegressionMode.Quick, options!.Mode);
        Assert.Equal("quick-20260102-030405", options.RunId);
        Assert.Contains(
            Path.Combine("temp", "regression", "quick-20260102-030405"),
            options.ArtifactRoot,
            StringComparison.Ordinal
        );
        Assert.False(options.DryRun);
    }

    [Fact]
    public void CommandLineRejectsAnUnknownMode()
    {
        Assert.False(
            RegressionCommandLine.TryParse(
                UnknownModeArgs,
                _repositoryRoot,
                DateTimeOffset.UnixEpoch,
                out RegressionOptions? options,
                out string? error
            )
        );
        Assert.Null(options);
        Assert.Contains("everything", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandLineRejectsAnOptionWithoutItsValue()
    {
        Assert.False(
            RegressionCommandLine.TryParse(
                MissingValueArgs,
                _repositoryRoot,
                DateTimeOffset.UnixEpoch,
                out _,
                out string? error
            )
        );
        Assert.Contains("--artifacts", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandLineParsesTheDeepScaleKnobs()
    {
        Assert.True(
            RegressionCommandLine.TryParse(
                DeepScaleArgs,
                _repositoryRoot,
                DateTimeOffset.UnixEpoch,
                out RegressionOptions? options,
                out _
            )
        );
        Assert.Equal(RegressionMode.Deep, options!.Mode);
        Assert.Equal(64, options.Context.PropertySeeds);
        Assert.Equal(1234, options.Context.LargeRowCount);
        Assert.Equal("osx-arm64", options.Context.RuntimeIdentifier);
    }

    // ---- execution ---------------------------------------------------------------------------

    [Fact]
    public void QuickRunPassesAndWritesAManifestAndSummary()
    {
        var runner = new FakeProcessRunner();
        RunManifest manifest = Execute(runner, RegressionMode.Quick);

        Assert.Equal("passed", manifest.Outcome);
        Assert.Equal(RegressionExitCode.Success, manifest.ExitCode);
        Assert.All(manifest.Steps, step => Assert.Equal(StepOutcome.Passed, step.Outcome));
        Assert.True(System.IO.File.Exists(Path.Combine(_artifactRoot, "run-manifest.json")));
        Assert.True(System.IO.File.Exists(Path.Combine(_artifactRoot, "summary.md")));

        RunManifest reloaded = RunManifestWriter.FromJson(
            System.IO.File.ReadAllText(Path.Combine(_artifactRoot, "run-manifest.json"))
        );
        Assert.Equal(manifest.RunId, reloaded.RunId);
        Assert.Equal(manifest.Steps.Count, reloaded.Steps.Count);
    }

    [Fact]
    public void EveryStepRecordsItsExactToolVersionsAndCommand()
    {
        var runner = new FakeProcessRunner();
        RunManifest manifest = Execute(runner, RegressionMode.Full);

        Assert.All(manifest.Tools, tool => Assert.True(tool.Available));
        Assert.All(manifest.Tools, tool => Assert.False(string.IsNullOrWhiteSpace(tool.Version)));
        Assert.All(
            manifest.Steps,
            step => Assert.False(string.IsNullOrWhiteSpace(step.CommandLine))
        );
        Assert.All(manifest.Steps, step => Assert.True(System.IO.File.Exists(step.LogPath!)));
        Assert.Contains(manifest.Tools, tool => tool.Id == "uv");
        Assert.Contains(manifest.Tools, tool => tool.Id == "duckdb");
    }

    [Fact]
    public void GeneratedDataIsRoutedIntoTheArtifactDirectoryAndNeverIntoTestData()
    {
        var runner = new FakeProcessRunner();
        RunManifest manifest = Execute(runner, RegressionMode.Deep);

        IEnumerable<string> values = manifest
            .Steps.SelectMany(step => step.Environment.Values)
            .Concat(manifest.Steps.Select(step => step.CommandLine));

        foreach (string value in values)
        {
            if (
                value.Contains("PARQUET_TEST_DATA", StringComparison.Ordinal)
                || value.EndsWith(".parquet", StringComparison.Ordinal)
            )
            {
                Assert.DoesNotContain("test/data", value, StringComparison.Ordinal);
            }
        }

        RegressionStep generation = RegressionPlan
            .Steps(RegressionMode.Full, Context)
            .Single(step => step.Id == "pyarrow-datasets");
        Assert.Equal("{artifacts}/data", generation.Environment["PARQUET_TEST_DATA_OUTPUT_DIR"]);

        StepResult executed = manifest.Steps.Single(step => step.Id == "pyarrow-datasets");
        Assert.StartsWith(
            _artifactRoot.Replace('\\', '/'),
            executed.Environment["PARQUET_TEST_DATA_OUTPUT_DIR"],
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void ToolPlaceholdersResolveToTheProbedExecutable()
    {
        var runner = new FakeProcessRunner();
        RunManifest manifest = Execute(runner, RegressionMode.Full);

        StepResult duckdb = manifest.Steps.Single(step => step.Id == "duckdb-interop");
        Assert.DoesNotContain("{tool:", duckdb.CommandLine, StringComparison.Ordinal);
        Assert.Contains("--duckdb duckdb", duckdb.CommandLine, StringComparison.Ordinal);
    }

    [Fact]
    public void AFailingStepStopsTheRunAndReportsAReproductionCommand()
    {
        var runner = new FakeProcessRunner();
        runner.FailWhenCommandContains("Category=DatasetIntegrity", exitCode: 3);

        RunManifest manifest = Execute(runner, RegressionMode.Quick);

        Assert.Equal(RegressionExitCode.StepFailed, manifest.ExitCode);
        StepResult failed = manifest.Steps.Single(step => step.Id == "fixture-integrity");
        Assert.Equal(StepOutcome.Failed, failed.Outcome);
        Assert.Equal(3, failed.ExitCode);
        Assert.Contains("Category=DatasetIntegrity", failed.CommandLine, StringComparison.Ordinal);
        Assert.All(
            manifest.Steps.SkipWhile(step => step.Id != "fixture-integrity").Skip(1),
            step => Assert.Equal(StepOutcome.NotRun, step.Outcome)
        );

        string summary = System.IO.File.ReadAllText(Path.Combine(_artifactRoot, "summary.md"));
        Assert.Contains("Reproduction commands", summary, StringComparison.Ordinal);
        Assert.Contains(failed.CommandLine, summary, StringComparison.Ordinal);
    }

    [Fact]
    public void KeepGoingRunsEveryStepAndStillFails()
    {
        var runner = new FakeProcessRunner();
        runner.FailWhenCommandContains("Category=DatasetIntegrity");

        RunManifest manifest = Execute(runner, RegressionMode.Quick, keepGoing: true);

        Assert.Equal(RegressionExitCode.StepFailed, manifest.ExitCode);
        Assert.DoesNotContain(manifest.Steps, step => step.Outcome == StepOutcome.NotRun);
    }

    [Fact]
    public void AMissingPrerequisiteFailsTheRunRatherThanSilentlySkipping()
    {
        var runner = new FakeProcessRunner();
        runner.MissingExecutables.Add("uv");

        RunManifest manifest = Execute(runner, RegressionMode.Full);

        Assert.Equal(RegressionExitCode.MissingPrerequisite, manifest.ExitCode);
        Assert.Equal("missing-prerequisite", manifest.Outcome);
        Assert.False(manifest.Tools.Single(tool => tool.Id == "uv").Available);
        Assert.Equal(
            StepOutcome.Failed,
            manifest.Steps.Single(step => step.Id == "pyarrow-datasets").Outcome
        );
    }

    [Fact]
    public void AllowMissingToolsDowngradesToARecordedSkipAndSaysSoInTheOutcome()
    {
        var runner = new FakeProcessRunner();
        runner.MissingExecutables.Add("duckdb");

        RunManifest manifest = Execute(
            runner,
            RegressionMode.Full,
            allowMissingTools: true,
            keepGoing: true
        );

        Assert.Equal(RegressionExitCode.Success, manifest.ExitCode);
        Assert.Equal("passed-with-skips", manifest.Outcome);
        Assert.Equal(
            StepOutcome.SkippedMissingTool,
            manifest.Steps.Single(step => step.Id == "duckdb-interop").Outcome
        );
    }

    [Fact]
    public void DryRunProbesPrerequisitesButExecutesNothing()
    {
        var runner = new FakeProcessRunner();
        RunManifest manifest = Execute(runner, RegressionMode.Deep, dryRun: true);

        Assert.Equal("planned", manifest.Outcome);
        Assert.All(manifest.Steps, step => Assert.Equal(StepOutcome.NotRun, step.Outcome));

        // Only the version probes should have run.
        Assert.All(
            runner.Invocations,
            invocation =>
                Assert.Contains("--version", invocation.CommandLine, StringComparison.Ordinal)
        );
    }

    [Fact]
    public void AMutatedFixtureFailsTheRunEvenWhenEveryStepPassed()
    {
        var runner = new MutatingProcessRunner(
            Path.Combine(_repositoryRoot, "test", "data", "v1", "fixture.parquet")
        );

        RunManifest manifest = Execute(runner, RegressionMode.Quick);

        Assert.Equal(RegressionExitCode.FixtureMutated, manifest.ExitCode);
        Assert.Equal("fixtures-mutated", manifest.Outcome);
        Assert.All(
            manifest.Steps.Where(step => step.Outcome != StepOutcome.NotRun),
            step => Assert.Equal(StepOutcome.Passed, step.Outcome)
        );
        FixtureDifference difference = Assert.Single(manifest.FixtureDifferences);
        Assert.Equal("v1/fixture.parquet", difference.Path);
        Assert.Equal("modified", difference.Change);
    }

    [Fact]
    public void AnUntouchedFixtureTreeReportsIdenticalDigests()
    {
        var runner = new FakeProcessRunner();
        RunManifest manifest = Execute(runner, RegressionMode.Quick);

        Assert.Empty(manifest.FixtureDifferences);
        Assert.Equal(manifest.FixtureDigestBefore, manifest.FixtureDigestAfter);
        Assert.Equal(2, manifest.FixtureFileCount);
        Assert.Equal("test/data", manifest.FixtureRoot);
    }

    [Fact]
    public void AddedAndRemovedFixturesAreBothDetected()
    {
        FixtureSnapshot before = FixtureGuard.Capture(_repositoryRoot);
        System.IO.File.WriteAllText(
            Path.Combine(_repositoryRoot, "test", "data", "v1", "extra.parquet"),
            "new"
        );
        System.IO.File.Delete(
            Path.Combine(_repositoryRoot, "test", "data", "fixture-manifest.json")
        );
        FixtureSnapshot after = FixtureGuard.Capture(_repositoryRoot);

        IReadOnlyList<FixtureDifference> differences = FixtureGuard.Diff(before, after);
        Assert.Equal(2, differences.Count);
        Assert.Contains(
            differences,
            difference => difference.Path == "v1/extra.parquet" && difference.Change == "added"
        );
        Assert.Contains(
            differences,
            difference =>
                difference.Path == "fixture-manifest.json" && difference.Change == "removed"
        );
    }

    [Fact]
    public void AnEnforcedBudgetFailsARunThatOverruns()
    {
        var runner = new FakeProcessRunner();
        DateTimeOffset now = DateTimeOffset.UnixEpoch;

        // Each clock read advances a minute, so a quick run of a dozen steps blows the 5 minute
        // budget without anything actually being slow.
        RunManifest manifest = Execute(
            runner,
            RegressionMode.Quick,
            enforceBudget: true,
            clock: () =>
            {
                now = now.AddMinutes(1);
                return now;
            }
        );

        Assert.Equal(RegressionExitCode.BudgetExceeded, manifest.ExitCode);
        Assert.Equal("budget-exceeded", manifest.Outcome);
        Assert.True(manifest.BudgetEnforced);
    }

    [Fact]
    public void AnAdvisoryBudgetDoesNotFailTheRun()
    {
        var runner = new FakeProcessRunner();
        DateTimeOffset now = DateTimeOffset.UnixEpoch;

        RunManifest manifest = Execute(
            runner,
            RegressionMode.Quick,
            enforceBudget: false,
            clock: () =>
            {
                now = now.AddMinutes(1);
                return now;
            }
        );

        Assert.Equal(RegressionExitCode.Success, manifest.ExitCode);
        Assert.False(manifest.BudgetEnforced);
        Assert.True(manifest.DurationSeconds > RegressionPlan.BudgetSeconds(RegressionMode.Quick));
    }

    [Fact]
    public void TheMarkdownSummaryReportsVersionsFixtureDigestsAndPendingCoverage()
    {
        var runner = new FakeProcessRunner();
        RunManifest manifest = Execute(runner, RegressionMode.Full);
        string markdown = RunManifestWriter.ToMarkdown(manifest);

        Assert.Contains("## Tool versions", markdown, StringComparison.Ordinal);
        Assert.Contains("## Steps", markdown, StringComparison.Ordinal);
        Assert.Contains(manifest.FixtureDigestBefore, markdown, StringComparison.Ordinal);
        Assert.Contains("not yet implemented", markdown, StringComparison.Ordinal);
        Assert.Contains("#167", markdown, StringComparison.Ordinal);
        Assert.Contains(_artifactRoot, markdown, StringComparison.Ordinal);
    }

    private static string[] Ids(RegressionMode mode) =>
        RegressionPlan.Steps(mode, Context).Select(step => step.Id).ToArray();

    private RunManifest Execute(
        IProcessRunner runner,
        RegressionMode mode,
        bool dryRun = false,
        bool allowMissingTools = false,
        bool keepGoing = false,
        bool enforceBudget = false,
        Func<DateTimeOffset>? clock = null
    )
    {
        var options = new RegressionOptions(
            mode,
            _repositoryRoot,
            _artifactRoot,
            "test-run",
            dryRun,
            allowMissingTools,
            keepGoing,
            enforceBudget,
            Context
        );

        using var output = new StringWriter();
        return new RegressionExecutor(runner, output, clock).Execute(options);
    }

    /// <summary>
    /// A runner that rewrites a checked-in fixture while "succeeding" — the exact failure mode the
    /// fixture guard exists to catch.
    /// </summary>
    private sealed class MutatingProcessRunner : IProcessRunner
    {
        private readonly string _fixturePath;
        private bool _mutated;

        public MutatingProcessRunner(string fixturePath) => _fixturePath = fixturePath;

        public ProcessResult Run(
            string executable,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string> environment
        )
        {
            if (!_mutated && arguments.Contains("test"))
            {
                _mutated = true;
                System.IO.File.WriteAllBytes(_fixturePath, new byte[] { 0x00, 0x00 });
            }

            return new ProcessResult(0, "ok");
        }
    }
}
