#pragma warning disable CA1305, CA1852, MA0002, MA0009, MA0011, MA0047, MA0051

#:package Microsoft.Build.Locator@1.7.8
#:package Microsoft.CodeAnalysis.CSharp@4.14.0
#:package Microsoft.CodeAnalysis.CSharp.Workspaces@4.14.0
#:package Microsoft.CodeAnalysis.Workspaces.MSBuild@4.14.0
#:package Microsoft.CodeAnalysis.AnalyzerUtilities@5.6.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeMetrics;
using Microsoft.CodeAnalysis.MSBuild;

// -----------------------------------------------------------------------------
// CodeMetrics.cs
//
// Layer 1 of #251. Emits a deterministic, checked-in code-metrics baseline for the hand-written
// product code under src/, and gates CI on drift against it. See docs/21-CODE-METRICS.md.
//
// The metrics are Roslyn's own — Maintainability Index, Cyclomatic Complexity, Class Coupling,
// Depth of Inheritance, Source/Executable Lines — computed by
// Microsoft.CodeAnalysis.CodeMetrics.CodeAnalysisMetricData, the exact type the CA1502/CA1505/
// CA1506 analyzers and the Microsoft `Metrics.exe` tool use.
//
// Why this script and not `Microsoft.CodeAnalysis.Metrics`: that package's entry point is
// `Metrics/Metrics.exe`, a .NET Framework, Windows-only executable (PE32 / MS Windows, with an
// `.exe.config` beside it). Invoking its `/t:Metrics` MSBuild target on Linux or macOS fails with
// "cannot execute binary file" (exit 126), so it cannot run on this repository's ubuntu-latest CI
// or on a macOS or Linux developer machine. `Microsoft.CodeAnalysis.AnalyzerUtilities` ships the
// same computation as a netstandard2.0 library, so this script is that tool, cross-platform, with
// a diff-friendly renderer instead of XML.
//
// Determinism: the Roslyn version that parses the source and computes the metrics is pinned above,
// not taken from the SDK, so the numbers do not move when a developer or a runner updates their
// SDK. Every ordering is `StringComparer.Ordinal` (see docs/17-GENERATED-API-BASELINES.md for why
// that matters here).
//
// Usage:
//   dotnet run scripts/CodeMetrics.cs                        # gate: compare against the baseline
//   UPDATE_GOLDEN_FILES=true dotnet run scripts/CodeMetrics.cs   # refresh the baseline
//   dotnet run scripts/CodeMetrics.cs -- --update            # same, explicit flag
//   dotnet run scripts/CodeMetrics.cs -- --summary out.md    # also write a Markdown report
// -----------------------------------------------------------------------------

// The measured set is the hand-written product code. Test, benchmark, sample and tooling projects
// are deliberately out of scope: their metrics churn with every new test case and say nothing
// about the maintainability of the shipped generator. Generated code is layer 2 of #251.
var measured = new (string Project, string? TargetFramework)[]
{
    ("src/Parquet.SourceGenerator/Parquet.SourceGenerator.csproj", null),
    ("src/Parquet.SourceGenerator.Legacy/Parquet.SourceGenerator.Legacy.csproj", null),
    // Multi-targeted. netstandard2.0 is the widest target and the one every other TFM is a
    // superset of, so it is pinned here: measuring "whichever TFM MSBuild happened to hand back
    // first" would not be deterministic.
    (
        "src/Parquet.SourceGenerator.Attributes/Parquet.SourceGenerator.Attributes.csproj",
        "netstandard2.0"
    ),
};

const string BaselineDirectory = "metrics";

// The tolerance. Cyclomatic complexity, class coupling, depth of inheritance and the two line
// counts are integer counts of syntactic facts: they are reproduced exactly by a pinned Roslyn, so
// they are compared exactly. The maintainability index is a rounded floating-point function of a
// Halstead volume, so it is allowed a +/-2 band to absorb rounding and reference-assembly noise
// without the gate crying wolf. See docs/21-CODE-METRICS.md.
const int MaintainabilityIndexTolerance = 2;

