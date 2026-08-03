using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Parquet.SourceGenerator.Models;

namespace Parquet.SourceGenerator.Parser;

/// <summary>
/// Extracts semantic models from Roslyn syntax contexts for decorated target types.
/// </summary>
public static class TargetParser
{
    private const string AttributeFullName = "Parquet.SourceGenerator.ParquetSerializableAttribute";
    private const string ColumnAttributeFullName = "Parquet.SourceGenerator.ParquetColumnAttribute";
    private const string IgnoreAttributeFullName = "Parquet.SourceGenerator.ParquetIgnoreAttribute";
    private const string DecimalAttributeFullName = "Parquet.SourceGenerator.ParquetDecimalAttribute";
    private const string TimestampAttributeFullName = "Parquet.SourceGenerator.ParquetTimestampAttribute";

    /// <summary>
    /// Parses a Roslyn syntax context and returns a value-equatable <see cref="TargetClassModel"/> if decorated with [ParquetSerializable].
    /// </summary>
    public static TargetClassModel? GetTargetModel(GeneratorSyntaxContext context)
    {
        SyntaxNode node = context.Node;
        if (node is not TypeDeclarationSyntax typeDeclaration) return null;

        ISymbol? symbol = context.SemanticModel.GetDeclaredSymbol(typeDeclaration);
        if (symbol is not INamedTypeSymbol typeSymbol) return null;

        AttributeData? serializableAttr = typeSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == AttributeFullName);

        if (serializableAttr is null) return null;

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

        List<PropertyModel> orderedProperties = propertyModels
            .OrderBy(p => p.Order >= 0 ? p.Order : int.MaxValue)
            .ThenBy(p => p.Name)
            .ToList();

        return new TargetClassModel(
            Namespace: namespaceName,
            ClassName: className,
            SchemaName: schemaName,
            Properties: new EquatableArray<PropertyModel>(orderedProperties.ToArray()),
            IsRecord: typeDeclaration is RecordDeclarationSyntax,
            IsValueType: typeSymbol.IsValueType);
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
