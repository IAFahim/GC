using System.Diagnostics;
using System.Text;
using gc.Application.Adapters;
using gc.Application.Services;
using gc.Domain.Common;
using gc.Domain.Interfaces;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;

namespace gc.Application.UseCases;

public sealed class GenerateContextUseCase
{
    private const string LlmContextHeader =
        "[Context compressed by gc+sqz for efficiency. " +
        "This contains the full source code — references like [→L], [×N], «A» are structural markers. " +
        "IMPORTANT: When writing code or answering, use the ORIGINAL full identifiers and patterns shown here. " +
        "Do NOT use abbreviated symbols or short-form in your output. Respond as if you received uncompressed source.]\n\n";

    private readonly IClipboardService _clipboard;
    private readonly ContentFilter _contentFilter;

    private readonly IFileDiscovery _discovery;
    private readonly FileFilter _filter;
    private readonly IMarkdownGenerator _generator;
    private readonly ILogger _logger;
    private readonly ShardSplitter _shardSplitter;

    public GenerateContextUseCase(
        IFileDiscovery discovery,
        FileFilter filter,
        ContentFilter contentFilter,
        IMarkdownGenerator generator,
        IClipboardService clipboard,
        ILogger logger)
    {
        _discovery = discovery;
        _filter = filter;
        _contentFilter = contentFilter;
        _generator = generator;
        _clipboard = clipboard;
        _logger = logger;
        _shardSplitter = new ShardSplitter();
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
        CancellationToken ct = default,
        ProfileReporter? profileReporter = null,
        bool unsafeDirectWrite = false,
        string? changedSince = null,
        ShardInfo? shardInfo = null)
    {
        var sw = Stopwatch.StartNew();
        Result<IEnumerable<string>> discoveryResult;

        if (!string.IsNullOrEmpty(changedSince))
        {
            _logger.Info($"Discovering files changed since {changedSince}...");
            discoveryResult = await _discovery.DiscoverFilesSinceAsync(rootPath, changedSince, config, ct);
        }
        else
        {
            discoveryResult = await _discovery.DiscoverFilesAsync(rootPath, config, ct);
        }

        profileReporter?.RecordStage("Discovery", sw.ElapsedTicks);
        if (!discoveryResult.IsSuccess) return Result.Failure(discoveryResult.Error!);

        sw.Restart();
        var filterResult = _filter.FilterFiles(
            discoveryResult.Value!, config, paths, excludes, extensions,
            excludePathPatterns, includePathPatterns, rootPath, rootPath);
        profileReporter?.RecordStage("Filtering", sw.ElapsedTicks);
        if (!filterResult.IsSuccess) return Result.Failure(filterResult.Error!);

        var entries = filterResult.Value!.ToList();
        if (entries.Count == 0)
        {
            _logger.Success("No files match the specified filters.");
            return Result.Success();
        }

        profileReporter?.RecordMetric("DiscoveredFiles", entries.Count.ToString());

        // Shard mode: break into N pieces and take one slice
        if (shardInfo != null && shardInfo.Of > 1)
        {
            _logger.Info($"Shard mode: processing slice {shardInfo.Slice} of {shardInfo.Of}...");
            var shardLists = _shardSplitter.SplitIntoShards(entries, shardInfo.Of, shardInfo.Slice, _logger);
            var shardEntries = shardLists.Count >= shardInfo.Slice ? shardLists[shardInfo.Slice - 1] : entries;
            var shardContents = shardEntries.Select(e => new FileContent(e, null, e.Size));
            return await WriteOutputAsync(shardContents, shardEntries.Count, rootPath, config, outputFile, appendMode,
                excludeLineIfStart, brainMode, compress, noCache, excludeContentPatterns, includeContentPatterns, ct,
                profileReporter, unsafeDirectWrite);
        }

        _logger.Success("Processing...");

        var contents = entries.Select(e => new FileContent(e, null, e.Size));

        return await WriteOutputAsync(contents, entries.Count, rootPath, config, outputFile, appendMode,
            excludeLineIfStart, brainMode, compress, noCache, excludeContentPatterns, includeContentPatterns, ct,
            profileReporter, unsafeDirectWrite);
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
        CancellationToken ct = default,
        ProfileReporter? profileReporter = null,
        bool unsafeDirectWrite = false,
        string? changedSince = null,
        ShardInfo? shardInfo = null)
    {
        var clusterConfig = config.Discovery?.Cluster ?? new ClusterConfiguration();
        if (clusterConfig.MaxDepth is null or <= 0) clusterConfig = clusterConfig with { MaxDepth = 2 };

        var sw = Stopwatch.StartNew();
        var reposResult = await _discovery.DiscoverGitReposAsync(clusterRoot, clusterConfig, ct);
        profileReporter?.RecordStage("ClusterDiscovery", sw.ElapsedTicks);
        if (!reposResult.IsSuccess) return Result.Failure(reposResult.Error!);

        var repos = reposResult.Value!;
        if (repos.Count == 0)
        {
            _logger.Warning("No git repositories found in cluster directory. Nothing to process.");
            return Result.Success();
        }

        profileReporter?.RecordMetric("DiscoveredRepos", repos.Count.ToString());

        _logger.Info($"Processing {repos.Count} repos in cluster mode...");

        sw.Restart();
        var repoEntries = new List<(RepoInfo Repo, List<FileEntry> Entries)>();
        var errors = new List<string>();

        var lockObj = new object();
        var maxParallel = clusterConfig.MaxParallelRepos is > 0 ? clusterConfig.MaxParallelRepos.Value : 1;
        await Parallel.ForEachAsync(repos, new ParallelOptions
        {
            MaxDegreeOfParallelism = maxParallel,
            CancellationToken = ct
        }, async (repo, token) =>
        {
            try
            {
                var discoveryResult = await _discovery.DiscoverFilesAsync(repo.RootPath, config, token);
                if (!discoveryResult.IsSuccess)
                {
                    if (clusterConfig.FailFast == true)
                        throw new InvalidOperationException(
                            $"Discovery failed for {repo.RelativePath}: {discoveryResult.Error}");

                    lock (lockObj)
                    {
                        errors.Add($"{repo.RelativePath}: {discoveryResult.Error}");
                    }

                    return;
                }

                var filterResult = _filter.FilterFiles(
                    discoveryResult.Value!, config, paths, excludes, extensions,
                    excludePathPatterns, includePathPatterns, rootPath: repo.RootPath);
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
                    // Cluster mode: override the root to the repo root and set display path.
                    // Size is already correctly computed from the repo root in CreateFileEntry.
                    var prefixedEntries = entries.Select(e =>
                    {
                        var displayPath = $"{repo.RelativePath}/{e.Relative}";
                        return e with { Root = repo.RootPath, Display = displayPath };
                    }).ToList();

                    lock (lockObj)
                    {
                        repoEntries.Add((repo, prefixedEntries));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (clusterConfig.FailFast == true) throw;

                lock (lockObj)
                {
                    errors.Add($"{repo.RelativePath}: {ex.Message}");
                }
            }
        });
        profileReporter?.RecordStage("ClusterProcessing", sw.ElapsedTicks);

        foreach (var error in errors) _logger.Warning($"Repo error: {error}");

        if (repoEntries.Count == 0)
        {
            _logger.Warning("No files found in any of the discovered repos.");
            return Result.Success();
        }

        var sorted = repoEntries
            .OrderBy(r => r.Repo.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totalFiles = sorted.Sum(s => s.Entries.Count);
        profileReporter?.RecordMetric("TotalFiles", totalFiles.ToString());

        var allContents = BuildClusterContents(sorted, clusterConfig, clusterRoot);

        _logger.Success($"Processing {totalFiles} files from {sorted.Count} repos...");

        // Shard mode: break all repo files into N shards
        if (shardInfo != null && shardInfo.Of > 1)
        {
            _logger.Info($"Shard mode: processing slice {shardInfo.Slice} of {shardInfo.Of}...");
            var flatEntries = sorted.SelectMany(r => r.Entries).ToList();
            var shardLists = _shardSplitter.SplitIntoShards(flatEntries, shardInfo.Of, shardInfo.Slice, _logger);
            var shardEntries = shardLists.Count >= shardInfo.Slice ? shardLists[shardInfo.Slice - 1] : flatEntries;
            var shardContents = shardEntries.Select(e => new FileContent(e, null, e.Size));
            return await WriteOutputAsync(shardContents, shardEntries.Count, clusterRoot, config, outputFile,
                appendMode,
                excludeLineIfStart, brainMode, compress, noCache, excludeContentPatterns, includeContentPatterns, ct,
                profileReporter, unsafeDirectWrite);
        }

        return await WriteOutputAsync(allContents, totalFiles, clusterRoot, config, outputFile, appendMode,
            excludeLineIfStart, brainMode, compress, noCache, excludeContentPatterns, includeContentPatterns, ct,
            profileReporter, unsafeDirectWrite);
    }

    private IEnumerable<FileContent> BuildClusterContents(
        List<(RepoInfo Repo, List<FileEntry> Entries)> sorted,
        ClusterConfiguration clusterConfig,
        string clusterRoot)
    {
        for (var i = 0; i < sorted.Count; i++)
        {
            var (repo, entries) = sorted[i];

            var separator = clusterConfig.RepoSeparator ?? "---";
            if (clusterConfig.IncludeRepoHeader == true && i > 0)
            {
                var separatorContent = $"{separator}\n# {repo.Name}\n{separator}";
                // Use repo root as the "file" location, with a sentinel-like display path.
                // This keeps FileEntry honest - no fake absolute paths.
                var separatorEntry = new FileEntry(
                    repo.RootPath,
                    $"[{separator}]",
                    "",
                    "markdown",
                    separatorContent.Length,
                    $"[{separator}] {repo.RelativePath}");
                yield return new FileContent(separatorEntry, separatorContent, separatorContent.Length);
            }
            else if (clusterConfig.IncludeRepoHeader == true && i == 0)
            {
                var headerContent = $"# {repo.Name}";
                var headerEntry = new FileEntry(
                    repo.RootPath,
                    "[header]",
                    "",
                    "markdown",
                    headerContent.Length,
                    $"[header] {repo.RelativePath}");
                yield return new FileContent(headerEntry, headerContent, headerContent.Length);
            }

            foreach (var entry in entries) yield return new FileContent(entry, null, entry.Size);
        }
    }

    // ========================================================================
    // Output Pipeline — composable transforms, two sinks (file, clipboard)
    // ========================================================================
    //
    // Instead of a 6-way matrix of near-identical blocks (brain × compress × {file, clipboard}),
    // we build a pipeline: IReadOnlyList<IOutputTransform>.
    // Each transform is total and referentially transparent; adding one is one list element, not a new branch.
    //
    // Pipeline combinations:
    //   []                         → no transforms, stream directly to sink
    //   [Dynamic, Crush]          → brain mode
    //   [Sqz]                     → compress only
    //   [Dynamic, Crush, Sqz]     → brain + compress
    // ========================================================================

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
        CancellationToken ct,
        ProfileReporter? profileReporter = null,
        bool unsafeDirectWrite = false)
    {
        var sw = Stopwatch.StartNew();

        // ── 1. Compile content filter & filter entries ──
        var compiled = _contentFilter.CompilePatterns(
            excludeContentPatterns ?? Array.Empty<string>(),
            includeContentPatterns ?? Array.Empty<string>());

        var filteredList = new List<FileContent>();
        foreach (var content in contents)
        {
            if (content.Entry.Path.StartsWith('['))
            {
                filteredList.Add(content);
                continue;
            }

            if (content.Content != null && !compiled.ShouldInclude(content.Content))
                continue;
            filteredList.Add(content);
        }

        var actualFileCount = filteredList.Count(c => !c.Entry.Path.StartsWith('['));

        // ── 2. Build the transform pipeline ──
        var transforms = new List<IOutputTransform>();
        SqzCompressionService? sqzService = null;
        BrainCrusher? brainCrusherInstance = brainMode ? new BrainCrusher() : null;

        if (brainMode)
        {
            transforms.Add(new DynamicCompressorAdapter());
        }

        if (compress)
        {
            sqzService = new SqzCompressionService();
            if (!sqzService.IsAvailable)
            {
                _logger.Warning(SqzCompressionService.InstallHint);
                _logger.Warning("Compression disabled for this run.");
                sqzService = null;
            }
            else
            {
                transforms.Add(new SqzCompressionAdapter(sqzService, noCache));
            }
        }

        var pipeline = new OutputPipeline(transforms);
        var hasPipeline = transforms.Count > 0;

        profileReporter?.RecordStage("Preprocessing", sw.ElapsedTicks);

        // ── 3. Generate markdown ──
        // If pipeline exists, materialize to memory for transforms; otherwise stream directly.
        sw.Restart();

        var rawOutput = "";

        if (hasPipeline)
        {
            await using var ms = new MemoryStream();
            var genResult = await _generator.GenerateMarkdownStreamingAsync(filteredList, ms, config, compiled,
                excludeLineIfStart, brainCrusherInstance, ct);
            profileReporter?.RecordStage("Generation", sw.ElapsedTicks);
            if (!genResult.IsSuccess) return Result.Failure(genResult.Error!);

            // Generator emits UTF-8 without BOM, so GetString over the buffer is byte-identical
            // to StreamReader but avoids the StreamReader allocation + intermediate char-buffer copy.
            rawOutput = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);

            // Use the authoritative count of files actually emitted (content-pattern filtering
            // happens inside the generator for streaming entries), not the pre-filter candidate count.
            actualFileCount = (_generator as MarkdownGenerator)?.LastEmittedFileCount ?? actualFileCount;
        }

        // ── 4. Apply pipeline ──
        sw.Restart();
        string finalOutput;
        var transformInfo = "";

        if (hasPipeline)
        {
            var pipelineResult = await pipeline.ApplyAsync(rawOutput, ct);
            finalOutput = pipelineResult.Output;

            // Prepend legend (from dynamic compressor) and LlmContextHeader uniformly
            finalOutput = pipelineResult.Legend + finalOutput;

            if (brainMode || compress)
                finalOutput = LlmContextHeader + finalOutput;

            // Build logging info
            var parts = new List<string>();
            if (brainMode) parts.Add("BrainMode ON");
            if (compress && sqzService != null) parts.Add("Compressed (sqz)");
            if (brainMode && pipelineResult.TokensSaved > 0)
            {
                parts.Add($"Dynamic: ~{pipelineResult.TokensSaved} tokens saved");
                profileReporter?.RecordMetric("BrainReplacements", pipelineResult.TokensSaved.ToString());
            }

            transformInfo = parts.Count > 0 ? $" | {string.Join(" | ", parts)}" : "";

            profileReporter?.RecordStage("Transformation", sw.ElapsedTicks);
        }
        else
        {
            finalOutput = "";
        }

        // ── 5. Emit to sink ──
        sw.Restart();

        if (!string.IsNullOrEmpty(outputFile))
            return await EmitToFile(filteredList, actualFileCount, outputFile, appendMode,
                hasPipeline, finalOutput, compiled, excludeLineIfStart, transformInfo,
                unsafeDirectWrite, config, brainCrusherInstance, ct, sw, profileReporter);

        return await EmitToClipboard(filteredList, actualFileCount, hasPipeline, finalOutput,
            compiled, excludeLineIfStart, transformInfo, config, appendMode, brainCrusherInstance, ct, sw,
            profileReporter);
    }

