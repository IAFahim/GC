using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace gc.Domain.Generators;

/// <summary>
/// Diagnostic analyzer for configuration validation.
/// Provides compile-time validation of configuration values.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ConfigurationDiagnosticAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor InvalidMemorySizeFormat = new(
        id: "GC1001",
        title: "Invalid memory size format",
        messageFormat: "Memory size '{0}' must be in format like '100MB', '1GB', etc.",
        category: "Configuration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidDiscoveryMode = new(
        id: "GC1002",
        title: "Invalid discovery mode",
        messageFormat: "Discovery mode '{0}' must be one of auto, git, filesystem",
        category: "Configuration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidLogLevel = new(
        id: "GC1003",
        title: "Invalid log level",
        messageFormat: "Log level '{0}' must be one of normal, verbose, debug, quiet",
        category: "Configuration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidOutputFormat = new(
        id: "GC1004",
        title: "Invalid output format",
        messageFormat: "Output format '{0}' must be one of markdown, plain, json",
        category: "Configuration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingPathPlaceholder = new(
        id: "GC1005",
        title: "Missing path placeholder in file header template",
        messageFormat: "FileHeaderTemplate should contain '{{path}}' placeholder for proper file header generation",
        category: "Configuration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NegativeMaxDepth = new(
        id: "GC1006",
        title: "Cluster MaxDepth must be non-negative",
        messageFormat: "Cluster MaxDepth must be non-negative, got: {0}",
        category: "Configuration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NegativeMaxParallelRepos = new(
        id: "GC1007",
        title: "Cluster MaxParallelRepos must be non-negative",
        messageFormat: "Cluster MaxParallelRepos must be non-negative, got: {0}",
        category: "Configuration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            InvalidMemorySizeFormat,
            InvalidDiscoveryMode,
            InvalidLogLevel,
            InvalidOutputFormat,
            MissingPathPlaceholder,
            NegativeMaxDepth,
            NegativeMaxParallelRepos);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeObjectInitializer, SyntaxKind.ObjectInitializerExpression);
        context.RegisterSyntaxNodeAction(AnalyzeAssignmentExpression, SyntaxKind.SimpleAssignmentExpression);
    }

    private static void AnalyzeObjectInitializer(SyntaxNodeAnalysisContext context)
    {
        var initializer = (InitializerExpressionSyntax)context.Node;

        var typeInfo = context.SemanticModel.GetTypeInfo(initializer);
        var typeSymbol = typeInfo.Type;
        if (typeSymbol == null) return;

        var typeName = typeSymbol.Name;
        var fullTypeName = typeSymbol.ToString();

        if (!IsConfigurationType(typeName, fullTypeName)) return;

        foreach (var expression in initializer.Expressions)
        {
            if (expression is AssignmentExpressionSyntax assignment)
            {
                if (assignment.Left is MemberAccessExpressionSyntax memberAccess)
                {
                    var propertyName = memberAccess.Name.Identifier.ValueText;
                    AnalyzePropertyAssignment(context, typeName, propertyName, assignment.Right, assignment.Right.GetLocation());
                }
            }
        }
    }

    private static void AnalyzeAssignmentExpression(SyntaxNodeAnalysisContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;

        if (assignment.Left is not MemberAccessExpressionSyntax memberAccess) return;

        var propertyName = memberAccess.Name.Identifier.ValueText;

        var typeInfo = context.SemanticModel.GetTypeInfo(memberAccess.Expression);
        if (typeInfo.Type == null) return;

        var typeName = typeInfo.Type.Name;
        if (!IsConfigurationType(typeName, typeInfo.Type.ToString())) return;

        AnalyzePropertyAssignment(context, typeName, propertyName, assignment.Right, assignment.Right.GetLocation());
    }

    private static bool IsConfigurationType(string typeName, string? fullTypeName)
    {
        return typeName.EndsWith("Configuration") ||
               typeName == "DefaultConfigOptions" ||
               typeName == "GcConfiguration" ||
               (fullTypeName != null && fullTypeName.Contains("Configuration"));
    }

    private static void AnalyzePropertyAssignment(SyntaxNodeAnalysisContext context, string typeName, string propertyName, ExpressionSyntax valueExpression, Location location)
    {
        var optionalValue = context.SemanticModel.GetConstantValue(valueExpression);
        if (!optionalValue.HasValue) return;

        var value = optionalValue.Value;
        if (value is not string stringValue || string.IsNullOrWhiteSpace(stringValue)) return;

        ValidateProperty(context, typeName, propertyName, stringValue, location, valueExpression);
    }

    private static void ValidateProperty(SyntaxNodeAnalysisContext context, string typeName, string propertyName, string stringValue, Location location, ExpressionSyntax expression)
    {
        switch (propertyName)
        {
            case "MaxFileSize":
            case "MaxClipboardSize":
            case "MaxMemoryBytes":
                if (IsLimitsConfiguration(typeName))
                {
                    if (!IsValidMemorySize(stringValue))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(InvalidMemorySizeFormat, location, stringValue));
                    }
                }
                break;

            case "Mode":
                if (IsDiscoveryConfiguration(typeName))
                {
                    if (!IsValidDiscoveryMode(stringValue))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(InvalidDiscoveryMode, location, stringValue));
                    }
                }
                break;

            case "MaxDepth":
                if (IsClusterConfiguration(typeName) && int.TryParse(stringValue, out var depth))
                {
                    if (depth < 0)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(NegativeMaxDepth, location, depth));
                    }
                }
                break;

            case "MaxParallelRepos":
                if (IsClusterConfiguration(typeName) && int.TryParse(stringValue, out var parallel))
                {
                    if (parallel < 0)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(NegativeMaxParallelRepos, location, parallel));
                    }
                }
                break;

            case "Level":
                if (IsLoggingConfiguration(typeName))
                {
                    if (!IsValidLogLevel(stringValue))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(InvalidLogLevel, location, stringValue));
                    }
                }
                break;

            case "DefaultFormat":
                if (IsOutputConfiguration(typeName))
                {
                    if (!IsValidOutputFormat(stringValue))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(InvalidOutputFormat, location, stringValue));
                    }
                }
                break;

            case "FileHeaderTemplate":
                if (IsMarkdownConfiguration(typeName))
                {
                    if (!stringValue.Contains("{path}"))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(MissingPathPlaceholder, location));
                    }
                }
                break;
        }
    }

    private static bool IsLimitsConfiguration(string typeName) =>
        typeName == "LimitsConfiguration" || typeName == "LimitsOptions";

    private static bool IsDiscoveryConfiguration(string typeName) =>
        typeName == "DiscoveryConfiguration" || typeName == "DiscoveryOptions";

    private static bool IsClusterConfiguration(string typeName) =>
        typeName == "ClusterConfiguration";

    private static bool IsLoggingConfiguration(string typeName) =>
        typeName == "LoggingConfiguration";

    private static bool IsOutputConfiguration(string typeName) =>
        typeName == "OutputConfiguration";

    private static bool IsMarkdownConfiguration(string typeName) =>
        typeName == "MarkdownConfiguration" || typeName == "MarkdownOptions";

    private static bool IsValidMemorySize(string value)
    {
        var normalized = value.ToUpperInvariant();
        if (normalized.Length < 2) return false;

        var suffix = normalized[^2..];
        if (!suffix.IsMatch("(KB|MB|GB|TB)")) return false;

        var numberPart = normalized[..^2];
        return decimal.TryParse(numberPart, out var number) && number > 0;
    }

    private static bool IsValidDiscoveryMode(string value)
    {
        var modes = new[] { "auto", "git", "filesystem" };
        return modes.Contains(value.ToLowerInvariant());
    }

    private static bool IsValidLogLevel(string value)
    {
        var levels = new[] { "normal", "verbose", "debug", "quiet" };
        return levels.Contains(value.ToLowerInvariant());
    }

    private static bool IsValidOutputFormat(string value)
    {
        var formats = new[] { "markdown", "plain", "json" };
        return formats.Contains(value.ToLowerInvariant());
    }
}

file static class StringExtensions
{
    public static bool IsMatch(this string input, string pattern)
    {
        return Regex.IsMatch(input, $"^{pattern}$");
    }
}
