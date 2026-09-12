#pragma warning disable CA1305, CA1852, MA0002, MA0009, MA0011, MA0047, MA0051

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

// -----------------------------------------------------------------------------
// MutationSummary.cs
//
// Reader for #261: parses Stryker's JSON report into the numbers the nightly PR
// body needs — the score, and the surviving-mutant inventory for the areas that
// matter most (the parser and the emitters), because a score without a list is
// not triageable.
//
// The score is RECOMPUTED, never taken from the report's own headline, and the
// denominators are explicit: mutants the runner could not meaningfully test
// (compile errors, ignored, not-run) are excluded from the denominator, and the
// exclusions are named so a reader can see exactly what the percentage means.
// A mutation score is only as honest as its visible denominator.
//
//   dotnet run scripts/MutationSummary.cs -- --report <mutation-report.json>
//       [--label "TargetParser+emitters"] [--out <markdown file>] [--append]
// -----------------------------------------------------------------------------

string? reportPath = null;
string label = "suite";
string? outPath = null;
bool append = false;

var argv = Environment.GetCommandLineArgs();
for (int i = 1; i < argv.Length; i++)
{
    switch (argv[i])
    {
        case "--report":
            reportPath = Require(argv, ref i, "--report");
            break;
        case "--label":
            label = Require(argv, ref i, "--label");
            break;
        case "--out":
            outPath = Require(argv, ref i, "--out");
            break;
        case "--append":
            append = true;
            break;
        default:
            Console.Error.WriteLine($"Unknown argument: {argv[i]}");
            return 2;
    }
}

if (reportPath is null || !File.Exists(reportPath))
{
    Console.Error.WriteLine(
        $"--report <path to Stryker mutation-report.json> is required and must exist."
    );
    return 2;
}

using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(reportPath));
JsonElement root = doc.RootElement;

int total = 0;
int killed = 0;
int survived = 0;
int timeout = 0;
int compileError = 0;
int ignored = 0;
int notRun = 0;
var survivorsByFile = new Dictionary<string, List<string>>(StringComparer.Ordinal);

foreach (JsonElement project in Enumerate(root, "Projects"))
{
    foreach (JsonProperty file in EnumerateFiles(project, "Files"))
    {
        foreach (JsonElement mutant in Enumerate(file.Value, "Mutants"))
        {
            string status = Str(mutant, "Status");
            total++;
            switch (status)
            {
                case "Killed":
                    killed++;
                    break;
                case "Survived":
                    survived++;
                    Add(file.Name, mutant);
                    break;
                case "Timeout":
                    timeout++;
                    break;
                case "CompileError":
                    compileError++;
                    break;
                case "Ignored":
                    ignored++;
                    break;
                case "NotRun":
                    notRun++;
                    break;
            }
        }
    }
}

void Add(string file, JsonElement mutant)
{
    if (!survivorsByFile.TryGetValue(file, out List<string>? list))
        survivorsByFile[file] = list = [];
    string line =
        mutant.TryGetProperty("Line", out JsonElement ln) && ln.TryGetInt32(out int n)
            ? "L" + n.ToString(CultureInfo.InvariantCulture)
            : "L?";
    list.Add($"{Str(mutant, "Name")} {line} [{Str(mutant, "MutatorName")}]");
}

int tested = total - compileError - ignored - notRun;
double score = tested == 0 ? 0 : 100.0 * (killed + timeout) / tested;

var sb = new StringBuilder();
sb.AppendLine($"### {label}");
sb.AppendLine();
sb.AppendLine(
    $"Mutation score **{score.ToString("F1", CultureInfo.InvariantCulture)}%** — "
        + $"{killed + timeout} detected / {tested} tested "
        + $"({total} generated; {compileError} compile-error, {ignored} ignored, {notRun} not-run excluded)."
);
sb.AppendLine();
sb.AppendLine($"| surviving mutants in | count |");
sb.AppendLine($"|:--|--:|");
foreach (
    (string file, List<string> mutants) in survivorsByFile
        .OrderByDescending(kv => kv.Value.Count)
        .ThenBy(kv => kv.Key, StringComparer.Ordinal)
        .Take(12)
)
{
    sb.AppendLine($"| `{Short(file)}` | {mutants.Count} |");
}
if (survivorsByFile.Count > 12)
    sb.AppendLine($"| … {survivorsByFile.Count - 12} more files | — |");
sb.AppendLine();

Console.Write(sb);

if (outPath is not null)
{
    if (!append || !File.Exists(outPath))
        File.WriteAllText(outPath, sb.ToString());
    else
        File.AppendAllText(outPath, sb.ToString());
}

return 0; // #261: report-only. A floor arrives in a later change that cites a measured score.

static string Require(string[] argv, ref int i, string flag)
{
    if (i + 1 >= argv.Length)
    {
        Console.Error.WriteLine($"{flag} requires a value");
        Environment.Exit(2);
    }
    return argv[++i];
}

static IEnumerable<JsonElement> Enumerate(JsonElement el, string property)
{
    if (el.TryGetProperty(property, out JsonElement arr) && arr.ValueKind == JsonValueKind.Array)
        return arr.EnumerateArray();
    if (el.TryGetProperty(property, out JsonElement obj) && obj.ValueKind == JsonValueKind.Object)
        return obj.EnumerateObject().Select(p => p.Value);
    return [];
}

static IEnumerable<JsonProperty> EnumerateFiles(JsonElement el, string property) =>
    el.TryGetProperty(property, out JsonElement files) && files.ValueKind == JsonValueKind.Object
        ? files.EnumerateObject()
        : [];

static string Str(JsonElement el, string property) =>
    el.TryGetProperty(property, out JsonElement v) && v.ValueKind == JsonValueKind.String
        ? v.GetString()!
        : "?";

static string Short(string path)
{
    int idx = path.IndexOf("src/", StringComparison.Ordinal);
    return idx >= 0 ? path[idx..] : path;
}
