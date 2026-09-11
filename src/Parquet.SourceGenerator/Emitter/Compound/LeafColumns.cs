using System;
using System.Collections.Generic;
using System.Linq;
using Parquet.SourceGenerator.Models;

namespace Parquet.SourceGenerator.Emitter.Compound;

/// <summary>
/// One leaf column of the flattened emission plan. A flat property produces exactly one column
/// whose <see cref="Slot"/> equals its property index and whose <see cref="SchemaPath"/> is
/// empty, so all-leaf models reproduce the pre-#176 numbering — and emitted output — verbatim.
/// </summary>
internal sealed class LeafColumn
{
    public int Slot { get; set; }
    public PropertyModel Leaf { get; set; } = null!;

    /// <summary>Index into TargetClassModel.Properties of the root member owning this leaf.</summary>
    public int RootPropertyIndex { get; set; }

    /// <summary>
    /// Field-index chain from the schema root down to this leaf: one entry per compound
    /// ancestor plus the leaf's own index within its parent group. Empty for a flat column.
    /// </summary>
    public int[] SchemaPath { get; set; } = [];

    /// <summary>
    /// def == MaxDef ⇔ the leaf carries a value. Every struct ancestor contributes one rung
    /// (groups are optional regardless of the C# annotation — docs/15 §2.4) and the leaf's own
    /// optionality another.
    /// </summary>
    public int MaxDef { get; set; }

    /// <summary>C# member chain from the row item to the value ("Ship", "City").</summary>
    public string[] MemberChain { get; set; } = [];

    /// <summary>Per ancestor (outermost first): whether it is a C# value type (never writes def 0).</summary>
    public bool[] AncestorIsValueType { get; set; } = [];

    /// <summary>
    /// Per chain step: the step is a value-type struct member declared <c>T?</c>. Such a step
    /// still writes through <c>.Value</c> after the presence test (a local of type <c>T?</c>
    /// never flow-sugars into member access), unlike a reference step.
    /// </summary>
    public bool[] AncestorNullableValueStep { get; set; } = [];

    /// <summary>Per ancestor (outermost first): def ≥ threshold ⇔ that ancestor exists in the row.</summary>
    public int[] AncestorPresenceThresholds { get; set; } = [];

    public int AncestorCount => SchemaPath.Length;
    public bool IsCompound => SchemaPath.Length > 0;

    /// <summary>Row-level list/array member (M3a): values pack into a lane, levels carry rep.</summary>
    public bool IsListLeaf { get; set; }

    /// <summary>List members: def ≥ this rung ⇔ the list exists (empty counts as existing).</summary>
    public int ListPresenceRung { get; set; }

    /// <summary>List members: def ≥ this rung ⇔ an element occupies this entry.</summary>
    public int ListElementRung { get; set; }

    /// <summary>List members: whether the C# member was annotated nullable (affects the bang).</summary>
    public bool MemberAnnotatedNullable { get; set; }

    /// <summary>List members: the member name read off the row item (element lanes key off it).</summary>
    public string ListMemberName { get; set; } = "";

    /// <summary>List members: whether the C# member is an array (lanes materialize via ToArray).</summary>
    public bool ListMemberIsArray { get; set; }

    /// <summary>Packed buffer / WriteAllPartsAsync generic argument for this leaf.</summary>
    public string PackedType => CompoundBuffers.GetPackedType(Leaf);

    /// <summary>Cast-chain expression reaching this leaf's DataField from the static Schema.</summary>
    public string SchemaAccessor
    {
        get
        {
            if (IsListLeaf)
                return $"((global::Parquet.Schema.DataField)((global::Parquet.Schema.ListField)Schema.Fields[{RootPropertyIndex}]).Item)";
            string cur = $"Schema.Fields[{RootPropertyIndex}]";
            for (int i = 0; i < SchemaPath.Length; i++)
            {
                cur = $"((global::Parquet.Schema.StructField){cur}).Fields[{SchemaPath[i]}]";
            }
            return $"(global::Parquet.Schema.DataField){cur}";
        }
    }
}

/// <summary>
/// A struct node to reconstruct per row group on the read path.
/// </summary>
internal sealed class StructNode
{
    public int Id { get; set; }
    public string ArrayVar => $"objs_{Id}";

