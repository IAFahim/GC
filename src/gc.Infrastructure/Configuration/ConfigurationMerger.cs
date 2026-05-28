using gc.Domain.Models.Configuration;

namespace gc.Infrastructure.Configuration;

/// <summary>
/// Smart configuration merger that only applies non-default values from patches.
/// Solves BUG-006: partial configs should not overwrite defaults with implicit false.
/// </summary>
public static class ConfigurationMerger
{
    /// <summary>
    /// Merges source into target, preserving target defaults for non-present fields.
    /// Uses pattern matching to determine if a field was explicitly set vs defaulted.
    /// </summary>
    public static GcConfiguration Merge(GcConfiguration target, GcConfiguration source)
    {
        return target with
        {
            Version = source.Version ?? target.Version,
            Limits = MergeLimits(target.Limits, source.Limits),
            Discovery = MergeDiscovery(target.Discovery, source.Discovery),
            Filters = MergeFilters(target.Filters, source.Filters),
            Presets = MergePresets(target.Presets, source.Presets),
            LanguageMappings = MergeLanguageMappings(target.LanguageMappings, source.LanguageMappings),
            Markdown = MergeMarkdown(target.Markdown, source.Markdown),
            Output = MergeOutput(target.Output, source.Output),
            Logging = MergeLogging(target.Logging, source.Logging)
        };
    }

    public static LimitsConfiguration MergeLimits(LimitsConfiguration target, LimitsConfiguration source)
    {
        return target with
        {
            // Only use source values that differ from defaults (avoid implicit false overwriting true)
            MaxFileSize = source.MaxFileSize ?? target.MaxFileSize,
            MaxClipboardSize = source.MaxClipboardSize ?? target.MaxClipboardSize,
            MaxMemoryBytes = source.MaxMemoryBytes ?? target.MaxMemoryBytes,
            // Use > 0 check: only apply if explicitly set to a positive value
            MaxFiles = source.MaxFiles > 0 ? source.MaxFiles : target.MaxFiles
        };
    }

    public static DiscoveryConfiguration MergeDiscovery(DiscoveryConfiguration target, DiscoveryConfiguration source)
    {
        return target with
        {
            Mode = source.Mode ?? target.Mode,
            UseGit = source.UseGit,
            FollowSymlinks = source.FollowSymlinks,
            MaxDepth = source.MaxDepth ?? target.MaxDepth,
            Cluster = MergeCluster(target.Cluster, source.Cluster)
        };
    }

    public static ClusterConfiguration? MergeCluster(ClusterConfiguration? target, ClusterConfiguration? source)
    {
        // If source is null or all fields are default, keep target
        if (source == null) return target;
        if (AllDefaults(source)) return target;

        return MergeClusterNonDefault(target ?? new ClusterConfiguration(), source);
    }

    private static ClusterConfiguration MergeClusterNonDefault(ClusterConfiguration target, ClusterConfiguration source)
    {
        // Apply only non-null values from source, keep target defaults otherwise.
        return target with
        {
            Enabled = source.Enabled ?? target.Enabled,
            MaxDepth = source.MaxDepth > 0 ? source.MaxDepth : target.MaxDepth,
            RepoSeparator = source.RepoSeparator ?? target.RepoSeparator,
            IncludeRepoHeader = source.IncludeRepoHeader ?? target.IncludeRepoHeader,
            MaxParallelRepos = source.MaxParallelRepos > 0 ? source.MaxParallelRepos : target.MaxParallelRepos,
            SkipDirectories = source.SkipDirectories ?? target.SkipDirectories,
            IncludeRootFiles = source.IncludeRootFiles ?? target.IncludeRootFiles,
            FailFast = source.FailFast ?? target.FailFast
        };
    }

    public static FiltersConfiguration MergeFilters(FiltersConfiguration target, FiltersConfiguration source)
    {
        // Only replace arrays if source explicitly provides them (non-null and non-empty).
        // A null array means "not provided", an empty array means "explicitly empty".
        return target with
        {
            SystemIgnoredPatterns = source.SystemIgnoredPatterns ?? target.SystemIgnoredPatterns,
            AdditionalExtensions = source.AdditionalExtensions ?? target.AdditionalExtensions,
            ExcludePathPatterns = source.ExcludePathPatterns ?? target.ExcludePathPatterns,
            IncludePathPatterns = source.IncludePathPatterns ?? target.IncludePathPatterns,
            ExcludeContentPatterns = source.ExcludeContentPatterns ?? target.ExcludeContentPatterns,
            IncludeContentPatterns = source.IncludeContentPatterns ?? target.IncludeContentPatterns
        };
    }

    public static IReadOnlyDictionary<string, PresetConfiguration> MergePresets(
        IReadOnlyDictionary<string, PresetConfiguration> target,
        IReadOnlyDictionary<string, PresetConfiguration> source)
    {
        if (source.Count == 0) return target;

        var result = new Dictionary<string, PresetConfiguration>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in target) result[kvp.Key] = kvp.Value;

        foreach (var kvp in source)
        {
            if (result.TryGetValue(kvp.Key, out var existing))
            {
                var mergedExtensions = new HashSet<string>(existing.Extensions, StringComparer.OrdinalIgnoreCase);
                if (kvp.Value.Extensions != null)
                    foreach (var ext in kvp.Value.Extensions)
                        mergedExtensions.Add(ext);

                result[kvp.Key] = existing with
                {
                    Extensions = mergedExtensions.ToArray(),
                    Description = string.IsNullOrWhiteSpace(kvp.Value.Description)
                        ? existing.Description
                        : kvp.Value.Description
                };
            }
            else
            {
                result[kvp.Key] = kvp.Value;
            }
        }
        return result;
    }

    public static IReadOnlyDictionary<string, string> MergeLanguageMappings(
        IReadOnlyDictionary<string, string> target,
        IReadOnlyDictionary<string, string> source)
    {
        if (source.Count == 0) return target;
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in target) result[kvp.Key] = kvp.Value;
        foreach (var kvp in source) result[kvp.Key] = kvp.Value;
        return result;
    }

    public static MarkdownConfiguration MergeMarkdown(MarkdownConfiguration target, MarkdownConfiguration source)
    {
        return target with
        {
            Fence = source.Fence ?? target.Fence,
            ProjectStructureHeader = source.ProjectStructureHeader ?? target.ProjectStructureHeader,
            FileHeaderTemplate = source.FileHeaderTemplate ?? target.FileHeaderTemplate,
            LanguageDetection = source.LanguageDetection ?? target.LanguageDetection
        };
    }

    public static OutputConfiguration MergeOutput(OutputConfiguration target, OutputConfiguration source)
    {
        return target with
        {
            DefaultFormat = source.DefaultFormat ?? target.DefaultFormat,
            IncludeStats = source.IncludeStats,
            SortByPath = source.SortByPath
        };
    }

    public static LoggingConfiguration MergeLogging(LoggingConfiguration target, LoggingConfiguration source)
    {
        return target with
        {
            Level = source.Level ?? target.Level,
            IncludeTimestamps = source.IncludeTimestamps
        };
    }

    private static bool AllDefaults(ClusterConfiguration c)
    {
        return c.Enabled == null && c.RepoSeparator == null &&
               c.IncludeRepoHeader == null && c.SkipDirectories == null &&
               c.IncludeRootFiles == null && c.FailFast == null;
    }
}