using System;
using System.Net.Sockets;

namespace CryCrawler
{
    public static class Extensions
    {
        /// <summary>
        /// Combine all messages from inner exceptions
        /// </summary>
        /// <param name="ex">Main exception</param>
        /// <returns>Combined messages of all exceptions</returns>
        public static string GetDetailedMessage(this Exception ex)
        {
            const int depthLimit = 10;

            var i = 0;
            var msg = "";
            Exception exc = ex;
            while (exc != null && i < depthLimit)
            {
                i++;
                msg += exc.Message + " ";
                exc = exc.InnerException;              
            }

            return msg; 
        }

        public static void ProperlyClose(this TcpClient client)
        {
            if (client?.Connected == true)
            {
                client.GetStream().Close();
                client?.Close();
            }

            client?.Dispose();
        }

        public static string Limit(this string text, int size) => text.Length > size ? text.Substring(0, size) : text;
    }
}
