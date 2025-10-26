using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using S3Mount.ViewModels;
using S3Mount.Services;
using S3Mount.Helpers;
using S3Mount.Views;

namespace S3Mount
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private LogViewerWindow? _logViewerWindow;
        
        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = _viewModel;
            
            // Enable dark mode for title bar
            WindowHelper.EnableDarkModeForWindow(this);
            
            // Log application start
            LogService.Instance.Info("=== S3Mount Application Started ===");
        }
        
        private void ViewLogsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_logViewerWindow == null || !_logViewerWindow.IsLoaded)
            {
                _logViewerWindow = new LogViewerWindow();
                _logViewerWindow.Show();
            }
            else
            {
                _logViewerWindow.Activate();
                _logViewerWindow.Focus();
            }
        }
        
        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
                
                // Show notification
                var app = Application.Current as App;
                app?.ShowTrayNotification("S3 Mount Manager", "Application minimized to system tray");
            }
        }
        
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Don't actually close, just minimize to tray
            e.Cancel = true;
            WindowState = WindowState.Minimized;
        }
    }
}