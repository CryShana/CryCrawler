using System;
using System.Text;
using System.Security.Cryptography;
using CryCrawler.Network;

namespace CryCrawler.Security
{
    public static class SecurityUtils
    {
        public static string GetHash(string text) => string.IsNullOrEmpty(text) ? null :
            Convert.ToBase64String(SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(text)));

        public static void DoHandshake(NetworkMessageHandler<NetworkMessage> messageHandler, string passwordHash, bool asClient)
        {
            if (asClient)
            {
                // send JOIN request
                messageHandler.SendMessage(new NetworkMessage(NetworkMessageType.Join, passwordHash));

                // wait for response
                var response = messageHandler.WaitForResponse().Result;
                if (response.MessageType != NetworkMessageType.Accept) throw new InvalidOperationException($"Rejected join! Got '{response.MessageType}', expected '{NetworkMessageType.Accept}'");

                // send OK
                messageHandler.SendMessage(new NetworkMessage(NetworkMessageType.OK));
            }
            else
            {
                // wait for JOIN request
                var response = messageHandler.WaitForResponse().Result;
                if (response.MessageType != NetworkMessageType.Join) throw new InvalidOperationException($"Invalid client response! Got '{response.MessageType}', expected '{NetworkMessageType.Join}'");

                // check password
                var password = response.Data as string;
                if (passwordHash != password) throw new IncorrectPasswordException("Invalid password!");

                // send ACCEPT 
                messageHandler.SendMessage(new NetworkMessage(NetworkMessageType.Accept));

                // wait for OK
                response = messageHandler.WaitForResponse().Result;
                if (response.MessageType != NetworkMessageType.OK) throw new InvalidOperationException($"Invalid client response! Got '{response.MessageType}', expected '{NetworkMessageType.OK}'");
            }
        }
    }

    public class IncorrectPasswordException : Exception
    {
        public IncorrectPasswordException(string msg) : base(msg) { }
    }
}
