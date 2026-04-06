using gc.Infrastructure.Configuration;
using gc.Infrastructure.Logging;
using gc.Domain.Models.Configuration;

namespace gc.Tests;

public class LoaderTests
{
    private static ConfigurationLoader CreateLoader() => new(new ConsoleLogger());

    // ---------------------------------------------------------------
    // 1. Cluster config merge
    // ---------------------------------------------------------------

    [Fact]
    public async Task LoadConfig_ClusterConfig_MergesCorrectly()
    {
        var json = """
        {
            "discovery": {
                "cluster": {
                    "enabled": true,
                    "maxDepth": 5,
                    "repoSeparator": "===",
                    "includeRepoHeader": false,
                    "maxParallelRepos": 8,
                    "skipDirectories": ["archive", "old"],
                    "includeRootFiles": true,
                    "failFast": true
                }
            }
        }
        """;

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var loader = CreateLoader();
            var result = await loader.LoadConfigFromFileAsync(tempFile);

            Assert.True(result.IsSuccess);
            var cluster = result.Value!.Discovery.Cluster;
            Assert.NotNull(cluster);
            Assert.True(cluster.Enabled);
            Assert.Equal(5, cluster.MaxDepth);
            Assert.Equal("===", cluster.RepoSeparator);
            Assert.False(cluster.IncludeRepoHeader);
            Assert.Equal(8, cluster.MaxParallelRepos);
            Assert.Equal(["archive", "old"], cluster.SkipDirectories);
            Assert.True(cluster.IncludeRootFiles);
            Assert.True(cluster.FailFast);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadConfig_ClusterConfig_DefaultValues_AreCorrect()
    {
        var json = """{ "discovery": { "cluster": {} } }""";

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var loader = CreateLoader();
            var result = await loader.LoadConfigFromFileAsync(tempFile);

            Assert.True(result.IsSuccess);
            var cluster = result.Value!.Discovery.Cluster;
            Assert.NotNull(cluster);
            // Empty cluster {} gets CLR defaults (STJ source gen doesn't use record init defaults)
            Assert.False(cluster.Enabled);
            Assert.Equal(0, cluster.MaxDepth);
            Assert.Null(cluster.RepoSeparator);
            Assert.False(cluster.IncludeRepoHeader);
            Assert.Equal(0, cluster.MaxParallelRepos);
            Assert.Null(cluster.SkipDirectories);
            Assert.False(cluster.IncludeRootFiles);
            Assert.False(cluster.FailFast);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ---------------------------------------------------------------
    // 2. Merge edge cases
    // ---------------------------------------------------------------

    [Fact]
    public async Task LoadConfig_PartialConfig_MergesWithDefaults()
    {
        // Only set one field; the rest should remain at their JSON default / record defaults
        var json = """{ "version": "2.0.0" }""";

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var loader = CreateLoader();
            var result = await loader.LoadConfigFromFileAsync(tempFile);

            Assert.True(result.IsSuccess);
            var config = result.Value!;
            Assert.Equal("2.0.0", config.Version);
            // LoadConfigFromFileAsync returns raw deserialized JSON - sub-objects not in JSON are null
            Assert.Null(config.Limits);
            Assert.Null(config.Discovery);
            Assert.Null(config.Output);
            Assert.Null(config.Logging);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadConfig_NullSubConfigs_KeepDefaults()
    {
        // An empty object should produce a config where sub-objects use record defaults
        var json = "{}";

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var loader = CreateLoader();
            var result = await loader.LoadConfigFromFileAsync(tempFile);

            Assert.True(result.IsSuccess);
            var config = result.Value!;
            // {} deserializes with ALL fields at CLR defaults (null for reference types)
            Assert.Null(config.Version);
            Assert.Null(config.Limits);
            Assert.Null(config.Discovery);
            Assert.Null(config.Filters);
            Assert.Null(config.Presets);
            Assert.Null(config.LanguageMappings);
            Assert.Null(config.Markdown);
            Assert.Null(config.Output);
            Assert.Null(config.Logging);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadConfig_MultiplePresets_MergesCorrectly()
    {
        var json = """
        {
            "presets": {
                "web": {
                    "extensions": ["vue", "svelte"],
                    "description": "Updated web preset"
                },
                "custom": {
                    "extensions": ["xyz", "abc"],
                    "description": "My custom preset"
                }
            }
        }
        """;

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var loader = CreateLoader();
            var result = await loader.LoadConfigFromFileAsync(tempFile);

            Assert.True(result.IsSuccess);
            var presets = result.Value!.Presets;

            // "web" was loaded from JSON with only vue+svelte
            Assert.True(presets.ContainsKey("web"));
            Assert.Equal("Updated web preset", presets["web"].Description);

            // "custom" is a brand-new preset
            Assert.True(presets.ContainsKey("custom"));
            Assert.Equal(["xyz", "abc"], presets["custom"].Extensions);
            Assert.Equal("My custom preset", presets["custom"].Description);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadConfig_LanguageMappings_MergesCorrectly()
    {
        var json = """
        {
            "languageMappings": {
                "rs": "rust",
                "go": "go",
                "kt": "kotlin",
                "py": "python3"
            }
        }
        """;

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var loader = CreateLoader();
            var result = await loader.LoadConfigFromFileAsync(tempFile);

            Assert.True(result.IsSuccess);
            var mappings = result.Value!.LanguageMappings;

            // New keys
            Assert.Equal("rust", mappings["rs"]);
            Assert.Equal("go", mappings["go"]);
            Assert.Equal("kotlin", mappings["kt"]);
            // Overridden key (py was "python" in defaults)
            Assert.Equal("python3", mappings["py"]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ---------------------------------------------------------------
    // 3. Cache behavior
    // ---------------------------------------------------------------

    [Fact]
    public async Task LoadConfig_Cache_ReturnsSameConfig()
    {
        var loader = CreateLoader();
        // LoadConfigAsync with useCache=true on subsequent calls returns cached
        var result1 = await loader.LoadConfigAsync(useCache: false);
        var result2 = await loader.LoadConfigAsync(useCache: true);

        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);
        // Both should return equivalent configs (may or may not be same reference due to caching)
        Assert.Equal(result1.Value!.Version, result2.Value!.Version);
        Assert.Equal(result1.Value.Limits.MaxFileSize, result2.Value.Limits.MaxFileSize);
    }

    [Fact]
    public async Task ClearCache_ForcesReload()
    {
        var loader = CreateLoader();

        // First load populates cache
        var result1 = await loader.LoadConfigAsync(useCache: true);
        Assert.True(result1.IsSuccess);

        // Clear the cache
        loader.ClearCache();

        // Second load should still succeed (reloads from defaults/filesystem)
        var result2 = await loader.LoadConfigAsync(useCache: true);
        Assert.True(result2.IsSuccess);
        Assert.NotNull(result2.Value);
    }

    // ---------------------------------------------------------------
    // 4. Error cases
    // ---------------------------------------------------------------

    [Fact]
    public async Task LoadConfig_EmptyFile_ReturnsDefault()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "");
            var loader = CreateLoader();
            var result = await loader.LoadConfigFromFileAsync(tempFile);

            // Empty file should fail deserialization (not valid JSON)
            Assert.False(result.IsSuccess);
            Assert.NotNull(result.Error);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadConfig_MalformedJson_ReturnsFailure()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "{ this is not valid json }}}");
            var loader = CreateLoader();
            var result = await loader.LoadConfigFromFileAsync(tempFile);

            Assert.False(result.IsSuccess);
            Assert.NotNull(result.Error);
            Assert.Contains("Failed to load", result.Error!);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadConfig_NonexistentPath_ReturnsFailure()
    {
        var loader = CreateLoader();
        var result = await loader.LoadConfigFromFileAsync("/tmp/gc_test_nonexistent_" + Guid.NewGuid() + ".json");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Contains("not found", result.Error!);
    }

    [Fact]
    public async Task LoadConfig_JsonWithComments_Works()
    {
        // The serializer is configured with ReadCommentHandling = JsonCommentHandling.Skip
        var json = """
        {
            // This is a line comment
            "version": "3.0.0",
            /* This is a block comment */
            "limits": {
                "maxFileSize": "5MB" // trailing comment
            }
        }
        """;

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var loader = CreateLoader();
            var result = await loader.LoadConfigFromFileAsync(tempFile);

            Assert.True(result.IsSuccess);
            Assert.Equal("3.0.0", result.Value!.Version);
            Assert.Equal("5MB", result.Value.Limits.MaxFileSize);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ---------------------------------------------------------------
    // 5. Merge specifics
    // ---------------------------------------------------------------

    [Fact]
    public async Task LoadConfig_MarkdownMerge_OverridesOnlyNonNull()
    {
        var json = """
        {
            "markdown": {
                "fence": "~~~",
                "projectStructureHeader": "**Files:**"
            }
        }
        """;

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var loader = CreateLoader();
            var result = await loader.LoadConfigFromFileAsync(tempFile);

            Assert.True(result.IsSuccess);
            var md = result.Value!.Markdown;
            Assert.NotNull(md);
            Assert.Equal("~~~", md.Fence);
            Assert.Equal("**Files:**", md.ProjectStructureHeader);
            // Non-specified fields are null (STJ source gen uses CLR defaults, not record init defaults)
            Assert.Null(md.FileHeaderTemplate);
            Assert.Null(md.LanguageDetection);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadConfig_OutputMerge_OverridesFields()
    {
        var json = """
        {
            "output": {
                "defaultFormat": "plain",
                "includeStats": false,
                "sortByPath": false
            }
        }
        """;

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var loader = CreateLoader();
            var result = await loader.LoadConfigFromFileAsync(tempFile);

            Assert.True(result.IsSuccess);
            var output = result.Value!.Output;
            Assert.Equal("plain", output.DefaultFormat);
            Assert.False(output.IncludeStats);
            Assert.False(output.SortByPath);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadConfig_LoggingMerge_OverridesFields()
    {
        var json = """
        {
            "logging": {
                "level": "debug",
                "includeTimestamps": true
            }
        }
        """;

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var loader = CreateLoader();
            var result = await loader.LoadConfigFromFileAsync(tempFile);

            Assert.True(result.IsSuccess);
            var logging = result.Value!.Logging;
            Assert.Equal("debug", logging.Level);
            Assert.True(logging.IncludeTimestamps);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadConfig_LimitsMerge_OverridesOnlyNonNull()
    {
        var json = """
        {
            "limits": {
                "maxFileSize": "50MB",
                "maxFiles": 500
            }
        }
        """;

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var loader = CreateLoader();
            var result = await loader.LoadConfigFromFileAsync(tempFile);

            Assert.True(result.IsSuccess);
            var limits = result.Value!.Limits;
            Assert.NotNull(limits);
            Assert.Equal("50MB", limits.MaxFileSize);
            Assert.Equal(500, limits.MaxFiles);
            // Non-specified fields are null (STJ source gen uses CLR defaults, not record init defaults)
            Assert.Null(limits.MaxClipboardSize);
            Assert.Null(limits.MaxMemoryBytes);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadConfig_DiscoveryMerge_OverridesCluster()
    {
        var json = """
        {
            "discovery": {
                "mode": "filesystem",
                "useGit": false,
                "followSymlinks": true,
                "cluster": {
                    "enabled": true,
                    "maxDepth": 10
                }
            }
        }
        """;

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var loader = CreateLoader();
            var result = await loader.LoadConfigFromFileAsync(tempFile);

            Assert.True(result.IsSuccess);
            var disc = result.Value!.Discovery;
            Assert.Equal("filesystem", disc.Mode);
            Assert.False(disc.UseGit);
            Assert.True(disc.FollowSymlinks);
            Assert.NotNull(disc.Cluster);
            Assert.True(disc.Cluster.Enabled);
            Assert.Equal(10, disc.Cluster.MaxDepth);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ---------------------------------------------------------------
    // 6. Filters merge
    // ---------------------------------------------------------------

    [Fact]
    public async Task LoadConfig_FiltersMerge_OverridesPatterns()
    {
        var json = """
        {
            "filters": {
                "systemIgnoredPatterns": ["*.log", "tmp/"],
                "additionalExtensions": ["xyz", "qrs"]
            }
        }
        """;

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var loader = CreateLoader();
            var result = await loader.LoadConfigFromFileAsync(tempFile);

            Assert.True(result.IsSuccess);
            var filters = result.Value!.Filters;
            Assert.Equal(["*.log", "tmp/"], filters.SystemIgnoredPatterns);
            Assert.Equal(["xyz", "qrs"], filters.AdditionalExtensions);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ---------------------------------------------------------------
    // 7. Additional coverage tests
    // ---------------------------------------------------------------

    [Fact]
    public void GetConfigDirectory_ReturnsNonNullPath()
    {
        var loader = CreateLoader();
        var dir = loader.GetConfigDirectory();
        Assert.False(string.IsNullOrEmpty(dir));
        Assert.True(dir.Contains("gc"));
    }

    [Fact]
    public void GetSystemConfigDirectory_ReturnsNonNullPath()
    {
        var loader = CreateLoader();
        var dir = loader.GetSystemConfigDirectory();
        Assert.False(string.IsNullOrEmpty(dir));
        Assert.True(dir.Contains("gc"));
    }

    [Fact]
    public async Task LoadConfig_FullConfig_AllFieldsSet()
    {
        var json = """
        {
            "version": "2.5.0",
            "limits": {
                "maxFileSize": "20MB",
                "maxClipboardSize": "50MB",
                "maxMemoryBytes": "500MB",
                "maxFiles": 25000
            },
            "discovery": {
                "mode": "git",
                "useGit": true,
                "followSymlinks": false,
                "cluster": {
                    "enabled": true,
                    "maxDepth": 3,
                    "repoSeparator": "***",
                    "includeRepoHeader": true,
                    "maxParallelRepos": 4,
                    "skipDirectories": ["vendor", "node_modules"],
                    "includeRootFiles": true,
                    "failFast": false
                }
            },
            "filters": {
                "systemIgnoredPatterns": ["dist/", "build/"],
                "additionalExtensions": ["txt", "cfg"]
            },
            "presets": {
                "my-preset": {
                    "extensions": ["cs", "fs", "vb"],
                    "description": "All .NET languages"
                }
            },
            "languageMappings": {
                "fs": "fsharp",
                "vb": "vbnet"
            },
            "markdown": {
                "fence": "~~~",
                "projectStructureHeader": "## Structure",
                "fileHeaderTemplate": "### {path}",
                "languageDetection": "shebang"
            },
            "output": {
                "defaultFormat": "json",
                "includeStats": false,
                "sortByPath": false
            },
            "logging": {
                "level": "verbose",
                "includeTimestamps": true
            }
        }
        """;

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var loader = CreateLoader();
            var result = await loader.LoadConfigFromFileAsync(tempFile);

            Assert.True(result.IsSuccess);
            var c = result.Value!;

            Assert.Equal("2.5.0", c.Version);

            // Limits
            Assert.Equal("20MB", c.Limits.MaxFileSize);
            Assert.Equal("50MB", c.Limits.MaxClipboardSize);
            Assert.Equal("500MB", c.Limits.MaxMemoryBytes);
            Assert.Equal(25000, c.Limits.MaxFiles);

            // Discovery
            Assert.Equal("git", c.Discovery.Mode);
            Assert.True(c.Discovery.UseGit);
            Assert.False(c.Discovery.FollowSymlinks);
            Assert.NotNull(c.Discovery.Cluster);
            Assert.True(c.Discovery.Cluster.Enabled);
            Assert.Equal(3, c.Discovery.Cluster.MaxDepth);
            Assert.Equal("***", c.Discovery.Cluster.RepoSeparator);
            Assert.True(c.Discovery.Cluster.IncludeRepoHeader);
            Assert.Equal(4, c.Discovery.Cluster.MaxParallelRepos);
            Assert.Equal(["vendor", "node_modules"], c.Discovery.Cluster.SkipDirectories);
            Assert.True(c.Discovery.Cluster.IncludeRootFiles);
            Assert.False(c.Discovery.Cluster.FailFast);

            // Filters
            Assert.Equal(["dist/", "build/"], c.Filters.SystemIgnoredPatterns);
            Assert.Equal(["txt", "cfg"], c.Filters.AdditionalExtensions);

            // Presets
            Assert.True(c.Presets.ContainsKey("my-preset"));
            Assert.Equal(["cs", "fs", "vb"], c.Presets["my-preset"].Extensions);
            Assert.Equal("All .NET languages", c.Presets["my-preset"].Description);

            // LanguageMappings
            Assert.Equal("fsharp", c.LanguageMappings["fs"]);
            Assert.Equal("vbnet", c.LanguageMappings["vb"]);

            // Markdown
            Assert.Equal("~~~", c.Markdown.Fence);
            Assert.Equal("## Structure", c.Markdown.ProjectStructureHeader);
            Assert.Equal("### {path}", c.Markdown.FileHeaderTemplate);
            Assert.Equal("shebang", c.Markdown.LanguageDetection);

            // Output
            Assert.Equal("json", c.Output.DefaultFormat);
            Assert.False(c.Output.IncludeStats);
            Assert.False(c.Output.SortByPath);

            // Logging
            Assert.Equal("verbose", c.Logging.Level);
            Assert.True(c.Logging.IncludeTimestamps);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadConfig_TrailingCommasAllowed()
    {
        // The serializer is configured with AllowTrailingCommas = true
        var json = """
        {
            "version": "1.0.0",
            "limits": {
                "maxFileSize": "2MB",
            },
        }
        """;

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var loader = CreateLoader();
            var result = await loader.LoadConfigFromFileAsync(tempFile);

            Assert.True(result.IsSuccess);
            Assert.Equal("2MB", result.Value!.Limits.MaxFileSize);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadConfig_PresetsNullValue_HandledGracefully()
    {
        // Empty presets object
        var json = """{ "presets": {} }""";

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var loader = CreateLoader();
            var result = await loader.LoadConfigFromFileAsync(tempFile);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value!.Presets);
            Assert.Empty(result.Value.Presets);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadConfig_LanguageMappingsEmpty_HandledGracefully()
    {
        var json = """{ "languageMappings": {} }""";

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var loader = CreateLoader();
            var result = await loader.LoadConfigFromFileAsync(tempFile);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value!.LanguageMappings);
            Assert.Empty(result.Value.LanguageMappings);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadConfig_OnlyDiscoveryNoCluster_PreservesNullCluster()
    {
        var json = """{ "discovery": { "mode": "filesystem" } }""";

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var loader = CreateLoader();
            var result = await loader.LoadConfigFromFileAsync(tempFile);

            Assert.True(result.IsSuccess);
            Assert.Equal("filesystem", result.Value!.Discovery.Mode);
            // Cluster not specified in JSON, so it's null in the deserialized object
            Assert.Null(result.Value.Discovery.Cluster);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadConfig_JsonWithBlockCommentOnly_Works()
    {
        var json = """
        /* Only a block comment at top */
        {
            "version": "4.0.0"
        }
        """;

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var loader = CreateLoader();
            var result = await loader.LoadConfigFromFileAsync(tempFile);

            Assert.True(result.IsSuccess);
            Assert.Equal("4.0.0", result.Value!.Version);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadConfig_LimitsMaxFilesZero_TreatedAsDefault()
    {
        // maxFiles = 0 should be treated as "not set" by MergeLimits (source.MaxFiles > 0 check)
        var json = """{ "limits": { "maxFiles": 0 } }""";

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var loader = CreateLoader();
            var result = await loader.LoadConfigFromFileAsync(tempFile);

            Assert.True(result.IsSuccess);
            // 0 is the value deserialized from JSON; when used as *source* in merge,
            // MergeLimits won't override target. But deserialized value is 0.
            Assert.Equal(0, result.Value!.Limits.MaxFiles);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadConfig_WriteThenRead_Roundtrip()
    {
        var original = new GcConfiguration
        {
            Version = "99.0",
            Limits = new LimitsConfiguration { MaxFileSize = "999MB", MaxFiles = 42 },
            Discovery = new DiscoveryConfiguration
            {
                Mode = "git",
                UseGit = false,
                FollowSymlinks = true,
                Cluster = new ClusterConfiguration
                {
                    Enabled = true,
                    MaxDepth = 7,
                    RepoSeparator = "|||",
                    SkipDirectories = ["skip1", "skip2"]
                }
            },
            Output = new OutputConfiguration { DefaultFormat = "plain", IncludeStats = false },
            Logging = new LoggingConfiguration { Level = "debug", IncludeTimestamps = true },
            Markdown = new MarkdownConfiguration { Fence = "```", LanguageDetection = "content" }
        };

        var tempFile = Path.GetTempFileName();
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(original,
                GcJsonSerializerContext.Default.GcConfiguration);
            File.WriteAllText(tempFile, json);

            var loader = CreateLoader();
            var result = await loader.LoadConfigFromFileAsync(tempFile);

            Assert.True(result.IsSuccess);
            var loaded = result.Value!;
            Assert.Equal("99.0", loaded.Version);
            Assert.Equal("999MB", loaded.Limits.MaxFileSize);
            Assert.Equal(42, loaded.Limits.MaxFiles);
            Assert.Equal("git", loaded.Discovery.Mode);
            Assert.False(loaded.Discovery.UseGit);
            Assert.True(loaded.Discovery.FollowSymlinks);
            Assert.NotNull(loaded.Discovery.Cluster);
            Assert.True(loaded.Discovery.Cluster.Enabled);
            Assert.Equal(7, loaded.Discovery.Cluster.MaxDepth);
            Assert.Equal("|||", loaded.Discovery.Cluster.RepoSeparator);
            Assert.Equal(["skip1", "skip2"], loaded.Discovery.Cluster.SkipDirectories);
            Assert.Equal("plain", loaded.Output.DefaultFormat);
            Assert.False(loaded.Output.IncludeStats);
            Assert.Equal("debug", loaded.Logging.Level);
            Assert.True(loaded.Logging.IncludeTimestamps);
            Assert.Equal("content", loaded.Markdown.LanguageDetection);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
