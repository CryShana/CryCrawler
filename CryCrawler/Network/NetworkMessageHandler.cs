using MessagePack;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CryCrawler.Network
{
    public class NetworkMessageHandler<T>
    {
        public Stream UnderlyingStream { get; }
        public event EventHandler<T> MessageReceived;
        public event EventHandler<Exception> ExceptionThrown;

        private TaskCompletionSource<T> messageTask = new TaskCompletionSource<T>();
        private Action<T> callback;
        private bool disposed;

        public NetworkMessageHandler(Stream stream)
        {
            UnderlyingStream = stream;
            new Task(() => handleReceiving(), TaskCreationOptions.LongRunning).Start();
        }

        public NetworkMessageHandler(Stream stream, Action<T> callback) : this(stream) => this.callback = callback;

        public void Dispose() => disposed = true;
        public void SendMessage(T message)
        {
            if (disposed) throw new ObjectDisposedException("Object disposed!");

            // use any serializer you want
            var buffer = MessagePackSerializer.Serialize(message);
            var lenBuffer = BitConverter.GetBytes(buffer.LongLength);
            UnderlyingStream.Write(lenBuffer);
            UnderlyingStream.Write(buffer);
        }

        public async Task<T> WaitForResponse(int timeout = 3000)
        {
            if (disposed) throw new ObjectDisposedException("Object disposed!");

            await Task.WhenAny(Task.Delay(timeout), messageTask.Task);

            if (disposed) throw new ObjectDisposedException("Object disposed!");

            if (messageTask.Task.IsCompletedSuccessfully)
            {
                var res = messageTask.Task.Result;
                messageTask = new TaskCompletionSource<T>();
                return res;
            }
            else throw new TimeoutException();
        }

        void handleReceiving()
        {
            while (!disposed)
            {
                try
                {
                    // wait for data
                    var buffer = readUntilSatisfied(UnderlyingStream, sizeof(long));
                    var contentLength = BitConverter.ToInt64(buffer);
                    var data = readUntilSatisfied(UnderlyingStream, contentLength);
                    var obj = MessagePackSerializer.Deserialize<T>(data);

                    if (disposed) break;

                    // report data
                    MessageReceived?.Invoke(this, obj);
                    messageTask?.SetResult(obj);
                    callback?.Invoke(obj);
                }
                catch (Exception ex)
                {
                    ExceptionThrown?.Invoke(this, ex);
                    Task.Delay(50).Wait();
                }
            }
        }
        byte[] readUntilSatisfied(Stream stream, long count)
        {
            byte[] bfr = new byte[count];
            var offset = 0;
            while (offset < count)
            {
                if (disposed) return null;

                var read = stream.Read(bfr, offset, (int)(count - offset));
                if (read < 0) throw new InvalidOperationException("Stream closed!");
                offset += read;
            }
            return bfr;
        }
    }
}
