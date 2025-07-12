using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnityBridge
{
    /// <summary>
    /// Unity Bridge Editor Window - Monitor server status and command history
    /// </summary>
    public class UnityBridgeWindow : EditorWindow
    {
        private Vector2 scrollPosition;
        private bool autoRefresh = true;
        private double lastRefreshTime;
        private const double REFRESH_INTERVAL = 1.0; // Refresh every second
        
        private static List<CommandHistoryEntry> commandHistory = new List<CommandHistoryEntry>();
        private static readonly object commandHistoryLock = new object();
        
        [Serializable]
        public class CommandHistoryEntry
        {
            public string command;
            public string timestamp;
            public string source;
            public string result;
            public bool success;
        }
        
        [MenuItem("Tools/Unity Bridge/Monitor Window")]
        public static void ShowWindow()
        {
            var window = GetWindow<UnityBridgeWindow>("Unity Bridge Monitor");
            window.minSize = new Vector2(400, 300);
            window.Show();
        }
        
        private void OnEnable()
        {
            // Subscribe to log events to track external commands
            Application.logMessageReceived += OnLogReceived;
        }
        
        private void OnDisable()
        {
            Application.logMessageReceived -= OnLogReceived;
        }
        
        private void OnLogReceived(string condition, string stackTrace, LogType type)
        {
            // Look for Unity Bridge command indicators
            if (condition.Contains("[UnityBridge]") && (condition.Contains("health") || 
                condition.Contains("compile") || condition.Contains("logs") || 
                condition.Contains("status") || condition.Contains("clear")))
            {
                lock (commandHistoryLock)
                {
                    var entry = new CommandHistoryEntry
                    {
                        command = ExtractCommand(condition),
                        timestamp = DateTime.Now.ToString("HH:mm:ss"),
                        source = "External Script",
                        result = condition,
                        success = !condition.Contains("error") && !condition.Contains("failed")
                    };
                    
                    commandHistory.Add(entry);
                    
                    // Keep only last 50 entries
                    if (commandHistory.Count > 50)
                    {
                        commandHistory.RemoveAt(0);
                    }
                }
            }
        }
        
        private string ExtractCommand(string logMessage)
        {
            // Extract command from log message
            if (logMessage.Contains("health")) return "health";
            if (logMessage.Contains("compile")) return "compile";
            if (logMessage.Contains("logs")) return "logs";
            if (logMessage.Contains("status")) return "status";
            if (logMessage.Contains("clear")) return "clear";
            return "unknown";
        }
        
        private void OnGUI()
        {
            // Auto-refresh logic
            if (autoRefresh && EditorApplication.timeSinceStartup - lastRefreshTime > REFRESH_INTERVAL)
            {
                lastRefreshTime = EditorApplication.timeSinceStartup;
                Repaint();
            }
            
            DrawHeader();
            EditorGUILayout.Space();
            DrawServerStatus();
            EditorGUILayout.Space();
            DrawLine();
            DrawCommandHistory();
        }

        private void DrawLine()
        {
            EditorGUILayout.Space(5);
            
            EditorGUILayout.BeginHorizontal();
            
            // 10px buffer on left
            GUILayout.Space(10);
            
            // Get a rect for the horizontal line with reduced width
            var rect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true), GUILayout.Height(1));
            
            // Draw a subtle gray line
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.5f));
            
            // 10px buffer on right
            GUILayout.Space(10);
            
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(5);
        }


        private void DrawHeader()
        {
            EditorGUILayout.Space();
            
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(10);
            
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField("Unity Bridge Monitor", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            
            EditorGUILayout.BeginHorizontal();
            autoRefresh = EditorGUILayout.Toggle("Auto Refresh", autoRefresh);
            
            if (GUILayout.Button("Refresh Now", GUILayout.Width(100)))
            {
                Repaint();
            }
            
            if (GUILayout.Button("Clear History", GUILayout.Width(100)))
            {
                lock (commandHistoryLock)
                {
                    commandHistory.Clear();
                }
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space();
            EditorGUILayout.EndVertical();
            
            GUILayout.Space(10);
            EditorGUILayout.EndHorizontal();
        }
        
        private void DrawServerStatus()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(10);
            
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField("Server Status", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.Space();
            
            // Status indicator (green/red dot)
            bool isRunning = UnityBridgeServer.IsServerRunning();
            Color statusColor = isRunning ? Color.green : Color.red;
            string statusText = isRunning ? "ONLINE" : "OFFLINE";

            // Draw colored status dot
            var rect = GUILayoutUtility.GetRect(10, EditorGUIUtility.singleLineHeight, GUILayout.Width(10));
            Handles.color = statusColor;
            Vector3 center = new Vector3(rect.center.x, rect.center.y + 3f, 0); // Adjust Y position to match text baseline
            float radius = 4f; // Fixed radius for consistency
            Handles.DrawSolidDisc(center, Vector3.forward, radius);
            
            EditorGUILayout.LabelField(statusText, GUILayout.Width(50), GUILayout.Height(EditorGUIUtility.singleLineHeight));
            
            EditorGUILayout.LabelField("|", GUILayout.Width(10), GUILayout.Height(EditorGUIUtility.singleLineHeight));
            
            // Additional status info
            if (isRunning)
            {
                bool isCompiling = UnityBridgeServer.IsCompiling();
                int logCount = UnityBridgeServer.GetLogCount();
                
                EditorGUILayout.LabelField($"Port: 5556", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(70));
                EditorGUILayout.LabelField("|", GUILayout.Width(10), GUILayout.Height(EditorGUIUtility.singleLineHeight));
                EditorGUILayout.LabelField($"Logs: {logCount}", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(70));
                EditorGUILayout.LabelField("|", GUILayout.Width(10), GUILayout.Height(EditorGUIUtility.singleLineHeight));
                
                if (isCompiling)
                {
                    var compilingRect = GUILayoutUtility.GetRect(10, EditorGUIUtility.singleLineHeight, GUILayout.Width(10));
                    Handles.color = Color.yellow;
                    Vector3 center = new Vector3(compilingRect.center.x, compilingRect.center.y + 3f, 0); // Adjust Y position to match text baseline
                    float radius = 4f; // Fixed radius for consistency
                    Handles.DrawSolidDisc(center, Vector3.forward, radius);
                    EditorGUILayout.LabelField("COMPILING", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(80));
                }
                else
                {
                    EditorGUILayout.LabelField("Ready", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(50));
                }
            }
            else
            {
                EditorGUILayout.LabelField("Server not responding", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(100));
            }
            
            GUILayout.FlexibleSpace(); // Push all elements to the left
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(10);

            // Control buttons
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Start Server", GUILayout.Width(100)))
            {
                UnityBridgeServer.StartServerManually();
                AddCommandToHistory("start_server", "Manual", "Server start requested", true);
            }
            
            if (GUILayout.Button("Stop Server", GUILayout.Width(100)))
            {
                UnityBridgeServer.StopServerManually();
                AddCommandToHistory("stop_server", "Manual", "Server stop requested", true);
            }
            
            if (GUILayout.Button("Test Compile", GUILayout.Width(100)))
            {
                UnityBridgeServer.TriggerTestCompilation();
                AddCommandToHistory("test_compile", "Manual", "Test compilation triggered", true);
            }
            
            if (GUILayout.Button("Clear Logs", GUILayout.Width(100)))
            {
                UnityBridgeServer.ClearLogsManually();
                AddCommandToHistory("clear_logs", "Manual", "Logs cleared", true);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
            EditorGUILayout.EndVertical();
            
            GUILayout.Space(10);
            EditorGUILayout.EndHorizontal();
        }
        
        private void DrawCommandHistory()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(10);
            
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField("Command History", EditorStyles.boldLabel);
            
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            
            lock (commandHistoryLock)
            {
                if (commandHistory.Count == 0)
                {
                    EditorGUILayout.LabelField("No commands executed yet", EditorStyles.centeredGreyMiniLabel);
                }
                else
                {
                    // Display commands in reverse chronological order (newest first)
                    for (int i = commandHistory.Count - 1; i >= 0; i--)
                    {
                        var entry = commandHistory[i];
                        DrawCommandEntry(entry);
                    }
                }
            }
            
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            
            GUILayout.Space(10);
            EditorGUILayout.EndHorizontal();
        }
        
        private void DrawCommandEntry(CommandHistoryEntry entry)
        {
            EditorGUILayout.BeginHorizontal("box");
            
            // Status indicator
            Color statusColor = entry.success ? Color.green : Color.red;
            var statusRect = GUILayoutUtility.GetRect(10, 10, GUILayout.Width(10), GUILayout.Height(10));
            EditorGUI.DrawRect(statusRect, statusColor);
            
            // Timestamp
            EditorGUILayout.LabelField(entry.timestamp, GUILayout.Width(60));
            
            // Command
            EditorGUILayout.LabelField(entry.command, EditorStyles.boldLabel, GUILayout.Width(80));
            
            // Source
            EditorGUILayout.LabelField($"({entry.source})", EditorStyles.miniLabel, GUILayout.Width(80));
            
            // Result (truncated)
            string displayResult = entry.result;
            if (displayResult.Length > 60)
            {
                displayResult = displayResult.Substring(0, 57) + "...";
            }
            EditorGUILayout.LabelField(displayResult, EditorStyles.miniLabel);
            
            EditorGUILayout.EndHorizontal();
        }
        
        private void AddCommandToHistory(string command, string source, string result, bool success)
        {
            lock (commandHistoryLock)
            {
                var entry = new CommandHistoryEntry
                {
                    command = command,
                    timestamp = DateTime.Now.ToString("HH:mm:ss"),
                    source = source,
                    result = result,
                    success = success
                };
                
                commandHistory.Add(entry);
                
                // Keep only last 50 entries
                if (commandHistory.Count > 50)
                {
                    commandHistory.RemoveAt(0);
                }
            }
        }
        
        // Static method to add external command history
        public static void AddExternalCommand(string command, string result, bool success)
        {
            lock (commandHistoryLock)
            {
                var entry = new CommandHistoryEntry
                {
                    command = command,
                    timestamp = DateTime.Now.ToString("HH:mm:ss"),
                    source = "External Script",
                    result = result,
                    success = success
                };
                
                commandHistory.Add(entry);
                
                // Keep only last 50 entries
                if (commandHistory.Count > 50)
                {
                    commandHistory.RemoveAt(0);
                }
            }
        }
    }
}