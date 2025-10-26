using System.Windows;
using System.Windows.Controls;
using S3Mount.ViewModels;
using S3Mount.Helpers;

namespace S3Mount.Views;

public partial class MountConfigurationDialog : Window
{
    private MountConfigurationViewModel ViewModel => (MountConfigurationViewModel)DataContext;
    
    public MountConfigurationDialog()
    {
        InitializeComponent();
        
        // Enable dark mode for title bar
        WindowHelper.EnableDarkModeForWindow(this);
        
        // Set the password if editing existing config
        Loaded += MountConfigurationDialog_Loaded;
    }
    
    private void MountConfigurationDialog_Loaded(object sender, RoutedEventArgs e)
    {
        // Set focus to mount name field
        if (!string.IsNullOrEmpty(ViewModel.SecretKey))
        {
            SecretKeyBox.Password = ViewModel.SecretKey;
        }
    }
    
    private void SecretKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MountConfigurationViewModel vm)
        {
            vm.SecretKey = ((PasswordBox)sender).Password;
        }
    }
    
    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        // The SaveCommand executes synchronously, so we can check immediately
        // But we need to give the command time to execute first
        System.Diagnostics.Debug.WriteLine($"SaveButton_Click - Before check, DialogResult: {ViewModel.DialogResult}");
        
        // Execute the save command manually if it hasn't been executed by the binding
        if (ViewModel.SaveCommand.CanExecute(null))
        {
            ViewModel.SaveCommand.Execute(null);
        }
        
        System.Diagnostics.Debug.WriteLine($"SaveButton_Click - After execute, DialogResult: {ViewModel.DialogResult}");
        
        // Now check the result
        if (ViewModel.DialogResult)
        {
            System.Diagnostics.Debug.WriteLine("SaveButton_Click - Setting Window DialogResult to true");
            DialogResult = true;
            Close();
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("SaveButton_Click - ViewModel DialogResult was false, not closing");
        }
    }
    
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
