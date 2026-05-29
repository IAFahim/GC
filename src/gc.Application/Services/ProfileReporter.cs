using System.Diagnostics;
using System.Text;
using System.Text.Json;
using gc.Domain.Models.Configuration;

namespace gc.Application.Services;

/// <summary>
///     Collects stage timings and metrics for profiling output.
/// </summary>
public sealed class ProfileReporter
{
    private readonly Dictionary<string, string> _metrics = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _stageTicks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stopwatch _totalSw = Stopwatch.StartNew();

    public void RecordStage(string name, long elapsedTicks)
    {
        _stageTicks[name] = elapsedTicks;
    }

    public void RecordMetric(string name, string value)
    {
        _metrics[name] = value;
    }

    public void Stop()
    {
        _totalSw.Stop();
    }

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Profile");
        sb.AppendLine();
        sb.AppendLine("| Stage | Time |");
        sb.AppendLine("|---|---|");

        foreach (var kvp in _stageTicks)
        {
            var ms = TimeSpan.FromTicks(kvp.Value).TotalMilliseconds;
            sb.AppendLine($"| {kvp.Key} | {ms:F1}ms |");
        }

        sb.AppendLine($"| **Total** | {_totalSw.ElapsedMilliseconds}ms |");
        sb.AppendLine();

        if (_metrics.Count > 0)
        {
            sb.AppendLine("| Metric | Value |");
            sb.AppendLine("|---|---|");
            foreach (var kvp in _metrics) sb.AppendLine($"| {kvp.Key} | {kvp.Value} |");
        }

        return sb.ToString();
    }

    public string ToJson()
    {
        var data = new Dictionary<string, object>
        {
            ["totalMs"] = _totalSw.ElapsedMilliseconds,
            ["stages"] = _stageTicks.ToDictionary(
                kvp => kvp.Key,
                kvp => TimeSpan.FromTicks(kvp.Value).TotalMilliseconds),
            ["metrics"] = _metrics
        };
        return JsonSerializer.Serialize(data, GcJsonSerializerContext.Default.DictionaryStringObject);
    }
}