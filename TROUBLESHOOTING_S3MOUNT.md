# Troubleshooting Guide - S3Mount File Issues

## Quick Diagnostics

### Check Debug Output
Open Visual Studio ? Debug ? Windows ? Output ? Select "Debug" from dropdown

Look for these messages:
```
S3Cache - Initializing cache at [path]
S3Cache - Found [N] S3 objects
S3Cache - Created [N] placeholder files
S3Cache - File watcher enabled
```

---

## Common Issues & Solutions

### Issue: Files still show 0 bytes

**Diagnosis**:
```
Look for: "Failed to create sparse file" in debug output
```

**Solutions**:
1. Check if cache directory has write permissions
2. File system must be NTFS (not FAT32) for sparse files
3. Try running as Administrator

**Workaround**:
If sparse files fail, the code automatically falls back to empty placeholder files. Files will still download correctly, but won't show size until downloaded.

---

### Issue: Files still appear corrupt when opened

**Diagnosis**:
```
S3Cache - Placeholder file accessed: [filename], downloading...
S3Cache - Download complete: [filename]
```
If you DON'T see these messages, FileSystemWatcher may not be triggering.

**Solutions**:
1. **Antivirus interference**: Some antivirus software blocks FileSystemWatcher
   - Add cache directory to exclusions: `%TEMP%\S3Mount_Cache\`
   
2. **File locked during download**:
   - Close any programs that might have the file open
   - Wait for download to complete before opening

3. **Network issue during download**:
   ```
   S3Cache - Download failed: [error]
   ```
   - Check internet connection
   - Verify S3 credentials still valid
   - Check Backblaze B2 account status

---

### Issue: Large files (60GB MKV) won't play or are stuttering

**Diagnosis**:
```
S3Cache - Using streaming for large file: [filename] ([size]MB)
S3Cache - Fetching range for [key]: [start]-[end]
```

**Solutions**:

1. **Slow internet connection**:
   - Reduce chunk size for slower connections:
   ```csharp
   // In S3StreamingFileHandler.cs
   private const long CHUNK_SIZE = 5 * 1024 * 1024; // 5MB instead of 10MB
   ```

2. **Video player buffering issues**:
   - VLC works best with streaming files
   - Windows Media Player may have issues
   - Try MPV player or PotPlayer for better buffering

3. **Increase prefetch**:
   ```csharp
   // In S3SmartCache.cs, DownloadFileOnDemandAsync method
   // Fetch initial 20MB instead of 10MB
   await _streamingHandler.FetchRangeAsync(key, 0, 20 * 1024 * 1024);
   ```

---

### Issue: Created files don't appear in Backblaze B2

**Diagnosis**:
```
S3Cache - New file created: [filename], uploading...
S3Cache - Upload complete: [filename]
```

**Solutions**:

1. **File created too quickly**:
   - The code waits 1 second after creation
   - For very large files, increase delay:
   ```csharp
   // In OnFileCreated method
   await Task.Delay(3000); // 3 seconds instead of 1
   ```

2. **Check debug output for errors**:
   ```
   S3Cache - Upload failed: [error message]
   ```
   - Verify write permissions in B2
   - Check S3 credentials have PUT permissions

3. **File might be locked**:
   - Ensure you close the file before expecting upload
   - Code retries 3 times, but very slow writes might timeout

---

### Issue: High disk space usage (cache not cleaning up)

**Diagnosis**:
```
S3Cache - Current cache size: [size]MB
S3Cache - Cleanup complete: [N] files converted to placeholders
```

**Solutions**:

1. **Reduce cache size limit**:
   ```csharp
   // In S3SmartCache.cs constructor
   private readonly long _maxCacheSize = 1L * 1024 * 1024 * 1024; // 1GB instead of 2GB
   ```

2. **More aggressive cleanup**:
   ```csharp
   // Clean up files older than 30 minutes instead of 1 hour
   CleanupStaleFiles(TimeSpan.FromMinutes(30));
   ```

3. **Force manual cleanup**:
   - Unmount and remount the drive
   - Cache is purged on unmount

---

### Issue: FileSystemWatcher not detecting file changes

**Diagnosis**:
No messages appearing when you create/modify files.

**Solutions**:

1. **Windows limitation**: FileSystemWatcher has limits on number of watched files
   - Limit: ~8,000 directories
   - If you have many folders, might hit limit

2. **Buffer overflow**:
   ```csharp
   // In SetupFileWatcher method, add:
   _fileWatcher.InternalBufferSize = 64 * 1024; // 64KB buffer
   ```

3. **Network drive issue**:
   - SUBST drives should work fine
   - If issues persist, try using different drive letter

---

### Issue: Drive won't mount

**Diagnosis**:
```
S3Cache - Initialization failed: [error]
```

**Solutions**:

1. **Cache directory creation failed**:
   - Check permissions on `%TEMP%` directory
   - Try running as Administrator

2. **S3 connection failed**:
   ```
   S3 connection test failed for [mount name]
   ```
   - Verify credentials (Access Key, Secret Key)
   - Check ServiceURL for Backblaze: `https://s3.us-west-004.backblazeb2.com`
   - Ensure Region matches: `us-west-004` (or your region)
   - Check `ForcePathStyle = true` is set

