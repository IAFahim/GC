using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using gc.Application.Services;
using gc.Domain.Interfaces;

namespace gc.Application.Adapters;



/// <summary>
///     Adapter that wraps DynamicCompressor to implement IOutputTransform.
/// </summary>
public sealed class DynamicCompressorAdapter : IOutputTransform
{
    private readonly DynamicCompressor _inner;

    public DynamicCompressorAdapter(DynamicCompressor? inner = null)
    {
        _inner = inner ?? new DynamicCompressor();
    }

    public string Name => "DynamicCompressor";

    public Task<TransformResult> TransformAsync(string input, CancellationToken ct = default)
    {
        var result = _inner.Compress(input);
        return Task.FromResult(new TransformResult(result.Output, result.Legend, result.TokensSaved));
    }
}

/// <summary>
///     Adapter that wraps SqzCompressionService to implement IOutputTransform.
///     Must run last in the pipeline since sqz is an external compression tool.
/// </summary>
public sealed class SqzCompressionAdapter : IOutputTransform
{
    private readonly SqzCompressionService _inner;
    private readonly bool _noCache;

    public SqzCompressionAdapter(SqzCompressionService inner, bool noCache = false)
    {
        _inner = inner;
        _noCache = noCache;
    }

    public string Name => "SqzCompression";

    public async Task<TransformResult> TransformAsync(string input, CancellationToken ct = default)
    {
        var compressed = await _inner.CompressAsync(input, _noCache, ct);
        return new TransformResult(compressed, string.Empty, 0);
    }
}

/// <summary>
///     Runs a sequence of transforms as a pipeline, accumulating output and legend.
///     Each transform is total and referentially transparent.
/// </summary>
public sealed class OutputPipeline
{
    private readonly IReadOnlyList<IOutputTransform> _transforms;

    public OutputPipeline(IReadOnlyList<IOutputTransform> transforms)
    {
        _transforms = transforms;
    }

    /// <summary>
    ///     Applies all transforms sequentially with async support.
    /// </summary>
    public async Task<PipelineResult> ApplyAsync(string input, CancellationToken ct = default)
    {
        var current = input;
        var legendBuilder = new StringBuilder();
        var totalTokensSaved = 0;

        foreach (var transform in _transforms)
        {
            var result = await transform.TransformAsync(current, ct);
            if (!string.IsNullOrEmpty(result.Legend))
                legendBuilder.AppendLine(result.Legend);
            totalTokensSaved += result.TokensSaved;
            current = result.Output;
        }

        return new PipelineResult(current, legendBuilder.ToString(), totalTokensSaved);
    }
}

/// <summary>
///     Result of running the full output pipeline.
/// </summary>
/// <param name="Output">The final transformed output.</param>
/// <param name="Legend">Accumulated legend/dictionary text from all transforms.</param>
/// <param name="TokensSaved">Total estimated tokens saved across all transforms.</param>
public readonly record struct PipelineResult(
    string Output,
    string Legend,
    int TokensSaved);