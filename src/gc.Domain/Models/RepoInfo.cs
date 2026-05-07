namespace gc.Domain.Models;

public sealed record RepoInfo
{
    public string RootPath { get; init; } = string.Empty;

    public string RelativePath { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public bool IsValid { get; init; }

    public string? Error { get; init; }
}
