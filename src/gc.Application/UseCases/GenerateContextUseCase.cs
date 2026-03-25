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
        bool appendMode = false,
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

        _logger.Success("Processing...");

        // Stream contents lazily
        var contents = entries.Select(e => new FileContent(e, null, e.Size));

        if (!string.IsNullOrEmpty(outputFile))
        {
            // Ensure parent directory exists
            var outputDir = Path.GetDirectoryName(outputFile);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // Check if we should append to existing file
            bool shouldAppend = appendMode && File.Exists(outputFile);
            FileMode fileMode = shouldAppend ? FileMode.Append : FileMode.Create;

            // Note: When user explicitly passes --append (appendMode=true), we honor it.
            // Time window checks are NOT applied when user explicitly requests append mode.

            using var fs = new FileStream(outputFile, fileMode, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            
            var genResult = await _generator.GenerateMarkdownStreamingAsync(contents, fs, config, ct);
            if (!genResult.IsSuccess) return Result.Failure(genResult.Error!);

            string action = shouldAppend && fileMode == FileMode.Append ? "Appended to" : "Exported to";
            _logger.Success($"✔ {action} {outputFile}: {entries.Count} files | Size: {Formatting.FormatSize(genResult.Value)} | Tokens: ~{genResult.Value / 4}");

            return Result.Success();
        }
        else
        {
            // Use a temporary memory stream to capture the output for the clipboard
            using var ms = new MemoryStream();
            var genResult = await _generator.GenerateMarkdownStreamingAsync(contents, ms, config, ct);
            if (!genResult.IsSuccess) return Result.Failure(genResult.Error!);

            ms.Position = 0;
            
            var clipResult = await _clipboard.CopyToClipboardAsync(ms, config.Limits, ct);
            if (!clipResult.IsSuccess) return Result.Failure(clipResult.Error!);

            _logger.Success($"✔ Copied: {entries.Count} files | Size: {Formatting.FormatSize(genResult.Value)} | Tokens: ~{genResult.Value / 4}");

            return Result.Success();
        }
    }
}
