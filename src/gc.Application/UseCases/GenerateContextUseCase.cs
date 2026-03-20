using gc.Domain.Common;
using gc.Domain.Interfaces;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;
using gc.Application.Services;

namespace gc.Application.UseCases;

public sealed class GenerateContextUseCase
{
    private readonly IFileDiscovery _discovery;
    private readonly FileFilter _filter;
    private readonly IFileReader _reader;
    private readonly IMarkdownGenerator _generator;
    private readonly IClipboardService _clipboard;
    private readonly ILogger _logger;

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

    public async Task<Result> ExecuteAsync(
        string rootPath,
        GcConfiguration config,
        IEnumerable<string> paths,
        IEnumerable<string> excludes,
        IEnumerable<string> extensions,
        string? outputFile,
        CancellationToken ct = default)
    {
        _logger.Info("Starting file discovery...");
        var discoveryResult = await _discovery.DiscoverFilesAsync(rootPath, config, ct);
        if (!discoveryResult.IsSuccess) return Result.Failure(discoveryResult.Error!);

        _logger.Info($"Discovered {discoveryResult.Value!.Count()} files. Applying filters...");
        var filterResult = _filter.FilterFiles(discoveryResult.Value!, config, paths, excludes, extensions);
        if (!filterResult.IsSuccess) return Result.Failure(filterResult.Error!);

        var entries = filterResult.Value!.ToList();
        if (!entries.Any()) return Result.Failure("No files match the specified filters.");

        _logger.Info($"Processing {entries.Count} files...");

        // Stream contents lazily
        var contents = entries.Select(e => new FileContent(e, null, e.Size));

        if (!string.IsNullOrEmpty(outputFile))
        {
            using var fs = new FileStream(outputFile, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            var genResult = await _generator.GenerateMarkdownStreamingAsync(contents, fs, config, ct);
            if (!genResult.IsSuccess) return Result.Failure(genResult.Error!);
            
            _logger.Info($"[OK] Exported to {outputFile}. Size: {genResult.Value} bytes.");
            return Result.Success();
        }
        else
        {
            // Use a temporary memory stream to capture the output for the clipboard
            using var ms = new MemoryStream();
            var genResult = await _generator.GenerateMarkdownStreamingAsync(contents, ms, config, ct);
            if (!genResult.IsSuccess) return Result.Failure(genResult.Error!);

            ms.Position = 0;
            _logger.Info("Copying to clipboard...");
            var clipResult = await _clipboard.CopyToClipboardAsync(ms, ct);
            if (!clipResult.IsSuccess) return Result.Failure(clipResult.Error!);

            _logger.Info($"[OK] Copied to clipboard. Size: {genResult.Value} bytes.");
            return Result.Success();
        }
    }
}
