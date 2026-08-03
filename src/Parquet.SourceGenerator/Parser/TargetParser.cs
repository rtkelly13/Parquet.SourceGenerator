using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Parquet.SourceGenerator.Diagnostics;
using Parquet.SourceGenerator.Models;

namespace Parquet.SourceGenerator.Parser;

/// <summary>
/// Result container holding parsed target model and pipeline diagnostics.
/// </summary>
public sealed record TargetParserResult(
    TargetClassModel? Model,
    EquatableArray<DiagnosticInfo> Diagnostics);

/// <summary>
/// Extracts semantic models from Roslyn syntax contexts for decorated target types and validates compiler rules.
/// </summary>
public static class TargetParser
{
    private const string AttributeFullName = "Parquet.SourceGenerator.ParquetSerializableAttribute";
    private const string ColumnAttributeFullName = "Parquet.SourceGenerator.ParquetColumnAttribute";
    private const string IgnoreAttributeFullName = "Parquet.SourceGenerator.ParquetIgnoreAttribute";
    private const string DecimalAttributeFullName = "Parquet.SourceGenerator.ParquetDecimalAttribute";
    private const string TimestampAttributeFullName = "Parquet.SourceGenerator.ParquetTimestampAttribute";

    /// <summary>
    /// Parses a Roslyn syntax context and returns a value-equatable <see cref="TargetParserResult"/> containing the target model and diagnostics.
    /// </summary>
    public static TargetParserResult GetTargetModel(GeneratorSyntaxContext context)
    {
        SyntaxNode node = context.Node;
        if (node is not TypeDeclarationSyntax typeDeclaration)
            return new TargetParserResult(null, EquatableArray<DiagnosticInfo>.Empty);

        ISymbol? symbol = context.SemanticModel.GetDeclaredSymbol(typeDeclaration);
        if (symbol is not INamedTypeSymbol typeSymbol)
            return new TargetParserResult(null, EquatableArray<DiagnosticInfo>.Empty);

        AttributeData? serializableAttr = typeSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == AttributeFullName);

        if (serializableAttr is null)
            return new TargetParserResult(null, EquatableArray<DiagnosticInfo>.Empty);

        var diagnostics = new List<DiagnosticInfo>();

