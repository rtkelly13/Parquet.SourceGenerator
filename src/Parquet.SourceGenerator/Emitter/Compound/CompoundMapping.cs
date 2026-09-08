using System.Linq;
using System.Text;
using Parquet.SourceGenerator.Emitter.Components;
using Parquet.SourceGenerator.Models;

namespace Parquet.SourceGenerator.Emitter.Compound;

/// <summary>
/// Compound write extraction and read reconstruction for the v6 emitter (issue #176 M2).
/// The definition-ladder conventions implemented here are the ones verified against
/// Parquet.Net 6.1.0 and PyArrow in docs/15 §1-2. v6-only by design; the classic backend's
/// equivalent lands in M5 on the 4.x conventions (docs/15 §3).
/// </summary>
internal static class CompoundMapping
{
    /// <summary>
    /// Write conversion for a compound-path leaf whose value local is known non-null after
    /// the ladder's null test: the non-nullable conversion forms, since nulls are carried
    /// by definition levels rather than the buffer.
    /// </summary>
    public static string GetCompoundLeafWriteExpression(PropertyModel leaf, string valueExpr)
    {
        string baseExpr =
            leaf.IsNullable && ValueNullableWrapper(leaf) ? $"{valueExpr}.Value" : valueExpr;
        return PropertyMappingComponent.GetWriteExpression(
            leaf with
            {
                IsNullable = false,
            },
            baseExpr
        );
    }

    /// <summary>
    /// Read conversion for a compound-path leaf: <paramref name="valueExpr"/> is a packed,
    /// definitely-present value read from the lane buffer.
    /// </summary>
    public static string GetCompoundLeafReadExpression(PropertyModel leaf, string valueExpr)
    {
        if (leaf.Kind == PropertyKind.Primitive && leaf.TypeName.Contains("string"))
            return $"{valueExpr}.ToString()";

        if (leaf.Kind == PropertyKind.ByteArray)
            return $"{valueExpr}.ToArray()";

        return PropertyMappingComponent.GetReadExpression(
            leaf with
            {
                IsNullable = false,
            },
            valueExpr
        );
    }

    /// <summary>
    /// Emits the ladder walk for one compound leaf inside an extraction loop: each ancestor is
    /// read into a local and null-tested before anything below it is dereferenced.
    /// </summary>
    public static void EmitCompoundExtraction(
        StringBuilder builder,
        LeafColumn col,
        string itemExpr,
        string indexVar,
        string prefix
    ) => EmitLadder(builder, col, itemExpr, level: 0, indexVar, prefix);

    private static void EmitLadder(
        StringBuilder builder,
        LeafColumn col,
        string curExpr,
        int level,
        string indexVar,
        string prefix
    )
    {
        int ancestors = col.MemberChain.Length - 1;

        if (level == ancestors)
        {
            // Leaf rung.
            string valueLocal = $"cv_{col.Slot}";
            string leafMember = col.MemberChain[col.MemberChain.Length - 1];
            builder.AppendLine($"{prefix}var {valueLocal} = {curExpr}.{leafMember};");

            PropertyModel leaf = col.Leaf;
            string defWrite = $"defLevels_{col.Slot}[{indexVar}]";
            string writeExpr = GetCompoundLeafWriteExpression(leaf, valueLocal);

            if (leaf.IsNullable)
            {
                builder.AppendLine($"{prefix}if ({valueLocal} is not null)");
                builder.AppendLine($"{prefix}{{");
                builder.AppendLine(
                    $"{prefix}    buffer_{col.Slot}[nonNullCount_{col.Slot}++] = {writeExpr};"
                );
                builder.AppendLine($"{prefix}    {defWrite} = {col.MaxDef};");
                builder.AppendLine($"{prefix}}}");
                builder.AppendLine($"{prefix}else {{");
                builder.AppendLine($"{prefix}    {defWrite} = {col.MaxDef - 1};");
                builder.AppendLine($"{prefix}}}");
            }
            else
            {
                builder.AppendLine(
                    $"{prefix}buffer_{col.Slot}[nonNullCount_{col.Slot}++] = {writeExpr};"
                );
                builder.AppendLine($"{prefix}{defWrite} = {col.MaxDef};");
            }
            return;
        }

        string local = $"ca_{col.Slot}_{level}";
        string member = col.MemberChain[level];
        builder.AppendLine($"{prefix}var {local} = {curExpr}.{member};");

        if (col.AncestorIsValueType[level])
        {
            // A C# struct member can never be null: its rung is always present, no test.
            EmitLadder(builder, col, local, level + 1, indexVar, prefix);
            return;
        }

        string defWriteOuter = $"defLevels_{col.Slot}[{indexVar}]";
        builder.AppendLine($"{prefix}if ({local} is not null)");
        builder.AppendLine($"{prefix}{{");
        EmitLadder(builder, col, local, level + 1, indexVar, prefix + "    ");
        builder.AppendLine($"{prefix}}}");
        builder.AppendLine($"{prefix}else {{");
        builder.AppendLine($"{prefix}    {defWriteOuter} = {level};");
        builder.AppendLine($"{prefix}}}");
    }