bool update =
    string.Equals(
        Environment.GetEnvironmentVariable("UPDATE_GOLDEN_FILES"),
        "true",
        StringComparison.OrdinalIgnoreCase
    ) || args.Contains("--update", StringComparer.Ordinal);
string? summaryPath = null;
for (int i = 0; i < args.Length; i++)
{
    if (string.Equals(args[i], "--summary", StringComparison.Ordinal) && i + 1 < args.Length)
    {
        summaryPath = args[i + 1];
    }
}

string repoRoot = FindRepositoryRoot();
Directory.SetCurrentDirectory(repoRoot);
Directory.CreateDirectory(BaselineDirectory);

ValidateCodeMetricsConfig("CodeMetricsConfig.txt");

MSBuildLocator.RegisterDefaults();

var failures = new List<string>();
var allRows = new List<MetricRow>();

foreach (var (projectPath, tfm) in measured)
{
    string name = Path.GetFileNameWithoutExtension(projectPath);
    Console.WriteLine($"Measuring {name}...");

    var rows = await MeasureAsync(projectPath, tfm).ConfigureAwait(false);
    allRows.AddRange(rows);

    string rendered = Render(name, rows);
    string baselinePath = Path.Combine(BaselineDirectory, name + ".metrics.txt");

    if (update)
    {
        WriteIfChanged(baselinePath, rendered);
        Console.WriteLine($"  wrote {baselinePath} ({rows.Count} entries)");
        continue;
    }

    if (!File.Exists(baselinePath))
    {
        failures.Add(
            $"Code metrics baseline missing: {baselinePath}\n"
                + "  No baseline exists for this project. Generate it with\n"
                + "  UPDATE_GOLDEN_FILES=true dotnet run scripts/CodeMetrics.cs\n"
                + "  and commit the result."
        );
        continue;
    }

    string drift = Compare(baselinePath, ReadAllLines(baselinePath), rows);
    if (drift.Length > 0)
    {
        failures.Add(drift);
    }
    else
    {
        Console.WriteLine($"  {baselinePath} OK ({rows.Count} entries)");
    }
}

if (summaryPath is not null)
{
    File.WriteAllText(summaryPath, RenderSummary(allRows), new UTF8Encoding(false));
    Console.WriteLine($"Wrote metrics summary to {summaryPath}");
}

if (failures.Count > 0)
{
    Console.Error.WriteLine();
    foreach (string f in failures)
    {
        Console.Error.WriteLine(f);
        Console.Error.WriteLine();
    }
    Console.Error.WriteLine(
        "Refresh with: UPDATE_GOLDEN_FILES=true dotnet run scripts/CodeMetrics.cs, then commit the\n"
            + "changed files under metrics/. The diff is the point: it shows the reviewer exactly which\n"
            + "types and members got harder to maintain. See docs/21-CODE-METRICS.md."
    );
    return 1;
}

Console.WriteLine(update ? "Baselines refreshed." : "Code metrics baselines match.");
return 0;

// ---------------------------------------------------------------------------------------------

