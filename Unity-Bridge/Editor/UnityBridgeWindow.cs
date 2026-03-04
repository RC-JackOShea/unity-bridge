using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnityBridge
{
    /// <summary>
    /// Unity Bridge Editor Window - Tabbed interface with Monitor and Tests views
    /// </summary>
    public class UnityBridgeWindow : EditorWindow
    {
        // --- Tab system ---
        [SerializeField] private int selectedTab;
        private readonly string[] tabLabels = { "Monitor", "Tests" };

        // --- Monitor tab state ---
        private Vector2 scrollPosition;
        private bool autoRefresh = true;
        private double lastRefreshTime;
        private const double REFRESH_INTERVAL = 1.0;

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

        // --- Tests tab state ---
        private string testDirectoryPath;
        private const string TEST_DIR_PREF_KEY = "UnityBridge_TestDirectory";
        private Vector2 testsScrollPosition;
        private List<TestSuiteInfo> discoveredSuites = new List<TestSuiteInfo>();
        private bool testsDiscovered;
        private Dictionary<string, bool> suiteFoldouts = new Dictionary<string, bool>();
        private Dictionary<string, bool> testFoldouts = new Dictionary<string, bool>();

        // --- Test execution state ---
        private bool isTestRunning;
        private string runningTestName;
        private int runningActionIndex;
        private int runningActionTotal;
        private string lastTestResult; // "Passed", "Failed", or null
        private string lastTestName;
        private double lastPollTime;
        private const double POLL_INTERVAL = 0.1;

        // Suite execution
        private Queue<TestFileInfo> suiteQueue;
        private bool isSuiteRunning;
        private int suitePassed;
        private int suiteFailed;
        private int suiteTotal;
        private string runningSuiteName;
        private bool suiteWaitingBetweenTests;
        private double suiteNextTestTime;
        private const double SUITE_PAUSE_SECONDS = 1.0;

        // Run-all execution (cycles play mode between suites)
        private Queue<TestSuiteInfo> runAllSuiteQueue;
        private bool isRunAllActive;
        private int runAllPassed;
        private int runAllFailed;
        private int runAllTotal;

        // Play mode lifecycle — pending test launch
        private bool waitingForPlayMode;
        private const string PENDING_TEST_PATH_KEY = "UnityBridge_PendingTestPath";
        private const string PENDING_SUITE_DIR_KEY = "UnityBridge_PendingSuiteDir";
        private const string PENDING_SUITE_NAME_KEY = "UnityBridge_PendingSuiteName";
        private const string SHOULD_EXIT_PLAY_KEY = "UnityBridge_ShouldExitPlayMode";
        private const string RUN_ALL_ACTIVE_KEY = "UnityBridge_RunAllActive";
        private const string RUN_ALL_SUITES_KEY = "UnityBridge_RunAllSuites";
        private const string RUN_ALL_PASSED_KEY = "UnityBridge_RunAllPassed";
        private const string RUN_ALL_FAILED_KEY = "UnityBridge_RunAllFailed";
        private const string RUN_ALL_TOTAL_KEY = "UnityBridge_RunAllTotal";

        // --- Data classes ---
        private class TestSuiteInfo
        {
            public string directoryName;
            public string directoryPath;
            public List<TestFileInfo> tests = new List<TestFileInfo>();
        }

        private class TestFileInfo
        {
            public string fileName;
            public string filePath;
            public string name;
            public string description;
            public int actionCount;
            public string[] tags;
            public string sceneName;
        }

        [MenuItem("Tools/Unity Bridge/Monitor Window")]
        public static void ShowWindow()
        {
            var window = GetWindow<UnityBridgeWindow>("Unity Bridge");
            window.minSize = new Vector2(400, 300);
            window.Show();
        }

        private void OnEnable()
        {
            Application.logMessageReceived += OnLogReceived;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += OnEditorUpdate;
            testDirectoryPath = EditorPrefs.GetString(TEST_DIR_PREF_KEY, "Assets/Tests/Integration");
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= OnLogReceived;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnLogReceived(string condition, string stackTrace, LogType type)
        {
            if (condition.Contains("[UnityBridge]") && (condition.Contains("health") ||
                condition.Contains("compile") || condition.Contains("logs") ||
                condition.Contains("status") || condition.Contains("clear") ||
                condition.Contains("playmode") || condition.Contains("screenshot") ||
                condition.Contains("input")))
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
                    if (commandHistory.Count > 50)
                        commandHistory.RemoveAt(0);
                }
            }
        }

        private string ExtractCommand(string logMessage)
        {
            if (logMessage.Contains("health")) return "health";
            if (logMessage.Contains("compile")) return "compile";
            if (logMessage.Contains("playmode")) return "playmode";
            if (logMessage.Contains("screenshot")) return "screenshot";
            if (logMessage.Contains("input")) return "input";
            if (logMessage.Contains("logs")) return "logs";
            if (logMessage.Contains("status")) return "status";
            if (logMessage.Contains("clear")) return "clear";
            return "unknown";
        }

        private void OnEditorUpdate()
        {
            // Between-test pause in suite execution
            if (suiteWaitingBetweenTests)
            {
                if (EditorApplication.timeSinceStartup >= suiteNextTestTime)
                {
                    suiteWaitingBetweenTests = false;
                    var next = suiteQueue.Dequeue();
                    LaunchTest(next);
                }
                Repaint();
                return;
            }

            if (!isTestRunning)
                return;

            if (EditorApplication.timeSinceStartup - lastPollTime < POLL_INTERVAL)
                return;

            lastPollTime = EditorApplication.timeSinceStartup;
            PollTestStatus();
            Repaint();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // Re-discover tests after domain reload so the Tests tab stays populated
            if (state == PlayModeStateChange.EnteredPlayMode || state == PlayModeStateChange.EnteredEditMode)
            {
                if (!string.IsNullOrEmpty(testDirectoryPath))
                    DiscoverTests();
            }

            // --- EnteredEditMode: continue run-all with next suite ---
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                if (SessionState.GetBool(RUN_ALL_ACTIVE_KEY, false))
                {
                    RestoreRunAllState();
                    if (runAllSuiteQueue != null && runAllSuiteQueue.Count > 0)
                    {
                        var nextSuite = runAllSuiteQueue.Dequeue();
                        SaveRunAllState();

                        if (nextSuite.tests.Count > 0)
                            OpenSceneForTest(nextSuite.tests[0]);

                        SessionState.SetString(PENDING_SUITE_DIR_KEY, nextSuite.directoryPath);
                        SessionState.SetString(PENDING_SUITE_NAME_KEY, nextSuite.directoryName);
                        SessionState.SetBool(SHOULD_EXIT_PLAY_KEY, true);
                        waitingForPlayMode = true;
                        EditorApplication.isPlaying = true;
                    }
                }
                return;
            }

            if (state != PlayModeStateChange.EnteredPlayMode)
                return;

            // Restore run-all state if active (survives domain reload)
            if (SessionState.GetBool(RUN_ALL_ACTIVE_KEY, false))
                RestoreRunAllState();

            // Check for pending single test
            string pendingTestPath = SessionState.GetString(PENDING_TEST_PATH_KEY, "");
            if (!string.IsNullOrEmpty(pendingTestPath))
            {
                SessionState.EraseString(PENDING_TEST_PATH_KEY);
                waitingForPlayMode = false;

                var test = ParseTestFile(pendingTestPath);
                isSuiteRunning = false;
                suiteQueue = null;
                suiteTotal = 0;
                suitePassed = 0;
                suiteFailed = 0;
                LaunchTest(test);
                Repaint();
                return;
            }

            // Check for pending suite (also used by run-all for each suite)
            string pendingSuiteDir = SessionState.GetString(PENDING_SUITE_DIR_KEY, "");
            if (!string.IsNullOrEmpty(pendingSuiteDir))
            {
                string suiteName = SessionState.GetString(PENDING_SUITE_NAME_KEY, "");
                SessionState.EraseString(PENDING_SUITE_DIR_KEY);
                SessionState.EraseString(PENDING_SUITE_NAME_KEY);
                waitingForPlayMode = false;

                // Re-discover tests from the directory
                if (Directory.Exists(pendingSuiteDir))
                {
                    string[] jsonFiles = Directory.GetFiles(pendingSuiteDir, "*.json");
                    Array.Sort(jsonFiles);

                    if (jsonFiles.Length > 0)
                    {
                        var tests = new List<TestFileInfo>();
                        foreach (var f in jsonFiles)
                            tests.Add(ParseTestFile(f));

                        isSuiteRunning = true;
                        runningSuiteName = suiteName;
                        suiteQueue = new Queue<TestFileInfo>(tests);
                        suiteTotal = tests.Count;
                        suitePassed = 0;
                        suiteFailed = 0;

                        var first = suiteQueue.Dequeue();
                        LaunchTest(first);
                    }
                }

                Repaint();
                return;
            }
        }

        private string FindScenePath(string sceneName)
        {
            string[] guids = AssetDatabase.FindAssets(sceneName + " t:Scene");
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == sceneName)
                    return path;
            }
            return null;
        }

        // =====================================================================
        // OnGUI — Tab dispatcher
        // =====================================================================

        private void OnGUI()
        {
            // Auto-refresh for Monitor tab
            if (autoRefresh && EditorApplication.timeSinceStartup - lastRefreshTime > REFRESH_INTERVAL)
            {
                lastRefreshTime = EditorApplication.timeSinceStartup;
                Repaint();
            }

            DrawTabBar();

            switch (selectedTab)
            {
                case 0:
                    DrawMonitorTab();
                    break;
                case 1:
                    DrawTestsTab();
                    break;
            }
        }

        private void DrawTabBar()
        {
            EditorGUILayout.Space(2);
            selectedTab = GUILayout.Toolbar(selectedTab, tabLabels, GUILayout.Height(24));
            EditorGUILayout.Space(4);
        }

        // =====================================================================
        // Monitor Tab — existing functionality
        // =====================================================================

        private void DrawMonitorTab()
        {
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
            GUILayout.Space(10);
            var rect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true), GUILayout.Height(1));
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.5f));
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

            EditorGUILayout.BeginHorizontal();
            autoRefresh = EditorGUILayout.Toggle("Auto Refresh", autoRefresh);

            if (GUILayout.Button("Refresh Now", GUILayout.Width(100)))
                Repaint();

            if (GUILayout.Button("Clear History", GUILayout.Width(100)))
            {
                lock (commandHistoryLock)
                    commandHistory.Clear();
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

            bool isRunning = UnityBridgeServer.IsServerRunning();
            Color statusColor = isRunning ? Color.green : Color.red;
            string statusText = isRunning ? "ONLINE" : "OFFLINE";

            var rect = GUILayoutUtility.GetRect(10, EditorGUIUtility.singleLineHeight, GUILayout.Width(10));
            Handles.color = statusColor;
            Vector3 center = new Vector3(rect.center.x, rect.center.y + 3f, 0);
            float radius = 4f;
            Handles.DrawSolidDisc(center, Vector3.forward, radius);

            EditorGUILayout.LabelField(statusText, GUILayout.Width(50), GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.LabelField("|", GUILayout.Width(10), GUILayout.Height(EditorGUIUtility.singleLineHeight));

            if (isRunning)
            {
                bool isCompiling = UnityBridgeServer.IsCompiling();
                int logCount = UnityBridgeServer.GetLogCount();

                EditorGUILayout.LabelField("Port: 5556", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(70));
                EditorGUILayout.LabelField("|", GUILayout.Width(10), GUILayout.Height(EditorGUIUtility.singleLineHeight));
                EditorGUILayout.LabelField($"Logs: {logCount}", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(70));
                EditorGUILayout.LabelField("|", GUILayout.Width(10), GUILayout.Height(EditorGUIUtility.singleLineHeight));

                if (isCompiling)
                {
                    var compilingRect = GUILayoutUtility.GetRect(10, EditorGUIUtility.singleLineHeight, GUILayout.Width(10));
                    Handles.color = Color.yellow;
                    center = new Vector3(compilingRect.center.x, compilingRect.center.y + 3f, 0);
                    Handles.DrawSolidDisc(center, Vector3.forward, radius);
                    EditorGUILayout.LabelField("COMPILING", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(80));
                }
                else if (UnityBridgeServer.IsPlaying())
                {
                    var playingRect = GUILayoutUtility.GetRect(10, EditorGUIUtility.singleLineHeight, GUILayout.Width(10));
                    Handles.color = Color.cyan;
                    center = new Vector3(playingRect.center.x, playingRect.center.y + 3f, 0);
                    Handles.DrawSolidDisc(center, Vector3.forward, radius);
                    EditorGUILayout.LabelField("PLAYING", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(60));
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

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

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

            Color statusColor = entry.success ? Color.green : Color.red;
            var statusRect = GUILayoutUtility.GetRect(10, 10, GUILayout.Width(10), GUILayout.Height(10));
            EditorGUI.DrawRect(statusRect, statusColor);

            EditorGUILayout.LabelField(entry.timestamp, GUILayout.Width(60));
            EditorGUILayout.LabelField(entry.command, EditorStyles.boldLabel, GUILayout.Width(80));
            EditorGUILayout.LabelField($"({entry.source})", EditorStyles.miniLabel, GUILayout.Width(80));

            string displayResult = entry.result;
            if (displayResult.Length > 60)
                displayResult = displayResult.Substring(0, 57) + "...";
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
                if (commandHistory.Count > 50)
                    commandHistory.RemoveAt(0);
            }
        }

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
                if (commandHistory.Count > 50)
                    commandHistory.RemoveAt(0);
            }
        }

        // =====================================================================
        // Tests Tab
        // =====================================================================

        private void DrawTestsTab()
        {
            // Auto-discover on first view
            if (!testsDiscovered)
            {
                DiscoverTests();
                testsDiscovered = true;
            }

            DrawDirectoryBar();
            EditorGUILayout.Space(4);
            DrawTestTree();
            DrawExecutionStatusBar();
        }

        // --- A. Directory bar ---

        private void DrawDirectoryBar()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(10);

            EditorGUILayout.LabelField("Test Directory", GUILayout.Width(90));
            testDirectoryPath = EditorGUILayout.TextField(testDirectoryPath);

            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                string abs = Path.GetFullPath(testDirectoryPath);
                string selected = EditorUtility.OpenFolderPanel("Select Test Directory", abs, "");
                if (!string.IsNullOrEmpty(selected))
                {
                    // Convert to project-relative path if inside Assets
                    string dataPath = Application.dataPath.Replace("\\", "/");
                    selected = selected.Replace("\\", "/");
                    if (selected.StartsWith(dataPath))
                        testDirectoryPath = "Assets" + selected.Substring(dataPath.Length);
                    else
                        testDirectoryPath = selected;

                    EditorPrefs.SetString(TEST_DIR_PREF_KEY, testDirectoryPath);
                    DiscoverTests();
                }
            }

            if (GUILayout.Button("Refresh", GUILayout.Width(60)))
                DiscoverTests();

            bool busy = isTestRunning || waitingForPlayMode || suiteWaitingBetweenTests;
            EditorGUI.BeginDisabledGroup(busy || discoveredSuites.Count == 0);
            if (GUILayout.Button("Run All", GUILayout.Width(60)))
                StartRunAll();
            EditorGUI.EndDisabledGroup();

            GUILayout.Space(10);
            EditorGUILayout.EndHorizontal();
        }

        // --- B. Test tree ---

        private void DrawTestTree()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(10);
            EditorGUILayout.BeginVertical();

            testsScrollPosition = EditorGUILayout.BeginScrollView(testsScrollPosition);

            if (discoveredSuites.Count == 0)
            {
                EditorGUILayout.LabelField("No test suites found", EditorStyles.centeredGreyMiniLabel);
                if (!Directory.Exists(testDirectoryPath))
                    EditorGUILayout.HelpBox("Directory not found: " + testDirectoryPath, MessageType.Warning);
            }

            bool busy = isTestRunning || waitingForPlayMode || suiteWaitingBetweenTests;

            foreach (var suite in discoveredSuites)
            {
                // Suite foldout header
                if (!suiteFoldouts.ContainsKey(suite.directoryName))
                    suiteFoldouts[suite.directoryName] = false;

                EditorGUILayout.BeginHorizontal("box");

                suiteFoldouts[suite.directoryName] = EditorGUILayout.Foldout(
                    suiteFoldouts[suite.directoryName],
                    suite.directoryName + "  (" + suite.tests.Count + " test" + (suite.tests.Count != 1 ? "s" : "") + ")",
                    true,
                    EditorStyles.foldoutHeader);

                GUILayout.FlexibleSpace();

                EditorGUI.BeginDisabledGroup(busy);
                if (GUILayout.Button("Run Suite", GUILayout.Width(80)))
                    StartSuiteExecution(suite);
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.EndHorizontal();

                // Test rows
                if (suiteFoldouts[suite.directoryName])
                {
                    EditorGUI.indentLevel++;
                    foreach (var test in suite.tests)
                    {
                        DrawTestRow(test, busy);
                    }
                    EditorGUI.indentLevel--;
                    EditorGUILayout.Space(4);
                }
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
            GUILayout.Space(10);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTestRow(TestFileInfo test, bool busy)
        {
            EditorGUILayout.BeginVertical("helpbox");

            // Header: foldout + action count + Run button
            EditorGUILayout.BeginHorizontal();

            if (!testFoldouts.ContainsKey(test.filePath))
                testFoldouts[test.filePath] = false;

            testFoldouts[test.filePath] = EditorGUILayout.Foldout(
                testFoldouts[test.filePath], test.fileName, true);

            GUILayout.FlexibleSpace();

            if (test.actionCount > 0)
                EditorGUILayout.LabelField(test.actionCount + " actions", EditorStyles.miniLabel, GUILayout.Width(65));

            EditorGUI.BeginDisabledGroup(busy);
            if (GUILayout.Button("Run", GUILayout.Width(50)))
                StartSingleTest(test);
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();

            // Expanded details
            if (testFoldouts[test.filePath])
            {
                // Name
                EditorGUILayout.LabelField(test.name, EditorStyles.miniLabel);

                // Description
                if (!string.IsNullOrEmpty(test.description))
                    EditorGUILayout.LabelField(test.description, EditorStyles.wordWrappedMiniLabel);

                // Tags
                if (test.tags != null && test.tags.Length > 0)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Tags:", EditorStyles.miniLabel, GUILayout.Width(35));

                    var tagStyle = new GUIStyle(EditorStyles.miniLabel);
                    tagStyle.normal.textColor = new Color(0.4f, 0.7f, 1f);
                    EditorGUILayout.LabelField(string.Join(", ", test.tags), tagStyle);

                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.EndVertical();
        }

        // --- C. Execution status bar ---

        private void DrawExecutionStatusBar()
        {
            if (!isTestRunning && !waitingForPlayMode && !suiteWaitingBetweenTests && lastTestResult == null)
                return;

            EditorGUILayout.Space(4);
            DrawLine();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(10);
            EditorGUILayout.BeginVertical();

            if (waitingForPlayMode)
            {
                // Entering play mode state
                EditorGUILayout.BeginHorizontal();
                var dotRect = GUILayoutUtility.GetRect(10, EditorGUIUtility.singleLineHeight, GUILayout.Width(10));
                Handles.color = Color.yellow;
                Vector3 center = new Vector3(dotRect.center.x, dotRect.center.y + 3f, 0);
                Handles.DrawSolidDisc(center, Vector3.forward, 4f);
                string waitLabel = isRunAllActive ? "Cycling to next suite..." : "Entering Play Mode...";
                EditorGUILayout.LabelField(waitLabel, EditorStyles.boldLabel);
                EditorGUILayout.EndHorizontal();
            }
            else if (suiteWaitingBetweenTests)
            {
                // Pause between suite tests
                EditorGUILayout.BeginHorizontal();
                var dotRect = GUILayoutUtility.GetRect(10, EditorGUIUtility.singleLineHeight, GUILayout.Width(10));
                Handles.color = Color.cyan;
                Vector3 center = new Vector3(dotRect.center.x, dotRect.center.y + 3f, 0);
                Handles.DrawSolidDisc(center, Vector3.forward, 4f);
                int remaining = suiteQueue != null ? suiteQueue.Count + 1 : 0;
                EditorGUILayout.LabelField(
                    $"Next test in {Mathf.Max(0f, (float)(suiteNextTestTime - EditorApplication.timeSinceStartup)):F1}s  [{runningSuiteName}: {remaining} remaining]",
                    EditorStyles.boldLabel);
                EditorGUILayout.EndHorizontal();
            }
            else if (isTestRunning)
            {
                // Running state
                EditorGUILayout.BeginHorizontal();

                // Yellow dot
                var dotRect = GUILayoutUtility.GetRect(10, EditorGUIUtility.singleLineHeight, GUILayout.Width(10));
                Handles.color = Color.yellow;
                Vector3 center = new Vector3(dotRect.center.x, dotRect.center.y + 3f, 0);
                Handles.DrawSolidDisc(center, Vector3.forward, 4f);

                string label = isSuiteRunning
                    ? $"Running: {runningTestName}  [{runningSuiteName}]"
                    : $"Running: {runningTestName}";
                if (isRunAllActive)
                    label += $"  (Run All: {runAllPassed + runAllFailed + suitePassed + suiteFailed}/{runAllTotal})";
                EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
                EditorGUILayout.EndHorizontal();

                // Progress bar
                float progress = runningActionTotal > 0 ? (float)runningActionIndex / runningActionTotal : 0f;
                string progressText = $"Action {runningActionIndex} / {runningActionTotal}";
                EditorGUI.ProgressBar(
                    EditorGUILayout.GetControlRect(false, 18),
                    progress,
                    progressText);
            }
            else if (lastTestResult != null)
            {
                // Completed state
                EditorGUILayout.BeginHorizontal();

                bool passed = lastTestResult == "Passed";
                var dotRect = GUILayoutUtility.GetRect(10, EditorGUIUtility.singleLineHeight, GUILayout.Width(10));
                Handles.color = passed ? Color.green : Color.red;
                Vector3 center = new Vector3(dotRect.center.x, dotRect.center.y + 3f, 0);
                Handles.DrawSolidDisc(center, Vector3.forward, 4f);

                string resultLabel;
                if (isSuiteRunning || suiteTotal > 0)
                {
                    resultLabel = $"{lastTestResult}  ({suitePassed} passed, {suiteFailed} failed of {suiteTotal})";
                    // Suite is done — reset suiteTotal flag after displaying
                }
                else
                {
                    resultLabel = $"{lastTestResult}: {lastTestName}";
                }

                EditorGUILayout.LabelField(resultLabel, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Dismiss", GUILayout.Width(60)))
                {
                    lastTestResult = null;
                    lastTestName = null;
                    suiteTotal = 0;
                    suitePassed = 0;
                    suiteFailed = 0;
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(10);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);
        }

        // =====================================================================
        // Test Discovery
        // =====================================================================

        private void DiscoverTests()
        {
            discoveredSuites.Clear();

            if (!Directory.Exists(testDirectoryPath))
                return;

            // Find subdirectories containing .json files — each is a suite
            string[] subdirs = Directory.GetDirectories(testDirectoryPath);
            Array.Sort(subdirs);

            foreach (var subdir in subdirs)
            {
                string[] jsonFiles = Directory.GetFiles(subdir, "*.json");
                if (jsonFiles.Length == 0)
                    continue;

                Array.Sort(jsonFiles);
                var suite = new TestSuiteInfo
                {
                    directoryName = Path.GetFileName(subdir),
                    directoryPath = subdir.Replace("\\", "/")
                };

                foreach (var jsonFile in jsonFiles)
                    suite.tests.Add(ParseTestFile(jsonFile));

                discoveredSuites.Add(suite);
            }

            // Standalone .json files at top level
            string[] topLevelFiles = Directory.GetFiles(testDirectoryPath, "*.json");
            if (topLevelFiles.Length > 0)
            {
                Array.Sort(topLevelFiles);
                var standalone = new TestSuiteInfo
                {
                    directoryName = "(standalone)",
                    directoryPath = testDirectoryPath.Replace("\\", "/")
                };

                foreach (var jsonFile in topLevelFiles)
                    standalone.tests.Add(ParseTestFile(jsonFile));

                discoveredSuites.Add(standalone);
            }

            EditorPrefs.SetString(TEST_DIR_PREF_KEY, testDirectoryPath);
        }

        private TestFileInfo ParseTestFile(string filePath)
        {
            var info = new TestFileInfo
            {
                fileName = Path.GetFileName(filePath),
                filePath = filePath.Replace("\\", "/"),
                name = Path.GetFileNameWithoutExtension(filePath),
                description = "",
                actionCount = 0,
                tags = new string[0],
                sceneName = null
            };

            try
            {
                string json = File.ReadAllText(filePath);
                var node = SimpleJson.Parse(json);

                info.name = node.GetString("name") ?? info.name;
                info.description = node.GetString("description") ?? "";

                var actions = node.Get("actions");
                if (actions?.arr != null)
                    info.actionCount = actions.arr.Count;

                var tags = node.Get("tags");
                if (tags?.arr != null)
                {
                    var tagList = new List<string>();
                    foreach (var tag in tags.arr)
                        tagList.Add(tag.AsString());
                    info.tags = tagList.ToArray();
                }

                var setup = node.Get("setup");
                if (setup != null)
                    info.sceneName = setup.GetString("scene");
            }
            catch
            {
                // Parse error — keep defaults
            }

            return info;
        }

        // =====================================================================
        // Test Execution
        // =====================================================================

        private void StartSingleTest(TestFileInfo test)
        {
            if (isTestRunning || waitingForPlayMode)
                return;

            if (EditorApplication.isPlaying)
            {
                // Already in Play Mode — launch directly
                isSuiteRunning = false;
                suiteQueue = null;
                suiteTotal = 0;
                suitePassed = 0;
                suiteFailed = 0;
                LaunchTest(test);
            }
            else
            {
                // Edit Mode — open scene, enter play mode, launch after transition
                OpenSceneForTest(test);
                SessionState.SetString(PENDING_TEST_PATH_KEY, test.filePath);
                SessionState.SetBool(SHOULD_EXIT_PLAY_KEY, true);
                waitingForPlayMode = true;
                lastTestResult = null;
                EditorApplication.isPlaying = true;
            }
        }

        private void StartSuiteExecution(TestSuiteInfo suite)
        {
            if (isTestRunning || waitingForPlayMode)
                return;

            if (suite.tests.Count == 0)
                return;

            if (EditorApplication.isPlaying)
            {
                // Already in Play Mode — launch directly
                isSuiteRunning = true;
                runningSuiteName = suite.directoryName;
                suiteQueue = new Queue<TestFileInfo>(suite.tests);
                suiteTotal = suite.tests.Count;
                suitePassed = 0;
                suiteFailed = 0;

                var first = suiteQueue.Dequeue();
                LoadSceneIfNeeded(first);
                LaunchTest(first);
            }
            else
            {
                // Edit Mode — open scene from first test, enter play mode
                if (suite.tests.Count > 0)
                    OpenSceneForTest(suite.tests[0]);

                SessionState.SetString(PENDING_SUITE_DIR_KEY, suite.directoryPath);
                SessionState.SetString(PENDING_SUITE_NAME_KEY, suite.directoryName);
                SessionState.SetBool(SHOULD_EXIT_PLAY_KEY, true);
                waitingForPlayMode = true;
                lastTestResult = null;
                EditorApplication.isPlaying = true;
            }
        }

        private void StartRunAll()
        {
            if (isTestRunning || waitingForPlayMode)
                return;

            if (discoveredSuites.Count == 0)
                return;

            // Count total tests across all suites
            int total = 0;
            foreach (var suite in discoveredSuites)
                total += suite.tests.Count;

            if (total == 0)
                return;

            // Initialize run-all state
            isRunAllActive = true;
            runAllPassed = 0;
            runAllFailed = 0;
            runAllTotal = total;
            lastTestResult = null;

            // Queue remaining suites (all except first)
            runAllSuiteQueue = new Queue<TestSuiteInfo>();
            for (int i = 1; i < discoveredSuites.Count; i++)
                runAllSuiteQueue.Enqueue(discoveredSuites[i]);

            SaveRunAllState();

            // Start the first suite (handles both edit/play mode)
            StartSuiteExecution(discoveredSuites[0]);
        }

        private void OpenSceneForTest(TestFileInfo test)
        {
            if (string.IsNullOrEmpty(test.sceneName))
                return;

            string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (currentScene == test.sceneName)
                return;

            string scenePath = FindScenePath(test.sceneName);
            if (string.IsNullOrEmpty(scenePath))
            {
                Debug.LogWarning("[UnityBridge] Scene not found: " + test.sceneName);
                return;
            }

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        }

        private void LaunchTest(TestFileInfo test)
        {
            string result = IntegrationTestRunner.RunTestAsync(test.filePath);

            try
            {
                var node = SimpleJson.Parse(result);
                bool success = node.Get("success")?.AsBool() ?? false;

                if (success)
                {
                    isTestRunning = true;
                    runningTestName = test.name;
                    runningActionIndex = 0;
                    runningActionTotal = test.actionCount;
                    lastTestResult = null;
                    lastTestName = null;
                    lastPollTime = EditorApplication.timeSinceStartup;
                }
                else
                {
                    // Launch failed
                    string error = node.GetString("error") ?? "Unknown error";
                    Debug.LogWarning("[UnityBridge] Test launch failed: " + error);
                    OnTestCompleted("Failed", test.name);
                }
            }
            catch
            {
                Debug.LogWarning("[UnityBridge] Failed to parse RunTestAsync result");
                OnTestCompleted("Failed", test.name);
            }
        }

        private void PollTestStatus()
        {
            string statusJson = IntegrationTestRunner.GetTestStatus();

            try
            {
                var node = SimpleJson.Parse(statusJson);
                string status = node.GetString("status") ?? "idle";

                if (status == "running")
                {
                    runningActionIndex = (int)(node.Get("progress")?.AsFloat() ?? 0);
                    runningActionTotal = (int)(node.Get("total")?.AsFloat() ?? runningActionTotal);
                    string name = node.GetString("name");
                    if (!string.IsNullOrEmpty(name))
                        runningTestName = name;
                }
                else if (status == "completed")
                {
                    string overallResult = node.GetString("overallResult") ?? "Unknown";
                    string name = node.GetString("name") ?? runningTestName;
                    OnTestCompleted(overallResult, name);
                }
                else if (status == "idle" && isTestRunning)
                {
                    // Test ended without completed status — treat as unknown
                    OnTestCompleted("Failed", runningTestName);
                }
            }
            catch
            {
                // Parse error — keep polling
            }
        }

        private void OnTestCompleted(string result, string testName)
        {
            isTestRunning = false;

            if (isSuiteRunning)
            {
                if (result == "Passed")
                    suitePassed++;
                else
                    suiteFailed++;

                // Next test in suite — reload scene now, pause, then launch
                if (suiteQueue != null && suiteQueue.Count > 0)
                {
                    ReloadSceneForTest(suiteQueue.Peek());
                    suiteWaitingBetweenTests = true;
                    suiteNextTestTime = EditorApplication.timeSinceStartup + SUITE_PAUSE_SECONDS;
                    Repaint();
                    return;
                }

                // Current suite complete
                isSuiteRunning = false;

                if (isRunAllActive)
                {
                    // Accumulate into run-all totals
                    runAllPassed += suitePassed;
                    runAllFailed += suiteFailed;
                    SaveRunAllState();

                    if (runAllSuiteQueue != null && runAllSuiteQueue.Count > 0)
                    {
                        // More suites — exit play mode, EnteredEditMode handler will continue
                        if (EditorApplication.isPlaying)
                            EditorApplication.isPlaying = false;
                        Repaint();
                        return;
                    }

                    // All suites done — show aggregate results
                    isRunAllActive = false;
                    lastTestResult = runAllFailed == 0 ? "Passed" : "Failed";
                    lastTestName = "All Tests";
                    suitePassed = runAllPassed;
                    suiteFailed = runAllFailed;
                    suiteTotal = runAllTotal;
                    ClearRunAllState();
                }
                else
                {
                    bool allPassed = suiteFailed == 0;
                    lastTestResult = allPassed ? "Passed" : "Failed";
                    lastTestName = runningSuiteName;
                }
            }
            else
            {
                lastTestResult = result;
                lastTestName = testName;
            }

            // Exit Play Mode if the window entered it
            if (SessionState.GetBool(SHOULD_EXIT_PLAY_KEY, false))
            {
                SessionState.EraseBool(SHOULD_EXIT_PLAY_KEY);
                if (EditorApplication.isPlaying)
                    EditorApplication.isPlaying = false;
            }

            Repaint();
        }

        private void LoadSceneIfNeeded(TestFileInfo test)
        {
            if (string.IsNullOrEmpty(test.sceneName))
                return;

            if (!EditorApplication.isPlaying)
                return;

            string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (currentScene == test.sceneName)
                return;

            SceneInventoryTool.LoadScene(test.sceneName);
        }

        private void ReloadSceneForTest(TestFileInfo test)
        {
            if (!EditorApplication.isPlaying)
                return;

            string sceneName = test.sceneName;
            if (string.IsNullOrEmpty(sceneName))
                sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

            SceneInventoryTool.LoadScene(sceneName);
        }

        // =====================================================================
        // Run-All State Persistence (survives domain reload via SessionState)
        // =====================================================================

        private void SaveRunAllState()
        {
            SessionState.SetBool(RUN_ALL_ACTIVE_KEY, isRunAllActive);
            SessionState.SetInt(RUN_ALL_PASSED_KEY, runAllPassed);
            SessionState.SetInt(RUN_ALL_FAILED_KEY, runAllFailed);
            SessionState.SetInt(RUN_ALL_TOTAL_KEY, runAllTotal);

            var paths = new List<string>();
            if (runAllSuiteQueue != null)
                foreach (var s in runAllSuiteQueue)
                    paths.Add(s.directoryPath);
            SessionState.SetString(RUN_ALL_SUITES_KEY, string.Join(";", paths));
        }

        private void RestoreRunAllState()
        {
            isRunAllActive = SessionState.GetBool(RUN_ALL_ACTIVE_KEY, false);
            runAllPassed = SessionState.GetInt(RUN_ALL_PASSED_KEY, 0);
            runAllFailed = SessionState.GetInt(RUN_ALL_FAILED_KEY, 0);
            runAllTotal = SessionState.GetInt(RUN_ALL_TOTAL_KEY, 0);

            runAllSuiteQueue = new Queue<TestSuiteInfo>();
            string suitesStr = SessionState.GetString(RUN_ALL_SUITES_KEY, "");
            if (!string.IsNullOrEmpty(suitesStr))
            {
                string[] paths = suitesStr.Split(';');
                foreach (var path in paths)
                {
                    if (string.IsNullOrEmpty(path)) continue;
                    foreach (var suite in discoveredSuites)
                    {
                        if (suite.directoryPath == path)
                        {
                            runAllSuiteQueue.Enqueue(suite);
                            break;
                        }
                    }
                }
            }
        }

        private void ClearRunAllState()
        {
            SessionState.EraseBool(RUN_ALL_ACTIVE_KEY);
            SessionState.EraseString(RUN_ALL_SUITES_KEY);
            SessionState.SetInt(RUN_ALL_PASSED_KEY, 0);
            SessionState.SetInt(RUN_ALL_FAILED_KEY, 0);
            SessionState.SetInt(RUN_ALL_TOTAL_KEY, 0);
            isRunAllActive = false;
            runAllSuiteQueue = null;
            runAllPassed = 0;
            runAllFailed = 0;
            runAllTotal = 0;
        }
    }
}
