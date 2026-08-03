using System;
using System.Linq;
using System.Threading.Tasks;

namespace Parquet.SourceGenerator.CLI;

sealed partial class Program
{
    static async Task Main(string[] args)
    {
        if (args.Contains("--profile") || args.Contains("profile"))
        {
            await ProfileWorkload.ExecuteAsync();
        }
        else
        {
            await TestDataGenerator.GenerateAllAsync();
        }
    }
}
