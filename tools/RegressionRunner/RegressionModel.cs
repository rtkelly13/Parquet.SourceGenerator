using System;
using System.Collections.Generic;

namespace Parquet.SourceGenerator.Tools.Regression;

/// <summary>
/// The three regression tiers exposed by the <c>/regression</c> command.
/// </summary>
/// <remarks>
/// The tiers are strictly nested: every <see cref="Quick"/> step also runs in <see cref="Full"/>,
/// and every <see cref="Full"/> step also runs in <see cref="Deep"/>. Nesting is what makes the
/// promise "deep is a superset of full" testable instead of aspirational — a step is tagged with
/// the cheapest tier it belongs to and the plan filter does the rest.
/// </remarks>
public enum RegressionMode
{
    /// <summary>Deterministic generated round trips plus checked-in fixture reads. No external tools.</summary>
    Quick = 0,

    /// <summary>Quick plus pinned external-tool, conformance, version-matrix and fixture-integrity checks.</summary>
    Full = 1,

    /// <summary>Full plus broad property seeds, corruption coverage, large datasets and expensive diagnostics.</summary>
    Deep = 2,
}

/// <summary>
/// What a step is for. Purely descriptive: it groups steps in the console output and the run
/// manifest so a failure can be attributed to a category without reading the command line.
/// </summary>
public enum RegressionStepKind
{
    /// <summary>Compiles the solution or restores prerequisites.</summary>
    Build,

    /// <summary>Runs a slice of the xUnit suite.</summary>
    Test,

    /// <summary>Produces datasets or interop inputs into the run's artifact directory.</summary>
    Generate,

    /// <summary>Cross-checks output with an external engine.</summary>
    Interop,

    /// <summary>Expensive analysis that is not a pass/fail unit test in the usual sense.</summary>
    Diagnostic,
}

/// <summary>
/// An external executable a step depends on, together with how to discover its exact version.
/// </summary>
/// <param name="Id">Stable identifier referenced by <see cref="RegressionStep.RequiredTools"/>.</param>
/// <param name="Executable">Default command name, used when <paramref name="EnvironmentOverride"/> is unset.</param>
/// <param name="VersionArguments">Arguments that make the tool print its version and exit 0.</param>
/// <param name="EnvironmentOverride">Environment variable holding an explicit path to the tool, if any.</param>
/// <param name="Purpose">Human-readable reason the suite needs it — printed when it is missing.</param>
public sealed record ToolRequirement(
    string Id,
    string Executable,
    IReadOnlyList<string> VersionArguments,
    string? EnvironmentOverride,
    string Purpose
);

/// <summary>
/// The outcome of probing one <see cref="ToolRequirement"/>.
/// </summary>
/// <param name="Id">The requirement's identifier.</param>
/// <param name="Available">Whether the executable was found and reported a version.</param>
/// <param name="ResolvedPath">The command actually invoked (after any environment override).</param>
/// <param name="Version">The exact version string reported by the tool, or null when unavailable.</param>
/// <param name="Detail">Diagnostic text for a failed probe.</param>
public sealed record ToolProbeResult(
    string Id,
    bool Available,
    string ResolvedPath,
    string? Version,
    string? Detail
);

/// <summary>
/// One executable unit of the regression suite.
/// </summary>
/// <param name="Id">Stable identifier; also the log file name.</param>
/// <param name="Title">One-line description shown in the console and manifest.</param>
/// <param name="MinimumMode">The cheapest tier this step belongs to.</param>
/// <param name="Kind">Descriptive grouping.</param>
/// <param name="Executable">Program to run.</param>
/// <param name="Arguments">Arguments, already split; never shell-interpreted.</param>
/// <param name="Environment">
/// Extra environment variables. Values containing <c>{artifacts}</c> are rewritten to the run's
/// artifact directory, which is how generated data is kept out of the checked-in fixture tree.
/// </param>
/// <param name="RequiredTools">Identifiers of <see cref="ToolRequirement"/>s that must be present.</param>
/// <param name="ProducesArtifacts">Artifact-relative paths this step writes, recorded in the manifest.</param>
public sealed record RegressionStep(
    string Id,
    string Title,
    RegressionMode MinimumMode,
    RegressionStepKind Kind,
    string Executable,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyList<string> RequiredTools,
    IReadOnlyList<string> ProducesArtifacts
)
{
    /// <summary>
    /// Gets the exact command line to re-run this step by hand.
    /// </summary>
    public string CommandLine => CommandFormatting.Format(Executable, Arguments);
}

