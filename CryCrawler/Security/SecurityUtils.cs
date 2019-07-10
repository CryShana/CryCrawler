using System;
using System.Text;
using System.Security.Cryptography;
using CryCrawler.Network;
using System.Collections.Generic;

namespace CryCrawler.Security
{
    public static class SecurityUtils
    {
        static readonly List<char> ValidPathChars;
        static SecurityUtils()
        {
            ValidPathChars = new List<char>();

            // add all 10 possible numbers
            for (byte i = 48; i <= 57; i++) ValidPathChars.Add((char)i);
            // add all 26 possible uppercase letters
            for (byte i = 65; i <= 90; i++) ValidPathChars.Add((char)i);
            // add all 26 possible lowercase letters
            for (byte i = 97; i <= 122; i++) ValidPathChars.Add((char)i);
        }

        public static string GetHash(string text) => string.IsNullOrEmpty(text) ? null :
            Convert.ToBase64String(SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(text)));

        /// <summary>
        /// Do handshake between client and server. Password is validated and client id is exchanged.
        /// </summary>
        /// <returns>Client ID that was exchanged</returns>
        public static string DoHandshake(NetworkMessageHandler<NetworkMessage> messageHandler, string passwordHash, bool asClient, 
            string existingClientId = null, Predicate<string> clientIdExists = null, Predicate<string> clientIdValid = null)
        {
            if (asClient)
            {
                // send JOIN request
                messageHandler.SendMessage(new NetworkMessage(NetworkMessageType.Join, new[] { passwordHash, existingClientId }));

                // wait for response
                var response = messageHandler.WaitForResponse().Result;
                if (response.MessageType != NetworkMessageType.Accept) throw new InvalidOperationException($"Rejected join! Got '{response.MessageType}', expected '{NetworkMessageType.Accept}'");

                // read client Id
                var id = response.Data as string;
                if (string.IsNullOrEmpty(id)) throw new InvalidOperationException("Server did not reply with valid client ID!");

                // send OK
                messageHandler.SendMessage(new NetworkMessage(NetworkMessageType.OK));

                return id;
            }
            else
            {
                // wait for JOIN request
                var response = messageHandler.WaitForResponse().Result;
                if (response.MessageType != NetworkMessageType.Join) throw new InvalidOperationException($"Invalid client response! Got '{response.MessageType}', expected '{NetworkMessageType.Join}'");

                var array = response.Data as object[];

                if (string.IsNullOrEmpty(passwordHash) == false)
                {
                    // check password
                    var password = array[0] as string;
                    if (passwordHash != password) throw new IncorrectPasswordException("Invalid password!");
                }

                // check client id
                var clientId = array == null ? null : array[1] as string;

                // if client id invalid, generate new one
                if (string.IsNullOrEmpty(clientId) || clientIdValid?.Invoke(clientId) != true)
                {
                    do
                    {
                        clientId = GenerateRandomSafeString(20);
                    } while (clientIdExists?.Invoke(clientId) != false);
                }                              

                // send ACCEPT 
                messageHandler.SendMessage(new NetworkMessage(NetworkMessageType.Accept, clientId));

                // wait for OK
                response = messageHandler.WaitForResponse().Result;
                if (response.MessageType != NetworkMessageType.OK) throw new InvalidOperationException($"Invalid client response! Got '{response.MessageType}', expected '{NetworkMessageType.OK}'");

                return clientId;
            }
        }

        public static string GenerateRandomSafeString(int length)
        {
            string filename = "";
            int count = ValidPathChars.Count;

            for (int i = 0; i < length; i++) filename += ValidPathChars[RandomNumberGenerator.GetInt32(0, count)];

            return filename;
        }
    }

    public class IncorrectPasswordException : Exception
    {
        public IncorrectPasswordException(string msg) : base(msg) { }
    }
}
