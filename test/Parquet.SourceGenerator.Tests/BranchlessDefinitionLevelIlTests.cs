using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

/// <summary>One nullable value column.</summary>
[ParquetSerializable]
public partial record OneNullableColumnRecord
{
    [ParquetColumn("id")]
    public int Id { get; init; }

    [ParquetColumn("n0")]
    public int? N0 { get; init; }
}

/// <summary>Six nullable value columns, otherwise identical in shape.</summary>
[ParquetSerializable]
public partial record SixNullableColumnRecord
{
    [ParquetColumn("id")]
    public int Id { get; init; }

    [ParquetColumn("n0")]
    public int? N0 { get; init; }

    [ParquetColumn("n1")]
    public int? N1 { get; init; }

    [ParquetColumn("n2")]
    public int? N2 { get; init; }

    [ParquetColumn("n3")]
    public int? N3 { get; init; }

    [ParquetColumn("n4")]
    public int? N4 { get; init; }

    [ParquetColumn("n5")]
    public int? N5 { get; init; }
}

/// <summary>
/// Issue #145 acceptance criterion 1 — "conditional branch instructions eliminated for
/// definition level extraction" — checked at the IL level.
/// </summary>
/// <remarks>
/// The extraction loop necessarily keeps its own loop-control branch, so the invariant that
/// actually proves the criterion is that the conditional-branch count of the generated
/// <c>ExtractSpan</c>/<c>ExtractArray</c> local function does not grow with the number of
/// nullable columns. Before the change each nullable column contributed its own
/// <c>brfalse</c> on <c>HasValue</c>; now none do.
/// </remarks>
public sealed class BranchlessDefinitionLevelIlTests
{
    private static readonly OpCode[] SingleByteOpCodes = new OpCode[256];
    private static readonly OpCode[] MultiByteOpCodes = new OpCode[256];

    static BranchlessDefinitionLevelIlTests()
    {
        foreach (
            FieldInfo fi in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        )
        {
            if (fi.GetValue(null) is OpCode op && op.OpCodeType != OpCodeType.Nternal)
            {
                if ((ushort)op.Value < 0x100)
                    SingleByteOpCodes[(byte)op.Value] = op;
                else if (((ushort)op.Value >> 8) == 0xFE)
                    MultiByteOpCodes[(byte)op.Value] = op;
            }
        }
    }

    private static IEnumerable<MethodInfo> EnumerateMethods(Type type)
    {
        const BindingFlags Flags =
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        foreach (MethodInfo method in type.GetMethods(Flags))
            yield return method;

        foreach (Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            foreach (MethodInfo method in EnumerateMethods(nested))
                yield return method;
        }
    }

    private static MethodInfo FindLocalFunction(Type extensionsType, string name) =>
        EnumerateMethods(extensionsType)
            .Single(m => m.Name.Contains("g__" + name, StringComparison.Ordinal));

    private static int CountConditionalBranches(MethodInfo method)
    {
        MethodBody? body = method.GetMethodBody();
        Assert.NotNull(body);
        byte[] il = body!.GetILAsByteArray() ?? Array.Empty<byte>();

        int conditional = 0;
        int pos = 0;
        while (pos < il.Length)
        {
            byte b = il[pos++];
            OpCode opcode = b switch
            {
                0xFE when pos < il.Length => MultiByteOpCodes[il[pos++]],
                _ => SingleByteOpCodes[b],
            };

            if (opcode.FlowControl == FlowControl.Cond_Branch)
                conditional++;

            switch (opcode.OperandType)
            {
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineVar:
                    pos += 1;
                    break;
                case OperandType.InlineVar:
                    pos += 2;
                    break;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    pos += 8;
                    break;
                case OperandType.InlineSwitch:
                    if (pos + 4 <= il.Length)
                    {
                        int switchCount = BitConverter.ToInt32(il, pos);
                        pos += 4 + (switchCount * 4);
                    }
                    break;
                default:
                    pos += 4;
                    break;
            }
        }

        return conditional;
    }

    [Theory]
    [InlineData("ExtractSpan")]
    [InlineData("ExtractArray")]
    public void ConditionalBranchCountDoesNotGrowWithNullableColumnCount(string localFunctionName)
    {
        int one = CountConditionalBranches(
            FindLocalFunction(typeof(OneNullableColumnRecordParquetExtensions), localFunctionName)
        );
        int six = CountConditionalBranches(
            FindLocalFunction(typeof(SixNullableColumnRecordParquetExtensions), localFunctionName)
        );

        Assert.Equal(one, six);
    }

    [Theory]
    [InlineData("ExtractSpan")]
    [InlineData("ExtractArray")]
    public void ExtractionLoopKeepsOnlyItsLoopControlBranch(string localFunctionName)
    {
        int branches = CountConditionalBranches(
            FindLocalFunction(typeof(SixNullableColumnRecordParquetExtensions), localFunctionName)
        );

        // The `for (int i = 0; i < count; i++)` control edge, and nothing per column.
        Assert.Equal(1, branches);
    }
}
