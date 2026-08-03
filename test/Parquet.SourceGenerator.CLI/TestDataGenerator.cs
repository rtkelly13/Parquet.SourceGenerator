using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Parquet.Schema;

namespace Parquet.SourceGenerator.CLI;

public static class TestDataGenerator
{
    private static readonly string BaseOutputDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data_csharp"));
    private static readonly string V3OutputDir = Path.Combine(BaseOutputDir, "v3");

    public static async Task GenerateAllAsync()
    {
        Directory.CreateDirectory(V3OutputDir);
        Console.WriteLine($"📁 Generating C# Parquet.Net datasets in: {V3OutputDir}");

        await Generate01SmallFlatPrimitivesAsync();
        await Generate02MediumNullableTypesAsync();
        await Generate03ComplexDecimalsGuidsAsync();
        await Generate05LargeScaleFlatAsync();

        Console.WriteLine("🎉 C# Parquet.Net test dataset generation complete!");
    }

    private static async Task Generate01SmallFlatPrimitivesAsync()
    {
        const int count = 100;
        var ids = Enumerable.Range(0, count).ToArray();
        var names = ids.Select(i => $"user_{i}").ToArray();
        var scores = ids.Select(i => (i * 1.5) % 100.0).ToArray();
        var isActive = ids.Select(i => i % 2 == 0).ToArray();
        var timestamps = ids.Select(i => 1700000000000L + (i * 1000L)).ToArray();

        var idField = new DataField<int>("id");
        var nameField = new DataField<string>("name");
        var scoreField = new DataField<double>("score");
        var activeField = new DataField<bool>("is_active");
        var timestampField = new DataField<long>("created_at_ms");

        var schema = new ParquetSchema(idField, nameField, scoreField, activeField, timestampField);

        string filePath = Path.Combine(V3OutputDir, "01_small_flat_primitives.parquet");
        await using (var stream = System.IO.File.Create(filePath))
        {
            await using (var writer = await Parquet.ParquetWriter.CreateAsync(schema, stream))
            {
                using (var groupWriter = writer.CreateRowGroup())
                {
                    await groupWriter.WriteAsync<int>(idField, ids);
                    await groupWriter.WriteAsync(nameField, names);
                    await groupWriter.WriteAsync<double>(scoreField, scores);
                    await groupWriter.WriteAsync<bool>(activeField, isActive);
                    await groupWriter.WriteAsync<long>(timestampField, timestamps);
                }
            }
        }

        Console.WriteLine($"  [Parquet.Net] Generated {filePath} ({count} rows)");
    }

    private static async Task Generate02MediumNullableTypesAsync()
    {
        const int count = 10_000;
        var ids = Enumerable.Range(0, count).ToArray();
        
        var nullableInts = ids.Select(i => i % 5 == 0 ? (int?)null : i * 10).ToArray();
        var nullableDoubles = ids.Select(i => i % 5 == 0 ? (double?)null : (i * 3.14159) % 1000.0).ToArray();
        var nullableStrings = ids.Select(i => i % 5 == 0 ? null : $"str_val_{i}").ToArray();
        var nullableBools = ids.Select(i => i % 5 == 0 ? (bool?)null : (i % 3 == 0)).ToArray();

        var idField = new DataField<int>("id");
        var intField = new DataField<int?>("nullable_int");
        var doubleField = new DataField<double?>("nullable_double");
        var stringField = new DataField<string?>("nullable_string");
        var boolField = new DataField<bool?>("nullable_bool");

        var schema = new ParquetSchema(idField, intField, doubleField, stringField, boolField);

        string filePath = Path.Combine(V3OutputDir, "02_medium_nullable_types.parquet");
        await using (var stream = System.IO.File.Create(filePath))
        {
            await using (var writer = await Parquet.ParquetWriter.CreateAsync(schema, stream))
            {
                using (var groupWriter = writer.CreateRowGroup())
                {
                    await groupWriter.WriteAsync<int>(idField, ids);
                    await groupWriter.WriteAsync<int>(intField, nullableInts);
                    await groupWriter.WriteAsync<double>(doubleField, nullableDoubles);
                    await groupWriter.WriteAsync(stringField, nullableStrings);
                    await groupWriter.WriteAsync<bool>(boolField, nullableBools);
                }
            }
        }

        Console.WriteLine($"  [Parquet.Net] Generated {filePath} ({count} rows)");
    }

