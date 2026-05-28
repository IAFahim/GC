namespace gc.Domain.Constants;

/// <summary>
/// Magic number constants extracted from throughout the codebase.
/// Centralizing these ensures consistent behavior and easier tuning.
/// </summary>
public static class MagicNumbers
{
    // === I/O Buffer Sizes ===
    
    /// <summary>
    /// Default buffer size for FileStream operations. Matches typical sector size
    /// for optimal disk I/O performance on most filesystems.
    /// </summary>
    public const int FileStreamBufferSize = 4096;
    
    /// <summary>
    /// Buffer size for PipeReader/PipeWriter operations. Large enough to handle
    /// typical file chunks without excessive pressure.
    /// </summary>
    public const int PipeBufferSize = 65536;
    
    /// <summary>
    /// StringBuilder initial capacity for line exclusion processing.
    /// Sized for typical single-file content without reallocation.
    /// </summary>
    public const int LineExclusionCapacity = 4096;
    
    /// <summary>
    /// Chunk size for writing large strings to PipeWriter in chunks.
    /// Balanced between allocation pressure and writes.
    /// </summary>
    public const int ChunkWriteSize = 65536;
    
    /// <summary>
    /// Maximum string length before switching to chunked WriteStringLine processing.
    /// </summary>
    public const int ChunkWriteStringThreshold = 4096;
    
    /// <summary>
    /// Character chunk size when chunking large strings.
    /// </summary>
    public const int ChunkCharCount = 4096;
    
    // === File Size Limits ===
    
    /// <summary>
    /// Maximum file size for fast streaming (10 MB). Files larger than this use
    /// streamed reading instead of full in-memory buffer.
    /// </summary>
    public const int MaxFileSizeForFastStream = 10 * 1024 * 1024; // 10MB
    
    /// <summary>
    /// Preview length for binary detection. Only reads the first N bytes to
    /// determine if a file is binary before full processing.
    /// </summary>
    public const int BinaryDetectionPreviewLength = 4096;
    
    /// <summary>
    /// Preview length for content-based filtering. Files are checked for patterns
    /// using only this initial portion for fast rejection.
    /// </summary>
    public const int ContentFilterPreviewLength = 8192;
    
    /// <summary>
    /// Sample size for language detection (fence detection). Uses a smaller
    /// sample to quickly determine the appropriate code fence style.
    /// </summary>
    public const int FenceDetectionSampleLength = 4096;
    
    // === Historical Buffer Sizes ===
    
    /// <summary>
    /// Buffer size for frequency analysis operations.
    /// </summary>
    public const int FrequencyAnalysisBufferSize = 4096;
    
    /// <summary>
    /// Array pool buffer size for FileDiscovery operations.
    /// </summary>
    public const int FileDiscoveryBufferSize = 65536;
}