/// <summary>
/// Coverage the suite has a slot for but cannot execute yet, because the tests it would run do not
/// exist on <c>main</c>.
/// </summary>
/// <remarks>
/// Declaring the gap is the point. A mode that silently runs four of its six advertised checks is
/// the same false green as a <c>continue-on-error</c> job, so pending coverage is printed on every
/// run, written to the manifest, and counted in the summary.
/// </remarks>
/// <param name="Id">Stable identifier.</param>
/// <param name="Title">What the missing coverage would do.</param>
/// <param name="MinimumMode">The tier it will join once it lands.</param>
/// <param name="TrackingIssue">The GitHub issue that will deliver it.</param>
public sealed record PendingCoverage(
    string Id,
    string Title,
    RegressionMode MinimumMode,
    string TrackingIssue
);

/// <summary>
/// Terminal state of a single step.
/// </summary>
public enum StepOutcome
{
    /// <summary>Exited zero.</summary>
    Passed,

    /// <summary>Exited non-zero.</summary>
    Failed,

    /// <summary>Not started because an earlier step failed.</summary>
    NotRun,

    /// <summary>Not started because a prerequisite tool was absent and missing tools were allowed.</summary>
    SkippedMissingTool,
}

/// <summary>
/// The record of one executed step.
/// </summary>
/// <param name="Id">The step identifier.</param>
/// <param name="Title">The step title.</param>
/// <param name="Kind">The step kind.</param>
/// <param name="Outcome">How it ended.</param>
/// <param name="ExitCode">Process exit code, or null when the step never started.</param>
/// <param name="DurationSeconds">Wall-clock duration.</param>
/// <param name="CommandLine">The exact command that was run — the reproduction command.</param>
/// <param name="Environment">The environment overrides applied, after artifact-path substitution.</param>
/// <param name="LogPath">Path to the captured stdout/stderr log.</param>
/// <param name="Artifacts">Absolute paths this step was declared to produce.</param>
/// <param name="Detail">Extra explanation, such as which tool was missing.</param>
public sealed record StepResult(
    string Id,
    string Title,
    RegressionStepKind Kind,
    StepOutcome Outcome,
    int? ExitCode,
    double DurationSeconds,
    string CommandLine,
    IReadOnlyDictionary<string, string> Environment,
    string? LogPath,
    IReadOnlyList<string> Artifacts,
    string? Detail
);

/// <summary>
/// Exit codes returned by the runner. Distinct codes matter: a missing prerequisite and a genuine
/// compatibility failure look identical in CI otherwise.
/// </summary>
public static class RegressionExitCode
{
    /// <summary>Everything the mode promised ran and passed.</summary>
    public const int Success = 0;

    /// <summary>At least one step failed.</summary>
    public const int StepFailed = 1;

    /// <summary>A required external tool was missing.</summary>
    public const int MissingPrerequisite = 2;

    /// <summary>The mode exceeded its enforced time budget.</summary>
    public const int BudgetExceeded = 3;

    /// <summary>A checked-in fixture changed on disk during the run.</summary>
    public const int FixtureMutated = 4;

    /// <summary>The command line itself was wrong.</summary>
    public const int UsageError = 64;
}

/// <summary>
/// Renders an executable plus arguments as a copy-pasteable command line.
/// </summary>
public static class CommandFormatting
{
    /// <summary>
    /// Formats a command, quoting only the arguments that need it.
    /// </summary>
    /// <param name="executable">Program name or path.</param>
    /// <param name="arguments">Arguments to append.</param>
    /// <returns>A single-line command string.</returns>
    public static string Format(string executable, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var builder = new System.Text.StringBuilder(Quote(executable));
        for (int i = 0; i < arguments.Count; i++)
        {
            builder.Append(' ').Append(Quote(arguments[i]));
        }

        return builder.ToString();
    }

    private static string Quote(string value)
    {
        if (value.Length == 0)
        {
            return "\"\"";
        }

        bool needsQuotes =
            value.Contains(' ', StringComparison.Ordinal)
            || value.Contains('"', StringComparison.Ordinal)
            || value.Contains('&', StringComparison.Ordinal)
            || value.Contains('!', StringComparison.Ordinal);

        return needsQuotes
            ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : value;
    }
}
