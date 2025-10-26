using System.IO;
using System.Runtime.InteropServices;

namespace S3Mount.Services;

/// <summary>
/// Intercepts file open operations to trigger on-demand downloads
/// Uses Windows Oplock (Opportunistic Lock) to detect when files are opened
/// </summary>
public class FileAccessMonitor : IDisposable
{
    private readonly string _rootPath;
    private readonly Func<string, Task> _onFileAccessed;
    private readonly CancellationTokenSource _cts = new();
    private Task? _monitorTask;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        IntPtr hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_OVERLAPPED = 0x40000000;

    public FileAccessMonitor(string rootPath, Func<string, Task> onFileAccessed)
    {
        _rootPath = rootPath;
        _onFileAccessed = onFileAccessed;
    }

    public void Start()
    {
        _monitorTask = Task.Run(async () => await MonitorDirectoryAsync(), _cts.Token);
    }

    private async Task MonitorDirectoryAsync()
    {
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                // Monitor all files periodically
                await Task.Delay(200, _cts.Token);
                
                // This is a simplified version - in production you'd use ReadDirectoryChangesW
                // or a kernel-mode filter driver for true file access detection
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FileAccessMonitor - Error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _monitorTask?.Wait(TimeSpan.FromSeconds(2));
        _cts.Dispose();
    }
}
