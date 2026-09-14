using System.IO;
using System.Text;

namespace BaiYunGe.Core;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error
}

/// <summary>
/// 轻量文件日志：按天滚动，UTF-8 无 BOM，写失败静默（不干扰主流程）。
/// </summary>
public sealed class AppLogger : IDisposable
{
    private readonly object _sync = new();
    private readonly string _logDirectory;
    private StreamWriter? _writer;
    private string _currentDay = string.Empty;

    public AppLogger(string logDirectory)
    {
        _logDirectory = logDirectory;
    }

    public void Debug(string message) => Write(LogLevel.Debug, message);

    public void Info(string message) => Write(LogLevel.Info, message);

    public void Warn(string message) => Write(LogLevel.Warn, message);

    public void Error(string message, Exception? exception = null)
    {
        Write(LogLevel.Error, exception is null ? message : $"{message}{Environment.NewLine}{exception}");
    }

    private void Write(LogLevel level, string message)
    {
        try
        {
            lock (_sync)
            {
                var day = DateTime.Now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
                if (_writer is null || day != _currentDay)
                {
                    _writer?.Dispose();
                    Directory.CreateDirectory(_logDirectory);
                    var path = Path.Combine(_logDirectory, $"baiyunge_{day}.log");
                    _writer = new StreamWriter(path, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    _currentDay = day;
                }

                _writer.WriteLine(
                    $"{DateTime.Now:HH:mm:ss.fff} [{level.ToString().ToUpperInvariant(),-5}] {message}");
                _writer.Flush();
            }
        }
        catch
        {
            // 日志失败不能影响主流程。
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