    /// <summary>Fully-qualified CLR type ("global::App.Address").</summary>
    public string ClrType { get; set; } = "";

    /// <summary>Value-type nodes reconstruct into non-nullable arrays (absent = default).</summary>
    public bool IsValueType { get; set; }

    /// <summary>C# member name on the parent (another node, or the row).</summary>
    public string MemberName { get; set; } = "";

    /// <summary>Whether the parent member was annotated nullable (affects the bang on assignment).</summary>
    public bool MemberAnnotatedNullable { get; set; }

    /// <summary>Parent node id, or -1 when attached directly to the row.</summary>
    public int ParentId { get; set; }

    /// <summary>Depth of this node in its member chain (0 for a root member's struct).</summary>
    public int Depth { get; set; }

    /// <summary>Direct leaf columns, schema order.</summary>
    public List<LeafColumn> Leaves { get; } = [];

    /// <summary>Child node ids, schema order.</summary>
    public List<int> ChildIds { get; } = [];

    /// <summary>First DFS leaf under this node, used for the presence check.</summary>
    public LeafColumn? PresenceLeaf { get; set; }

    public int PresenceThreshold => PresenceLeaf!.AncestorPresenceThresholds[Depth];

    /// <summary>Element type of the per-row object array (value-type nodes cannot hold nulls).</summary>
    public string ArrayElementType => IsValueType ? ClrType : $"{ClrType}?";
}

/// <summary>
/// The flattened column plan + struct reconstruction tree for one model. Built per emission
/// (parse time only — never in generated code); an all-leaf model yields the identity plan and
/// every consumer falls back to the pre-#176 code paths.
/// </summary>
internal sealed class EmissionPlan
{
    public LeafColumn[] Columns { get; set; } = [];
    public StructNode[] Nodes { get; set; } = [];

    /// <summary>Root-member property index → its top struct node id, or -1.</summary>
    public int[] RootNodeByProperty { get; set; } = [];

    /// <summary>Leaf-property index → its column slot, or -1 for compound members.</summary>
    public int[] ColumnSlotByProperty { get; set; } = [];

    public bool HasCompound => Nodes.Length > 0 || Columns.Any(c => c.IsListLeaf);

    public static EmissionPlan For(TargetClassModel model)
    {
        var columns = new List<LeafColumn>();
        var nodes = new List<StructNode>();
        var rootNodeByProperty = new int[model.Properties.Length];
        var columnSlotByProperty = new int[model.Properties.Length];

        for (int root = 0; root < model.Properties.Length; root++)
        {
            PropertyModel prop = model.Properties[root];
            if (prop.Kind == PropertyKind.List)
            {
                // M3a: row-level list/array of a leaf element. The element model carries the
                // leaf's own optionality; the group ladder starts one rung above the row base.
                rootNodeByProperty[root] = -1;
                columnSlotByProperty[root] = columns.Count;
                PropertyModel element = prop.Element!;
                columns.Add(
                    new LeafColumn
                    {
                        Slot = columns.Count,
                        Leaf = element,
                        RootPropertyIndex = root,
                        SchemaPath = [],
                        MaxDef = 2 + (element.IsNullable ? 1 : 0),
                        MemberChain = [prop.Name],
                        AncestorIsValueType = [],
                        AncestorPresenceThresholds = [],
                        IsListLeaf = true,
                        ListPresenceRung = 1,
                        ListElementRung = 2,
                        MemberAnnotatedNullable = prop.IsNullable,
                        ListMemberName = prop.Name,
                        ListMemberIsArray = prop.TypeName.EndsWith("[]", StringComparison.Ordinal),
                    }
                );
                continue;
            }
            if (prop.Kind != PropertyKind.Struct)
            {
                rootNodeByProperty[root] = -1;
                columnSlotByProperty[root] = columns.Count;
                columns.Add(
                    new LeafColumn
                    {
                        Slot = columns.Count,
                        Leaf = prop,
                        RootPropertyIndex = root,
                        SchemaPath = [],
                        MaxDef = prop.IsNullable ? 1 : 0,
                        MemberChain = [prop.Name],
                        AncestorIsValueType = [],
                        AncestorPresenceThresholds = [],
                    }
                );
                continue;
            }

            var top = new StructNode
            {
                Id = nodes.Count,
                ClrType = prop.TypeName.TrimEnd('?'),
                IsValueType = prop.CompoundIsValueType,
                MemberName = prop.Name,
                ParentId = -1,
                Depth = 0,
                MemberAnnotatedNullable = prop.IsNullable,
            };
            nodes.Add(top);
            rootNodeByProperty[root] = top.Id;
            columnSlotByProperty[root] = -1;
            WalkStruct(
                prop,
                top,
                root,
                schemaPrefix: [],
                depth: 0,
                valueTypes: [prop.CompoundIsValueType],
                nullableValueSteps: [prop.IsNullable && prop.CompoundIsValueType],
                memberPrefix: [prop.Name],
                columns,
                nodes
            );
        }

        return new EmissionPlan
        {
            Columns = columns.ToArray(),
            Nodes = nodes.ToArray(),
            RootNodeByProperty = rootNodeByProperty,
            ColumnSlotByProperty = columnSlotByProperty,
        };
    }

