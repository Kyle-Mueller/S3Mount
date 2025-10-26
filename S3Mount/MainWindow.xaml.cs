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

namespace S3Mount
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        
        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = _viewModel;
            
            // Enable dark mode for title bar
            WindowHelper.EnableDarkModeForWindow(this);
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