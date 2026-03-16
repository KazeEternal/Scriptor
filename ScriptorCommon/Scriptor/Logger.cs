using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scripts.Scriptor
{
    public static class Logger
    {
        public sealed record LogEntry(
            DateTimeOffset Timestamp,
            LogLevel Level,
            string Message,
            string? ScopeKey,
            bool IsProgress,
            string? ProgressKey,
            double? ProgressValue);

        public enum LogLevel
        {
            Event,
            Warning,
            Error,
        }

        private static readonly AsyncLocal<string?> _scopeKey = new();
        private static readonly object _fileGate = new();
        private static StreamWriter? _fileWriter;
        private static string? _sessionLogPath;
        private static bool _sessionInitialized;

        public delegate void LogEventHandler(string format, params object[] args);
        public delegate void LogEntryHandler(LogEntry entry);
        public static event LogEventHandler Error;
        public static event LogEventHandler Warning;
        public static event LogEventHandler Event;
        public static event LogEntryHandler EntryWritten;

        static Logger()
        {
            InitializeSessionIfNeeded(null);

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                WriteCrashLine("UnhandledException", ex?.ToString() ?? e.ExceptionObject?.ToString() ?? "<unknown>");
            };

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                WriteCrashLine("UnobservedTaskException", e.Exception.ToString());
            };
        }

        public static string? SessionLogPath
        {
            get
            {
                lock (_fileGate)
                {
                    return _sessionLogPath;
                }
            }
        }

        public static void ConfigureFileLogging(string rootDirectory)
        {
            InitializeSessionIfNeeded(rootDirectory);
        }

        private static void InitializeSessionIfNeeded(string? rootDirectory)
        {
            lock (_fileGate)
            {
                if (_sessionInitialized)
                {
                    return;
                }

                var baseDir = string.IsNullOrWhiteSpace(rootDirectory)
                    ? Path.Combine(AppContext.BaseDirectory, ".scriptor", "logs")
                    : Path.Combine(rootDirectory, ".scriptor", "logs");

                try
                {
                    Directory.CreateDirectory(baseDir);
                    var fileName = $"session-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log";
                    _sessionLogPath = Path.Combine(baseDir, fileName);
                    var stream = new FileStream(_sessionLogPath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite);
                    _fileWriter = new StreamWriter(stream) { AutoFlush = true };
                    _fileWriter.WriteLine($"[{DateTimeOffset.Now:O}] [INFO] Logger session started.");
                }
                catch
                {
                    _fileWriter = null;
                }

                _sessionInitialized = true;
            }
        }

        private static void WriteFileLine(LogEntry entry)
        {
            lock (_fileGate)
            {
                if (_fileWriter == null)
                {
                    return;
                }

                var progressPart = entry.IsProgress
                    ? $" progressKey={entry.ProgressKey} progress={entry.ProgressValue:0.##}%"
                    : string.Empty;

                _fileWriter.WriteLine($"[{entry.Timestamp:O}] [{entry.Level}] scope={entry.ScopeKey ?? "-"}{progressPart} :: {entry.Message}");
                _fileWriter.Flush();
            }
        }

        private static void WriteCrashLine(string source, string message)
        {
            lock (_fileGate)
            {
                if (_fileWriter == null)
                {
                    return;
                }

                _fileWriter.WriteLine($"[{DateTimeOffset.Now:O}] [CRASH] [{source}] {message}");
                _fileWriter.Flush();
            }
        }

        private static void EmitLegacy(LogLevel level, string format, object[] args)
        {
            switch (level)
            {
                case LogLevel.Event:
                    if (Event != null) Event(format, args);
                    break;
                case LogLevel.Warning:
                    if (Warning != null) Warning(format, args);
                    break;
                case LogLevel.Error:
                    if (Error != null) Error(format, args);
                    break;
                default:
                    if (Event != null) Event(String.Format("[{0}]", level.ToString(), format), args);
                    break;
            }
        }

        public static IDisposable BeginScope(string scopeKey)
        {
            var previous = _scopeKey.Value;
            _scopeKey.Value = scopeKey;
            return new ScopeToken(() => _scopeKey.Value = previous);
        }

        public static void Write(LogLevel level, string format, params object[] args)
        {
            var message = args != null && args.Length > 0 ? string.Format(format, args) : format;
            EmitLegacy(level, format, args);

            var entry = new LogEntry(
                DateTimeOffset.Now,
                level,
                message,
                _scopeKey.Value,
                false,
                null,
                null);
            EntryWritten?.Invoke(entry);
            WriteFileLine(entry);
        }

        public static void WriteProgress(string progressKey, double progressValue, string message = "")
        {
            var normalized = Math.Max(0, Math.Min(100, progressValue));
            var output = string.IsNullOrWhiteSpace(message) ? $"Progress {normalized:0.##}%" : message;

            var entry = new LogEntry(
                DateTimeOffset.Now,
                LogLevel.Event,
                output,
                _scopeKey.Value,
                true,
                progressKey,
                normalized);
            EntryWritten?.Invoke(entry);
            WriteFileLine(entry);
        }

        public static void WriteToProgress(string progressKey, LogLevel level, string format, params object[] args)
        {
            var message = args != null && args.Length > 0 ? string.Format(format, args) : format;
            EmitLegacy(level, format, args);

            var entry = new LogEntry(
                DateTimeOffset.Now,
                level,
                message,
                _scopeKey.Value,
                false,
                progressKey,
                null);
            EntryWritten?.Invoke(entry);
            WriteFileLine(entry);
        }

        public static void WriteLine(LogLevel level, string format, params object[] args)
        {
            Write(level, format +"\n", args);
        }

        private sealed class ScopeToken : IDisposable
        {
            private readonly Action _dispose;
            private bool _isDisposed;

            public ScopeToken(Action dispose)
            {
                _dispose = dispose;
            }

            public void Dispose()
            {
                if (_isDisposed)
                {
                    return;
                }

                _dispose();
                _isDisposed = true;
            }
        }
    }
}
