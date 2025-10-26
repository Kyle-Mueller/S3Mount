using System.IO;
using System.Collections.Concurrent;

namespace S3Mount.Services;

/// <summary>
/// Provides transparent streaming access to large S3 files without downloading the entire file
/// Uses range requests to fetch data on-demand
/// </summary>
public class S3StreamingFileHandler : IDisposable
{
    private readonly S3Service _s3Service;
    private readonly ConcurrentDictionary<string, StreamingFileInfo> _streamingFiles = new();
    private const long CHUNK_SIZE = 10 * 1024 * 1024; // 10MB chunks
    private const long LARGE_FILE_THRESHOLD = 100 * 1024 * 1024; // Files > 100MB use streaming
    
    public S3StreamingFileHandler(S3Service s3Service)
    {
        _s3Service = s3Service;
    }

    /// <summary>
    /// Determines if a file should use streaming based on size
    /// </summary>
    public bool ShouldUseStreaming(long fileSize)
    {
        return fileSize > LARGE_FILE_THRESHOLD;
    }

    /// <summary>
    /// Creates a streaming file wrapper that provides transparent access to S3 data
    /// </summary>
    public async Task<bool> SetupStreamingFileAsync(string localPath, string s3Key, long fileSize)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"S3Streaming - Setting up streaming for {s3Key} ({fileSize / 1024 / 1024}MB)");

            // Create sparse file with correct size
            CreateSparseFile(localPath, fileSize);

            // Mark as sparse and offline
            File.SetAttributes(localPath, FileAttributes.SparseFile | FileAttributes.Offline);

            // Store streaming info
            _streamingFiles[s3Key] = new StreamingFileInfo
            {
                LocalPath = localPath,
                S3Key = s3Key,
                TotalSize = fileSize,
                CachedRanges = new List<DataRange>()
            };

            System.Diagnostics.Debug.WriteLine($"S3Streaming - Streaming setup complete for {s3Key}");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"S3Streaming - Setup failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Downloads a specific range of the file on-demand
    /// </summary>
    public async Task<bool> FetchRangeAsync(string s3Key, long offset, long length)
    {
        if (!_streamingFiles.TryGetValue(s3Key, out var fileInfo))
            return false;

        try
        {
            System.Diagnostics.Debug.WriteLine($"S3Streaming - Fetching range for {s3Key}: {offset}-{offset + length - 1}");

            // Calculate aligned chunk boundaries
            long chunkStart = (offset / CHUNK_SIZE) * CHUNK_SIZE;
            long chunkEnd = Math.Min(((offset + length - 1) / CHUNK_SIZE + 1) * CHUNK_SIZE - 1, fileInfo.TotalSize - 1);

            // Check if range is already cached
            if (IsRangeCached(fileInfo, chunkStart, chunkEnd))
            {
                System.Diagnostics.Debug.WriteLine($"S3Streaming - Range already cached");
                return true;
            }

            // Fetch from S3
            using var stream = await _s3Service.GetObjectRangeAsync(s3Key, chunkStart, chunkEnd);
            if (stream == null)
                return false;

            // Write to local file at correct offset
            using var fileStream = new FileStream(fileInfo.LocalPath, FileMode.Open, FileAccess.Write, FileShare.Read);
            fileStream.Seek(chunkStart, SeekOrigin.Begin);
            await stream.CopyToAsync(fileStream);

            // Mark range as cached
            lock (fileInfo.CachedRanges)
            {
                fileInfo.CachedRanges.Add(new DataRange { Start = chunkStart, End = chunkEnd });
                MergeAdjacentRanges(fileInfo.CachedRanges);
            }

            System.Diagnostics.Debug.WriteLine($"S3Streaming - Range fetched: {chunkStart}-{chunkEnd}");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"S3Streaming - Range fetch failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Prefetch the next chunk based on sequential access pattern
    /// </summary>
    public async Task PrefetchNextChunkAsync(string s3Key, long currentPosition)
    {
        if (!_streamingFiles.TryGetValue(s3Key, out var fileInfo))
            return;

        try
        {
            long nextChunkStart = ((currentPosition / CHUNK_SIZE) + 1) * CHUNK_SIZE;
            long nextChunkEnd = Math.Min(nextChunkStart + CHUNK_SIZE - 1, fileInfo.TotalSize - 1);

            if (!IsRangeCached(fileInfo, nextChunkStart, nextChunkEnd))
            {
                System.Diagnostics.Debug.WriteLine($"S3Streaming - Prefetching next chunk: {nextChunkStart}-{nextChunkEnd}");
                await FetchRangeAsync(s3Key, nextChunkStart, CHUNK_SIZE);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"S3Streaming - Prefetch failed: {ex.Message}");
        }
    }

    private bool IsRangeCached(StreamingFileInfo fileInfo, long start, long end)
    {
        lock (fileInfo.CachedRanges)
        {
            return fileInfo.CachedRanges.Any(r => r.Start <= start && r.End >= end);
        }
    }

    private void MergeAdjacentRanges(List<DataRange> ranges)
    {
        if (ranges.Count <= 1)
            return;

        ranges.Sort((a, b) => a.Start.CompareTo(b.Start));

        for (int i = 0; i < ranges.Count - 1; i++)
        {
            if (ranges[i].End >= ranges[i + 1].Start - 1)
            {
                ranges[i].End = Math.Max(ranges[i].End, ranges[i + 1].End);
                ranges.RemoveAt(i + 1);
                i--;
            }
        }
    }

    private void CreateSparseFile(string filePath, long size)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            fs.SetLength(size);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"S3Streaming - Failed to create sparse file: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _streamingFiles.Clear();
    }

    private class StreamingFileInfo
    {
        public string LocalPath { get; set; } = string.Empty;
        public string S3Key { get; set; } = string.Empty;
        public long TotalSize { get; set; }
        public List<DataRange> CachedRanges { get; set; } = new();
    }

    private class DataRange
    {
        public long Start { get; set; }
        public long End { get; set; }
    }
}
