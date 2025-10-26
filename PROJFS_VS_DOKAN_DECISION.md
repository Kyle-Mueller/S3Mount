# Windows ProjFS Implementation for S3 Virtual File System

## Current Status

I've installed the `Microsoft.Windows.ProjFS` NuGet package, but the implementation requires significant work due to:

1. **API Complexity**: ProjFS requires precise implementation of multiple callback interfaces
2. **Version Differences**: The API has changed between versions
3. **Testing Requirements**: Requires Windows 10 1809+ with ProjFS feature enabled

## What ProjFS Implementation Requires

### 1. Enable ProjFS on Windows

```powershell
# Run as Administrator
Enable-WindowsOptionalFeature -Online -FeatureName Client-ProjFS -NoRestart
```

### 2. Core Implementation Steps

```csharp
// 1. Mark directory as virtualization root
VirtualizationInstance.MarkDirectoryAsVirtualizationRoot(path);

// 2. Implement IRequiredCallbacks
- StartDirectoryEnumerationCallback
- GetDirectoryEnumerationCallback  
- EndDirectoryEnumerationCallback
- GetPlaceholderInfoCallback
- GetFileDataCallback
- QueryFileNameCallback

// 3. Start virtualization instance
var instance = new VirtualizationInstance(path, ...);
instance.StartVirtualizing(callbacksInstance);
```

### 3. Callback Flow

```
User opens Z:\ in Explorer
    ?
StartDirectoryEnumerationCallback()
    ? List S3 objects
GetDirectoryEnumerationCallback()
    ? Return file/folder list
EndDirectoryEnumerationCallback()
    ?
Files appear in Explorer (as placeholders)

User opens a file
    ?
GetPlaceholderInfoCallback()
    ? Get S3 metadata
GetFileDataCallback()
    ? Stream from S3
User sees file content
```

## Alternative: Use Dokan Instead

Given the complexity of ProjFS, **I recommend using Dokan** instead. It's:
- ? More mature and stable
- ? Better documentation
- ? Easier to implement
- ? Works on older Windows versions
- ?? Requires Dokan driver installation

### Dokan Implementation (Much Simpler)

```csharp
// Install-Package DokanNet

public class S3FileSystem : IDokanOperations
{
    private S3Service _s3Service;
    
    public NtStatus FindFilesWithPattern(
        string fileName,
        string searchPattern,
        out IList<FileInformation> files,
        IDokanFileInfo info)
    {
        // List S3 objects
        var objects = await _s3Service.ListObjectsAsync(fileName);
        
        files = objects.Select(obj => new FileInformation
        {
            FileName = Path.GetFileName(obj.Key),
            Length = obj.Size,
            LastWriteTime = obj.LastModified,
            Attributes = FileAttributes.Normal
        }).ToList();
        
        return DokanResult.Success;
    }
    
    public NtStatus ReadFile(
        string fileName,
        byte[] buffer,
        out int bytesRead,
        long offset,
        IDokanFileInfo info)
    {
        // Download from S3 on-demand
        using var stream = await _s3Service.GetObjectRangeAsync(
            fileName, 
            offset, 
            offset + buffer.Length);
            
        bytesRead = stream.Read(buffer, 0, buffer.Length);
        return DokanResult.Success;
    }
}

// Mount the drive
var dokan = new Dokan(new S3FileSystem());
dokan.Mount("Z:\\", DokanOptions.DebugMode);
```

## Recommended Path Forward

### Option 1: Quick Win with Dokan (2-3 hours)
1. Install Dokan driver: https://github.com/dokan-dev/dokany/releases
2. Install NuGet: `dotnet add package DokanNet`
3. Implement `IDokanOperations` interface
4. Test and deploy

**Pros:**
- ? Straightforward API
- ? Well-documented
- ? Many examples available
- ? Works with older Windows

**Cons:**
- ? Requires separate driver installation
- ? User needs admin rights to install Dokan

### Option 2: Complete ProjFS Implementation (1-2 days)
1. Debug and fix all API compatibility issues
2. Handle edge cases (empty directories, large files, etc.)
3. Implement proper async/await patterns
4. Test thoroughly on Windows 10+

**Pros:**
- ? Native Windows feature
- ? No driver installation needed
- ? Better performance potential

**Cons:**
- ? Complex implementation
- ? Windows 10 1809+ required
- ? Limited documentation
- ? Harder to debug

### Option 3: Hybrid Approach (Recommended)
1. **Now**: Use sync workaround for small buckets
2. **Later**: Implement Dokan for full solution
3. **Future**: Add ProjFS as optional feature

## Current Working State

Right now, the app:
- ? Creates virtual drives
- ? Sets drive labels
- ? Mounts/unmounts properly
- ? Shows empty folders (needs VFS implementation)

## Next Steps

**Which approach would you like to pursue?**

1. **Dokan (fastest to working solution)**
   - I'll install DokanNet
   - Implement the 6 core methods
   - You get working file access in ~2 hours

2. **ProjFS (Windows-native but complex)**
   - I'll fix all the API compatibility issues
   - Complete full implementation
   - Takes longer but no driver needed

3. **Sync workaround (temporary)**
   - Downloads all files on mount
   - Works immediately
   - Limited to small buckets

**Please let me know which path you'd like to take!**
