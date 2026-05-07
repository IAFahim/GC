using gc.Domain.Common;
using gc.Domain.Interfaces;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;
using gc.Application.Services;
using System.Text;

namespace gc.Application.UseCases;

/// <summary>
/// Orchestrates file discovery, filtering, and markdown generation for a single repository or cluster of repos.
/// </summary>
public sealed class GenerateContextUseCase
{
    private readonly IFileDiscovery _discovery;
    private readonly FileFilter _filter;
    private readonly IFileReader _reader;
    private readonly IMarkdownGenerator _generator;
    private readonly IClipboardService _clipboard;
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new instance with all required dependencies.
    /// </summary>
    public GenerateContextUseCase(
        IFileDiscovery discovery,
        FileFilter filter,
        IFileReader reader,
        IMarkdownGenerator generator,
        IClipboardService clipboard,
        ILogger logger)
    {
        _discovery = discovery;
        _filter = filter;
        _reader = reader;
        _generator = generator;
        _clipboard = clipboard;
        _logger = logger;
    }

    /// <summary>
    /// Discovers, filters, and generates markdown for files in a single repository.
    /// </summary>
    public async Task<Result> ExecuteAsync(
        string rootPath,
        GcConfiguration config,
        IEnumerable<string> paths,
        IEnumerable<string> excludes,
        IEnumerable<string> extensions,
        string? outputFile,
        bool appendMode = false,
        IEnumerable<string>? excludeLineIfStart = null,
        bool brainMode = false,
        CancellationToken ct = default)
    {
        var discoveryResult = await _discovery.DiscoverFilesAsync(rootPath, config, ct);
        if (!discoveryResult.IsSuccess) return Result.Failure(discoveryResult.Error!);

        var filterResult = _filter.FilterFiles(discoveryResult.Value!, config, paths, excludes, extensions);
        if (!filterResult.IsSuccess) return Result.Failure(filterResult.Error!);

        var entries = filterResult.Value!.ToList();
        if (!entries.Any())
        {
            _logger.Success("No files match the specified filters.");
            return Result.Success();
        }

        // Phase 3.5: Pre-warm the page cache with readahead() / prefetch
        var fullPaths = entries.Select(e => Path.Combine(rootPath, e.Path));
        gc.Application.Native.LinuxFastPath.Prewarm(fullPaths, entries.Count);

        _logger.Success("Processing...");

        // Stream contents lazily
        var contents = entries.Select(e => new FileContent(e, null, e.Size));

        return await WriteOutputAsync(contents, entries.Count, rootPath, config, outputFile, appendMode, excludeLineIfStart, brainMode, ct);
    }

