using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace UnityBridge
{
    /// <summary>
    /// Programmatic package management: list, install, remove, update, search.
    /// Uses direct manifest.json editing for install/remove/update and
    /// PackageManager.Client API for search and compatibility.
    /// </summary>
    public static class PackageManagerTool
    {
        public static string ListInstalled()
        {
            try
            {
                string manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
                if (!File.Exists(manifestPath))
                    return "{\"error\":\"manifest.json not found\"}";

                string manifestContent = File.ReadAllText(manifestPath);
                var manifest = SimpleJson.Parse(manifestContent);
                var deps = manifest.Get("dependencies");
                if (deps == null || deps.obj == null)
                    return "{\"packages\":[],\"total\":0}";

                // Try to read lock file for resolved info
                string lockPath = Path.Combine(Application.dataPath, "..", "Packages", "packages-lock.json");
                SimpleJson.JsonNode lockData = null;
                if (File.Exists(lockPath))
                {
                    try { lockData = SimpleJson.Parse(File.ReadAllText(lockPath)); }
                    catch { }
                }
                var lockDeps = lockData?.Get("dependencies");

                var packages = new List<string>();
                foreach (var kv in deps.obj)
                {
                    string name = kv.Key;
                    string version = kv.Value.AsString();
                    string source = ClassifySource(version);
                    string resolvedPath = "";

                    if (lockDeps?.Get(name) != null)
                    {
                        var lockEntry = lockDeps.Get(name);
                        string lockSource = lockEntry.GetString("source") ?? "";
                        if (!string.IsNullOrEmpty(lockSource)) source = lockSource;
                        resolvedPath = lockEntry.GetString("resolvedPath") ?? "";
                    }

                    packages.Add(string.Format(
                        "{{\"name\":\"{0}\",\"version\":\"{1}\",\"source\":\"{2}\",\"resolvedPath\":\"{3}\"}}",
                        Esc(name), Esc(version), Esc(source), Esc(resolvedPath)));
                }

                return string.Format("{{\"packages\":[{0}],\"total\":{1}}}",
                    string.Join(",", packages.ToArray()), packages.Count);
            }
            catch (Exception e)
            {
                return "{\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        public static string SearchRegistry(string query)
        {
            if (string.IsNullOrEmpty(query))
                return "{\"error\":\"query is required\"}";

            try
            {
                var request = Client.SearchAll(false);
                while (!request.IsCompleted)
                    System.Threading.Thread.Sleep(50);

                if (request.Status != StatusCode.Success)
                    return "{\"error\":\"Search failed: " + Esc(request.Error?.message ?? "unknown") + "\"}";

                var results = new List<string>();
                string lowerQuery = query.ToLowerInvariant();
                foreach (var pkg in request.Result)
                {
                    if (pkg.name.ToLowerInvariant().Contains(lowerQuery) ||
                        (pkg.displayName != null && pkg.displayName.ToLowerInvariant().Contains(lowerQuery)))
                    {
                        string latest = pkg.versions.latestCompatible ?? pkg.versions.latest ?? "";
                        results.Add(string.Format(
                            "{{\"name\":\"{0}\",\"displayName\":\"{1}\",\"latestVersion\":\"{2}\",\"description\":\"{3}\"}}",
                            Esc(pkg.name), Esc(pkg.displayName ?? ""), Esc(latest),
                            Esc(pkg.description != null && pkg.description.Length > 200 ? pkg.description.Substring(0, 200) + "..." : pkg.description ?? "")));
                    }
                }

                return string.Format("{{\"query\":\"{0}\",\"results\":[{1}],\"totalResults\":{2}}}",
                    Esc(query), string.Join(",", results.ToArray()), results.Count);
            }
            catch (Exception e)
            {
                return "{\"error\":\"Search error: " + Esc(e.Message) + "\"}";
            }
        }

        public static string InstallPackage(string packageId, string version)
        {
            if (string.IsNullOrEmpty(packageId))
                return "{\"error\":\"packageId is required\"}";
            if (string.IsNullOrEmpty(version))
                return "{\"error\":\"version is required\"}";

            try
            {
                string manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
                string content = File.ReadAllText(manifestPath);
                var manifest = SimpleJson.Parse(content);
                var deps = manifest.Get("dependencies");

                if (deps?.Get(packageId) != null)
                {
                    string existing = deps.GetString(packageId);
                    return string.Format("{{\"success\":true,\"operation\":\"install\",\"packageId\":\"{0}\",\"message\":\"Package already installed at version {1}\"}}",
                        Esc(packageId), Esc(existing));
                }

                // Add to manifest.json directly
                // Simple approach: insert before the closing } of dependencies
                int depsStart = content.IndexOf("\"dependencies\"");
                if (depsStart < 0) return "{\"error\":\"No dependencies section in manifest.json\"}";
                int braceOpen = content.IndexOf('{', depsStart);
                int braceClose = FindMatchingBrace(content, braceOpen);
                if (braceClose < 0) return "{\"error\":\"Malformed manifest.json\"}";

                // Check if deps is empty
                string depsContent = content.Substring(braceOpen + 1, braceClose - braceOpen - 1).Trim();
                string newEntry = "\"" + packageId + "\": \"" + version + "\"";
                string newContent;
                if (string.IsNullOrEmpty(depsContent))
                {
                    newContent = content.Substring(0, braceOpen + 1) + "\n    " + newEntry + "\n  " + content.Substring(braceClose);
                }
                else
                {
                    newContent = content.Substring(0, braceClose) + ",\n    " + newEntry + "\n  " + content.Substring(braceClose);
                }

                File.WriteAllText(manifestPath, newContent);
                AssetDatabase.Refresh();
                Client.Resolve();

                return string.Format("{{\"success\":true,\"operation\":\"install\",\"packageId\":\"{0}\",\"version\":\"{1}\",\"message\":\"Package added to manifest.json\"}}",
                    Esc(packageId), Esc(version));
            }
            catch (Exception e)
            {
                return "{\"error\":\"Install error: " + Esc(e.Message) + "\"}";
            }
        }

        public static string RemovePackage(string packageId)
        {
            if (string.IsNullOrEmpty(packageId))
                return "{\"error\":\"packageId is required\"}";

            try
            {
                string manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
                string content = File.ReadAllText(manifestPath);

                // Check if package exists
                if (!content.Contains("\"" + packageId + "\""))
                    return "{\"error\":\"Package not found in manifest: " + Esc(packageId) + "\"}";

                // Remove the line containing the package
                var lines = new List<string>(content.Split('\n'));
                int removeIdx = -1;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].Contains("\"" + packageId + "\""))
                    {
                        removeIdx = i;
                        break;
                    }
                }

                if (removeIdx >= 0)
                {
                    // Remove trailing comma from previous line if needed
                    string removedLine = lines[removeIdx];
                    lines.RemoveAt(removeIdx);

                    // If previous line ends with comma and current line is the last entry, remove comma
                    if (removeIdx > 0 && removeIdx < lines.Count)
                    {
                        // Check if the next non-empty line starts with }
                        string nextLine = removeIdx < lines.Count ? lines[removeIdx].Trim() : "";
                        if (nextLine.StartsWith("}") && lines[removeIdx - 1].TrimEnd().EndsWith(","))
                        {
                            lines[removeIdx - 1] = lines[removeIdx - 1].TrimEnd().TrimEnd(',');
                        }
                    }

                    File.WriteAllText(manifestPath, string.Join("\n", lines.ToArray()));
                    AssetDatabase.Refresh();
                    Client.Resolve();
                }

                return string.Format("{{\"success\":true,\"operation\":\"remove\",\"packageId\":\"{0}\",\"message\":\"Package removed from manifest.json\"}}",
                    Esc(packageId));
            }
            catch (Exception e)
            {
                return "{\"error\":\"Remove error: " + Esc(e.Message) + "\"}";
            }
        }

        public static string UpdatePackage(string packageId, string targetVersion)
        {
            if (string.IsNullOrEmpty(packageId))
                return "{\"error\":\"packageId is required\"}";
            if (string.IsNullOrEmpty(targetVersion))
                return "{\"error\":\"targetVersion is required\"}";

            try
            {
                string manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
                string content = File.ReadAllText(manifestPath);

                // Find and replace the version
                var regex = new System.Text.RegularExpressions.Regex(
                    "\"" + System.Text.RegularExpressions.Regex.Escape(packageId) + "\"\\s*:\\s*\"[^\"]+\"");
                if (!regex.IsMatch(content))
                    return "{\"error\":\"Package not found in manifest: " + Esc(packageId) + "\"}";

                string newContent = regex.Replace(content, "\"" + packageId + "\": \"" + targetVersion + "\"");
                File.WriteAllText(manifestPath, newContent);
                AssetDatabase.Refresh();
                Client.Resolve();

                return string.Format("{{\"success\":true,\"operation\":\"update\",\"packageId\":\"{0}\",\"version\":\"{1}\",\"message\":\"Package version updated in manifest.json\"}}",
                    Esc(packageId), Esc(targetVersion));
            }
            catch (Exception e)
            {
                return "{\"error\":\"Update error: " + Esc(e.Message) + "\"}";
            }
        }

        public static string CheckCompatibility(string packageId, string version)
        {
            if (string.IsNullOrEmpty(packageId))
                return "{\"error\":\"packageId is required\"}";

            try
            {
                string identifier = string.IsNullOrEmpty(version) ? packageId : packageId + "@" + version;
                var request = Client.Add(identifier);
                // We just want to check, not actually install — use Search instead
                var searchReq = Client.SearchAll(false);
                while (!searchReq.IsCompleted)
                    System.Threading.Thread.Sleep(50);

                if (searchReq.Status != StatusCode.Success)
                    return "{\"error\":\"Compatibility check failed: " + Esc(searchReq.Error?.message ?? "unknown") + "\"}";

                string unityVersion = Application.unityVersion;
                bool found = false;
                bool compatible = false;
                string latestVersion = "";

                foreach (var pkg in searchReq.Result)
                {
                    if (pkg.name == packageId)
                    {
                        found = true;
                        latestVersion = pkg.versions.latestCompatible ?? "";
                        compatible = !string.IsNullOrEmpty(latestVersion);
                        if (!string.IsNullOrEmpty(version))
                        {
                            // Check if requested version exists in compatible list
                            compatible = false;
                            foreach (var v in pkg.versions.compatible)
                            {
                                if (v == version) { compatible = true; break; }
                            }
                        }
                        break;
                    }
                }

                if (!found)
                    return string.Format("{{\"packageId\":\"{0}\",\"compatible\":false,\"unityVersion\":\"{1}\",\"message\":\"Package not found in registry\"}}",
                        Esc(packageId), Esc(unityVersion));

                return string.Format(
                    "{{\"packageId\":\"{0}\",\"version\":\"{1}\",\"compatible\":{2},\"unityVersion\":\"{3}\",\"latestCompatible\":\"{4}\",\"message\":\"{5}\"}}",
                    Esc(packageId), Esc(version ?? ""), compatible ? "true" : "false",
                    Esc(unityVersion), Esc(latestVersion),
                    compatible ? "Package is compatible" : "Version may not be compatible");
            }
            catch (Exception e)
            {
                return "{\"error\":\"Compatibility check error: " + Esc(e.Message) + "\"}";
            }
        }

        private static string ClassifySource(string version)
        {
            if (version.StartsWith("https://") || version.StartsWith("git://") || version.StartsWith("ssh://"))
                return "git";
            if (version.StartsWith("file:"))
                return "local";
            return "registry";
        }

        private static int FindMatchingBrace(string content, int openIdx)
        {
            int depth = 0;
            for (int i = openIdx; i < content.Length; i++)
            {
                if (content[i] == '{') depth++;
                else if (content[i] == '}') { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        private static string Esc(string str)
        {
            if (str == null) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
