using System;
using System.IO;
using System.Text;
using CryCrawler.Network;
using System.Net.Security;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Net;

namespace CryCrawler.Security
{
    public static class SecurityUtils
    {
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
                        clientId = Extensions.GenerateRandomPathSafeString(20);
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

        /// <summary>
        /// Establish SSL stream between server and client. This should be called by the server.
        /// </summary>
        /// <param name="stream">Stream to be used by SSL stream</param>
        /// <param name="serverCertificate">Server certificate</param>
        public static SslStream ServerEstablishSSL(Stream stream, X509Certificate2 serverCertificate)
        {
            var ssl = new SslStream(stream, true, (a, b, c, d) => true);

            ssl.AuthenticateAsServer(serverCertificate, false, SslProtocols.None, false);

            return ssl;
        }

        /// <summary>
        /// Establish SSL stream between server and client. This should be called by the client.
        /// </summary>
        /// <param name="stream">Stream to be used by SSL stream</param>
        public static SslStream ClientEstablishSSL(Stream stream)
        {
            var ssl = new SslStream(stream, true, (a, b, c, d) => true);

            ssl.AuthenticateAsClient("", null, SslProtocols.None, false);

            return ssl;
        }

        /// <summary>
        /// Build a self signed certificate for use in SSL communication
        /// </summary>
        /// <param name="certName">Name of certificate</param>
        /// <param name="password">Password for certificate</param>
        /// <returns></returns>
        public static X509Certificate2 BuildSelfSignedCertificate(string certName, string password = "")
        {
            SubjectAlternativeNameBuilder sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddIpAddress(IPAddress.Loopback);
            sanBuilder.AddIpAddress(IPAddress.IPv6Loopback);
            sanBuilder.AddDnsName("localhost");
            sanBuilder.AddDnsName(Environment.MachineName);

            X500DistinguishedName distinguishedName = new X500DistinguishedName($"CN={certName}");

            using (RSA rsa = RSA.Create(2048))
            {
                var request = new CertificateRequest(distinguishedName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

                request.CertificateExtensions.Add(
                    new X509KeyUsageExtension(X509KeyUsageFlags.DataEncipherment | X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DigitalSignature, false));


                request.CertificateExtensions.Add(
                   new X509EnhancedKeyUsageExtension(
                       new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));

                request.CertificateExtensions.Add(sanBuilder.Build());

                var certificate = request.CreateSelfSigned(new DateTimeOffset(DateTime.UtcNow.AddDays(-1)), new DateTimeOffset(DateTime.UtcNow.AddDays(3650)));
                certificate.FriendlyName = certName;

                return new X509Certificate2(certificate.Export(X509ContentType.Pfx, password), password, X509KeyStorageFlags.MachineKeySet);
            }
        }
    }

    public class IncorrectPasswordException : Exception
    {
        public IncorrectPasswordException(string msg) : base(msg) { }
    }
}
