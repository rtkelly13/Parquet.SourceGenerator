using System.Text;
using Parquet.SourceGenerator.Models;

namespace Parquet.SourceGenerator.Emitter.Components;

/// <summary>
/// Composable emitter component for emitting the zero-allocation L1 StringDeduplicator struct
/// and deduplicator state setup within reading methods.
/// </summary>
internal static class StringDeduplicatorComponent
{
    public static bool HasStringProperties(TargetClassModel model) =>
        global::System.Linq.Enumerable.Any(
            model.Properties,
            p => p.Kind == PropertyKind.Primitive && p.TypeName.Contains("string")
        );

    public static void EmitStringDeduplicator(StringBuilder builder)
    {
        builder.AppendLine("    /// <summary>");
        builder.AppendLine(
            "    /// Lightweight, zero-allocation L1 string cache for deduplicating repeated string instances"
        );
        builder.AppendLine(
            "    /// across columnar reads, slashing managed heap allocations on categorical string columns."
        );
        builder.AppendLine("    /// </summary>");
        builder.AppendLine("    private struct StringDeduplicator : global::System.IDisposable");
        builder.AppendLine("    {");
        builder.AppendLine("        private string?[]? _entries;");
        builder.AppendLine("        private readonly int _mask;");
        builder.AppendLine();
        builder.AppendLine("        public StringDeduplicator(int capacity = 512)");
        builder.AppendLine("        {");
        builder.AppendLine(
            "            _entries = global::System.Buffers.ArrayPool<string?>.Shared.Rent(capacity);"
        );
        builder.AppendLine("            _mask = capacity - 1;");
        builder.AppendLine("            global::System.Array.Clear(_entries, 0, capacity);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        [global::System.Runtime.CompilerServices.MethodImpl(");
        builder.AppendLine(
            "            global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]"
        );
        builder.AppendLine("        public string? Deduplicate(string? value)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (value is null) return null;");
        builder.AppendLine("            if (value.Length == 0) return string.Empty;");
        builder.AppendLine();
        builder.AppendLine("            var entries = _entries;");
        builder.AppendLine("            if (entries is null) return value;");
        builder.AppendLine();
        builder.AppendLine("            int index = value.GetHashCode() & _mask;");
        builder.AppendLine("            string? candidate = entries[index];");
        builder.AppendLine(
            "            if (candidate is not null && string.Equals(candidate, value, global::System.StringComparison.Ordinal))"
        );
        builder.AppendLine("            {");
        builder.AppendLine("                return candidate;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            entries[index] = value;");
        builder.AppendLine("            return value;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public void Dispose()");
        builder.AppendLine("        {");
        builder.AppendLine("            var entries = _entries;");
        builder.AppendLine("            if (entries != null)");
        builder.AppendLine("            {");
        builder.AppendLine("                _entries = null;");
        builder.AppendLine(
            "                global::System.Buffers.ArrayPool<string?>.Shared.Return(entries, clearArray: true);"
        );
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
    }

    public static void EmitDeduplicatorDeclaration(
        StringBuilder builder,
        TargetClassModel model,
        string indent = "        "
    )
    {
        if (HasStringProperties(model))
        {
            builder.AppendLine(
                $"{indent}using var stringDeduplicator = new StringDeduplicator(512);"
            );
            builder.AppendLine($"{indent}bool deduplicateStrings = options.DeduplicateStrings;");
            builder.AppendLine();
        }
    }
}
