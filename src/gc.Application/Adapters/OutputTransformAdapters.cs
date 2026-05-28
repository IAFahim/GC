using gc.Domain.Interfaces;
using gc.Application.Services;

namespace gc.Application.Adapters;

/// <summary>
/// Adapter that wraps BrainCrusher to implement IOutputTransform.
/// </summary>
public sealed class BrainCrusherAdapter : IOutputTransform
{
    private readonly BrainCrusher _inner;

    public string Name => "BrainCrusher";

    public BrainCrusherAdapter(BrainCrusher? inner = null)
    {
        _inner = inner ?? new BrainCrusher();
    }

    public TransformResult Transform(string input)
    {
        var crushed = _inner.Crush(input);
        return new TransformResult(crushed, string.Empty, 0);
    }
}

/// <summary>
/// Adapter that wraps DynamicCompressor to implement IOutputTransform.
/// </summary>
public sealed class DynamicCompressorAdapter : IOutputTransform
{
    private readonly DynamicCompressor _inner;

    public string Name => "DynamicCompressor";

    public DynamicCompressorAdapter(DynamicCompressor? inner = null)
    {
        _inner = inner ?? new DynamicCompressor();
    }

    public TransformResult Transform(string input)
    {
        var result = _inner.Compress(input);
        return new TransformResult(result.Output, result.Legend, result.TokensSaved);
    }
}