// CodeMetricsConfig.txt is what CA1502 / CA1505 / CA1506 read their thresholds from, and it has a
// dangerous failure mode: a single unrecognised entry — `NamedType` where the parser wants `Type`,
// say — makes all three rules stop reporting ENTIRELY and SILENTLY. No CA1509 diagnostic is
// emitted, the build goes green, and the gate is gone. Verified empirically against
// Microsoft.CodeAnalysis.NetAnalyzers as shipped with the .NET 9 SDK. This validator is the guard:
// the gate that the build depends on is checked by the gate that CI depends on.
static void ValidateCodeMetricsConfig(string path)
{
    if (!File.Exists(path))
    {
        throw new InvalidOperationException(
            $"{path} is missing. CA1502/CA1505/CA1506 read their thresholds from it; without it the\n"
                + "build-time half of the code metrics gate is silently disabled."
        );
    }

    // Empirically verified accepted spellings. `NamedType` is NOT one of them.
    string[] validKinds = ["Assembly", "Namespace", "Type", "Method", "Field", "Property", "Event"];
    string[] validRules = ["CA1501", "CA1502", "CA1505", "CA1506"];

    int lineNumber = 0;
    foreach (string raw in ReadAllLines(path))
    {
        lineNumber++;
        string line = raw.Trim();
        if (line.Length == 0 || line[0] == '#')
        {
            continue;
        }

        int colon = line.IndexOf(':', StringComparison.Ordinal);
        string left = colon < 0 ? string.Empty : line[..colon].Trim();
        string right = colon < 0 ? string.Empty : line[(colon + 1)..].Trim();
        string rule = left;
        string? kind = null;
        int paren = left.IndexOf('(', StringComparison.Ordinal);
        if (paren >= 0 && left.EndsWith(")", StringComparison.Ordinal))
        {
            rule = left[..paren].Trim();
            kind = left[(paren + 1)..^1].Trim();
        }

        bool ok =
            colon > 0
            && validRules.Contains(rule, StringComparer.Ordinal)
            && (kind is null || validKinds.Contains(kind, StringComparer.Ordinal))
            && int.TryParse(
                right,
                System.Globalization.NumberStyles.None,
                CultureInfo.InvariantCulture,
                out _
            );

        if (!ok)
        {
            throw new InvalidOperationException(
                $"{path}({lineNumber}): malformed entry '{raw}'.\n"
                    + "  Expected 'RuleId: Threshold' or 'RuleId(SymbolKind): Threshold'.\n"
                    + $"  Valid rules: {string.Join(", ", validRules)}.\n"
                    + $"  Valid symbol kinds: {string.Join(", ", validKinds)}.\n"
                    + "  A malformed entry silently disables CA1502, CA1505 and CA1506 for the whole build."
            );
        }
    }
}

static string FindRepositoryRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (
        dir is not null && !File.Exists(Path.Combine(dir.FullName, "Parquet.SourceGenertor.sln"))
    )
    {
        dir = dir.Parent;
    }

    return dir?.FullName
        ?? throw new InvalidOperationException("Could not locate the repository root.");
}

static async Task<List<MetricRow>> MeasureAsync(string projectPath, string? targetFramework)
{
    var properties = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Release so the measured compilation is the one CI builds and ships.
        ["Configuration"] = "Release",
    };
    if (targetFramework is not null)
    {
        properties["TargetFramework"] = targetFramework;
    }

    using var workspace = MSBuildWorkspace.Create(properties);
    var loadFailures = new List<string>();
    workspace.WorkspaceFailed += (_, e) =>
    {
        if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
        {
            loadFailures.Add(e.Diagnostic.Message);
        }
    };

    var project = await workspace.OpenProjectAsync(projectPath).ConfigureAwait(false);
    if (loadFailures.Count > 0)
    {
        throw new InvalidOperationException(
            $"MSBuild could not load {projectPath}:\n  " + string.Join("\n  ", loadFailures)
        );
    }

    var compilation =
        await project.GetCompilationAsync().ConfigureAwait(false)
        ?? throw new InvalidOperationException($"No compilation produced for {projectPath}.");

    // A compilation with unresolved references produces degraded IOperation trees and therefore
    // wrong metrics. Failing loudly here is the difference between a baseline and a fiction.
    var errors = compilation
        .GetDiagnostics()
        .Where(d => d.Severity == DiagnosticSeverity.Error)
        .Select(d => d.ToString())
        .Distinct(StringComparer.Ordinal)
        .OrderBy(s => s, StringComparer.Ordinal)
        .Take(10)
        .ToList();
    if (errors.Count > 0)
    {
        throw new InvalidOperationException(
            $"{projectPath} does not compile cleanly under the metrics host; metrics would be\n"
                + "unreliable. First errors:\n  "
                + string.Join("\n  ", errors)
        );
    }

    var context = new CodeMetricsAnalysisContext(compilation, CancellationToken.None);
    var data = await CodeAnalysisMetricData.ComputeAsync(context).ConfigureAwait(false);

    var rows = new List<MetricRow>();
    Collect(data, rows);
    rows.Sort(static (a, b) => StringComparer.Ordinal.Compare(a.Key, b.Key));
    return rows;
}

