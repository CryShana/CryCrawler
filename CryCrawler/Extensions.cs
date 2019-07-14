using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;

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

                                throw;
                            }
                        }
                        catch
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
                            else throw new InvalidOperationException($"Invalid value for property '{name}' (expected {ptype.Name}, got {valtype.Name})");
                        }
                    }

                    // set value
                    p.SetValue(instance, val);
                }
            }

            return instance;
        }
    }
}
