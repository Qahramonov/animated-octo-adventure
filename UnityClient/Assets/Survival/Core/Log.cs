using System;

namespace Survival.Core
{
    public enum LogLevel : byte { Debug, Info, Warning, Error }

    public interface ILogSink
    {
        void Write(LogLevel level, string category, string message);
    }

    /// <summary>
    /// Engine-independent logging facade. The Unity client routes it to Debug.Log, the dedicated
    /// server to the console. Thread-safe as long as the sink is.
    /// </summary>
    public static class Log
    {
        private sealed class ConsoleSink : ILogSink
        {
            private readonly object _lock = new object();

            public void Write(LogLevel level, string category, string message)
            {
                lock (_lock)
                {
                    Console.WriteLine($"{DateTime.UtcNow:HH:mm:ss.fff} [{level}] [{category}] {message}");
                }
            }
        }

        public static ILogSink Sink = new ConsoleSink();
        public static LogLevel MinLevel = LogLevel.Info;

        public static void Debug(string category, string message) => Write(LogLevel.Debug, category, message);
        public static void Info(string category, string message) => Write(LogLevel.Info, category, message);
        public static void Warn(string category, string message) => Write(LogLevel.Warning, category, message);
        public static void Error(string category, string message) => Write(LogLevel.Error, category, message);

        public static void Exception(string category, Exception e) => Write(LogLevel.Error, category, e.ToString());

        private static void Write(LogLevel level, string category, string message)
        {
            if (level < MinLevel) return;
            try
            {
                Sink?.Write(level, category, message);
            }
            catch
            {
                // Logging must never take the game or the server down.
            }
        }
    }
}
