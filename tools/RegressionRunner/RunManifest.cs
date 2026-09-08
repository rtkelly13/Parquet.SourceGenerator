using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Parquet.SourceGenerator.Tools.Regression;

/// <summary>
/// The complete, machine-readable record of one regression run.
/// </summary>
/// <remarks>
/// Every acceptance criterion about reporting collapses into this one artifact: exact tool
/// versions, the exact command lines (which double as reproduction commands), the fixture manifest
/// digest, where the retained Parquet files and logs live, and which advertised coverage did not
/// run. It is written whether the run passed or failed.
/// </remarks>
/// <param name="RunId">Directory-safe identifier, also the artifact folder name.</param>
/// <param name="Mode">The tier that was requested.</param>
/// <param name="DryRun">True when the plan was printed rather than executed.</param>
/// <param name="StartedUtc">Run start.</param>
/// <param name="CompletedUtc">Run end.</param>
/// <param name="DurationSeconds">Wall-clock duration.</param>
/// <param name="BudgetSeconds">The tier's agreed budget.</param>
/// <param name="BudgetEnforced">Whether exceeding the budget fails the run.</param>
/// <param name="RepositoryRoot">Repository root the run executed against.</param>
/// <param name="ArtifactRoot">Directory holding logs, manifests and retained Parquet files.</param>
/// <param name="Host">Operating system and .NET description.</param>
/// <param name="Tools">Version probe results for every prerequisite of this tier.</param>
/// <param name="Steps">Per-step outcomes in execution order.</param>
/// <param name="FixtureRoot">The checked-in fixture tree that was guarded.</param>
/// <param name="FixtureFileCount">Number of fixture files hashed.</param>
/// <param name="FixtureDigestBefore">Fixture tree digest before the run.</param>
/// <param name="FixtureDigestAfter">Fixture tree digest after the run.</param>
/// <param name="FixtureDifferences">Any fixture mutation detected. Non-empty fails the run.</param>
/// <param name="PendingCoverage">Advertised coverage this tier cannot run yet.</param>
/// <param name="Outcome">One-word verdict.</param>
/// <param name="ExitCode">Process exit code the runner returned.</param>
public sealed record RunManifest(
    string RunId,
    RegressionMode Mode,
    bool DryRun,
    DateTimeOffset StartedUtc,
    DateTimeOffset CompletedUtc,
    double DurationSeconds,
    int BudgetSeconds,
    bool BudgetEnforced,
    string RepositoryRoot,
    string ArtifactRoot,
    string Host,
    IReadOnlyList<ToolProbeResult> Tools,
    IReadOnlyList<StepResult> Steps,
    string FixtureRoot,
    int FixtureFileCount,
    string FixtureDigestBefore,
    string FixtureDigestAfter,
    IReadOnlyList<FixtureDifference> FixtureDifferences,
    IReadOnlyList<PendingCoverage> PendingCoverage,
    string Outcome,
    int ExitCode
);

