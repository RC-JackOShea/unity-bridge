using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace UnityBridge
{
    /// <summary>
    /// Unity Bridge Server - HTTP API for Unity Editor integration
    /// Provides REST API for external tools to interact with Unity Editor
    /// Place this script in Assets/Editor/ folder
    /// </summary>
    [InitializeOnLoad]
    public static class UnityBridgeServer
    {
        private const int SERVER_PORT = 5556;
        private static HttpListener httpListener;
        private static Thread serverThread;
        private static bool isRunning = false;
        
        private static List<LogEntry> logs = new List<LogEntry>();
        private static List<CompilationResult> compilationHistory = new List<CompilationResult>();
        private static bool isCompiling = false;
        private static CompilationResult currentCompilation;
        private static readonly object lockObject = new object();
        
        private static Queue<Action> pendingUnityActions = new Queue<Action>();
        
        [Serializable]
        public class LogEntry
        {
            public string message;
            public string stackTrace;
            public string type;
            public string timestamp;
            public string source;
        }
        
        [Serializable]
        public class CompilationResult
        {
            public string status;
            public bool success;
            public string[] errors;
            public string[] warnings;
            public LogEntry[] logs;
            public string startTime;
            public string endTime;
            public double duration;
        }
        
        [Serializable]
        public class ApiResponse
        {
            public string status;
            public string message;
            public object data;
            
            public string ToJson()
            {
                var jsonString = $"{{\"status\":\"{status}\",\"message\":\"{EscapeJsonString(message)}\"";
                
                if (data != null)
                {
                    string dataJson;
                    if (data is string)
                    {
                        dataJson = $"\"{EscapeJsonString(data.ToString())}\"";
                    }
                    else
                    {
                        dataJson = JsonUtility.ToJson(data);
                        if (dataJson == "{}")
                        {
                            // Handle arrays and complex objects
                            if (data is System.Collections.IEnumerable enumerable && !(data is string))
                            {
                                var items = new List<string>();
                                foreach (var item in enumerable)
                                {
                                    items.Add(JsonUtility.ToJson(item));
                                }
                                dataJson = "[" + string.Join(",", items.ToArray()) + "]";
                            }
                        }
                    }
                    jsonString += $",\"data\":{dataJson}";
                }
                
                jsonString += "}";
                return jsonString;
            }
            
            private string EscapeJsonString(string str)
            {
                if (str == null) return "";
                return str.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
            }
        }
        
        static UnityBridgeServer()
        {
            Initialize();
        }
        
        private static void Initialize()
        {
            // Subscribe to log events
            Application.logMessageReceived += OnLogMessageReceived;
            
            // Subscribe to compilation events
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
            
            // Subscribe to Unity's update loop to process Unity API calls only
            EditorApplication.update += ProcessPendingUnityActions;
            
            // Start HTTP server
            StartServer();
            
            Debug.Log($"[UnityBridge] Started on http://localhost:{SERVER_PORT}");
        }
        
        private static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            lock (lockObject)
            {
                var logEntry = new LogEntry
                {
                    message = condition,
                    stackTrace = stackTrace,
                    type = type.ToString(),
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    source = "Unity Console"
                };
                
                logs.Add(logEntry);
                
                // Also add to current compilation if active
                if (isCompiling && currentCompilation != null)
                {
                    var compilationLogs = new List<LogEntry>(currentCompilation.logs ?? new LogEntry[0]);
                    compilationLogs.Add(logEntry);
                    currentCompilation.logs = compilationLogs.ToArray();
                }
            }
        }
        
        private static void OnCompilationStarted(object obj)
        {
            lock (lockObject)
            {
                isCompiling = true;
                currentCompilation = new CompilationResult
                {
                    status = "compiling",
                    success = false,
                    errors = new string[0],
                    warnings = new string[0],
                    logs = new LogEntry[0],
                    startTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    endTime = null,
                    duration = 0
                };
            }
        }
        
        private static void OnCompilationFinished(object obj)
        {
            lock (lockObject)
            {
                if (currentCompilation != null)
                {
                    var endTime = DateTime.Now;
                    var startTime = DateTime.Parse(currentCompilation.startTime);
                    
                    currentCompilation.endTime = endTime.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    currentCompilation.duration = (endTime - startTime).TotalSeconds;
                    
                    // Messages are collected from assemblyCompilationFinished events
                    // Set final status based on collected errors
                    currentCompilation.success = currentCompilation.errors.Length == 0;
                    currentCompilation.status = currentCompilation.success ? "success" : "failed";
                    
                    compilationHistory.Add(currentCompilation);
                    
                    // Keep only last 10 compilations
                    if (compilationHistory.Count > 10)
                    {
                        compilationHistory.RemoveAt(0);
                    }
                }
                
                isCompiling = false;
            }
        }
        
        private static void OnAssemblyCompilationFinished(string assemblyName, UnityEditor.Compilation.CompilerMessage[] messages)
        {
            lock (lockObject)
            {
                if (isCompiling && currentCompilation != null)
                {
                    var errors = new List<string>(currentCompilation.errors ?? new string[0]);
                    var warnings = new List<string>(currentCompilation.warnings ?? new string[0]);
                    
                    foreach (var message in messages)
                    {
                        if (message.type == CompilerMessageType.Error)
                        {
                            errors.Add($"{message.file}:{message.line} - {message.message}");
                        }
                        else if (message.type == CompilerMessageType.Warning)
                        {
                            warnings.Add($"{message.file}:{message.line} - {message.message}");
                        }
                    }
                    
                    currentCompilation.errors = errors.ToArray();
                    currentCompilation.warnings = warnings.ToArray();
                }
            }
        }
        
        private static void StartServer()
        {
            if (isRunning) return;
            
            // Stop any existing server first
            StopServer();
            
            httpListener = new HttpListener();
            
            // Try binding to all interfaces first (requires admin)
            bool boundToAll = false;
            try
            {
                httpListener.Prefixes.Add($"http://*:{SERVER_PORT}/");
                httpListener.Start();
                isRunning = true;
                boundToAll = true;
                Debug.Log($"[UnityBridge] Started on *:{SERVER_PORT} (all interfaces - WSL compatible)");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UnityBridge] Could not bind to all interfaces: {e.Message}");
                
                // Fallback to localhost only
                try
                {
                    httpListener?.Stop();
                    httpListener = new HttpListener();
                    httpListener.Prefixes.Add($"http://localhost:{SERVER_PORT}/");
                    httpListener.Start();
                    isRunning = true;
                    Debug.Log($"[UnityBridge] Started on localhost:{SERVER_PORT} (localhost only)");
                }
                catch (Exception fallbackError)
                {
                    Debug.LogError($"[UnityBridge] Failed to start server: {fallbackError.Message}");
                    return;
                }
            }
            
            if (isRunning)
            {
                serverThread = new Thread(ServerLoop);
                serverThread.IsBackground = true;
                serverThread.Name = "UnityLogServerThread";
                serverThread.Start();
                
                if (boundToAll)
                {
                    Debug.Log("[UnityBridge] WSL connections supported");
                }
                else
                {
                    Debug.LogWarning("[UnityBridge] WSL connections NOT supported. Run Unity as Administrator to enable.");
                }
            }
        }
        
        private static void ServerLoop()
        {
            while (isRunning && httpListener != null && httpListener.IsListening)
            {
                try
                {
                    var context = httpListener.GetContext();
                    // Handle request immediately on background thread
                    ThreadPool.QueueUserWorkItem(_ => HandleRequest(context));
                }
                catch (ThreadAbortException)
                {
                    // Thread abort is expected during Unity recompilation - don't log as error
                    break;
                }
                catch (Exception e)
                {
                    if (isRunning)
                    {
                        Debug.LogError($"[UnityBridge] Server error: {e.Message}");
                    }
                }
            }
        }
        
        private static void ProcessPendingUnityActions()
        {
            lock (pendingUnityActions)
            {
                while (pendingUnityActions.Count > 0)
                {
                    var action = pendingUnityActions.Dequeue();
                    try
                    {
                        action();
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[UnityBridge] Unity action error: {e.Message}");
                    }
                }
            }
        }
        
        private static void QueueUnityAction(Action action)
        {
            lock (pendingUnityActions)
            {
                pendingUnityActions.Enqueue(action);
            }
        }
        
        private static void HandleRequest(object obj)
        {
            var context = (HttpListenerContext)obj;
            var request = context.Request;
            var response = context.Response;
            
            try
            {
                response.Headers.Add("Access-Control-Allow-Origin", "*");
                response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
                
                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = 200;
                    response.Close();
                    return;
                }
                
                var path = request.Url.AbsolutePath.ToLower();
                var method = request.HttpMethod;
                
                ApiResponse apiResponse = null;
                
                switch (path)
                {
                    case "/compile":
                        if (method == "POST")
                            apiResponse = HandleCompile();
                        break;
                    case "/logs":
                        if (method == "GET")
                            apiResponse = HandleGetLogs();
                        break;
                    case "/status":
                        if (method == "GET")
                            apiResponse = HandleGetStatus();
                        break;
                    case "/clear":
                        if (method == "POST")
                            apiResponse = HandleClear();
                        break;
                    case "/health":
                        if (method == "GET")
                            apiResponse = new ApiResponse { status = "ok", message = "Unity Bridge is running" };
                        break;
                }
                
                if (apiResponse == null)
                {
                    apiResponse = new ApiResponse { status = "error", message = "Endpoint not found" };
                    response.StatusCode = 404;
                }
                
                string json;
                try 
                {
                    json = apiResponse.ToJson();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[UnityBridge] JSON serialization error: {e.Message}");
                    // Fallback to basic Unity JSON serialization
                    json = JsonUtility.ToJson(apiResponse);
                }
                var buffer = Encoding.UTF8.GetBytes(json);
                
                response.ContentType = "application/json";
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.Close();
            }
            catch (Exception e)
            {
                Debug.LogError($"[UnityBridge] Request handling error: {e.Message}");
                try
                {
                    response.StatusCode = 500;
                    response.Close();
                }
                catch { }
            }
        }
        
        private static ApiResponse HandleCompile()
        {
            lock (lockObject)
            {
                if (isCompiling)
                {
                    return new ApiResponse 
                    { 
                        status = "in_progress", 
                        message = "Compilation already in progress",
                        data = new { 
                            compilationId = currentCompilation?.startTime,
                            isCompiling = true
                        }
                    };
                }
                
                // Clear logs for this compilation
                logs.Clear();
                
                // Queue Unity API calls to be executed on main thread when Unity updates
                QueueUnityAction(() => {
                    try
                    {
                        AssetDatabase.Refresh();
                        CompilationPipeline.RequestScriptCompilation();
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[UnityBridge] Compilation trigger error: {e.Message}");
                    }
                });
                
                return new ApiResponse 
                { 
                    status = "started", 
                    message = "Compilation queued successfully",
                    data = new { 
                        note = "Compilation will start when Unity processes the queue. Use /status endpoint to check progress.",
                        backgroundProcessing = true
                    }
                };
            }
        }
        
        private static ApiResponse HandleGetLogs()
        {
            lock (lockObject)
            {
                return new ApiResponse 
                { 
                    status = "success", 
                    message = $"Retrieved {logs.Count} logs",
                    data = logs.ToArray()
                };
            }
        }
        
        private static ApiResponse HandleGetStatus()
        {
            lock (lockObject)
            {
                var statusData = new {
                    isCompiling = isCompiling,
                    logCount = logs.Count,
                    compilationHistory = compilationHistory.ToArray(),
                    currentCompilation = currentCompilation,
                    serverRunning = isRunning,
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                };
                
                return new ApiResponse 
                { 
                    status = "success", 
                    message = $"Status retrieved - {logs.Count} logs, compiling: {isCompiling}",
                    data = statusData
                };
            }
        }
        
        private static ApiResponse HandleClear()
        {
            lock (lockObject)
            {
                logs.Clear();
                compilationHistory.Clear();
                
                return new ApiResponse 
                { 
                    status = "success", 
                    message = "Logs cleared" 
                };
            }
        }
        
        // Public API for Editor Window
        public static bool IsServerRunning()
        {
            return isRunning && httpListener != null && httpListener.IsListening;
        }
        
        public static bool IsCompiling()
        {
            return isCompiling;
        }
        
        public static int GetLogCount()
        {
            lock (lockObject)
            {
                return logs.Count;
            }
        }
        
        public static List<LogEntry> GetRecentLogs(int maxCount = 50)
        {
            lock (lockObject)
            {
                var recentLogs = new List<LogEntry>();
                int startIndex = Math.Max(0, logs.Count - maxCount);
                
                for (int i = startIndex; i < logs.Count; i++)
                {
                    recentLogs.Add(logs[i]);
                }
                
                return recentLogs;
            }
        }
        
        public static void StartServerManually()
        {
            StartServer();
            Debug.Log("[UnityBridge] Server started manually");
        }
        
        public static void StopServerManually()
        {
            StopServer();
        }
        
        public static void TriggerTestCompilation()
        {
            Debug.Log("[UnityBridge] Starting test compilation...");
            AssetDatabase.Refresh();
            CompilationPipeline.RequestScriptCompilation();
        }
        
        public static void ClearLogsManually()
        {
            lock (lockObject)
            {
                logs.Clear();
                compilationHistory.Clear();
                Debug.Log("[UnityBridge] Logs cleared manually");
            }
        }
        
        // Menu items for manual control
        [MenuItem("Tools/Unity Bridge/Start Server")]
        private static void StartServerMenuItem()
        {
            StartServer();
            Debug.Log("[UnityBridge] Server started manually");
        }
        
        [MenuItem("Tools/Unity Bridge/Stop Server")]
        private static void StopServer()
        {
            isRunning = false;
            
            try
            {
                httpListener?.Stop();
                httpListener?.Close();
            }
            catch (Exception) { /* Ignore cleanup errors */ }
            
            try
            {
                serverThread?.Abort();
                serverThread = null;
            }
            catch (Exception) { /* Ignore thread abort errors */ }
            
            httpListener = null;
            Debug.Log("[UnityBridge] Server stopped");
        }
        
        [MenuItem("Tools/Unity Bridge/Test Compile")]
        private static void TestCompile()
        {
            Debug.Log("[UnityBridge] Starting test compilation...");
            AssetDatabase.Refresh();
            CompilationPipeline.RequestScriptCompilation();
        }
        
        [MenuItem("Tools/Unity Bridge/Clear Logs")]
        private static void ClearLogs()
        {
            lock (lockObject)
            {
                logs.Clear();
                Debug.Log("[UnityBridge] Logs cleared");
            }
        }
    }
}