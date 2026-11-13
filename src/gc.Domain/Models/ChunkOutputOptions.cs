namespace gc.Domain.Models;

/// <summary>Controls complete, size-limited Markdown files instead of clipboard output.</summary>
public sealed record ChunkOutputOptions(int MaxBytes, string? Directory = null, string Prefix = "context");
