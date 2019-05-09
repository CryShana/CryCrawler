using System;
using System.Text;
using System.Security.Cryptography;
using CryCrawler.Network;

namespace CryCrawler.Security
{
    public static class SecurityUtils
    {
        public static string GetHash(string text) => Convert.ToBase64String(SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(text)));   
        
        public static void DoHandshake(NetworkMessageHandler<NetworkMessage> messageHandler, string passwordHash)
        {
            // wait for JOIN request
            var response = messageHandler.WaitForResponse().Result;
            if (response.MessageType != NetworkMessageType.Join) throw new InvalidOperationException($"Invalid client response! Got '{response.MessageType}', expected '{NetworkMessageType.Join}'");

            // check password
            var password = response.Data as string;
            if (passwordHash != GetHash(password)) throw new IncorrectPasswordException("Invalid password!");

            // send ACCEPT 
            messageHandler.SendMessage(new NetworkMessage(NetworkMessageType.Accept));

            // wait for OK
            response = messageHandler.WaitForResponse().Result;
            if (response.MessageType != NetworkMessageType.OK) throw new InvalidOperationException($"Invalid client response! Got '{response.MessageType}', expected '{NetworkMessageType.OK}'");
        }
    }

    public class IncorrectPasswordException : Exception
    {
        public IncorrectPasswordException(string msg) : base(msg) { }
    }
}
