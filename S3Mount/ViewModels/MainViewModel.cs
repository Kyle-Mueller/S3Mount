using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using S3Mount.Models;
using S3Mount.Services;
using S3Mount.Helpers;
using System.Collections.ObjectModel;
using System.Windows;

namespace S3Mount.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly CredentialService _credentialService;
    private readonly VirtualDriveService _driveService;
    
    [ObservableProperty]
    private ObservableCollection<S3MountConfiguration> _mounts = new();
    
    [ObservableProperty]
    private S3MountConfiguration? _selectedMount;
    
    [ObservableProperty]
    private bool _isLoading;
    
    public MainViewModel(CredentialService credentialService, VirtualDriveService driveService)
    {
        _credentialService = credentialService;
        _driveService = driveService;
        
        LoadMounts();
    }
    
    [RelayCommand]
    private void LoadMounts()
    {
        IsLoading = true;
        
        try
        {
            System.Diagnostics.Debug.WriteLine("LoadMounts - Starting");
            var configs = _credentialService.GetAllConfigurations();
            System.Diagnostics.Debug.WriteLine($"LoadMounts - Got {configs.Count} configurations");
            
            Mounts.Clear();
            
            foreach (var config in configs)
            {
                System.Diagnostics.Debug.WriteLine($"LoadMounts - Adding: {config.MountName} (ID: {config.Id})");
                Mounts.Add(config);
            }
            
            System.Diagnostics.Debug.WriteLine($"LoadMounts - Total mounts in collection: {Mounts.Count}");
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    [RelayCommand]
    private async Task AddMountAsync()
    {
        System.Diagnostics.Debug.WriteLine("AddMountAsync - Opening dialog");
        var dialog = new Views.MountConfigurationDialog();
        var viewModel = new MountConfigurationViewModel(_credentialService, _driveService);
        dialog.DataContext = viewModel;
        
        var result = dialog.ShowDialog();
        System.Diagnostics.Debug.WriteLine($"AddMountAsync - Dialog result: {result}");
        
        if (result == true)
        {
            System.Diagnostics.Debug.WriteLine("AddMountAsync - Calling LoadMounts");
            LoadMounts();
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("AddMountAsync - Dialog was cancelled or returned false");
        }
    }
    
    [RelayCommand]
    private async Task EditMountAsync(S3MountConfiguration config)
    {
        if (config == null) return;
        
        System.Diagnostics.Debug.WriteLine($"EditMountAsync - Editing {config.MountName}");
        
        // Remember if it was mounted
        var wasMounted = config.IsMounted;
        var originalDriveLetter = config.DriveLetter;
        
        // Unmount if currently mounted
        if (wasMounted)
        {
            System.Diagnostics.Debug.WriteLine($"EditMountAsync - Unmounting {config.MountName} before edit");
            await _driveService.UnmountDrive(config.Id);
        }
        
        var dialog = new Views.MountConfigurationDialog();
        var viewModel = new MountConfigurationViewModel(_credentialService, _driveService, config);
        dialog.DataContext = viewModel;
        
        var result = dialog.ShowDialog();
        
        if (result == true)
        {
            System.Diagnostics.Debug.WriteLine($"EditMountAsync - Changes saved for {config.MountName}");
            LoadMounts();
            
            // Remount if it was mounted before
            if (wasMounted)
            {
                System.Diagnostics.Debug.WriteLine($"EditMountAsync - Remounting {config.MountName}");
                
                // Get the updated config
                var updatedConfig = _credentialService.GetConfiguration(config.Id);
                if (updatedConfig != null)
                {
                    IsLoading = true;
                    try
                    {
                        var success = await _driveService.MountDriveAsync(updatedConfig);
                        if (success)
                        {
                            System.Diagnostics.Debug.WriteLine($"EditMountAsync - Successfully remounted {config.MountName}");
                            updatedConfig.IsMounted = true;
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"EditMountAsync - Failed to remount {config.MountName}");
                            DarkMessageBox.Show(
                                $"Configuration saved but failed to remount '{config.MountName}'. You can manually mount it from the list.",
                                "Remount Failed",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                    }
                    finally
                    {
                        IsLoading = false;
                        LoadMounts();
                    }
                }
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"EditMountAsync - Edit cancelled for {config.MountName}");
            
            // If edit was cancelled and it was mounted, remount with original settings
            if (wasMounted)
            {
                System.Diagnostics.Debug.WriteLine($"EditMountAsync - Remounting {config.MountName} with original settings");
                IsLoading = true;
                try
                {
                    await _driveService.MountDriveAsync(config);
                }
                finally
                {
                    IsLoading = false;
                    LoadMounts();
                }
            }
        }
    }
    
    [RelayCommand]
    private async Task DeleteMountAsync(S3MountConfiguration config)
    {
        if (config == null) return;
        
        var result = DarkMessageBox.Show(
            $"Are you sure you want to delete the mount '{config.MountName}'?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
            
        if (result == MessageBoxResult.Yes)
        {
            // Unmount if currently mounted
            if (config.IsMounted)
            {
                await _driveService.UnmountDrive(config.Id);
            }
            
            _credentialService.DeleteConfiguration(config.Id);
            LoadMounts();
        }
    }
    
    [RelayCommand]
    private async Task ToggleMountAsync(S3MountConfiguration config)
    {
        if (config == null) return;
        
        IsLoading = true;
        
        try
        {
            if (config.IsMounted)
            {
                System.Diagnostics.Debug.WriteLine($"ToggleMountAsync - Unmounting {config.MountName}");
                var success = await _driveService.UnmountDrive(config.Id);
                if (success)
                {
                    System.Diagnostics.Debug.WriteLine($"ToggleMountAsync - Successfully unmounted {config.MountName}");
                    config.IsMounted = false;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"ToggleMountAsync - Failed to unmount {config.MountName}");
                    DarkMessageBox.Show(
                        $"Failed to unmount '{config.MountName}'. The drive may be in use.",
                        "Unmount Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"ToggleMountAsync - Mounting {config.MountName}");
                var success = await _driveService.MountDriveAsync(config);
                if (success)
                {
                    System.Diagnostics.Debug.WriteLine($"ToggleMountAsync - Successfully mounted {config.MountName}");
                    config.IsMounted = true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"ToggleMountAsync - Failed to mount {config.MountName}");
                    DarkMessageBox.Show(
                        $"Failed to mount '{config.MountName}'. Please check your credentials and connection.",
                        "Mount Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            
            LoadMounts();
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    public async Task AutoMountDrivesAsync()
    {
        var configs = _credentialService.GetAllConfigurations();
        
        foreach (var config in configs.Where(c => c.AutoMount && !c.IsMounted))
        {
            try
            {
                await _driveService.MountDriveAsync(config);
            }
            catch
            {
                // Continue with other mounts even if one fails
            }
        }
        
        LoadMounts();
    }
}
