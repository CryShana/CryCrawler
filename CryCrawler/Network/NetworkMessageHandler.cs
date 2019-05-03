using MessagePack;
using System;
using System.IO;
using System.Threading.Tasks;

namespace CryCrawler.Network
{
    public class NetworkMessageHandler<T>
    {
        public Stream UnderlyingStream { get; }
        public event EventHandler<T> MessageReceived;

        private TaskCompletionSource<T> messageTask = new TaskCompletionSource<T>();
        private Action<T> callback;

        public NetworkMessageHandler(Stream stream)
        {
            UnderlyingStream = stream;
            new Task(() => handleReceiving(), TaskCreationOptions.LongRunning).Start();
        }

        public NetworkMessageHandler(Stream stream, Action<T> callback) : this(stream) => this.callback = callback;

        public void SendMessage(T message)
        {
            var buffer = MessagePackSerializer.Serialize(message);
            var lenBuffer = BitConverter.GetBytes(buffer.LongLength);
            UnderlyingStream.Write(lenBuffer);
            UnderlyingStream.Write(buffer);
        }

        public async Task<T> WaitForResponse(int timeout = 3000)
        {
            var res = await messageTask.Task;
            messageTask = new TaskCompletionSource<T>();
            return res;
        }

        void handleReceiving()
        {
            while (true)
            {
                try
                {
                    // wait for data
                    var buffer = readUntilSatisfied(UnderlyingStream, sizeof(long));
                    var contentLength = BitConverter.ToInt64(buffer);
                    var data = readUntilSatisfied(UnderlyingStream, contentLength);
                    var obj = MessagePackSerializer.Deserialize<T>(data);

                    // report data
                    MessageReceived?.Invoke(this, obj);
                    messageTask?.SetResult(obj);
                    callback?.Invoke(obj);
                }
                catch
                {
                }
            }
        }
        byte[] readUntilSatisfied(Stream stream, long count)
        {
            byte[] bfr = new byte[count];
            var offset = 0;
            while (offset < count)
            {
                var read = stream.Read(bfr, offset, (int)(count - offset));
                if (read < 0) throw new InvalidOperationException("Stream closed!");
                offset += read;
            }
            return bfr;
        }
    }
}
