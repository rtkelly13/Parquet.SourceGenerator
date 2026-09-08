using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Parquet.SourceGenerator.Tools.Regression;

/// <summary>
/// Runs a <see cref="RegressionPlan"/> tier and produces a <see cref="RunManifest"/>.
/// </summary>
/// <remarks>
/// The executor never decides what to run — that is the plan's job — and never fails a run
/// implicitly. Three things fail a run: a step exiting non-zero, a prerequisite being absent, and
/// the fixture corpus changing on disk. A skipped step is recorded as a skip and, unless the
/// caller explicitly passed <c>--allow-missing-tools</c>, is also a failure. Nothing in here
/// swallows an error to keep a job green.
/// </remarks>
public sealed class RegressionExecutor
{
    private readonly IProcessRunner _runner;
    private readonly TextWriter _output;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="RegressionExecutor"/> class.
    /// </summary>
    /// <param name="runner">Process runner used for every child command.</param>
    /// <param name="output">Console output sink.</param>
    /// <param name="clock">Clock, injected so budget behaviour is testable.</param>
    public RegressionExecutor(
        IProcessRunner runner,
        TextWriter output,
        Func<DateTimeOffset>? clock = null
    )
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Executes a tier.
    /// </summary>
    /// <param name="options">Parsed command line.</param>
    /// <returns>The run manifest, already written to the artifact directory.</returns>
    public RunManifest Execute(RegressionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        DateTimeOffset started = _clock();
        IReadOnlyList<RegressionStep> steps = RegressionPlan.Steps(options.Mode, options.Context);
        IReadOnlyList<ToolRequirement> requirements = RegressionPlan.RequiredTools(
            options.Mode,
            options.Context
        );

        Directory.CreateDirectory(options.ArtifactRoot);
        string logDirectory = Path.Combine(options.ArtifactRoot, "logs");
        Directory.CreateDirectory(logDirectory);

        WriteHeader(options, steps);

        // Prerequisites are probed before any step runs, so a missing PyArrow is reported as a
        // named prerequisite rather than as a mysterious failure forty minutes into a full run.
        Dictionary<string, ToolProbeResult> probes = requirements
            .Select(requirement => Probe(requirement, options))
            .ToDictionary(probe => probe.Id, StringComparer.Ordinal);
        foreach (ToolProbeResult probe in probes.Values)
        {
            _output.WriteLine(
                probe.Available
                    ? $"  [tool] {probe.Id, -8} {probe.Version}  ({probe.ResolvedPath})"
                    : $"  [tool] {probe.Id, -8} MISSING — {probe.Detail}"
            );
        }

        FixtureSnapshot before = FixtureGuard.Capture(options.RepositoryRoot);
        _output.WriteLine($"  [fixtures] {FixtureGuard.Describe(before)}");
        _output.WriteLine();

        var results = new List<StepResult>();
        bool halted = false;
        bool missingPrerequisite = false;

        foreach (RegressionStep step in steps)
        {
            IReadOnlyDictionary<string, string> environment = Substitute(
                step.Environment,
                options,
                probes
            );
            string executable = Substitute(step.Executable, options, probes);
            IReadOnlyList<string> arguments = step
                .Arguments.Select(argument => Substitute(argument, options, probes))
                .ToList();
            string commandLine = CommandFormatting.Format(executable, arguments);
            IReadOnlyList<string> artifacts = step
                .ProducesArtifacts.Select(path => Path.Combine(options.ArtifactRoot, path))
                .ToList();

            string? missing = step.RequiredTools.FirstOrDefault(tool =>
                !probes.TryGetValue(tool, out ToolProbeResult? probe) || !probe.Available
            );

            if (halted)
            {
                results.Add(
                    Skeleton(
                        step,
                        StepOutcome.NotRun,
                        commandLine,
                        environment,
                        artifacts,
                        "An earlier step failed."
                    )
                );
                continue;
            }

            if (missing is not null)
            {
                missingPrerequisite = true;
                StepOutcome outcome = options.AllowMissingTools
                    ? StepOutcome.SkippedMissingTool
                    : StepOutcome.Failed;
                string detail =
                    $"Prerequisite '{missing}' is unavailable: {probes[missing].Detail}";
                _output.WriteLine($"  [skip] {step.Id}: {detail}");
                results.Add(Skeleton(step, outcome, commandLine, environment, artifacts, detail));
                halted = outcome == StepOutcome.Failed && !options.KeepGoing;
                continue;
            }

            if (options.DryRun)
            {
                results.Add(
                    Skeleton(
                        step,
                        StepOutcome.NotRun,
                        commandLine,
                        environment,
                        artifacts,
                        "Dry run."
                    )
                );
                _output.WriteLine($"  [plan] {step.Id, -24} {commandLine}");
                continue;
            }

            _output.WriteLine($"  [run ] {step.Id, -24} {commandLine}");
            DateTimeOffset stepStarted = _clock();
            ProcessResult result = _runner.Run(
                executable,
                arguments,
                options.RepositoryRoot,
                environment
            );
            double elapsed = (_clock() - stepStarted).TotalSeconds;

            string logPath = Path.Combine(logDirectory, step.Id + ".log");
            File.WriteAllText(
                logPath,
                $"$ {commandLine}{System.Environment.NewLine}{System.Environment.NewLine}{result.Output}"
            );

            StepOutcome stepOutcome =
                result.ExitCode == 0 ? StepOutcome.Passed : StepOutcome.Failed;
            results.Add(
                new StepResult(
                    step.Id,
                    step.Title,
                    step.Kind,
                    stepOutcome,
                    result.ExitCode,
                    elapsed,
                    commandLine,
                    environment,
                    logPath,
                    artifacts,
                    stepOutcome == StepOutcome.Passed
                        ? null
                        : $"Exited {result.ExitCode.ToString(CultureInfo.InvariantCulture)}."
                )
            );

            _output.WriteLine(
                $"  [{(stepOutcome == StepOutcome.Passed ? "pass" : "FAIL")}] {step.Id, -24} {elapsed.ToString("F1", CultureInfo.InvariantCulture)}s"
            );

            if (stepOutcome == StepOutcome.Failed && !options.KeepGoing)
            {
                halted = true;
            }
        }

        FixtureSnapshot after = FixtureGuard.Capture(options.RepositoryRoot);
        IReadOnlyList<FixtureDifference> differences = FixtureGuard.Diff(before, after);

        DateTimeOffset completed = _clock();
        double duration = (completed - started).TotalSeconds;
        int budget = RegressionPlan.BudgetSeconds(options.Mode);

        (string outcomeText, int exitCode) = Verdict(
            options,
            results,
            differences,
            missingPrerequisite,
            duration,
            budget
        );

        var manifest = new RunManifest(
            options.RunId,
            options.Mode,
            options.DryRun,
            started,
            completed,
            duration,
            budget,
            options.EnforceBudget,
            options.RepositoryRoot,
            options.ArtifactRoot,
            $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription} / {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}",
            probes.Values.ToList(),
            results,
            FixtureGuard.FixtureRoot,
            before.Count,
            before.Digest,
            after.Digest,
            differences,
            RegressionPlan.Pending(options.Mode),
            outcomeText,
            exitCode
        );

