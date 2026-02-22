using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace UnityBridge
{
    /// <summary>
    /// Launches built applications as subprocesses, monitors process state and log output,
    /// waits for ready signals, runs automated checks, and returns structured verification reports.
    /// Uses System.Diagnostics.Process for process management and file-based log monitoring.
    ///
    /// AltTester evaluation: Open-source UI automation for Unity builds. Requires embedding an
    /// AltTester component in the build. Provides TCP-based protocol for remote object queries,
    /// input injection, and screenshot capture. Good for UI-heavy testing but adds build dependency.
    ///
    /// GameDriver evaluation: Commercial tool for external-process game testing. Does not require
    /// build modifications. Uses computer vision and API hooks. More powerful but adds licensing
    /// cost and external dependency. Both evaluated per Brief Section 10 for future enhancement.
    /// </summary>
    public static class BuildVerifier
    {
        private static Dictionary<int, Process> trackedProcesses = new Dictionary<int, Process>();

        public static string LaunchBuild(string buildPath)
        {
            if (string.IsNullOrEmpty(buildPath))
                return "{\"success\":false,\"error\":\"buildPath is required\"}";

            try
            {
                if (!File.Exists(buildPath))
                    return "{\"success\":false,\"error\":\"Build not found: " + Esc(buildPath) + "\"}";

                var startInfo = new ProcessStartInfo
                {
                    FileName = buildPath,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(buildPath)
                };

                var process = Process.Start(startInfo);
                if (process == null)
                    return "{\"success\":false,\"error\":\"Failed to start process\"}";

                trackedProcesses[process.Id] = process;

                return string.Format("{{\"success\":true,\"processId\":{0},\"buildPath\":\"{1}\",\"message\":\"Build launched\"}}",
                    process.Id, Esc(buildPath));
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"Launch error: " + Esc(e.Message) + "\"}";
            }
        }

        public static string CheckBuildRunning(int processId)
        {
            try
            {
                Process process = null;
                if (trackedProcesses.ContainsKey(processId))
                    process = trackedProcesses[processId];
                else
                {
                    try { process = Process.GetProcessById(processId); }
                    catch { }
                }

                if (process == null)
                    return string.Format("{{\"success\":true,\"processId\":{0},\"status\":\"not_found\",\"message\":\"Process not found\"}}", processId);

                if (process.HasExited)
                {
                    int exitCode = process.ExitCode;
                    string status = exitCode == 0 ? "exited" : "crashed";
                    return string.Format("{{\"success\":true,\"processId\":{0},\"status\":\"{1}\",\"exitCode\":{2}}}",
                        processId, status, exitCode);
                }

                return string.Format("{{\"success\":true,\"processId\":{0},\"status\":\"running\"}}", processId);
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        public static string StopBuild(int processId)
        {
            try
            {
                Process process = null;
                if (trackedProcesses.ContainsKey(processId))
                    process = trackedProcesses[processId];
                else
                {
                    try { process = Process.GetProcessById(processId); }
                    catch { }
                }

                if (process == null)
                    return string.Format("{{\"success\":true,\"processId\":{0},\"message\":\"Process not found (may have already exited)\"}}", processId);

                if (process.HasExited)
                {
                    trackedProcesses.Remove(processId);
                    return string.Format("{{\"success\":true,\"processId\":{0},\"message\":\"Process already exited\",\"exitCode\":{1}}}",
                        processId, process.ExitCode);
                }

                // Try graceful close first
                try { process.CloseMainWindow(); }
                catch { }

                // Wait briefly for graceful exit
                bool exited = process.WaitForExit(3000);
                if (!exited)
                {
                    // Force kill
                    try { process.Kill(); }
                    catch { }
                    process.WaitForExit(2000);
                }

                trackedProcesses.Remove(processId);

                return string.Format("{{\"success\":true,\"processId\":{0},\"message\":\"Process stopped\",\"graceful\":{1}}}",
                    processId, exited ? "true" : "false");
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"Stop error: " + Esc(e.Message) + "\"}";
            }
        }

        public static string GetBuildLogs(string logPath)
        {
            try
            {
                if (string.IsNullOrEmpty(logPath))
                {
                    // Derive default Unity Player log path
                    string company = PlayerSettings.companyName;
                    string product = PlayerSettings.productName;
                    string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    // On Windows: %LOCALAPPDATA%Low/<company>/<product>/Player.log
                    // LocalApplicationData gives AppData/Local, we need AppData/LocalLow
                    string localLow = Path.Combine(Directory.GetParent(appData).FullName, "LocalLow");
                    logPath = Path.Combine(localLow, company, product, "Player.log");
                }

                if (!File.Exists(logPath))
                    return "{\"success\":false,\"error\":\"Log file not found: " + Esc(logPath) + "\"}";

                // Read with shared access (file may be locked by running build)
                string content;
                using (var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    content = reader.ReadToEnd();
                }

                // Extract errors and warnings
                var errors = new List<string>();
                var warnings = new List<string>();
                string[] lines = content.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Contains("Error") || line.Contains("error") || line.Contains("Exception"))
                        errors.Add(line.Trim());
                    else if (line.Contains("Warning") || line.Contains("warning"))
                        warnings.Add(line.Trim());
                }

                // Truncate content if too long
                int maxLen = 10000;
                string truncatedContent = content.Length > maxLen
                    ? content.Substring(content.Length - maxLen)
                    : content;

                var errorEntries = new List<string>();
                foreach (var e in errors) errorEntries.Add("\"" + Esc(e) + "\"");
                var warnEntries = new List<string>();
                foreach (var w in warnings) warnEntries.Add("\"" + Esc(w) + "\"");

                return string.Format(
                    "{{\"success\":true,\"logPath\":\"{0}\",\"totalLines\":{1},\"errors\":[{2}],\"errorCount\":{3},\"warnings\":[{4}],\"warningCount\":{5},\"recentContent\":\"{6}\"}}",
                    Esc(logPath), lines.Length,
                    string.Join(",", errorEntries.ToArray()), errors.Count,
                    string.Join(",", warnEntries.ToArray()), warnings.Count,
                    Esc(truncatedContent));
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"Log read error: " + Esc(e.Message) + "\"}";
            }
        }

        public static string RunBuildTest(string jsonTestSpec)
        {
            if (string.IsNullOrEmpty(jsonTestSpec))
                return "{\"success\":false,\"error\":\"jsonTestSpec is required\"}";

            try
            {
                var spec = SimpleJson.Parse(jsonTestSpec);
                string buildPath = spec.GetString("buildPath") ?? "";
                var waitForReady = spec.Get("waitForReady");
                var checks = spec.Get("checks");
                float overallTimeout = spec.Get("timeout")?.AsFloat() ?? 60f;

                if (string.IsNullOrEmpty(buildPath))
                    return "{\"success\":false,\"error\":\"buildPath is required\"}";

                double totalStart = EditorApplication.timeSinceStartup;

                // Launch
                if (!File.Exists(buildPath))
                    return "{\"success\":false,\"error\":\"Build not found: " + Esc(buildPath) + "\"}";

                var startInfo = new ProcessStartInfo
                {
                    FileName = buildPath,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(buildPath)
                };

                var process = Process.Start(startInfo);
                if (process == null)
                    return "{\"success\":false,\"error\":\"Failed to start process\"}";

                trackedProcesses[process.Id] = process;
                bool launchSuccess = true;
                int processId = process.Id;

                // Wait for ready signal
                bool readySignalDetected = false;
                double readySignalTime = 0;

                if (waitForReady != null)
                {
                    string readyType = waitForReady.GetString("type") ?? "logFile";
                    string readyContains = waitForReady.GetString("contains") ?? "";
                    string readyLogPath = waitForReady.GetString("path") ?? "";
                    float readyTimeout = waitForReady.Get("timeout")?.AsFloat() ?? 30f;

                    if (readyType == "logFile" && !string.IsNullOrEmpty(readyContains))
                    {
                        double readyEnd = EditorApplication.timeSinceStartup + readyTimeout;
                        while (EditorApplication.timeSinceStartup < readyEnd)
                        {
                            if (process.HasExited) break;

                            string logContent = "";
                            string logFile = readyLogPath;
                            if (string.IsNullOrEmpty(logFile))
                            {
                                string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                                string localLow = Path.Combine(Directory.GetParent(appData).FullName, "LocalLow");
                                logFile = Path.Combine(localLow, PlayerSettings.companyName, PlayerSettings.productName, "Player.log");
                            }

                            if (File.Exists(logFile))
                            {
                                try
                                {
                                    using (var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                    using (var reader = new StreamReader(stream))
                                        logContent = reader.ReadToEnd();
                                }
                                catch { }
                            }

                            if (logContent.Contains(readyContains))
                            {
                                readySignalDetected = true;
                                readySignalTime = EditorApplication.timeSinceStartup - totalStart;
                                break;
                            }

                            System.Threading.Thread.Sleep(500);
                        }
                    }
                }

                // Run checks
                var checkResults = new List<string>();
                var logErrors = new List<string>();
                var logWarnings = new List<string>();
                bool allPassed = true;

                if (checks != null && checks.arr != null)
                {
                    foreach (var check in checks.arr)
                    {
                        string checkType = check.GetString("type") ?? "";

                        switch (checkType)
                        {
                            case "processRunning":
                            {
                                bool running = !process.HasExited;
                                checkResults.Add(string.Format("{{\"type\":\"processRunning\",\"passed\":{0}}}", running ? "true" : "false"));
                                if (!running) allPassed = false;
                                break;
                            }
                            case "logContains":
                            {
                                string text = check.GetString("text") ?? "";
                                string logContent = ReadPlayerLog("");
                                bool found = logContent.Contains(text);
                                checkResults.Add(string.Format("{{\"type\":\"logContains\",\"text\":\"{0}\",\"passed\":{1}}}", Esc(text), found ? "true" : "false"));
                                if (!found) allPassed = false;
                                break;
                            }
                            case "logDoesNotContain":
                            {
                                string text = check.GetString("text") ?? "";
                                string logContent = ReadPlayerLog("");
                                bool notFound = !logContent.Contains(text);
                                checkResults.Add(string.Format("{{\"type\":\"logDoesNotContain\",\"text\":\"{0}\",\"passed\":{1}}}", Esc(text), notFound ? "true" : "false"));
                                if (!notFound) allPassed = false;
                                break;
                            }
                            case "waitSeconds":
                            {
                                float seconds = check.Get("seconds")?.AsFloat() ?? 1f;
                                System.Threading.Thread.Sleep((int)(seconds * 1000));
                                checkResults.Add(string.Format(CultureInfo.InvariantCulture,
                                    "{{\"type\":\"waitSeconds\",\"seconds\":{0},\"passed\":true}}", seconds));
                                break;
                            }
                            case "screenshotCapture":
                            {
                                string outputPath = check.GetString("outputPath") ?? "";
                                checkResults.Add(string.Format(
                                    "{{\"type\":\"screenshotCapture\",\"outputPath\":\"{0}\",\"passed\":false,\"reason\":\"Screenshot mechanism not available for external processes\"}}",
                                    Esc(outputPath)));
                                break;
                            }
                            default:
                                checkResults.Add(string.Format("{{\"type\":\"{0}\",\"passed\":false,\"reason\":\"Unknown check type\"}}", Esc(checkType)));
                                break;
                        }

                        // Check overall timeout
                        if (EditorApplication.timeSinceStartup - totalStart > overallTimeout)
                        {
                            allPassed = false;
                            break;
                        }
                    }
                }

                // Collect final log errors/warnings
                string finalLog = ReadPlayerLog("");
                foreach (var line in finalLog.Split('\n'))
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;
                    if (trimmed.Contains("Error") || trimmed.Contains("Exception"))
                        logErrors.Add(trimmed);
                    else if (trimmed.Contains("Warning") || trimmed.Contains("warning"))
                        logWarnings.Add(trimmed);
                }

                // Stop the build
                int exitCode = 0;
                bool crashed = false;
                if (!process.HasExited)
                {
                    try { process.CloseMainWindow(); }
                    catch { }
                    if (!process.WaitForExit(3000))
                    {
                        try { process.Kill(); }
                        catch { }
                        process.WaitForExit(2000);
                    }
                }
                if (process.HasExited)
                {
                    exitCode = process.ExitCode;
                    crashed = exitCode != 0;
                }
                trackedProcesses.Remove(processId);

                double totalDuration = EditorApplication.timeSinceStartup - totalStart;
                string result = allPassed && !crashed ? "Passed" : "Failed";

                var errEntries = new List<string>();
                foreach (var e in logErrors) errEntries.Add("\"" + Esc(e.Length > 200 ? e.Substring(0, 200) : e) + "\"");
                var warnEntries = new List<string>();
                foreach (var w in logWarnings) warnEntries.Add("\"" + Esc(w.Length > 200 ? w.Substring(0, 200) : w) + "\"");

                return string.Format(CultureInfo.InvariantCulture,
                    "{{\"success\":true,\"buildPath\":\"{0}\",\"launchSuccess\":{1},\"processId\":{2},\"duration\":{3},\"readySignalDetected\":{4},\"readySignalTime\":{5},\"checks\":[{6}],\"logErrors\":[{7}],\"logWarnings\":[{8}],\"exitCode\":{9},\"crashed\":{10},\"result\":\"{11}\"}}",
                    Esc(buildPath), launchSuccess ? "true" : "false", processId, totalDuration,
                    readySignalDetected ? "true" : "false", readySignalTime,
                    string.Join(",", checkResults.ToArray()),
                    string.Join(",", errEntries.ToArray()),
                    string.Join(",", warnEntries.ToArray()),
                    exitCode, crashed ? "true" : "false", result);
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        private static string ReadPlayerLog(string logPath)
        {
            try
            {
                if (string.IsNullOrEmpty(logPath))
                {
                    string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    string localLow = Path.Combine(Directory.GetParent(appData).FullName, "LocalLow");
                    logPath = Path.Combine(localLow, PlayerSettings.companyName, PlayerSettings.productName, "Player.log");
                }
                if (!File.Exists(logPath)) return "";

                using (var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                    return reader.ReadToEnd();
            }
            catch { return ""; }
        }

        private static string Esc(string str)
        {
            if (str == null) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
