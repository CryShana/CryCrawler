using System;
using System.Text;
using System.Security.Cryptography;


namespace CryCrawler.Security
{
    public static class SecurityUtils
    {
        public static string GetHash(string text) => Convert.ToBase64String(SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(text)));      
    }
}