3. **Drive letter in use**:
   - Choose different drive letter
   - Code should auto-select available letter

---

## Performance Tuning

### For Slow Internet (< 10 Mbps):
```csharp
// Smaller chunks for better responsiveness
private const long CHUNK_SIZE = 2 * 1024 * 1024; // 2MB chunks
private const long STREAMING_THRESHOLD = 50 * 1024 * 1024; // Stream files > 50MB
```

### For Fast Internet (> 100 Mbps):
```csharp
// Larger chunks for better throughput
private const long CHUNK_SIZE = 50 * 1024 * 1024; // 50MB chunks
private const long STREAMING_THRESHOLD = 500 * 1024 * 1024; // Stream files > 500MB
```

### For Limited Disk Space:
```csharp
// Smaller cache
private readonly long _maxCacheSize = 500L * 1024 * 1024; // 500MB cache
CleanupStaleFiles(TimeSpan.FromMinutes(15)); // 15-minute timeout
```

### For Lots of Disk Space:
```csharp
// Larger cache for better performance
private readonly long _maxCacheSize = 10L * 1024 * 1024 * 1024; // 10GB cache
CleanupStaleFiles(TimeSpan.FromHours(24)); // 24-hour timeout
```

---

## Debugging Steps

### 1. Test S3 Connection
```csharp
// In your code, add test:
var testSuccess = await _s3Service.TestConnectionAsync();
System.Diagnostics.Debug.WriteLine($"S3 Connection Test: {testSuccess}");
```

### 2. Test File Listing
```csharp
var objects = await _s3Service.ListObjectsAsync("");
System.Diagnostics.Debug.WriteLine($"Found {objects.Count} objects");
foreach (var obj in objects.Take(10))
{
    System.Diagnostics.Debug.WriteLine($"  {obj.Key} - {obj.Size} bytes");
}
```

### 3. Test File Download
```csharp
using var stream = await _s3Service.GetObjectStreamAsync("test-file.txt");
if (stream != null)
{
    var reader = new StreamReader(stream);
    var content = await reader.ReadToEndAsync();
    System.Diagnostics.Debug.WriteLine($"Downloaded: {content}");
}
```

### 4. Test Sparse File Creation
```csharp
var testPath = Path.Combine(Path.GetTempPath(), "sparse-test.bin");
CreateSparseFile(testPath, 1024 * 1024 * 1024); // 1GB
var info = new FileInfo(testPath);
System.Diagnostics.Debug.WriteLine($"File size: {info.Length} bytes");
System.Diagnostics.Debug.WriteLine($"Is sparse: {info.Attributes.HasFlag(FileAttributes.SparseFile)}");
```

---

## Backblaze B2 Specific Checks

### Verify Settings:
```csharp
Name: "Backblaze B2"
ServiceUrl: "https://s3.us-west-004.backblazeb2.com"  // Or your region
Region: "us-west-004"  // Or your region
ForcePathStyle: true  // REQUIRED for B2
```

### Get B2 Region from URL:
1. Log into Backblaze B2
2. Go to Browse Files
3. Click on your bucket
4. Look at the URL: `s3.us-west-004.backblazeb2.com`
   - Region = `us-west-004`

### Generate Application Key:
1. B2 Dashboard ? App Keys ? Add New Application Key
2. **Capabilities**: Read, Write, Delete (all required)
3. **Bucket**: Select your bucket
4. Copy the Application Key ID (Access Key)
5. Copy the Application Key (Secret Key) - **only shown once!**

---

## Clean Reinstall Process

If all else fails:

1. **Unmount all drives**
2. **Delete cache**:
   ```
   %TEMP%\S3Mount_Cache\
   ```
3. **Clear registry** (if you set drive labels):
   ```
   HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\DriveIcons
   ```
4. **Restart application**
5. **Remount with fresh settings**

---

## Getting Help

Include this info when reporting issues:

1. **Debug output** from VS Output window
2. **S3 provider** (Backblaze B2, AWS, etc.)
3. **File size** causing issues
4. **Operation** that failed (mount, download, upload, etc.)
5. **Error messages** from debug output
6. **Windows version**
7. **File system type** (NTFS, FAT32, etc.)
