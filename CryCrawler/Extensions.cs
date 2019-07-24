using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text.RegularExpressions;

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

        public static T Deserialize<T>(this IDictionary<object, object> values)
        {
            var type = typeof(T);
            var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);

            var instance = Activator.CreateInstance<T>();
            foreach (var p in properties)
            {
                var ptype = p.PropertyType;

                var name = p.Name;
                if (values.ContainsKey(name))
                {
                    object val = values[name];
                    if (ReferenceEquals(val, null))
                    {
                        p.SetValue(instance, null);
                        continue;
                    }

                    var valtype = val.GetType();

                    // if type of value is not same as property type - check if class is nested and convert value to correct one
                    if (ptype != valtype)
                    {
                        try
                        {
                            try
                            {
                                // check if type is convertible to the other one directly
                                p.SetValue(instance, val);
                            }
                            catch
                            {
                                // check if we have an array of objects
                                if (valtype.Name == "Object[]")
                                {
                                    // TODO: can implement for other types too, but no need
                                    val = ((object[])val).Cast<string>();
                                    val = Activator.CreateInstance(ptype, val);

                                    p.SetValue(instance, val);
                                    continue;
                                }
                                else if (ptype.Name == "Int32")
                                {
                                    p.SetValue(instance, val.AsInteger());
                                    continue;
                                }

                                throw;
                            }
                        }
                        catch (Exception ex)
                        {
                            if (valtype.FullName.StartsWith("System.Collections.Generic.Dictionary") &&
                                valtype.GenericTypeArguments.Count(x => x.Name == "Object") == 2)
                            {
                                var dic = ((IDictionary<object, object>)val);

                                // deserialize it again
                                MethodInfo m = typeof(Extensions).GetMethod("Deserialize");
                                MethodInfo typed = m.MakeGenericMethod(ptype);
                                val = typed.Invoke(dic, new object[] { dic });
                            }
                            else throw new InvalidOperationException($"Invalid value for property '{name}' (expected {ptype.Name}, got {valtype.Name}) [Inner exception: {ex.Message}]");
                        }
                    }

                    // set value
                    p.SetValue(instance, val);
                }
            }

            return instance;
        }

        public static int AsInteger(this object obj)
        {
            var type = obj.GetType();

            if (type.Name == "UInt16") return (ushort)obj;
            else if (type.Name == "Int16") return (short)obj;
            else if (type.Name == "UInt32") return (int)((uint)obj);
            else throw new InvalidCastException("Invalid integer type!");
        }


        // HELPER METHODS

        /// <summary>
        /// Based on domain whitelist and blacklist, decides if URL is allowed to be added to backlog
        /// </summary>
        public static bool IsUrlWhitelisted(string url, WorkerConfiguration config)
        {
            // check if url ends with a slash, otherwise add it
            var domain = GetDomainName(url, out _);

            // reject url if domain is empty
            if (string.IsNullOrEmpty(domain)) return false;

            // check whitelist first
            if (config.DomainWhitelist.Count > 0)
            {
                foreach (var w in config.DomainWhitelist)
                {
                    // if domain contains any of the words, automatically accept it
                    if (domain.Contains(w.ToLower())) return true;
                }

                // if whitelist is not empty, any non-matching domains are rejected!
                return false;
            }

            // check blacklist second
            foreach (var w in config.DomainBlacklist)
            {
                // if domain contains any of the blacklisted words, automatically reject it
                if (domain.Contains(w.ToLower())) return false;
            }

            // accept url if it doesn't contain any blacklisted word
            return true;
        }

        public static string GetDomainName(string url, out string protocol, bool withProtocol = false)
        {
            // check if url ends with a slash, otherwise add it
            if (url.Count(x => x == '/') == 2) url += '/';

            try
            {
                // should be case insensitive!
                var match = Regex.Match(url, @"(http[s]?):\/\/(.*?)\/");
                protocol = match.Groups[1].Value;
                var domain = match.Groups[2].Value;

                if (withProtocol == false) return domain;
                else return $"{protocol}://{domain}";
            }
            catch
            {
                protocol = null;
                return null;
            }
        }

        /// <summary>
        /// Get's the relative path to file
        /// </summary>
        /// <param name="absolutePath">Absolute path of file</param>
        /// <param name="ignoreDownloadsFolder">Ignore downloads folder</param>
        /// <returns>Relative path</returns>
        public static string GetRelativeFilePath(string absolutePath, WorkerConfiguration config, bool ignoreDownloadsFolder = true)
        {
            var relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), absolutePath);
            if (relative.StartsWith(config.DownloadsPath))
                relative = relative.Substring(config.DownloadsPath.Length + 1);

            return relative;
        }

        /// <summary>
        /// Removes any invalid path characters from given path
        /// </summary>
        /// <param name="path">Original path</param>
        /// <returns>Modified paths without any invalid path characters</returns>
        public static string FixPath(string path)
        {
            if (path == null) return "";

            var chars = Path.GetInvalidPathChars();
            int index = path.IndexOfAny(chars);
            while (index >= 0)
            {
                path = path.Remove(index, 1);
                index = path.IndexOfAny(chars);
            }

            return path;
        }
    }
}
