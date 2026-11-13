using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using gc.Application.Native;
using gc.Domain.Common;
using gc.Domain.Interfaces;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;
using Microsoft.Win32.SafeHandles;
using CompiledContentPatterns = gc.Domain.Interfaces.CompiledContentPatterns;

namespace gc.Application.Services;

public sealed class MarkdownGenerator : IMarkdownGenerator
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private static readonly int NewlineByteCount = Utf8NoBom.GetByteCount(Environment.NewLine);
    private static readonly int DefaultFenceByteCount = Utf8NoBom.GetByteCount("```");
    private static readonly byte[] NewlineBytes = Utf8NoBom.GetBytes(Environment.NewLine);

    private static readonly SearchValues<byte> NullByte = SearchValues.Create(0);

    [ThreadStatic] private static StringBuilder? t_lineExclusionBuilder;

    private readonly ILogger _logger;

    public MarkdownGenerator(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<Result<long>> GenerateMarkdownStreamingAsync(IEnumerable<FileContent> contents,
        Stream outputStream, GcConfiguration config, IEnumerable<string>? excludeLineIfStart = null,
        IBrainCrusher? brainCrusher = null, CancellationToken ct = default)
    {
        return await GenerateMarkdownStreamingAsync(contents, outputStream, config, default, excludeLineIfStart,
            brainCrusher, ct);
    }

    public long LastEstimatedTokens { get; private set; }

    public int LastEmittedFileCount { get; private set; }



    public async Task<Result<long>> GenerateMarkdownStreamingAsync(IEnumerable<FileContent> contents,
        Stream outputStream, GcConfiguration config, CompiledContentPatterns contentFilter,
        IEnumerable<string>? excludeLineIfStart = null, IBrainCrusher? brainCrusher = null,
        CancellationToken ct = default)
    {
        PipeWriter? writer = null;
        try
        {
            LastEstimatedTokens = 0;
            LastEmittedFileCount = 0;

            string[] excludeArray = excludeLineIfStart as string[]
                ?? excludeLineIfStart?.ToArray()
                ?? Array.Empty<string>();
            bool hasExclude = excludeArray.Length > 0;
            bool excludeNewline = Array.IndexOf(excludeArray, "\n") >= 0;

            var pipeOptions = new StreamPipeWriterOptions(leaveOpen: true, minimumBufferSize: 65536);
            writer = PipeWriter.Create(outputStream, pipeOptions);
            var boundaries = outputStream as IMarkdownBoundarySink;

            var sortedContents = config.Output.SortByPath.GetValueOrDefault()
                ? contents.OrderBy(c => c.Entry.DisplayPath ?? c.Entry.Path, StringComparer.OrdinalIgnoreCase)
                : contents;

            long totalBytes = 0;
            var fileList = new List<string>();
            var maxMemoryBytes = config.Limits.GetMaxMemoryBytesValue();
            var maxFileSize = config.Limits.GetMaxFileSizeBytes();

            var degreeOfParallelism = Environment.ProcessorCount;
            var maxPendingFiles = degreeOfParallelism * 2;

            var sortedList = sortedContents.ToList();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = cts.Token;
            using var readWindow = new SemaphoreSlim(maxPendingFiles);

            // Admit indices in order and release only after ordered consumption. A bounded
            // channel alone cannot bound the dictionary when an early file reads slowly.
            async IAsyncEnumerable<int> WindowedIndices([EnumeratorCancellation] CancellationToken readToken)
            {
                for (var index = 0; index < sortedList.Count; index++)
                {
                    await readWindow.WaitAsync(readToken);
                    yield return index;
                }
            }

            // On the plain path (no brain-crush, no line-exclude, no content filter) the per-file CPU —
            // trailing-trim and the content token estimate — is pure and order-independent, so the parallel
            // worker computes it and the reader does only ordered budget-gating + memcpy + summation. The
            // binary scan and fence-run scan are computed in the worker for every file regardless of mode.
            var plainPath = brainCrusher == null && !hasExclude && contentFilter.IsEmpty;

            var channel = Channel.CreateBounded<ReadResult>(
                new BoundedChannelOptions(maxPendingFiles) { SingleReader = true, SingleWriter = false });

            var generateTask = Task.Run(async () =>
            {
                try
                {
                    await Parallel.ForEachAsync(
                        WindowedIndices(token),
                        new ParallelOptions { MaxDegreeOfParallelism = degreeOfParallelism, CancellationToken = token },
                        async (index, threadToken) =>
                        {
                            var content = sortedList[index];
                            if (content.Content != null)
                            {
                                await channel.Writer.WriteAsync(ReadResult.Marker(index), threadToken);
                                return;
                            }

                            try
                            {
                                // Compute the absolute path once: FileEntry.AbsolutePath runs
                                // Path.GetFullPath+Combine on every access, and this hot parallel loop
                                // would otherwise normalize it repeatedly.
                                var absolutePath = content.Entry.AbsolutePath;

                                // Open FIRST, then fstat once via RandomAccess.GetLength — this removes the
                                // separate FileInfo stat (a syscall + a heap alloc per file). "File not found"
                                // is derived from open() failing with ENOENT; any other errno falls through to
                                // the managed File.OpenHandle so its typed exception reproduces today's
                                // [Error reading file: <ex.Message>] bytes verbatim via the catch below.
                                SafeFileHandle? handle = null;
                                if (OperatingSystem.IsLinux())
                                {
                                    // O_NONBLOCK: a FIFO/named pipe with no writer would otherwise block
                                    // open() forever and hang the whole pipeline. It is a no-op for regular
                                    // file reads, so this only changes the pathological special-file case.
                                    var fd = LinuxFastPath.open(absolutePath,
                                        LinuxFastPath.O_RDONLY | LinuxFastPath.O_NONBLOCK | LinuxFastPath.O_NOATIME |
                                        LinuxFastPath.O_CLOEXEC);
                                    if (fd < 0)
                                        fd = LinuxFastPath.open(absolutePath,
                                            LinuxFastPath.O_RDONLY | LinuxFastPath.O_NONBLOCK | LinuxFastPath.O_CLOEXEC);
                                    if (fd >= 0)
                                    {
                                        handle = new SafeFileHandle(fd, true);
                                    }
                                    else if (Marshal.GetLastPInvokeError() == LinuxFastPath.ENOENT)
                                    {
                                        await channel.Writer.WriteAsync(
                                            ReadResult.Failure(index,
                                                $"File not found: {SanitizePathForOutput(content.Entry.DisplayPath ?? content.Entry.RelativePath)}"),
                                            threadToken);
                                        return;
                                    }
                                    // else: fall through to the managed open below.
                                }

                                handle ??= File.OpenHandle(absolutePath, FileMode.Open, FileAccess.Read,
                                    FileShare.ReadWrite, FileOptions.SequentialScan);

                                using (handle)
                                {
                                    var fileLength = RandomAccess.GetLength(handle);

                                    if (fileLength > maxFileSize)
                                    {
                                        await channel.Writer.WriteAsync(
                                            ReadResult.Failure(index,
                                                $"File size ({fileLength} bytes) exceeds maximum allowed size ({maxFileSize} bytes)"),
                                            threadToken);
                                        return;
                                    }

                                    // The file is already gated by maxFileSize above; read it whole up to the
                                    // single-buffer ceiling (int.MaxValue). Only genuinely enormous files
                                    // (>2 GB, unbufferable in one array) fall through to the placeholder, so a
                                    // user who raises maxFileSize past 10 MB now gets the real content.
                                    if (fileLength <= int.MaxValue)
                                    {
                                        var len = (int)fileLength;

                                        // posix_fadvise only earns its syscall on files large enough for kernel
                                        // readahead to matter; for the common small file it is pure overhead.
                                        if (OperatingSystem.IsLinux() && len > 128 * 1024)
                                            LinuxFastPath.posix_fadvise((int)handle.DangerousGetHandle(), 0, 0,
                                                LinuxFastPath.POSIX_FADV_SEQUENTIAL);

                                        var buffer = ArrayPool<byte>.Shared.Rent(len);
                                        var deposited = false;
                                        try
                                        {
                                            // Synchronous read: the handle is opened synchronously, so ReadAsync
                                            // would only bounce the blocking read onto a thread-pool thread —
                                            // wasteful since parallelism already comes from Parallel.ForEachAsync.
                                            // The fill loop is mandatory: a single read may return short of EOF.
                                            var bytesRead = 0;
                                            while (bytesRead < len)
                                            {
                                                var n = RandomAccess.Read(handle,
                                                    buffer.AsSpan(bytesRead, len - bytesRead), bytesRead);
                                                if (n == 0) break; // EOF (e.g. concurrent truncation)
                                                bytesRead += n;
                                            }

                                            // Per-file CPU, computed HERE in the parallel worker instead of on the
                                            // single reader: binary detection (NUL in first 4 KB) and the fence-run
                                            // scan for every file, plus the trailing-trim length and the content
                                            // token estimate on the plain path. The reader then only orders,
                                            // budget-gates, memcpys, and sums — collapsing the serial bottleneck.
                                            var isBinary = buffer.AsSpan(0, Math.Min(bytesRead, 4096))
                                                .ContainsAny(NullByte);
                                            var fenceRun = isBinary ? 0 : GetFenceRunForBytes(buffer, bytesRead);
                                            var trimmedLength = -1;
                                            var contentTokens = 0;
                                            if (plainPath && !isBinary)
                                            {
                                                var validLength = bytesRead;
                                                while (validLength > 0)
                                                {
                                                    var b = buffer[validLength - 1];
                                                    if (b == (byte)' ' || b == (byte)'\t' || b == (byte)'\r' ||
                                                        b == (byte)'\n')
                                                        validLength--;
                                                    else
                                                        break;
                                                }

                                                trimmedLength = validLength;
                                                contentTokens =
                                                    TokenEstimator.EstimateTokensUtf8(buffer.AsSpan(0, validLength));
                                            }

                                            await channel.Writer.WriteAsync(
                                                new ReadResult(index, buffer, bytesRead, null, isBinary, fenceRun,
                                                    trimmedLength, contentTokens),
                                                threadToken);
                                            deposited = true;
                                        }
                                        finally
                                        {
                                            if (!deposited) ArrayPool<byte>.Shared.Return(buffer);
                                        }
                                    }
                                    else
                                    {
                                        await channel.Writer.WriteAsync(ReadResult.TooLarge(index), threadToken);
                                    }
                                }
                            }
                            catch (Exception) when (Directory.Exists(content.Entry.AbsolutePath))
                            {
                                // A directory entry (e.g. a git submodule / gitlink that ls-files reports)
                                // cannot be read as a file: open() succeeds on a directory and the read then
                                // fails with EISDIR. Reproduce the historical behavior exactly — a quiet
                                // "File not found" placeholder with no error log — so both the output bytes
                                // and the console output stay identical.
                                await channel.Writer.WriteAsync(
                                    ReadResult.Failure(index,
                                        $"File not found: {SanitizePathForOutput(content.Entry.DisplayPath ?? content.Entry.RelativePath)}"),
                                    threadToken);
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(
                                    $"Failed to read file {SanitizePathForOutput(content.Entry.DisplayPath ?? content.Entry.RelativePath)}",
                                    ex);
                                await channel.Writer.WriteAsync(ReadResult.Failure(index, ex.Message), threadToken);
                            }
                        });
                }
                finally
                {
                    channel.Writer.Complete();
                }
            }, token);

            var nextExpectedIndex = 0;
            // The admission window bounds both channel and out-of-order results together.
            var outOfOrderBuffer = new Dictionary<int, ReadResult>(maxPendingFiles);
            long estimatedTokensAccumulator = 0;

            try
            {
                await foreach (var result in channel.Reader.ReadAllAsync(token))
                {
                    outOfOrderBuffer[result.Index] = result;

                    while (outOfOrderBuffer.TryGetValue(nextExpectedIndex, out var ready))
                    {
                        outOfOrderBuffer.Remove(nextExpectedIndex);
                        var content = sortedList[nextExpectedIndex];
                        nextExpectedIndex++;

                        try
                        {
                            if (content.Content != null)
                            {
                                var actualContent = content.Content;
                                List<int>? sourceLines = null;
                                // Synthetic cluster markers (repo separators/headers, Path "[...]") carry
                                // their own ready-made markdown; never line-filter or brain-crush them, or
                                // a -z prefix / minifier could mangle the separators.
                                var isSynthetic = content.Entry.Path.StartsWith('[');
                                if (hasExclude && !isSynthetic)
                                {
                                    var span = actualContent.AsSpan();
                                    var sb = t_lineExclusionBuilder ??= new StringBuilder(4096);
                                    sb.Clear();
                                    sb.EnsureCapacity(actualContent.Length);
                                    var first = true;
                                    if (boundaries != null) sourceLines = [];
                                    var sourceLine = 0;

                                    foreach (var line in span.EnumerateLines())
                                    {
                                        sourceLine++;
                                        var trimmedLine = line.TrimStart().TrimStart('\uFEFF');
                                        if (excludeNewline && (trimmedLine.IsEmpty || trimmedLine.IsWhiteSpace()))
                                            continue;

                                        var shouldExclude = false;
                                        foreach (var startStr in excludeArray)
                                            if (startStr != "\n" &&
                                                trimmedLine.StartsWith(startStr, StringComparison.Ordinal))
                                            {
                                                shouldExclude = true;
                                                break;
                                            }

                                        if (shouldExclude)
                                            continue;

                                        if (!first) sb.Append('\n');
                                        sb.Append(line);
                                        sourceLines?.Add(sourceLine);
                                        first = false;
                                    }

                                    actualContent = sb.ToString();
                                }

                                var fence = GetFenceForContent(actualContent);

                                var header = (config.Markdown.FileHeaderTemplate ?? "## File: {path}").Replace("{path}",
                                    SanitizePathForOutput(content.Entry.DisplayPath ?? content.Entry.RelativePath),
                                    StringComparison.Ordinal);
                                var headerBytes = Utf8NoBom.GetByteCount(header) + NewlineByteCount;
                                var fenceLine = $"{fence}{content.Entry.Language}";
                                var fenceLineBytes = Utf8NoBom.GetByteCount(fenceLine) + NewlineByteCount;
                                var closingFenceBytes = Utf8NoBom.GetByteCount(fence) + NewlineByteCount;
                                var newlineBytes = NewlineByteCount;

                                actualContent = actualContent.TrimEnd(' ', '\t', '\r', '\n');

                                if (brainCrusher != null && !isSynthetic)
                                    actualContent = brainCrusher.CrushBlock(actualContent, content.Entry.Language);

                                var contentBytes = Utf8NoBom.GetByteCount(actualContent);

                                long entryTotalBytes = headerBytes + fenceLineBytes + contentBytes + closingFenceBytes +
                                                       newlineBytes * 2;

                                if (totalBytes + entryTotalBytes > maxMemoryBytes)
                                {
                                    cts.Cancel();
                                    return Result<long>.Failure(
                                        $"Output size ({totalBytes + entryTotalBytes} bytes) would exceed maximum output limit ({maxMemoryBytes} bytes).");
                                }

                                if (boundaries != null)
                                {
                                    await writer.FlushAsync(token);
                                    boundaries.BeginFile(isSynthetic ? null : SanitizePathForOutput(content.Entry.DisplayPath ?? content.Entry.RelativePath),
                                        entryTotalBytes, Math.Max(fence.Length, Math.Max(GetFenceForContent(header).Length - 1,
                                            GetFenceForContent(fenceLine).Length - 1)));
                                }
                                await WriteStringLineAsync(writer, header, token);
                                await WriteStringLineAsync(writer, fenceLine, token);
                                if (boundaries != null)
                                {
                                    await writer.FlushAsync(token);
                                    boundaries.BeginContent(contentBytes, sourceLines);
                                }
                                await WriteStringAsync(writer, actualContent, token);
                                if (boundaries != null)
                                {
                                    await writer.FlushAsync(token);
                                    boundaries.EndContent();
                                }
                                await WriteStringLineAsync(writer, "", token);
                                await WriteStringLineAsync(writer, fence, token);
                                await WriteStringLineAsync(writer, "", token);
                                if (boundaries != null)
                                {
                                    await writer.FlushAsync(token);
                                    boundaries.EndFile();
                                }

                                estimatedTokensAccumulator += TokenEstimator.EstimateTokens(header);
                                estimatedTokensAccumulator += TokenEstimator.EstimateTokens(fenceLine);
                                estimatedTokensAccumulator += TokenEstimator.EstimateTokens(actualContent);
                                estimatedTokensAccumulator += TokenEstimator.EstimateTokens(fence);

                                fileList.Add(SanitizePathForOutput(content.Entry.DisplayPath ?? content.Entry.RelativePath));
                                totalBytes += entryTotalBytes;
                                if (!content.Entry.Path.StartsWith('[')) LastEmittedFileCount++;
                            }
                            else
                            {
                                if (ready.Error != null)
                                {
                                    var errorMsg = $"[Error reading file: {ready.Error}]";
                                    await WriteMetadataLineAsync(writer, boundaries, errorMsg, token);
                                    totalBytes += Utf8NoBom.GetByteCount(errorMsg) + NewlineByteCount;
                                    estimatedTokensAccumulator += TokenEstimator.EstimateTokens(errorMsg);
                                    continue;
                                }

                                if (ready.Length == -1)
                                {
                                    var errorMsg =
                                        $"[File too large to buffer (>2GB): {SanitizePathForOutput(content.Entry.DisplayPath ?? content.Entry.RelativePath)}]";
                                    await WriteMetadataLineAsync(writer, boundaries, errorMsg, token);
                                    totalBytes += Utf8NoBom.GetByteCount(errorMsg) + NewlineByteCount;
                                    estimatedTokensAccumulator += TokenEstimator.EstimateTokens(errorMsg);
                                    continue;
                                }

                                if (ready.IsBinary)
                                {
                                    var errorMsg =
                                        $"[Skipping binary file: {SanitizePathForOutput(content.Entry.DisplayPath ?? content.Entry.RelativePath)}]";
                                    await WriteMetadataLineAsync(writer, boundaries, errorMsg, token);
                                    totalBytes += Utf8NoBom.GetByteCount(errorMsg) + NewlineByteCount;
                                    estimatedTokensAccumulator += TokenEstimator.EstimateTokens(errorMsg);
                                    continue;
                                }

                                // Apply compiled content filter to preview bytes — fast reject for streaming files.
                                if (!contentFilter.IsEmpty && !contentFilter.ShouldInclude(ready.Buffer!, ready.Length))
                                {
                                    var errorMsg =
                                        $"[Skipped by content filter: {SanitizePathForOutput(content.Entry.DisplayPath ?? content.Entry.RelativePath)}]";
                                    await WriteMetadataLineAsync(writer, boundaries, errorMsg, token);
                                    totalBytes += Utf8NoBom.GetByteCount(errorMsg) + NewlineByteCount;
                                    estimatedTokensAccumulator += TokenEstimator.EstimateTokens(errorMsg);
                                    continue;
                                }

                                // Fence run was scanned in the worker; build the fence string here (the scan,
                                // not this tiny alloc, was the cost). Matches the old new string('`', run+1).
                                var fence = new string('`', ready.FenceRun + 1);

                                var header = (config.Markdown.FileHeaderTemplate ?? "## File: {path}").Replace("{path}",
                                    SanitizePathForOutput(content.Entry.DisplayPath ?? content.Entry.RelativePath),
                                    StringComparison.Ordinal);
                                var headerBytes = Utf8NoBom.GetByteCount(header) + NewlineByteCount;
                                var fenceLine = $"{fence}{content.Entry.Language}";
                                var fenceLineBytes = Utf8NoBom.GetByteCount(fenceLine) + NewlineByteCount;
                                var closingFenceBytes = Utf8NoBom.GetByteCount(fence) + NewlineByteCount;
                                var newlineBytes = NewlineByteCount;

                                // Pre-calculate entry size BEFORE writing to enforce maxMemoryBytes limit
                                long contentBytesWritten = 0;
                                byte[]? contentBuffer = null;
                                string? crushedContent = null;
                                List<int>? sourceLines = null;

                                if (brainCrusher != null)
                                {
                                    var rawText = Utf8NoBom.GetString(ready.Buffer!, 0, ready.Length);
                                    crushedContent = brainCrusher.CrushBlock(rawText, content.Entry.Language);
                                    contentBytesWritten = crushedContent.Length > 0
                                        ? Utf8NoBom.GetByteCount(crushedContent) + NewlineByteCount
                                        : 0;
                                }
                                else if (hasExclude)
                                {
                                    // Reuse the thread-static builder (same as the in-memory path) so the
                                    // streaming exclude path allocates no per-file StringBuilder and avoids
                                    // growth reallocations. The decoded string must stay rooted while the
                                    // span over it is enumerated, so keep it in a local.
                                    var sb = t_lineExclusionBuilder ??= new StringBuilder(4096);
                                    sb.Clear();
                                    sb.EnsureCapacity(ready.Length);
                                    var text = Utf8NoBom.GetString(ready.Buffer!, 0, ready.Length);
                                    var first = true;
                                    if (boundaries != null) sourceLines = [];
                                    var sourceLine = 0;
                                    foreach (var line in text.AsSpan().EnumerateLines())
                                    {
                                        sourceLine++;
                                        var trimmedLine = line.TrimStart().TrimStart('\uFEFF');
                                        if (excludeNewline && (trimmedLine.IsEmpty || trimmedLine.IsWhiteSpace())) continue;
                                        var shouldExclude = false;
                                        foreach (var startStr in excludeArray)
                                            if (startStr != "\n" &&
                                                trimmedLine.StartsWith(startStr, StringComparison.Ordinal))
                                            {
                                                shouldExclude = true;
                                                break;
                                            }

                                        if (shouldExclude) continue;
                                        if (!first) sb.Append('\n');
                                        sb.Append(line);
                                        sourceLines?.Add(sourceLine);
                                        first = false;
                                    }

                                    crushedContent = sb.ToString();
                                    contentBytesWritten = crushedContent.Length > 0
                                        ? Utf8NoBom.GetByteCount(crushedContent) + NewlineByteCount : 0;
                                }
                                else
                                {
                                    contentBuffer = ready.Buffer!;
                                    if (ready.TrimmedLength >= 0)
                                    {
                                        // Plain path: the worker already trimmed trailing whitespace.
                                        contentBytesWritten = ready.TrimmedLength;
                                    }
                                    else
                                    {
                                        // Content-filter path (filter present, no crush/exclude): the worker did
                                        // not precompute, so trim here as before.
                                        var validLength = ready.Length;
                                        while (validLength > 0)
                                        {
                                            var b = contentBuffer[validLength - 1];
                                            if (b == (byte)' ' || b == (byte)'\t' || b == (byte)'\r' || b == (byte)'\n')
                                                validLength--;
                                            else
                                                break;
                                        }

                                        contentBytesWritten = validLength;
                                    }
                                }

                                var needsTrailingNewline = true;
                                if (crushedContent != null && crushedContent.Length == 0) needsTrailingNewline = false;
                                if (contentBuffer != null && contentBytesWritten == 0) needsTrailingNewline = false;

                                var entryTotalBytes = headerBytes + fenceLineBytes + contentBytesWritten +
                                                      closingFenceBytes + newlineBytes;
                                if (needsTrailingNewline) entryTotalBytes += newlineBytes;
                                if (totalBytes + entryTotalBytes > maxMemoryBytes)
                                {
                                    cts.Cancel();
                                    return Result<long>.Failure(
                                        $"Output size ({totalBytes + entryTotalBytes} bytes) would exceed maximum output limit ({maxMemoryBytes} bytes).");
                                }

                                // Write now that we've confirmed we have budget
                                if (boundaries != null)
                                {
                                    await writer.FlushAsync(token);
                                    boundaries.BeginFile(SanitizePathForOutput(content.Entry.DisplayPath ?? content.Entry.RelativePath),
                                        entryTotalBytes, Math.Max(fence.Length, Math.Max(GetFenceForContent(header).Length - 1,
                                            GetFenceForContent(fenceLine).Length - 1)));
                                }
                                await WriteStringLineAsync(writer, header, token);
                                await WriteStringLineAsync(writer, fenceLine, token);
                                if (boundaries != null)
                                {
                                    await writer.FlushAsync(token);
                                    boundaries.BeginContent(crushedContent != null ? Utf8NoBom.GetByteCount(crushedContent) : contentBytesWritten,
                                        sourceLines);
                                }

                                estimatedTokensAccumulator += TokenEstimator.EstimateTokens(header);
                                estimatedTokensAccumulator += TokenEstimator.EstimateTokens(fenceLine);

                                if (crushedContent != null && crushedContent.Length > 0)
                                {
                                    await WriteStringAsync(writer, crushedContent, token);
                                    estimatedTokensAccumulator += TokenEstimator.EstimateTokens(crushedContent);
                                }
                                else if (contentBuffer != null && contentBytesWritten > 0)
                                {
                                    const int chunkSize = 65536;
                                    var remaining = (int)contentBytesWritten;
                                    var offset = 0;
                                    while (remaining > 0)
                                    {
                                        var toWrite = Math.Min(remaining, chunkSize);
                                        var destMemory = writer.GetMemory(toWrite);
                                        contentBuffer.AsSpan(offset, toWrite).CopyTo(destMemory.Span);
                                        writer.Advance(toWrite);
                                        await FlushIfNeededAsync(writer, token);
                                        offset += toWrite;
                                        remaining -= toWrite;
                                    }

                                    // Token estimate: on the plain path the worker already computed it from the
                                    // UTF-8 bytes (carried in ready.ContentTokens); on the content-filter path
                                    // estimate it here. Both are byte-identical to decoding then estimating
                                    // (proven by TokenEstimatorUtf8Tests).
                                    estimatedTokensAccumulator += ready.TrimmedLength >= 0
                                        ? ready.ContentTokens
                                        : TokenEstimator.EstimateTokensUtf8(
                                            contentBuffer.AsSpan(0, (int)contentBytesWritten));
                                }

                                if (boundaries != null)
                                {
                                    await writer.FlushAsync(token);
                                    boundaries.EndContent();
                                }
                                if (crushedContent is { Length: > 0 }) await WriteStringLineAsync(writer, "", token);
                                if (needsTrailingNewline) await WriteStringLineAsync(writer, "", token);
                                await WriteStringLineAsync(writer, fence, token);
                                await WriteStringLineAsync(writer, "", token);
                                if (boundaries != null)
                                {
                                    await writer.FlushAsync(token);
                                    boundaries.EndFile();
                                }

                                estimatedTokensAccumulator += TokenEstimator.EstimateTokens(fence);

                                fileList.Add(SanitizePathForOutput(content.Entry.DisplayPath ?? content.Entry.RelativePath));
                                totalBytes += entryTotalBytes;
                                if (!content.Entry.Path.StartsWith('[')) LastEmittedFileCount++;
                            }
                        }
                        finally
                        {
                            if (ready.Buffer != null) ArrayPool<byte>.Shared.Return(ready.Buffer);
                            readWindow.Release();
                        }
                    }
                }
            }
            finally
            {
                cts.Cancel();

                // Return any remaining buffers in outOfOrderBuffer to ArrayPool
                foreach (var item in outOfOrderBuffer.Values)
                {
                    if (item.Buffer != null)
                    {
                        ArrayPool<byte>.Shared.Return(item.Buffer);
                    }
                }

                try
                {
                    await generateTask;
                }
                catch
                {
                    // Ignore exceptions from background task during cleanup
                }

                // Drain any remaining items in the channel
                while (channel.Reader.TryRead(out var result))
                {
                    if (result.Buffer != null)
                    {
                        ArrayPool<byte>.Shared.Return(result.Buffer);
                    }
                }
            }

            var structureHeader = config.Markdown.ProjectStructureHeader ?? "_Project Structure:_ ";
            var structureFence = config.Markdown.Fence ?? "```";
            var projectStructureBytes =
                Utf8NoBom.GetByteCount(structureHeader) + NewlineByteCount;
            projectStructureBytes += Utf8NoBom.GetByteCount($"{structureFence}text") + NewlineByteCount;
            foreach (var path in fileList)
            {
                if (path.StartsWith('[')) continue;
                projectStructureBytes += Utf8NoBom.GetByteCount(path) + NewlineByteCount;
            }

            projectStructureBytes += Utf8NoBom.GetByteCount(structureFence) + NewlineByteCount;
            projectStructureBytes += NewlineByteCount;
            if (boundaries != null)
            {
                await writer.FlushAsync(ct);
                var longestFence = Math.Max(GetFenceForContent(structureHeader).Length - 1,
                    GetFenceForContent(structureFence).Length - 1);
                foreach (var path in fileList)
                    longestFence = Math.Max(longestFence, GetFenceForContent(path).Length - 1);
                boundaries.BeginFile(null, projectStructureBytes, longestFence);
            }
            await WriteProjectStructureAsync(writer, fileList, config, ct);
            await writer.FlushAsync(ct);
            boundaries?.EndFile();
            await writer.CompleteAsync();
            writer = null;
            totalBytes += projectStructureBytes;

            LastEstimatedTokens = estimatedTokensAccumulator;

            return Result<long>.Success(totalBytes);
        }
        catch (OperationCanceledException)
        {
            return Result<long>.Failure("Operation cancelled.");
        }
        catch (Exception ex)
        {
            _logger.Error("Markdown generation failed", ex);
            return Result<long>.Failure(ex.Message);
        }
        finally
        {
            // Release pooled pipe segments on cancellation or an early budget/error return too.
            if (writer != null)
            {
                try { await writer.CompleteAsync(); }
                catch { /* Preserve the generation error already reported above. */ }
            }
        }
    }

    private static async ValueTask WriteStringAsync(PipeWriter writer, string str, CancellationToken ct)
    {
        const int chunkCharCount = 4096;
        var offset = 0;
        while (offset < str.Length)
        {
            var length = Math.Min(chunkCharCount, str.Length - offset);
            // A surrogate pair must be encoded together even at an internal write boundary.
            if (offset + length < str.Length && char.IsHighSurrogate(str[offset + length - 1]) &&
                char.IsLowSurrogate(str[offset + length])) length--;
            var written = Utf8NoBom.GetBytes(str.AsSpan(offset, length),
                writer.GetSpan(Utf8NoBom.GetMaxByteCount(length)));
            writer.Advance(written);
            offset += length;
            await FlushIfNeededAsync(writer, ct);
        }
    }

    private static ValueTask FlushIfNeededAsync(PipeWriter writer, CancellationToken ct)
    {
        // StreamPipeWriter otherwise retains the whole export until the final flush.
        return writer.UnflushedBytes >= 65536 ? FlushAsync(writer, ct) : ValueTask.CompletedTask;

        static async ValueTask FlushAsync(PipeWriter pipe, CancellationToken token) => await pipe.FlushAsync(token);
    }

    private static async ValueTask WriteStringLineAsync(PipeWriter writer, string str, CancellationToken ct)
    {
        await WriteStringAsync(writer, str, ct);
        NewlineBytes.CopyTo(writer.GetSpan(NewlineBytes.Length));
        writer.Advance(NewlineBytes.Length);
        await FlushIfNeededAsync(writer, ct);
    }

    private static async ValueTask WriteMetadataLineAsync(PipeWriter writer, IMarkdownBoundarySink? boundaries,
        string text, CancellationToken ct)
    {
        if (boundaries != null)
        {
            await writer.FlushAsync(ct);
            boundaries.BeginFile(null, Utf8NoBom.GetByteCount(text) + NewlineByteCount,
                GetFenceForContent(text).Length - 1);
        }
        await WriteStringLineAsync(writer, text, ct);
        if (boundaries != null)
        {
            await writer.FlushAsync(ct);
            boundaries.EndFile();
        }
    }

    private static async ValueTask WriteProjectStructureAsync(PipeWriter writer, IEnumerable<string> filePaths,
        GcConfiguration config, CancellationToken ct)
    {
        var structureFence = config.Markdown.Fence ?? "```";
        await WriteStringLineAsync(writer, config.Markdown.ProjectStructureHeader ?? "_Project Structure:_ ", ct);
        await WriteStringLineAsync(writer, $"{structureFence}text", ct);
        foreach (var path in filePaths)
        {
            if (path.StartsWith('[')) continue;
            await WriteStringLineAsync(writer, path, ct);
        }
        await WriteStringLineAsync(writer, structureFence, ct);
        await WriteStringLineAsync(writer, "", ct);
    }

    // A filename can legally contain control characters (newline, CR, tab, …); `git ls-files -z`
    // and the filesystem walker faithfully surface them. Emitting one raw into a "## File:" header,
    // an error placeholder, or the structure listing would break the markdown and let a
    // maliciously-named file inject a fake "## File:" line into LLM-bound output. Replace C0 control
    // chars with U+FFFD for DISPLAY only — never for IO, which uses Entry.AbsolutePath.
    private static string SanitizePathForOutput(string path)
    {
        // Fast path: clean paths (the overwhelming common case) return as-is with no allocation.
        if (path.AsSpan().IndexOfAnyInRange('\u0000', '\u001F') < 0) return path;

        return string.Create(path.Length, path, static (dst, src) =>
        {
            for (var i = 0; i < src.Length; i++)
            {
                var c = src[i];
                dst[i] = c <= '\u001F' ? '\uFFFD' : c;
            }
        });
    }

    // =========================================================================
    // Fence selection: find the longest backtick run in content and use run+1.
    // Shared by both in-memory and streamed paths so we never close early.
    // =========================================================================
    private static string GetFenceForContent(string content)
    {
        var span = content.AsSpan();
        var longestRun = 3;
        var i = 0;
        while (true)
        {
            // Vectorized skip over the (overwhelmingly common) non-backtick stretches.
            var rel = span.Slice(i).IndexOf('`');
            if (rel < 0) break;
            i += rel;
            var run = 0;
            while (i < span.Length && span[i] == '`') { run++; i++; }
            if (run > longestRun) longestRun = run;
        }

        return new string('`', longestRun + 1);
    }

    // For streaming: scan the full byte span for the longest backtick run, returning the run length
    // (>= 3). The caller emits run+1 backticks. Computed in the parallel worker so the scan is not
    // serialized on the reader; the fence string itself is built cheaply on the reader.
    private static int GetFenceRunForBytes(byte[] buffer, int length)
    {
        var span = buffer.AsSpan(0, length);
        var longestRun = 3;
        var i = 0;
        while (true)
        {
            var rel = span.Slice(i).IndexOf((byte)'`');
            if (rel < 0) break;
            i += rel;
            var run = 0;
            while (i < length && buffer[i] == (byte)'`') { run++; i++; }
            if (run > longestRun) longestRun = run;
        }

        return longestRun;
    }

    // Result of a single file's parallel read + precomputed per-file work, reassembled in index order
    // by the reader. Buffer ownership transfers to the reader, which returns it to the pool.
    private readonly record struct ReadResult(
        int Index,
        byte[]? Buffer,
        int Length,
        string? Error,
        bool IsBinary,
        int FenceRun, // longest backtick run over Buffer[0..Length]; used only when emitting content
        int TrimmedLength, // plain-path trailing-trimmed content length; -1 when the worker did not precompute
        int ContentTokens) // EstimateTokensUtf8 over Buffer[0..TrimmedLength]; valid iff TrimmedLength >= 0
    {
        // Marker for caller-supplied in-memory content (read from FileContent.Content, not from disk).
        public static ReadResult Marker(int index) => new(index, null, 0, null, false, 0, -1, 0);

        public static ReadResult Failure(int index, string error) =>
            new(index, null, 0, error, false, 0, -1, 0);

        public static ReadResult TooLarge(int index) => new(index, null, -1, null, false, 0, -1, 0);
    }
}
