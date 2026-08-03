using System;

namespace Parquet.SourceGenerator;

/// <summary>
/// Specifies compression method for Parquet serialization.
/// </summary>
public enum ParquetCompressionMethod
{
    /// <summary>
    /// No compression (uncompressed raw bytes).
    /// </summary>
    None = 0,

    /// <summary>
    /// Snappy compression (default).
    /// </summary>
    Snappy = 1,

    /// <summary>
    /// Gzip compression.
    /// </summary>
    Gzip = 2,

    /// <summary>
    /// Lz4 compression.
    /// </summary>
    Lz4 = 3,

    /// <summary>
    /// Brotli compression.
    /// </summary>
    Brotli = 4,

    /// <summary>
    /// Zstd compression.
    /// </summary>
    Zstd = 5
}

/// <summary>
/// Configurable options for Parquet source generator serialization and deserialization operations.
/// </summary>
public sealed class ParquetSerializerOptions
{
    /// <summary>
    /// Gets the global default configuration instance.
    /// </summary>
    public static ParquetSerializerOptions Default { get; } = new();

    /// <summary>
    /// Gets or sets the target row group size for batched writing operations (default is 50,000 rows).
    /// </summary>
    public int RowGroupSize { get; set; } = 50_000;

    /// <summary>
    /// Gets or sets the maximum degree of parallelism for parallel row group reading (default is -1, using Environment.ProcessorCount).
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = -1;

    /// <summary>
    /// Gets or sets the compression method to apply when creating Parquet files (default is Snappy).
    /// </summary>
    public ParquetCompressionMethod CompressionMethod { get; set; } = ParquetCompressionMethod.Snappy;

    /// <summary>
    /// Gets or sets whether to use 8-byte Int64 microsecond timestamps by default instead of 12-byte Impala Int96 timestamps.
    /// </summary>
    public bool UseMicrosecondTimestamps { get; set; }
}
