using System;
using System.Threading;
using System.Threading.Tasks;

namespace CryCrawler
{
    public static class ConsoleHost
    {
        /// <summary>
        /// Block the calling thread until shutdown is triggered via Ctrl+C or SIGTERM.
        /// </summary>
        public static void WaitForShutdown() => WaitForShutdownAsync().GetAwaiter().GetResult();

        /// <summary>
        /// Returns a Task that completes when shutdown is triggered via the given token, Ctrl+C or SIGTERM.
        /// </summary>
        /// <param name="token">The token to trigger shutdown.</param>
        public static async Task WaitForShutdownAsync(CancellationToken token = default)
        {
            var done = new ManualResetEventSlim(false);
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                AttachCtrlcSigtermShutdown(cts, done);
                await WaitForTokenShutdownAsync(cts.Token);
                done.Set();
            }
        }

        private static void AttachCtrlcSigtermShutdown(CancellationTokenSource cts, ManualResetEventSlim resetEvent, string shutdownMessage = null)
        {
            void ShutDown()
            {
                if (!cts.IsCancellationRequested)
                {
                    if (!string.IsNullOrWhiteSpace(shutdownMessage))
                        Console.WriteLine(shutdownMessage);
                    
                    try
                    {
                        // attempt to cancel token
                        cts.Cancel();
                    }
                    catch (ObjectDisposedException) { }
                }

                // wait for the "WaitForTokenShutdownAsync" to detect token cancellation and do cleanup
                resetEvent.Wait();
            };
                       
            AppDomain.CurrentDomain.ProcessExit += (a, b) => ShutDown();
            Console.CancelKeyPress += (a, b) =>
            {
                ShutDown();

                b.Cancel = true;
            };
        }

        private static async Task WaitForTokenShutdownAsync(CancellationToken token)
        {
            // this function only waits for Token to be cancelled
            var waitForStop = new TaskCompletionSource<object>();
            token.Register(obj =>
            {
                // on token cancel it will mark the "waitForStop" task as Completed
                var tcs = (TaskCompletionSource<object>)obj;
                tcs.TrySetResult(null);
            }, waitForStop);

            // wait until Token is cancelled from Shutdown() method or otherwise
            await waitForStop.Task;
        }
    }
}
