using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;

namespace gc.Domain.Generators;

/// <summary>
/// Code fix provider for configuration diagnostic issues.
/// Provides quick fixes for common configuration mistakes.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ConfigurationCodeFixProvider))]
[Shared]
public sealed class ConfigurationCodeFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(
            "GC1001", // Invalid memory size format
            "GC1002", // Invalid discovery mode
            "GC1003", // Invalid log level
            "GC1004", // Invalid output format
            "GC1005", // Missing path placeholder
            "GC1006", // Negative MaxDepth
            "GC1007"  // Negative MaxParallelRepos
        );

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            switch (diagnostic.Id)
            {
                case "GC1001": // Invalid memory size
                    await RegisterMemorySizeFixes(context, diagnostic);
                    break;

                case "GC1002": // Invalid discovery mode
                    await RegisterDiscoveryModeFixes(context, diagnostic);
                    break;

                case "GC1003": // Invalid log level
                    await RegisterLogLevelFixes(context, diagnostic);
                    break;

                case "GC1004": // Invalid output format
                    await RegisterOutputFormatFixes(context, diagnostic);
                    break;

                case "GC1005": // Missing path placeholder
                    await RegisterPathPlaceholderFix(context, diagnostic);
                    break;

                case "GC1006": // Negative MaxDepth
                case "GC1007": // Negative MaxParallelRepos
                    await RegisterNonNegativeFix(context, diagnostic);
                    break;
            }
        }
    }

    private static async Task RegisterMemorySizeFixes(CodeFixContext context, Diagnostic diagnostic)
    {
        context.RegisterCodeFix(
            CodeAction.Create(
                "Change to '1GB'",
                ct => FixStringLiteralAsync(context.Document, diagnostic, "1GB"),
                "FixMemorySizeTo1GB"),
            diagnostic);

        context.RegisterCodeFix(
            CodeAction.Create(
                "Change to '100MB'",
                ct => FixStringLiteralAsync(context.Document, diagnostic, "100MB"),
                "FixMemorySizeTo100MB"),
            diagnostic);

        context.RegisterCodeFix(
            CodeAction.Create(
                "Change to '10MB'",
                ct => FixStringLiteralAsync(context.Document, diagnostic, "10MB"),
                "FixMemorySizeTo10MB"),
            diagnostic);
    }

    private static async Task RegisterDiscoveryModeFixes(CodeFixContext context, Diagnostic diagnostic)
    {
        context.RegisterCodeFix(
            CodeAction.Create(
                "Change to 'auto'",
                ct => FixStringLiteralAsync(context.Document, diagnostic, "auto"),
                "FixDiscoveryModeToAuto"),
            diagnostic);

        context.RegisterCodeFix(
            CodeAction.Create(
                "Change to 'git'",
                ct => FixStringLiteralAsync(context.Document, diagnostic, "git"),
                "FixDiscoveryModeToGit"),
            diagnostic);

        context.RegisterCodeFix(
            CodeAction.Create(
                "Change to 'filesystem'",
                ct => FixStringLiteralAsync(context.Document, diagnostic, "filesystem"),
                "FixDiscoveryModeToFilesystem"),
            diagnostic);
    }

    private static async Task RegisterLogLevelFixes(CodeFixContext context, Diagnostic diagnostic)
    {
        var levels = new[] { "normal", "verbose", "debug", "quiet" };
        foreach (var level in levels)
        {
            var title = char.ToUpper(level[0]) + level[1..];
            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Change to '{title}'",
                    ct => FixStringLiteralAsync(context.Document, diagnostic, level),
                    $"FixLogLevelTo{title}"),
                diagnostic);
        }
    }

    private static async Task RegisterOutputFormatFixes(CodeFixContext context, Diagnostic diagnostic)
    {
        var formats = new[] { "markdown", "plain", "json" };
        foreach (var format in formats)
        {
            var title = char.ToUpper(format[0]) + format[1..];
            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Change to '{title}'",
                    ct => FixStringLiteralAsync(context.Document, diagnostic, format),
                    $"FixOutputFormatTo{title}"),
                diagnostic);
        }
    }

    private static async Task RegisterPathPlaceholderFix(CodeFixContext context, Diagnostic diagnostic)
    {
        context.RegisterCodeFix(
            CodeAction.Create(
                "Add '{path}' to template",
                ct => FixAddPathPlaceholderAsync(context.Document, diagnostic),
                "AddPathPlaceholder"),
            diagnostic);
    }

    private static async Task RegisterNonNegativeFix(CodeFixContext context, Diagnostic diagnostic)
    {
        context.RegisterCodeFix(
            CodeAction.Create(
                "Change to '0'",
                ct => FixNumericLiteralAsync(context.Document, diagnostic, "0"),
                "FixToZero"),
            diagnostic);
    }

    private static async Task<Document> FixStringLiteralAsync(Document document, Diagnostic diagnostic, string newValue)
    {
        var root = await document.GetSyntaxRootAsync();
        if (root == null) return document;

        var node = root.FindNode(diagnostic.Location.SourceSpan);
        if (node == null) return document;

        var stringLiteral = node.FirstAncestorOrSelf<LiteralExpressionSyntax>();
        if (stringLiteral == null) return document;

        var newLiteral = stringLiteral.WithToken(
            SyntaxFactory.Literal(newValue, newValue));

        var newRoot = root.ReplaceNode(stringLiteral, newLiteral);
        return document.WithSyntaxRoot(newRoot);
    }

    private static async Task<Document> FixNumericLiteralAsync(Document document, Diagnostic diagnostic, string newValue)
    {
        var root = await document.GetSyntaxRootAsync();
        if (root == null) return document;

        var node = root.FindNode(diagnostic.Location.SourceSpan);
        if (node == null) return document;

        var numericLiteral = node.FirstAncestorOrSelf<LiteralExpressionSyntax>();
        if (numericLiteral == null) return document;

        var newLiteral = numericLiteral.WithToken(
            SyntaxFactory.Literal(newValue));

        var newRoot = root.ReplaceNode(numericLiteral, newLiteral);
        return document.WithSyntaxRoot(newRoot);
    }

    private static async Task<Document> FixAddPathPlaceholderAsync(Document document, Diagnostic diagnostic)
    {
        var root = await document.GetSyntaxRootAsync();
        if (root == null) return document;

        var node = root.FindNode(diagnostic.Location.SourceSpan);
        if (node == null) return document;

        var stringLiteral = node.FirstAncestorOrSelf<LiteralExpressionSyntax>();
        if (stringLiteral == null) return document;

        var currentValue = stringLiteral.Token.ValueText ?? string.Empty;
        var newValue = currentValue.Contains("{path}") ? currentValue : $"{currentValue.TrimEnd()} {{path}}";

        var newLiteral = stringLiteral.WithToken(
            SyntaxFactory.Literal(newValue, newValue));

        var newRoot = root.ReplaceNode(stringLiteral, newLiteral);
        return document.WithSyntaxRoot(newRoot);
    }
}
