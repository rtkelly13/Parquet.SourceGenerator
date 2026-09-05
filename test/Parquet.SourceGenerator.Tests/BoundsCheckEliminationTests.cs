using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Parquet.SourceGenerator;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

[ParquetSerializable]
public partial record BoundsCheckRecord
{
    [ParquetColumn("id")]
    public int Id { get; init; }

    [ParquetColumn("amount")]
    public long Amount { get; init; }

    [ParquetColumn("ratio")]
    public double Ratio { get; init; }

    [ParquetColumn("optional_value")]
    public int? OptionalValue { get; init; }

    [ParquetColumn("created_at")]
    public DateTime CreatedAt { get; init; }

    [ParquetColumn("name")]
    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// Issue #144: the List and Array extraction fast paths in generated
/// WriteParquetRowGroupAsync must lower to zero bounds-checked array element
/// accesses. The emitter produces ExtractSpan/ExtractArray local functions that
/// address every source and destination slot through Unsafe.Add over raw refs
/// (MemoryMarshal.GetReference / GetArrayDataReference), so the compiled IL of
/// those local functions must contain no ldelema and no ldelem/stelem family
/// opcodes, and must positively reference Unsafe.Add.
/// </summary>
public sealed class BoundsCheckEliminationTests
{
    private static readonly OpCode[] SingleByteOpCodes = new OpCode[256];
    private static readonly OpCode[] MultiByteOpCodes = new OpCode[256];

    private static readonly HashSet<string> ForbiddenArrayAccessOpCodeNames = new()
    {
        "ldelema",
        "ldelem",
        "ldelem.i",
        "ldelem.u1",
        "ldelem.u2",
        "ldelem.u4",
        "ldelem.i1",
        "ldelem.i2",
        "ldelem.i4",
        "ldelem.ref",
        "ldelem.any",
        "stelem",
        "stelem.i",
        "stelem.i1",
        "stelem.i2",
        "stelem.i4",
        "stelem.ref",
        "stelem.any",
    };

    static BoundsCheckEliminationTests()
    {
        foreach (
            FieldInfo fi in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        )
        {
            if (fi.GetValue(null) is OpCode op && op.OpCodeType != OpCodeType.Nternal)
            {
                if ((ushort)op.Value < 0x100)
                {
                    SingleByteOpCodes[(byte)op.Value] = op;
                }
                else if (((ushort)op.Value >> 8) == 0xFE)
                {
                    MultiByteOpCodes[(byte)op.Value] = op;
                }
            }
        }
    }

    // Local functions inside the generated WriteParquetRowGroupAsync are compiled as
    // static methods whose names are mangled to "<MethodName>g__LocalName|N_M".
    private static IEnumerable<MethodInfo> FindLocalFunctions(params string[] names)
    {
        return EnumerateMethods(typeof(BoundsCheckRecordParquetExtensions))
            .Where(m => names.Any(n => m.Name.Contains("g__" + n, StringComparison.Ordinal)));
    }

    private static IEnumerable<MethodInfo> EnumerateMethods(Type type)
    {
        const BindingFlags flags =
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        foreach (MethodInfo method in type.GetMethods(flags))
        {
            yield return method;
        }

        foreach (Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            foreach (MethodInfo method in EnumerateMethods(nested))
            {
                yield return method;
            }
        }
    }

    private static (int ForbiddenCount, int UnsafeAddCalls) Inspect(MethodInfo method)
    {
        MethodBody? body = method.GetMethodBody();
        Assert.NotNull(body);
        byte[] il = body!.GetILAsByteArray() ?? Array.Empty<byte>();

        int forbidden = 0;
        int unsafeAddCalls = 0;
        int pos = 0;
        while (pos < il.Length)
        {
            byte b = il[pos++];
            OpCode opcode = b switch
            {
                0xFE when pos < il.Length => MultiByteOpCodes[il[pos++]],
                _ => SingleByteOpCodes[b],
            };

            if (
                opcode.Name is string opcodeName
                && ForbiddenArrayAccessOpCodeNames.Contains(opcodeName)
            )
            {
                forbidden++;
            }

            if (opcode.OperandType == OperandType.InlineMethod && pos + 4 <= il.Length)
            {
                int token = BitConverter.ToInt32(il, pos);
                try
                {
                    MethodBase? called = method.Module.ResolveMethod(token);
                    if (
                        called is not null
                        && called.Name == "Add"
                        && called.DeclaringType?.FullName
                            == "System.Runtime.CompilerServices.Unsafe"
                    )
                    {
                        unsafeAddCalls++;
                    }
                }
                catch
                {
                    // Non-method token (e.g. generic type arg encoded differently); ignore.
                }
            }

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
                case OperandType.InlineI:
                case OperandType.InlineBrTarget:
                case OperandType.InlineMethod:
                case OperandType.InlineField:
                case OperandType.InlineType:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineSig:
                case OperandType.ShortInlineR:
                    pos += 4;
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
                    break;
            }
        }

        return (forbidden, unsafeAddCalls);
    }

    [Fact]
    public void GeneratedExtractionLocalFunctionsExist()
    {
        var methods = FindLocalFunctions("ExtractSpan", "ExtractArray").ToList();

        Assert.Contains(methods, m => m.Name.Contains("g__ExtractSpan", StringComparison.Ordinal));
        Assert.Contains(methods, m => m.Name.Contains("g__ExtractArray", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("ExtractSpan")]
    [InlineData("ExtractArray")]
    public void ExtractionLoopEmitsZeroBoundsCheckedElementAccesses(string localFunctionName)
    {
        MethodInfo method = Assert.Single(
            FindLocalFunctions(localFunctionName),
            m => m.Name.Contains("g__" + localFunctionName, StringComparison.Ordinal)
        );

        (int forbidden, int unsafeAddCalls) = Inspect(method);

        Assert.Equal(0, forbidden);
        Assert.True(
            unsafeAddCalls > 0,
            $"{localFunctionName} should address elements via Unsafe.Add but no calls were found."
        );
    }
}
