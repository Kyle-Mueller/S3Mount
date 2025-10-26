using S3Mount.Services;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace S3Mount.Views;

public partial class LogViewerWindow : Window
{
    private readonly LogService _logService;
    private string _filterText = string.Empty;

    public LogViewerWindow()
    {
        InitializeComponent();
        _logService = LogService.Instance;
        
        LogPathText.Text = $"Log file: {_logService.LogFilePath}";
        
        // Load initial logs
        LoadLogs();
        
        // Subscribe to live updates if enabled
        if (LiveUpdateCheckBox.IsChecked == true)
        {
            _logService.NewLogEntry += OnNewLogEntry;
        }
    }

    private void LoadLogs()
    {
        var allLogs = _logService.GetAllLogs();
        
        // Apply filter if set
        var filteredLogs = string.IsNullOrWhiteSpace(_filterText)
            ? allLogs
            : allLogs.Where(line => line.Contains(_filterText, StringComparison.OrdinalIgnoreCase)).ToArray();

        // Colorize log levels
        var colorizedText = string.Join(Environment.NewLine, filteredLogs.Select(ColorizeLogLine));
        
        LogTextBox.Text = colorizedText;
        LineCountText.Text = $"{filteredLogs.Length} lines";
        StatusText.Text = $"Loaded at {DateTime.Now:HH:mm:ss}";

        // Auto-scroll to bottom
        if (AutoScrollCheckBox.IsChecked == true)
        {
            LogScrollViewer.ScrollToEnd();
        }
    }

    private string ColorizeLogLine(string line)
    {
        // Simple text coloring - in a real implementation you might use RichTextBox
        // For now, just return the line as-is
        return line;
    }

    private void OnNewLogEntry(string logEntry)
    {
        // Update on UI thread
        Dispatcher.Invoke(() =>
        {
            // Apply filter
            if (!string.IsNullOrWhiteSpace(_filterText) && 
                !logEntry.Contains(_filterText, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Append new entry
            if (!string.IsNullOrEmpty(LogTextBox.Text))
            {
                LogTextBox.AppendText(Environment.NewLine);
            }
            LogTextBox.AppendText(logEntry);

            // Update line count
            var lineCount = LogTextBox.LineCount;
            LineCountText.Text = $"{lineCount} lines";

            // Auto-scroll if enabled
            if (AutoScrollCheckBox.IsChecked == true)
            {
                LogScrollViewer.ScrollToEnd();
            }
        });
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadLogs();
        StatusText.Text = $"Refreshed at {DateTime.Now:HH:mm:ss}";
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Are you sure you want to clear the log?\n\nThis will delete all log entries.",
            "Clear Log",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            _logService.ClearLog();
            LoadLogs();
            StatusText.Text = "Log cleared";
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var logFolder = Path.GetDirectoryName(_logService.LogFilePath);
            if (!string.IsNullOrEmpty(logFolder))
            {
                Process.Start("explorer.exe", logFolder);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var saveDialog = new SaveFileDialog
        {
            Title = "Export Log File",
            Filter = "Log files (*.log)|*.log|Text files (*.txt)|*.txt|All files (*.*)|*.*",
            DefaultExt = "log",
            FileName = $"s3mount_log_{DateTime.Now:yyyyMMdd_HHmmss}.log"
        };

        if (saveDialog.ShowDialog() == true)
        {
            try
            {
                File.Copy(_logService.LogFilePath, saveDialog.FileName, overwrite: true);
                StatusText.Text = $"Log exported to {Path.GetFileName(saveDialog.FileName)}";
                MessageBox.Show("Log exported successfully!", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to export log: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void LiveUpdateCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        _logService.NewLogEntry += OnNewLogEntry;
        StatusText.Text = "Live updates enabled";
    }

    private void LiveUpdateCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        _logService.NewLogEntry -= OnNewLogEntry;
        StatusText.Text = "Live updates disabled";
    }

    private void FilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _filterText = FilterTextBox.Text;
        LoadLogs();
    }

    protected override void OnClosed(EventArgs e)
    {
        // Unsubscribe from events
        _logService.NewLogEntry -= OnNewLogEntry;
        base.OnClosed(e);
    }
}
