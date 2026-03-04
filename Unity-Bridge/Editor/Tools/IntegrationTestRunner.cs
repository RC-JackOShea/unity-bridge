using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityBridge
{
    /// <summary>
    /// Loads integration test JSON files from disk, executes them using PlayModeInteractor's
    /// action execution for the original 13 action types and PlayModeUIScanner for the 7 new
    /// element-based action types, and aggregates suite reports.
    /// </summary>
    public static class IntegrationTestRunner
    {
        private const string RESULTS_DIR = "C:/temp/integration_test_results";

        public static string RunTest(string testFilePath)
        {
            if (!EditorApplication.isPlaying)
                return "{\"success\":false,\"error\":\"IntegrationTestRunner requires Play Mode\"}";

            if (string.IsNullOrEmpty(testFilePath))
                return "{\"success\":false,\"error\":\"testFilePath is required\"}";

            try
            {
                if (!File.Exists(testFilePath))
                    return "{\"success\":false,\"error\":\"Test file not found: " + Esc(testFilePath) + "\"}";

                string json = File.ReadAllText(testFilePath);
                var test = SimpleJson.Parse(json);

                return ExecuteTestDefinition(test, testFilePath);
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        public static string RunSuite(string directoryPath)
        {
            if (!EditorApplication.isPlaying)
                return "{\"success\":false,\"error\":\"IntegrationTestRunner requires Play Mode\"}";

            if (string.IsNullOrEmpty(directoryPath))
                return "{\"success\":false,\"error\":\"directoryPath is required\"}";

            if (!Directory.Exists(directoryPath))
                return "{\"success\":false,\"error\":\"Directory not found: " + Esc(directoryPath) + "\"}";

            try
            {
                string[] testFiles = Directory.GetFiles(directoryPath, "*.json");
                if (testFiles.Length == 0)
                    return "{\"success\":false,\"error\":\"No .json test files found in: " + Esc(directoryPath) + "\"}";

                Array.Sort(testFiles);
                double suiteStart = EditorApplication.timeSinceStartup;
                var testResults = new List<string>();
                int passed = 0, failed = 0;
                string sceneName = SceneManager.GetActiveScene().name;

                foreach (var testFile in testFiles)
                {
                    try
                    {
                        string json = File.ReadAllText(testFile);
                        var test = SimpleJson.Parse(json);
                        string result = ExecuteTestDefinition(test, testFile);

                        var resultNode = SimpleJson.Parse(result);
                        string overallResult = resultNode.GetString("overallResult") ?? "Unknown";

                        if (overallResult == "Passed") passed++;
                        else failed++;

                        testResults.Add(result);

                        // Reload scene between tests for isolation
                        if (EditorApplication.isPlaying)
                        {
                            SceneManager.LoadScene(sceneName);
                            // Brief wait for scene reload
                            System.Threading.Thread.Sleep(500);
                        }
                    }
                    catch (Exception e)
                    {
                        failed++;
                        testResults.Add(string.Format(
                            "{{\"success\":false,\"file\":\"{0}\",\"overallResult\":\"Failed\",\"error\":\"{1}\"}}",
                            Esc(Path.GetFileName(testFile)), Esc(e.Message)));
                    }
                }

                double suiteDuration = EditorApplication.timeSinceStartup - suiteStart;
                int total = passed + failed;

                string suiteReport = string.Format(CultureInfo.InvariantCulture,
                    "{{\"success\":true,\"directory\":\"{0}\",\"duration\":{1:F1},\"summary\":{{\"total\":{2},\"passed\":{3},\"failed\":{4}}},\"tests\":[{5}]}}",
                    Esc(directoryPath.Replace("\\", "/")), suiteDuration, total, passed, failed,
                    string.Join(",", testResults.ToArray()));

                // Persist report
                SaveReport("suite_report", suiteReport);

                return suiteReport;
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        public static string ListTests(string directoryPath)
        {
            if (string.IsNullOrEmpty(directoryPath))
                return "{\"success\":false,\"error\":\"directoryPath is required\"}";

            if (!Directory.Exists(directoryPath))
                return "{\"success\":false,\"error\":\"Directory not found: " + Esc(directoryPath) + "\"}";

            try
            {
                string[] testFiles = Directory.GetFiles(directoryPath, "*.json");
                Array.Sort(testFiles);
                var entries = new List<string>();

                foreach (var testFile in testFiles)
                {
                    try
                    {
                        string json = File.ReadAllText(testFile);
                        var test = SimpleJson.Parse(json);
                        string name = test.GetString("name") ?? Path.GetFileNameWithoutExtension(testFile);
                        string description = test.GetString("description") ?? "";
                        var actions = test.Get("actions");
                        int actionCount = actions?.arr?.Count ?? 0;

                        var tags = test.Get("tags");
                        var tagStrs = new List<string>();
                        if (tags?.arr != null)
                        {
                            foreach (var tag in tags.arr)
                                tagStrs.Add("\"" + Esc(tag.AsString()) + "\"");
                        }

                        entries.Add(string.Format(
                            "{{\"file\":\"{0}\",\"name\":\"{1}\",\"description\":\"{2}\",\"actionCount\":{3},\"tags\":[{4}]}}",
                            Esc(Path.GetFileName(testFile)), Esc(name), Esc(description), actionCount,
                            string.Join(",", tagStrs.ToArray())));
                    }
                    catch
                    {
                        entries.Add(string.Format(
                            "{{\"file\":\"{0}\",\"name\":\"(parse error)\",\"actionCount\":0,\"tags\":[]}}",
                            Esc(Path.GetFileName(testFile))));
                    }
                }

                return string.Format("{{\"success\":true,\"directory\":\"{0}\",\"testCount\":{1},\"tests\":[{2}]}}",
                    Esc(directoryPath.Replace("\\", "/")), entries.Count,
                    string.Join(",", entries.ToArray()));
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        public static string RunByTag(string directoryPath, string tag)
        {
            if (!EditorApplication.isPlaying)
                return "{\"success\":false,\"error\":\"IntegrationTestRunner requires Play Mode\"}";

            if (string.IsNullOrEmpty(directoryPath))
                return "{\"success\":false,\"error\":\"directoryPath is required\"}";
            if (string.IsNullOrEmpty(tag))
                return "{\"success\":false,\"error\":\"tag is required\"}";

            if (!Directory.Exists(directoryPath))
                return "{\"success\":false,\"error\":\"Directory not found: " + Esc(directoryPath) + "\"}";

            try
            {
                string[] testFiles = Directory.GetFiles(directoryPath, "*.json");
                Array.Sort(testFiles);
                var matchingFiles = new List<string>();

                foreach (var testFile in testFiles)
                {
                    try
                    {
                        string json = File.ReadAllText(testFile);
                        var test = SimpleJson.Parse(json);
                        var tags = test.Get("tags");
                        if (tags?.arr != null)
                        {
                            foreach (var t in tags.arr)
                            {
                                if (string.Equals(t.AsString(), tag, StringComparison.OrdinalIgnoreCase))
                                {
                                    matchingFiles.Add(testFile);
                                    break;
                                }
                            }
                        }
                    }
                    catch { }
                }

                if (matchingFiles.Count == 0)
                    return "{\"success\":false,\"error\":\"No tests found with tag: " + Esc(tag) + "\"}";

                double suiteStart = EditorApplication.timeSinceStartup;
                var testResults = new List<string>();
                int passed = 0, failed = 0;
                string sceneName = SceneManager.GetActiveScene().name;

                foreach (var testFile in matchingFiles)
                {
                    try
                    {
                        string json = File.ReadAllText(testFile);
                        var test = SimpleJson.Parse(json);
                        string result = ExecuteTestDefinition(test, testFile);

                        var resultNode = SimpleJson.Parse(result);
                        string overallResult = resultNode.GetString("overallResult") ?? "Unknown";

                        if (overallResult == "Passed") passed++;
                        else failed++;

                        testResults.Add(result);

                        if (EditorApplication.isPlaying)
                        {
                            SceneManager.LoadScene(sceneName);
                            System.Threading.Thread.Sleep(500);
                        }
                    }
                    catch (Exception e)
                    {
                        failed++;
                        testResults.Add(string.Format(
                            "{{\"success\":false,\"file\":\"{0}\",\"overallResult\":\"Failed\",\"error\":\"{1}\"}}",
                            Esc(Path.GetFileName(testFile)), Esc(e.Message)));
                    }
                }

                double suiteDuration = EditorApplication.timeSinceStartup - suiteStart;
                int total = passed + failed;

                string suiteReport = string.Format(CultureInfo.InvariantCulture,
                    "{{\"success\":true,\"tag\":\"{0}\",\"directory\":\"{1}\",\"duration\":{2:F1},\"summary\":{{\"total\":{3},\"passed\":{4},\"failed\":{5}}},\"tests\":[{6}]}}",
                    Esc(tag), Esc(directoryPath.Replace("\\", "/")), suiteDuration, total, passed, failed,
                    string.Join(",", testResults.ToArray()));

                SaveReport("tag_" + tag + "_report", suiteReport);

                return suiteReport;
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        // --- Core execution ---

        private static string ExecuteTestDefinition(SimpleJson.JsonNode test, string testFilePath)
        {
            string testName = test.GetString("name") ?? "Unnamed";
            var actions = test.Get("actions");

            if (actions == null || actions.arr == null || actions.arr.Count == 0)
                return "{\"success\":false,\"error\":\"No actions in test: " + Esc(testName) + "\"}";

            // Register log callback
            if (!PlayModeInteractor.logCallbackRegistered)
            {
                Application.logMessageReceived += OnLogReceived;
                PlayModeInteractor.logCallbackRegistered = true;
            }

            // Process setup
            var setup = test.Get("setup");
            if (setup != null)
            {
                bool clearLogs = setup.Get("clearLogs")?.AsBool() ?? false;
                if (clearLogs) PlayModeInteractor.capturedLogs.Clear();

                float waitAfterPlay = setup.Get("waitAfterPlay")?.AsFloat() ?? 0f;
                if (waitAfterPlay > 0)
                    System.Threading.Thread.Sleep((int)(waitAfterPlay * 1000));
            }

            double totalStart = EditorApplication.timeSinceStartup;
            var actionResults = new List<string>();
            var screenshots = new List<string>();
            var errors = new List<string>();
            string overallResult = "Passed";

            // Teardown config
            var teardown = test.Get("teardown");
            bool screenshotOnFailure = teardown?.Get("screenshotOnFailure")?.AsBool() ?? false;
            string failurePath = teardown?.GetString("failurePath") ?? "C:/temp/integration_test_failures/";

            for (int i = 0; i < actions.arr.Count; i++)
            {
                var action = actions.arr[i];
                string actionType = action.GetString("type") ?? "";
                double actionStart = EditorApplication.timeSinceStartup;

                try
                {
                    string result;

                    // Route to appropriate handler
                    if (IsNewActionType(actionType))
                        result = ExecuteNewAction(action, actionType, screenshots);
                    else
                        result = PlayModeInteractor.ExecuteAction(action, actionType, screenshots);

                    double duration = EditorApplication.timeSinceStartup - actionStart;
                    bool success = !result.StartsWith("FAIL:");
                    string details = success ? result : result.Substring(5);

                    if (!success)
                    {
                        overallResult = "Failed";
                        errors.Add("Action " + i + " (" + actionType + "): " + details);

                        // Capture failure screenshot
                        if (screenshotOnFailure && EditorApplication.isPlaying)
                        {
                            try
                            {
                                if (!Directory.Exists(failurePath))
                                    Directory.CreateDirectory(failurePath);
                                string failFile = Path.Combine(failurePath,
                                    testName.Replace(" ", "_") + "_action" + i + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png")
                                    .Replace("\\", "/");
                                var failScreenshot = new SimpleJson.JsonNode();
                                failScreenshot.obj = new Dictionary<string, SimpleJson.JsonNode>();
                                failScreenshot.obj["path"] = new SimpleJson.JsonNode { str = failFile };
                                var dummyList = new List<string>();
                                PlayModeInteractor.ExecuteAction(failScreenshot, "screenshot", dummyList);
                            }
                            catch { }
                        }

                        // Check critical flag
                        bool critical = action.Get("critical")?.AsBool() ?? false;
                        if (critical)
                        {
                            actionResults.Add(string.Format(CultureInfo.InvariantCulture,
                                "{{\"index\":{0},\"type\":\"{1}\",\"result\":\"failed\",\"duration\":{2},\"details\":\"{3}\"}}",
                                i, Esc(actionType), duration, Esc(details)));
                            break;
                        }
                    }

                    actionResults.Add(string.Format(CultureInfo.InvariantCulture,
                        "{{\"index\":{0},\"type\":\"{1}\",\"result\":\"{2}\",\"duration\":{3},\"details\":\"{4}\"}}",
                        i, Esc(actionType), success ? "success" : "failed", duration, Esc(details)));
                }
                catch (Exception e)
                {
                    double duration = EditorApplication.timeSinceStartup - actionStart;
                    overallResult = "Failed";
                    errors.Add("Action " + i + " (" + actionType + "): " + e.Message);
                    actionResults.Add(string.Format(CultureInfo.InvariantCulture,
                        "{{\"index\":{0},\"type\":\"{1}\",\"result\":\"error\",\"duration\":{2},\"details\":\"{3}\"}}",
                        i, Esc(actionType), duration, Esc(e.Message)));
                }
            }

            double totalDuration = EditorApplication.timeSinceStartup - totalStart;

            var screenshotEntries = new List<string>();
            foreach (var s in screenshots) screenshotEntries.Add("\"" + Esc(s) + "\"");

            var errorEntries = new List<string>();
            foreach (var e in errors) errorEntries.Add("\"" + Esc(e) + "\"");

            string report = string.Format(CultureInfo.InvariantCulture,
                "{{\"success\":true,\"name\":\"{0}\",\"file\":\"{1}\",\"overallResult\":\"{2}\",\"duration\":{3:F1},\"actionResults\":[{4}],\"screenshots\":[{5}],\"errors\":[{6}]}}",
                Esc(testName), Esc(Path.GetFileName(testFilePath)), overallResult, totalDuration,
                string.Join(",", actionResults.ToArray()),
                string.Join(",", screenshotEntries.ToArray()),
                string.Join(",", errorEntries.ToArray()));

            // Persist individual test report
            SaveReport(testName.Replace(" ", "_"), report);

            return report;
        }

        private static bool IsNewActionType(string actionType)
        {
            switch (actionType)
            {
                case "scan_ui":
                case "tap_element":
                case "assert_active":
                case "assert_text":
                case "assert_interactable":
                case "assert_not_visible":
                case "assert_screenshot":
                    return true;
                default:
                    return false;
            }
        }

        private static string ExecuteNewAction(SimpleJson.JsonNode action, string actionType, List<string> screenshots)
        {
            switch (actionType)
            {
                case "scan_ui":
                    return DoScanUI();
                case "tap_element":
                    return DoTapElement(action);
                case "assert_active":
                    return DoAssertActive(action);
                case "assert_text":
                    return DoAssertText(action);
                case "assert_interactable":
                    return DoAssertInteractable(action);
                case "assert_not_visible":
                    return DoAssertNotVisible(action);
                case "assert_screenshot":
                    return DoAssertScreenshot(action, screenshots);
                default:
                    return "FAIL:Unknown new action type: " + actionType;
            }
        }

        private static string DoScanUI()
        {
            if (!EditorApplication.isPlaying)
                return "FAIL:Not in Play Mode";

            string scanResult = PlayModeUIScanner.ScanUI();
            var node = SimpleJson.Parse(scanResult);
            bool success = node.Get("success")?.AsBool() ?? false;

            if (!success)
                return "FAIL:UI scan failed: " + (node.GetString("error") ?? "unknown");

            return "UI scan completed";
        }

        private static string DoTapElement(SimpleJson.JsonNode action)
        {
            if (!EditorApplication.isPlaying)
                return "FAIL:Not in Play Mode";

            string path = action.GetString("path") ?? "";
            if (string.IsNullOrEmpty(path))
                return "FAIL:tap_element requires 'path' field";

            var go = PlayModeUIScanner.FindElementGameObject(path);
            if (go == null)
                return "FAIL:Element not found: " + path;

            if (!go.activeInHierarchy)
                return "FAIL:Element is not active: " + path;

            var rootCanvas = PlayModeUIScanner.GetRootCanvas(go);
            if (rootCanvas == null)
                return "FAIL:No root canvas found for element: " + path;

            var center = PlayModeUIScanner.GetScreenCenter(go, rootCanvas);
            if (center.x < 0 || center.y < 0)
                return "FAIL:Could not compute screen position for: " + path;

            // Use InputEmulator via reflection (same as PlayModeInteractor)
            var emulatorType = FindType("UnityBridge.InputEmulator");
            if (emulatorType == null)
                return "FAIL:InputEmulator not found";

            var tapMethod = emulatorType.GetMethod("SimulateTap", BindingFlags.Public | BindingFlags.Static);
            if (tapMethod == null)
                return "FAIL:SimulateTap method not found";

            float duration = action.Get("duration")?.AsFloat() ?? 0.1f;
            tapMethod.Invoke(null, new object[] { center.x, center.y, duration });

            return "Tapped element '" + path + "' at (" + center.x.ToString("F0") + ", " + center.y.ToString("F0") + ")";
        }

        private static string DoAssertActive(SimpleJson.JsonNode action)
        {
            string path = action.GetString("path") ?? "";
            if (string.IsNullOrEmpty(path))
                return "FAIL:assert_active requires 'path' field";

            bool expected = action.Get("expected")?.AsBool() ?? true;

            var go = PlayModeUIScanner.FindElementGameObject(path);

            if (expected)
            {
                if (go == null)
                    return "FAIL:Element not found (expected active): " + path;
                if (!go.activeInHierarchy)
                    return "FAIL:Element is not active in hierarchy: " + path;
                return "Element is active: " + path;
            }
            else
            {
                if (go == null || !go.activeInHierarchy)
                    return "Element is inactive (as expected): " + path;
                return "FAIL:Element is active (expected inactive): " + path;
            }
        }

        private static string DoAssertText(SimpleJson.JsonNode action)
        {
            string path = action.GetString("path") ?? "";
            if (string.IsNullOrEmpty(path))
                return "FAIL:assert_text requires 'path' field";

            string expected = action.GetString("expected") ?? "";
            bool containsMode = action.Get("contains")?.AsBool() ?? false;

            var go = PlayModeUIScanner.FindElementGameObject(path);
            if (go == null)
                return "FAIL:Element not found: " + path;

            string actualText = PlayModeUIScanner.GetElementText(go);
            if (actualText == null)
                return "FAIL:No text component found on: " + path;

            if (containsMode)
            {
                if (actualText.Contains(expected))
                    return "Text contains '" + expected + "' (actual: '" + actualText + "')";
                return "FAIL:Text does not contain '" + expected + "' (actual: '" + actualText + "')";
            }
            else
            {
                if (actualText == expected)
                    return "Text matches: '" + expected + "'";
                return "FAIL:Text mismatch — expected '" + expected + "', got '" + actualText + "'";
            }
        }

        private static string DoAssertInteractable(SimpleJson.JsonNode action)
        {
            string path = action.GetString("path") ?? "";
            if (string.IsNullOrEmpty(path))
                return "FAIL:assert_interactable requires 'path' field";

            var go = PlayModeUIScanner.FindElementGameObject(path);
            if (go == null)
                return "FAIL:Element not found: " + path;

            if (!go.activeInHierarchy)
                return "FAIL:Element is not active: " + path;

            // Check for Selectable components (Button, Toggle, Slider, InputField, Dropdown)
            var selectable = go.GetComponent<UnityEngine.UI.Selectable>();
            if (selectable == null)
                return "FAIL:No interactable component on: " + path;

            if (!selectable.interactable)
                return "FAIL:Element exists but is not interactable: " + path;

            return "Element is interactable: " + path;
        }

        private static string DoAssertNotVisible(SimpleJson.JsonNode action)
        {
            string path = action.GetString("path") ?? "";
            if (string.IsNullOrEmpty(path))
                return "FAIL:assert_not_visible requires 'path' field";

            var go = PlayModeUIScanner.FindElementGameObject(path);

            // Not found = not visible
            if (go == null)
                return "Element not found (not visible): " + path;

            // Inactive = not visible
            if (!go.activeInHierarchy)
                return "Element is inactive (not visible): " + path;

            // Alpha = 0 means not visible
            float alpha = PlayModeUIScanner.GetEffectiveAlpha(go);
            if (alpha <= 0f)
                return "Element has zero alpha (not visible): " + path;

            return "FAIL:Element is visible (active with alpha=" + alpha.ToString("F2") + "): " + path;
        }

        private static string DoAssertScreenshot(SimpleJson.JsonNode action, List<string> screenshots)
        {
            if (!EditorApplication.isPlaying)
                return "FAIL:Not in Play Mode";

            string baseline = action.GetString("baseline") ?? "";
            if (string.IsNullOrEmpty(baseline))
                return "FAIL:assert_screenshot requires 'baseline' field";

            float threshold = action.Get("threshold")?.AsFloat() ?? 0.1f;

            // Capture current screenshot
            string currentPath = "C:/temp/integration_test_compare_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
            var screenshotAction = new SimpleJson.JsonNode();
            screenshotAction.obj = new Dictionary<string, SimpleJson.JsonNode>();
            screenshotAction.obj["path"] = new SimpleJson.JsonNode { str = currentPath };
            string captureResult = PlayModeInteractor.ExecuteAction(screenshotAction, "screenshot", screenshots);

            if (captureResult.StartsWith("FAIL:"))
                return "FAIL:Screenshot capture failed: " + captureResult.Substring(5);

            // Compare using ScreenshotValidator
            string compareResult = ScreenshotValidator.CompareScreenshots(baseline, currentPath);
            var compareNode = SimpleJson.Parse(compareResult);

            bool compareSuccess = compareNode.Get("success")?.AsBool() ?? false;
            if (!compareSuccess)
                return "FAIL:Screenshot comparison error: " + (compareNode.GetString("error") ?? "unknown");

            bool match = compareNode.Get("match")?.AsBool() ?? false;
            float diffPercent = compareNode.Get("differencePercent")?.AsFloat() ?? 100f;

            if (match || diffPercent <= threshold)
                return "Screenshot matches baseline (diff=" + diffPercent.ToString("F4") + "%, threshold=" + threshold + "%)";

            return "FAIL:Screenshot differs from baseline (diff=" + diffPercent.ToString("F4") + "%, threshold=" + threshold + "%)";
        }

        // --- Coroutine-based async execution ---
        // Runs one action per rendered frame via StartCoroutine on a MonoBehaviour.
        // yield return null returns control to Unity's player loop, which renders
        // the Game view before resuming the coroutine. No hacks needed.

        /// <summary>
        /// MonoBehaviour that owns test execution via StartCoroutine. Runs inside
        /// the player loop so Unity renders between actions automatically.
        /// </summary>
        private class IntegrationTestBehaviour : MonoBehaviour
        {
            internal static IntegrationTestBehaviour ActiveInstance;

            string testName, testFile;
            List<SimpleJson.JsonNode> actions;
            int actionIndex;
            List<string> actionResults, screenshots, errors;
            string overallResult, finalReport;
            bool screenshotOnFailure;
            string failurePath;
            double totalStart;
            bool running;
            float waitAfterPlay;
            bool previousRunInBackground;

            void Awake()
            {
                if (ActiveInstance != null && ActiveInstance != this)
                    DestroyImmediate(ActiveInstance.gameObject);
                ActiveInstance = this;
                DontDestroyOnLoad(gameObject);
            }

            void OnDestroy()
            {
                if (ActiveInstance == this)
                    ActiveInstance = null;
            }

            internal void StartTest(SimpleJson.JsonNode test, string filePath)
            {
                testName = test.GetString("name") ?? "Unnamed";
                testFile = filePath;

                var actionsNode = test.Get("actions");
                actions = actionsNode.arr;
                actionIndex = 0;
                actionResults = new List<string>();
                screenshots = new List<string>();
                errors = new List<string>();
                overallResult = "Passed";
                finalReport = null;
                totalStart = EditorApplication.timeSinceStartup;

                // Register log callback
                if (!PlayModeInteractor.logCallbackRegistered)
                {
                    Application.logMessageReceived += OnLogReceived;
                    PlayModeInteractor.logCallbackRegistered = true;
                }

                // Setup
                var setup = test.Get("setup");
                waitAfterPlay = 0f;
                if (setup != null)
                {
                    bool clearLogs = setup.Get("clearLogs")?.AsBool() ?? false;
                    if (clearLogs) PlayModeInteractor.capturedLogs.Clear();
                    waitAfterPlay = setup.Get("waitAfterPlay")?.AsFloat() ?? 0f;
                }

                // Teardown config
                var teardown = test.Get("teardown");
                screenshotOnFailure = teardown?.Get("screenshotOnFailure")?.AsBool() ?? false;
                failurePath = teardown?.GetString("failurePath") ?? "C:/temp/integration_test_failures/";

                // Ensure player loop runs even when Unity loses focus
                previousRunInBackground = Application.runInBackground;
                Application.runInBackground = true;

                running = true;
                StartCoroutine(RunTestCoroutine());
            }

            internal string GetStatus()
            {
                if (running)
                {
                    return string.Format(CultureInfo.InvariantCulture,
                        "{{\"success\":true,\"status\":\"running\",\"progress\":{0},\"total\":{1},\"name\":\"{2}\"}}",
                        actionIndex, actions.Count, Esc(testName));
                }

                if (finalReport != null)
                    return finalReport;

                return "{\"success\":true,\"status\":\"idle\"}";
            }

            IEnumerator RunTestCoroutine()
            {
                // Initial wait after play mode
                if (waitAfterPlay > 0f)
                    yield return new WaitForSeconds(waitAfterPlay);

                for (actionIndex = 0; actionIndex < actions.Count; actionIndex++)
                {
                    if (!EditorApplication.isPlaying)
                    {
                        overallResult = "Failed";
                        errors.Add("Play Mode exited during test");
                        break;
                    }

                    var action = actions[actionIndex];
                    string actionType = action.GetString("type") ?? "";
                    double actionStart = EditorApplication.timeSinceStartup;

                    // Track yield type outside try-catch (C# forbids yield inside try-catch)
                    float yieldWaitSeconds = -1f;
                    int yieldWaitFrames = -1;
                    bool shouldBreak = false;

                    try
                    {
                        // wait_seconds — record result, defer yield
                        if (actionType == "wait_seconds")
                        {
                            float dur = action.Get("duration")?.AsFloat() ?? 1.0f;
                            actionResults.Add(string.Format(CultureInfo.InvariantCulture,
                                "{{\"index\":{0},\"type\":\"wait_seconds\",\"result\":\"success\",\"duration\":{1:F3},\"details\":\"Waiting {1:F1}s\"}}",
                                actionIndex, dur));
                            yieldWaitSeconds = dur;
                        }
                        // wait_frames — record result, defer yield
                        else if (actionType == "wait_frames")
                        {
                            int frames = (int)(action.Get("count")?.AsFloat() ?? 1);
                            actionResults.Add(string.Format(CultureInfo.InvariantCulture,
                                "{{\"index\":{0},\"type\":\"wait_frames\",\"result\":\"success\",\"duration\":0,\"details\":\"Waiting {1} frames\"}}",
                                actionIndex, frames));
                            yieldWaitFrames = frames;
                        }
                        else
                        {
                            string result;
                            if (IsNewActionType(actionType))
                                result = ExecuteNewAction(action, actionType, screenshots);
                            else
                                result = PlayModeInteractor.ExecuteAction(action, actionType, screenshots);

                            double duration = EditorApplication.timeSinceStartup - actionStart;
                            bool success = !result.StartsWith("FAIL:");
                            string details = success ? result : result.Substring(5);

                            if (!success)
                            {
                                overallResult = "Failed";
                                errors.Add("Action " + actionIndex + " (" + actionType + "): " + details);

                                // Capture failure screenshot
                                if (screenshotOnFailure && EditorApplication.isPlaying)
                                {
                                    try
                                    {
                                        if (!Directory.Exists(failurePath))
                                            Directory.CreateDirectory(failurePath);
                                        string failFile = Path.Combine(failurePath,
                                            testName.Replace(" ", "_") + "_action" + actionIndex
                                            + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png")
                                            .Replace("\\", "/");
                                        var failNode = new SimpleJson.JsonNode();
                                        failNode.obj = new Dictionary<string, SimpleJson.JsonNode>();
                                        failNode.obj["path"] = new SimpleJson.JsonNode { str = failFile };
                                        var dummyList = new List<string>();
                                        PlayModeInteractor.ExecuteAction(failNode, "screenshot", dummyList);
                                    }
                                    catch { }
                                }

                                // Critical failure — stop test
                                bool critical = action.Get("critical")?.AsBool() ?? false;
                                if (critical)
                                {
                                    actionResults.Add(string.Format(CultureInfo.InvariantCulture,
                                        "{{\"index\":{0},\"type\":\"{1}\",\"result\":\"failed\",\"duration\":{2},\"details\":\"{3}\"}}",
                                        actionIndex, Esc(actionType), duration, Esc(details)));
                                    shouldBreak = true;
                                }
                            }

                            if (!shouldBreak)
                            {
                                actionResults.Add(string.Format(CultureInfo.InvariantCulture,
                                    "{{\"index\":{0},\"type\":\"{1}\",\"result\":\"{2}\",\"duration\":{3},\"details\":\"{4}\"}}",
                                    actionIndex, Esc(actionType), success ? "success" : "failed", duration, Esc(details)));
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        double duration = EditorApplication.timeSinceStartup - actionStart;
                        overallResult = "Failed";
                        errors.Add("Action " + actionIndex + " (" + actionType + "): " + e.Message);
                        actionResults.Add(string.Format(CultureInfo.InvariantCulture,
                            "{{\"index\":{0},\"type\":\"{1}\",\"result\":\"error\",\"duration\":{2},\"details\":\"{3}\"}}",
                            actionIndex, Esc(actionType), duration, Esc(e.Message)));
                    }

                    // Yields must be outside try-catch per C# spec
                    if (shouldBreak)
                        break;

                    if (yieldWaitSeconds >= 0f)
                    {
                        yield return new WaitForSeconds(yieldWaitSeconds);
                        continue;
                    }

                    if (yieldWaitFrames >= 0)
                    {
                        for (int f = 0; f < yieldWaitFrames; f++)
                            yield return null;
                        continue;
                    }

                    // Default: yield one frame — Unity renders the Game view before resuming
                    yield return null;
                }

                BuildFinalReport();
            }

            void BuildFinalReport()
            {
                running = false;
                Application.runInBackground = previousRunInBackground;
                double totalDuration = EditorApplication.timeSinceStartup - totalStart;

                var screenshotEntries = new List<string>();
                foreach (var s in screenshots) screenshotEntries.Add("\"" + Esc(s) + "\"");

                var errorEntries = new List<string>();
                foreach (var e in errors) errorEntries.Add("\"" + Esc(e) + "\"");

                finalReport = string.Format(CultureInfo.InvariantCulture,
                    "{{\"success\":true,\"status\":\"completed\",\"name\":\"{0}\",\"file\":\"{1}\",\"overallResult\":\"{2}\",\"duration\":{3:F1},\"actionResults\":[{4}],\"screenshots\":[{5}],\"errors\":[{6}]}}",
                    Esc(testName), Esc(Path.GetFileName(testFile)), overallResult, totalDuration,
                    string.Join(",", actionResults.ToArray()),
                    string.Join(",", screenshotEntries.ToArray()),
                    string.Join(",", errorEntries.ToArray()));

                SaveReport(testName.Replace(" ", "_"), finalReport);
            }
        }

        public static string RunTestAsync(string testFilePath)
        {
            if (!EditorApplication.isPlaying)
                return "{\"success\":false,\"error\":\"RunTestAsync requires Play Mode\"}";

            if (IntegrationTestBehaviour.ActiveInstance != null
                && IntegrationTestBehaviour.ActiveInstance.GetStatus().Contains("\"running\""))
                return "{\"success\":false,\"error\":\"A test is already running\"}";

            if (string.IsNullOrEmpty(testFilePath) || !File.Exists(testFilePath))
                return "{\"success\":false,\"error\":\"Test file not found: " + Esc(testFilePath ?? "") + "\"}";

            try
            {
                string json = File.ReadAllText(testFilePath);
                var test = SimpleJson.Parse(json);

                string testName = test.GetString("name") ?? "Unnamed";
                var actions = test.Get("actions");
                if (actions == null || actions.arr == null || actions.arr.Count == 0)
                    return "{\"success\":false,\"error\":\"No actions in test\"}";

                // Clean up previous instance
                if (IntegrationTestBehaviour.ActiveInstance != null)
                    UnityEngine.Object.DestroyImmediate(IntegrationTestBehaviour.ActiveInstance.gameObject);

                var go = new GameObject("[IntegrationTestRunner]");
                var behaviour = go.AddComponent<IntegrationTestBehaviour>();
                behaviour.StartTest(test, testFilePath);

                return "{\"success\":true,\"status\":\"started\",\"name\":\"" + Esc(testName)
                    + "\",\"actionCount\":" + actions.arr.Count + "}";
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        public static string GetTestStatus()
        {
            if (IntegrationTestBehaviour.ActiveInstance == null)
                return "{\"success\":true,\"status\":\"idle\"}";

            return IntegrationTestBehaviour.ActiveInstance.GetStatus();
        }

        // --- Helpers ---

        private static void OnLogReceived(string condition, string stackTrace, LogType type)
        {
            PlayModeInteractor.capturedLogs.Add(condition);
        }

        private static void SaveReport(string name, string json)
        {
            try
            {
                if (!Directory.Exists(RESULTS_DIR))
                    Directory.CreateDirectory(RESULTS_DIR);

                string fileName = name + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json";
                string filePath = Path.Combine(RESULTS_DIR, fileName);
                File.WriteAllText(filePath, json);
            }
            catch { }
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = asm.GetType(fullName);
                if (type != null) return type;
            }
            return null;
        }

        private static string Esc(string str)
        {
            if (str == null) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
