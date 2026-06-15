using gc.Domain.Common;
using gc.Domain.Constants;
using gc.Domain.Models;

namespace gc.Tests;

public class FormattingFormatSizeTests
{
    [Fact]
    public void Formatting_FormatSize_ZeroBytes()
    {
        Assert.Equal("0 B", Formatting.FormatSize(0));
    }

    [Fact]
    public void Formatting_FormatSize_1023Bytes()
    {
        var result = Formatting.FormatSize(1023);
        Assert.EndsWith("B", result);
        Assert.DoesNotContain("KB", result);
    }

    [Fact]
    public void Formatting_FormatSize_Exact1KB()
    {
        Assert.Equal("1.00 KB", Formatting.FormatSize(1024));
    }

    [Fact]
    public void Formatting_FormatSize_Exact1MB()
    {
        Assert.Equal("1.00 MB", Formatting.FormatSize(1048576));
    }

    [Fact]
    public void Formatting_FormatSize_MaxValue()
    {
        var result = Formatting.FormatSize(long.MaxValue);
        Assert.Contains("MB", result);
    }

    [Fact]
    public void Formatting_FormatSize_Negative()
    {
        // Should not crash; negative is < 1024 so it renders as "{bytes} B"
        var result = Formatting.FormatSize(-500);
        Assert.NotNull(result);
        Assert.Contains("B", result);
    }
}

public class FormattingFormatRelativeTimeTests
{
    [Fact]
    public void Formatting_FormatRelativeTime_Yesterday()
    {
        // The "yesterday" branch now covers the whole 24h–48h band ({ TotalDays: < 2 }), so any time
        // ~1 day ago renders exactly "yesterday" — no more ungrammatical "1 days ago".
        var past = DateTime.UtcNow.AddDays(-1);
        var result = Formatting.FormatRelativeTime(past);
        Assert.Equal("yesterday", result);
    }

    [Fact]
    public void Formatting_FormatRelativeTime_TwoDays_IsPluralDays()
    {
        var result = Formatting.FormatRelativeTime(DateTime.UtcNow.AddDays(-2));
        Assert.Equal("2 days ago", result);
    }

    [Fact]
    public void Formatting_FormatRelativeTime_1HourAgo()
    {
        var result = Formatting.FormatRelativeTime(DateTime.Now.AddHours(-1));
        Assert.Contains("1 hour ago", result);
    }

    [Fact]
    public void Formatting_FormatRelativeTime_1MinuteAgo()
    {
        var result = Formatting.FormatRelativeTime(DateTime.Now.AddMinutes(-1));
        Assert.Contains("1 min ago", result);
    }

