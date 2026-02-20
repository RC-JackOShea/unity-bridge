using System;
using System.Reflection;
using UnityEngine;

namespace UnityBridge
{
    /// <summary>
    /// Reflection-based method executor for the /execute endpoint.
    /// Resolves and invokes static methods by fully-qualified name.
    ///
    /// Method path format: "Namespace.Class.Method" (split on last '.' to get type + method)
    /// Args format: JSON array string, e.g. "[1,2]" or "["hello"]"
    /// </summary>
    public static class MethodExecutor
    {
        public static string Execute(string methodPath, string argsJson)
        {
            if (string.IsNullOrEmpty(methodPath))
                return "{\"success\":false,\"error\":\"Method path is empty\"}";

            // Split on last '.' to separate type from method name
            int lastDot = methodPath.LastIndexOf('.');
            if (lastDot <= 0 || lastDot >= methodPath.Length - 1)
                return "{\"success\":false,\"error\":\"Invalid method path format. Expected 'Namespace.Class.Method'\"}";

            string typeName = methodPath.Substring(0, lastDot);
            string methodName = methodPath.Substring(lastDot + 1);

            // Find type across all loaded assemblies
            Type type = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName);
                if (type != null) break;
            }

            if (type == null)
                return "{\"success\":false,\"error\":\"Type not found: " + EscapeJson(typeName) + "\"}";

            // Find static method
            MethodInfo method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null)
                return "{\"success\":false,\"error\":\"Static method not found: " + EscapeJson(methodName) + " on type " + EscapeJson(typeName) + "\"}";

            // Parse arguments
            ParameterInfo[] parameters = method.GetParameters();
            object[] args = ParseArgs(argsJson, parameters);

            if (args == null && parameters.Length > 0)
                return "{\"success\":false,\"error\":\"Failed to parse arguments. Expected " + parameters.Length + " args.\"}";

            if (args != null && args.Length != parameters.Length)
                return "{\"success\":false,\"error\":\"Argument count mismatch. Expected " + parameters.Length + ", got " + args.Length + "\"}";

            // Invoke
            try
            {
                object result = method.Invoke(null, args);

                if (method.ReturnType == typeof(void))
                {
                    return "{\"success\":true,\"result\":null}";
                }
                else if (method.ReturnType == typeof(string))
                {
                    // String returns are assumed to be JSON — embed directly
                    string strResult = (string)result;
                    if (string.IsNullOrEmpty(strResult))
                        return "{\"success\":true,\"result\":null}";
                    return "{\"success\":true,\"result\":" + strResult + "}";
                }
                else
                {
                    // Other types — serialize with JsonUtility
                    string json = JsonUtility.ToJson(result);
                    return "{\"success\":true,\"result\":" + json + "}";
                }
            }
            catch (TargetInvocationException tie)
            {
                var inner = tie.InnerException ?? tie;
                return "{\"success\":false,\"error\":\"" + EscapeJson(inner.GetType().Name + ": " + inner.Message) + "\"}";
            }
            catch (Exception ex)
            {
                return "{\"success\":false,\"error\":\"" + EscapeJson(ex.GetType().Name + ": " + ex.Message) + "\"}";
            }
        }

        /// <summary>
        /// Parses a JSON array string into typed arguments matching the method parameters.
        /// Supports: int, float, double, bool, string, long. Falls back to Convert.ChangeType.
        /// </summary>
        private static object[] ParseArgs(string argsJson, ParameterInfo[] parameters)
        {
            if (parameters.Length == 0)
                return new object[0];

            if (string.IsNullOrEmpty(argsJson) || argsJson.Trim() == "[]")
                return new object[0];

            // Trim outer brackets
            string trimmed = argsJson.Trim();
            if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                trimmed = trimmed.Substring(1, trimmed.Length - 2).Trim();

            if (string.IsNullOrEmpty(trimmed))
                return new object[0];

            // Split by commas, respecting quoted strings and nested structures
            var tokens = SplitJsonArray(trimmed);
            if (tokens.Length != parameters.Length)
                return tokens.Length == 0 ? new object[0] : new object[tokens.Length]; // Will trigger count mismatch

            object[] result = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                string token = tokens[i].Trim();
                Type targetType = parameters[i].ParameterType;

                try
                {
                    result[i] = ConvertArg(token, targetType);
                }
                catch (Exception)
                {
                    return null; // Signal parse failure
                }
            }

            return result;
        }

        private static string[] SplitJsonArray(string json)
        {
            var results = new System.Collections.Generic.List<string>();
            int depth = 0;
            bool inString = false;
            bool escape = false;
            int start = 0;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];

                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escape = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString) continue;

                if (c == '[' || c == '{') depth++;
                else if (c == ']' || c == '}') depth--;
                else if (c == ',' && depth == 0)
                {
                    results.Add(json.Substring(start, i - start));
                    start = i + 1;
                }
            }

            results.Add(json.Substring(start));
            return results.ToArray();
        }

        private static object ConvertArg(string token, Type targetType)
        {
            // Remove surrounding quotes for string values
            if (token.StartsWith("\"") && token.EndsWith("\""))
            {
                string unquoted = token.Substring(1, token.Length - 2);
                // Unescape basic JSON escapes
                unquoted = unquoted.Replace("\\\"", "\"").Replace("\\\\", "\\")
                    .Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t");

                if (targetType == typeof(string))
                    return unquoted;

                return Convert.ChangeType(unquoted, targetType);
            }

            // Handle null
            if (token == "null")
                return null;

            // Handle booleans
            if (targetType == typeof(bool))
                return token.Trim().ToLower() == "true";

            // Handle numeric types
            if (targetType == typeof(int))
                return int.Parse(token.Trim());
            if (targetType == typeof(long))
                return long.Parse(token.Trim());
            if (targetType == typeof(float))
                return float.Parse(token.Trim(), System.Globalization.CultureInfo.InvariantCulture);
            if (targetType == typeof(double))
                return double.Parse(token.Trim(), System.Globalization.CultureInfo.InvariantCulture);

            // Handle string without quotes (shouldn't happen in valid JSON, but be lenient)
            if (targetType == typeof(string))
                return token.Trim();

            // Fallback
            return Convert.ChangeType(token.Trim(), targetType, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string EscapeJson(string str)
        {
            if (str == null) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
