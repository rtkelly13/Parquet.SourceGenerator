using System;
using System.Linq;
using Parquet.SourceGenerator.Tools.Regression;

if (args.Any(argument => argument is "-h" or "--help"))
{
    Console.WriteLine(RegressionCommandLine.Usage);
    return RegressionExitCode.Success;
}

string discoveredRoot = RegressionCommandLine.DiscoverRepositoryRoot(Environment.CurrentDirectory);

if (
    !RegressionCommandLine.TryParse(
        args,
        discoveredRoot,
        DateTimeOffset.UtcNow,
        out RegressionOptions? options,
        out string? error
    ) || options is null
)
{
    Console.Error.WriteLine($"error: {error}");
    Console.Error.WriteLine();
    Console.Error.WriteLine(RegressionCommandLine.Usage);
    return RegressionExitCode.UsageError;
}

var executor = new RegressionExecutor(new SystemProcessRunner(Console.Out), Console.Out);
RunManifest manifest = executor.Execute(options);
return manifest.ExitCode;
