using System.Globalization;

namespace BluetoothOff.Logging;

internal sealed class BoundedFileLoggerProvider : ILoggerProvider
{
    private const long MaximumTotalBytes = 5 * 1024 * 1024;
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private readonly object _gate = new();
    private readonly string _logsDirectory;
    private bool _disposed;

    internal BoundedFileLoggerProvider(string logsDirectory)
    {
        _logsDirectory = logsDirectory;
        Directory.CreateDirectory(_logsDirectory);
        PruneLogs();
    }

    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new BoundedFileLogger(this, categoryName);
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private void Write(
        string category,
        LogLevel level,
        EventId eventId,
        string message,
        Exception? exception)
    {
        if (_disposed || level < LogLevel.Information
            || !category.StartsWith("BluetoothOff", StringComparison.Ordinal))
        {
            return;
        }

        var safeMessage = Sanitize(message);
        var exceptionType = exception?.GetType().FullName;
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTimeOffset.UtcNow:O} [{level}] {eventId.Id} {safeMessage}");

        if (!string.IsNullOrWhiteSpace(exceptionType))
        {
            line = string.Concat(line, " ExceptionType=", exceptionType);
        }

        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_logsDirectory);
                PruneLogs();
                var file = Path.Combine(
                    _logsDirectory,
                    string.Create(CultureInfo.InvariantCulture, $"bluetooth-off-{DateTime.UtcNow:yyyyMMdd}.log"));
                File.AppendAllText(file, string.Concat(line, Environment.NewLine));
            }
            catch (IOException)
            {
                // Logging must never interrupt Bluetooth control.
            }
            catch (UnauthorizedAccessException)
            {
                // Logging must never interrupt Bluetooth control.
            }
        }
    }

    private void PruneLogs()
    {
        try
        {
            var cutoff = DateTime.UtcNow - Retention;
            var files = Directory.EnumerateFiles(_logsDirectory, "bluetooth-off-*.log")
                .Select(static path => new FileInfo(path))
                .OrderBy(static file => file.LastWriteTimeUtc)
                .ToList();

            foreach (var file in files.Where(file => file.LastWriteTimeUtc < cutoff).ToList())
            {
                file.Delete();
                files.Remove(file);
            }

            var total = files.Sum(static file => file.Exists ? file.Length : 0);
            foreach (var file in files)
            {
                if (total <= MaximumTotalBytes)
                {
                    break;
                }

                var length = file.Exists ? file.Length : 0;
                file.Delete();
                total -= length;
            }
        }
        catch (IOException)
        {
            // Best-effort maintenance only.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort maintenance only.
        }
    }

    private static string Sanitize(string value)
    {
        const int maximumLength = 2048;
        var oneLine = value.Replace('\r', ' ').Replace('\n', ' ');
        return oneLine.Length <= maximumLength ? oneLine : oneLine[..maximumLength];
    }

    private sealed class BoundedFileLogger(
        BoundedFileLoggerProvider provider,
        string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= LogLevel.Information
                && category.StartsWith("BluetoothOff", StringComparison.Ordinal);
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            provider.Write(category, logLevel, eventId, formatter(state, exception), exception);
        }
    }
}

