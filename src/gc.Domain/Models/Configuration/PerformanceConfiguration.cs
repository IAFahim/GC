namespace gc.Domain.Models.Configuration;

public sealed record PerformanceConfiguration
{
    public bool? PrewarmEnabled { get; init; } = false;
    public int PrewarmMaxFiles { get; init; } = 50;
    public string? PrewarmMaxBytesPerFile { get; init; } = "10MB";
}