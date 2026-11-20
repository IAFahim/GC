using gc.Application.Services;
using gc.Domain.Models;

namespace gc.Application.UseCases;

/// <summary>
///     Everything a single run needs besides the root folder, configuration and cancellation token.
///     Built once after argument parsing and passed whole, instead of a long positional parameter list.
/// </summary>
public sealed record RunOptions
{
    /// <summary>Folders, files or globs to collect. Empty means everything discovered.</summary>
    public IReadOnlyList<string> Paths { get; init; } = [];

    /// <summary>Folders, files or globs to skip.</summary>
    public IReadOnlyList<string> Excludes { get; init; } = [];

    /// <summary>Only these extensions.</summary>
    public IReadOnlyList<string> Extensions { get; init; } = [];

    public string[]? ExcludePathPatterns { get; init; }
    public string[]? IncludePathPatterns { get; init; }
    public string[]? ExcludeContentPatterns { get; init; }
    public string[]? IncludeContentPatterns { get; init; }

    /// <summary>Drop lines that start with any of these strings.</summary>
    public IEnumerable<string>? DropLinesStartingWith { get; init; }

    /// <summary>Destination file. Null or empty means the clipboard.</summary>
    public string? OutputFile { get; init; }

    public bool Append { get; init; }
    public bool Brain { get; init; }
    public bool Compress { get; init; }
    public bool NoCache { get; init; }

    /// <summary>Only files changed since this Git revision.</summary>
    public string? ChangedSince { get; init; }

    public ShardInfo? Shard { get; init; }

    /// <summary>Size-limited Markdown parts. Null for single output.</summary>
    public ChunkOutputOptions? Parts { get; init; }

    public ProfileReporter? Reporter { get; init; }
}
