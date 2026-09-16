using System;
using System.IO;
using System.Linq;
using Shouldly;
using Xunit;
using IOFile = System.IO.File;

namespace Parquet.SourceGenerator.Tests;

/// <summary>
/// Protects the CI/release test-data verification boundary from drifting back into two copies.
/// </summary>
public sealed class WorkflowConsistencyTests
{
    private const string SharedAction = "./.github/actions/verify-test-data";
    private const string HashStep =
        "- name: Verify Checked-In Dataset Hash Integrity (pre-regeneration)";
    private const string PythonGenerationStep =
        "- name: Generate Python Test Datasets (PyArrow v1 & v2)";
    private const string CSharpGenerationStep = "- name: Generate C# Test Datasets (Parquet.Net)";

    [Fact]
    public void CiAndReleaseDelegateTheTestDataSequenceToOneSharedAction()
    {
        string root = FindRepositoryRoot();
        string action = Read(root, ".github", "actions", "verify-test-data", "action.yml");
        string ci = Read(root, ".github", "workflows", "ci.yml");
        string release = Read(root, ".github", "workflows", "release.yml");

        AssertWorkflowDelegatesOnce(ci);
        AssertWorkflowDelegatesOnce(release);

        foreach (string step in new[] { HashStep, PythonGenerationStep, CSharpGenerationStep })
        {
            Count(action, step).ShouldBe(1, $"Shared action must define '{step}' exactly once.");
            Count(ci, step).ShouldBe(0, $"ci.yml must not duplicate '{step}'.");
            Count(release, step).ShouldBe(0, $"release.yml must not duplicate '{step}'.");
        }

        int hashIndex = action.IndexOf(HashStep, StringComparison.Ordinal);
        int pythonIndex = action.IndexOf(PythonGenerationStep, StringComparison.Ordinal);
        int csharpIndex = action.IndexOf(CSharpGenerationStep, StringComparison.Ordinal);
        hashIndex.ShouldBeLessThan(pythonIndex);
        pythonIndex.ShouldBeLessThan(csharpIndex);

        action.ShouldContain(
            "PARQUET_TEST_DATA_OUTPUT_DIR: ${{ runner.temp }}/parquet-source-generator/data"
        );
        action.ShouldContain(
            "PARQUET_TEST_DATA_CSHARP_OUTPUT_DIR: ${{ runner.temp }}/parquet-source-generator/data_csharp"
        );
    }

    private static void AssertWorkflowDelegatesOnce(string workflow)
    {
        Count(workflow, $"uses: {SharedAction}").ShouldBe(1);
    }

    private static string Read(string root, params string[] segments) =>
        IOFile
            .ReadAllText(Path.Combine(new[] { root }.Concat(segments).ToArray()))
            .Replace("\r\n", "\n");

    private static int Count(string value, string needle)
    {
        int count = 0;
        int offset = 0;
        while ((offset = value.IndexOf(needle, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += needle.Length;
        }

        return count;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (
                IOFile.Exists(Path.Combine(directory.FullName, ".github", "workflows", "ci.yml"))
                && IOFile.Exists(
                    Path.Combine(
                        directory.FullName,
                        ".github",
                        "actions",
                        "verify-test-data",
                        "action.yml"
                    )
                )
            )
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not find the repository root containing the CI workflow and shared verification action."
        );
    }
}