        // Rule PARQ001: Target type must be declared as partial
        bool isPartial = typeDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
        if (!isPartial)
        {
            diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.MustBePartial,
                typeDeclaration.Identifier.GetLocation(),
                new[] { typeSymbol.Name }));
        }

        string namespaceName = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : typeSymbol.ContainingNamespace.ToDisplayString();

        string className = typeSymbol.Name;
        string schemaName = className;

        foreach (KeyValuePair<string, TypedConstant> namedArg in serializableAttr.NamedArguments)
        {
            if (namedArg.Key == "SchemaName" && namedArg.Value.Value is string customSchemaName)
                schemaName = customSchemaName;
        }

        var propertyModels = new List<PropertyModel>();
        var seenColumnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<ISymbol> members = typeSymbol.GetMembers();

        foreach (ISymbol member in members)
        {
            if (member.IsStatic || member.DeclaredAccessibility != Accessibility.Public)
                continue;

            ITypeSymbol? memberType = null;
            if (member is IPropertySymbol propSymbol)
                memberType = propSymbol.Type;
            else if (member is IFieldSymbol fieldSymbol)
                memberType = fieldSymbol.Type;

            if (memberType is null) continue;

            bool isIgnored = member.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == IgnoreAttributeFullName);
            if (isIgnored) continue;

            AttributeData? columnAttr = member.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ColumnAttributeFullName ||
                                     a.AttributeClass?.ToDisplayString() == "Parquet.Attributes.ParquetColumnAttribute");

            string columnName = member.Name;
            int order = -1;

            if (columnAttr is not null && columnAttr.ConstructorArguments.Length > 0)
            {
                if (columnAttr.ConstructorArguments[0].Value is string customColName)
                    columnName = customColName;

                foreach (KeyValuePair<string, TypedConstant> namedArg in columnAttr.NamedArguments)
                {
                    if (namedArg.Key == "Order" && namedArg.Value.Value is int customOrder)
                        order = customOrder;
                }
            }

            // Rule PARQ002: Duplicate column name check
            if (!seenColumnNames.Add(columnName))
            {
                Location loc = member.Locations.FirstOrDefault() ?? typeDeclaration.Identifier.GetLocation();
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticDescriptors.DuplicateColumnName,
                    loc,
                    new[] { columnName, className }));
            }

            AttributeData? decimalAttr = member.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == DecimalAttributeFullName);
            int? precision = null;
            int? scale = null;
            if (decimalAttr is not null && decimalAttr.ConstructorArguments.Length >= 2)
            {
                if (decimalAttr.ConstructorArguments[0].Value is int p && decimalAttr.ConstructorArguments[1].Value is int s)
                {
                    precision = p;
                    scale = s;
                }
            }

            AttributeData? timestampAttr = member.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == TimestampAttributeFullName);
            string? timestampUnit = null;
            if (timestampAttr is not null && timestampAttr.ConstructorArguments.Length > 0)
                timestampUnit = timestampAttr.ConstructorArguments[0].Value?.ToString();

            // Unwrap Nullable<T> to get the underlying type for kind classification
            ITypeSymbol underlyingType = memberType;
            bool isNullable = memberType.IsReferenceType ||
                              memberType.NullableAnnotation == NullableAnnotation.Annotated;

            if (memberType is INamedTypeSymbol { IsGenericType: true } genericType &&
                genericType.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T)
            {
                isNullable = true;
                underlyingType = genericType.TypeArguments[0];
            }

            PropertyKind kind = ClassifyKind(underlyingType, memberType);

            // For enum types, capture the underlying type name for correct array allocation
            string? enumUnderlyingTypeName = null;
            if (kind == PropertyKind.Enum && underlyingType is INamedTypeSymbol enumTypeSymbol)
                enumUnderlyingTypeName = enumTypeSymbol.EnumUnderlyingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            string typeName = memberType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            propertyModels.Add(new PropertyModel(
                Name: member.Name,
                ParquetColumnName: columnName,
                TypeName: typeName,
                TimestampUnit: timestampUnit,
                EnumUnderlyingTypeName: enumUnderlyingTypeName,
                Order: order,
                DecimalPrecision: precision,
                DecimalScale: scale,
                Kind: kind,
                IsNullable: isNullable));
        }

        // Rule PARQ003: Warning if no public serializable properties found
        if (propertyModels.Count == 0)
        {
            diagnostics.Add(new DiagnosticInfo(
                DiagnosticDescriptors.NoPropertiesFound,
                typeDeclaration.Identifier.GetLocation(),
                new[] { className }));
        }

        List<PropertyModel> orderedProperties = propertyModels
            .OrderBy(p => p.Order >= 0 ? p.Order : int.MaxValue)
            .ThenBy(p => p.Name)
            .ToList();

        TargetClassModel? model = isPartial ? new TargetClassModel(
            Namespace: namespaceName,
            ClassName: className,
            SchemaName: schemaName,
            Properties: new EquatableArray<PropertyModel>(orderedProperties.ToArray()),
            IsRecord: typeDeclaration is RecordDeclarationSyntax,
            IsValueType: typeSymbol.IsValueType) : null;

        return new TargetParserResult(model, new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()));
    }

    private static PropertyKind ClassifyKind(ITypeSymbol underlyingType, ITypeSymbol memberType)
    {
        // Enum
        if (underlyingType.TypeKind == TypeKind.Enum)
            return PropertyKind.Enum;

        // byte[] — must check before the array branch
        if (memberType is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte })
            return PropertyKind.ByteArray;

        string fqn = underlyingType.ToDisplayString();

        return fqn switch
        {
            "decimal" => PropertyKind.Decimal,
            "System.DateTime" => PropertyKind.DateTime,
            "System.DateTimeOffset" => PropertyKind.DateTime,
            "System.TimeSpan" => PropertyKind.TimeSpan,
            "System.Guid" => PropertyKind.Guid,
            _ => PropertyKind.Primitive,
        };
    }
}
