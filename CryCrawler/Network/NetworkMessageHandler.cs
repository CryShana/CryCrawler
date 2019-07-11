using System;
using System.IO;
using MessagePack;
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
        public bool Disposed { get; private set; }

        public NetworkMessageHandler(Stream stream)
        {
            UnderlyingStream = stream;
            new Task(() => handleReceiving(), TaskCreationOptions.LongRunning).Start();
        }

        public NetworkMessageHandler(Stream stream, Action<T> callback) : this(stream) => this.callback = callback;

        public void Dispose()
        {
            Disposed = true;
            UnderlyingStream?.Close();
        }

        public void SendMessage(T message)
        {
            if (Disposed) throw new ObjectDisposedException("Object disposed!");

            // use any serializer you want
            var buffer = MessagePackSerializer.Serialize(message);
            var lenBuffer = BitConverter.GetBytes(buffer.LongLength);
            UnderlyingStream.Write(lenBuffer);
            UnderlyingStream.Write(buffer);
        }

        public async Task<T> WaitForResponse(int timeout = 3000)
        {
            if (Disposed) throw new ObjectDisposedException("Object disposed!");

            await Task.WhenAny(Task.Delay(timeout), messageTask.Task);

            if (Disposed) throw new ObjectDisposedException("Object disposed!");

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
            while (!Disposed)
            {
                try
                {
                    // wait for data
                    var buffer = readUntilSatisfied(UnderlyingStream, sizeof(long));
                    var contentLength = BitConverter.ToInt64(buffer);
                    var data = readUntilSatisfied(UnderlyingStream, contentLength);
                    var obj = MessagePackSerializer.Deserialize<T>(data);

                    if (Disposed) break;

                    // report data
                    MessageReceived?.Invoke(this, obj);

                    if (messageTask?.TrySetResult(obj) == false)
                    {
                        messageTask = new TaskCompletionSource<T>();
                    }

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
                if (Disposed) return null;

                var read = stream.Read(bfr, offset, (int)(count - offset));
                if (read < 0) throw new InvalidOperationException("Stream closed!");
                offset += read;
            }
            return bfr;
        }
    }
}
