using System.Collections.Concurrent;
using System.IO;

namespace S3Mount.Services;

/// <summary>
/// Centralized logging service that writes to a rotating log file
/// Max 5000 lines, auto-rotates when exceeded
/// </summary>
public class LogService
{
    private static readonly Lazy<LogService> _instance = new(() => new LogService());
    public static LogService Instance => _instance.Value;

    private readonly string _logFilePath;
    private readonly int _maxLines = 5000;
    private readonly object _fileLock = new();
    private readonly ConcurrentQueue<string> _recentLogs = new();
    private const int MaxRecentLogs = 1000;

    public event Action<string>? NewLogEntry;

    private LogService()
    {
        // Store logs in a standard temp location
        var logDir = Path.Combine(Path.GetTempPath(), "S3Mount", "Logs");
        Directory.CreateDirectory(logDir);
        
        _logFilePath = Path.Combine(logDir, "s3mount.log");
        
        // Create log file if it doesn't exist
        if (!File.Exists(_logFilePath))
        {
            File.WriteAllText(_logFilePath, $"=== S3Mount Log Started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
        }
    }

    public string LogFilePath => _logFilePath;

    public void Log(string message, LogLevel level = LogLevel.Info)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logEntry = $"[{timestamp}] [{level}] {message}";

        // Add to recent logs queue
        _recentLogs.Enqueue(logEntry);
        while (_recentLogs.Count > MaxRecentLogs)
        {
            _recentLogs.TryDequeue(out _);
        }

        // Notify subscribers (for live log viewer)
        NewLogEntry?.Invoke(logEntry);

        // Write to file
        lock (_fileLock)
        {
            try
            {
                File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);

                // Check if rotation needed
                var lineCount = File.ReadLines(_logFilePath).Count();
                if (lineCount > _maxLines)
                {
                    RotateLog();
                }
            }
            catch (Exception ex)
            {
                // Fallback to console if file write fails
                Console.WriteLine($"Failed to write to log: {ex.Message}");
                Console.WriteLine(logEntry);
            }
        }
    }

    public void Debug(string message) => Log(message, LogLevel.Debug);
    public void Info(string message) => Log(message, LogLevel.Info);
    public void Warning(string message) => Log(message, LogLevel.Warning);
    public void Error(string message) => Log(message, LogLevel.Error);
    public void Success(string message) => Log(message, LogLevel.Success);

    private void RotateLog()
    {
        try
        {
            // Read all lines
            var lines = File.ReadAllLines(_logFilePath);

            // Keep only the most recent lines (80% of max)
            var linesToKeep = (int)(_maxLines * 0.8);
            var newLines = lines.Skip(lines.Length - linesToKeep).ToArray();

            // Write back
            var header = $"=== Log rotated at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}";
            File.WriteAllText(_logFilePath, header);
            File.AppendAllLines(_logFilePath, newLines);

            Log("Log file rotated", LogLevel.Info);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to rotate log: {ex.Message}");
        }
    }

    public string[] GetRecentLogs(int count = 100)
    {
        return _recentLogs.TakeLast(count).ToArray();
    }

    public string[] GetAllLogs()
    {
        lock (_fileLock)
        {
            try
            {
                if (File.Exists(_logFilePath))
                {
                    return File.ReadAllLines(_logFilePath);
                }
            }
            catch
            {
                // Ignore
            }

            return Array.Empty<string>();
        }
    }

    public void ClearLog()
    {
        lock (_fileLock)
        {
            try
            {
                var header = $"=== S3Mount Log Cleared at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}";
                File.WriteAllText(_logFilePath, header);
                _recentLogs.Clear();
                Log("Log cleared by user", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to clear log: {ex.Message}");
            }
        }
    }
}

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Success
}
