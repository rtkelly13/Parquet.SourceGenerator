using System;
using Parquet.SourceGenerator.Emitter.Compound;
using Parquet.SourceGenerator.Models;

namespace Parquet.SourceGenerator.Emitter.Arrow;

/// <summary>
/// The Arrow logical type a generated leaf column projects onto.
/// </summary>
/// <remarks>
/// One entry per Parquet leaf kind the bridge can express. Nested kinds
/// (<see cref="PropertyKind.Struct"/>, <see cref="PropertyKind.List"/>, <see cref="PropertyKind.Map"/>)
/// deliberately have no member: nested export waits on the recursive model of #176, and a model
/// containing one simply does not get an Arrow partial rather than getting a wrong one.
/// </remarks>
internal enum ArrowMapping
{
    /// <summary><c>bool</c> → <c>Apache.Arrow.BooleanArray</c> (bit-packed values buffer).</summary>
    Boolean,

    /// <summary><c>sbyte</c> → <c>Int8Array</c>.</summary>
    Int8,

    /// <summary><c>byte</c> → <c>UInt8Array</c>.</summary>
    UInt8,

    /// <summary><c>short</c> → <c>Int16Array</c>.</summary>
    Int16,

    /// <summary><c>ushort</c> → <c>UInt16Array</c>.</summary>
    UInt16,

    /// <summary><c>int</c> → <c>Int32Array</c>.</summary>
    Int32,

    /// <summary><c>uint</c> → <c>UInt32Array</c>.</summary>
    UInt32,

    /// <summary><c>long</c> → <c>Int64Array</c>.</summary>
    Int64,

    /// <summary><c>ulong</c> → <c>UInt64Array</c>.</summary>
    UInt64,

    /// <summary><c>float</c> → <c>FloatArray</c>.</summary>
    Float,

    /// <summary><c>double</c> → <c>DoubleArray</c>.</summary>
    Double,

    /// <summary><c>string</c> → <c>StringArray</c> (utf8: int32 offsets + byte data).</summary>
    Utf8,

    /// <summary><c>byte[]</c> → <c>BinaryArray</c>.</summary>
    Binary,

    /// <summary><c>decimal</c> → <c>Decimal128Array</c>, precision/scale off the resolved DecimalDataField.</summary>
    Decimal128,

    /// <summary><c>DateTime</c> → <c>TimestampArray</c>, unit off the resolved DateTimeDataField.</summary>
    Timestamp,

    /// <summary><c>DateOnly</c> → <c>Date32Array</c> (days since the Unix epoch).</summary>
    Date32,

    /// <summary><c>TimeOnly</c> → <c>Time64Array</c> (microseconds since midnight).</summary>
    Time64,

    /// <summary><c>TimeSpan</c> → <c>DurationArray</c> (milliseconds — Parquet.Net's TimeSpan encoding).</summary>
    Duration,

    /// <summary><c>Guid</c> → <c>FixedSizeBinaryArray</c> of width 16, RFC 4122 byte order.</summary>
    FixedSizeBinary16,

    /// <summary>Parquet.Net's <c>Interval</c> → <c>MonthDayNanosecondIntervalArray</c>.</summary>
    MonthDayNanosecondInterval,
}

/// <summary>
/// The shared half of the Arrow bridge: the leaf-kind → Arrow-type mapping table plus the
/// conditional-emission gate both bridge halves key off.
/// </summary>
/// <remarks>
/// <para>
/// Issue #178 (RecordBatch export) and issue #177 (RecordBatch ingestion) are two directions over
/// one table. #177 had not landed when the export experiment was written, so the table and the gate
/// arrive here; ingestion is expected to consume them unchanged rather than restate them.
/// </para>
/// <para>
/// The gate is a plain "does the consumer compilation reference Apache.Arrow" test. When it is
/// false no Arrow-typed code exists anywhere in the generated output, so a consumer that never
/// references Apache.Arrow inherits no Arrow dependency — which is the whole reason the bridge is
/// emitted conditionally rather than shipped in a package.
/// </para>
/// </remarks>
internal static class ArrowMappingComponent
{
    /// <summary>
    /// The metadata name the gate probes for. <c>RecordBatch</c> rather than the assembly name so
    /// that a facade or a trimmed reference that cannot actually carry the bridge fails the gate.
    /// </summary>
    public const string GateTypeMetadataName = "Apache.Arrow.RecordBatch";

