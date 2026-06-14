using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;

namespace gc.Domain.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class ConfigurationSchemaGenerator : IIncrementalGenerator
{
    // Incremental generator pipeline - tracks changes precisely
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Phase 1: Find all record declarations in Configuration namespace
        // Using RegisterSyntaxNodeOutput for incremental tracking of syntax changes
        var configurationRecords = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsPotentialConfigurationRecord(node),
                transform: static (ctx, _) => TryGetConfigurationRecord(ctx))
            .Where(static m => m is not null)
            .Collect();

        // Phase 2: Combine with compilation for semantic analysis
        // This ensures we only regenerate when compilation or tracked syntax changes
        var compilationAndRecords = context.CompilationProvider.Combine(configurationRecords);

        // Phase 3: Generate source
        context.RegisterSourceOutput(compilationAndRecords, static (ctx, source) =>
        {
            var compilation = source.Left;
            var records = source.Right;

            if (records.IsDefaultOrEmpty) return;

            var validRecords = records
                .Where(r => r is not null)
                .SelectMany(r => r!)
                .GroupBy(r => r.Name)
                .Select(g => g.First())
                .ToList();

            if (validRecords.Count == 0) return;

            Execute(ctx, validRecords);
        });
    }

    // Fast syntax filter - returns true for potential configuration records
    private static bool IsPotentialConfigurationRecord(SyntaxNode node)
    {
        return node is RecordDeclarationSyntax record &&
               !record.Modifiers.Any(SyntaxKind.AbstractKeyword) &&
               (record.Identifier.ValueText.EndsWith("Configuration") ||
                record.Identifier.ValueText == "GcConfiguration" ||
                record.Identifier.ValueText.EndsWith("Options"));
    }

    // Transform to get semantic info - only runs for nodes passing the predicate
    private static List<ConfigurationRecordInfo>? TryGetConfigurationRecord(GeneratorSyntaxContext context)
    {
        if (context.Node is not RecordDeclarationSyntax record) return null;

        var recordSymbol = context.SemanticModel.GetDeclaredSymbol(record);
        if (recordSymbol == null) return null;

        var namespaceName = recordSymbol.ContainingNamespace?.ToString() ?? string.Empty;
        // Accept records from Configuration or Models namespaces
        if (!namespaceName.Contains("Configuration") &&
            !namespaceName.Contains("Models") &&
            !namespaceName.Contains("gc.Domain")) return null;

        var properties = new List<PropertyInfo>();
        foreach (var member in recordSymbol.GetMembers())
        {
            if (member is IPropertySymbol property &&
                property.DeclaredAccessibility == Accessibility.Public &&
                !property.IsStatic &&
                property.GetMethod != null) // readable properties
            {
                properties.Add(new PropertyInfo
                {
                    Name = property.Name,
                    Type = GetTypeName(property.Type),
                    FullyQualifiedType = GetFullyQualifiedTypeName(property.Type),
                    IsNullable = property.Type.NullableAnnotation == NullableAnnotation.Annotated ||
                                 (property.Type.IsValueType && property.Type is INamedTypeSymbol namedType &&
                                 namedType.OriginalDefinition.ToString().StartsWith("System.Nullable")),
                    IsValueType = property.Type.IsValueType,
                    IsDictionary = IsDictionaryType(property.Type),
                    IsReadOnlyDictionary = IsReadOnlyDictionaryType(property.Type)
                });
            }
        }

        if (properties.Count == 0) return null;

        return new List<ConfigurationRecordInfo>
        {
            new ConfigurationRecordInfo
            {
                Name = recordSymbol.Name,
                Namespace = namespaceName,
                Properties = properties
            }
        };
    }

    private static bool IsDictionaryType(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol namedType)
        {
            var originalDefinition = namedType.OriginalDefinition;
            return originalDefinition.ToString().StartsWith("System.Collections.Generic.Dictionary");
        }
        return false;
    }

    private static bool IsReadOnlyDictionaryType(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol namedType)
        {
            var originalDefinition = namedType.OriginalDefinition;
            return originalDefinition.ToString().StartsWith("System.Collections.Generic.IReadOnlyDictionary");
        }
        return false;
    }

    private static string GetTypeName(ITypeSymbol type)
    {
        return type.ToString();
    }

    private static string GetFullyQualifiedTypeName(ITypeSymbol type)
    {
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static void Execute(SourceProductionContext context, List<ConfigurationRecordInfo> records)
    {
        var generator = new ConfigurationAccessorGenerator();
        var sourceCode = generator.Generate(records);

        context.AddSource("ConfigurationAccessors.g.cs", sourceCode);
    }
}

