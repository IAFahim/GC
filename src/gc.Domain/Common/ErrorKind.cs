namespace gc.Domain.Common;

/// <summary>
/// Structured error codes for gc operations.
/// Provides machine-readable error classification alongside human-readable messages.
/// </summary>
public enum ErrorKind
{
    // === Discovery ===
    DiscoveryFailed = 100,
    DiscoveryNoFiles = 101,
    DiscoveryPathNotFound = 102,
    DiscoveryGitNotAvailable = 103,
    DiscoveryMaxDepthExceeded = 104,

    // === Filtering ===
    FilterNoMatch = 200,
    FilterPatternInvalid = 201,
    FilterPathDenied = 202,
    FilterExtensionInvalid = 203,

    // === Content ===
    ContentReadFailed = 300,
    ContentSizeExceedsLimit = 301,
    ContentBinarySkipped = 302,
    ContentEncodingInvalid = 303,
    ContentEmpty = 304,

    // === Output ===
    OutputWriteFailed = 400,
    OutputDirCreateFailed = 401,
    OutputClipboardFull = 402,
    OutputSizeExceedsMemoryLimit = 403,
    OutputFileLocked = 404,
    OutputAppendFailed = 405,

    // === Configuration ===
    ConfigInvalid = 500,
    ConfigFileNotFound = 501,
    ConfigFileCorrupt = 502,
    ConfigDuplicatePreset = 503,
    ConfigPresetNotFound = 504,
    ConfigSecretNotFound = 505,

    // === Compression ===
    CompressInternalFailed = 600,
    CompressSqzNotAvailable = 601,
    CompressNoSavings = 602,
    CompressDecodingFailed = 603,
    CompressTimeout = 604,

    // === Cluster ===
    ClusterDirNotFound = 700,
    ClusterNoReposFound = 701,
    ClusterDiscoveryFailed = 702,

    // === System ===
    SystemOutOfMemory = 800,
    SystemOperationCancelled = 801,
    SystemFileNotFound = 802,
    SystemPermissionDenied = 803,
    SystemArgumentMissing = 804,
    SystemArgumentInvalid = 805,

    // === General ===
    Unknown = 999,
}

public static class ErrorKindExtensions
{
    /// <summary>
    /// Returns a category label for grouping errors in logs/UI.
    /// </summary>
    public static string Category(this ErrorKind kind) => kind switch
    {
        >= (ErrorKind)100 and <= (ErrorKind)199 => "Discovery",
        >= (ErrorKind)200 and <= (ErrorKind)299 => "Filter",
        >= (ErrorKind)300 and <= (ErrorKind)399 => "Content",
        >= (ErrorKind)400 and <= (ErrorKind)499 => "Output",
        >= (ErrorKind)500 and <= (ErrorKind)599 => "Config",
        >= (ErrorKind)600 and <= (ErrorKind)699 => "Compression",
        >= (ErrorKind)700 and <= (ErrorKind)799 => "Cluster",
        >= (ErrorKind)800 and <= (ErrorKind)899 => "System",
        _ => "Unknown"
    };

    /// <summary>
    /// Returns whether the error is retryable (transient) vs fatal.
    /// </summary>
    public static bool IsRetryable(this ErrorKind kind) => kind switch
    {
        ErrorKind.DiscoveryFailed => true,
        ErrorKind.ContentReadFailed => true,
        ErrorKind.CompressInternalFailed => true,
        ErrorKind.OutputWriteFailed => true,
        ErrorKind.SystemOutOfMemory => true,
        _ => false
    };
}