    /// <summary>
    /// Maps one leaf property onto its Arrow array kind. Returns false for anything the bridge
    /// cannot express, which is what keeps a partially-mapped model from emitting at all.
    /// </summary>
    public static bool TryMap(PropertyModel prop, out ArrowMapping mapping)
    {
        switch (prop.Kind)
        {
            case PropertyKind.Decimal:
                mapping = ArrowMapping.Decimal128;
                return true;
            case PropertyKind.DateTime:
                mapping = ArrowMapping.Timestamp;
                return true;
            case PropertyKind.DateOnly:
                mapping = ArrowMapping.Date32;
                return true;
            case PropertyKind.TimeOnly:
                mapping = ArrowMapping.Time64;
                return true;
            case PropertyKind.TimeSpan:
                mapping = ArrowMapping.Duration;
                return true;
            case PropertyKind.Guid:
                mapping = ArrowMapping.FixedSizeBinary16;
                return true;
            case PropertyKind.ByteArray:
                mapping = ArrowMapping.Binary;
                return true;
            case PropertyKind.Enum:
                return TryMapScalar(prop.EnumUnderlyingTypeName ?? "int", out mapping);
            case PropertyKind.Primitive:
                return TryMapScalar(prop.TypeName.TrimEnd('?'), out mapping);
            default:
                mapping = default;
                return false;
        }
    }

    private static bool TryMapScalar(string typeName, out ArrowMapping mapping)
    {
        mapping = typeName switch
        {
            "bool" => ArrowMapping.Boolean,
            "sbyte" => ArrowMapping.Int8,
            "byte" => ArrowMapping.UInt8,
            "short" => ArrowMapping.Int16,
            "ushort" => ArrowMapping.UInt16,
            "int" => ArrowMapping.Int32,
            "uint" => ArrowMapping.UInt32,
            "long" => ArrowMapping.Int64,
            "ulong" => ArrowMapping.UInt64,
            "float" => ArrowMapping.Float,
            "double" => ArrowMapping.Double,
            "string" => ArrowMapping.Utf8,
            // Parquet.Net's INTERVAL primitive: months + days + millis, which is exactly Arrow's
            // month_day_nano interval once the millisecond part is widened to nanoseconds.
            "global::Parquet.File.Values.Primitives.Interval"
            or "Parquet.File.Values.Primitives.Interval" => ArrowMapping.MonthDayNanosecondInterval,
            _ => (ArrowMapping)(-1),
        };
        return (int)mapping >= 0;
    }

    /// <summary>
    /// Whether the whole model can cross the bridge. Nested members and any unmapped leaf make the
    /// answer no, and the Arrow partial is then not emitted for that type at all.
    /// </summary>
    public static bool IsExportable(TargetClassModel model)
    {
        if (model.Properties.Length == 0)
            return false;

        if (EmissionPlan.For(model).HasCompound)
            return false;

        for (int i = 0; i < model.Properties.Length; i++)
        {
            if (!TryMap(model.Properties[i], out _))
                return false;
        }

        return true;
    }

    /// <summary>
    /// The concrete <c>Apache.Arrow</c> array type a column materialises as.
    /// </summary>
    public static string ArrayTypeName(ArrowMapping mapping) =>
        mapping switch
        {
            ArrowMapping.Boolean => "global::Apache.Arrow.BooleanArray",
            ArrowMapping.Int8 => "global::Apache.Arrow.Int8Array",
            ArrowMapping.UInt8 => "global::Apache.Arrow.UInt8Array",
            ArrowMapping.Int16 => "global::Apache.Arrow.Int16Array",
            ArrowMapping.UInt16 => "global::Apache.Arrow.UInt16Array",
            ArrowMapping.Int32 => "global::Apache.Arrow.Int32Array",
            ArrowMapping.UInt32 => "global::Apache.Arrow.UInt32Array",
            ArrowMapping.Int64 => "global::Apache.Arrow.Int64Array",
            ArrowMapping.UInt64 => "global::Apache.Arrow.UInt64Array",
            ArrowMapping.Float => "global::Apache.Arrow.FloatArray",
            ArrowMapping.Double => "global::Apache.Arrow.DoubleArray",
            ArrowMapping.Utf8 => "global::Apache.Arrow.StringArray",
            ArrowMapping.Binary => "global::Apache.Arrow.BinaryArray",
            ArrowMapping.Decimal128 => "global::Apache.Arrow.Decimal128Array",
            ArrowMapping.Timestamp => "global::Apache.Arrow.TimestampArray",
            ArrowMapping.Date32 => "global::Apache.Arrow.Date32Array",
            ArrowMapping.Time64 => "global::Apache.Arrow.Time64Array",
            ArrowMapping.Duration => "global::Apache.Arrow.DurationArray",
            ArrowMapping.MonthDayNanosecondInterval =>
                "global::Apache.Arrow.MonthDayNanosecondIntervalArray",
            _ => "global::Apache.Arrow.Arrays.FixedSizeBinaryArray",
        };

