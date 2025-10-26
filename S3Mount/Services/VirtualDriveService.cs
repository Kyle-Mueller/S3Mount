using S3Mount.Models;
using System.IO;
using System.Runtime.InteropServices;

namespace S3Mount.Services;

public class VirtualDriveService
{
    private readonly S3Service _s3Service;
    private readonly CredentialService _credentialService;
    private readonly Dictionary<Guid, DriveMapping> _activeMounts = new();
    
    public VirtualDriveService(S3Service s3Service, CredentialService credentialService)
    {
        _s3Service = s3Service;
        _credentialService = credentialService;
    }
    
    public List<string> GetAvailableDriveLetters()
    {
        var availableLetters = new List<string>();
        var usedDrives = DriveInfo.GetDrives().Select(d => d.Name[0]).ToHashSet();
        
        // Start from Z and go backwards (convention for network/virtual drives)
        for (char c = 'Z'; c >= 'C'; c--) // Skip A and B (floppy drives)
        {
            if (!usedDrives.Contains(c))
            {
                availableLetters.Add($"{c}:");
            }
        }
        
        return availableLetters;
    }
    
    public async Task<bool> MountDriveAsync(S3MountConfiguration config)
    {
        if (_activeMounts.ContainsKey(config.Id))
        {
            System.Diagnostics.Debug.WriteLine($"Mount already exists for {config.MountName}");
            return false;
        }
            
        try
        {
            // Initialize S3 connection
            var s3Service = new S3Service();
            s3Service.Initialize(config);
            
            // Test connection
            if (!await s3Service.TestConnectionAsync())
            {
                System.Diagnostics.Debug.WriteLine($"S3 connection test failed for {config.MountName}");
                return false;
            }
            
            // Find available drive letter
            var driveLetter = GetAvailableDriveLetter(config.DriveLetter);
            if (string.IsNullOrEmpty(driveLetter))
            {
                System.Diagnostics.Debug.WriteLine("No available drive letters");
                return false;
            }
            
            System.Diagnostics.Debug.WriteLine($"Mounting {config.MountName} to {driveLetter} using Smart Cache");
            
            // Create cache root directory
            var cacheRoot = Path.Combine(Path.GetTempPath(), "S3Mount_Cache", config.BucketName + "_" + config.Id.ToString("N").Substring(0, 8));
            Directory.CreateDirectory(cacheRoot);
            
            System.Diagnostics.Debug.WriteLine($"Cache root: {cacheRoot}");
            
            // Create and initialize smart cache
            var smartCache = new S3SmartCache(s3Service, config, cacheRoot);
            
            if (!await smartCache.InitializeCacheAsync())
            {
                System.Diagnostics.Debug.WriteLine("Failed to initialize smart cache");
                return false;
            }
            
            // Create subst drive pointing to cache root
            var result = MapNetworkDrive(driveLetter, config.MountName, cacheRoot);
            
            if (result)
            {
                _activeMounts[config.Id] = new DriveMapping
                {
                    ConfigId = config.Id,
                    DriveLetter = driveLetter,
                    MountName = config.MountName,
                    CacheRoot = cacheRoot,
                    SmartCache = smartCache,
                    S3Service = s3Service
                };
                
                // Update configuration
                config.IsMounted = true;
                config.DriveLetter = driveLetter;
                _credentialService.SaveConfiguration(config);
                
                // Set drive label to mount name
                SetDriveLabel(driveLetter, config.MountName);
                
                // Set custom icon if specified
                if (!string.IsNullOrEmpty(config.IconPath) && File.Exists(config.IconPath))
                {
                    SetDriveIcon(driveLetter, config.IconPath);
                }
                
                System.Diagnostics.Debug.WriteLine($"Successfully mounted {config.MountName} to {driveLetter} with Smart Cache");
            }
            else
            {
                // Failed to create drive, dispose cache
                smartCache.Dispose();
                System.Diagnostics.Debug.WriteLine($"Failed to create drive mapping for {config.MountName}");
            }
            
            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Mount failed: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }
    
    public bool UnmountDrive(Guid configId)
    {
        System.Diagnostics.Debug.WriteLine($"UnmountDrive called for config ID: {configId}");
        
        if (!_activeMounts.TryGetValue(configId, out var mapping))
        {
            System.Diagnostics.Debug.WriteLine($"No active mount found for config ID: {configId}");
            
            // Check if config exists and try to unmount anyway
            var config = _credentialService.GetConfiguration(configId);
            if (config != null && !string.IsNullOrEmpty(config.DriveLetter))
            {
                System.Diagnostics.Debug.WriteLine($"Attempting to unmount drive {config.DriveLetter} anyway");
                DisconnectNetworkDrive(config.DriveLetter);
                config.IsMounted = false;
                _credentialService.SaveConfiguration(config);
                return true;
            }
            
            return false;
        }
            
        try
        {
            System.Diagnostics.Debug.WriteLine($"Unmounting {mapping.MountName} from {mapping.DriveLetter}");
            
            // Disconnect network drive first
            DisconnectNetworkDrive(mapping.DriveLetter);
            
            // Dispose smart cache (this purges all cached files)
            if (mapping.SmartCache != null)
            {
                System.Diagnostics.Debug.WriteLine("Disposing smart cache and purging files");
                mapping.SmartCache.Dispose();
            }
            
            // Dispose S3 service
            mapping.S3Service?.Dispose();
            
            // Clean up cache directory
            if (!string.IsNullOrEmpty(mapping.CacheRoot) && Directory.Exists(mapping.CacheRoot))
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"Deleting cache directory: {mapping.CacheRoot}");
                    Directory.Delete(mapping.CacheRoot, true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to delete cache directory: {ex.Message}");
                }
            }
            
            _activeMounts.Remove(configId);
            
            // Update configuration
            var config = _credentialService.GetConfiguration(configId);
            if (config != null)
            {
                config.IsMounted = false;
                _credentialService.SaveConfiguration(config);
                System.Diagnostics.Debug.WriteLine($"Successfully unmounted {mapping.MountName}");
            }
            
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Unmount failed: {ex.Message}");
            return false;
        }
    }
    
    public void UnmountAllDrives()
    {
        System.Diagnostics.Debug.WriteLine("Unmounting all drives");
        var configIds = _activeMounts.Keys.ToList();
        foreach (var id in configIds)
        {
            UnmountDrive(id);
        }
    }
    
    private string GetAvailableDriveLetter(string preferred)
    {
        // Check if preferred letter is available
        if (!string.IsNullOrEmpty(preferred))
        {
            // Ensure it has colon
            if (!preferred.EndsWith(":"))
                preferred = preferred + ":";
                
            if (IsDriveLetterAvailable(preferred))
                return preferred;
        }
            
        // Find first available drive letter starting from Z
        for (char c = 'Z'; c >= 'C'; c--)
        {
            var letter = c + ":";
            if (IsDriveLetterAvailable(letter))
                return letter;
        }
        
        return string.Empty;
    }
    
    private bool IsDriveLetterAvailable(string driveLetter)
    {
        if (string.IsNullOrEmpty(driveLetter))
            return false;
            
        // Ensure format is correct (e.g., "Z:")
        if (!driveLetter.EndsWith(":"))
            driveLetter = driveLetter + ":";
            
        var drives = DriveInfo.GetDrives();
        return !drives.Any(d => d.Name.StartsWith(driveLetter, StringComparison.OrdinalIgnoreCase));
    }
    
    private bool MapNetworkDrive(string driveLetter, string mountName, string targetPath)
    {
        try
        {
            // Ensure drive letter format
            if (!driveLetter.EndsWith(":"))
                driveLetter = driveLetter + ":";
            
            System.Diagnostics.Debug.WriteLine($"Creating subst drive: {driveLetter} -> {targetPath}");
            
            // Create subst drive
            var substCommand = $"subst {driveLetter} \"{targetPath}\"";
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {substCommand}",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            
            using var process = System.Diagnostics.Process.Start(processInfo);
            if (process != null)
            {
                process.WaitForExit();
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                
                if (!string.IsNullOrEmpty(output))
                    System.Diagnostics.Debug.WriteLine($"Subst output: {output}");
                if (!string.IsNullOrEmpty(error))
                    System.Diagnostics.Debug.WriteLine($"Subst error: {error}");
            }
            
            // Verify the drive was created
            System.Threading.Thread.Sleep(500);
            var wasCreated = IsDriveLetterAvailable(driveLetter) == false;
            System.Diagnostics.Debug.WriteLine($"Drive {driveLetter} created: {wasCreated}");
            return wasCreated;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MapNetworkDrive failed: {ex.Message}");
            return false;
        }
    }
    
    private bool DisconnectNetworkDrive(string driveLetter)
    {
        try
        {
            // Ensure drive letter format
            if (!driveLetter.EndsWith(":"))
                driveLetter = driveLetter + ":";
            
            System.Diagnostics.Debug.WriteLine($"Removing subst drive: {driveLetter}");
                
            // Remove subst drive
            var substCommand = $"subst {driveLetter} /D";
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {substCommand}",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            
            using var process = System.Diagnostics.Process.Start(processInfo);
            if (process != null)
            {
                process.WaitForExit();
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                
                if (!string.IsNullOrEmpty(output))
                    System.Diagnostics.Debug.WriteLine($"Subst /D output: {output}");
                if (!string.IsNullOrEmpty(error))
                    System.Diagnostics.Debug.WriteLine($"Subst /D error: {error}");
            }
            
            // Verify drive was removed
            System.Threading.Thread.Sleep(500);
            var wasRemoved = IsDriveLetterAvailable(driveLetter);
            System.Diagnostics.Debug.WriteLine($"Drive {driveLetter} removed: {wasRemoved}");
            
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DisconnectNetworkDrive failed: {ex.Message}");
            return false;
        }
    }
    
    private void SetDriveLabel(string driveLetter, string label)
    {
        try
        {
            // Ensure drive letter format
            var letter = driveLetter.TrimEnd(':');
            
            System.Diagnostics.Debug.WriteLine($"Setting drive label for {letter}: to '{label}'");
            
            // Set volume label using registry
            var keyPath = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\DriveIcons\{letter}\DefaultLabel";
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(keyPath);
            if (key != null)
            {
                key.SetValue("", label);
                System.Diagnostics.Debug.WriteLine($"Drive label set successfully");
            }
            
            // Also try using label command (works better for subst drives)
            var labelCommand = $"label {letter}: \"{label}\"";
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {labelCommand}",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            
            using var process = System.Diagnostics.Process.Start(processInfo);
            process?.WaitForExit();
            
            // Refresh Explorer to show new label
            RefreshExplorer();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SetDriveLabel failed: {ex.Message}");
        }
    }
    
    private void SetDriveIcon(string driveLetter, string iconPath)
    {
        try
        {
            var letter = driveLetter.TrimEnd(':');
            var keyPath = $@"Software\Classes\Applications\Explorer.exe\Drives\{letter}\DefaultIcon";
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(keyPath);
            key?.SetValue("", iconPath);
            
            RefreshExplorer();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SetDriveIcon failed: {ex.Message}");
        }
    }
    
    private void RefreshExplorer()
    {
        try
        {
            // Notify Windows that drive list has changed
            SHChangeNotify(0x8000000, 0x1000, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            // Silently ignore if refresh fails
        }
    }
    
    [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
    
    private class DriveMapping
    {
        public Guid ConfigId { get; set; }
        public string DriveLetter { get; set; } = string.Empty;
        public string MountName { get; set; } = string.Empty;
        public string CacheRoot { get; set; } = string.Empty;
        public S3SmartCache? SmartCache { get; set; }
        public S3Service? S3Service { get; set; }
    }
}