public sealed class ConfigurationRecordInfo
{
    public required string Name { get; init; }
    public required string Namespace { get; init; }
    public required List<PropertyInfo> Properties { get; init; }
}

public sealed class PropertyInfo
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required string FullyQualifiedType { get; init; }
    public bool IsNullable { get; init; }
    public bool IsValueType { get; init; }
    public bool IsDictionary { get; init; }
    public bool IsReadOnlyDictionary { get; init; }
}

/// <summary>
/// Generates strongly-typed, AOT-friendly configuration accessors.
/// </summary>
public sealed class ConfigurationAccessorGenerator
{
    public string Generate(List<ConfigurationRecordInfo> records)
    {
        var sb = new StringBuilder();

        // File header
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// This file is generated by ConfigurationSchemaGenerator. Do not modify directly.");
        sb.AppendLine("#pragma warning disable CS1591 // Missing XML comment");
        sb.AppendLine("#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type");
        sb.AppendLine("#pragma warning disable CS8603 // Possible null reference return");
        sb.AppendLine("#pragma warning disable CS8618 // Nullable non-nullable field");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Diagnostics.CodeAnalysis;");
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine("using gc.Domain.Models.Configuration;");
        sb.AppendLine();
        sb.AppendLine("namespace gc.Domain.Generated;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Strongly-typed, AOT-friendly configuration accessors.");
        sb.AppendLine("/// Generated by ConfigurationSchemaGenerator to eliminate reflection overhead.");
        sb.AppendLine("/// All accessors use aggressive inlining for zero-performance overhead.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public static partial class ConfigurationAccessors");
        sb.AppendLine("{");
        sb.AppendLine("    [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.AppendLine("    private static T NonNull<T>([NotNull] T? value) where T : class =>");
        sb.AppendLine("        value ?? throw new InvalidOperationException(\"Configuration property cannot be null.\");");
        sb.AppendLine();

        foreach (var record in records)
        {
            GenerateAccessorForRecord(sb, record);
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static void GenerateAccessorForRecord(StringBuilder sb, ConfigurationRecordInfo record)
    {
        sb.AppendLine($"    /// <summary>");
        sb.AppendLine($"    /// Accessor for {record.Name} properties.");
        sb.AppendLine($"    /// Provides strongly-typed, null-safe access to configuration values.");
        sb.AppendLine($"    /// </summary>");
        sb.AppendLine($"    public static partial class {record.Name}Accessor");
        sb.AppendLine("    {");

        foreach (var property in record.Properties)
        {
            GeneratePropertyAccessor(sb, record.Name, property);
        }

        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void GeneratePropertyAccessor(StringBuilder sb, string declaringTypeName, PropertyInfo property)
    {
        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// Gets the value of {property.Name} from {declaringTypeName}.");
        sb.AppendLine($"        /// </summary>");

        var paramType = declaringTypeName;

        // Generate appropriate accessor based on property type and nullability
        if (property.IsValueType)
        {
            // Value types don't need null checks
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public static {property.Type} Get{property.Name}({paramType} config) => config.{property.Name};");
        }
        else if (property.IsReadOnlyDictionary)
        {
            // Dictionary types need special handling
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public static {property.Type} Get{property.Name}({paramType} config) => config.{property.Name};");
        }
        else if (property.IsNullable)
        {
            // Nullable reference types - might be null
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public static {property.Type} Get{property.Name}({paramType} config) => config.{property.Name}!;");
        }
        else
        {
            // Non-nullable reference types - enforce non-null
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public static {property.Type} Get{property.Name}({paramType} config) => NonNull(config.{property.Name});");
        }

        sb.AppendLine();

        // Generate Has method for nullable properties
        if (property.IsNullable && !property.IsValueType)
        {
            sb.AppendLine($"        /// <summary>");
            sb.AppendLine($"        /// Checks whether {property.Name} has a value.");
            sb.AppendLine($"        /// </summary>");
            sb.AppendLine($"        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine($"        public static bool Has{property.Name}({paramType} config) => config.{property.Name} != null;");
            sb.AppendLine();
        }
    }
}
