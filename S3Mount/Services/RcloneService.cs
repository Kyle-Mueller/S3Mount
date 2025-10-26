using System.Diagnostics;
using System.IO;
using System.Text;
using System.Security;
using System.Runtime.InteropServices;

namespace S3Mount.Services;

/// <summary>
/// Service for managing rclone.exe operations
/// Handles mounting, unmounting, and configuration of S3 buckets via rclone
/// </summary>
public class RcloneService
{
    private readonly LogService _log = LogService.Instance;
    private readonly string _rcloneExePath;
    private readonly string _rcloneConfigPath;
    private readonly Dictionary<string, Process> _activeProcesses = new();

    public RcloneService()
    {
        // Look for rclone.exe in application directory
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        _rcloneExePath = Path.Combine(appDir, "rclone.exe");

        // Store rclone config in AppData
        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "S3Mount"
        );
        Directory.CreateDirectory(configDir);
        _rcloneConfigPath = Path.Combine(configDir, "rclone.conf");

        _log.Info($"?? RcloneService initialized - Config: {_rcloneConfigPath}");
    }

    /// <summary>
    /// Check if rclone.exe exists
    /// </summary>
    public bool IsRcloneAvailable()
    {
        var exists = File.Exists(_rcloneExePath);
        if (!exists)
        {
            _log.Error($"? rclone.exe not found at {_rcloneExePath}");
        }
        return exists;
    }

    /// <summary>
    /// Get rclone version
    /// </summary>
    public async Task<string?> GetRcloneVersionAsync()
    {
        try
        {
            var result = await RunRcloneCommandAsync("version", "--version");
            return result?.Split('\n').FirstOrDefault();
        }
        catch (Exception ex)
        {
            _log.Error($"? Failed to get rclone version: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Configure a remote in rclone
    /// </summary>
    public async Task<bool> ConfigureRemoteAsync(
        string remoteName,
        string provider,
        string endpoint,
        string region,
        string accessKey,
        string secretKey,
        bool forcePathStyle)
    {
        try
        {
            _log.Info($"?? Configuring rclone remote: {remoteName}");

            // Build rclone config content
            var configContent = new StringBuilder();
            configContent.AppendLine($"[{remoteName}]");
            configContent.AppendLine($"type = s3");
            configContent.AppendLine($"provider = {GetRcloneProvider(provider)}");
            configContent.AppendLine($"access_key_id = {accessKey}");
            configContent.AppendLine($"secret_access_key = {secretKey}");
            configContent.AppendLine($"endpoint = {endpoint}");
            configContent.AppendLine($"region = {region}");
            
            if (forcePathStyle)
            {
                configContent.AppendLine("force_path_style = true");
            }

            configContent.AppendLine("acl = private");
            configContent.AppendLine();

            // Read existing config
            var existingConfig = File.Exists(_rcloneConfigPath) 
                ? await File.ReadAllTextAsync(_rcloneConfigPath) 
                : string.Empty;

            // Remove existing remote with same name
            existingConfig = RemoveRemoteFromConfig(existingConfig, remoteName);

            // Add new remote config
            var newConfig = existingConfig + configContent.ToString();

            // Write config
            await File.WriteAllTextAsync(_rcloneConfigPath, newConfig);

            _log.Success($"? Remote configured: {remoteName}");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error($"? Failed to configure remote: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Mount an S3 bucket using rclone
    /// </summary>
    public async Task<bool> MountAsync(
        string remoteName,
        string bucketName,
        string driveLetter)
    {
        try
        {
            var mountPoint = $"{driveLetter}:";
            var remotePath = $"{remoteName}:{bucketName}";

            _log.Info($"?? Mounting {remotePath} to {mountPoint}");

            // Build rclone mount command
            var args = new List<string>
            {
                "mount",
                remotePath,
                mountPoint,
                $"--config={_rcloneConfigPath}",
                "--vfs-cache-mode=writes", // Cache writes for better performance
                "--vfs-cache-max-age=1h",  // Keep cache for 1 hour
                "--vfs-read-chunk-size=128M", // Large read chunks for streaming
                "--buffer-size=16M",       // Memory buffer
                "--dir-cache-time=5m",     // Cache directory listings
                "--poll-interval=15s",     // Check for changes every 15s
                "--no-console",            // No console window
                "--log-level=INFO"         // Logging level
            };

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _rcloneExePath,
                    Arguments = string.Join(" ", args),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                }
            };

            // Handle output
            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    _log.Debug($"[rclone] {e.Data}");
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    _log.Warning($"[rclone] {e.Data}");
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Store process
            _activeProcesses[remoteName] = process;

            // Wait a bit for mount to initialize
            await Task.Delay(2000);

            // Check if mount succeeded
            if (!Directory.Exists(mountPoint))
            {
                _log.Error($"? Mount failed - drive {mountPoint} not accessible");
                return false;
            }

            _log.Success($"? Mounted {remotePath} to {mountPoint}");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error($"? Mount failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Unmount a drive
    /// </summary>
    public async Task<bool> UnmountAsync(string remoteName, string driveLetter)
    {
        try
        {
            var mountPoint = $"{driveLetter}:";
            _log.Info($"?? Unmounting {mountPoint}");

            // Kill the rclone process for this remote
            if (_activeProcesses.TryGetValue(remoteName, out var process))
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync();
                    }
                    
                    _activeProcesses.Remove(remoteName);
                }
                catch (Exception ex)
                {
                    _log.Warning($"?? Error killing rclone process: {ex.Message}");
                }
            }

            // Also try using rclone umount command
            try
            {
                await RunRcloneCommandAsync("mount", "umount", mountPoint);
            }
            catch
            {
                // Ignore errors - process might already be gone
            }

            _log.Success($"? Unmounted {mountPoint}");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error($"? Unmount failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Test connection to a remote
    /// </summary>
    public async Task<bool> TestConnectionAsync(string remoteName, string bucketName)
    {
        try
        {
            _log.Info($"?? Testing connection to {remoteName}:{bucketName}");

            var remotePath = $"{remoteName}:{bucketName}";
            var result = await RunRcloneCommandAsync(
                "lsd",
                remotePath,
                $"--config={_rcloneConfigPath}",
                "--max-depth=1"
            );

            var success = result != null;
            
            if (success)
            {
                _log.Success($"? Connection test successful for {remoteName}");
            }
            else
            {
                _log.Error($"? Connection test failed for {remoteName}");
            }

            return success;
        }
        catch (Exception ex)
        {
            _log.Error($"? Connection test failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Remove a remote from config
    /// </summary>
    public async Task<bool> RemoveRemoteAsync(string remoteName)
    {
        try
        {
            _log.Info($"??? Removing remote: {remoteName}");

            if (!File.Exists(_rcloneConfigPath))
            {
                return true;
            }

            var config = await File.ReadAllTextAsync(_rcloneConfigPath);
            var newConfig = RemoveRemoteFromConfig(config, remoteName);
            await File.WriteAllTextAsync(_rcloneConfigPath, newConfig);

            _log.Success($"? Remote removed: {remoteName}");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error($"? Failed to remove remote: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Run an rclone command and return output
    /// </summary>
    private async Task<string?> RunRcloneCommandAsync(params string[] args)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _rcloneExePath,
                    Arguments = string.Join(" ", args),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                _log.Warning($"?? rclone command failed: {error}");
                return null;
            }

            return output;
        }
        catch (Exception ex)
        {
            _log.Error($"? Failed to run rclone command: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get rclone provider name from friendly name
    /// </summary>
    private string GetRcloneProvider(string providerName)
    {
        return providerName.ToLowerInvariant() switch
        {
            "aws s3" => "AWS",
            "backblaze b2" => "Other", // B2 uses S3-compatible API
            "wasabi" => "Wasabi",
            "digitalocean spaces" => "DigitalOcean",
            "minio" => "Minio",
            _ => "Other"
        };
    }

    /// <summary>
    /// Remove a remote section from config
    /// </summary>
    private string RemoveRemoteFromConfig(string config, string remoteName)
    {
        var lines = config.Split('\n');
        var result = new List<string>();
        bool inRemoteSection = false;

        foreach (var line in lines)
        {
            if (line.Trim().StartsWith($"[{remoteName}]"))
            {
                inRemoteSection = true;
                continue;
            }

            if (inRemoteSection && line.Trim().StartsWith("["))
            {
                inRemoteSection = false;
            }

            if (!inRemoteSection)
            {
                result.Add(line);
            }
        }

        return string.Join('\n', result);
    }

    /// <summary>
    /// Cleanup - unmount all active mounts
    /// </summary>
    public async Task CleanupAllMountsAsync()
    {
        _log.Info("?? Cleaning up all rclone mounts");

        foreach (var kvp in _activeProcesses.ToList())
        {
            try
            {
                if (!kvp.Value.HasExited)
                {
                    kvp.Value.Kill(entireProcessTree: true);
                    await kvp.Value.WaitForExitAsync();
                }
            }
            catch (Exception ex)
            {
                _log.Warning($"?? Error cleaning up mount {kvp.Key}: {ex.Message}");
            }
        }

        _activeProcesses.Clear();
        _log.Success("? All mounts cleaned up");
    }
}