    /// <summary>
    /// Emits the read-path reconstruction of every struct node in the model, children before
    /// parents: one object array per node, filled by walking the leaves' definition ladders
    /// in row order against packed-lane cursors.
    /// </summary>
    public static void EmitCompoundReconstruction(
        StringBuilder builder,
        TargetClassModel model,
        string rowCountVar,
        string indent
    )
    {
        EmissionPlan plan = EmissionPlan.For(model);
        if (!plan.HasCompound)
            return;

        foreach (LeafColumn col in plan.Columns)
        {
            if (col.IsCompound)
                builder.AppendLine($"{indent}int cursor_{col.Slot} = 0;");
        }

        foreach (StructNode node in plan.NodesInReconstructionOrder())
        {
            builder.AppendLine(
                $"{indent}var {node.ArrayVar} = new {node.ArrayElementType}[{rowCountVar}];"
            );
            builder.AppendLine(
                $"{indent}for (int ri_{node.Id} = 0; ri_{node.Id} < {rowCountVar}; ri_{node.Id}++)"
            );
            builder.AppendLine($"{indent}{{");
            LeafColumn presence = node.PresenceLeaf!;
            builder.AppendLine(
                $"{indent}    if (defLevels_{presence.Slot}[ri_{node.Id}] >= {node.PresenceThreshold})"
            );
            builder.AppendLine($"{indent}    {{");
            string p = indent + "        ";
            builder.AppendLine($"{p}var o_{node.Id} = new {node.ClrType}");
            builder.AppendLine($"{p}{{");
            foreach (int childId in node.ChildIds)
            {
                StructNode child = plan.Nodes[childId];
                string bang = child.IsValueType || child.MemberAnnotatedNullable ? "" : "!";
                builder.AppendLine(
                    $"{p}    {child.MemberName} = {child.ArrayVar}[ri_{node.Id}]{bang},"
                );
            }
            foreach (LeafColumn leafCol in node.Leaves)
            {
                string lane = $"buffer_{leafCol.Slot}[cursor_{leafCol.Slot}++]";
                string expr = GetCompoundLeafReadExpression(leafCol.Leaf, lane);
                bool nullableish =
                    leafCol.Leaf.IsNullable
                    || (
                        leafCol.Leaf.Kind == PropertyKind.Primitive
                        && leafCol.Leaf.TypeName.Contains("string")
                    )
                    || leafCol.Leaf.Kind == PropertyKind.ByteArray;
                string absent = nullableish ? "null!" : "default";
                builder.AppendLine(
                    $"{p}    {leafCol.Leaf.Name} = defLevels_{leafCol.Slot}[ri_{node.Id}] >= {leafCol.MaxDef} ? {expr} : {absent},"
                );
            }
            builder.AppendLine($"{p}}};");
            builder.AppendLine($"{p}{node.ArrayVar}[ri_{node.Id}] = o_{node.Id};");
            builder.AppendLine($"{indent}    }}");
            builder.AppendLine($"{indent}}}");
        }
    }

    /// <summary>
    /// Per-row member assignments for the root object initializer, mapping each root property
    /// to its leaf buffer lane or reconstructed struct array.
    /// </summary>
    public static void EmitRootMemberAssignments(
        StringBuilder builder,
        TargetClassModel model,
        string indexVar,
        string prefix,
        string bufferPrefix = "buffer_"
    )
    {
        EmissionPlan plan = EmissionPlan.For(model);
        for (int p = 0; p < model.Properties.Length; p++)
        {
            PropertyModel prop = model.Properties[p];
            int nodeId = plan.RootNodeByProperty[p];
            if (nodeId >= 0)
            {
                StructNode node = plan.Nodes[nodeId];
                string bang = node.IsValueType || prop.IsNullable ? "" : "!";
                builder.AppendLine($"{prefix}{prop.Name} = {node.ArrayVar}[{indexVar}]{bang},");
                continue;
            }

            int slot = plan.ColumnSlotByProperty[p];
            string readExpr = PropertyMappingComponent.GetReadExpression(
                prop,
                $"{bufferPrefix}{slot}[{indexVar}]"
            );
            builder.AppendLine($"{prefix}{prop.Name} = {readExpr},");
        }
    }

    /// <summary>
    /// Row-group materialization for compound models: reconstruction preamble then the row
    /// loop building each root object from the arrays. Mirrors the shape of
    /// PropertyMappingComponent.EmitArrayMaterialization's standard branch.
    /// </summary>
    public static void EmitArrayMaterialization(
        StringBuilder builder,
        TargetClassModel model,
        string targetArrayVar,
        string startOffsetVar,
        string rowCountVar = "rowCount",
        string indexVar = "i",
        string bufferPrefix = "buffer_",
        string indent = "                "
    )
    {
        EmitCompoundReconstruction(builder, model, rowCountVar, indent);
        builder.AppendLine(
            $"{indent}for (int {indexVar} = 0; {indexVar} < {rowCountVar}; {indexVar}++)"
        );
        builder.AppendLine($"{indent}{{");
        string targetSlot = string.IsNullOrEmpty(startOffsetVar)
            ? $"{targetArrayVar}[{indexVar}]"
            : $"{targetArrayVar}[{startOffsetVar} + {indexVar}]";
        builder.AppendLine($"{indent}    {targetSlot} = new {model.ClassName}");
        builder.AppendLine($"{indent}    {{");
        EmitRootMemberAssignments(builder, model, indexVar, indent + "        ", bufferPrefix);
        builder.AppendLine($"{indent}    }};");
        builder.AppendLine($"{indent}}}");
    }

    private static bool ValueNullableWrapper(PropertyModel leaf) =>
        !(leaf.Kind == PropertyKind.Primitive && leaf.TypeName.Contains("string"))
        && leaf.Kind != PropertyKind.ByteArray;
}
