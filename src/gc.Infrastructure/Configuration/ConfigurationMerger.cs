using gc.Domain.Models.Configuration;

namespace gc.Infrastructure.Configuration;

/// <summary>
///     Smart configuration merger that only applies non-default values from patches.
///     Solves BUG-006: partial configs should not overwrite defaults with implicit false.
/// </summary>
public static class ConfigurationMerger
{
    /// <summary>
    ///     Merges source into target, preserving target defaults for non-present fields.
    ///     Uses pattern matching to determine if a field was explicitly set vs defaulted.
    /// </summary>
    public static GcConfiguration Merge(GcConfiguration target, GcConfiguration source)
    {
        // A null source section means the higher-priority config omitted it entirely
        // (System.Text.Json source-gen leaves omitted reference members null). Treat that as
        // "not provided -> keep target", which also avoids dereferencing a null section (NRE on startup).
        return target with
        {
            Version = source.Version ?? target.Version,
            Limits = source.Limits is null ? target.Limits : MergeLimits(target.Limits, source.Limits),
            Discovery = source.Discovery is null ? target.Discovery : MergeDiscovery(target.Discovery, source.Discovery),
            Filters = source.Filters is null ? target.Filters : MergeFilters(target.Filters, source.Filters),
            Presets = source.Presets is null ? target.Presets : MergePresets(target.Presets, source.Presets),
            LanguageMappings = source.LanguageMappings is null
                ? target.LanguageMappings
                : MergeLanguageMappings(target.LanguageMappings, source.LanguageMappings),
            Markdown = source.Markdown is null ? target.Markdown : MergeMarkdown(target.Markdown, source.Markdown),
            Output = source.Output is null ? target.Output : MergeOutput(target.Output, source.Output),
            Logging = source.Logging is null ? target.Logging : MergeLogging(target.Logging, source.Logging)
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
            UseGit = source.UseGit ?? target.UseGit,
            FollowSymlinks = source.FollowSymlinks ?? target.FollowSymlinks,
            MaxDepth = source.MaxDepth ?? target.MaxDepth,
            Cluster = MergeCluster(target.Cluster, source.Cluster)
        };
    }

    public static ClusterConfiguration? MergeCluster(ClusterConfiguration? target, ClusterConfiguration? source)
    {
        if (source == null) return target;
        if (target == null) return source;

        // Every omittable field is nullable, so `?? target` keeps the lower-priority layer for any
        // field the higher-priority layer omitted, while still allowing an explicit value (including
        // an explicit 0) to override it. No "all-defaults" short-circuit is needed.
        return target with
        {
            Enabled = source.Enabled ?? target.Enabled,
            MaxDepth = source.MaxDepth ?? target.MaxDepth,
            RepoSeparator = source.RepoSeparator ?? target.RepoSeparator,
            IncludeRepoHeader = source.IncludeRepoHeader ?? target.IncludeRepoHeader,
            MaxParallelRepos = source.MaxParallelRepos ?? target.MaxParallelRepos,
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
            IncludeStats = source.IncludeStats ?? target.IncludeStats,
            SortByPath = source.SortByPath ?? target.SortByPath,
            NoClipboard = source.NoClipboard ?? target.NoClipboard
        };
    }

    public static LoggingConfiguration MergeLogging(LoggingConfiguration target, LoggingConfiguration source)
    {
        return target with
        {
            Level = source.Level ?? target.Level,
            IncludeTimestamps = source.IncludeTimestamps ?? target.IncludeTimestamps
        };
    }
}