        File.WriteAllText(
            Path.Combine(options.ArtifactRoot, "run-manifest.json"),
            RunManifestWriter.ToJson(manifest)
        );
        File.WriteAllText(
            Path.Combine(options.ArtifactRoot, "summary.md"),
            RunManifestWriter.ToMarkdown(manifest)
        );

        WriteFooter(manifest);
        return manifest;
    }

    private static (string Outcome, int ExitCode) Verdict(
        RegressionOptions options,
        IReadOnlyList<StepResult> results,
        IReadOnlyList<FixtureDifference> differences,
        bool missingPrerequisite,
        double duration,
        int budget
    )
    {
        // Fixture mutation outranks everything: a run whose steps all passed but which rewrote the
        // committed corpus has invalidated its own evidence.
        if (differences.Count > 0)
        {
            return ("fixtures-mutated", RegressionExitCode.FixtureMutated);
        }

        if (results.Any(result => result.Outcome == StepOutcome.Failed))
        {
            return (
                missingPrerequisite && !options.AllowMissingTools
                    ? "missing-prerequisite"
                    : "failed",
                missingPrerequisite && !options.AllowMissingTools
                    ? RegressionExitCode.MissingPrerequisite
                    : RegressionExitCode.StepFailed
            );
        }

        if (options.DryRun)
        {
            return ("planned", RegressionExitCode.Success);
        }

        if (options.EnforceBudget && duration > budget)
        {
            return ("budget-exceeded", RegressionExitCode.BudgetExceeded);
        }

        return (
            results.Any(result => result.Outcome == StepOutcome.SkippedMissingTool)
                ? "passed-with-skips"
                : "passed",
            RegressionExitCode.Success
        );
    }

    private static StepResult Skeleton(
        RegressionStep step,
        StepOutcome outcome,
        string commandLine,
        IReadOnlyDictionary<string, string> environment,
        IReadOnlyList<string> artifacts,
        string detail
    ) =>
        new(
            step.Id,
            step.Title,
            step.Kind,
            outcome,
            null,
            0,
            commandLine,
            environment,
            null,
            artifacts,
            detail
        );

    private ToolProbeResult Probe(ToolRequirement requirement, RegressionOptions options)
    {
        string? overridden = requirement.EnvironmentOverride is null
            ? null
            : System.Environment.GetEnvironmentVariable(requirement.EnvironmentOverride);
        string executable = string.IsNullOrWhiteSpace(overridden)
            ? requirement.Executable
            : overridden;

        ProcessResult result = _runner.Run(
            executable,
            requirement.VersionArguments,
            options.RepositoryRoot,
            new Dictionary<string, string>(StringComparer.Ordinal)
        );

        if (result.ExitCode != 0)
        {
            return new ToolProbeResult(
                requirement.Id,
                false,
                executable,
                null,
                $"{requirement.Purpose} Probe exited {result.ExitCode.ToString(CultureInfo.InvariantCulture)}: {FirstLine(result.Output)}"
            );
        }

        return new ToolProbeResult(
            requirement.Id,
            true,
            executable,
            FirstLine(result.Output),
            null
        );
    }

    private static string FirstLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        ReadOnlySpan<char> span = text.AsSpan();
        int index = span.IndexOf('\n');
        if (index >= 0)
        {
            span = span[..index];
        }

        return span.Trim().ToString();
    }

    private static Dictionary<string, string> Substitute(
        IReadOnlyDictionary<string, string> environment,
        RegressionOptions options,
        IReadOnlyDictionary<string, ToolProbeResult> probes
    )
    {
        var substituted = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in environment)
        {
            substituted[entry.Key] = Substitute(entry.Value, options, probes);
        }

        return substituted;
    }

    private static string Substitute(
        string value,
        RegressionOptions options,
        IReadOnlyDictionary<string, ToolProbeResult> probes
    )
    {
        string result = value.Replace(
            "{artifacts}",
            options.ArtifactRoot.Replace('\\', '/'),
            StringComparison.Ordinal
        );
        foreach (KeyValuePair<string, ToolProbeResult> probe in probes)
        {
            result = result.Replace(
                "{tool:" + probe.Key + "}",
                probe.Value.ResolvedPath,
                StringComparison.Ordinal
            );
        }

        return result;
    }

    private void WriteHeader(RegressionOptions options, IReadOnlyList<RegressionStep> steps)
    {
        _output.WriteLine(
            $"regression {options.Mode.ToString().ToLowerInvariant()} — {steps.Count.ToString(CultureInfo.InvariantCulture)} steps, budget {RegressionPlan.BudgetSeconds(options.Mode).ToString(CultureInfo.InvariantCulture)}s{(options.EnforceBudget ? " (enforced)" : "")}"
        );
        _output.WriteLine($"  repository : {options.RepositoryRoot}");
        _output.WriteLine($"  artifacts  : {options.ArtifactRoot}");
        foreach (PendingCoverage pending in RegressionPlan.Pending(options.Mode))
        {
            _output.WriteLine(
                $"  [pending] {pending.Id}: {pending.Title} (tracked by {pending.TrackingIssue})"
            );
        }
    }

    private void WriteFooter(RunManifest manifest)
    {
        _output.WriteLine();
        _output.WriteLine(
            $"regression {manifest.Mode.ToString().ToLowerInvariant()} → {manifest.Outcome} in {manifest.DurationSeconds.ToString("F1", CultureInfo.InvariantCulture)}s (exit {manifest.ExitCode.ToString(CultureInfo.InvariantCulture)})"
        );
        _output.WriteLine(
            $"  manifest : {Path.Combine(manifest.ArtifactRoot, "run-manifest.json")}"
        );
        _output.WriteLine($"  summary  : {Path.Combine(manifest.ArtifactRoot, "summary.md")}");
        _output.WriteLine($"  logs     : {Path.Combine(manifest.ArtifactRoot, "logs")}");

        foreach (FixtureDifference difference in manifest.FixtureDifferences)
        {
            _output.WriteLine(
                $"  [FIXTURE MUTATED] {difference.Path} ({difference.Change}) — checked-in fixtures must never change during a run."
            );
        }

        foreach (StepResult failure in manifest.Steps.Where(s => s.Outcome == StepOutcome.Failed))
        {
            _output.WriteLine();
            _output.WriteLine($"  FAILED {failure.Id}: {failure.Detail}");
            foreach (KeyValuePair<string, string> variable in failure.Environment)
            {
                _output.WriteLine($"    export {variable.Key}='{variable.Value}'");
            }

            _output.WriteLine($"    {failure.CommandLine}");
            if (failure.LogPath is not null)
            {
                _output.WriteLine($"    log: {failure.LogPath}");
            }
        }
    }
}
