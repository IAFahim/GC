namespace gc.Domain.Models.Configuration;

public sealed record DiscoveryConfiguration
{
    public string? Mode { get; init; }
    public bool UseGit { get; init; } = true;
    public bool FollowSymlinks { get; init; } = false;
    public int? MaxDepth { get; init; }
    public ClusterConfiguration? Cluster { get; init; }
}
