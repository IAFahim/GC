using gc.Domain.Common;
using gc.Domain.Constants;
using Xunit;

namespace gc.Tests.FeatureTests;

public class AppendModeTests
{
    [Fact]
    public async Task AppendStateManager_SavesAndLoadsState()
    {
        // Arrange
        var testRepoPath = "/tmp/test/repo";
        AppendStateManager.ClearState();

        // Act - Save state
        await AppendStateManager.SaveStateAsync(testRepoPath);

        // Assert - Load state immediately after saving
        var isWithinWindow = await AppendStateManager.IsWithinAppendWindowAsync(testRepoPath, appendWindowSeconds: 5);
        Assert.True(isWithinWindow);

        // Cleanup
        AppendStateManager.ClearState();
    }

    [Fact]
    public async Task AppendStateManager_DetectsExpiredWindow()
    {
        // Arrange
        var testRepoPath = "/tmp/test/repo2";
        AppendStateManager.ClearState();

        // Act - Save state
        await AppendStateManager.SaveStateAsync(testRepoPath);

        // Wait for window to expire
        await Task.Delay(6000); // Wait 6 seconds (window is 5 seconds)

        // Assert - Should not be within window
        var isWithinWindow = await AppendStateManager.IsWithinAppendWindowAsync(testRepoPath, appendWindowSeconds: 5);
        Assert.False(isWithinWindow);

        // Cleanup
        AppendStateManager.ClearState();
    }

    [Fact]
    public async Task AppendStateManager_HandlesCorruptedState()
    {
        // Arrange
        var stateFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".gcstate"
        );

        // Create corrupted state file
        await File.WriteAllTextAsync(stateFile, "invalid json content");

        // Act & Assert - Should handle corruption gracefully
        var isWithinWindow = await AppendStateManager.IsWithinAppendWindowAsync("/tmp/test", appendWindowSeconds: 5);
        Assert.False(isWithinWindow);

        // Cleanup
        AppendStateManager.ClearState();
    }

    [Fact]
    public async Task AppendStateManager_DetectsDifferentRepo()
    {
        // Arrange
        var repo1 = "/tmp/test/repo1";
        var repo2 = "/tmp/test/repo2";
        AppendStateManager.ClearState();

        // Act - Save state for repo1
        await AppendStateManager.SaveStateAsync(repo1);

        // Assert - Should not detect append mode for different repo
        var isWithinWindow = await AppendStateManager.IsWithinAppendWindowAsync(repo2, appendWindowSeconds: 5);
        Assert.False(isWithinWindow);

        // Cleanup
        AppendStateManager.ClearState();
    }

    [Fact]
    public async Task AppendStateManager_ClearsState()
    {
        // Arrange
        var testRepoPath = "/tmp/test/repo3";
        AppendStateManager.ClearState();

        // Act - Save and then clear state
        await AppendStateManager.SaveStateAsync(testRepoPath);
        AppendStateManager.ClearState();

        // Assert - Should not find state after clearing
        var isWithinWindow = await AppendStateManager.IsWithinAppendWindowAsync(testRepoPath, appendWindowSeconds: 5);
        Assert.False(isWithinWindow);
    }
}
