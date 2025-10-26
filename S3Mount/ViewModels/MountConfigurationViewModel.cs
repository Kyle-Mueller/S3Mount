using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using S3Mount.Models;
using S3Mount.Services;
using S3Mount.Helpers;
using System.Collections.ObjectModel;
using System.Windows;

namespace S3Mount.ViewModels;

public partial class MountConfigurationViewModel : ObservableObject
{
    private readonly CredentialService _credentialService;
    private readonly VirtualDriveService _driveService;
    private readonly S3MountConfiguration? _existingConfig;
    
    [ObservableProperty]
    private string _mountName = string.Empty;
    
    [ObservableProperty]
    private string? _selectedDriveLetter;
    
    [ObservableProperty]
    private ObservableCollection<string> _availableDriveLetters = new();
    
    [ObservableProperty]
    private string _bucketName = string.Empty;
    
    [ObservableProperty]
    private string _serviceUrl = string.Empty;
    
    [ObservableProperty]
    private string _region = string.Empty;
    
    [ObservableProperty]
    private bool _forcePathStyle;
    
    [ObservableProperty]
    private string _accessKey = string.Empty;
    
    [ObservableProperty]
    private string _secretKey = string.Empty;
    
    [ObservableProperty]
    private string _iconPath = string.Empty;
    
    [ObservableProperty]
    private bool _autoMount = true;
    
    [ObservableProperty]
    private S3ProviderTemplate? _selectedProvider;
    
    [ObservableProperty]
    private ObservableCollection<S3ProviderTemplate> _providers = new();
    
    [ObservableProperty]
    private bool _isTestingConnection;
    
    [ObservableProperty]
    private string _testResult = string.Empty;
    
    public bool DialogResult { get; set; }
    
    public MountConfigurationViewModel(
        CredentialService credentialService, 
        VirtualDriveService driveService,
        S3MountConfiguration? existingConfig = null)
    {
        _credentialService = credentialService;
        _driveService = driveService;
        _existingConfig = existingConfig;
        
        LoadProviders();
        LoadAvailableDriveLetters();
        
        if (_existingConfig != null)
        {
            LoadExistingConfiguration();
        }
    }
    
    private void LoadProviders()
    {
        var templates = S3ProviderTemplate.GetDefaultTemplates();
        
        foreach (var template in templates)
        {
            Providers.Add(template);
        }
        
        SelectedProvider = Providers.FirstOrDefault();
    }
    
    private void LoadAvailableDriveLetters()
    {
        // Add "Auto" option
        AvailableDriveLetters.Add("Auto");
        
        // Add all available drive letters
        var letters = _driveService.GetAvailableDriveLetters();
        foreach (var letter in letters)
        {
            AvailableDriveLetters.Add(letter);
        }
        
        // Default to Auto
        SelectedDriveLetter = "Auto";
    }
    
    private void LoadExistingConfiguration()
    {
        if (_existingConfig == null) return;
        
        MountName = _existingConfig.MountName;
        
        // Set drive letter if it exists
        if (!string.IsNullOrEmpty(_existingConfig.DriveLetter))
        {
            SelectedDriveLetter = _existingConfig.DriveLetter;
        }
        
        BucketName = _existingConfig.BucketName;
        ServiceUrl = _existingConfig.ServiceUrl;
        Region = _existingConfig.Region;
        ForcePathStyle = _existingConfig.ForcePathStyle;
        AccessKey = _existingConfig.AccessKey;
        SecretKey = _existingConfig.SecretKey;
        IconPath = _existingConfig.IconPath;
        AutoMount = _existingConfig.AutoMount;
        
        // Select matching provider
        var matchingProvider = Providers.FirstOrDefault(p => 
            p.ServiceUrl.Equals(_existingConfig.ServiceUrl, StringComparison.OrdinalIgnoreCase));
        
        if (matchingProvider != null)
        {
            SelectedProvider = matchingProvider;
        }
        else
        {
            SelectedProvider = Providers.FirstOrDefault(p => p.Name == "Custom");
        }
    }
    
    partial void OnSelectedProviderChanged(S3ProviderTemplate? value)
    {
        if (value == null) return;
        
        // Don't overwrite if editing existing config
        if (_existingConfig != null && !string.IsNullOrEmpty(ServiceUrl))
            return;
            
        ServiceUrl = value.ServiceUrl;
        Region = value.Region;
        ForcePathStyle = value.ForcePathStyle;
    }
    