    /// <summary>
    /// The CLR type stored in an Arrow fixed-width values buffer for this mapping, or null when the
    /// mapping is not a plain fixed-width copy (bool, utf8, binary, decimal, guid).
    /// </summary>
    public static string? FixedWidthStorageType(ArrowMapping mapping) =>
        mapping switch
        {
            ArrowMapping.Int8 => "sbyte",
            ArrowMapping.UInt8 => "byte",
            ArrowMapping.Int16 => "short",
            ArrowMapping.UInt16 => "ushort",
            ArrowMapping.Int32 => "int",
            ArrowMapping.UInt32 => "uint",
            ArrowMapping.Int64 => "long",
            ArrowMapping.UInt64 => "ulong",
            ArrowMapping.Float => "float",
            ArrowMapping.Double => "double",
            ArrowMapping.Timestamp => "long",
            ArrowMapping.Date32 => "int",
            ArrowMapping.Time64 => "long",
            ArrowMapping.Duration => "long",
            ArrowMapping.MonthDayNanosecondInterval =>
                "global::Apache.Arrow.Scalars.MonthDayNanosecondInterval",
            _ => null,
        };

    /// <summary>
    /// A C# expression producing the Arrow <c>IArrowType</c> for this column, evaluated against the
    /// <c>DataField</c> resolved from the file at read time (hence precision, scale and timestamp
    /// unit come from the actual schema rather than from a compile-time guess).
    /// </summary>
    public static string ArrowTypeExpression(ArrowMapping mapping, string fieldExpr) =>
        mapping switch
        {
            ArrowMapping.Boolean => "global::Apache.Arrow.Types.BooleanType.Default",
            ArrowMapping.Int8 => "global::Apache.Arrow.Types.Int8Type.Default",
            ArrowMapping.UInt8 => "global::Apache.Arrow.Types.UInt8Type.Default",
            ArrowMapping.Int16 => "global::Apache.Arrow.Types.Int16Type.Default",
            ArrowMapping.UInt16 => "global::Apache.Arrow.Types.UInt16Type.Default",
            ArrowMapping.Int32 => "global::Apache.Arrow.Types.Int32Type.Default",
            ArrowMapping.UInt32 => "global::Apache.Arrow.Types.UInt32Type.Default",
            ArrowMapping.Int64 => "global::Apache.Arrow.Types.Int64Type.Default",
            ArrowMapping.UInt64 => "global::Apache.Arrow.Types.UInt64Type.Default",
            ArrowMapping.Float => "global::Apache.Arrow.Types.FloatType.Default",
            ArrowMapping.Double => "global::Apache.Arrow.Types.DoubleType.Default",
            ArrowMapping.Utf8 => "global::Apache.Arrow.Types.StringType.Default",
            ArrowMapping.Binary => "global::Apache.Arrow.Types.BinaryType.Default",
            ArrowMapping.Decimal128 => $"ArrowDecimalType({fieldExpr})",
            ArrowMapping.Timestamp => $"ArrowTimestampType({fieldExpr})",
            ArrowMapping.Date32 => "global::Apache.Arrow.Types.Date32Type.Default",
            ArrowMapping.Time64 => "ArrowTimeType()",
            ArrowMapping.Duration => "global::Apache.Arrow.Types.DurationType.Millisecond",
            ArrowMapping.MonthDayNanosecondInterval =>
                "global::Apache.Arrow.Types.IntervalType.MonthDayNanosecond",
            _ => "ArrowUuidType()",
        };

    /// <summary>
    /// Whether the mapping needs the resolved <c>DataField</c> at schema-build time (precision,
    /// scale, timestamp unit) rather than a constant singleton.
    /// </summary>
    public static bool NeedsResolvedField(ArrowMapping mapping) =>
        mapping is ArrowMapping.Decimal128 or ArrowMapping.Timestamp;

    /// <summary>
    /// Ticks per unit for a timestamp column, expressed against Arrow's <c>TimeUnit</c>.
    /// </summary>
    public static string TimestampConversionHelperName => "ToArrowTimestamp";

    /// <summary>
    /// Guards against an unmapped leaf reaching the emitter — a bug in <see cref="IsExportable"/>
    /// rather than a user error, so it throws rather than reporting a diagnostic.
    /// </summary>
    public static ArrowMapping MapOrThrow(PropertyModel prop)
    {
        if (!TryMap(prop, out ArrowMapping mapping))
        {
            throw new InvalidOperationException(
                $"Property '{prop.Name}' of kind {prop.Kind} has no Arrow mapping; "
                    + "IsExportable should have rejected the model before emission."
            );
        }

        return mapping;
    }
}
