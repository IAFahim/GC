using gc.Domain.Common;
using gc.Domain.Interfaces;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;
using gc.Application.Services;
using System.Text;

namespace gc.Application.UseCases;

public sealed class GenerateContextUseCase
{
    private const string LlmContextHeader =
        "[Context compressed by gc+sqz for efficiency. " +
        "This contains the full source code — references like [→L], [×N], «A» are structural markers. " +
        "IMPORTANT: When writing code or answering, use the ORIGINAL full identifiers and patterns shown here. " +
        "Do NOT use abbreviated symbols or short-form in your output. Respond as if you received uncompressed source.]\n\n";

    private readonly IFileDiscovery _discovery;
    private readonly FileFilter _filter;
    private readonly ContentFilter _contentFilter;
    private readonly IFileReader _reader;
    private readonly IMarkdownGenerator _generator;
    private readonly IClipboardService _clipboard;
    private readonly ILogger _logger;

    public GenerateContextUseCase(
        IFileDiscovery discovery,
        FileFilter filter,
        ContentFilter contentFilter,
        IFileReader reader,
        IMarkdownGenerator generator,
        IClipboardService clipboard,
        ILogger logger)
    {
        _discovery = discovery;
        _filter = filter;
        _contentFilter = contentFilter;
        _reader = reader;
        _generator = generator;
        _clipboard = clipboard;
        _logger = logger;
    }

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
        bool compress = false,
        bool noCache = false,
        string[]? excludePathPatterns = null,
        string[]? includePathPatterns = null,
        string[]? excludeContentPatterns = null,
        string[]? includeContentPatterns = null,
        CancellationToken ct = default)
    {
        var discoveryResult = await _discovery.DiscoverFilesAsync(rootPath, config, ct);
        if (!discoveryResult.IsSuccess) return Result.Failure(discoveryResult.Error!);

        var filterResult = _filter.FilterFiles(
            discoveryResult.Value!, config, paths, excludes, extensions,
            excludePathPatterns, includePathPatterns);
        if (!filterResult.IsSuccess) return Result.Failure(filterResult.Error!);

        var entries = filterResult.Value!.ToList();
        if (!entries.Any())
        {
            _logger.Success("No files match the specified filters.");
            return Result.Success();
        }

        var fullPaths = entries.Select(e => Path.Combine(rootPath, e.Path));
        _ = gc.Application.Native.LinuxFastPath.PrewarmAsync(fullPaths, entries.Count);

        _logger.Success("Processing...");

        var contents = entries.Select(e => new FileContent(e, null, e.Size));

        return await WriteOutputAsync(contents, entries.Count, rootPath, config, outputFile, appendMode,
            excludeLineIfStart, brainMode, compress, noCache, excludeContentPatterns, includeContentPatterns, ct);
    }

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
        bool compress = false,
        bool noCache = false,
        string[]? excludePathPatterns = null,
        string[]? includePathPatterns = null,
        string[]? excludeContentPatterns = null,
        string[]? includeContentPatterns = null,
        CancellationToken ct = default)
    {
        var clusterConfig = config.Discovery?.Cluster ?? new ClusterConfiguration();
        if (clusterConfig.MaxDepth <= 0) clusterConfig = clusterConfig with { MaxDepth = 2 };

        var reposResult = await _discovery.DiscoverGitReposAsync(clusterRoot, clusterConfig, ct);
        if (!reposResult.IsSuccess) return Result.Failure(reposResult.Error!);

        var repos = reposResult.Value!;
        if (repos.Count == 0)
        {
            _logger.Warning("No git repositories found in cluster directory. Nothing to process.");
            return Result.Success();
        }

        _logger.Info($"Processing {repos.Count} repos in cluster mode...");

        var repoEntries = new List<(RepoInfo Repo, List<FileEntry> Entries)>();
        var errors = new List<string>();
        var maxParallel = clusterConfig.MaxParallelRepos > 0
            ? clusterConfig.MaxParallelRepos
            : Environment.ProcessorCount;

        var lockObj = new object();
        await Parallel.ForEachAsync(repos, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Min(maxParallel, repos.Count),
            CancellationToken = ct
        }, async (repo, token) =>
        {
            try
            {
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

                var filterResult = _filter.FilterFiles(
                    discoveryResult.Value!, config, paths, excludes, extensions,
                    excludePathPatterns, includePathPatterns);
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

        foreach (var error in errors)
        {
            _logger.Warning($"Repo error: {error}");
        }

        if (repoEntries.Count == 0)
        {
            _logger.Warning("No files found in any of the discovered repos.");
            return Result.Success();
        }

        var sorted = repoEntries
            .OrderBy(r => r.Repo.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totalFiles = sorted.Sum(s => s.Entries.Count);

        var allContents = BuildClusterContents(sorted, clusterConfig, clusterRoot);
        var allEntries = sorted.SelectMany(s => s.Entries).ToList();

        Native.LinuxFastPath.PrewarmAsync(allEntries.Select(e => e.Path), allEntries.Count);

        _logger.Success($"Processing {totalFiles} files from {sorted.Count} repos...");

        return await WriteOutputAsync(allContents, totalFiles, clusterRoot, config, outputFile, appendMode,
            excludeLineIfStart, brainMode, compress, noCache, excludeContentPatterns, includeContentPatterns, ct);
    }

    private IEnumerable<FileContent> BuildClusterContents(
        List<(RepoInfo Repo, List<FileEntry> Entries)> sorted,
        ClusterConfiguration clusterConfig,
        string clusterRoot)
    {
        for (int i = 0; i < sorted.Count; i++)
        {
            var (repo, entries) = sorted[i];

            if (clusterConfig.IncludeRepoHeader && i > 0)
            {
                var separatorContent = $"{clusterConfig.RepoSeparator}\n# {repo.Name}\n{clusterConfig.RepoSeparator}";
                var separatorEntry = new FileEntry($"[{clusterConfig.RepoSeparator}] {repo.RelativePath}", "", "markdown", separatorContent.Length);
                yield return new FileContent(separatorEntry, separatorContent, separatorContent.Length);
            }
            else if (clusterConfig.IncludeRepoHeader && i == 0)
            {
                var headerContent = $"# {repo.Name}";
                var headerEntry = new FileEntry($"[header] {repo.RelativePath}", "", "markdown", headerContent.Length);
                yield return new FileContent(headerEntry, headerContent, headerContent.Length);
            }

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
        bool compress,
        bool noCache,
        string[]? excludeContentPatterns,
        string[]? includeContentPatterns,
        CancellationToken ct)
    {
        // Apply content filters — only for actual files, not metadata entries starting with '['
        var exPatterns = excludeContentPatterns ?? Array.Empty<string>();
        var inPatterns = includeContentPatterns ?? Array.Empty<string>();
        var filteredList = new List<FileContent>();

        foreach (var content in contents)
        {
            if (content.Entry.Path.StartsWith('['))
            {
                filteredList.Add(content);
                continue;
            }

            // For files with embedded content, apply content filter immediately
            if (content.Content != null && !_contentFilter.ShouldInclude(content.Content, exPatterns, inPatterns))
            {
                continue;
            }

            filteredList.Add(content);
        }

        int actualFileCount = filteredList.Count(c => !c.Entry.Path.StartsWith('['));

        var crusher = brainMode ? new BrainCrusher() : null;
        var dynamicCompressor = brainMode ? new DynamicCompressor() : null;
        SqzCompressionService? sqzService = compress ? new SqzCompressionService() : null;

        if (compress && !sqzService!.IsAvailable)
        {
            _logger.Warning(SqzCompressionService.InstallHint);
            _logger.Warning("Compression disabled for this run.");
            compress = false;
            sqzService = null;
        }

        if (!string.IsNullOrEmpty(outputFile))
        {
            var outputDir = Path.GetDirectoryName(outputFile);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            bool shouldAppend = appendMode && File.Exists(outputFile);
            FileMode fileMode = shouldAppend ? FileMode.Append : FileMode.Create;

            if (brainMode)
            {
                using var ms = new MemoryStream();
                var genResult = await _generator.GenerateMarkdownStreamingAsync(filteredList, ms, config, excludeLineIfStart, null, ct);
                if (!genResult.IsSuccess) return Result.Failure(genResult.Error!);

                ms.Position = 0;
                using var reader = new StreamReader(ms, Encoding.UTF8);
                var rawOutput = await reader.ReadToEndAsync(ct);

                string finalOutput;
                string dynInfo = "";

                if (compress)
                {
                    var afterCrush = crusher!.Crush(rawOutput);
                    finalOutput = LlmContextHeader + await sqzService!.CompressAsync(afterCrush, noCache);
                }
                else
                {
                    var dynResult = dynamicCompressor!.Compress(rawOutput);
                    var afterCrush = crusher!.Crush(dynResult.Output);
                    finalOutput = dynResult.Legend + afterCrush;
                    dynInfo = dynResult.ReplacementCount > 0 ? $" | Dynamic: {dynResult.ReplacementCount} replacements, ~{dynResult.TokensSaved} tokens saved" : "";
                }

                var finalBytes = Encoding.UTF8.GetBytes(finalOutput);

                using var fs = new FileStream(outputFile, fileMode, FileAccess.Write, FileShare.None, 4096, useAsync: true);
                await fs.WriteAsync(finalBytes, ct);

                string action = shouldAppend && fileMode == FileMode.Append ? "Appended to" : "Exported to";
                _logger.Success($"{action} {outputFile}: {actualFileCount} files | Size: {Formatting.FormatSize(finalBytes.Length)} | BrainMode ON{dynInfo} | Tokens: ~{finalBytes.Length / 4}");

                return Result.Success();
            }

            if (compress)
            {
                using var ms = new MemoryStream();
                var genResult = await _generator.GenerateMarkdownStreamingAsync(filteredList, ms, config, excludeLineIfStart, null, ct);
                if (!genResult.IsSuccess) return Result.Failure(genResult.Error!);

                ms.Position = 0;
                using var reader = new StreamReader(ms, Encoding.UTF8);
                var rawOutput = await reader.ReadToEndAsync(ct);

                var compressedOutput = await sqzService!.CompressAsync(rawOutput, noCache);
                var finalBytes = Encoding.UTF8.GetBytes(LlmContextHeader + compressedOutput);

                using var fs = new FileStream(outputFile, fileMode, FileAccess.Write, FileShare.None, 4096, useAsync: true);
                await fs.WriteAsync(finalBytes, ct);

                string action = shouldAppend && fileMode == FileMode.Append ? "Appended to" : "Exported to";
                _logger.Success($"✔ {action} {outputFile}: {actualFileCount} files | Size: {Formatting.FormatSize(finalBytes.Length)} | Compressed (sqz) | Tokens: ~{finalBytes.Length / 4}");

                return Result.Success();
            }

            using var fs2 = new FileStream(outputFile, fileMode, FileAccess.Write, FileShare.None, 4096, useAsync: true);

            var genResult2 = await _generator.GenerateMarkdownStreamingAsync(filteredList, fs2, config, excludeLineIfStart, null, ct);
            if (!genResult2.IsSuccess) return Result.Failure(genResult2.Error!);

            string action2 = shouldAppend && fileMode == FileMode.Append ? "Appended to" : "Exported to";
            _logger.Success($"✔ {action2} {outputFile}: {actualFileCount} files | Size: {Formatting.FormatSize(genResult2.Value)} | Tokens: ~{genResult2.Value / 4}");

            return Result.Success();
        }
        else
        {
            using var ms = new MemoryStream();
            var genResult = await _generator.GenerateMarkdownStreamingAsync(filteredList, ms, config, excludeLineIfStart, null, ct);
            if (!genResult.IsSuccess) return Result.Failure(genResult.Error!);

            if (brainMode)
            {
                ms.Position = 0;
                using var reader = new StreamReader(ms, Encoding.UTF8);
                var rawOutput = await reader.ReadToEndAsync(ct);

                string finalOutput;
                string dynInfo = "";

                if (compress)
                {
                    var afterCrush = crusher!.Crush(rawOutput);
                    finalOutput = LlmContextHeader + await sqzService!.CompressAsync(afterCrush, noCache);
                }
                else
                {
                    var dynResult = dynamicCompressor!.Compress(rawOutput);
                    var afterCrush = crusher!.Crush(dynResult.Output);
                    finalOutput = dynResult.Legend + afterCrush;
                    dynInfo = dynResult.ReplacementCount > 0 ? $" | Dynamic: {dynResult.ReplacementCount} replacements, ~{dynResult.TokensSaved} tokens saved" : "";
                }

                var finalBytes = Encoding.UTF8.GetBytes(finalOutput);

                using var finalMs = new MemoryStream(finalBytes);
                finalMs.Position = 0;
                var clipResult = await _clipboard.CopyToClipboardAsync(finalMs, config.Limits, appendMode, ct);
                if (!clipResult.IsSuccess) return Result.Failure(clipResult.Error!);

                _logger.Success($"✔ Copied: {actualFileCount} files | Size: {Formatting.FormatSize(finalBytes.Length)} | BrainMode ON{dynInfo} | Tokens: ~{finalBytes.Length / 4}");

                return Result.Success();
            }

            if (compress)
            {
                ms.Position = 0;
                using var reader = new StreamReader(ms, Encoding.UTF8);
                var rawOutput = await reader.ReadToEndAsync(ct);

                var compressedOutput = await sqzService!.CompressAsync(rawOutput, noCache);
                var finalBytes = Encoding.UTF8.GetBytes(LlmContextHeader + compressedOutput);

                using var finalMs = new MemoryStream(finalBytes);
                finalMs.Position = 0;
                var clipResult = await _clipboard.CopyToClipboardAsync(finalMs, config.Limits, appendMode, ct);
                if (!clipResult.IsSuccess) return Result.Failure(clipResult.Error!);

                _logger.Success($"✔ Copied: {actualFileCount} files | Size: {Formatting.FormatSize(finalBytes.Length)} | Compressed (sqz) | Tokens: ~{finalBytes.Length / 4}");

                return Result.Success();
            }

            ms.Position = 0;
            var clipResult2 = await _clipboard.CopyToClipboardAsync(ms, config.Limits, appendMode, ct);
            if (!clipResult2.IsSuccess) return Result.Failure(clipResult2.Error!);

            _logger.Success($"✔ Copied: {actualFileCount} files | Size: {Formatting.FormatSize(genResult.Value)} | Tokens: ~{genResult.Value / 4}");

            return Result.Success();
        }
    }
}
