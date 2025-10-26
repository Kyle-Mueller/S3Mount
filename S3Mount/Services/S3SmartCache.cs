using S3Mount.Models;
using System.IO;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Amazon.S3.Model;

namespace S3Mount.Services;

/// <summary>
/// Smart on-demand file cache for S3 - downloads files when accessed, auto-purges when not in use
/// Similar to OneDrive's file on-demand feature - no permanent storage
/// </summary>
public class S3SmartCache : IDisposable
{
    private readonly S3Service _s3Service;
    private readonly S3MountConfiguration _config;
    private readonly string _cacheRoot;
    private readonly ConcurrentDictionary<string, FileMetadata> _fileMetadata = new();
    private readonly ConcurrentDictionary<string, FileWatcher> _activeFiles = new();
    private FileSystemWatcher? _fileWatcher;
    private bool _isActive = false;
    private readonly long _maxCacheSize = 2L * 1024 * 1024 * 1024; // 2GB default - will be purged
    private Timer? _cleanupTimer;
    private readonly HashSet<string> _downloadingFiles = new();
    private readonly object _downloadLock = new object();
    private S3StreamingFileHandler? _streamingHandler;
    private Timer? _pollingTimer;
    private readonly ConcurrentDictionary<string, DateTime> _lastAccessCheck = new();

    // Large file threshold for streaming (100MB)
    private const long STREAMING_THRESHOLD = 100 * 1024 * 1024;
    
    // Small file threshold - files smaller than this are downloaded immediately (5MB)
    private const long SMALL_FILE_THRESHOLD = 5 * 1024 * 1024;

    // P/Invoke for creating sparse files
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        IntPtr hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint GENERIC_WRITE = 0x40000000;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const uint FSCTL_SET_SPARSE = 0x000900c4;

    public S3SmartCache(S3Service s3Service, S3MountConfiguration config, string cacheRoot)
    {
        _s3Service = s3Service;
        _config = config;
        _cacheRoot = cacheRoot;
        _streamingHandler = new S3StreamingFileHandler(s3Service);
    }