static void Collect(CodeAnalysisMetricData data, List<MetricRow> rows)
{
    string? key = KeyFor(data.Symbol);
    if (key is not null)
    {
        rows.Add(
            new MetricRow(
                key,
                data.MaintainabilityIndex,
                data.CyclomaticComplexity,
                data.CoupledNamedTypes.Count,
                data.DepthOfInheritance,
                data.SourceLines,
                data.ExecutableLines
            )
        );
    }

    foreach (var child in data.Children)
    {
        Collect(child, rows);
    }
}

static string? KeyFor(ISymbol symbol) =>
    symbol.Kind switch
    {
        SymbolKind.Assembly => "A:" + symbol.Name,
        // The global namespace has an empty name and no metrics of its own worth a line.
        SymbolKind.Namespace => ((INamespaceSymbol)symbol).IsGlobalNamespace
            ? null
            : "N:" + symbol.ToDisplayString(Display.Format),
        SymbolKind.NamedType => "T:" + symbol.ToDisplayString(Display.Format),
        SymbolKind.Method => "M:" + symbol.ToDisplayString(Display.Format),
        SymbolKind.Property => "P:" + symbol.ToDisplayString(Display.Format),
        SymbolKind.Event => "E:" + symbol.ToDisplayString(Display.Format),
        // Fields carry no complexity — every field is MI=100, CC=0 — so a line per field would be
        // pure churn on rename with no signal. Deliberately omitted; see docs/21-CODE-METRICS.md.
        SymbolKind.Field => null,
        _ => null,
    };

static string Render(string projectName, List<MetricRow> rows)
{
    var sb = new StringBuilder();
    sb.Append("# Code metrics baseline: ").Append(projectName).Append('\n');
    sb.Append("# Generated by scripts/CodeMetrics.cs. Do not edit by hand.\n");
    sb.Append("# Refresh: UPDATE_GOLDEN_FILES=true dotnet run scripts/CodeMetrics.cs\n");
    sb.Append("# Kinds: A assembly, N namespace, T type, M method, P property, E event.\n");
    sb.Append(
        "# MI maintainability index 0-100 (higher is better), CC cyclomatic complexity,\n"
            + "# CL class coupling, DIT depth of inheritance, SLOC source lines, ELOC executable lines.\n"
            + "# Ordinal-sorted. MI is compared with a +/-2 tolerance; every other metric exactly.\n"
    );
    foreach (var row in rows)
    {
        sb.Append(row.Key).Append(" | ").Append(row.Metrics).Append('\n');
    }

    return sb.ToString();
}

static string[] ReadAllLines(string path) =>
    File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

static string Compare(string baselinePath, string[] baselineLines, List<MetricRow> current)
{
    var baseline = new Dictionary<string, MetricRow>(StringComparer.Ordinal);
    var baselineOrder = new List<string>();
    foreach (string line in baselineLines)
    {
        if (line.Length == 0 || line[0] == '#')
        {
            continue;
        }

        var row = MetricRow.Parse(line);
        baseline[row.Key] = row;
        baselineOrder.Add(row.Key);
    }

    var currentByKey = current.ToDictionary(r => r.Key, StringComparer.Ordinal);
    var messages = new List<string>();

    foreach (string key in baselineOrder)
    {
        if (!currentByKey.ContainsKey(key))
        {
            messages.Add($"  - removed: {key}");
        }
    }

    foreach (var row in current)
    {
        if (!baseline.TryGetValue(row.Key, out var old))
        {
            messages.Add($"  + added:   {row.Key} | {row.Metrics}");
            continue;
        }

        var changes = row.DriftAgainst(old, MaintainabilityIndexTolerance);
        if (changes.Count > 0)
        {
            messages.Add($"  ~ changed: {row.Key}");
            foreach (string change in changes)
            {
                messages.Add("      " + change);
            }
        }
    }

    if (messages.Count == 0)
    {
        // Ordering is part of the artifact: a reordered file is a drifted file.
        var currentKeys = current.Select(r => r.Key).ToList();
        if (!currentKeys.SequenceEqual(baselineOrder, StringComparer.Ordinal))
        {
            messages.Add("  ~ ordering: the baseline is not in ordinal order; refresh it.");
        }
    }

    if (messages.Count == 0)
    {
        return string.Empty;
    }

    return $"Code metrics baseline drifted: {baselinePath}\n" + string.Join("\n", messages);
}

