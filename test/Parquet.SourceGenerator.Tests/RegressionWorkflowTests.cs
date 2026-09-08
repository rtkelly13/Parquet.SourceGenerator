using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Parquet.SourceGenerator.Tools.Regression;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace Parquet.SourceGenerator.Tests;

/// <summary>
/// Structural assertions over the CI wiring for the regression suite.
/// </summary>
/// <remarks>
/// Workflow YAML is only ever exercised by running it, which means a wiring mistake is discovered
/// by a nightly job that quietly reported success. These tests parse the workflow instead of
/// trusting it, and they concentrate on the failure modes this repository has actually hit: a
/// gate job that does not fail when an upstream job is skipped or cancelled, a
/// <c>continue-on-error</c> step producing a false green, and unpinned action references.
/// </remarks>
public sealed class RegressionWorkflowTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string[] ExpectedJobIds =
    {
        "regression",
        "regression-gate",
        "resolve-mode",
    };
    private static readonly string[] ExpectedTiers = { "quick", "full", "deep" };

    private static string WorkflowDirectory => Path.Combine(RepositoryRoot, ".github", "workflows");

    private static YamlMappingNode LoadWorkflow(string fileName)
    {
        string path = Path.Combine(WorkflowDirectory, fileName);
        Assert.True(System.IO.File.Exists(path), $"workflow not found: {path}");

        var stream = new YamlStream();
        using var reader = new StringReader(System.IO.File.ReadAllText(path));
        stream.Load(reader);
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    private static YamlNode Child(YamlMappingNode node, string key)
    {
        Assert.True(
            node.Children.ContainsKey(new YamlScalarNode(key)),
            $"expected key '{key}' in mapping"
        );
        return node.Children[new YamlScalarNode(key)];
    }

    private static YamlMappingNode Map(YamlNode node) => Assert.IsType<YamlMappingNode>(node);

    private static string Scalar(YamlNode node) =>
        Assert.IsType<YamlScalarNode>(node).Value ?? string.Empty;

    // "on" is a YAML 1.1 boolean, so a parser may hand back the key as `true`. Both spellings are
    // checked so this test cannot pass for the wrong reason.
    private static YamlMappingNode Triggers(YamlMappingNode workflow)
    {
        foreach (string key in new[] { "on", "True", "true" })
        {
            if (workflow.Children.ContainsKey(new YamlScalarNode(key)))
            {
                return Map(workflow.Children[new YamlScalarNode(key)]);
            }
        }

        Assert.Fail("workflow has no trigger mapping");
        return null!;
    }

    [Fact]
    public void RegressionWorkflowParsesAndDeclaresTheThreeJobs()
    {
        YamlMappingNode workflow = LoadWorkflow("regression.yml");
        YamlMappingNode jobs = Map(Child(workflow, "jobs"));

        string[] jobIds = jobs
            .Children.Keys.Select(key => Scalar(key))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(ExpectedJobIds, jobIds);
    }

    [Fact]
    public void FullAndDeepAreReachableThroughDispatchAndSchedule()
    {
        YamlMappingNode triggers = Triggers(LoadWorkflow("regression.yml"));

        YamlMappingNode dispatch = Map(Child(triggers, "workflow_dispatch"));
        YamlMappingNode mode = Map(Child(Map(Child(dispatch, "inputs")), "mode"));
        Assert.Equal("choice", Scalar(Child(mode, "type")));
        Assert.Equal(
            ExpectedTiers,
            ((YamlSequenceNode)Child(mode, "options")).Select(Scalar).ToArray()
        );

        var schedule = (YamlSequenceNode)Child(triggers, "schedule");
        string[] crons = schedule.Select(entry => Scalar(Child(Map(entry), "cron"))).ToArray();
        Assert.Equal(2, crons.Length);
        Assert.All(crons, cron => Assert.Equal(5, cron.Split(' ').Length));

        // A pull request must also be a trigger — the quick tier is the PR-facing surface.
        Assert.True(triggers.Children.ContainsKey(new YamlScalarNode("pull_request")));
    }

    [Fact]
    public void PullRequestsAndSchedulesResolveToTheIntendedTiers()
    {
        YamlMappingNode workflow = LoadWorkflow("regression.yml");
        YamlMappingNode resolve = Map(Child(Map(Child(workflow, "jobs")), "resolve-mode"));
        var steps = (YamlSequenceNode)Child(resolve, "steps");
        string script = string.Join("\n", steps.Select(step => Scalar(Child(Map(step), "run"))));

        // The deep cron in the trigger list must be the same string the resolver matches on,
        // otherwise the weekly deep run silently degrades to a full run.
        YamlMappingNode triggers = Triggers(workflow);
        var schedule = (YamlSequenceNode)Child(triggers, "schedule");
        string[] crons = schedule.Select(entry => Scalar(Child(Map(entry), "cron"))).ToArray();
        string deepCron = Assert.Single(
            crons,
            cron => script.Contains(cron, StringComparison.Ordinal)
        );

        Assert.Contains($"\"{deepCron}\"", script, StringComparison.Ordinal);
        Assert.Contains("mode=\"deep\"", script, StringComparison.Ordinal);
        Assert.Contains("mode=\"full\"", script, StringComparison.Ordinal);
        Assert.Contains("mode=\"quick\"", script, StringComparison.Ordinal);
        Assert.Contains("workflow_dispatch)", script, StringComparison.Ordinal);
        Assert.Contains("mode=$mode", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGateJobFailsWhenAnyUpstreamJobIsNotSuccessful()
    {
        YamlMappingNode jobs = Map(Child(LoadWorkflow("regression.yml"), "jobs"));
        YamlMappingNode gate = Map(Child(jobs, "regression-gate"));

        // if: always() is what makes the gate run at all when an upstream job was skipped or
        // cancelled. Without it the gate is skipped too, and a skipped required check is green.
        Assert.Equal("always()", Scalar(Child(gate, "if")));

        string[] needs = ((YamlSequenceNode)Child(gate, "needs")).Select(Scalar).ToArray();
        string[] otherJobs = jobs
            .Children.Keys.Select(Scalar)
            .Where(id => !string.Equals(id, "regression-gate", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(
            otherJobs.OrderBy(id => id, StringComparer.Ordinal),
            needs.OrderBy(id => id, StringComparer.Ordinal)
        );

        var steps = (YamlSequenceNode)Child(gate, "steps");
        YamlMappingNode step = Map(Assert.Single(steps));
        string script = Scalar(Child(step, "run"));
        string environment = string.Join(
            "\n",
            Map(Child(step, "env")).Children.Select(pair => Scalar(pair.Value))
        );

        // Every upstream result must be consumed, and anything other than "success" must fail.
        foreach (string job in otherJobs)
        {
            Assert.Contains($"needs.{job}.result", environment, StringComparison.Ordinal);
        }

        Assert.Contains("!= \"success\"", script, StringComparison.Ordinal);
        Assert.Contains("exit 1", script, StringComparison.Ordinal);
    }

    [Fact]
    public void NoJobOrStepInTheRegressionWorkflowIsContinueOnError()
    {
        YamlMappingNode jobs = Map(Child(LoadWorkflow("regression.yml"), "jobs"));
        foreach (KeyValuePair<YamlNode, YamlNode> job in jobs)
        {
            YamlMappingNode body = Map(job.Value);
            Assert.False(
                body.Children.ContainsKey(new YamlScalarNode("continue-on-error")),
                $"job '{Scalar(job.Key)}' is continue-on-error; that produces a false green"
            );

            foreach (YamlNode step in (YamlSequenceNode)Child(body, "steps"))
            {
                Assert.False(
                    Map(step).Children.ContainsKey(new YamlScalarNode("continue-on-error")),
                    $"a step in job '{Scalar(job.Key)}' is continue-on-error"
                );
            }
        }
    }

    [Fact]
    public void TheRegressionJobRunsTheRunnerWithTheResolvedModeAndNoToolWaiver()
    {
        YamlMappingNode jobs = Map(Child(LoadWorkflow("regression.yml"), "jobs"));
        YamlMappingNode regression = Map(Child(jobs, "regression"));

        Assert.Equal("resolve-mode", Scalar(Child(regression, "needs")));
        Assert.Contains(
            "needs.resolve-mode.outputs.mode",
            Scalar(Child(Map(Child(regression, "env")), "REGRESSION_MODE")),
            StringComparison.Ordinal
        );

        var steps = (YamlSequenceNode)Child(regression, "steps");

        string scripts = string.Join(
            "\n",
            steps
                .Select(Map)
                .Where(step => step.Children.ContainsKey(new YamlScalarNode("run")))
                .Select(step => Scalar(Child(step, "run")))
        );

        Assert.Contains(
            "tools/RegressionRunner/RegressionRunner.csproj",
            scripts,
            StringComparison.Ordinal
        );
        Assert.Contains("\"$REGRESSION_MODE\"", scripts, StringComparison.Ordinal);

        // Waiving prerequisites in CI would turn "PyArrow was not installed" into a green run.
        Assert.DoesNotContain("--allow-missing-tools", scripts, StringComparison.Ordinal);
    }

    [Fact]
    public void ExternalEngineSetupIsSkippedForTheQuickTier()
    {
        YamlMappingNode jobs = Map(Child(LoadWorkflow("regression.yml"), "jobs"));
        var steps = (YamlSequenceNode)Child(Map(Child(jobs, "regression")), "steps");

        foreach (YamlNode node in steps)
        {
            YamlMappingNode step = Map(node);
            string name = step.Children.ContainsKey(new YamlScalarNode("name"))
                ? Scalar(Child(step, "name"))
                : string.Empty;

            if (
                name.Contains("uv", StringComparison.OrdinalIgnoreCase)
                || name.Contains("DuckDB", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Python", StringComparison.OrdinalIgnoreCase)
            )
            {
                Assert.True(
                    step.Children.ContainsKey(new YamlScalarNode("if")),
                    $"step '{name}' installs an external engine but has no tier condition"
                );
                Assert.Contains("!= 'quick'", Scalar(Child(step, "if")), StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void RegressionArtifactsAreUploadedEvenWhenTheRunFails()
    {
        YamlMappingNode jobs = Map(Child(LoadWorkflow("regression.yml"), "jobs"));
        var steps = (YamlSequenceNode)Child(Map(Child(jobs, "regression")), "steps");

        YamlMappingNode upload = Assert.Single(
            steps.Select(Map),
            step =>
                step.Children.ContainsKey(new YamlScalarNode("uses"))
                && Scalar(Child(step, "uses")).Contains("upload-artifact", StringComparison.Ordinal)
        );

        Assert.Equal("always()", Scalar(Child(upload, "if")));
        string path = Scalar(Child(Map(Child(upload, "with")), "path"));
        Assert.Contains("run-manifest.json", path, StringComparison.Ordinal);
        Assert.Contains("logs/**", path, StringComparison.Ordinal);
        Assert.Contains(".parquet", path, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryActionReferenceInEveryWorkflowIsPinnedToACommitSha()
    {
        var sha = new Regex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);
        foreach (string file in Directory.EnumerateFiles(WorkflowDirectory, "*.yml"))
        {
            foreach (string line in System.IO.File.ReadAllLines(file))
            {
                string trimmed = line.Trim();
                if (!trimmed.StartsWith("uses:", StringComparison.Ordinal))
                {
                    continue;
                }

                string reference = trimmed["uses:".Length..].Trim();
                int comment = reference.IndexOf('#', StringComparison.Ordinal);
                if (comment >= 0)
                {
                    reference = reference[..comment].Trim();
                }

                if (reference.StartsWith("./", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] parts = reference.Split('@');
                Assert.True(
                    parts.Length == 2 && sha.IsMatch(parts[1]),
                    $"{Path.GetFileName(file)}: '{reference}' is not pinned to a 40-character commit SHA"
                );
            }
        }
    }

    [Fact]
    public void RequiredPullRequestChecksDoNotRunTheDeepSuite()
    {
        string ci = System.IO.File.ReadAllText(Path.Combine(WorkflowDirectory, "ci.yml"));

        // The PR pipeline's test filter must be exactly the runner's default filter, so the two
        // definitions of "what a PR runs" cannot drift.
        Assert.Contains(RegressionPlan.DefaultTestFilter, ci, StringComparison.Ordinal);

        foreach (string category in new[] { "Property", "Corruption", "LargeDataset" })
        {
            Assert.DoesNotContain($"Category={category}", ci, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheSlashCommandDocumentsEveryTierAndPointsAtTheRunner()
    {
        string command = System.IO.File.ReadAllText(
            Path.Combine(RepositoryRoot, ".claude", "commands", "regression.md")
        );

        Assert.Contains(
            "tools/RegressionRunner/RegressionRunner.csproj",
            command,
            StringComparison.Ordinal
        );
        foreach (string tier in new[] { "quick", "full", "deep" })
        {
            Assert.Contains($"`{tier}`", command, StringComparison.Ordinal);
        }

        Assert.Contains("$ARGUMENTS", command, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRegressionSuiteIsDocumented()
    {
        string doc = System.IO.File.ReadAllText(
            Path.Combine(RepositoryRoot, "docs", "16-REGRESSION-SUITE.md")
        );

        Assert.Contains("/regression", doc, StringComparison.Ordinal);
        foreach (string tier in new[] { "quick", "full", "deep" })
        {
            Assert.Contains(tier, doc, StringComparison.Ordinal);
        }

        Assert.Contains("run-manifest.json", doc, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (
                System.IO.File.Exists(Path.Combine(directory.FullName, RegressionPlan.SolutionFile))
            )
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
