using gc.Application.Services;
using gc.Domain.Constants;
using gc.Domain.Models.Configuration;
using gc.Infrastructure.Logging;

namespace gc.Tests;

public class FileFilterExtendedTests
{
    private readonly FileFilter _filter = new(new ConsoleLogger());

    // ──────────────────────────── Extension filtering ────────────────────────────

    [Fact]
    public void Filter_DotnetExtensions_Passes()
    {
        var rawFiles = new[] { "Program.cs", "App.csproj", "Index.razor", "config.json", "app.exe", "lib.dll", "photo.png" };
        var config = new GcConfiguration();
        var result = _filter.FilterFiles(rawFiles, config, Array.Empty<string>(), Array.Empty<string>(), BuiltInPresets.PresetDotnet);

        Assert.True(result.IsSuccess);
        var entries = result.Value!.ToList();
        Assert.Contains(entries, e => e.Path == "Program.cs");
        Assert.Contains(entries, e => e.Path == "App.csproj");
        Assert.Contains(entries, e => e.Path == "Index.razor");
        Assert.Contains(entries, e => e.Path == "config.json");
        Assert.DoesNotContain(entries, e => e.Path == "app.exe");
        Assert.DoesNotContain(entries, e => e.Path == "lib.dll");
        Assert.DoesNotContain(entries, e => e.Path == "photo.png");
    }

