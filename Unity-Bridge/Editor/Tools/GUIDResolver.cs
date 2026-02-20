using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;

namespace UnityBridge
{
    /// <summary>
    /// Resolves GUIDs to asset paths using AssetDatabase with lazy-built cache.
    /// All calls execute on the main thread (via bridge); no explicit locking needed.
    /// </summary>
    public static class GUIDResolver
    {
        private static Dictionary<string, string> _cache;
        private static DateTime _lastBuildTime;
        private static long _buildDurationMs;

        /// <summary>
        /// Scans all assets via AssetDatabase and builds the GUID-to-path cache.
        /// Can be called explicitly to rebuild, or is called automatically on first Resolve.
        /// </summary>
        public static string BuildCache()
        {
            var sw = Stopwatch.StartNew();
            _cache = new Dictionary<string, string>();

            // Primary approach: use AssetDatabase.FindAssets to get all GUIDs
            var guids = AssetDatabase.FindAssets("");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                    _cache[guid] = path;
            }

            sw.Stop();
            _buildDurationMs = sw.ElapsedMilliseconds;
            _lastBuildTime = DateTime.UtcNow;

            return string.Format(
                "{{\"success\":true,\"cacheSize\":{0},\"buildDurationMs\":{1},\"timestamp\":\"{2}\"}}",
                _cache.Count,
                _buildDurationMs,
                _lastBuildTime.ToString("o")
            );
        }

        /// <summary>
        /// Resolves a single GUID to an asset path. Auto-builds cache if needed.
        /// </summary>
        public static string Resolve(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return "{\"error\":\"GUID is required\"}";

            EnsureCache();
            guid = guid.Trim().ToLower();

            if (_cache.TryGetValue(guid, out string path))
            {
                string assetType = GetAssetType(path);
                return string.Format(
                    "{{\"guid\":\"{0}\",\"assetPath\":\"{1}\",\"assetType\":\"{2}\"}}",
                    EscapeJson(guid), EscapeJson(path), EscapeJson(assetType)
                );
            }

            // Also try via AssetDatabase directly (might be a built-in resource)
            string directPath = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(directPath))
            {
                string assetType = GetAssetType(directPath);
                return string.Format(
                    "{{\"guid\":\"{0}\",\"assetPath\":\"{1}\",\"assetType\":\"{2}\"}}",
                    EscapeJson(guid), EscapeJson(directPath), EscapeJson(assetType)
                );
            }

            return string.Format(
                "{{\"guid\":\"{0}\",\"assetPath\":null,\"assetType\":null,\"error\":\"GUID not found in cache. It may be a built-in Unity resource or the cache may need rebuilding.\"}}",
                EscapeJson(guid)
            );
        }

        /// <summary>
        /// Resolves multiple GUIDs in one call. Accepts a JSON array string of GUIDs.
        /// </summary>
        public static string ResolveMultiple(string guidsJson)
        {
            if (string.IsNullOrEmpty(guidsJson))
                return "{\"error\":\"guidsJson is required\"}";

            EnsureCache();

            // Parse JSON array of strings
            var guids = ParseStringArray(guidsJson);
            var results = new List<string>();
            int resolved = 0, unresolved = 0;

            foreach (var guid in guids)
            {
                string g = guid.Trim().ToLower();
                if (_cache.TryGetValue(g, out string path))
                {
                    string assetType = GetAssetType(path);
                    results.Add(string.Format(
                        "{{\"guid\":\"{0}\",\"assetPath\":\"{1}\",\"assetType\":\"{2}\"}}",
                        EscapeJson(g), EscapeJson(path), EscapeJson(assetType)
                    ));
                    resolved++;
                }
                else
                {
                    string directPath = AssetDatabase.GUIDToAssetPath(g);
                    if (!string.IsNullOrEmpty(directPath))
                    {
                        string assetType = GetAssetType(directPath);
                        results.Add(string.Format(
                            "{{\"guid\":\"{0}\",\"assetPath\":\"{1}\",\"assetType\":\"{2}\"}}",
                            EscapeJson(g), EscapeJson(directPath), EscapeJson(assetType)
                        ));
                        resolved++;
                    }
                    else
                    {
                        results.Add(string.Format(
                            "{{\"guid\":\"{0}\",\"assetPath\":null,\"assetType\":null,\"error\":\"GUID not found in cache\"}}",
                            EscapeJson(g)
                        ));
                        unresolved++;
                    }
                }
            }

            return string.Format(
                "{{\"results\":[{0}],\"resolved\":{1},\"unresolved\":{2},\"total\":{3}}}",
                string.Join(",", results.ToArray()),
                resolved, unresolved, guids.Count
            );
        }

        /// <summary>
        /// Returns cache statistics.
        /// </summary>
        public static string GetCacheStats()
        {
            bool isBuilt = _cache != null;
            return string.Format(
                "{{\"cacheSize\":{0},\"lastBuildTime\":\"{1}\",\"buildDurationMs\":{2},\"isCacheBuilt\":{3}}}",
                isBuilt ? _cache.Count : 0,
                isBuilt ? _lastBuildTime.ToString("o") : "",
                _buildDurationMs,
                isBuilt ? "true" : "false"
            );
        }

        private static void EnsureCache()
        {
            if (_cache == null)
                BuildCache();
        }

        private static string GetAssetType(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return "Unknown";

            var type = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
            if (type != null) return type.Name;

            // Fallback: classify by extension
            string ext = System.IO.Path.GetExtension(assetPath).ToLower();
            switch (ext)
            {
                case ".cs": return "MonoScript";
                case ".unity": return "SceneAsset";
                case ".prefab": return "Prefab";
                case ".mat": return "Material";
                case ".png": case ".jpg": case ".jpeg": case ".tga": case ".psd": return "Texture2D";
                case ".wav": case ".mp3": case ".ogg": return "AudioClip";
                case ".fbx": case ".obj": case ".blend": return "Model";
                case ".asset": return "ScriptableObject";
                case ".anim": return "AnimationClip";
                case ".controller": return "AnimatorController";
                case ".shader": return "Shader";
                case ".ttf": case ".otf": return "Font";
                default: return "Unknown";
            }
        }

        private static List<string> ParseStringArray(string json)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(json)) return result;

            string trimmed = json.Trim();
            if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                trimmed = trimmed.Substring(1, trimmed.Length - 2).Trim();

            if (string.IsNullOrEmpty(trimmed)) return result;

            // Split by commas, handle quoted strings
            bool inString = false;
            bool escape = false;
            int start = 0;

            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (escape) { escape = false; continue; }
                if (c == '\\' && inString) { escape = true; continue; }
                if (c == '"') { inString = !inString; continue; }
                if (c == ',' && !inString)
                {
                    string token = trimmed.Substring(start, i - start).Trim().Trim('"');
                    if (!string.IsNullOrEmpty(token)) result.Add(token);
                    start = i + 1;
                }
            }
            string last = trimmed.Substring(start).Trim().Trim('"');
            if (!string.IsNullOrEmpty(last)) result.Add(last);

            return result;
        }

        private static string EscapeJson(string str)
        {
            if (str == null) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
