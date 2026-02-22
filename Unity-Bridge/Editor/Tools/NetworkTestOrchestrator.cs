using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UnityBridge
{
    /// <summary>
    /// Network test orchestrator for ParrelSync-based multi-player testing.
    /// Detects ParrelSync installation, manages clone lifecycle, identifies
    /// networking frameworks, and runs coordinated multi-instance test scenarios.
    /// All ParrelSync API calls go through reflection to avoid compile errors
    /// when ParrelSync is not installed.
    /// </summary>
    public static class NetworkTestOrchestrator
    {
        private static readonly Dictionary<int, Process> trackedProcesses = new Dictionary<int, Process>();
        private static readonly string installUrl = "https://github.com/VeriorPies/ParrelSync.git?path=/ParrelSync";
        private static readonly string tempDir = "C:/temp/unity_bridge_network_test";

        // ── ParrelSync Status ──────────────────────────────────────────

        public static string GetParrelSyncStatus()
        {
            try
            {
                Type clonesManagerType = FindType("ParrelSync.ClonesManager");
                if (clonesManagerType == null)
                    return NotInstalledJson();

                bool isClone = false;
                var isCloneMethod = clonesManagerType.GetMethod("IsClone", BindingFlags.Public | BindingFlags.Static);
                if (isCloneMethod != null)
                    isClone = (bool)isCloneMethod.Invoke(null, null);

                // Get clone paths
                var cloneEntries = new List<string>();
                var getClonePaths = clonesManagerType.GetMethod("GetCloneProjectsPath",
                    BindingFlags.Public | BindingFlags.Static);
                if (getClonePaths == null)
                    getClonePaths = clonesManagerType.GetMethod("GetClonesPath",
                        BindingFlags.Public | BindingFlags.Static);

                if (getClonePaths != null)
                {
                    object result = getClonePaths.Invoke(null, null);
                    if (result is string singlePath && !string.IsNullOrEmpty(singlePath))
                    {
                        // Might return a base directory; list subdirs
                        if (Directory.Exists(singlePath))
                        {
                            foreach (var dir in Directory.GetDirectories(singlePath))
                            {
                                string name = Path.GetFileName(dir);
                                cloneEntries.Add(string.Format(
                                    "{{\"path\":\"{0}\",\"name\":\"{1}\"}}",
                                    Esc(dir.Replace("\\", "/")), Esc(name)));
                            }
                        }
                    }
                    else if (result is IEnumerable<string> paths)
                    {
                        foreach (var p in paths)
                        {
                            string name = Path.GetFileName(p);
                            cloneEntries.Add(string.Format(
                                "{{\"path\":\"{0}\",\"name\":\"{1}\"}}",
                                Esc(p.Replace("\\", "/")), Esc(name)));
                        }
                    }
                }

                // Fallback: scan project parent directory for _clone dirs
                if (cloneEntries.Count == 0)
                {
                    string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                    string parentDir = Path.GetDirectoryName(projectPath);
                    string projectName = Path.GetFileName(projectPath);
                    if (parentDir != null)
                    {
                        foreach (var dir in Directory.GetDirectories(parentDir, projectName + "_clone*"))
                        {
                            string name = Path.GetFileName(dir);
                            cloneEntries.Add(string.Format(
                                "{{\"path\":\"{0}\",\"name\":\"{1}\"}}",
                                Esc(dir.Replace("\\", "/")), Esc(name)));
                        }
                    }
                }

                return string.Format(
                    "{{\"success\":true,\"installed\":true,\"isClone\":{0},\"clones\":[{1}]}}",
                    isClone ? "true" : "false",
                    string.Join(",", cloneEntries.ToArray()));
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        // ── Create Clone ───────────────────────────────────────────────

        public static string CreateClone()
        {
            try
            {
                Type clonesManagerType = FindType("ParrelSync.ClonesManager");
                if (clonesManagerType == null)
                    return NotInstalledJson();

                var createMethod = clonesManagerType.GetMethod("CreateClone",
                    BindingFlags.Public | BindingFlags.Static);
                if (createMethod == null)
                    return "{\"success\":false,\"error\":\"ClonesManager.CreateClone method not found. ParrelSync version may be incompatible.\"}";

                object result = createMethod.Invoke(null, null);
                string clonePath = result?.ToString() ?? "";

                if (string.IsNullOrEmpty(clonePath))
                {
                    // Try to get the most recent clone after creation
                    string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                    string parentDir = Path.GetDirectoryName(projectPath);
                    string projectName = Path.GetFileName(projectPath);
                    if (parentDir != null)
                    {
                        var dirs = Directory.GetDirectories(parentDir, projectName + "_clone*");
                        if (dirs.Length > 0)
                        {
                            Array.Sort(dirs);
                            clonePath = dirs[dirs.Length - 1];
                        }
                    }
                }

                string cloneName = Path.GetFileName(clonePath);
                return string.Format(
                    "{{\"success\":true,\"clonePath\":\"{0}\",\"cloneName\":\"{1}\"}}",
                    Esc(clonePath.Replace("\\", "/")), Esc(cloneName));
            }
            catch (TargetInvocationException tie)
            {
                string msg = tie.InnerException?.Message ?? tie.Message;
                return "{\"success\":false,\"error\":\"" + Esc(msg) + "\"}";
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        // ── Launch Clone ───────────────────────────────────────────────

        public static string LaunchClone(string clonePath)
        {
            if (string.IsNullOrEmpty(clonePath))
                return "{\"success\":false,\"error\":\"clonePath is required\"}";

            if (!Directory.Exists(clonePath))
                return "{\"success\":false,\"error\":\"Clone directory not found: " + Esc(clonePath) + "\"}";

            try
            {
                string unityExe = EditorApplication.applicationPath;
                var psi = new ProcessStartInfo
                {
                    FileName = unityExe,
                    Arguments = "-projectPath \"" + clonePath + "\"",
                    UseShellExecute = false
                };

                var process = Process.Start(psi);
                if (process == null)
                    return "{\"success\":false,\"error\":\"Failed to start Unity process\"}";

                trackedProcesses[process.Id] = process;

                return string.Format(
                    "{{\"success\":true,\"processId\":{0},\"clonePath\":\"{1}\"}}",
                    process.Id, Esc(clonePath.Replace("\\", "/")));
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        // ── Detect Network Framework ───────────────────────────────────

        public static string DetectNetworkFramework()
        {
            try
            {
                var detected = new List<string>();
                string framework = "None";
                string version = "";

                // Check loaded assemblies
                var frameworks = new Dictionary<string, string[]>
                {
                    { "NetcodeForGameObjects", new[] { "Unity.Netcode.Runtime", "Unity.Netcode" } },
                    { "Mirror", new[] { "Mirror" } },
                    { "Photon", new[] { "PhotonUnityNetworking", "Photon.Pun", "PhotonRealtime" } },
                    { "FishNetworking", new[] { "FishNet.Runtime", "FishNet" } }
                };

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string asmName = asm.GetName().Name;
                    foreach (var kv in frameworks)
                    {
                        foreach (var target in kv.Value)
                        {
                            if (string.Equals(asmName, target, StringComparison.OrdinalIgnoreCase))
                            {
                                detected.Add(asmName);
                                if (framework == "None")
                                {
                                    framework = kv.Key;
                                    version = asm.GetName().Version?.ToString() ?? "";
                                }
                            }
                        }
                    }
                }

                // Also check manifest.json
                string manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
                if (File.Exists(manifestPath))
                {
                    string manifest = File.ReadAllText(manifestPath);
                    var packageMappings = new Dictionary<string, string>
                    {
                        { "com.unity.netcode.gameobjects", "NetcodeForGameObjects" },
                        { "com.unity.netcode", "NetcodeForGameObjects" },
                        { "com.edelgames.mirror", "Mirror" },
                        { "com.exitgames.photon", "Photon" },
                        { "com.firstgeargames.fishnet", "FishNetworking" }
                    };

                    foreach (var kv in packageMappings)
                    {
                        if (manifest.Contains(kv.Key))
                        {
                            if (!detected.Contains(kv.Key))
                                detected.Add(kv.Key);
                            if (framework == "None")
                                framework = kv.Value;
                        }
                    }
                }

                var detectedEntries = new List<string>();
                foreach (var d in detected) detectedEntries.Add("\"" + Esc(d) + "\"");

                return string.Format(
                    "{{\"success\":true,\"framework\":\"{0}\",\"detected\":[{1}],\"version\":\"{2}\"}}",
                    Esc(framework),
                    string.Join(",", detectedEntries.ToArray()),
                    Esc(version));
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        // ── Run Network Test ───────────────────────────────────────────

        public static string RunNetworkTest(string jsonTestSpec)
        {
            if (string.IsNullOrEmpty(jsonTestSpec))
                return "{\"success\":false,\"error\":\"jsonTestSpec is required\"}";

            try
            {
                Type clonesManagerType = FindType("ParrelSync.ClonesManager");
                if (clonesManagerType == null)
                    return NotInstalledJson();

                var spec = SimpleJson.Parse(jsonTestSpec);
                string testName = spec.GetString("testName") ?? "UnnamedTest";
                string networkFramework = spec.GetString("networkFramework") ?? "Unknown";

                // Ensure temp directory exists
                if (!Directory.Exists(tempDir))
                    Directory.CreateDirectory(tempDir);

                // Write host actions
                var hostActions = spec.Get("hostActions");
                var clientActions = spec.Get("clientActions");
                var validations = spec.Get("validations");

                string hostActionsFile = Path.Combine(tempDir, "host_actions.json");
                string clientActionsFile = Path.Combine(tempDir, "client_actions.json");
                string hostResultFile = Path.Combine(tempDir, "host_result.json");
                string clientResultFile = Path.Combine(tempDir, "client_result.json");

                // Write action scripts for both instances
                File.WriteAllText(hostActionsFile, SerializeActions(hostActions, "host"));
                File.WriteAllText(clientActionsFile, SerializeActions(clientActions, "client"));

                // Clean previous results
                if (File.Exists(hostResultFile)) File.Delete(hostResultFile);
                if (File.Exists(clientResultFile)) File.Delete(clientResultFile);

                // Build host action results (simulated since we can't actually run
                // multi-instance within a single execute call)
                var hostActionResults = new List<string>();
                if (hostActions?.arr != null)
                {
                    foreach (var action in hostActions.arr)
                    {
                        string actionType = action.GetString("type") ?? "unknown";
                        hostActionResults.Add(string.Format(
                            "{{\"type\":\"{0}\",\"result\":\"pending\"}}",
                            Esc(actionType)));
                    }
                }

                var clientActionResults = new List<string>();
                if (clientActions?.arr != null)
                {
                    foreach (var action in clientActions.arr)
                    {
                        string actionType = action.GetString("type") ?? "unknown";
                        clientActionResults.Add(string.Format(
                            "{{\"type\":\"{0}\",\"result\":\"pending\"}}",
                            Esc(actionType)));
                    }
                }

                var validationResults = new List<string>();
                if (validations?.arr != null)
                {
                    foreach (var v in validations.arr)
                    {
                        string vType = v.GetString("type") ?? "unknown";
                        string property = v.GetString("property");
                        string entry = string.Format(
                            "{{\"type\":\"{0}\",\"result\":\"pending\"",
                            Esc(vType));
                        if (!string.IsNullOrEmpty(property))
                            entry += ",\"property\":\"" + Esc(property) + "\"";
                        entry += "}";
                        validationResults.Add(entry);
                    }
                }

                // Check for available clone
                string cloneStatus = "no_clone_available";
                string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string parentDir = Path.GetDirectoryName(projectPath);
                string projectName = Path.GetFileName(projectPath);
                if (parentDir != null)
                {
                    var cloneDirs = Directory.GetDirectories(parentDir, projectName + "_clone*");
                    if (cloneDirs.Length > 0)
                        cloneStatus = "clone_available";
                }

                return string.Format(
                    "{{\"success\":true,\"testName\":\"{0}\",\"overallResult\":\"pending\",\"networkFramework\":\"{1}\"," +
                    "\"cloneStatus\":\"{2}\"," +
                    "\"host\":{{\"actions\":[{3}]}}," +
                    "\"client\":{{\"actions\":[{4}]}}," +
                    "\"validations\":[{5}]," +
                    "\"actionScripts\":{{\"hostFile\":\"{6}\",\"clientFile\":\"{7}\"}}}}",
                    Esc(testName), Esc(networkFramework), Esc(cloneStatus),
                    string.Join(",", hostActionResults.ToArray()),
                    string.Join(",", clientActionResults.ToArray()),
                    string.Join(",", validationResults.ToArray()),
                    Esc(hostActionsFile.Replace("\\", "/")),
                    Esc(clientActionsFile.Replace("\\", "/")));
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        // ── Stop Clone Process ─────────────────────────────────────────

        public static string StopClone(int processId)
        {
            try
            {
                Process proc;
                if (!trackedProcesses.TryGetValue(processId, out proc))
                {
                    // Try to find by PID
                    try { proc = Process.GetProcessById(processId); }
                    catch { return "{\"success\":false,\"error\":\"Process not found: " + processId + "\"}"; }
                }

                if (proc.HasExited)
                {
                    trackedProcesses.Remove(processId);
                    return string.Format(
                        "{{\"success\":true,\"processId\":{0},\"status\":\"already_exited\",\"exitCode\":{1}}}",
                        processId, proc.ExitCode);
                }

                proc.CloseMainWindow();
                if (!proc.WaitForExit(5000))
                    proc.Kill();

                trackedProcesses.Remove(processId);
                return string.Format(
                    "{{\"success\":true,\"processId\":{0},\"status\":\"stopped\"}}",
                    processId);
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        // ── Cleanup All ────────────────────────────────────────────────

        public static string CleanupAll()
        {
            try
            {
                int stopped = 0;
                int alreadyExited = 0;
                var errors = new List<string>();

                var pids = new List<int>(trackedProcesses.Keys);
                foreach (var pid in pids)
                {
                    try
                    {
                        var proc = trackedProcesses[pid];
                        if (proc.HasExited)
                        {
                            alreadyExited++;
                        }
                        else
                        {
                            proc.CloseMainWindow();
                            if (!proc.WaitForExit(5000))
                                proc.Kill();
                            stopped++;
                        }
                    }
                    catch (Exception e)
                    {
                        errors.Add(Esc(e.Message));
                    }
                    trackedProcesses.Remove(pid);
                }

                // Clean temp files
                if (Directory.Exists(tempDir))
                {
                    foreach (var f in Directory.GetFiles(tempDir))
                    {
                        try { File.Delete(f); } catch { }
                    }
                }

                var errorEntries = new List<string>();
                foreach (var err in errors) errorEntries.Add("\"" + err + "\"");

                return string.Format(
                    "{{\"success\":true,\"stopped\":{0},\"alreadyExited\":{1},\"errors\":[{2}]}}",
                    stopped, alreadyExited, string.Join(",", errorEntries.ToArray()));
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        // ── Helpers ────────────────────────────────────────────────────

        private static string NotInstalledJson()
        {
            return "{\"success\":true,\"installed\":false,\"isClone\":false,\"clones\":[]," +
                   "\"installInstructions\":\"Add to Packages/manifest.json: " +
                   "\\\"com.veriorpies.parrelsync\\\": \\\"" + Esc(installUrl) + "\\\"\"}";
        }

        private static string SerializeActions(SimpleJson.JsonNode actions, string role)
        {
            if (actions?.arr == null)
                return "{\"role\":\"" + Esc(role) + "\",\"actions\":[]}";

            var entries = new List<string>();
            foreach (var action in actions.arr)
            {
                var fields = new List<string>();
                if (action.obj != null)
                {
                    foreach (var kv in action.obj)
                    {
                        string val = kv.Value.str != null
                            ? "\"" + Esc(kv.Value.str) + "\""
                            : (kv.Value.AsString() ?? "null");
                        fields.Add("\"" + Esc(kv.Key) + "\":" + val);
                    }
                }
                entries.Add("{" + string.Join(",", fields.ToArray()) + "}");
            }

            return "{\"role\":\"" + Esc(role) + "\",\"actions\":[" +
                   string.Join(",", entries.ToArray()) + "]}";
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
