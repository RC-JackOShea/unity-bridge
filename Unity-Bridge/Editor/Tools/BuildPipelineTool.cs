using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace UnityBridge
{
    /// <summary>
    /// Programmatic build configuration, execution, and report parsing.
    /// Named to avoid conflict with Unity's BuildPipeline class.
    /// </summary>
    public static class BuildPipelineTool
    {
        private static string _lastBuildReport;

        public static string GetCurrentBuildConfig()
        {
            var scenes = EditorBuildSettings.scenes;
            var sceneList = new List<string>();
            foreach (var s in scenes)
            {
                sceneList.Add(string.Format("{{\"path\":\"{0}\",\"enabled\":{1}}}",
                    Esc(s.path), s.enabled ? "true" : "false"));
            }

            var target = EditorUserBuildSettings.activeBuildTarget;
            var group = BuildPipeline.GetBuildTargetGroup(target);
            var backend = PlayerSettings.GetScriptingBackend(group);
            var apiLevel = PlayerSettings.GetApiCompatibilityLevel(group);

            return string.Format(
                "{{\"activeBuildTarget\":\"{0}\",\"buildTargetGroup\":\"{1}\",\"scriptingBackend\":\"{2}\",\"apiCompatibilityLevel\":\"{3}\",\"scenes\":[{4}],\"playerSettings\":{{\"companyName\":\"{5}\",\"productName\":\"{6}\",\"bundleVersion\":\"{7}\"}},\"development\":{8}}}",
                target.ToString(), group.ToString(), backend.ToString(), apiLevel.ToString(),
                string.Join(",", sceneList.ToArray()),
                Esc(PlayerSettings.companyName), Esc(PlayerSettings.productName),
                Esc(PlayerSettings.bundleVersion),
                EditorUserBuildSettings.development ? "true" : "false");
        }

        public static string ConfigureBuild(string jsonSpec)
        {
            if (string.IsNullOrEmpty(jsonSpec))
                return "{\"error\":\"jsonSpec is required\"}";

            try
            {
                var spec = SimpleJson.Parse(jsonSpec);

                // Scenes
                var scenePaths = spec.GetArray("scenes");
                if (scenePaths != null)
                {
                    var editorScenes = new List<EditorBuildSettingsScene>();
                    foreach (var sp in scenePaths)
                    {
                        string path = sp.AsString();
                        if (!string.IsNullOrEmpty(path))
                        {
                            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                            if (asset == null)
                                return "{\"error\":\"Scene not found: " + Esc(path) + "\"}";
                            editorScenes.Add(new EditorBuildSettingsScene(path, true));
                        }
                    }
                    EditorBuildSettings.scenes = editorScenes.ToArray();
                }

                // Player Settings
                var ps = spec.Get("playerSettings");
                if (ps != null)
                {
                    string company = ps.GetString("companyName");
                    if (!string.IsNullOrEmpty(company)) PlayerSettings.companyName = company;
                    string product = ps.GetString("productName");
                    if (!string.IsNullOrEmpty(product)) PlayerSettings.productName = product;
                    string version = ps.GetString("bundleVersion");
                    if (!string.IsNullOrEmpty(version)) PlayerSettings.bundleVersion = version;
                }

                // Options
                var opts = spec.Get("options");
                if (opts != null)
                {
                    var devNode = opts.Get("development");
                    if (devNode != null) EditorUserBuildSettings.development = devNode.AsBool();
                }

                // Target platform
                string targetStr = spec.GetString("target");
                if (!string.IsNullOrEmpty(targetStr))
                {
                    if (Enum.TryParse<BuildTarget>(targetStr, out var bt))
                    {
                        var btg = BuildPipeline.GetBuildTargetGroup(bt);
                        if (EditorUserBuildSettings.activeBuildTarget != bt)
                            EditorUserBuildSettings.SwitchActiveBuildTarget(btg, bt);

                        // Scripting backend
                        string backendStr = opts?.GetString("scriptingBackend");
                        if (!string.IsNullOrEmpty(backendStr) && Enum.TryParse<ScriptingImplementation>(backendStr, out var si))
                            PlayerSettings.SetScriptingBackend(btg, si);
                    }
                    else
                    {
                        return "{\"error\":\"Unknown build target: " + Esc(targetStr) + "\"}";
                    }
                }

                return "{\"success\":true,\"message\":\"Build configuration applied\"}";
            }
            catch (Exception e)
            {
                return "{\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        public static string ProduceBuild(string jsonSpec)
        {
            if (string.IsNullOrEmpty(jsonSpec))
                return "{\"error\":\"jsonSpec is required\"}";

            try
            {
                var spec = SimpleJson.Parse(jsonSpec);
                string outputPath = spec.GetString("outputPath");
                if (string.IsNullOrEmpty(outputPath))
                    return "{\"error\":\"outputPath is required\"}";

                // Resolve target
                BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
                string targetStr = spec.GetString("target");
                if (!string.IsNullOrEmpty(targetStr))
                {
                    if (!Enum.TryParse(targetStr, out target))
                        return "{\"error\":\"Unknown build target: " + Esc(targetStr) + "\"}";
                }

                // Resolve scenes
                var scenePaths = spec.GetArray("scenes");
                string[] scenes;
                if (scenePaths != null && scenePaths.Count > 0)
                {
                    var list = new List<string>();
                    foreach (var sp in scenePaths)
                    {
                        string p = sp.AsString();
                        var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p);
                        if (asset == null)
                            return "{\"error\":\"Scene not found: " + Esc(p) + "\"}";
                        list.Add(p);
                    }
                    scenes = list.ToArray();
                }
                else
                {
                    var buildScenes = EditorBuildSettings.scenes;
                    var list = new List<string>();
                    foreach (var s in buildScenes)
                        if (s.enabled) list.Add(s.path);
                    scenes = list.ToArray();
                }

                if (scenes.Length == 0)
                    return "{\"error\":\"No scenes specified for build\"}";

                // Build options
                var options = BuildOptions.None;
                var opts = spec.Get("options");
                if (opts != null)
                {
                    if (opts.Get("development") != null && opts.Get("development").AsBool())
                        options |= BuildOptions.Development;
                }

                var buildPlayerOptions = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = outputPath,
                    target = target,
                    options = options
                };

                BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
                _lastBuildReport = FormatBuildReport(report, outputPath);
                return _lastBuildReport;
            }
            catch (Exception e)
            {
                return "{\"error\":\"Build failed: " + Esc(e.Message) + "\"}";
            }
        }

        public static string GetBuildReport()
        {
            if (string.IsNullOrEmpty(_lastBuildReport))
                return "{\"error\":\"No build report available. Run ProduceBuild first.\"}";
            return _lastBuildReport;
        }

        private static string FormatBuildReport(BuildReport report, string outputPath)
        {
            bool success = report.summary.result == BuildResult.Succeeded;
            var steps = new List<string>();
            foreach (var step in report.steps)
            {
                steps.Add(string.Format(CultureInfo.InvariantCulture,
                    "{{\"name\":\"{0}\",\"duration\":{1}}}",
                    Esc(step.name), step.duration.TotalSeconds));
            }

            var errors = new List<string>();
            var warnings = new List<string>();
            // BuildReport messages are accessed via steps
            foreach (var step in report.steps)
            {
                for (int i = 0; i < step.messages.Length; i++)
                {
                    var msg = step.messages[i];
                    if (msg.type == LogType.Error || msg.type == LogType.Exception)
                        errors.Add("\"" + Esc(msg.content) + "\"");
                    else if (msg.type == LogType.Warning)
                        warnings.Add("\"" + Esc(msg.content) + "\"");
                }
            }

            return string.Format(CultureInfo.InvariantCulture,
                "{{\"success\":{0},\"result\":\"{1}\",\"totalTime\":{2},\"totalSize\":{3},\"errors\":[{4}],\"warnings\":[{5}],\"steps\":[{6}],\"outputPath\":\"{7}\"}}",
                success ? "true" : "false",
                report.summary.result.ToString(),
                report.summary.totalTime.TotalSeconds,
                report.summary.totalSize,
                string.Join(",", errors.ToArray()),
                string.Join(",", warnings.ToArray()),
                string.Join(",", steps.ToArray()),
                Esc(outputPath));
        }

        private static string Esc(string str)
        {
            if (str == null) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
