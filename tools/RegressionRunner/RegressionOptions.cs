using System;
using System.Collections.Generic;
using System.Globalization;

namespace Parquet.SourceGenerator.Tools.Regression;

/// <summary>
/// Parsed command line for the <c>/regression</c> entrypoint.
/// </summary>
/// <param name="Mode">The tier to run.</param>
/// <param name="RepositoryRoot">Repository root; every command runs from here.</param>
/// <param name="ArtifactRoot">Where logs, manifests and generated Parquet files are written.</param>
/// <param name="RunId">Identifier for this run.</param>
/// <param name="DryRun">Print the plan and prerequisite check, execute nothing.</param>
/// <param name="AllowMissingTools">Downgrade a missing prerequisite from failure to a recorded skip.</param>
/// <param name="KeepGoing">Continue after a failing step instead of stopping at the first one.</param>
/// <param name="EnforceBudget">Fail the run when it exceeds the tier's time budget.</param>
/// <param name="Context">Deterministic plan inputs.</param>
public sealed record RegressionOptions(
    RegressionMode Mode,
    string RepositoryRoot,
    string ArtifactRoot,
    string RunId,
    bool DryRun,
    bool AllowMissingTools,
    bool KeepGoing,
    bool EnforceBudget,
    PlanContext Context
);

/// <summary>
/// Command-line parsing, kept separate from execution so the surface can be unit-tested.
/// </summary>
public static class RegressionCommandLine
{
    /// <summary>Usage text printed by <c>--help</c> and on a usage error.</summary>
    public const string Usage = """
        Usage: dotnet run --project tools/RegressionRunner -- [quick|full|deep] [options]

          quick   (default) deterministic generated round trips and checked-in fixture reads
          full    quick plus pinned PyArrow, DuckDB, version-matrix and fixture-integrity checks
          deep    full plus broad property seeds, corruption, large datasets and AOT/IL diagnostics

        Options:
          --repo-root <dir>      repository root (default: discovered from the working directory)
          --artifacts <dir>      artifact directory (default: <repo>/temp/regression/<run-id>)
          --run-id <id>          run identifier (default: <mode>-<utc timestamp>)
          --rid <rid>            runtime identifier for the deep-mode AOT publish
          --seeds <n>            property seed count for deep mode
          --large-rows <n>       row count for the deep-mode large dataset suite
          --dry-run              print the plan and prerequisite report, run nothing
          --allow-missing-tools  record missing prerequisites as skips instead of failing
          --keep-going           run every step even after one fails
          --enforce-budget       fail the run if it exceeds the mode's time budget
          -h, --help             show this help
        """;

    /// <summary>
    /// Parses arguments.
    /// </summary>
    /// <param name="args">Raw arguments.</param>
    /// <param name="defaultRepositoryRoot">Repository root to use when <c>--repo-root</c> is absent.</param>
    /// <param name="timestamp">Timestamp used to build the default run id.</param>
    /// <param name="options">The parsed options when parsing succeeded.</param>
    /// <param name="error">The reason parsing failed, or null.</param>
    /// <returns>True when the arguments were valid.</returns>
    public static bool TryParse(
        IReadOnlyList<string> args,
        string defaultRepositoryRoot,
        DateTimeOffset timestamp,
        out RegressionOptions? options,
        out string? error
    )
    {
        ArgumentNullException.ThrowIfNull(args);

        options = null;
        error = null;

        RegressionMode mode = RegressionMode.Quick;
        string? repositoryRoot = null;
        string? artifactRoot = null;
        string? runId = null;
        string? rid = null;
        int seeds = 512;
        long largeRows = 500_000;
        bool dryRun = false;
        bool allowMissingTools = false;
        bool keepGoing = false;
        bool enforceBudget = false;
        bool modeSeen = false;

        for (int i = 0; i < args.Count; i++)
        {
            string argument = args[i];
            switch (argument)
            {
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--allow-missing-tools":
                    allowMissingTools = true;
                    break;
                case "--keep-going":
                    keepGoing = true;
                    break;
                case "--enforce-budget":
                    enforceBudget = true;
                    break;
                case "--repo-root":
                    if (!TryTake(args, ref i, argument, out repositoryRoot, out error))
                    {
                        return false;
                    }

                    break;
                case "--artifacts":
                    if (!TryTake(args, ref i, argument, out artifactRoot, out error))
                    {
                        return false;
                    }

                    break;
                case "--run-id":
                    if (!TryTake(args, ref i, argument, out runId, out error))
                    {
                        return false;
                    }

                    break;
                case "--rid":
                    if (!TryTake(args, ref i, argument, out rid, out error))
                    {
                        return false;
                    }

                    break;
                case "--seeds":
                    if (
                        !TryTake(args, ref i, argument, out string? seedText, out error)
                        || !int.TryParse(
                            seedText,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out seeds
                        )
                        || seeds <= 0
                    )
                    {
                        error ??= "--seeds requires a positive integer.";
                        return false;
                    }

                    break;
                case "--large-rows":
                    if (
                        !TryTake(args, ref i, argument, out string? rowText, out error)
                        || !long.TryParse(
                            rowText,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out largeRows
                        )
                        || largeRows <= 0
                    )
                    {
                        error ??= "--large-rows requires a positive integer.";
                        return false;
                    }

                    break;
                default:
                    if (argument.StartsWith('-'))
                    {
                        error = $"Unknown option '{argument}'.";
                        return false;
                    }

                    if (modeSeen)
                    {
                        error = $"Unexpected argument '{argument}'.";
                        return false;
                    }

                    if (!RegressionPlan.TryParseMode(argument, out mode))
                    {
                        error = $"Unknown mode '{argument}'. Expected quick, full or deep.";
                        return false;
                    }

                    modeSeen = true;
                    break;
            }
        }

        string root = System.IO.Path.GetFullPath(repositoryRoot ?? defaultRepositoryRoot);
        string id =
            runId
            ?? string.Create(
                CultureInfo.InvariantCulture,
                $"{mode.ToString().ToLowerInvariant()}-{timestamp:yyyyMMdd-HHmmss}"
            );
        string artifacts = System.IO.Path.GetFullPath(
            artifactRoot ?? System.IO.Path.Combine(root, "temp", "regression", id)
        );

        options = new RegressionOptions(
            mode,
            root,
            artifacts,
            id,
            dryRun,
            allowMissingTools,
            keepGoing,
            enforceBudget,
            new PlanContext(
                rid ?? System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
                seeds,
                largeRows
            )
        );
        return true;
    }

    /// <summary>
    /// Walks upwards from a directory looking for the repository root.
    /// </summary>
    /// <param name="start">Directory to start from.</param>
    /// <returns>The repository root, or <paramref name="start"/> when none is found.</returns>
    public static string DiscoverRepositoryRoot(string start)
    {
        ArgumentNullException.ThrowIfNull(start);
        var directory = new System.IO.DirectoryInfo(System.IO.Path.GetFullPath(start));
        while (directory is not null)
        {
            if (
                System.IO.File.Exists(
                    System.IO.Path.Combine(directory.FullName, RegressionPlan.SolutionFile)
                )
            )
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return System.IO.Path.GetFullPath(start);
    }

    private static bool TryTake(
        IReadOnlyList<string> args,
        ref int index,
        string option,
        out string? value,
        out string? error
    )
    {
        if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            value = null;
            error = $"{option} requires a value.";
            return false;
        }

        index++;
        value = args[index];
        error = null;
        return true;
    }
}