static string RenderSummary(List<MetricRow> allRows)
{
    // TargetParser and friends are linked into both generator projects, so the same entity is
    // measured twice. One row per entity in the report.
    var rows = allRows.GroupBy(r => r.Key, StringComparer.Ordinal).Select(g => g.First()).ToList();

    var sb = new StringBuilder();
    sb.Append("## Code metrics (hand-written `src/`)\n\n");

    AppendTable(
        sb,
        "Least maintainable types (lowest MI)",
        rows.Where(r => r.Key.StartsWith("T:", StringComparison.Ordinal))
            .OrderBy(r => r.MaintainabilityIndex)
            .ThenBy(r => r.Key, StringComparer.Ordinal)
            .Take(10)
    );
    AppendTable(
        sb,
        "Most complex methods (highest cyclomatic complexity)",
        rows.Where(r => r.Key.StartsWith("M:", StringComparison.Ordinal))
            .OrderByDescending(r => r.CyclomaticComplexity)
            .ThenBy(r => r.Key, StringComparer.Ordinal)
            .Take(10)
    );
    AppendTable(
        sb,
        "Highest class coupling (types)",
        rows.Where(r => r.Key.StartsWith("T:", StringComparison.Ordinal))
            .OrderByDescending(r => r.ClassCoupling)
            .ThenBy(r => r.Key, StringComparer.Ordinal)
            .Take(10)
    );

    sb.Append(
        "\n> The Maintainability Index is a Halstead-derived 1990s formula and a poor absolute\n"
            + "> judgement of quality. It is used here as a change detector, not a score to optimise.\n"
            + "> See `docs/21-CODE-METRICS.md`.\n"
    );
    return sb.ToString();
}

static void AppendTable(StringBuilder sb, string title, IEnumerable<MetricRow> rows)
{
    sb.Append("### ").Append(title).Append("\n\n");
    sb.Append("| Entity | MI | CC | CL | SLOC |\n|:--|--:|--:|--:|--:|\n");
    foreach (var r in rows)
    {
        // The parameter list is what makes an entity unique in the baseline; in a report it is
        // unreadable noise, so the summary shows the name and an arity.
        string name = r.Key[2..];
        int paren = name.IndexOf('(', StringComparison.Ordinal);
        if (paren >= 0)
        {
            string parameters = name[(paren + 1)..^1];
            int arity = parameters.Length == 0 ? 0 : Depth(parameters) + 1;
            name = string.Create(CultureInfo.InvariantCulture, $"{name[..paren]}(..{arity}..)");
        }

        sb.Append("| `")
            .Append(name)
            .Append("` | ")
            .Append(r.MaintainabilityIndex.ToString(CultureInfo.InvariantCulture))
            .Append(" | ")
            .Append(r.CyclomaticComplexity.ToString(CultureInfo.InvariantCulture))
            .Append(" | ")
            .Append(r.ClassCoupling.ToString(CultureInfo.InvariantCulture))
            .Append(" | ")
            .Append(r.SourceLines.ToString(CultureInfo.InvariantCulture))
            .Append(" |\n");
    }

    sb.Append('\n');
}

