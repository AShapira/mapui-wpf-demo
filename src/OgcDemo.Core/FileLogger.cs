using Microsoft.Extensions.Logging;

namespace OgcDemo.Core;

/// <summary>Small daily file sink. Callers log authored event messages, never URLs, credentials, or feature data.</summary>
public sealed class FileLoggerProvider(string directory) : ILoggerProvider
{
    private readonly object _gate = new();
    private readonly string _directory = directory;
    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);
    public void Dispose() { }
    private sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Information;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            // Deliberately exclude exception messages/stacks; URLs can occur inside transport exceptions.
            var line = $"{DateTimeOffset.UtcNow:O} {level} {category} {formatter(state, null)} {exception?.GetType().Name}";
            lock (owner._gate)
            {
                try
                {
                    Directory.CreateDirectory(owner._directory);
                    File.AppendAllText(Path.Combine(owner._directory, $"ogc-demo-{DateTime.UtcNow:yyyyMMdd}.log"), line + Environment.NewLine);
                }
                catch (IOException) { System.Diagnostics.Debug.WriteLine("OgcDemo log write failed."); }
                catch (UnauthorizedAccessException) { System.Diagnostics.Debug.WriteLine("OgcDemo log directory is not writable."); }
            }
        }
    }
}
