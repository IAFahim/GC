using gc.CLI.Services;
using gc.Infrastructure.Configuration;

namespace gc.Tests;

/// <summary>gc --migrate-settings moves the pre-two-file sources into gc.json and .gc.json.</summary>
public sealed class SettingsMigrationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("gc-migrate").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    private string ConfigDir => Path.Combine(_root, "config");
    private string Project => Path.Combine(_root, "project");

    private async Task<int> MigrateAsync()
    {
        Directory.CreateDirectory(Project);
        Directory.CreateDirectory(Path.Combine(Project, ".git")); // stops the upward search at the project
        var (_, shouldExit, exitCode) =
            await CliArgumentResolver.ResolveAsync(["--migrate-settings"], ConfigDir, Project);
        Assert.True(shouldExit);
        return exitCode;
    }

    [Fact]
    public async Task OldUserFiles_MoveIntoGcJson()
    {
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(Path.Combine(ConfigDir, "config.json"), "{ \"limits\": { \"maxFileSize\": \"2MB\" } }");
        File.WriteAllText(Path.Combine(ConfigDir, "profiles.json"), "{ \"docs\": [\"-e\", \"md\"] }");

        Assert.Equal(0, await MigrateAsync());

        var user = await SettingsStore.LoadAsync(SettingsStore.UserPath(ConfigDir));
        Assert.Equal("2MB", user.Settings!.Limits.MaxFileSize);
        Assert.Equal(new[] { "-e", "md" }, user.Profiles["docs"]);
        Assert.False(File.Exists(Path.Combine(ConfigDir, "config.json")));
        Assert.True(File.Exists(Path.Combine(ConfigDir, "config.json.migrated")));
        Assert.True(File.Exists(Path.Combine(ConfigDir, "profiles.json.migrated")));
    }

    [Fact]
    public async Task OldProjectFiles_MoveIntoDotGcJson()
    {
        Directory.CreateDirectory(Project);
        File.WriteAllText(Path.Combine(Project, ".gc"), "-e cs -x bin");

        Assert.Equal(0, await MigrateAsync());

        var project = await SettingsStore.LoadAsync(Path.Combine(Project, SettingsStore.ProjectFileName));
        Assert.Equal(new[] { "-e", "cs", "-x", "bin" }, project.Defaults);
        Assert.True(File.Exists(Path.Combine(Project, ".gc.migrated")));
        Assert.False(File.Exists(Path.Combine(Project, ".gc")));
    }

    [Fact]
    public async Task OldProjectConfigFolder_MovesIntoSettings()
    {
        Directory.CreateDirectory(Path.Combine(Project, ".gc"));
        File.WriteAllText(Path.Combine(Project, ".gc", "config.json"), "{ \"markdown\": { \"fence\": \"~~~\" } }");

        Assert.Equal(0, await MigrateAsync());

        var project = await SettingsStore.LoadAsync(Path.Combine(Project, SettingsStore.ProjectFileName));
        Assert.Equal("~~~", project.Settings!.Markdown.Fence);
        Assert.True(File.Exists(Path.Combine(Project, ".gc", "config.json.migrated")));
    }

    [Fact]
    public async Task OldDirectoryDefaults_ForThisFolder_MoveIntoDotGcJson()
    {
        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(Project);
        var key = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Project)).Replace("\\", "\\\\");
        File.WriteAllText(Path.Combine(ConfigDir, "directory_defaults.json"), "{ \"" + key + "\": [\"-b\"] }");

        Assert.Equal(0, await MigrateAsync());

        var project = await SettingsStore.LoadAsync(Path.Combine(Project, SettingsStore.ProjectFileName));
        Assert.Equal(new[] { "-b" }, project.Defaults);
        Assert.True(File.Exists(Path.Combine(ConfigDir, "directory_defaults.json.migrated")));
    }

    [Fact]
    public async Task NothingToMigrate_IsAnSuccess_AndWritesNoFiles()
    {
        Assert.Equal(0, await MigrateAsync());

        Assert.False(File.Exists(Path.Combine(Project, SettingsStore.ProjectFileName)));
        Assert.False(File.Exists(SettingsStore.UserPath(ConfigDir)));
    }

    [Fact]
    public void OldFiles_AreReported_ButNeverRead()
    {
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(Path.Combine(ConfigDir, "profiles.json"), "{}");
        Directory.CreateDirectory(Project);
        File.WriteAllText(Path.Combine(Project, ".gc"), "-v");

        var found = SettingsStore.FindLegacyFiles(ConfigDir, Project);

        Assert.Contains(found, f => f.EndsWith("profiles.json"));
        Assert.Contains(found, f => f.EndsWith(".gc"));
    }
}
