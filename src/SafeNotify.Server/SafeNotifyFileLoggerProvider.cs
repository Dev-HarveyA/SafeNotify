using Microsoft.Extensions.Logging;

namespace SafeNotify.Server;

internal sealed class SafeNotifyFileLoggerProvider : ILoggerProvider
{
    private readonly string directory;
    private readonly object writeLock = new();

    public SafeNotifyFileLoggerProvider(string directory)
    {
        this.directory = directory;
        Directory.CreateDirectory(directory);
        RemoveExpiredFiles();
    }

    public ILogger CreateLogger(string categoryName) => new SafeNotifyFileLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void RemoveExpiredFiles()
    {
        foreach (var file in Directory.EnumerateFiles(directory, "server-*.log"))
        {
            if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-30))
            {
                File.Delete(file);
            }
        }
    }

    private void Write<TState>(string categoryName, LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        if (string.IsNullOrWhiteSpace(message) && exception is null)
        {
            return;
        }

        var line = $"{DateTimeOffset.UtcNow:O} [{logLevel}] {categoryName} ({eventId.Id}): {message}";
        if (exception is not null)
        {
            line += Environment.NewLine + exception;
        }

        try
        {
            lock (writeLock)
            {
                var path = Path.Combine(directory, $"server-{DateTime.UtcNow:yyyyMMdd}.log");
                File.AppendAllText(path, line + Environment.NewLine, System.Text.Encoding.UTF8);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class SafeNotifyFileLogger(SafeNotifyFileLoggerProvider provider, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                provider.Write(categoryName, logLevel, eventId, state, exception, formatter);
            }
        }
    }
}
