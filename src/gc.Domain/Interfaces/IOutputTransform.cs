using gc.Domain.Common;

namespace gc.Domain.Interfaces;

/// <summary>
/// Defines a transform that can be applied to content as part of an output pipeline.
/// Each transform takes a string and returns a transformed result.
/// </summary>
public interface IOutputTransform
{
    /// <summary>
    /// A descriptive name for logging purposes.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Applies this transform to the input content.
    /// </summary>
    /// <param name="input">The input string to transform.</param>
    /// <returns>The transform result with output, legend, and metadata.</returns>
    TransformResult Transform(string input);
}

/// <summary>
/// Result of applying an output transform.
/// </summary>
/// <param name="Output">The transformed output string.</param>
/// <param name="Legend">A legend/dictionary header to prepend (e.g., for symbol substitution).</param>
/// <param name="TokensSaved">Estimated tokens saved by this transform.</param>
public readonly record struct TransformResult(
    string Output,
    string Legend,
    int TokensSaved);

/// <summary>
/// Marker interface for transforms that produce their legend inline.
/// </summary>
public interface ILegendlessTransform : IOutputTransform { }

/// <summary>
/// Marker interface for transforms that must run last in a pipeline.
/// </summary>
public interface ICompressTransform : IOutputTransform { }