    private async Task<Result> EmitToFile(
        List<FileContent> filteredList,
        int actualFileCount,
        string outputFile,
        bool shouldAppend,
        bool hasPipeline,
        string finalOutput,
        CompiledContentPatterns compiled,
        IEnumerable<string>? excludeLineIfStart,
        string transformInfo,
        bool unsafeDirectWrite,
        GcConfiguration config,
        BrainCrusher? brainCrusher,
        CancellationToken ct,
        Stopwatch sw,
        ProfileReporter? profileReporter)
    {
        var outputDir = Path.GetDirectoryName(outputFile);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        var isAppend = shouldAppend && File.Exists(outputFile);

        if (hasPipeline)
        {
            var finalBytes = Encoding.UTF8.GetBytes(finalOutput);

            if (unsafeDirectWrite || isAppend)
            {
                var fileMode = isAppend ? FileMode.Append : FileMode.Create;
                await using var fs = new FileStream(outputFile, fileMode, FileAccess.Write, FileShare.None, 4096, true);
                await fs.WriteAsync(finalBytes, ct);
            }
            else
            {
                await SafeFileWriter.WriteAllBytesAsync(outputFile, finalBytes, ct);
            }

            var action = isAppend ? "Appended to" : "Exported to";
            _logger.Success(
                $"✔ {action} {outputFile}: {actualFileCount} files | Size: {Formatting.FormatSize(finalBytes.Length)}{transformInfo} | Tokens: ~{TokenEstimator.EstimateTokens(finalOutput)}");
            profileReporter?.RecordMetric("OutputSize", finalBytes.Length.ToString());
            return Result.Success();
        }

        // No pipeline — stream directly to file
        if (unsafeDirectWrite || isAppend)
        {
            var fileMode = isAppend ? FileMode.Append : FileMode.Create;
            await using var fs = new FileStream(outputFile, fileMode, FileAccess.Write, FileShare.None, 4096, true);
            var genResult = await _generator.GenerateMarkdownStreamingAsync(filteredList, fs, config, compiled,
                excludeLineIfStart, brainCrusher, ct);
            profileReporter?.RecordStage("Generation", sw.ElapsedTicks);
            if (!genResult.IsSuccess) return Result.Failure(genResult.Error!);

            var estimatedTokens = (_generator as MarkdownGenerator)?.LastEstimatedTokens ?? (genResult.Value / 4);
            var emittedCount = (_generator as MarkdownGenerator)?.LastEmittedFileCount ?? actualFileCount;
            var action = isAppend ? "Appended to" : "Exported to";
            _logger.Success(
                $"✔ {action} {outputFile}: {emittedCount} files | Size: {Formatting.FormatSize(genResult.Value)} | Tokens: ~{estimatedTokens}");
            profileReporter?.RecordMetric("OutputSize", genResult.Value.ToString());
        }
        else
        {
            await using var ms = new MemoryStream();
            var genResult = await _generator.GenerateMarkdownStreamingAsync(filteredList, ms, config, compiled,
                excludeLineIfStart, brainCrusher, ct);
            profileReporter?.RecordStage("Generation", sw.ElapsedTicks);
            if (!genResult.IsSuccess) return Result.Failure(genResult.Error!);

            await SafeFileWriter.WriteAllBytesAsync(outputFile, ms.GetBuffer().AsMemory(0, (int)ms.Length), ct);

            var estimatedTokens = (_generator as MarkdownGenerator)?.LastEstimatedTokens ?? (genResult.Value / 4);
            var emittedCount = (_generator as MarkdownGenerator)?.LastEmittedFileCount ?? actualFileCount;
            _logger.Success(
                $"✔ Exported to {outputFile}: {emittedCount} files | Size: {Formatting.FormatSize(genResult.Value)} | Tokens: ~{estimatedTokens}");
            profileReporter?.RecordMetric("OutputSize", genResult.Value.ToString());
        }

        return Result.Success();
    }

