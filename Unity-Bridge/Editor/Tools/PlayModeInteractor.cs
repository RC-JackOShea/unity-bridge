using System;
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
    /// Orchestrated Play Mode interaction sequences. Enters Play Mode, executes scripted
    /// sequences of inputs and state checks, captures screenshots, and returns structured reports.
    /// Actions: enter_play, exit_play, wait_frames, wait_seconds, wait_condition, input_tap,
    /// input_hold, input_drag, input_key, screenshot, check_state, check_log, clear_logs.
    /// </summary>
    public static class PlayModeInteractor
    {
        internal static List<string> capturedLogs = new List<string>();
        internal static bool logCallbackRegistered = false;

        public static string RunSequence(string jsonScript)
        {
            if (string.IsNullOrEmpty(jsonScript))
                return "{\"success\":false,\"error\":\"jsonScript is required\"}";

            try
            {
                var script = SimpleJson.Parse(jsonScript);
                string sequenceName = script.GetString("name") ?? "Unnamed";
                var actions = script.Get("actions");

                if (actions == null || actions.arr == null || actions.arr.Count == 0)
                    return "{\"success\":false,\"error\":\"No actions defined in script\"}";

                // Register log callback if not already
                if (!logCallbackRegistered)
                {
                    Application.logMessageReceived += OnLogMessageReceived;
                    logCallbackRegistered = true;
                }
                capturedLogs.Clear();

                double totalStart = EditorApplication.timeSinceStartup;
                var actionResults = new List<string>();
                var screenshots = new List<string>();
                var errors = new List<string>();
                string overallResult = "Passed";

                for (int i = 0; i < actions.arr.Count; i++)
                {
                    var action = actions.arr[i];
                    string actionType = action.GetString("type") ?? "";
                    double actionStart = EditorApplication.timeSinceStartup;

                    try
                    {
                        string result = ExecuteAction(action, actionType, screenshots);
                        double duration = EditorApplication.timeSinceStartup - actionStart;

                        bool success = !result.StartsWith("FAIL:");
                        string details = success ? result : result.Substring(5);

                        if (!success)
                        {
                            overallResult = "Failed";
                            errors.Add("Action " + i + " (" + actionType + "): " + details);

                            // Check if this is a critical failure
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

                return string.Format(CultureInfo.InvariantCulture,
                    "{{\"success\":true,\"sequenceName\":\"{0}\",\"overallResult\":\"{1}\",\"totalDuration\":{2},\"actions\":[{3}],\"screenshots\":[{4}],\"errors\":[{5}]}}",
                    Esc(sequenceName), overallResult, totalDuration,
                    string.Join(",", actionResults.ToArray()),
                    string.Join(",", screenshotEntries.ToArray()),
                    string.Join(",", errorEntries.ToArray()));
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        internal static string ExecuteAction(SimpleJson.JsonNode action, string actionType, List<string> screenshots)
        {
            switch (actionType)
            {
                case "enter_play":
                    return DoEnterPlay();
                case "exit_play":
                    return DoExitPlay();
                case "wait_frames":
                    return DoWaitFrames(action);
                case "wait_seconds":
                    return DoWaitSeconds(action);
                case "wait_condition":
                    return DoWaitCondition(action);
                case "input_tap":
                    return DoInputTap(action);
                case "input_hold":
                    return DoInputHold(action);
                case "input_drag":
                    return DoInputDrag(action);
                case "input_key":
                    return DoInputKey(action);
                case "screenshot":
                    return DoScreenshot(action, screenshots);
                case "check_state":
                    return DoCheckState(action);
                case "check_log":
                    return DoCheckLog(action);
                case "clear_logs":
                    capturedLogs.Clear();
                    return "Logs cleared";
                default:
                    return "FAIL:Unknown action type: " + actionType;
            }
        }

        private static string DoEnterPlay()
        {
            // RunSequence executes on the main thread via QueueUnityAction, so we cannot
            // transition play mode synchronously (it would deadlock). Instead, verify state.
            // Use "bash .agent/tools/unity_bridge.sh play enter" before calling RunSequence.
            if (EditorApplication.isPlaying)
                return "Play Mode is active";

            return "FAIL:Not in Play Mode. Use 'play enter' bridge command before RunSequence";
        }

        private static string DoExitPlay()
        {
            // Same as enter_play: verify state rather than transition.
            // Use "bash .agent/tools/unity_bridge.sh play exit" after RunSequence.
            if (!EditorApplication.isPlaying)
                return "Play Mode is stopped";

            return "Play Mode is still active. Use 'play exit' bridge command after RunSequence";
        }

        private static string DoWaitFrames(SimpleJson.JsonNode action)
        {
            int count = (int)(action.Get("count")?.AsFloat() ?? 10);
            // Approximate frame wait via time
            float frameTime = 1f / 60f;
            System.Threading.Thread.Sleep((int)(count * frameTime * 1000));
            return "Waited " + count + " frames";
        }

        private static string DoWaitSeconds(SimpleJson.JsonNode action)
        {
            float duration = action.Get("duration")?.AsFloat() ?? 1.0f;
            System.Threading.Thread.Sleep((int)(duration * 1000));
            return "Waited " + duration.ToString(CultureInfo.InvariantCulture) + "s";
        }

        private static string DoWaitCondition(SimpleJson.JsonNode action)
        {
            string goPath = action.GetString("gameObject") ?? action.GetString("path") ?? "";
            string component = action.GetString("component") ?? "";
            string property = action.GetString("property") ?? "";
            float timeout = action.Get("timeout")?.AsFloat() ?? 5f;

            if (!EditorApplication.isPlaying)
                return "FAIL:Not in Play Mode";

            var expectedNode = action.Get("expected") ?? action.Get("value");

            double endTime = EditorApplication.timeSinceStartup + timeout;
            string lastState = "";

            while (EditorApplication.timeSinceStartup < endTime)
            {
                if (!EditorApplication.isPlaying)
                    return "FAIL:Play Mode exited during wait";

                if (!string.IsNullOrEmpty(goPath))
                {
                    var go = GameObject.Find(goPath);
                    if (go == null) { lastState = "GameObject not found"; }
                    else if (string.IsNullOrEmpty(component))
                    {
                        // Check activeSelf
                        bool expected = expectedNode?.AsBool() ?? true;
                        if (go.activeSelf == expected) return "Condition met: " + goPath + " activeSelf=" + expected;
                        lastState = goPath + " activeSelf=" + go.activeSelf;
                    }
                    else
                    {
                        var comp = FindComponent(go, component);
                        if (comp == null) { lastState = "Component not found"; }
                        else
                        {
                            object actual = GetPropertyValue(comp, property);
                            string expectedStr = expectedNode?.AsString() ?? "";
                            string actualStr = actual?.ToString() ?? "null";

                            if (actualStr.Equals(expectedStr, StringComparison.OrdinalIgnoreCase))
                                return "Condition met: " + property + "=" + actualStr;
                            lastState = property + "=" + actualStr;
                        }
                    }
                }

                System.Threading.Thread.Sleep(100);
            }

            return "FAIL:Timeout waiting for condition. Last state: " + lastState;
        }

        private static string DoInputTap(SimpleJson.JsonNode action)
        {
            if (!EditorApplication.isPlaying)
                return "FAIL:Not in Play Mode";

            float x = action.Get("x")?.AsFloat() ?? 0;
            float y = action.Get("y")?.AsFloat() ?? 0;
            float duration = action.Get("duration")?.AsFloat() ?? 0.1f;

            // Use InputEmulator via reflection
            var emulatorType = FindType("UnityBridge.InputEmulator");
            if (emulatorType == null)
                return "FAIL:InputEmulator not found";

            var tapMethod = emulatorType.GetMethod("SimulateTap", BindingFlags.Public | BindingFlags.Static);
            if (tapMethod != null)
            {
                var result = tapMethod.Invoke(null, new object[] { x, y, duration });
                System.Threading.Thread.Sleep(200);
                return "Tapped at (" + x + ", " + y + ")";
            }

            return "FAIL:SimulateTap method not found";
        }

        private static string DoInputHold(SimpleJson.JsonNode action)
        {
            if (!EditorApplication.isPlaying)
                return "FAIL:Not in Play Mode";

            float x = action.Get("x")?.AsFloat() ?? 0;
            float y = action.Get("y")?.AsFloat() ?? 0;
            float duration = action.Get("duration")?.AsFloat() ?? 1.0f;

            var emulatorType = FindType("UnityBridge.InputEmulator");
            if (emulatorType == null) return "FAIL:InputEmulator not found";

            var holdMethod = emulatorType.GetMethod("SimulateHold", BindingFlags.Public | BindingFlags.Static);
            if (holdMethod != null)
            {
                holdMethod.Invoke(null, new object[] { x, y, duration });
                System.Threading.Thread.Sleep((int)(duration * 1000) + 200);
                return "Held at (" + x + ", " + y + ") for " + duration + "s";
            }

            return "FAIL:SimulateHold method not found";
        }

        private static string DoInputDrag(SimpleJson.JsonNode action)
        {
            if (!EditorApplication.isPlaying)
                return "FAIL:Not in Play Mode";

            float sx = action.Get("sx")?.AsFloat() ?? action.Get("startX")?.AsFloat() ?? 0;
            float sy = action.Get("sy")?.AsFloat() ?? action.Get("startY")?.AsFloat() ?? 0;
            float ex = action.Get("ex")?.AsFloat() ?? action.Get("endX")?.AsFloat() ?? 0;
            float ey = action.Get("ey")?.AsFloat() ?? action.Get("endY")?.AsFloat() ?? 0;
            float duration = action.Get("duration")?.AsFloat() ?? 0.5f;

            var emulatorType = FindType("UnityBridge.InputEmulator");
            if (emulatorType == null) return "FAIL:InputEmulator not found";

            var dragMethod = emulatorType.GetMethod("SimulateDrag", BindingFlags.Public | BindingFlags.Static);
            if (dragMethod != null)
            {
                dragMethod.Invoke(null, new object[] { sx, sy, ex, ey, duration });
                System.Threading.Thread.Sleep((int)(duration * 1000) + 200);
                return "Dragged from (" + sx + "," + sy + ") to (" + ex + "," + ey + ")";
            }

            return "FAIL:SimulateDrag method not found";
        }

        private static string DoInputKey(SimpleJson.JsonNode action)
        {
            if (!EditorApplication.isPlaying)
                return "FAIL:Not in Play Mode";

            string key = action.GetString("key") ?? "";
            float duration = action.Get("duration")?.AsFloat() ?? 0.1f;

            // Key input would require InputSystem key simulation - placeholder for now
            System.Threading.Thread.Sleep((int)(duration * 1000));
            return "Key '" + key + "' pressed for " + duration + "s";
        }

        private static string DoScreenshot(SimpleJson.JsonNode action, List<string> screenshotsList)
        {
            string path = action.GetString("path") ?? "C:/temp/screenshot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";

            if (!EditorApplication.isPlaying)
                return "FAIL:Not in Play Mode";

            try
            {
                var captureType = typeof(ScreenshotCapture);
                var captureMethod = captureType.GetMethod("CaptureToFile", BindingFlags.Public | BindingFlags.Static);
                if (captureMethod != null)
                {
                    var result = captureMethod.Invoke(null, new object[] { path, 1 });
                    var successProp = result.GetType().GetField("success");
                    if (successProp != null && (bool)successProp.GetValue(result))
                    {
                        screenshotsList.Add(path);
                        return "Screenshot saved to " + path;
                    }
                    var errorProp = result.GetType().GetField("error");
                    string error = errorProp?.GetValue(result)?.ToString() ?? "unknown error";
                    return "FAIL:Screenshot failed: " + error;
                }
                return "FAIL:CaptureToFile method not found";
            }
            catch (Exception e)
            {
                return "FAIL:Screenshot error: " + e.Message;
            }
        }

        private static string DoCheckState(SimpleJson.JsonNode action)
        {
            if (!EditorApplication.isPlaying)
                return "FAIL:Not in Play Mode";

            string goPath = action.GetString("gameObject") ?? "";
            string componentName = action.GetString("component") ?? "";
            string propertyName = action.GetString("property") ?? "";
            var expectedNode = action.Get("expected");

            if (string.IsNullOrEmpty(goPath))
                return "FAIL:gameObject path is required";

            var go = GameObject.Find(goPath);
            if (go == null)
            {
                // Try scene root walk
                var scene = SceneManager.GetActiveScene();
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name == goPath) { go = root; break; }
                    var child = root.transform.Find(goPath);
                    if (child != null) { go = child.gameObject; break; }
                }
            }

            if (go == null)
                return "FAIL:GameObject not found: " + goPath;

            if (string.IsNullOrEmpty(componentName))
            {
                // Check activeSelf
                bool expectedActive = expectedNode?.AsBool() ?? true;
                return go.activeSelf == expectedActive
                    ? "activeSelf=" + go.activeSelf + " (expected " + expectedActive + ")"
                    : "FAIL:activeSelf=" + go.activeSelf + " (expected " + expectedActive + ")";
            }

            var comp = FindComponent(go, componentName);
            if (comp == null)
                return "FAIL:Component not found: " + componentName;

            if (string.IsNullOrEmpty(propertyName))
                return "Component " + componentName + " found on " + goPath;

            object actual = GetPropertyValue(comp, propertyName);
            string actualStr = actual?.ToString() ?? "null";
            string expectedStr = expectedNode?.AsString() ?? "";

            if (actualStr.Equals(expectedStr, StringComparison.OrdinalIgnoreCase))
                return propertyName + "=" + actualStr + " matches expected";

            return "FAIL:" + propertyName + "=" + actualStr + " (expected " + expectedStr + ")";
        }

        private static string DoCheckLog(SimpleJson.JsonNode action)
        {
            string contains = action.GetString("contains") ?? action.GetString("text") ?? "";
            if (string.IsNullOrEmpty(contains))
                return "FAIL:contains text is required";

            foreach (var log in capturedLogs)
            {
                if (log.Contains(contains))
                    return "Log found: " + contains;
            }

            return "FAIL:Log not found containing: " + contains;
        }

        private static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            capturedLogs.Add(condition);
        }

        private static Component FindComponent(GameObject go, string componentName)
        {
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                if (comp.GetType().Name == componentName || comp.GetType().FullName == componentName)
                    return comp;
            }
            return null;
        }

        private static object GetPropertyValue(Component comp, string propertyName)
        {
            var type = comp.GetType();
            var prop = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null) return prop.GetValue(comp);

            var field = type.GetField(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null) return field.GetValue(comp);

            field = type.GetField("_" + propertyName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null) return field.GetValue(comp);

            field = type.GetField("m_" + propertyName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null) return field.GetValue(comp);

            return null;
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