/// <summary>
/// Serialization for <see cref="RunManifest"/> and the human-readable summary derived from it.
/// </summary>
public static class RunManifestWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Serializes a manifest to JSON.
    /// </summary>
    /// <param name="manifest">The manifest.</param>
    /// <returns>Indented JSON.</returns>
    public static string ToJson(RunManifest manifest) =>
        JsonSerializer.Serialize(manifest, SerializerOptions);

    /// <summary>
    /// Deserializes a manifest from JSON.
    /// </summary>
    /// <param name="json">Manifest JSON.</param>
    /// <returns>The manifest.</returns>
    public static RunManifest FromJson(string json) =>
        JsonSerializer.Deserialize<RunManifest>(json, SerializerOptions)
        ?? throw new InvalidOperationException("Run manifest JSON deserialized to null.");

    /// <summary>
    /// Renders the Markdown summary written beside the manifest and pasted into CI job summaries.
    /// </summary>
    /// <param name="manifest">The manifest.</param>
    /// <returns>Markdown text.</returns>
    public static string ToMarkdown(RunManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var builder = new StringBuilder();
        builder
            .Append("# Regression run — ")
            .Append(manifest.Mode.ToString().ToLowerInvariant())
            .AppendLine()
            .AppendLine();
        builder
            .Append("**Outcome:** ")
            .Append(manifest.Outcome)
            .Append(" (exit ")
            .Append(manifest.ExitCode.ToString(CultureInfo.InvariantCulture))
            .Append(")  ")
            .AppendLine();
        builder
            .Append("**Duration:** ")
            .Append(manifest.DurationSeconds.ToString("F1", CultureInfo.InvariantCulture))
            .Append("s of a ")
            .Append(manifest.BudgetSeconds.ToString(CultureInfo.InvariantCulture))
            .Append("s budget")
            .Append(manifest.BudgetEnforced ? " (enforced)" : " (advisory)")
            .Append("  ")
            .AppendLine();
        builder.Append("**Artifacts:** `").Append(manifest.ArtifactRoot).Append("`  ").AppendLine();
        builder
            .Append("**Fixture corpus:** ")
            .Append(manifest.FixtureFileCount.ToString(CultureInfo.InvariantCulture))
            .Append(" files, digest `")
            .Append(manifest.FixtureDigestBefore)
            .Append("` → `")
            .Append(manifest.FixtureDigestAfter)
            .Append('`')
            .AppendLine()
            .AppendLine();

        builder.AppendLine("## Tool versions").AppendLine();
        builder.AppendLine("| tool | resolved | version |");
        builder.AppendLine("| --- | --- | --- |");
        foreach (ToolProbeResult tool in manifest.Tools)
        {
            builder
                .Append("| ")
                .Append(tool.Id)
                .Append(" | `")
                .Append(tool.ResolvedPath)
                .Append("` | ")
                .Append(tool.Available ? tool.Version : "**missing** — " + tool.Detail)
                .AppendLine(" |");
        }

        builder.AppendLine().AppendLine("## Steps").AppendLine();
        builder.AppendLine("| step | outcome | seconds | command |");
        builder.AppendLine("| --- | --- | --- | --- |");
        foreach (StepResult step in manifest.Steps)
        {
            builder
                .Append("| ")
                .Append(step.Id)
                .Append(" | ")
                .Append(step.Outcome)
                .Append(" | ")
                .Append(step.DurationSeconds.ToString("F1", CultureInfo.InvariantCulture))
                .Append(" | `")
                .Append(step.CommandLine)
                .AppendLine("` |");
        }

        if (manifest.FixtureDifferences.Count > 0)
        {
            builder.AppendLine().AppendLine("## Fixture mutations (run failed)").AppendLine();
            foreach (FixtureDifference difference in manifest.FixtureDifferences)
            {
                builder
                    .Append("- `")
                    .Append(difference.Path)
                    .Append("` ")
                    .Append(difference.Change)
                    .AppendLine();
            }
        }

        if (manifest.PendingCoverage.Count > 0)
        {
            builder
                .AppendLine()
                .AppendLine("## Advertised coverage not yet implemented")
                .AppendLine();
            foreach (PendingCoverage pending in manifest.PendingCoverage)
            {
                builder
                    .Append("- `")
                    .Append(pending.Id)
                    .Append("` — ")
                    .Append(pending.Title)
                    .Append(" (")
                    .Append(pending.TrackingIssue)
                    .AppendLine(")");
            }
        }

        List<StepResult> failures = manifest
            .Steps.Where(step => step.Outcome == StepOutcome.Failed)
            .ToList();
        if (failures.Count > 0)
        {
            builder.AppendLine().AppendLine("## Reproduction commands").AppendLine();
            foreach (StepResult failure in failures)
            {
                builder.AppendLine("```bash");
                foreach (
                    KeyValuePair<string, string> variable in failure.Environment.OrderBy(
                        pair => pair.Key,
                        StringComparer.Ordinal
                    )
                )
                {
                    builder
                        .Append("export ")
                        .Append(variable.Key)
                        .Append("='")
                        .Append(variable.Value)
                        .AppendLine("'");
                }

                builder.AppendLine(failure.CommandLine);
                builder.AppendLine("```");
                if (failure.LogPath is not null)
                {
                    builder.Append("Log: `").Append(failure.LogPath).AppendLine("`").AppendLine();
                }
            }
        }

        return builder.ToString();
    }
}
