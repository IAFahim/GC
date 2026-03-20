namespace gc.Domain.Models.Configuration;

public sealed record DiscoveryConfiguration
{
    public string Mode { get; init; } = "auto";
    public bool UseGit { get; init; } = true;
    public bool FollowSymlinks { get; init; } = false;
}
