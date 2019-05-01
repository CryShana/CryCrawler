using System;
using System.Timers;
using System.Threading;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace CryCrawler
{
    public static class Logger
    {
        const int TimerInterval = 50;

        static bool IsActive = false;
        public static bool DebugMode = false;
        static readonly SemaphoreSlim semaphore = new SemaphoreSlim(1);
        static readonly ConcurrentQueue<LogMessage> QueuedLogs = new ConcurrentQueue<LogMessage>();

        public static async Task Log(string message, LogSeverity severity = LogSeverity.Information)
        {
            QueuedLogs.Enqueue(new LogMessage(message, severity));

            // check if logger active
            if (!IsActive)
            {
                // if logger not active, start the timer and activate it
                await semaphore.WaitAsync().ConfigureAwait(false);

                var timer = new System.Timers.Timer(TimerInterval);
                timer.Elapsed += TimerElapsed;
                timer.Start();
                IsActive = true;

                semaphore.Release();
            }
        }

        private static void TimerElapsed(object sender, ElapsedEventArgs e)
        {
            if (QueuedLogs.IsEmpty) return;
            if (QueuedLogs.TryDequeue(out LogMessage msg) == false) return;
            if (!DebugMode && msg.Severity == LogSeverity.Debug) return;

            // set Console color if log not informational
            if (msg.Severity == LogSeverity.Error) Console.ForegroundColor = ConsoleColor.Red;
            else if (msg.Severity == LogSeverity.Warning) Console.ForegroundColor = ConsoleColor.Yellow;
            else if (msg.Severity == LogSeverity.Debug) Console.ForegroundColor = ConsoleColor.DarkGray;

            // severity text
            var severityText = "";
            switch (msg.Severity)
            {
                case LogSeverity.Information:
                    severityText = "INFO";
                    break;
                case LogSeverity.Error:
                    severityText = "ERROR";
                    break;
                case LogSeverity.Warning:
                    severityText = "WARN";
                    break;
                case LogSeverity.Debug:
                    severityText = "DEBUG";
                    break;
            }

            // display log message
            Console.WriteLine($"[{msg.LogTime.ToString("dd.MM.yyyy HH:mm:ss.ffff")}] {severityText} - {msg.Message}");

            // reset color if log was not informational
            if (msg.Severity != LogSeverity.Information) Console.ResetColor();
        }

        private struct LogMessage
        {
            public readonly string Message;
            public readonly LogSeverity Severity;
            public readonly DateTime LogTime;
            public LogMessage(string message, LogSeverity severity)
            {
                Message = message;
                Severity = severity;
                LogTime = DateTime.Now;
            }
        }

        public enum LogSeverity
        {
            Information,
            Warning,
            Error,
            Debug
        }
    }
}
