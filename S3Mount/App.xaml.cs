using System.Configuration;
using System.Data;
using System.Windows;
using S3Mount.Services;
using S3Mount.ViewModels;
using Hardcodet.Wpf.TaskbarNotification;
using System.IO;
using System.Windows.Media.Imaging;

namespace S3Mount
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private TaskbarIcon? _trayIcon;
        private MainWindow? _mainWindow;
        private CredentialService? _credentialService;
        private S3Service? _s3Service;
        private VirtualDriveService? _driveService;
        private MainViewModel? _mainViewModel;
        
        private async void Application_Startup(object sender, StartupEventArgs e)
        {
            try
            {
                // Initialize services
                _credentialService = new CredentialService();
                _s3Service = new S3Service();
                _driveService = new VirtualDriveService(_s3Service, _credentialService);
                
                // Initialize ViewModel
                _mainViewModel = new MainViewModel(_credentialService, _driveService);
                
                // Setup system tray with error handling
                try
                {
                    _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
                    
                    // Try to load icon if it exists
                    LoadTrayIcon();
                }
                catch (Exception ex)
                {
                    // If tray icon fails to load, create a simple one without icon
                    _trayIcon = new TaskbarIcon
                    {
                        ToolTipText = "S3 Mount Manager",
                        MenuActivation = Hardcodet.Wpf.TaskbarNotification.PopupActivationMode.RightClick
                    };
                    
                    // Try to load icon for the created tray icon too
                    LoadTrayIcon();
                    
                    // Create context menu manually
                    var contextMenu = new System.Windows.Controls.ContextMenu();
                    
                    var showMenuItem = new System.Windows.Controls.MenuItem { Header = "Show Manager", FontWeight = FontWeights.Bold };
                    showMenuItem.Click += ShowManager_Click;
                    contextMenu.Items.Add(showMenuItem);
                    
                    contextMenu.Items.Add(new System.Windows.Controls.Separator());
                    
                    var exitMenuItem = new System.Windows.Controls.MenuItem { Header = "Exit" };
                    exitMenuItem.Click += Exit_Click;
                    contextMenu.Items.Add(exitMenuItem);
                    
                    _trayIcon.ContextMenu = contextMenu;
                    _trayIcon.TrayLeftMouseUp += TrayIcon_TrayLeftMouseUp;
                    
                    System.Diagnostics.Debug.WriteLine($"Tray icon resource load failed: {ex.Message}");
                }
                
                // Create main window but don't show it yet
                _mainWindow = new MainWindow(_mainViewModel);
                
                // Auto-mount configured drives
                await _mainViewModel.AutoMountDrivesAsync();
                
                // Check if there are any mounts configured
                var configs = _credentialService.GetAllConfigurations();
                
                if (configs.Count == 0)
                {
                    // No mounts configured, show the window immediately
                    _mainWindow.Show();
                    ShowTrayNotification("S3 Mount Manager", "Welcome! Please configure your first S3 mount.");
                }
                else
                {
                    // Mounts exist, start in tray
                    ShowTrayNotification("S3 Mount Manager", $"Application started. {configs.Count} mount(s) configured.");
                }
            }
            catch (Exception ex)
            {
                Helpers.DarkMessageBox.Show(
                    $"Error starting application: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}",
                    "Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown();
            }
        }
        
        private void LoadTrayIcon()
        {
            if (_trayIcon == null) return;
            
            try
            {
                // Try multiple possible icon locations
                var possiblePaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "icon.ico"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico"),
                    Path.Combine(Environment.CurrentDirectory, "Resources", "icon.ico"),
                    Path.Combine(Environment.CurrentDirectory, "icon.ico")
                };
                
                foreach (var iconPath in possiblePaths)
                {
                    if (File.Exists(iconPath))
                    {
                        _trayIcon.IconSource = new BitmapImage(new Uri(iconPath, UriKind.Absolute));
                        System.Diagnostics.Debug.WriteLine($"Loaded tray icon from: {iconPath}");
                        return;
                    }
                }
                
                System.Diagnostics.Debug.WriteLine("No icon file found. Application will run with default system icon.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load tray icon: {ex.Message}");
                // Continue without icon - not a critical error
            }
        }
        
        private void Application_Exit(object sender, ExitEventArgs e)
        {
            // Unmount all drives on exit
            _driveService?.UnmountAllDrives();
            
            // Cleanup
            _trayIcon?.Dispose();
            _s3Service?.Dispose();
        }
        
        private void TrayIcon_TrayLeftMouseUp(object sender, RoutedEventArgs e)
        {
            ShowMainWindow();
        }
        
        private void ShowManager_Click(object sender, RoutedEventArgs e)
        {
            ShowMainWindow();
        }
        
        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            // Actually exit the application
            _driveService?.UnmountAllDrives();
            Shutdown();
        }
        
        private void ShowMainWindow()
        {
            if (_mainWindow != null)
            {
                _mainWindow.Show();
                _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.Activate();
            }
        }
        
        public void ShowTrayNotification(string title, string message)
        {
            try
            {
                _trayIcon?.ShowBalloonTip(title, message, BalloonIcon.Info);
            }
            catch
            {
                // Silently fail if balloon tip can't be shown
            }
        }
        
        protected override void OnExit(ExitEventArgs e)
        {
            _driveService?.UnmountAllDrives();
            _trayIcon?.Dispose();
            _s3Service?.Dispose();
            base.OnExit(e);
        }
    }
}