    private static async Task Generate03ComplexDecimalsGuidsAsync()
    {
        const int count = 5_000;
        var ids = Enumerable.Range(0, count).ToArray();

        var guidStrs = ids.Select(i => new Guid((i + 1), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0).ToString()).ToArray();
        var decimals = ids.Select(i => (decimal)((i * 123.4567) % 99999.9999)).ToArray();
        var timestampsUs = ids.Select(i => 1700000000000L + (i * 100000L)).ToArray();
        var categories = ids.Select(i => i % 4).ToArray();

        var idField = new DataField<int>("id");
        var guidField = new DataField<string>("guid_str");
        var decimalField = new DecimalDataField("amount", 18, 4);
        var timestampField = new DataField<long>("timestamp_us");
        var categoryField = new DataField<int>("category");

        var schema = new ParquetSchema(idField, guidField, decimalField, timestampField, categoryField);

        string filePath = Path.Combine(V3OutputDir, "03_complex_decimals_guids.parquet");
        await using (var stream = System.IO.File.Create(filePath))
        {
            await using (var writer = await Parquet.ParquetWriter.CreateAsync(schema, stream))
            {
                using (var groupWriter = writer.CreateRowGroup())
                {
                    await groupWriter.WriteAsync<int>(idField, ids);
                    await groupWriter.WriteAsync(guidField, guidStrs);
                    await groupWriter.WriteAsync<decimal>(decimalField, decimals);
                    await groupWriter.WriteAsync<long>(timestampField, timestampsUs);
                    await groupWriter.WriteAsync<int>(categoryField, categories);
                }
            }
        }

        Console.WriteLine($"  [Parquet.Net] Generated {filePath} ({count} rows)");
    }

    private static async Task Generate05LargeScaleFlatAsync()
    {
        const int count = 100_000;
        var ids = Enumerable.Range(0, count).Select(i => (long)i).ToArray();
        var payloads = Enumerable.Range(0, count).Select(i => $"payload_data_string_buffer_segment_{i % 500}").ToArray();
        var valA = Enumerable.Range(0, count).Select(i => i * 7).ToArray();
        var valB = Enumerable.Range(0, count).Select(i => i * 0.123456789).ToArray();
        var isValid = Enumerable.Range(0, count).Select(i => i % 7 != 0).ToArray();

        var idField = new DataField<long>("id");
        var payloadField = new DataField<string>("payload");
        var valAField = new DataField<int>("val_a");
        var valBField = new DataField<double>("val_b");
        var validField = new DataField<bool>("is_valid");

        var schema = new ParquetSchema(idField, payloadField, valAField, valBField, validField);

        string filePath = Path.Combine(V3OutputDir, "05_large_scale_flat.parquet");
        await using (var stream = System.IO.File.Create(filePath))
        {
            await using (var writer = await Parquet.ParquetWriter.CreateAsync(schema, stream))
            {
                using (var groupWriter = writer.CreateRowGroup())
                {
                    await groupWriter.WriteAsync<long>(idField, ids);
                    await groupWriter.WriteAsync(payloadField, payloads);
                    await groupWriter.WriteAsync<int>(valAField, valA);
                    await groupWriter.WriteAsync<double>(valBField, valB);
                    await groupWriter.WriteAsync<bool>(validField, isValid);
                }
            }
        }

        Console.WriteLine($"  [Parquet.Net] Generated {filePath} ({count} rows)");
    }
}
