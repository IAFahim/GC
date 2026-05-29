using System.Text;
using gc.Application.Services;
using gc.Domain.Interfaces;

namespace gc.Application.Adapters;

/// <summary>
///     Adapter that wraps BrainCrusher to implement IOutputTransform.
/// </summary>
public sealed class BrainCrusherAdapter : IOutputTransform
{
    private readonly BrainCrusher _inner;

    public BrainCrusherAdapter(BrainCrusher? inner = null)
    {
        _inner = inner ?? new BrainCrusher();
    }

    public string Name => "BrainCrusher";

    public TransformResult Transform(string input)
    {
        var crushed = _inner.Crush(input);
        return new TransformResult(crushed, string.Empty, 0);
    }
}

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

    public TransformResult Transform(string input)
    {
        var result = _inner.Compress(input);
        return new TransformResult(result.Output, result.Legend, result.TokensSaved);
    }
}

/// <summary>
///     Adapter that wraps SqzCompressionService to implement ICompressTransform.
///     Must run last in the pipeline since sqz is an external compression tool.
/// </summary>
public sealed class SqzCompressionAdapter : ICompressTransform
{
    private readonly SqzCompressionService _inner;
    private readonly bool _noCache;

    public SqzCompressionAdapter(SqzCompressionService inner, bool noCache = false)
    {
        _inner = inner;
        _noCache = noCache;
    }

    public string Name => "SqzCompression";

    public TransformResult Transform(string input)
    {
        // Sqz is async, so we use GetAwaiter().GetResult() in the sync interface.
        // This is safe because the pipeline only runs it from WriteOutputAsync
        // which is already async.
        var compressed = _inner.CompressAsync(input, _noCache).GetAwaiter().GetResult();
        return new TransformResult(compressed, string.Empty, 0);
    }

    public async Task<TransformResult> TransformAsync(string input, CancellationToken ct = default)
    {
        var compressed = await _inner.CompressAsync(input, _noCache);
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
    ///     Applies all transforms sequentially, accumulating legend text.
    /// </summary>
    public PipelineResult Apply(string input)
    {
        var current = input;
        var legendBuilder = new StringBuilder();
        var totalTokensSaved = 0;

        foreach (var transform in _transforms)
        {
            var result = transform.Transform(current);
            if (!string.IsNullOrEmpty(result.Legend))
                legendBuilder.AppendLine(result.Legend);
            totalTokensSaved += result.TokensSaved;
            current = result.Output;
        }

        return new PipelineResult(current, legendBuilder.ToString(), totalTokensSaved);
    }

    /// <summary>
    ///     Applies all transforms sequentially with async support for the final sqz transform.
    /// </summary>
    public async Task<PipelineResult> ApplyAsync(string input, CancellationToken ct = default)
    {
        var current = input;
        var legendBuilder = new StringBuilder();
        var totalTokensSaved = 0;

        foreach (var transform in _transforms)
            if (transform is SqzCompressionAdapter sqzAdapter)
            {
                var result = await sqzAdapter.TransformAsync(current, ct);
                if (!string.IsNullOrEmpty(result.Legend))
                    legendBuilder.AppendLine(result.Legend);
                totalTokensSaved += result.TokensSaved;
                current = result.Output;
            }
            else
            {
                var result = transform.Transform(current);
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