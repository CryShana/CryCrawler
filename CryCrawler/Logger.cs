using System;
using System.Timers;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace CryCrawler
{
    public static class Logger
    {
        const int TimerInterval = 50;

        public static bool DebugMode = false;
        static readonly ConcurrentQueue<LogMessage> QueuedLogs = new ConcurrentQueue<LogMessage>();

        static Logger()
        {
            var timer = new System.Timers.Timer(TimerInterval);
            timer.Elapsed += TimerElapsed;
            timer.Start();
        }

        public static void Log(string message, LogSeverity severity = LogSeverity.Information)
        {
            // get caller name
            var stack = new StackTrace();
            var callername = "";
            for (int i = 0; i < stack.FrameCount; i++)
            {
                var m = stack.GetFrame(i).GetMethod();
                var n = m.DeclaringType.FullName;

                if (n.StartsWith("CryCrawler"))
                {
                    if (n.Contains("Logger")) continue;

                    var name = "";
                    if (n.Contains('+'))
                    {
                        var match = Regex.Match(m.DeclaringType.FullName, @"\.(\w*?)\+");
                        name = match.Groups[1].Value;
                    }
                    else name = m.DeclaringType.Name;

                    callername = name;
                    break;
                }
            }

            // enqueue log
            QueuedLogs.Enqueue(new LogMessage(message, severity, callername));
        }

        private static void TimerElapsed(object sender, ElapsedEventArgs e)
        {
            if (QueuedLogs.IsEmpty) return;
            if (QueuedLogs.TryDequeue(out LogMessage msg) == false) return;
            if (!DebugMode && msg.Severity == LogSeverity.Debug) return;

            // set Console color if log not informational
            var targetColor = ConsoleColor.Gray;
            if (msg.Severity == LogSeverity.Error) targetColor = ConsoleColor.Red;
            else if (msg.Severity == LogSeverity.Warning) targetColor = ConsoleColor.Yellow;
            else if (msg.Severity == LogSeverity.Debug) targetColor = ConsoleColor.DarkGray;

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

            
            Console.ForegroundColor = targetColor;
            Console.Write($"[{msg.LogTime.ToString("dd.MM HH:mm:ss")}] {severityText, -5} ");

            //if (DebugMode)
            //{
                const int length = 15;
                Console.ForegroundColor = targetColor;
                Console.Write($"{msg.Caller.Limit(length - 1) ?? "-",-length}");
            //}

            Console.ForegroundColor = targetColor;
            Console.WriteLine($"{msg.Message}");
            

            /*
            Console.ForegroundColor = targetColor;
            Console.WriteLine($"[{msg.LogTime.ToString("dd.MM.yyyy HH:mm:ss.ffff")}] " +
                $"{severityText} - {msg.Message}");*/

            // reset color if log was not informational
            if (msg.Severity != LogSeverity.Information) Console.ResetColor();
        }

        private struct LogMessage
        {
            public readonly string Caller;
            public readonly string Message;
            public readonly LogSeverity Severity;
            public readonly DateTime LogTime;
            public LogMessage(string message, LogSeverity severity, string caller = null)
            {
                Caller = caller;
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