    public async Task<bool> InitializeCacheAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"S3Cache - Initializing cache at {_cacheRoot}");
            
            // Create cache directory
            Directory.CreateDirectory(_cacheRoot);

            // Populate directory structure with placeholder files
            await PopulateDirectoryStructureAsync();

            // Set up file system watcher for on-demand downloads and uploads
            SetupFileWatcher();

            // Start cleanup timer (runs every 30 seconds)
            _cleanupTimer = new Timer(
                callback: _ => CleanupCache(),
                state: null,
                dueTime: TimeSpan.FromSeconds(30),
                period: TimeSpan.FromSeconds(30));

            _isActive = true;
            System.Diagnostics.Debug.WriteLine("S3Cache - Initialization complete");
            
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"S3Cache - Initialization failed: {ex.Message}");
            return false;
        }
    }

    private async Task PopulateDirectoryStructureAsync()
    {
        System.Diagnostics.Debug.WriteLine("S3Cache - Populating directory structure");
        
        try
        {
            // List all S3 objects
            var objects = await _s3Service.ListObjectsAsync("");
            System.Diagnostics.Debug.WriteLine($"S3Cache - Found {objects.Count} S3 objects");

            int filesCreated = 0;
            int dirsCreated = 0;

            foreach (var obj in objects)
            {
                var key = obj.Key;
                var localPath = Path.Combine(_cacheRoot, key.Replace('/', '\\'));

                // Create directory if this is a folder
                if (key.EndsWith('/'))
                {
                    Directory.CreateDirectory(localPath);
                    dirsCreated++;
                    continue;
                }

                // Create parent directory
                var parentDir = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrEmpty(parentDir))
                {
                    Directory.CreateDirectory(parentDir);
                }

                // Store metadata
                _fileMetadata[key] = new FileMetadata
                {
                    Key = key,
                    Size = obj.Size.GetValueOrDefault(0),
                    LastModified = obj.LastModified ?? DateTime.UtcNow,
                    ETag = obj.ETag ?? string.Empty
                };

                // Create placeholder file - DO NOT use Offline attribute as it prevents reading
                if (!File.Exists(localPath))
                {
                    var fileSize = obj.Size.GetValueOrDefault(0);
                    
                    // Download small files (< 5MB) immediately for instant access
                    // This includes most images, documents, etc.
                    if (fileSize > 0 && fileSize <= SMALL_FILE_THRESHOLD)
                    {
                        System.Diagnostics.Debug.WriteLine($"S3Cache - Small file, downloading immediately: {key} ({fileSize / 1024}KB)");
                        
                        try
                        {
                            using var s3Stream = await _s3Service.GetObjectStreamAsync(key);
                            if (s3Stream != null)
                            {
                                using var fileStream = File.Create(localPath);
                                await s3Stream.CopyToAsync(fileStream);
                                
                                File.SetAttributes(localPath, FileAttributes.Normal);
                                
                                if (obj.LastModified.HasValue)
                                {
                                    File.SetCreationTime(localPath, obj.LastModified.Value);
                                    File.SetLastWriteTime(localPath, obj.LastModified.Value);
                                }
                                
                                _activeFiles[key] = new FileWatcher
                                {
                                    LocalPath = localPath,
                                    Key = key,
                                    LastAccess = DateTime.Now,
                                    Size = fileSize
                                };
                                
                                filesCreated++;
                                System.Diagnostics.Debug.WriteLine($"S3Cache - Small file ready: {key}");
                                continue; // Skip creating placeholder
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"S3Cache - Failed to download small file: {ex.Message}");
                        }
                    }
                    
                    // For larger files, create empty placeholder WITHOUT Offline attribute
                    // This allows the file to be opened, triggering a download
                    File.WriteAllBytes(localPath, Array.Empty<byte>());
                    
                    // Use Hidden + System attributes to mark as placeholder internally
                    // Don't use Offline as it prevents reading
                    File.SetAttributes(localPath, FileAttributes.Hidden | FileAttributes.System);
                    
                    // Set file time to match S3
                    if (obj.LastModified.HasValue)
                    {
                        File.SetCreationTime(localPath, obj.LastModified.Value);
                        File.SetLastWriteTime(localPath, obj.LastModified.Value);
                    }

                    filesCreated++;
                }
            }

            System.Diagnostics.Debug.WriteLine($"S3Cache - Created {filesCreated} placeholder files, {dirsCreated} directories");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"S3Cache - Error populating structure: {ex.Message}");
            throw;
        }
    }

    private void CreateSparseFile(string filePath, long size)
    {
        try
        {
            // Create file with specified size but no actual data (sparse file)
            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                fs.SetLength(size);
            }

            // Mark as sparse file
            IntPtr handle = CreateFile(
                filePath,
                GENERIC_WRITE,
                0,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL,
                IntPtr.Zero);

            if (handle != new IntPtr(-1))
            {
                DeviceIoControl(handle, FSCTL_SET_SPARSE, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
                CloseHandle(handle);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"S3Cache - Failed to create sparse file: {ex.Message}");
            // Fall back to empty file
            File.WriteAllBytes(filePath, Array.Empty<byte>());
        }
    }

    private void SetupFileWatcher()
    {
        _fileWatcher = new FileSystemWatcher(_cacheRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | 
                          NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.LastAccess
        };

        // Watch for file access/reads
        _fileWatcher.Changed += OnFileChanged;
        _fileWatcher.Created += OnFileCreated;
        _fileWatcher.Deleted += OnFileDeleted;
        _fileWatcher.Renamed += OnFileRenamed;
        
        _fileWatcher.EnableRaisingEvents = true;
        
        System.Diagnostics.Debug.WriteLine("S3Cache - File watcher enabled");
        
        // Start polling timer to check for file access (every 500ms)
        // This handles cases where FileSystemWatcher doesn't detect reads
        _pollingTimer = new Timer(
            callback: _ => CheckForFileAccess(),
            state: null,
            dueTime: TimeSpan.FromSeconds(1),
            period: TimeSpan.FromMilliseconds(500));
    }
    
    private void CheckForFileAccess()
    {
        try
        {
            if (!_isActive) return;
            
            // Check all placeholder files for access attempts
            var files = Directory.GetFiles(_cacheRoot, "*", SearchOption.AllDirectories);
            
            foreach (var filePath in files)
            {
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    if (!fileInfo.Exists)
                        continue;
                    
                    // Only check placeholder files (Hidden + System, 0 bytes)
                    var attributes = File.GetAttributes(filePath);
                    if (!attributes.HasFlag(FileAttributes.Hidden) || !attributes.HasFlag(FileAttributes.System))
                        continue;
                    
                    if (fileInfo.Length != 0)
                        continue;
                    
                    // Get key
                    var relativePath = Path.GetRelativePath(_cacheRoot, filePath);
                    var key = relativePath.Replace('\\', '/');
                    
                    // Skip if already downloading
                    if (_downloadingFiles.Contains(key))
                        continue;
                    
                    // Try to open the file to see if someone is accessing it
                    // This will fail if file is locked by another process
                    try
                    {
                        using var testStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Write | FileShare.Delete, 1, FileOptions.Asynchronous);
                        // If we can open it, no one else is trying to access it
                    }
                    catch (IOException)
                    {
                        // File is locked - someone is trying to access it!
                        System.Diagnostics.Debug.WriteLine($"S3Cache - File access detected (locked): {fileInfo.Name}");
                        _ = Task.Run(async () => await DownloadFileOnDemandAsync(filePath, key));
                    }
                }
                catch
                {
                    // Skip files that can't be checked
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"S3Cache - Polling error: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Ensures a file is downloaded and ready to use. Call this before opening a file.
    /// </summary>
    public async Task<bool> EnsureFileAvailableAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return false;
            
            var attributes = File.GetAttributes(filePath);
            
            // If file is a placeholder (Hidden + System, 0 bytes), download it
            if (attributes.HasFlag(FileAttributes.Hidden) && attributes.HasFlag(FileAttributes.System))
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length == 0)
                {
                    var relativePath = Path.GetRelativePath(_cacheRoot, filePath);
                    var key = relativePath.Replace('\\', '/');
                    
                    System.Diagnostics.Debug.WriteLine($"S3Cache - Ensuring file available: {key}");
                    await DownloadFileOnDemandAsync(filePath, key);
                    
                    // Wait for download to complete
                    int retries = 100; // 10 seconds max
                    while (retries > 0)
                    {
                        if (!File.Exists(filePath))
                            return false;
                        
                        var newAttributes = File.GetAttributes(filePath);
                        var newFileInfo = new FileInfo(filePath);
                        
                        // Check if it's no longer a placeholder
                        if (!newAttributes.HasFlag(FileAttributes.Hidden) && newFileInfo.Length > 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"S3Cache - File is now available: {key}");
                            return true;
                        }
                        
                        await Task.Delay(100);
                        retries--;
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"S3Cache - Timeout waiting for file: {key}");
                    return false;
                }
            }
            
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"S3Cache - EnsureFileAvailable error: {ex.Message}");
            return false;
        }
    }

    private async void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Ignore directory changes
        if (Directory.Exists(e.FullPath))
            return;

        try
        {
            var relativePath = Path.GetRelativePath(_cacheRoot, e.FullPath);
            var key = relativePath.Replace('\\', '/');

            // Check if this is a write operation (file size increased or modified)
            if (File.Exists(e.FullPath))
            {
                var fileInfo = new FileInfo(e.FullPath);
                var attributes = File.GetAttributes(e.FullPath);

                // If file is a placeholder (Hidden+System, 0 bytes) and being accessed, download it
                if (attributes.HasFlag(FileAttributes.Hidden) && attributes.HasFlag(FileAttributes.System) && fileInfo.Length == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"S3Cache - Placeholder file accessed: {e.Name}, downloading...");
                    await DownloadFileOnDemandAsync(e.FullPath, key);
                }
                // If file is NOT a placeholder and has been modified, upload it
                else if (!attributes.HasFlag(FileAttributes.Hidden) && !attributes.HasFlag(FileAttributes.System) && fileInfo.Length > 0)
                {
                    // Check if this file was recently downloaded (skip upload in that case)
                    if (_activeFiles.TryGetValue(key, out var watcher) && 
                        DateTime.Now - watcher.LastAccess < TimeSpan.FromSeconds(5))
                    {
                        return; // Recent download, ignore
                    }

                    System.Diagnostics.Debug.WriteLine($"S3Cache - File modified: {e.Name}, uploading...");
                    await UploadFileAsync(e.FullPath, key);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"S3Cache - Error in file changed handler: {ex.Message}");
        }
    }

    private async void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        // Ignore directory creation
        if (Directory.Exists(e.FullPath))
            return;

        try
        {
            var relativePath = Path.GetRelativePath(_cacheRoot, e.FullPath);
            var key = relativePath.Replace('\\', '/');

            // Skip if this is a known S3 object (just created as placeholder)
            if (_fileMetadata.ContainsKey(key))
                return;

            // Wait a bit to ensure file is fully written
            await Task.Delay(1000);

            if (File.Exists(e.FullPath))
            {
                var fileInfo = new FileInfo(e.FullPath);
                if (fileInfo.Length > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"S3Cache - New file created: {e.Name}, uploading...");
                    await UploadFileAsync(e.FullPath, key);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"S3Cache - Error in file created handler: {ex.Message}");
        }
    }

    private async void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        try
        {
            var relativePath = Path.GetRelativePath(_cacheRoot, e.FullPath);
            var key = relativePath.Replace('\\', '/');

            System.Diagnostics.Debug.WriteLine($"S3Cache - File deleted locally: {e.Name}, deleting from S3...");
            
            var success = await _s3Service.DeleteObjectAsync(key);
            if (success)
            {
                _fileMetadata.TryRemove(key, out _);
                _activeFiles.TryRemove(key, out _);
                System.Diagnostics.Debug.WriteLine($"S3Cache - Deleted from S3: {key}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"S3Cache - Error in file deleted handler: {ex.Message}");
        }
    }

    private async void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        try
        {
            var oldRelativePath = Path.GetRelativePath(_cacheRoot, e.OldFullPath);
            var oldKey = oldRelativePath.Replace('\\', '/');
            
            var newRelativePath = Path.GetRelativePath(_cacheRoot, e.FullPath);
            var newKey = newRelativePath.Replace('\\', '/');

            System.Diagnostics.Debug.WriteLine($"S3Cache - File renamed: {e.OldName} -> {e.Name}");

            // Copy to new location in S3
            // Note: S3 doesn't have a native rename, so we need to copy then delete
            // For now, we'll treat it as a new file upload and delete the old one
            if (File.Exists(e.FullPath))
            {
                await UploadFileAsync(e.FullPath, newKey);
                await _s3Service.DeleteObjectAsync(oldKey);
                
                _fileMetadata.TryRemove(oldKey, out _);
                _activeFiles.TryRemove(oldKey, out _);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"S3Cache - Error in file renamed handler: {ex.Message}");
        }
    }

    private async Task DownloadFileOnDemandAsync(string localPath, string key)
    {
        // Prevent multiple simultaneous downloads of the same file
        lock (_downloadLock)
        {
            if (_downloadingFiles.Contains(key))
            {
                System.Diagnostics.Debug.WriteLine($"S3Cache - Already downloading: {key}");
                return;
            }
            _downloadingFiles.Add(key);
        }

        try
        {
            // Check if this is a large file that should use streaming
            if (_fileMetadata.TryGetValue(key, out var metadata) && 
                metadata.Size > STREAMING_THRESHOLD && 
                _streamingHandler != null)
            {
                System.Diagnostics.Debug.WriteLine($"S3Cache - Using streaming for large file: {key} ({metadata.Size / 1024 / 1024}MB)");
                
                // Fetch initial chunk (first 10MB) for quick access
                await _streamingHandler.FetchRangeAsync(key, 0, 10 * 1024 * 1024);
                
                // Prefetch next chunk in background
                _ = Task.Run(async () => await _streamingHandler.PrefetchNextChunkAsync(key, 0));
                
                // Remove placeholder attributes
                var attributes = File.GetAttributes(localPath);
                File.SetAttributes(localPath, FileAttributes.Normal);
                
                _activeFiles[key] = new FileWatcher
                {
                    LocalPath = localPath,
                    Key = key,
                    LastAccess = DateTime.Now,
                    Size = metadata.Size
                };
                
                return;
            }

            System.Diagnostics.Debug.WriteLine($"S3Cache - Downloading: {key}");

            // Download from S3
            using var s3Stream = await _s3Service.GetObjectStreamAsync(key);
            
            if (s3Stream == null)
            {
                System.Diagnostics.Debug.WriteLine($"S3Cache - Failed to get stream for: {key}");
                return;
            }

            // Write to temporary file first
            var tempFile = localPath + ".downloading";
            
            // Temporarily disable file watcher to prevent triggering during download
            if (_fileWatcher != null)
                _fileWatcher.EnableRaisingEvents = false;

            try
            {
                using (var fileStream = File.Create(tempFile))
                {
                    await s3Stream.CopyToAsync(fileStream);
                }

                // Replace placeholder with actual file
                if (File.Exists(localPath))
                    File.Delete(localPath);
                    
                File.Move(tempFile, localPath);

                // Remove placeholder attributes, set to normal
                File.SetAttributes(localPath, FileAttributes.Normal);

                // Track this file
                _activeFiles[key] = new FileWatcher
                {
                    LocalPath = localPath,
                    Key = key,
                    LastAccess = DateTime.Now,
                    Size = new FileInfo(localPath).Length
                };

                System.Diagnostics.Debug.WriteLine($"S3Cache - Download complete: {key}");
            }
            finally
            {
                // Clean up temp file if it exists
                if (File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch { }
                }
                
                // Re-enable file watcher
                if (_fileWatcher != null)
                    _fileWatcher.EnableRaisingEvents = true;
            }

            // Check if we need to clean up
            await Task.Run(() => CleanupCache());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"S3Cache - Download failed: {ex.Message}");
        }
        finally
        {
            lock (_downloadLock)
            {
                _downloadingFiles.Remove(key);
            }
        }
    }

    private async Task UploadFileAsync(string localPath, string key)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"S3Cache - Uploading: {key}");

            // Retry logic for file access (might be locked)
            int retries = 3;
            while (retries > 0)
            {
                try
                {
                    using var fileStream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var success = await _s3Service.PutObjectAsync(key, fileStream);

                    if (success)
                    {
                        var fileInfo = new FileInfo(localPath);
                        
                        // Update metadata
                        _fileMetadata[key] = new FileMetadata
                        {
                            Key = key,
                            Size = fileInfo.Length,
                            LastModified = fileInfo.LastWriteTime,
                            ETag = ""
                        };

                        System.Diagnostics.Debug.WriteLine($"S3Cache - Upload complete: {key}");
                    }
                    break;
                }
                catch (IOException)
                {
                    retries--;
                    if (retries > 0)
                        await Task.Delay(500);
                    else
                        throw;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"S3Cache - Upload failed: {ex.Message}");
        }
    }

    private void CleanupCache()
    {
        try
        {
            var cacheSize = GetCacheSize();
            System.Diagnostics.Debug.WriteLine($"S3Cache - Current cache size: {cacheSize / 1024 / 1024}MB");

            if (cacheSize > _maxCacheSize)
            {
                System.Diagnostics.Debug.WriteLine("S3Cache - Cache size exceeded, cleaning up...");
                
                // Get all downloaded files (not placeholders)
                var downloadedFiles = Directory.GetFiles(_cacheRoot, "*", SearchOption.AllDirectories)
                    .Select(f => new FileInfo(f))
                    .Where(f => {
                        var attributes = f.Attributes;
                        // Only include actual downloaded files
                        return f.Length > 0 && 
                               !attributes.HasFlag(FileAttributes.Hidden) &&
                               !attributes.HasFlag(FileAttributes.System);
                    })
                    .OrderBy(f => f.LastAccessTime) // Oldest first
                    .ToList();

                long freedSpace = 0;
                int filesConverted = 0;

                foreach (var file in downloadedFiles)
                {
                    // Get S3 metadata for this file
                    var relativePath = Path.GetRelativePath(_cacheRoot, file.FullName);
                    var key = relativePath.Replace('\\', '/');

                    // Convert back to placeholder
                    ConvertToPlaceholder(file.FullName, key);
                    
                    freedSpace += file.Length;
                    filesConverted++;
                    
                    // Remove from active tracking
                    _activeFiles.TryRemove(key, out _);

                    // Stop when we've freed enough space (keep at 80% capacity)
                    if (cacheSize - freedSpace <= _maxCacheSize * 0.8)
                    {
                        break;
                    }
                }

                System.Diagnostics.Debug.WriteLine($"S3Cache - Cleanup complete: {filesConverted} files converted to placeholders, freed {freedSpace / 1024 / 1024}MB");
            }

            // Also clean up files not accessed in last hour
            CleanupStaleFiles(TimeSpan.FromHours(1));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"S3Cache - Cleanup error: {ex.Message}");
        }
    }

    private void CleanupStaleFiles(TimeSpan maxAge)
    {
        try
        {
            var cutoff = DateTime.Now - maxAge;
            var staleFiles = Directory.GetFiles(_cacheRoot, "*", SearchOption.AllDirectories)
                .Select(f => new FileInfo(f))
                .Where(f => {
                    var attributes = f.Attributes;
                    // Only clean actual files, not placeholders
                    return f.Length > 0 && 
                           !attributes.HasFlag(FileAttributes.Hidden) &&
                           !attributes.HasFlag(FileAttributes.System) &&
                           f.LastAccessTime < cutoff;
                })
                .ToList();

            foreach (var file in staleFiles)
            {
                var relativePath = Path.GetRelativePath(_cacheRoot, file.FullName);
                var key = relativePath.Replace('\\', '/');
                
                ConvertToPlaceholder(file.FullName, key);
                _activeFiles.TryRemove(key, out _);
            }

            if (staleFiles.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"S3Cache - Cleaned up {staleFiles.Count} stale files");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"S3Cache - Stale file cleanup error: {ex.Message}");
        }
    }

    private void ConvertToPlaceholder(string filePath, string key)
    {
        try
        {
            // Get file metadata before deleting
            var fileInfo = new FileInfo(filePath);
            var lastModified = fileInfo.LastWriteTime;

            // Delete actual file
            File.Delete(filePath);

            // Create empty placeholder
            File.WriteAllBytes(filePath, Array.Empty<byte>());

            // Mark as placeholder with Hidden + System attributes
            File.SetAttributes(filePath, FileAttributes.Hidden | FileAttributes.System);
            File.SetLastWriteTime(filePath, lastModified);

            System.Diagnostics.Debug.WriteLine($"S3Cache - Converted to placeholder: {key}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"S3Cache - Failed to convert to placeholder: {ex.Message}");
        }
    }

    private long GetCacheSize()
    {
        try
        {
            return Directory.GetFiles(_cacheRoot, "*", SearchOption.AllDirectories)
                .Select(f => new FileInfo(f))
                .Where(f => {
                    // Exclude placeholder files (Hidden + System, 0 bytes)
                    var attributes = f.Attributes;
                    if (attributes.HasFlag(FileAttributes.Hidden) && attributes.HasFlag(FileAttributes.System))
                        return false;
                    return true;
                })
                .Sum(f => f.Length);
        }
        catch
        {
            return 0;
        }
    }

    public void PurgeAllCache()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("S3Cache - Purging all cached files");
            
            var files = Directory.GetFiles(_cacheRoot, "*", SearchOption.AllDirectories)
                .Where(f => {
                    var info = new FileInfo(f);
                    // Only purge actual downloaded files, not placeholders
                    var attributes = info.Attributes;
                    return info.Length > 0 && 
                           !attributes.HasFlag(FileAttributes.Hidden) && 
                           !attributes.HasFlag(FileAttributes.System);
                })
                .ToList();

            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(_cacheRoot, file);
                var key = relativePath.Replace('\\', '/');
                ConvertToPlaceholder(file, key);
            }

            _activeFiles.Clear();
            System.Diagnostics.Debug.WriteLine($"S3Cache - Purged {files.Count} files");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"S3Cache - Purge failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _isActive = false;
        _fileWatcher?.Dispose();
        _cleanupTimer?.Dispose();
        _pollingTimer?.Dispose();
        _streamingHandler?.Dispose();
        
        // Purge all cached files on unmount
        PurgeAllCache();
        
        System.Diagnostics.Debug.WriteLine("S3Cache - Disposed");
    }

    private class FileMetadata
    {
        public string Key { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime LastModified { get; set; }
        public string ETag { get; set; } = string.Empty;
    }

    private class FileWatcher
    {
        public string LocalPath { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public DateTime LastAccess { get; set; }
        public long Size { get; set; }
    }
}