    private static void WalkStruct(
        PropertyModel structProp,
        StructNode node,
        int rootPropertyIndex,
        int[] schemaPrefix,
        int depth,
        bool[] valueTypes,
        bool[] nullableValueSteps,
        string[] memberPrefix,
        List<LeafColumn> columns,
        List<StructNode> nodes
    )
    {
        for (int c = 0; c < structProp.Children.Length; c++)
        {
            PropertyModel child = structProp.Children[c];
            int[] childPath = [.. schemaPrefix, c];
            string[] chain = [.. memberPrefix, child.Name];

            if (child.Kind == PropertyKind.Struct)
            {
                var childNode = new StructNode
                {
                    Id = nodes.Count,
                    ClrType = child.TypeName.TrimEnd('?'),
                    IsValueType = child.CompoundIsValueType,
                    MemberName = child.Name,
                    MemberAnnotatedNullable = child.IsNullable,
                    ParentId = node.Id,
                    // This node occupies ancestor position `depth + 1` in every
                    // descendant leaf's thresholds array (the parent holds `depth`).
                    Depth = depth + 1,
                };
                nodes.Add(childNode);
                node.ChildIds.Add(childNode.Id);
                WalkStruct(
                    child,
                    childNode,
                    rootPropertyIndex,
                    childPath,
                    depth + 1,
                    [.. valueTypes, child.CompoundIsValueType],
                    [.. nullableValueSteps, child.IsNullable && child.CompoundIsValueType],
                    chain,
                    columns,
                    nodes
                );
                continue;
            }

            // Leaf. Rungs: one per ancestor struct (all optional groups, docs/15 §2.4) plus
            // the leaf's own optionality. Presence threshold for ancestor k: the def a row
            // carries when that ancestor exists and everything below it is absent.
            int ancestors = childPath.Length;
            int maxDef = ancestors + (child.IsNullable ? 1 : 0);
            var thresholds = new int[ancestors];
            for (int a = 0; a < ancestors; a++)
            {
                int rungsBelow = (ancestors - 1 - a) + (child.IsNullable ? 1 : 0);
                thresholds[a] = maxDef - rungsBelow;
            }

            var column = new LeafColumn
            {
                Slot = columns.Count,
                Leaf = child,
                RootPropertyIndex = rootPropertyIndex,
                SchemaPath = childPath,
                MaxDef = maxDef,
                MemberChain = chain,
                AncestorIsValueType = valueTypes,
                AncestorNullableValueStep = nullableValueSteps,
                AncestorPresenceThresholds = thresholds,
            };
            columns.Add(column);
            node.Leaves.Add(column);

            // Node presence (for reconstruction) keys off the first DFS leaf beneath it;
            // valid writers keep every leaf's ladder consistent.
            for (
                StructNode? cur = node;
                cur is not null;
                cur = cur.ParentId >= 0 ? nodes[cur.ParentId] : null
            )
            {
                if (cur.PresenceLeaf is null)
                    cur.PresenceLeaf = column;
                if (cur.ParentId < 0)
                    break;
            }
        }
    }

    /// <summary>Nodes in reconstruction order: children before parents.</summary>
    public IEnumerable<StructNode> NodesInReconstructionOrder()
    {
        // Creation order is strictly parent-before-child, so reversed is child-before-parent.
        for (int i = Nodes.Length - 1; i >= 0; i--)
            yield return Nodes[i];
    }
}
