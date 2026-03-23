using System.Text.Json;
using gc.Domain.Interfaces;

namespace gc.Domain.Common;

/// <summary>
/// Manages append mode state for detecting consecutive gc runs within a time window.
/// State is persisted to ~/.gcstate as JSON for cross-run tracking.
/// </summary>
public static class AppendStateManager
{
    private const string StateFileName = ".gcstate";
    private const string StateDirectory = "~/.gc";
    private static readonly string StateFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".gcstate"
    );

    private const int DefaultAppendWindowSeconds = 5;

    /// <summary>
    /// State file structure persisted as JSON
    /// </summary>
    private class GcState
    {
        public DateTime LastRun { get; set; }
        public string RepoPath { get; set; } = string.Empty;
        public int ProcessId { get; set; }
    }

    /// <summary>
    /// Checks if the current gc run is within the append window of the previous run.
    /// </summary>
    /// <param name="currentRepoPath">Current repository path</param>
    /// <param name="appendWindowSeconds">Time window in seconds (default: 5)</param>
    /// <returns>True if within append window, false otherwise</returns>
    public static async Task<bool> IsWithinAppendWindowAsync(
        string currentRepoPath,
        int appendWindowSeconds = DefaultAppendWindowSeconds)
    {
        try
        {
            var state = await LoadStateAsync();
            if (state == null)
                return false;

            // Check if it's the same repo
            if (!string.Equals(state.RepoPath, currentRepoPath, StringComparison.OrdinalIgnoreCase))
                return false;

            // Check if within time window
            var timeSinceLastRun = DateTime.UtcNow - state.LastRun;
            return timeSinceLastRun.TotalSeconds <= appendWindowSeconds;
        }
        catch
        {
            // If we can't read state, treat as not in window (safe default)
            return false;
        }
    }

    /// <summary>
    /// Saves the current run state to the state file.
    /// </summary>
    /// <param name="currentRepoPath">Current repository path</param>
    public static async Task SaveStateAsync(string currentRepoPath)
    {
        try
        {
            var state = new GcState
            {
                LastRun = DateTime.UtcNow,
                RepoPath = currentRepoPath,
                ProcessId = Environment.ProcessId
            };

            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(StateFilePath, json);
        }
        catch
        {
            // Silently fail if we can't save state - non-critical feature
            // Could log here but don't want to spam logs on every gc run
        }
    }

    /// <summary>
    /// Loads the state file, handling corruption gracefully.
    /// </summary>
    /// <returns>Parsed state, or null if file doesn't exist or is corrupted</returns>
    private static async Task<GcState?> LoadStateAsync()
    {
        try
        {
            if (!File.Exists(StateFilePath))
                return null;

            var json = await File.ReadAllTextAsync(StateFilePath);
            var state = JsonSerializer.Deserialize<GcState>(json);

            // Validate state has required fields
            if (state == null || state.LastRun == default)
                return null;

            return state;
        }
        catch
        {
            // Corrupted state file - back up and recreate
            try
            {
                var backupPath = StateFilePath + ".corrupted." + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                File.Copy(StateFilePath, backupPath);
                File.Delete(StateFilePath);
            }
            catch
            {
                // Ignore backup failures - not critical
            }

            return null;
        }
    }

    /// <summary>
    /// Gets the last run time from the state file.
    /// </summary>
    /// <returns>Last run DateTime, or null if no state exists</returns>
    public static async Task<DateTime?> GetLastRunAsync()
    {
        try
        {
            var state = await LoadStateAsync();
            return state?.LastRun;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Clears the state file (useful for testing or reset).
    /// </summary>
    public static void ClearState()
    {
        try
        {
            if (File.Exists(StateFilePath))
                File.Delete(StateFilePath);
        }
        catch
        {
            // Ignore clear failures
        }
    }
}