    [RelayCommand]
    private void BrowseIcon()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Icon Files (*.ico)|*.ico|PNG Files (*.png)|*.png|All Files (*.*)|*.*",
            Title = "Select Drive Icon"
        };
        
        if (dialog.ShowDialog() == true)
        {
            IconPath = dialog.FileName;
        }
    }
    
    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (string.IsNullOrEmpty(BucketName) || 
            string.IsNullOrEmpty(ServiceUrl) || 
            string.IsNullOrEmpty(AccessKey) || 
            string.IsNullOrEmpty(SecretKey))
        {
            TestResult = "? Please fill in all required fields";
            return;
        }
        
        IsTestingConnection = true;
        TestResult = "?? Testing connection...";
        
        try
        {
            var rcloneService = new RcloneService();
            var credManager = new CredentialManagerService();
            
            // Check if rclone is available
            if (!rcloneService.IsRcloneAvailable())
            {
                TestResult = "? rclone.exe not found. Please download it from rclone.org";
                return;
            }
            
            // Create a temporary remote name for testing
            var tempRemoteName = $"test_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            
            try
            {
                // Store temp credentials
                credManager.StoreCredentials(tempRemoteName, AccessKey, SecretKey);
                
                // Configure temp remote
                var configured = await rcloneService.ConfigureRemoteAsync(
                    tempRemoteName,
                    SelectedProvider?.Name ?? "Custom",
                    ServiceUrl,
                    Region,
                    AccessKey,
                    SecretKey,
                    ForcePathStyle
                );
                
                if (!configured)
                {
                    TestResult = "? Failed to configure test connection";
                    return;
                }
                
                // Test connection
                var success = await rcloneService.TestConnectionAsync(tempRemoteName, BucketName);
                
                TestResult = success 
                    ? "? Connection successful!" 
                    : "? Connection failed. Please check your credentials and bucket name.";
            }
            finally
            {
                // Clean up temp remote
                await rcloneService.RemoveRemoteAsync(tempRemoteName);
                credManager.DeleteCredentials(tempRemoteName);
            }
        }
        catch (Exception ex)
        {
            TestResult = $"? Error: {ex.Message}";
        }
        finally
        {
            IsTestingConnection = false;
        }
    }
    
    [RelayCommand]
    private void Save()
    {
        System.Diagnostics.Debug.WriteLine("Save command started");
        
        if (string.IsNullOrEmpty(MountName))
        {
            DarkMessageBox.Show("Please enter a mount name.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        if (string.IsNullOrEmpty(BucketName))
        {
            DarkMessageBox.Show("Please enter a bucket name.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        if (string.IsNullOrEmpty(ServiceUrl))
        {
            DarkMessageBox.Show("Please enter a service URL.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        if (string.IsNullOrEmpty(AccessKey) || string.IsNullOrEmpty(SecretKey))
        {
            DarkMessageBox.Show("Please enter access credentials.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        var config = _existingConfig ?? new S3MountConfiguration();
        
        config.MountName = MountName;
        config.DriveLetter = SelectedDriveLetter == "Auto" ? string.Empty : (SelectedDriveLetter ?? string.Empty);
        config.BucketName = BucketName;
        config.ServiceUrl = ServiceUrl;
        config.Region = Region;
        config.ForcePathStyle = ForcePathStyle;
        config.AccessKey = AccessKey;
        config.SecretKey = SecretKey;
        config.IconPath = IconPath;
        config.AutoMount = AutoMount;
        config.ProviderName = SelectedProvider?.Name ?? "Custom";
        config.LastModified = DateTime.Now;
        
        System.Diagnostics.Debug.WriteLine($"Saving config: {config.MountName}, ID: {config.Id}");
        
        try
        {
            _credentialService.SaveConfiguration(config);
            System.Diagnostics.Debug.WriteLine("Configuration saved successfully");
            DialogResult = true;
            System.Diagnostics.Debug.WriteLine($"DialogResult set to: {DialogResult}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Save failed: {ex.Message}");
            DarkMessageBox.Show($"Failed to save configuration: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            DialogResult = false;
        }
    }
    
    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
    }
}