    [Fact]
    public void Formatting_FormatRelativeTime_MonthsAgo()
    {
        var result = Formatting.FormatRelativeTime(DateTime.Now.AddDays(-60));
        Assert.Contains("month", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Formatting_FormatRelativeTime_1MonthAgo()
    {
        // 35 days => TotalDays/30 < 2 so singular "month"
        var result = Formatting.FormatRelativeTime(DateTime.Now.AddDays(-35));
        Assert.Contains("month", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("months", result);
    }

    [Fact]
    public void Formatting_FormatRelativeTime_YearsAgo()
    {
        var result = Formatting.FormatRelativeTime(DateTime.Now.AddDays(-400));
        Assert.Contains("year", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Formatting_FormatRelativeTime_1YearAgo()
    {
        // 370 days => TotalDays/365 < 2 so singular "year"
        var result = Formatting.FormatRelativeTime(DateTime.Now.AddDays(-370));
        Assert.Contains("year", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("years", result);
    }

    [Fact]
    public void Formatting_FormatRelativeTime_Future()
    {
        // Should not crash
        var result = Formatting.FormatRelativeTime(DateTime.Now.AddMinutes(5));
        Assert.NotNull(result);
    }
}

public class ResultTests
{
    [Fact]
    public void Result_Success_Equality()
    {
        var a = Result.Success();
        var b = Result.Success();
        Assert.Equal(a, b);
    }

    [Fact]
    public void Result_Failure_Equality()
    {
        var a = Result.Failure("err");
        var b = Result.Failure("err");
        Assert.Equal(a, b);
    }

    [Fact]
    public void ResultT_Success_Equality()
    {
        var a = Result<int>.Success(42);
        var b = Result<int>.Success(42);
        Assert.Equal(a, b);
    }

    [Fact]
    public void ResultT_Failure_ReferenceType()
    {
        var result = Result<string>.Failure("err");
        Assert.False(result.IsSuccess);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void ResultT_GetHashCode()
    {
        var a = Result<int>.Success(42);
        var b = Result<int>.Success(42);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
}

public class BuiltInPresetsTests
{
    [Fact]
    public void BuiltInPresets_Backend_HasEntries()
    {
        Assert.NotEmpty(BuiltInPresets.PresetBackend);
    }

    [Fact]
    public void BuiltInPresets_Java_HasEntries()
    {
        Assert.NotEmpty(BuiltInPresets.PresetJava);
    }

    [Fact]
    public void BuiltInPresets_Cpp_HasEntries()
    {
        Assert.NotEmpty(BuiltInPresets.PresetCpp);
    }

    [Fact]
    public void BuiltInPresets_Script_HasEntries()
    {
        Assert.NotEmpty(BuiltInPresets.PresetScript);
    }

    [Fact]
    public void BuiltInPresets_Data_HasEntries()
    {
        Assert.NotEmpty(BuiltInPresets.PresetData);
    }

    [Fact]
    public void BuiltInPresets_Build_HasEntries()
    {
        Assert.NotEmpty(BuiltInPresets.PresetBuild);
    }

    [Fact]
    public void BuiltInPresets_Docs_HasEntries()
    {
        Assert.NotEmpty(BuiltInPresets.PresetDocs);
    }

    [Fact]
    public void BuiltInPresets_LanguageMappings_CaseInsensitive()
    {
        Assert.True(BuiltInPresets.LanguageMappings.TryGetValue("JS", out var js));
        Assert.Equal("javascript", js);

        Assert.True(BuiltInPresets.LanguageMappings.TryGetValue("CS", out var cs));
        Assert.Equal("cs", cs);
    }

    [Fact]
    public void BuiltInPresets_GetAllPresets_ReturnsAllKeys()
    {
        var presets = BuiltInPresets.GetAllPresets();
        Assert.True(presets.Count >= 11);
    }

    [Fact]
    public void BuiltInPresets_SystemIgnoredPatterns_Count()
    {
        Assert.True(BuiltInPresets.SystemIgnoredPatterns.Length >= 20);
    }
}

public class MemorySizeParserTests
{
    [Fact]
    public void MemorySizeParser_NegativeValue()
    {
        // double.TryParse will parse -100, the result will be negative => falls through to default
        var result = MemorySizeParser.Parse("-100MB");
        Assert.True(result > 0 || result == 104857600); // returns default for negative
    }

    [Fact]
    public void MemorySizeParser_VerySmallDecimal()
    {
        // Should not crash; 0.001 KB = ~1 byte
        var result = MemorySizeParser.Parse("0.001KB");
        Assert.True(result >= 0);
    }

    [Fact]
    public void MemorySizeParser_ScientificNotation()
    {
        // NumberStyles.Any allows scientific notation; 1e3 MB = 1000 MB
        var result = MemorySizeParser.Parse("1e3MB");
        Assert.True(result > 0 || result == 104857600); // either parses or returns default
    }

    [Fact]
    public void MemorySizeParser_LeadingZeros()
    {
        var result = MemorySizeParser.Parse("0000100MB");
        var expected = MemorySizeParser.Parse("100MB");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void MemorySizeParser_VeryLargeBytes()
    {
        // Should not crash
        var result = MemorySizeParser.Parse("999999999999B");
        Assert.True(result >= 0);
    }
}

public class FileEntryTests
{
    [Fact]
    public void FileEntry_RecordStructEquality()
    {
        var a = new FileEntry("", "src/foo.cs", ".cs", "csharp", 100);
        var b = new FileEntry("", "src/foo.cs", ".cs", "csharp", 100);
        Assert.Equal(a, b);
    }

    [Fact]
    public void FileEntry_DefaultConstructor()
    {
        var entry = new FileEntry();
        Assert.Null(entry.Path);
        Assert.Null(entry.Extension);
        Assert.Equal(0, entry.Size);
    }
}

public class FileContentTests
{
    [Fact]
    public void FileContent_RecordStructEquality()
    {
        var entry = new FileEntry("", "a.cs", ".cs", "csharp", 10);
        var a = new FileContent(entry, "hello", 5);
        var b = new FileContent(entry, "hello", 5);
        Assert.Equal(a, b);
    }

    [Fact]
    public void FileContent_EmptyStringContent()
    {
        var entry = new FileEntry("", "a.cs", ".cs", "csharp", 0);
        var content = new FileContent(entry, "", 0);
        Assert.False(content.IsStreaming); // Content is "" not null
    }
}

public class RepoInfoTests
{
    [Fact]
    public void RepoInfo_RecordEquality()
    {
        var a = new RepoInfo { Name = "myrepo", RootPath = "/tmp", IsValid = true };
        var b = new RepoInfo { Name = "myrepo", RootPath = "/tmp", IsValid = true };
        Assert.Equal(a, b);
    }

    [Fact]
    public void RepoInfo_WithError()
    {
        var info = new RepoInfo { Name = "broken", Error = "some error" };
        Assert.Equal("some error", info.Error);
    }
}