    /// <summary>
    /// Cluster mode: discovers all git repos in a directory and processes them all into a single merged output.
    /// Each repo's files are prefixed with the repo name and separated by configurable separators.
    /// </summary>
    public async Task<Result> ExecuteClusterAsync(
        string clusterRoot,
        GcConfiguration config,
        IEnumerable<string> paths,
        IEnumerable<string> excludes,
        IEnumerable<string> extensions,
        string? outputFile,
        bool appendMode = false,
        IEnumerable<string>? excludeLineIfStart = null,
        bool brainMode = false,
        CancellationToken ct = default)
    {
        var clusterConfig = config.Discovery?.Cluster ?? new ClusterConfiguration();
        if (clusterConfig.MaxDepth <= 0) clusterConfig = clusterConfig with { MaxDepth = 2 };

        // Step 1: Discover all git repos in the cluster directory
        var reposResult = await _discovery.DiscoverGitReposAsync(clusterRoot, clusterConfig, ct);
        if (!reposResult.IsSuccess) return Result.Failure(reposResult.Error!);

        var repos = reposResult.Value!;
        if (repos.Count == 0)
        {
            _logger.Warning("No git repositories found in cluster directory. Nothing to process.");
            return Result.Success();
        }

        _logger.Info($"Processing {repos.Count} repos in cluster mode...");

        // Step 2: For each repo, discover files, filter, and collect entries
        var repoEntries = new List<(RepoInfo Repo, List<FileEntry> Entries)>();
        var errors = new List<string>();
        var maxParallel = clusterConfig.MaxParallelRepos > 0
            ? clusterConfig.MaxParallelRepos
            : Environment.ProcessorCount;

        // Parallel discovery and filtering
        var lockObj = new object();
        await Parallel.ForEachAsync(repos, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Min(maxParallel, repos.Count),
            CancellationToken = ct
        }, async (repo, token) =>
        {
            try
            {
                // Discover files in this repo
                var discoveryResult = await _discovery.DiscoverFilesAsync(repo.RootPath, config, token);
                if (!discoveryResult.IsSuccess)
                {
                    if (clusterConfig.FailFast)
                        throw new InvalidOperationException($"Discovery failed for {repo.RelativePath}: {discoveryResult.Error}");

                    lock (lockObj)
                    {
                        errors.Add($"{repo.RelativePath}: {discoveryResult.Error}");
                    }
                    return;
                }

                // Filter files
                var filterResult = _filter.FilterFiles(discoveryResult.Value!, config, paths, excludes, extensions);
                if (!filterResult.IsSuccess)
                {
                    lock (lockObj)
                    {
                        errors.Add($"{repo.RelativePath}: {filterResult.Error}");
                    }
                    return;
                }

                var entries = filterResult.Value!.ToList();
                if (entries.Count > 0)
                {
                    // Prefix paths with repo relative path to avoid collisions,
                    // but store the ABSOLUTE filesystem path so MarkdownGenerator can open the file.
                    // The display path (repo_relative/file_relative) is used for markdown headers.
                    var prefixedEntries = entries.Select(e =>
                    {
                        var displayPath = $"{repo.RelativePath}/{e.Path}";
                        var absPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(repo.RootPath, e.Path));
                        return e with { Path = absPath, DisplayPath = displayPath };
                    }).ToList();

                    lock (lockObj)
                    {
                        repoEntries.Add((repo, prefixedEntries));
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                if (clusterConfig.FailFast) throw;

                lock (lockObj)
                {
                    errors.Add($"{repo.RelativePath}: {ex.Message}");
                }
            }
        });

        // Report any errors
        foreach (var error in errors)
        {
            _logger.Warning($"Repo error: {error}");
        }

        if (repoEntries.Count == 0)
        {
            _logger.Warning("No files found in any of the discovered repos.");
            return Result.Success();
        }

        // Step 3: Sort repos by name for deterministic output
        var sorted = repoEntries
            .OrderBy(r => r.Repo.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totalFiles = sorted.Sum(s => s.Entries.Count);

        // Step 4: Build merged file content stream with repo headers
        var allContents = BuildClusterContents(sorted, clusterConfig, clusterRoot);
        var allEntries = sorted.SelectMany(s => s.Entries).ToList();

        // Pre-warm page cache (paths are already absolute in cluster mode)
        Native.LinuxFastPath.Prewarm(allEntries.Select(e => e.Path), allEntries.Count);

        _logger.Success($"Processing {totalFiles} files from {sorted.Count} repos...");

        return await WriteOutputAsync(allContents, totalFiles, clusterRoot, config, outputFile, appendMode, excludeLineIfStart, brainMode, ct);
    }

    /// <summary>
    /// Builds a merged stream of FileContent with repo headers inserted between repos.
    /// </summary>
    private IEnumerable<FileContent> BuildClusterContents(
        List<(RepoInfo Repo, List<FileEntry> Entries)> sorted,
        ClusterConfiguration clusterConfig,
        string clusterRoot)
    {
        for (int i = 0; i < sorted.Count; i++)
        {
            var (repo, entries) = sorted[i];

            // Insert repo header if configured
            if (clusterConfig.IncludeRepoHeader && i > 0)
            {
                // Insert separator between repos as a "virtual" file
                var separatorContent = $"{clusterConfig.RepoSeparator}\n# {repo.Name}\n{clusterConfig.RepoSeparator}";
                var separatorEntry = new FileEntry($"[{clusterConfig.RepoSeparator}] {repo.RelativePath}", "", "markdown", separatorContent.Length);
                yield return new FileContent(separatorEntry, separatorContent, separatorContent.Length);
            }
            else if (clusterConfig.IncludeRepoHeader && i == 0)
            {
                // First repo header (no preceding separator)
                var headerContent = $"# {repo.Name}";
                var headerEntry = new FileEntry($"[header] {repo.RelativePath}", "", "markdown", headerContent.Length);
                yield return new FileContent(headerEntry, headerContent, headerContent.Length);
            }

            // Output all files from this repo
            foreach (var entry in entries)
            {
                yield return new FileContent(entry, null, entry.Size);
            }
        }
    }

    private async Task<Result> WriteOutputAsync(
        IEnumerable<FileContent> contents,
        int fileCount,
        string rootPath,
        GcConfiguration config,
        string? outputFile,
        bool appendMode,
        IEnumerable<string>? excludeLineIfStart,
        bool brainMode,
        CancellationToken ct)
    {
        var crusher = brainMode ? new BrainCrusher() : null;
        var dynamicCompressor = brainMode ? new DynamicCompressor() : null;

        if (!string.IsNullOrEmpty(outputFile))
        {
            // Ensure parent directory exists
            var outputDir = Path.GetDirectoryName(outputFile);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            bool shouldAppend = appendMode && File.Exists(outputFile);
            FileMode fileMode = shouldAppend ? FileMode.Append : FileMode.Create;

            // When dynamic compression is active, buffer to memory first
            // Memory is bounded by maxMemoryBytes config
            if (dynamicCompressor != null)
            {
                using var ms = new MemoryStream();

                // Write static Brain Mode header
                if (crusher != null)
                {
                    var headerBytes = Encoding.UTF8.GetBytes(crusher.GetDictionaryHeader());
                    await ms.WriteAsync(headerBytes, ct);
                }

                var genResult = await _generator.GenerateMarkdownStreamingAsync(contents, ms, config, excludeLineIfStart, crusher, ct);
                if (!genResult.IsSuccess) return Result.Failure(genResult.Error!);

                // Dynamic compression pass
                ms.Position = 0;
                using var reader = new StreamReader(ms, Encoding.UTF8);
                var rawOutput = await reader.ReadToEndAsync(ct);

                var dynResult = dynamicCompressor.Compress(rawOutput);
                var finalOutput = dynResult.Legend + dynResult.Output;
                var finalBytes = Encoding.UTF8.GetBytes(finalOutput);

                using var fs = new FileStream(outputFile, fileMode, FileAccess.Write, FileShare.None, 4096, useAsync: true);
                await fs.WriteAsync(finalBytes, ct);

                string action = shouldAppend && fileMode == FileMode.Append ? "Appended to" : "Exported to";
                string dynInfo = dynResult.ReplacementCount > 0 ? $" | Dynamic: {dynResult.ReplacementCount} replacements, ~{dynResult.TokensSaved} tokens saved" : "";
                _logger.Success($"{action} {outputFile}: {fileCount} files | Size: {Formatting.FormatSize(finalBytes.Length)} | BrainMode ON{dynInfo} | Tokens: ~{finalBytes.Length / 4}");

                return Result.Success();
            }

            using var fs2 = new FileStream(outputFile, fileMode, FileAccess.Write, FileShare.None, 4096, useAsync: true);

            var genResult2 = await _generator.GenerateMarkdownStreamingAsync(contents, fs2, config, excludeLineIfStart, crusher, ct);
            if (!genResult2.IsSuccess) return Result.Failure(genResult2.Error!);

            string action2 = shouldAppend && fileMode == FileMode.Append ? "Appended to" : "Exported to";
            _logger.Success($"✔ {action2} {outputFile}: {fileCount} files | Size: {Formatting.FormatSize(genResult2.Value)} | Tokens: ~{genResult2.Value / 4}");

            return Result.Success();
        }
        else
        {
            using var ms = new MemoryStream();

            // Write static Brain Mode header first
            if (crusher != null)
            {
                var headerBytes = Encoding.UTF8.GetBytes(crusher.GetDictionaryHeader());
                await ms.WriteAsync(headerBytes, ct);
            }

            var genResult = await _generator.GenerateMarkdownStreamingAsync(contents, ms, config, excludeLineIfStart, crusher, ct);
            if (!genResult.IsSuccess) return Result.Failure(genResult.Error!);

            // Dynamic compression pass (clipboard path already buffers)
            Stream finalStream = ms;
            DynamicCompressor.CompressResult? dynResult = null;

            if (dynamicCompressor != null)
            {
                ms.Position = 0;
                using var reader = new StreamReader(ms, Encoding.UTF8);
                var rawOutput = await reader.ReadToEndAsync(ct);

                var dr = dynamicCompressor.Compress(rawOutput);
                var finalOutput = dr.Legend + dr.Output;
                var finalBytes = Encoding.UTF8.GetBytes(finalOutput);
                finalStream = new MemoryStream(finalBytes);
                dynResult = dr;
            }

            finalStream.Position = 0;
            var clipResult = await _clipboard.CopyToClipboardAsync(finalStream, config.Limits, appendMode, ct);
            if (!clipResult.IsSuccess) return Result.Failure(clipResult.Error!);

            if (dynResult.HasValue)
            {
                var dr = dynResult.Value;
                string dynInfo = dr.ReplacementCount > 0 ? $" | Dynamic: {dr.ReplacementCount} replacements, ~{dr.TokensSaved} tokens saved" : "";
                _logger.Success($"Copied: {fileCount} files | Size: {Formatting.FormatSize(dr.Output.Length)} | BrainMode ON{dynInfo} | Tokens: ~{dr.Output.Length / 4}");
            }
            else
            {
                _logger.Success($"✔ Copied: {fileCount} files | Size: {Formatting.FormatSize(genResult.Value)} | Tokens: ~{genResult.Value / 4}");
            }

            return Result.Success();
        }
    }
}