    [Fact]
    public void Filter_DotnetExtensions_Blocks()
    {
        var rawFiles = new[] { "game.exe", "lib.dll", "icon.png", "video.mp4" };
        var config = new GcConfiguration();
        var result = _filter.FilterFiles(rawFiles, config, Array.Empty<string>(), Array.Empty<string>(), BuiltInPresets.PresetDotnet);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public void Filter_WebExtensions_Passes()
    {
        var rawFiles = new[] { "index.html", "styles.css", "app.js", "app.ts", "Program.cs", "readme.md" };
        var config = new GcConfiguration();
        var result = _filter.FilterFiles(rawFiles, config, Array.Empty<string>(), Array.Empty<string>(), BuiltInPresets.PresetWeb);

        Assert.True(result.IsSuccess);
        var entries = result.Value!.ToList();
        Assert.Contains(entries, e => e.Path == "index.html");
        Assert.Contains(entries, e => e.Path == "styles.css");
        Assert.Contains(entries, e => e.Path == "app.js");
        Assert.Contains(entries, e => e.Path == "app.ts");
        Assert.DoesNotContain(entries, e => e.Path == "Program.cs");
        Assert.DoesNotContain(entries, e => e.Path == "readme.md");
    }

    [Fact]
    public void Filter_MultipleExtensions_Passes()
    {
        // cs + py + rs — simulate a polyglot project
        var extensions = new[] { "cs", "py", "rs" };
        var rawFiles = new[] { "Main.cs", "script.py", "main.rs", "app.js", "style.css" };
        var config = new GcConfiguration();
        var result = _filter.FilterFiles(rawFiles, config, Array.Empty<string>(), Array.Empty<string>(), extensions);

        Assert.True(result.IsSuccess);
        var entries = result.Value!.ToList();
        Assert.Equal(3, entries.Count);
        Assert.Contains(entries, e => e.Path == "Main.cs");
        Assert.Contains(entries, e => e.Path == "script.py");
        Assert.Contains(entries, e => e.Path == "main.rs");
    }

    // ──────────────────────────── Preset filtering ────────────────────────────

    [Fact]
    public void Filter_WithDotnetPreset_OnlyReturnsMatchingFiles()
    {
        var dotnetExtensions = BuiltInPresets.GetAllPresets()["dotnet"].Extensions;
        var rawFiles = new[] { "Program.cs", "App.csproj", "Index.razor", "appsettings.json", "UnitTest.cs", "style.css" };
        var config = new GcConfiguration();
        var result = _filter.FilterFiles(rawFiles, config, Array.Empty<string>(), Array.Empty<string>(), dotnetExtensions);

        Assert.True(result.IsSuccess);
        var entries = result.Value!.ToList();
        Assert.All(entries, e => Assert.Contains(e.Extension, dotnetExtensions));
        Assert.DoesNotContain(entries, e => e.Path == "style.css");
    }

    [Fact]
    public void Filter_WithWebPreset_OnlyReturnsMatchingFiles()
    {
        var webExtensions = BuiltInPresets.GetAllPresets()["web"].Extensions;
        var rawFiles = new[] { "page.html", "app.js", "module.ts", "style.css", "Program.cs", "data.json" };
        var config = new GcConfiguration();
        var result = _filter.FilterFiles(rawFiles, config, Array.Empty<string>(), Array.Empty<string>(), webExtensions);

        Assert.True(result.IsSuccess);
        var entries = result.Value!.ToList();
        Assert.All(entries, e => Assert.Contains(e.Extension, webExtensions));
        Assert.DoesNotContain(entries, e => e.Path == "Program.cs");
    }

    [Fact]
    public void Filter_WithNoPreset_ReturnsAllFiles()
    {
        // Empty extensions means no extension filter — everything passes (unless excluded)
        var rawFiles = new[] { "Program.cs", "app.js", "style.css", "README.md", "Dockerfile" };
        var config = new GcConfiguration();
        var result = _filter.FilterFiles(rawFiles, config, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

        Assert.True(result.IsSuccess);
        var entries = result.Value!.ToList();
        Assert.Equal(5, entries.Count);
    }

    // ──────────────────────────── Ignored patterns ────────────────────────────

    [Fact]
    public void Filter_IgnoresNodeModules()
    {
        var rawFiles = new[]
        {
            "src/main.cs",
            "src/node_modules/react/index.js",
            "node_modules/lodash/lodash.js",
            "app/node_modules/vue/index.js"
        };
        var config = new GcConfiguration
        {
            Filters = new FiltersConfiguration
            {
                SystemIgnoredPatterns = new[] { "node_modules" }
            }
        };
        var result = _filter.FilterFiles(rawFiles, config, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

        Assert.True(result.IsSuccess);
        var entries = result.Value!.ToList();
        Assert.Single(entries);
        Assert.Contains(entries, e => e.Path == "src/main.cs");
    }

    [Fact]
    public void Filter_IgnoresBinObj()
    {
        var rawFiles = new[]
        {
            "src/Program.cs",
            "bin/Debug/app.dll",
            "obj/Release/app.dll",
            "src/bin/app.pdb",
            "src/obj/temp.cs"
        };
        var config = new GcConfiguration
        {
            Filters = new FiltersConfiguration
            {
                SystemIgnoredPatterns = new[] { "bin/", "obj/" }
            }
        };
        var result = _filter.FilterFiles(rawFiles, config, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

        Assert.True(result.IsSuccess);
        var entries = result.Value!.ToList();
        Assert.Single(entries);
        Assert.Contains(entries, e => e.Path == "src/Program.cs");
    }

    [Fact]
    public void Filter_IgnoresDotGit()
    {
        var rawFiles = new[]
        {
            "src/main.cs",
            ".git/config",
            ".git/HEAD",
            "repo/.git/objects/pack"
        };
        var config = new GcConfiguration
        {
            Filters = new FiltersConfiguration
            {
                SystemIgnoredPatterns = new[] { ".git/" }
            }
        };
        var result = _filter.FilterFiles(rawFiles, config, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

        Assert.True(result.IsSuccess);
        var entries = result.Value!.ToList();
        Assert.Single(entries);
        Assert.Contains(entries, e => e.Path == "src/main.cs");
    }

    [Fact]
    public void Filter_IgnoresCustomPatterns()
    {
        var rawFiles = new[]
        {
            "src/main.cs",
            "src/generated/code.cs",
            "temp/debug.cs",
            "dist/bundle.js"
        };
        var config = new GcConfiguration();
        var result = _filter.FilterFiles(
            rawFiles, config,
            Array.Empty<string>(),
            new[] { "generated", "temp/", "dist/" },
            Array.Empty<string>());

        Assert.True(result.IsSuccess);
        var entries = result.Value!.ToList();
        Assert.Single(entries);
        Assert.Contains(entries, e => e.Path == "src/main.cs");
    }

    // ──────────────────────────── Edge cases ────────────────────────────

    [Fact]
    public void Filter_EmptyList_ReturnsEmptyList()
    {
        var config = new GcConfiguration();
        var result = _filter.FilterFiles(Array.Empty<string>(), config, Array.Empty<string>(), Array.Empty<string>(), new[] { "cs" });

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public void Filter_AllFilesIgnored_ReturnsEmptyList()
    {
        var rawFiles = new[] { "node_modules/a.js", ".git/config", "bin/app.dll" };
        var config = new GcConfiguration
        {
            Filters = new FiltersConfiguration
            {
                SystemIgnoredPatterns = new[] { "node_modules", ".git", "bin/" }
            }
        };
        var result = _filter.FilterFiles(rawFiles, config, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public void Filter_FilesWithNoExtension_Handled()
    {
        // Files like Dockerfile, Makefile have no extension but can match by exact filename
        var rawFiles = new[] { "Dockerfile", "Makefile", "README", "src/main.cs" };
        var config = new GcConfiguration();
        // Include "Dockerfile" and "Makefile" as extension-like entries
        var result = _filter.FilterFiles(rawFiles, config, Array.Empty<string>(), Array.Empty<string>(), new[] { "Dockerfile", "Makefile", "cs" });

        Assert.True(result.IsSuccess);
        var entries = result.Value!.ToList();
        Assert.Equal(3, entries.Count);
        Assert.Contains(entries, e => e.Path == "Dockerfile");
        Assert.Contains(entries, e => e.Path == "Makefile");
        Assert.Contains(entries, e => e.Path == "src/main.cs");
        Assert.DoesNotContain(entries, e => e.Path == "README");
    }

    [Fact]
    public void Filter_CaseInsensitiveExtensions()
    {
        var rawFiles = new[] { "Program.CS", "app.Js", "style.CSS", "readme.MD" };
        var config = new GcConfiguration();
        var result = _filter.FilterFiles(rawFiles, config, Array.Empty<string>(), Array.Empty<string>(), new[] { "cs", "js", "css" });

        Assert.True(result.IsSuccess);
        var entries = result.Value!.ToList();
        Assert.Equal(3, entries.Count);
        Assert.Contains(entries, e => e.Path == "Program.CS");
        Assert.Contains(entries, e => e.Path == "app.Js");
        Assert.Contains(entries, e => e.Path == "style.CSS");
        Assert.DoesNotContain(entries, e => e.Path == "readme.MD");
    }

    [Fact]
    public void Filter_LanguageMapping_ResolvedCorrectly()
    {
        // BuiltInPresets maps "js" -> "javascript", "ts" -> "typescript", "cs" -> "cs"
        var rawFiles = new[] { "app.js", "module.ts", "Program.cs" };
        var config = new GcConfiguration();
        var result = _filter.FilterFiles(rawFiles, config, Array.Empty<string>(), Array.Empty<string>(), new[] { "js", "ts", "cs" });

        Assert.True(result.IsSuccess);
        var entries = result.Value!.ToList();
        Assert.Equal(3, entries.Count);

        var jsEntry = entries.First(e => e.Path == "app.js");
        Assert.Equal("javascript", jsEntry.Language);

        var tsEntry = entries.First(e => e.Path == "module.ts");
        Assert.Equal("typescript", tsEntry.Language);

        var csEntry = entries.First(e => e.Path == "Program.cs");
        Assert.Equal("cs", csEntry.Language);
    }

    [Fact]
    public void Filter_MaxFilesLimit_Respected()
    {
        var rawFiles = new[] { "a.cs", "b.cs", "c.cs", "d.cs", "e.cs" };
        var config = new GcConfiguration
        {
            Limits = new LimitsConfiguration { MaxFiles = 3 }
        };
        var result = _filter.FilterFiles(rawFiles, config, Array.Empty<string>(), Array.Empty<string>(), new[] { "cs" });

        Assert.True(result.IsSuccess);
        var entries = result.Value!.ToList();
        Assert.Equal(3, entries.Count);
    }

    [Fact]
    public void Filter_CombinesSystemIgnoredAndExcludePatterns()
    {
        var rawFiles = new[]
        {
            "src/main.cs",          // should pass
            "node_modules/a.js",    // blocked by SystemIgnoredPatterns
            "temp/debug.cs",        // blocked by excludePatterns
            "src/util.cs"           // should pass
        };
        var config = new GcConfiguration
        {
            Filters = new FiltersConfiguration
            {
                SystemIgnoredPatterns = new[] { "node_modules" }
            }
        };
        var result = _filter.FilterFiles(
            rawFiles, config,
            Array.Empty<string>(),
            new[] { "temp/" },
            Array.Empty<string>());

        Assert.True(result.IsSuccess);
        var entries = result.Value!.ToList();
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Path == "src/main.cs");
        Assert.Contains(entries, e => e.Path == "src/util.cs");
    }
}
