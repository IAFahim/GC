using gc.CLI.Services;
using gc.Domain.Constants;
using gc.Domain.Models.Configuration;

namespace gc.Tests;

/// <summary>
///     Tests for fun CLI keywords: grab, yeet, type, spit, zap, brain, horde.
///     Also tests the new short flags: -g, -y, -t, -s, -z, -b.
/// </summary>
public class FunKeywordsTests
{
    private readonly GcConfiguration _config = BuiltInPresets.GetDefaultConfiguration();
    private readonly CliParser _parser = new();

    // =========================================================================
    // 1. grab keyword → Paths
    // =========================================================================

    [Fact]
    public void Grab_SetsPathsState()
    {
        var result = _parser.Parse(["grab", "src", "lib"], _config);
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Paths.Length);
        Assert.Contains("src", result.Value.Paths);
        Assert.Contains("lib", result.Value.Paths);
    }

    [Fact]
    public void ShortG_SetsPathsState()
    {
        var result = _parser.Parse(["-g", "src"], _config);
        Assert.True(result.IsSuccess);
        Assert.Contains("src", result.Value!.Paths);
    }

    [Fact]
    public void Grab_MixedWithStandardPath()
    {
        // grab sets state to Paths, subsequent bare args also go to paths
        var result = _parser.Parse(["grab", "src", "tests"], _config);
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Paths.Length);
    }

    // =========================================================================
    // 2. yeet keyword → Excludes
    // =========================================================================

    [Fact]
    public void Yeet_SetsExcludesState()
    {
        var result = _parser.Parse(["yeet", "bin", "obj"], _config);
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Excludes.Length);
        Assert.Contains("bin", result.Value.Excludes);
        Assert.Contains("obj", result.Value.Excludes);
    }

    [Fact]
    public void ShortY_SetsExcludesState()
    {
        var result = _parser.Parse(["-y", "node_modules"], _config);
        Assert.True(result.IsSuccess);
        Assert.Contains("node_modules", result.Value!.Excludes);
    }

    // =========================================================================
    // 3. type keyword → Extensions
    // =========================================================================

    [Fact]
    public void Type_SetsExtensionsState()
    {
        var result = _parser.Parse(["type", "cs", "md"], _config);
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Extensions.Length);
        Assert.Contains("cs", result.Value.Extensions);
        Assert.Contains("md", result.Value.Extensions);
    }

    [Fact]
    public void ShortT_SetsExtensionsState()
    {
        var result = _parser.Parse(["-t", "py"], _config);
        Assert.True(result.IsSuccess);
        Assert.Contains("py", result.Value!.Extensions);
    }

    // =========================================================================
    // 4. spit keyword → Output
    // =========================================================================

    [Fact]
    public void Spit_SetsOutputFile()
    {
        var result = _parser.Parse(["spit", "brain.md"], _config);
        Assert.True(result.IsSuccess);
        Assert.Equal("brain.md", result.Value!.OutputFile);
    }

    [Fact]
    public void ShortS_SetsOutputFile()
    {
        var result = _parser.Parse(["-s", "out.md"], _config);
        Assert.True(result.IsSuccess);
        Assert.Equal("out.md", result.Value!.OutputFile);
    }

    [Fact]
    public void SpitWithoutValue_ReturnsError()
    {
        var result = _parser.Parse(["spit"], _config);
        Assert.False(result.IsSuccess);
        Assert.Contains("Missing value", result.Error!);
    }

    // =========================================================================
    // 5. zap keyword → ExcludeLineIfStart
    // =========================================================================

    [Fact]
    public void Zap_SetsExcludeLineIfStart()
    {
        var result = _parser.Parse(["zap", "//", "///"], _config);
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.ExcludeLineIfStart.Length);
        Assert.Contains("//", result.Value.ExcludeLineIfStart);
        Assert.Contains("///", result.Value.ExcludeLineIfStart);
    }

    [Fact]
    public void ShortZ_SetsExcludeLineIfStart()
    {
        var result = _parser.Parse(["-z", "#"], _config);
        Assert.True(result.IsSuccess);
        Assert.Contains("#", result.Value!.ExcludeLineIfStart);
    }

    // =========================================================================
    // 6. brain keyword → BrainMode
    // =========================================================================

    [Fact]
    public void Brain_SetsBrainMode()
    {
        var result = _parser.Parse(["brain"], _config);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.BrainMode);
    }

    [Fact]
    public void ShortB_SetsBrainMode()
    {
        var result = _parser.Parse(["-b"], _config);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.BrainMode);
    }

    [Fact]
    public void LongBrainFlag_SetsBrainMode()
    {
        var result = _parser.Parse(["--brain"], _config);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.BrainMode);
    }

    // =========================================================================
    // 7. horde keyword → Cluster mode
    // =========================================================================

    [Fact]
    public void Horde_SetsCluster()
    {
        var result = _parser.Parse(["horde"], _config);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Cluster);
    }

    [Fact]
    public void Horde_WithClusterDir()
    {
        var result = _parser.Parse(["horde", "--cluster-dir", "/tmp/repos"], _config);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Cluster);
        Assert.Equal("/tmp/repos", result.Value!.ClusterDir);
    }

    // =========================================================================
    // 8. Full fun combos (the examples from the spec)
    // =========================================================================

    [Fact]
    public void FullCombo_GrabYeetTypeBrainSpit()
    {
        // gc grab src yeet tests type cs brain spit brain.md
        var result = _parser.Parse(["grab", "src", "yeet", "tests", "type", "cs", "brain", "spit", "brain.md"],
            _config);
        Assert.True(result.IsSuccess);
        Assert.Contains("src", result.Value!.Paths);
        Assert.Contains("tests", result.Value!.Excludes);
        Assert.Contains("cs", result.Value!.Extensions);
        Assert.True(result.Value.BrainMode);
        Assert.Equal("brain.md", result.Value.OutputFile);
    }

    [Fact]
    public void FullCombo_HordeYeetZapSpit()
    {
        // gc horde yeet bin obj zap "Console.Log" spit context.md
        var result = _parser.Parse(["horde", "yeet", "bin", "obj", "zap", "Console.Log", "spit", "context.md"],
            _config);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Cluster);
        Assert.Equal(2, result.Value.Excludes.Length);
        Assert.Contains("Console.Log", result.Value.ExcludeLineIfStart);
        Assert.Equal("context.md", result.Value.OutputFile);
    }

    [Fact]
    public void FullCombo_ShortFlags()
    {
        // gc -g src -y tests -t cs -b -s brain.md
        var result = _parser.Parse(["-g", "src", "-y", "tests", "-t", "cs", "-b", "-s", "brain.md"], _config);
        Assert.True(result.IsSuccess);
        Assert.Contains("src", result.Value!.Paths);
        Assert.Contains("tests", result.Value!.Excludes);
        Assert.Contains("cs", result.Value!.Extensions);
        Assert.True(result.Value.BrainMode);
        Assert.Equal("brain.md", result.Value.OutputFile);
    }

    // =========================================================================
    // 9. Mixed fun + standard flags
    // =========================================================================

    [Fact]
    public void MixedFunAndStandardFlags()
    {
        var result = _parser.Parse(["grab", "src", "-v", "brain", "-f"], _config);
        Assert.True(result.IsSuccess);
        Assert.Contains("src", result.Value!.Paths);
        Assert.True(result.Value.Verbose);
        Assert.True(result.Value.BrainMode);
        Assert.True(result.Value.Force);
    }

    [Fact]
    public void FunKeywordsAfterStateChange()
    {
        // grab src → then type cs (type should override state)
        var result = _parser.Parse(["grab", "src", "type", "cs"], _config);
        Assert.True(result.IsSuccess);
        Assert.Contains("src", result.Value!.Paths);
        Assert.Contains("cs", result.Value!.Extensions);
    }

    // =========================================================================
    // 10. BrainMode defaults to false
    // =========================================================================

    [Fact]
    public void BrainMode_DefaultsFalse()
    {
        var result = _parser.Parse([], _config);
        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.BrainMode);
    }

    // =========================================================================
    // 11. Edge cases — fun keywords as values (they should be intercepted)
    // =========================================================================

    [Fact]
    public void FunKeyword_GrabAsPath_NotShadowedWhenAfterState()
    {
        // After "yeet bin", the word "grab" should still be intercepted as a keyword
        // because TryGetNewState runs before state processing
        var result = _parser.Parse(["yeet", "bin", "grab", "src"], _config);
        Assert.True(result.IsSuccess);
        Assert.Contains("bin", result.Value!.Excludes);
        Assert.Contains("src", result.Value!.Paths);
        // "grab" was intercepted as keyword, not treated as a yeet value
    }

    [Fact]
    public void ShortG_StillWorksAlongsideOldP()
    {
        // Both -g and -p should work
        var r1 = _parser.Parse(["-g", "a"], _config);
        var r2 = _parser.Parse(["-p", "a"], _config);
        Assert.True(r1.IsSuccess);
        Assert.True(r2.IsSuccess);
        Assert.Equal(r2.Value!.Paths, r1.Value!.Paths);
    }

    [Fact]
    public void ShortT_StillWorksAlongsideOldE()
    {
        var r1 = _parser.Parse(["-t", "cs"], _config);
        var r2 = _parser.Parse(["-e", "cs"], _config);
        Assert.True(r1.IsSuccess);
        Assert.True(r2.IsSuccess);
        Assert.Equal(r2.Value!.Extensions, r1.Value!.Extensions);
    }

    [Fact]
    public void ShortY_StillWorksAlongsideOldX()
    {
        var r1 = _parser.Parse(["-y", "bin"], _config);
        var r2 = _parser.Parse(["-x", "bin"], _config);
        Assert.True(r1.IsSuccess);
        Assert.True(r2.IsSuccess);
        Assert.Equal(r2.Value!.Excludes, r1.Value!.Excludes);
    }

    [Fact]
    public void ShortS_StillWorksAlongsideOldO()
    {
        var r1 = _parser.Parse(["-s", "out.md"], _config);
        var r2 = _parser.Parse(["-o", "out.md"], _config);
        Assert.True(r1.IsSuccess);
        Assert.True(r2.IsSuccess);
        Assert.Equal(r2.Value!.OutputFile, r1.Value!.OutputFile);
    }

    [Fact]
    public void ShortZ_StillWorksAlongsideOldExcludeLineIfStart()
    {
        var r1 = _parser.Parse(["-z", "//"], _config);
        var r2 = _parser.Parse(["--exclude-line-if-start", "//"], _config);
        Assert.True(r1.IsSuccess);
        Assert.True(r2.IsSuccess);
        Assert.Equal(r2.Value!.ExcludeLineIfStart, r1.Value!.ExcludeLineIfStart);
    }
}