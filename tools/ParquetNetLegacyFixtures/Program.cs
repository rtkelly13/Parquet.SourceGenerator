// Emits the committed Parquet.Net 6.0.3 producer fixtures under test/data_producers/.
//
// Why this exists as a separate, manually-run tool rather than part of the CLI dataset generator:
// CI regenerates test/data_csharp on every run using the solution's currently pinned Parquet.Net,
// so anything written there is produced by the *current* version regardless of what the fixture
// directory is named. The version/schema-evolution matrix reads the producer out of each file's
// own footer, so a cell declared as "Parquet.Net 6.0.3" needs a file that a 6.0.3 writer really
// produced and that nothing regenerates. This tool pins Parquet.Net 6.0.3 explicitly and writes to
// test/data_producers/parquet-net-6.0.3/, which CI never touches.
//
// Run it only when those fixtures need to change:
//   dotnet run --project tools/ParquetNetLegacyFixtures/ParquetNetLegacyFixtures.csproj
using System.Security.Cryptography;
using Parquet;
using Parquet.Schema;

string repoRoot = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")
);
string outputDir =
    args.Length > 0
        ? args[0]
        : Path.Combine(repoRoot, "test", "data_producers", "parquet-net-6.0.3");

Directory.CreateDirectory(outputDir);
Console.WriteLine($"Writing Parquet.Net 6.0.3 producer fixtures to: {outputDir}");

string path = Path.Combine(outputDir, "01_small_flat_primitives.parquet");
await Write01SmallFlatPrimitivesAsync(path).ConfigureAwait(false);
await ReportAsync(path).ConfigureAwait(false);

// Deliberately mirrors TestDataGenerator.Generate01SmallFlatPrimitivesAsync: same schema, same 100
// rows, same values. Only the writer version differs, which is the whole point.
static async Task Write01SmallFlatPrimitivesAsync(string filePath)
{
    const int count = 100;
    int[] ids = Enumerable.Range(0, count).ToArray();
    string[] names = ids.Select(i => $"user_{i}").ToArray();
    double[] scores = ids.Select(i => (i * 1.5) % 100.0).ToArray();
    bool[] isActive = ids.Select(i => i % 2 == 0).ToArray();
    long[] timestamps = ids.Select(i => 1700000000000L + (i * 1000L)).ToArray();

    var idField = new DataField<int>("id");
    var nameField = new DataField<string>("name");
    var scoreField = new DataField<double>("score");
    var activeField = new DataField<bool>("is_active");
    var timestampField = new DataField<long>("created_at_ms");

    var schema = new ParquetSchema(idField, nameField, scoreField, activeField, timestampField);

    await using var stream = File.Create(filePath);
    await using var writer = await ParquetWriter.CreateAsync(schema, stream).ConfigureAwait(false);
    using ParquetRowGroupWriter groupWriter = writer.CreateRowGroup();
    await groupWriter.WriteAsync<int>(idField, ids).ConfigureAwait(false);
    await groupWriter.WriteAsync(nameField, names).ConfigureAwait(false);
    await groupWriter.WriteAsync<double>(scoreField, scores).ConfigureAwait(false);
    await groupWriter.WriteAsync<bool>(activeField, isActive).ConfigureAwait(false);
    await groupWriter.WriteAsync<long>(timestampField, timestamps).ConfigureAwait(false);
}

// The fixture is only worth committing if its footer actually names the old writer, so print the
// footer and the hash rather than asking anyone to take it on trust.
static async Task ReportAsync(string filePath)
{
    await using var stream = File.OpenRead(filePath);
    await using (
        ParquetReader reader = await ParquetReader.CreateAsync(stream).ConfigureAwait(false)
    )
    {
        Console.WriteLine($"  {Path.GetFileName(filePath)}");
        Console.WriteLine($"    CreatedBy : {reader.Metadata?.CreatedBy}");
        Console.WriteLine($"    Rows      : {reader.Metadata?.NumRows}");
        Console.WriteLine($"    Columns   : {reader.Schema.DataFields.Length}");
    }

    stream.Position = 0;
    Console.WriteLine(
        $"    SHA-256   : {Convert.ToHexString(await SHA256.HashDataAsync(stream).ConfigureAwait(false)).ToLowerInvariant()}"
    );
}