    private async Task<Result> EmitToClipboard(
        List<FileContent> filteredList,
        int actualFileCount,
        bool hasPipeline,
        string finalOutput,
        CompiledContentPatterns compiled,
        IEnumerable<string>? excludeLineIfStart,
        string transformInfo,
        GcConfiguration config,
        bool appendMode,
        BrainCrusher? brainCrusher,
        CancellationToken ct,
        Stopwatch sw,
        ProfileReporter? profileReporter)
    {
        var noClipboard = config.Output.NoClipboard == true;

        if (hasPipeline)
        {
            var finalBytes = Encoding.UTF8.GetBytes(finalOutput);
            if (!noClipboard)
            {
                using var finalMs = new MemoryStream(finalBytes);
                finalMs.Position = 0;
                var clipResult = await _clipboard.CopyToClipboardAsync(finalMs, config.Limits, appendMode, ct);
                profileReporter?.RecordStage("Clipboard", sw.ElapsedTicks);
                if (!clipResult.IsSuccess) return Result.Failure(clipResult.Error!);
            }

            var prefix = noClipboard ? "✔ [Clipboard Skipped]" : "✔ Copied";
            _logger.Success(
                $"{prefix}: {actualFileCount} files | Size: {Formatting.FormatSize(finalBytes.Length)}{transformInfo} | Tokens: ~{TokenEstimator.EstimateTokens(finalOutput)}");
            profileReporter?.RecordMetric("OutputSize", finalBytes.Length.ToString());
            return Result.Success();
        }

        // No pipeline — stream directly to clipboard
        await using var ms = new MemoryStream();
        var genResult =
            await _generator.GenerateMarkdownStreamingAsync(filteredList, ms, config, compiled, excludeLineIfStart,
                brainCrusher, ct);
        profileReporter?.RecordStage("Generation", sw.ElapsedTicks);
        if (!genResult.IsSuccess) return Result.Failure(genResult.Error!);

        if (!noClipboard)
        {
            ms.Position = 0;
            var clipResult2 = await _clipboard.CopyToClipboardAsync(ms, config.Limits, appendMode, ct);
            profileReporter?.RecordStage("Clipboard", sw.ElapsedTicks);
            if (!clipResult2.IsSuccess) return Result.Failure(clipResult2.Error!);
        }

        var estimatedTokens = (_generator as MarkdownGenerator)?.LastEstimatedTokens ?? (genResult.Value / 4);
        var emittedCount = (_generator as MarkdownGenerator)?.LastEmittedFileCount ?? actualFileCount;
        var prefix2 = noClipboard ? "✔ [Clipboard Skipped]" : "✔ Copied";
        _logger.Success(
            $"{prefix2}: {emittedCount} files | Size: {Formatting.FormatSize(genResult.Value)} | Tokens: ~{estimatedTokens}");
        profileReporter?.RecordMetric("OutputSize", genResult.Value.ToString());
        return Result.Success();
    }
}