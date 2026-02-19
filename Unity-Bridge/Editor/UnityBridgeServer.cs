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
        private static int nextLogId = 1;
        private static List<CompilationResult> compilationHistory = new List<CompilationResult>();
        private static bool isCompiling = false;
        private static CompilationResult currentCompilation;
        private static readonly object lockObject = new object();

        private static List<ExternalCommandEntry> externalCommands = new List<ExternalCommandEntry>();

        private static Queue<Action> pendingUnityActions = new Queue<Action>();

        // Play mode state tracking
        private static bool isPlayModeTransitioning = false;
        private static string playModeTargetState = null; // "playing" or "stopped"

        // --- Request DTOs ---

        [Serializable]
        public class PlayModeRequest
        {
            public string action; // "enter" or "exit"
        }

        [Serializable]
        public class ScreenshotRequest
        {
            public string format; // "file" or "base64"
            public string filePath;
            public int superSize; // optional, defaults to 1
        }

        [Serializable]
        public class InputRequest
        {
            public string action; // "tap", "hold", "drag", "swipe", "pinch", "multi_tap"
            public float x, y;
            public float startX, startY, endX, endY;
            public float duration;
            public float centerX, centerY;
            public float startDistance, endDistance;
            public int count;
            public float interval;
        }

        // --- Response data classes ---

        [Serializable]
        public class PlayModeStateData
        {
            public bool isPlaying;
            public bool isPaused;
            public string state; // "stopped", "entering", "playing", "exiting"
        }

        [Serializable]
        public class PlayModeTransitionData
        {
            public string previousState;
            public string targetState;
        }

        [Serializable]
        public class LogsResponseData
        {
            public LogEntry[] logs;
            public int lastId;
            public int totalCount;
        }

        [Serializable]
        public class ScreenshotResponseData
        {
            public string base64;
            public string filePath;
            public int width;
            public int height;
        }

        [Serializable]
        public class InputStartedData
        {
            public string inputId;
            public float estimatedDuration;
        }

        [Serializable]
        public class LogEntry
        {
            public int id;
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
        public class ExternalCommandEntry
        {
            public string command;
            public string timestamp;
            public string endpoint;
            public string method;
            public string userAgent;
            public bool success;
            public string response;
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

            // Subscribe to play mode state changes
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            // Subscribe to Unity's update loop to process Unity API calls only
            EditorApplication.update += ProcessPendingUnityActions;

            // Initialize input emulator on main thread
            InputEmulator.Initialize();

            // Start HTTP server
            StartServer();

            Debug.Log($"[UnityBridge] Started on http://localhost:{SERVER_PORT}");
        }

        // --- Synchronous QueueUnityAction<T> ---

        private static T QueueUnityAction<T>(Func<T> func, int timeoutMs = 5000)
        {
            T result = default;
            Exception caught = null;
            using (var done = new ManualResetEventSlim(false))
            {
                QueueUnityAction(() =>
                {
                    try
                    {
                        result = func();
                    }
                    catch (Exception e)
                    {
                        caught = e;
                    }
                    finally
                    {
                        done.Set();
                    }
                });

                if (!done.Wait(timeoutMs))
                {
                    throw new TimeoutException($"QueueUnityAction timed out after {timeoutMs}ms");
                }
            }

            if (caught != null)
                throw caught;

            return result;
        }

        // --- Utility ---

        private static string ReadRequestBody(HttpListenerRequest request)
        {
            if (!request.HasEntityBody)
                return "{}";

            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                return reader.ReadToEnd();
            }
        }

        // --- Play mode state change handler ---

        private static void OnPlayModeStateChanged(PlayModeStateChange stateChange)
        {
            switch (stateChange)
            {
                case PlayModeStateChange.ExitingEditMode:
                    isPlayModeTransitioning = true;
                    playModeTargetState = "playing";
                    break;
                case PlayModeStateChange.EnteredPlayMode:
                    isPlayModeTransitioning = false;
                    playModeTargetState = null;
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                    isPlayModeTransitioning = true;
                    playModeTargetState = "stopped";
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    isPlayModeTransitioning = false;
                    playModeTargetState = null;
                    break;
            }
        }

        private static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            lock (lockObject)
            {
                var logEntry = new LogEntry
                {
                    id = nextLogId++,
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
                Debug.Log($"[UnityBridge] Started on *:{SERVER_PORT} (all interfaces)");
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

                if (!boundToAll)
                {
                    Debug.LogWarning("[UnityBridge] Could not bind to all interfaces. Run Unity as Administrator to enable external connections.");
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
                var userAgent = request.UserAgent ?? "Unknown";

                ApiResponse apiResponse = null;

                switch (path)
                {
                    case "/compile":
                        if (method == "POST")
                            apiResponse = HandleCompile();
                        break;
                    case "/logs":
                        if (method == "GET")
                            apiResponse = HandleGetLogs(request);
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
                    case "/playmode":
                        if (method == "POST")
                            apiResponse = HandlePlayModePost(request);
                        else if (method == "GET")
                            apiResponse = HandlePlayModeGet();
                        break;
                    case "/screenshot":
                        if (method == "POST")
                            apiResponse = HandleScreenshot(request);
                        break;
                    case "/input":
                        if (method == "POST")
                            apiResponse = HandleInputPost(request);
                        else if (method == "GET")
                            apiResponse = HandleInputGet();
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

                // Track external command
                LogExternalCommand(path, method, userAgent, apiResponse?.status == "ok" || apiResponse?.status == "success" || apiResponse?.status == "started", apiResponse?.message ?? "");
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
                nextLogId = 1;

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

        // --- Enhanced Logs Handler ---

        private static ApiResponse HandleGetLogs(HttpListenerRequest request)
        {
            lock (lockObject)
            {
                var queryParams = request.QueryString;

                // Parse query parameters
                string levelFilter = queryParams["level"];
                int sinceId = 0;
                int limit = 0;

                if (queryParams["since"] != null)
                    int.TryParse(queryParams["since"], out sinceId);
                if (queryParams["limit"] != null)
                    int.TryParse(queryParams["limit"], out limit);

                // Parse level filter into a set
                HashSet<string> allowedLevels = null;
                if (!string.IsNullOrEmpty(levelFilter))
                {
                    allowedLevels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var level in levelFilter.Split(','))
                    {
                        var trimmed = level.Trim();
                        // Map common names to Unity LogType names
                        switch (trimmed.ToLower())
                        {
                            case "error":
                                allowedLevels.Add("Error");
                                allowedLevels.Add("Exception");
                                allowedLevels.Add("Assert");
                                break;
                            case "warning":
                                allowedLevels.Add("Warning");
                                break;
                            case "log":
                                allowedLevels.Add("Log");
                                break;
                            default:
                                allowedLevels.Add(trimmed);
                                break;
                        }
                    }
                }

                // Filter logs
                var filtered = new List<LogEntry>();
                foreach (var log in logs)
                {
                    if (sinceId > 0 && log.id <= sinceId)
                        continue;
                    if (allowedLevels != null && !allowedLevels.Contains(log.type))
                        continue;
                    filtered.Add(log);
                }

                // Apply limit
                if (limit > 0 && filtered.Count > limit)
                {
                    filtered = filtered.GetRange(filtered.Count - limit, limit);
                }

                int lastId = filtered.Count > 0 ? filtered[filtered.Count - 1].id : sinceId;

                var responseData = new LogsResponseData
                {
                    logs = filtered.ToArray(),
                    lastId = lastId,
                    totalCount = logs.Count
                };

                return new ApiResponse
                {
                    status = "success",
                    message = $"Retrieved {filtered.Count} logs",
                    data = responseData
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
                nextLogId = 1;
                compilationHistory.Clear();

                return new ApiResponse
                {
                    status = "success",
                    message = "Logs cleared"
                };
            }
        }

        // --- Play Mode Handlers ---

        private static ApiResponse HandlePlayModePost(HttpListenerRequest request)
        {
            var body = ReadRequestBody(request);
            var req = JsonUtility.FromJson<PlayModeRequest>(body);

            if (req == null || string.IsNullOrEmpty(req.action))
            {
                return new ApiResponse { status = "error", message = "Missing 'action' field. Use 'enter' or 'exit'." };
            }

            switch (req.action.ToLower())
            {
                case "enter":
                {
                    bool alreadyPlaying = QueueUnityAction(() => EditorApplication.isPlaying);
                    if (alreadyPlaying)
                    {
                        return new ApiResponse { status = "error", message = "Already in Play Mode" };
                    }

                    QueueUnityAction(() => { EditorApplication.isPlaying = true; });

                    var data = new PlayModeTransitionData
                    {
                        previousState = "stopped",
                        targetState = "playing"
                    };
                    return new ApiResponse { status = "started", message = "Entering Play Mode", data = data };
                }
                case "exit":
                {
                    bool currentlyPlaying = QueueUnityAction(() => EditorApplication.isPlaying);
                    if (!currentlyPlaying)
                    {
                        return new ApiResponse { status = "error", message = "Not in Play Mode" };
                    }

                    QueueUnityAction(() => { EditorApplication.isPlaying = false; });

                    var data = new PlayModeTransitionData
                    {
                        previousState = "playing",
                        targetState = "stopped"
                    };
                    return new ApiResponse { status = "started", message = "Exiting Play Mode", data = data };
                }
                default:
                    return new ApiResponse { status = "error", message = $"Unknown action '{req.action}'. Use 'enter' or 'exit'." };
            }
        }

        private static ApiResponse HandlePlayModeGet()
        {
            var stateData = QueueUnityAction(() =>
            {
                string state;
                if (isPlayModeTransitioning)
                {
                    state = playModeTargetState == "playing" ? "entering" : "exiting";
                }
                else if (EditorApplication.isPlaying)
                {
                    state = "playing";
                }
                else
                {
                    state = "stopped";
                }

                return new PlayModeStateData
                {
                    isPlaying = EditorApplication.isPlaying,
                    isPaused = EditorApplication.isPaused,
                    state = state
                };
            });

            return new ApiResponse
            {
                status = "success",
                message = $"Play mode state: {stateData.state}",
                data = stateData
            };
        }

        // --- Screenshot Handler ---

        private static ApiResponse HandleScreenshot(HttpListenerRequest request)
        {
            // Check play mode
            bool playing = QueueUnityAction(() => EditorApplication.isPlaying);
            if (!playing)
            {
                return new ApiResponse { status = "error", message = "Play Mode must be active to capture screenshots" };
            }

            var body = ReadRequestBody(request);
            var req = JsonUtility.FromJson<ScreenshotRequest>(body);

            string format = req?.format ?? "base64";
            int superSize = (req != null && req.superSize > 0) ? req.superSize : 1;

            if (format == "file")
            {
                string filePath = req?.filePath;
                if (string.IsNullOrEmpty(filePath))
                {
                    filePath = Path.Combine(Application.temporaryCachePath, $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                }

                var result = QueueUnityAction(() => ScreenshotCapture.CaptureToFile(filePath, superSize), 10000);

                if (!result.success)
                {
                    return new ApiResponse { status = "error", message = result.error };
                }

                var data = new ScreenshotResponseData
                {
                    filePath = result.filePath,
                    width = result.width,
                    height = result.height
                };
                return new ApiResponse { status = "success", message = "Screenshot saved to file", data = data };
            }
            else
            {
                var result = QueueUnityAction(() => ScreenshotCapture.CaptureAsBase64(superSize), 10000);

                if (!result.success)
                {
                    return new ApiResponse { status = "error", message = result.error };
                }

                var data = new ScreenshotResponseData
                {
                    base64 = result.base64,
                    width = result.width,
                    height = result.height
                };
                return new ApiResponse { status = "success", message = "Screenshot captured", data = data };
            }
        }

        // --- Input Handlers ---

        private static ApiResponse HandleInputPost(HttpListenerRequest request)
        {
            // Check play mode
            bool playing = QueueUnityAction(() => EditorApplication.isPlaying);
            if (!playing)
            {
                return new ApiResponse { status = "error", message = "Play Mode must be active to emulate input" };
            }

            var body = ReadRequestBody(request);
            var req = JsonUtility.FromJson<InputRequest>(body);

            if (req == null || string.IsNullOrEmpty(req.action))
            {
                return new ApiResponse { status = "error", message = "Missing 'action' field" };
            }

            InputEmulator.InputSequence sequence;

            switch (req.action.ToLower())
            {
                case "tap":
                    sequence = new InputEmulator.TapSequence(req.x, req.y, req.duration > 0 ? req.duration : 0.05f);
                    break;
                case "hold":
                    sequence = new InputEmulator.HoldSequence(req.x, req.y, req.duration > 0 ? req.duration : 1.0f);
                    break;
                case "drag":
                    sequence = new InputEmulator.DragSequence(req.startX, req.startY, req.endX, req.endY,
                        req.duration > 0 ? req.duration : 0.3f, "drag");
                    break;
                case "swipe":
                    sequence = new InputEmulator.DragSequence(req.startX, req.startY, req.endX, req.endY,
                        req.duration > 0 ? req.duration : 0.15f, "swipe");
                    break;
                case "pinch":
                    sequence = new InputEmulator.PinchSequence(req.centerX, req.centerY, req.startDistance, req.endDistance,
                        req.duration > 0 ? req.duration : 0.5f);
                    break;
                case "multi_tap":
                    sequence = new InputEmulator.MultiTapSequence(req.x, req.y,
                        req.count > 0 ? req.count : 2,
                        req.interval > 0 ? req.interval : 0.15f);
                    break;
                default:
                    return new ApiResponse { status = "error", message = $"Unknown input action '{req.action}'" };
            }

            var inputId = InputEmulator.EnqueueInput(sequence);

            var data = new InputStartedData
            {
                inputId = inputId,
                estimatedDuration = sequence.EstimatedDuration
            };

            return new ApiResponse { status = "started", message = $"Input '{req.action}' queued", data = data };
        }

        private static ApiResponse HandleInputGet()
        {
            var statusData = InputEmulator.GetStatus();
            return new ApiResponse
            {
                status = "success",
                message = $"Active: {statusData.activeCount}, Completed: {statusData.completedCount}",
                data = statusData
            };
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

        public static bool IsPlaying()
        {
            return EditorApplication.isPlaying;
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
                nextLogId = 1;
                compilationHistory.Clear();
                externalCommands.Clear();
                Debug.Log("[UnityBridge] Logs cleared manually");
            }
        }

        private static void LogExternalCommand(string endpoint, string method, string userAgent, bool success, string response)
        {
            lock (lockObject)
            {
                var commandName = endpoint.TrimStart('/');
                if (string.IsNullOrEmpty(commandName)) commandName = "unknown";

                var entry = new ExternalCommandEntry
                {
                    command = commandName,
                    timestamp = DateTime.Now.ToString("HH:mm:ss"),
                    endpoint = endpoint,
                    method = method,
                    userAgent = userAgent,
                    success = success,
                    response = response
                };

                externalCommands.Add(entry);

                // Keep only last 100 commands
                if (externalCommands.Count > 100)
                {
                    externalCommands.RemoveAt(0);
                }

                // Also notify the window if it exists
                try
                {
                    UnityBridgeWindow.AddExternalCommand(commandName, response, success);
                }
                catch (Exception)
                {
                    // Window might not exist, ignore
                }
            }
        }

        public static List<ExternalCommandEntry> GetExternalCommands()
        {
            lock (lockObject)
            {
                return new List<ExternalCommandEntry>(externalCommands);
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
                nextLogId = 1;
                Debug.Log("[UnityBridge] Logs cleared");
            }
        }
    }
}
