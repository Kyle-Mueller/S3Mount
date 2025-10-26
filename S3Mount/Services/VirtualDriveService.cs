using S3Mount.Models;
using System.IO;
using System.Runtime.InteropServices;

namespace S3Mount.Services;

/// <summary>
/// Service for managing virtual drive mounting using rclone
/// </summary>
public class VirtualDriveService
{
    private readonly RcloneService _rcloneService;
    private readonly CredentialManagerService _credentialManager;
    private readonly CredentialService _credentialService;
    private readonly LogService _log = LogService.Instance;
    private readonly Dictionary<Guid, DriveMapping> _activeMounts = new();
    
    public VirtualDriveService(CredentialService credentialService)
    {
        _rcloneService = new RcloneService();
        _credentialManager = new CredentialManagerService();
        _credentialService = credentialService;
        
        _log.Info("?? VirtualDriveService initialized");
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
            _log.Warning($"?? Mount already exists for {config.MountName}");
            return false;
        }
            
        try
        {
            _log.Info($"?? Starting mount process for {config.MountName}");
            
            // Check rclone availability
            if (!_rcloneService.IsRcloneAvailable())
            {
                _log.Error("? rclone.exe not found");
                return false;
            }
            
            // Find available drive letter
            var driveLetter = GetAvailableDriveLetter(config.DriveLetter);
            if (string.IsNullOrEmpty(driveLetter))
            {
                _log.Error("? No available drive letters");
                return false;
            }
            
            // Remove colon for remote name
            var remoteName = $"{config.MountName.Replace(" ", "_")}_{driveLetter.TrimEnd(':')}";
            
            // Store credentials in Windows Credential Manager
            _credentialManager.StoreCredentials(
                remoteName,
                config.AccessKey,
                config.SecretKey
            );
            
            // Configure rclone remote
            var configured = await _rcloneService.ConfigureRemoteAsync(
                remoteName,
                config.ProviderName,
                config.ServiceUrl,
                config.Region,
                config.AccessKey,
                config.SecretKey,
                config.ForcePathStyle
            );
            
            if (!configured)
            {
                _log.Error($"? Failed to configure remote for {config.MountName}");
                _credentialManager.DeleteCredentials(remoteName);
                return false;
            }
            
            // Test connection
            var connected = await _rcloneService.TestConnectionAsync(remoteName, config.BucketName);
            if (!connected)
            {
                _log.Error($"? Connection test failed for {config.MountName}");
                await _rcloneService.RemoveRemoteAsync(remoteName);
                _credentialManager.DeleteCredentials(remoteName);
                return false;
            }
            
            // Mount using rclone
            var mounted = await _rcloneService.MountAsync(remoteName, config.BucketName, driveLetter.TrimEnd(':'));
            
            if (mounted)
            {
                _activeMounts[config.Id] = new DriveMapping
                {
                    ConfigId = config.Id,
                    DriveLetter = driveLetter,
                    MountName = config.MountName,
                    RemoteName = remoteName
                };
                
                // Update configuration
                config.IsMounted = true;
                config.DriveLetter = driveLetter;
                _credentialService.SaveConfiguration(config);
                
                // Set drive label
                SetDriveLabel(driveLetter, config.MountName);
                
                _log.Success($"? Successfully mounted {config.MountName} to {driveLetter}");
                return true;
            }
            else
            {
                _log.Error($"? Failed to mount {config.MountName}");
                await _rcloneService.RemoveRemoteAsync(remoteName);
                _credentialManager.DeleteCredentials(remoteName);
                return false;
            }
        }
        catch (Exception ex)
        {
            _log.Error($"? Mount failed: {ex.Message}");
            return false;
        }
    }
    
    public async Task<bool> UnmountDrive(Guid configId)
    {
        _log.Info($"?? UnmountDrive called for config ID: {configId}");
        
        if (!_activeMounts.TryGetValue(configId, out var mapping))
        {
            _log.Warning($"?? No active mount found for config ID: {configId}");
            
            // Check if config exists and update status
            var config = _credentialService.GetConfiguration(configId);
            if (config != null)
            {
                config.IsMounted = false;
                _credentialService.SaveConfiguration(config);
            }
            
            return false;
        }
            
        try
        {
            _log.Info($"?? Unmounting {mapping.MountName} from {mapping.DriveLetter}");
            
            // Unmount using rclone
            await _rcloneService.UnmountAsync(mapping.RemoteName, mapping.DriveLetter.TrimEnd(':'));
            
            // Remove remote configuration
            await _rcloneService.RemoveRemoteAsync(mapping.RemoteName);
            
            // Delete credentials
            _credentialManager.DeleteCredentials(mapping.RemoteName);
            
            _activeMounts.Remove(configId);
            
            // Update configuration
            var config = _credentialService.GetConfiguration(configId);
            if (config != null)
            {
                config.IsMounted = false;
                _credentialService.SaveConfiguration(config);
            }
            
            _log.Success($"? Successfully unmounted {mapping.MountName}");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error($"? Unmount failed: {ex.Message}");
            return false;
        }
    }
    
    public async Task UnmountAllDrives()
    {
        _log.Info("?? Unmounting all drives");
        var configIds = _activeMounts.Keys.ToList();
        
        foreach (var id in configIds)
        {
            await UnmountDrive(id);
        }
        
        // Also cleanup any stray rclone mounts
        await _rcloneService.CleanupAllMountsAsync();
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
    
    private void SetDriveLabel(string driveLetter, string label)
    {
        try
        {
            var letter = driveLetter.TrimEnd(':');
            
            _log.Debug($"??? Setting drive label for {letter}: to '{label}'");
            
            // Set volume label using registry
            var keyPath = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\DriveIcons\{letter}\DefaultLabel";
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(keyPath);
            if (key != null)
            {
                key.SetValue("", label);
            }
            
            // Refresh Explorer
            RefreshExplorer();
        }
        catch (Exception ex)
        {
            _log.Warning($"?? SetDriveLabel failed: {ex.Message}");
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
        public string RemoteName { get; set; } = string.Empty;
    }
}