// Counts top-level commas in a parameter list, ignoring those inside generic arguments.
static int Depth(string parameters)
{
    int commas = 0;
    int nesting = 0;
    foreach (char c in parameters)
    {
        if (c is '<' or '[')
        {
            nesting++;
        }
        else if (c is '>' or ']')
        {
            nesting--;
        }
        else if (c == ',' && nesting == 0)
        {
            commas++;
        }
    }

    return commas;
}

static void WriteIfChanged(string path, string content)
{
    if (
        File.Exists(path)
        && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal)
    )
    {
        return;
    }

    File.WriteAllText(path, content, new UTF8Encoding(false));
}

internal sealed record MetricRow(
    string Key,
    int MaintainabilityIndex,
    int CyclomaticComplexity,
    int ClassCoupling,
    int? DepthOfInheritance,
    long SourceLines,
    long ExecutableLines
)
{
    public string Metrics =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"MI={MaintainabilityIndex} CC={CyclomaticComplexity} CL={ClassCoupling} DIT={(DepthOfInheritance.HasValue ? DepthOfInheritance.Value.ToString(CultureInfo.InvariantCulture) : "-")} SLOC={SourceLines} ELOC={ExecutableLines}"
        );

    public static MetricRow Parse(string line)
    {
        int bar = line.LastIndexOf(" | ", StringComparison.Ordinal);
        if (bar < 0)
        {
            throw new InvalidOperationException($"Malformed metrics baseline line: {line}");
        }

        string key = line[..bar];
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (
            string token in line[(bar + 3)..].Split(' ', StringSplitOptions.RemoveEmptyEntries)
        )
        {
            int eq = token.IndexOf('=', StringComparison.Ordinal);
            fields[token[..eq]] = token[(eq + 1)..];
        }

        return new MetricRow(
            key,
            int.Parse(fields["MI"], CultureInfo.InvariantCulture),
            int.Parse(fields["CC"], CultureInfo.InvariantCulture),
            int.Parse(fields["CL"], CultureInfo.InvariantCulture),
            string.Equals(fields["DIT"], "-", StringComparison.Ordinal)
                ? null
                : int.Parse(fields["DIT"], CultureInfo.InvariantCulture),
            long.Parse(fields["SLOC"], CultureInfo.InvariantCulture),
            long.Parse(fields["ELOC"], CultureInfo.InvariantCulture)
        );
    }

    public List<string> DriftAgainst(MetricRow baseline, int maintainabilityIndexTolerance)
    {
        var changes = new List<string>();
        if (
            Math.Abs(MaintainabilityIndex - baseline.MaintainabilityIndex)
            > maintainabilityIndexTolerance
        )
        {
            changes.Add(
                $"MI {baseline.MaintainabilityIndex} -> {MaintainabilityIndex} (tolerance +/-{maintainabilityIndexTolerance})"
            );
        }

        Exact(changes, "CC", baseline.CyclomaticComplexity, CyclomaticComplexity);
        Exact(changes, "CL", baseline.ClassCoupling, ClassCoupling);
        if (baseline.DepthOfInheritance != DepthOfInheritance)
        {
            changes.Add($"DIT {Show(baseline.DepthOfInheritance)} -> {Show(DepthOfInheritance)}");
        }

        Exact(changes, "SLOC", baseline.SourceLines, SourceLines);
        Exact(changes, "ELOC", baseline.ExecutableLines, ExecutableLines);
        return changes;

        static void Exact(List<string> into, string name, long was, long now)
        {
            if (was != now)
            {
                into.Add($"{name} {was} -> {now}");
            }
        }

        static string Show(int? value) =>
            value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "-";
    }
}

internal static class Display
{
    // The display format. Fully qualified, no `global::`, parameter types but not parameter names —
    // a parameter rename is not a maintainability change and must not produce a diff.
    public static readonly SymbolDisplayFormat Format = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeParameters
            | SymbolDisplayMemberOptions.IncludeContainingType
            | SymbolDisplayMemberOptions.IncludeExplicitInterface,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType
            | SymbolDisplayParameterOptions.IncludeParamsRefOut,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes
            | SymbolDisplayMiscellaneousOptions.ExpandNullable
    );
}
