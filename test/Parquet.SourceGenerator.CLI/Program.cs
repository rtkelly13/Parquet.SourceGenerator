using System;
using System.Linq;
using System.Threading.Tasks;

namespace Parquet.SourceGenerator.CLI;

sealed partial class Program
{
    static async Task Main(string[] args)
    {
        if (
            args.Contains("--profile", StringComparer.Ordinal)
            || args.Contains("profile", StringComparer.Ordinal)
        )
        {
            await ProfileWorkload.ExecuteAsync();
        }
        else if (TryGetOption(args, "--pyarrow-output", out string? outputPath))
        {
            await PyArrowInteropGenerator.GenerateAsync(outputPath!);
        }
        else
        {
            await TestDataGenerator.GenerateAllAsync();
        }
    }

    private static bool TryGetOption(string[] args, string option, out string? value)
    {
        int index = Array.IndexOf(args, option);
        if (index >= 0 && index + 1 < args.Length)
        {
            value = args[index + 1];
            return true;
        }

        value = null;
        return false;
    }
}
