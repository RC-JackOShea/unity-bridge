using System;

namespace UnityBridge
{
    /// <summary>
    /// Static utility methods callable via the /execute endpoint.
    /// All future tool methods live here. Methods should be public static
    /// and return a JSON string when returning data.
    /// </summary>
    public static class BridgeTools
    {
        /// <summary>
        /// Simple connectivity test. Returns a pong message with timestamp.
        /// </summary>
        public static string Ping()
        {
            var timestamp = DateTime.UtcNow.ToString("o");
            return "{\"message\":\"pong\",\"timestamp\":\"" + timestamp + "\"}";
        }

        /// <summary>
        /// Adds two integers. Demonstrates argument passing through /execute.
        /// </summary>
        public static string Add(int a, int b)
        {
            return "{\"result\":" + (a + b) + "}";
        }
    }
}
