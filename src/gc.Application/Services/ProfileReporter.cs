using System.Diagnostics;
using System.Text;
using System.Text.Json;
using gc.Domain.Models.Configuration;

namespace gc.Application.Services;

/// <summary>
///     Collects stage timings and metrics for profiling output.
///     Optimized with zero allocations in recording paths.
/// </summary>
public sealed class ProfileReporter
{
    private const int MaxStages = 32;
    private const int MaxMetrics = 64;

    // Inline arrays for zero-allocation storage
    private (string Name, long Ticks)[] _stages;
    private (string Name, string Value)[] _metrics;
    private int _stageCount;
    private int _metricCount;
    private readonly Stopwatch _totalSw = Stopwatch.StartNew();

    public ProfileReporter()
    {
        _stages = new (string, long)[MaxStages];
        _metrics = new (string, string)[MaxMetrics];
    }

    /// <summary>
    ///     Records a stage timing. Zero allocation.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void RecordStage(string name, long elapsedTicks)
    {
        if (_stageCount < MaxStages)
        {
            _stages[_stageCount++] = (name, elapsedTicks);
        }
    }

    /// <summary>
    ///     Records a metric. Zero allocation.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void RecordMetric(string name, string value)
    {
        if (_metricCount < MaxMetrics)
        {
            _metrics[_metricCount++] = (name, value);
        }
    }

    public void Stop()
    {
        _totalSw.Stop();
    }

    public string ToMarkdown()
    {
        // Pre-capacity based on known counts
        var capacity = 80 + (_stageCount * 40) + (_metricCount * 30);
        var sb = new StringBuilder(capacity);

        sb.AppendLine("## Profile");
        sb.AppendLine();
        sb.AppendLine("| Stage | Time |");
        sb.AppendLine("|---|---|");

        for (var i = 0; i < _stageCount; i++)
        {
            var (name, ticks) = _stages[i];
            var ms = (double)ticks * 1000 / Stopwatch.Frequency;
            sb.AppendLine($"| {name} | {ms:F1}ms |");
        }

        sb.AppendLine($"| **Total** | {_totalSw.ElapsedMilliseconds}ms |");
        sb.AppendLine();

        if (_metricCount > 0)
        {
            sb.AppendLine("| Metric | Value |");
            sb.AppendLine("|---|---|");
            for (var i = 0; i < _metricCount; i++)
            {
                var (name, value) = _metrics[i];
                sb.AppendLine($"| {name} | {value} |");
            }
        }

        return sb.ToString();
    }

    public string ToJson()
    {
        // Build dictionary from inline arrays for serialization
        var stages = new Dictionary<string, double>((int)(_stageCount * 1.5f), StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _stageCount; i++)
        {
            var (name, ticks) = _stages[i];
            stages[name] = (double)ticks * 1000 / Stopwatch.Frequency;
        }

        var metrics = new Dictionary<string, string>((int)(_metricCount * 1.5f), StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _metricCount; i++)
        {
            var (name, value) = _metrics[i];
            metrics[name] = value;
        }

        var data = new Dictionary<string, object>
        {
            ["totalMs"] = _totalSw.ElapsedMilliseconds,
            ["stages"] = stages,
            ["metrics"] = metrics
        };

        return JsonSerializer.Serialize(data, GcJsonSerializerContext.Default.DictionaryStringObject);
    }
}
