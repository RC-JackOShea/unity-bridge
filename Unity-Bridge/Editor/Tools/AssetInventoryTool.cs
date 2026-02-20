using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace UnityBridge
{
    /// <summary>
    /// Scans all assets in Assets/ and produces a manifest organized by type.
    /// Provides dependency analysis and unreferenced asset detection.
    /// </summary>
    public static class AssetInventoryTool
    {
        private static readonly Dictionary<string, string> ExtensionToType = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {".cs", "Script"}, {".js", "Script"},
            {".prefab", "Prefab"},
            {".unity", "Scene"},
            {".mat", "Material"},
            {".png", "Texture"}, {".jpg", "Texture"}, {".jpeg", "Texture"}, {".tga", "Texture"}, {".psd", "Texture"}, {".exr", "Texture"}, {".hdr", "Texture"},
            {".wav", "AudioClip"}, {".mp3", "AudioClip"}, {".ogg", "AudioClip"}, {".aif", "AudioClip"}, {".aiff", "AudioClip"},
            {".fbx", "Model"}, {".obj", "Model"}, {".blend", "Model"}, {".dae", "Model"}, {".3ds", "Model"},
            {".asset", "ScriptableObject"},
            {".anim", "Animation"}, {".playable", "Animation"},
            {".controller", "AnimatorController"}, {".overrideController", "AnimatorController"},
            {".shader", "Shader"}, {".shadergraph", "Shader"}, {".shadersubgraph", "Shader"}, {".compute", "Shader"},
            {".ttf", "Font"}, {".otf", "Font"}, {".fontsettings", "Font"},
            {".lighting", "LightingSettings"}, {".renderTexture", "RenderTexture"},
            {".mixer", "AudioMixer"}, {".physicMaterial", "PhysicMaterial"},
            {".guiskin", "GUISkin"}, {".flare", "Flare"},
        };

        /// <summary>
        /// Returns complete asset manifest with summary and per-asset details.
        /// </summary>
        public static string GetFullInventory()
        {
            var guids = AssetDatabase.FindAssets("", new[] { "Assets" });
            var assets = new List<string>();
            var typeCounts = new Dictionary<string, int>();
            var typeSizes = new Dictionary<string, long>();
            long totalSize = 0;

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/")) continue;
                if (AssetDatabase.IsValidFolder(path)) continue;

                string assetType = ClassifyAsset(path);
                long fileSize = 0;
                string lastModified = "";

                try
                {
                    var fi = new FileInfo(path);
                    if (fi.Exists)
                    {
                        fileSize = fi.Length;
                        lastModified = fi.LastWriteTimeUtc.ToString("o");
                    }
                }
                catch { }

                totalSize += fileSize;
                if (!typeCounts.ContainsKey(assetType)) { typeCounts[assetType] = 0; typeSizes[assetType] = 0; }
                typeCounts[assetType]++;
                typeSizes[assetType] += fileSize;

                assets.Add(string.Format(
                    "{{\"path\":\"{0}\",\"guid\":\"{1}\",\"type\":\"{2}\",\"fileSize\":{3},\"lastModified\":\"{4}\"}}",
                    EscapeJson(path), EscapeJson(guid), EscapeJson(assetType), fileSize, EscapeJson(lastModified)
                ));
            }

            // Build summary
            var byType = new List<string>();
            foreach (var kv in typeCounts)
            {
                byType.Add(string.Format(
                    "\"{0}\":{{\"count\":{1},\"sizeBytes\":{2}}}",
                    EscapeJson(kv.Key), kv.Value, typeSizes[kv.Key]
                ));
            }

            return string.Format(
                "{{\"summary\":{{\"totalAssets\":{0},\"totalSizeBytes\":{1},\"byType\":{{{2}}}}},\"assets\":[{3}]}}",
                assets.Count, totalSize,
                string.Join(",", byType.ToArray()),
                string.Join(",", assets.ToArray())
            );
        }

        /// <summary>
        /// Returns assets filtered by type name (e.g. "Script", "Prefab", "Material").
        /// </summary>
        public static string GetInventoryByType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return "{\"error\":\"typeName is required\"}";

            var guids = AssetDatabase.FindAssets("", new[] { "Assets" });
            var assets = new List<string>();

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/")) continue;
                if (AssetDatabase.IsValidFolder(path)) continue;

                string assetType = ClassifyAsset(path);
                if (!string.Equals(assetType, typeName, StringComparison.OrdinalIgnoreCase)) continue;

                long fileSize = 0;
                string lastModified = "";
                try
                {
                    var fi = new FileInfo(path);
                    if (fi.Exists) { fileSize = fi.Length; lastModified = fi.LastWriteTimeUtc.ToString("o"); }
                }
                catch { }

                assets.Add(string.Format(
                    "{{\"path\":\"{0}\",\"guid\":\"{1}\",\"type\":\"{2}\",\"fileSize\":{3},\"lastModified\":\"{4}\"}}",
                    EscapeJson(path), EscapeJson(guid), EscapeJson(assetType), fileSize, EscapeJson(lastModified)
                ));
            }

            return string.Format(
                "{{\"type\":\"{0}\",\"count\":{1},\"assets\":[{2}]}}",
                EscapeJson(typeName), assets.Count, string.Join(",", assets.ToArray())
            );
        }

        /// <summary>
        /// Returns outbound dependencies of an asset (what it references).
        /// </summary>
        public static string GetAssetDependencies(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return "{\"error\":\"assetPath is required\"}";

            var deps = AssetDatabase.GetDependencies(assetPath, false);
            var entries = new List<string>();
            foreach (var dep in deps)
            {
                if (dep == assetPath) continue; // skip self
                string guid = AssetDatabase.AssetPathToGUID(dep);
                string assetType = ClassifyAsset(dep);
                entries.Add(string.Format(
                    "{{\"path\":\"{0}\",\"guid\":\"{1}\",\"type\":\"{2}\"}}",
                    EscapeJson(dep), EscapeJson(guid), EscapeJson(assetType)
                ));
            }

            return string.Format(
                "{{\"assetPath\":\"{0}\",\"dependencyCount\":{1},\"dependencies\":[{2}]}}",
                EscapeJson(assetPath), entries.Count, string.Join(",", entries.ToArray())
            );
        }

        /// <summary>
        /// Returns assets that reference the given asset (inbound references).
        /// Scans all assets to find those that depend on the target.
        /// </summary>
        public static string GetAssetReferencedBy(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return "{\"error\":\"assetPath is required\"}";

            var guids = AssetDatabase.FindAssets("", new[] { "Assets" });
            var referencedBy = new List<string>();

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || path == assetPath) continue;
                if (AssetDatabase.IsValidFolder(path)) continue;

                var deps = AssetDatabase.GetDependencies(path, false);
                foreach (var dep in deps)
                {
                    if (dep == assetPath)
                    {
                        string depType = ClassifyAsset(path);
                        referencedBy.Add(string.Format(
                            "{{\"path\":\"{0}\",\"guid\":\"{1}\",\"type\":\"{2}\"}}",
                            EscapeJson(path), EscapeJson(guid), EscapeJson(depType)
                        ));
                        break;
                    }
                }
            }

            return string.Format(
                "{{\"assetPath\":\"{0}\",\"referencedByCount\":{1},\"referencedBy\":[{2}]}}",
                EscapeJson(assetPath), referencedBy.Count, string.Join(",", referencedBy.ToArray())
            );
        }

        /// <summary>
        /// Finds assets that are not referenced by any build scene or other asset.
        /// </summary>
        public static string FindUnreferencedAssets()
        {
            // Collect all assets referenced from build scenes
            var referencedPaths = new HashSet<string>();
            var buildScenes = EditorBuildSettings.scenes;
            foreach (var scene in buildScenes)
            {
                if (!scene.enabled) continue;
                var deps = AssetDatabase.GetDependencies(scene.path, true);
                foreach (var dep in deps)
                    referencedPaths.Add(dep);
            }

            // Find unreferenced assets
            var guids = AssetDatabase.FindAssets("", new[] { "Assets" });
            var unreferenced = new List<string>();

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/")) continue;
                if (AssetDatabase.IsValidFolder(path)) continue;

                // Skip editor-only assets and meta files
                if (path.Contains("/Editor/")) continue;

                if (!referencedPaths.Contains(path))
                {
                    string assetType = ClassifyAsset(path);
                    unreferenced.Add(string.Format(
                        "{{\"path\":\"{0}\",\"guid\":\"{1}\",\"type\":\"{2}\"}}",
                        EscapeJson(path), EscapeJson(guid), EscapeJson(assetType)
                    ));
                }
            }

            return string.Format(
                "{{\"unreferencedCount\":{0},\"unreferenced\":[{1}]}}",
                unreferenced.Count, string.Join(",", unreferenced.ToArray())
            );
        }

        private static string ClassifyAsset(string path)
        {
            string ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext)) return "Other";
            if (ExtensionToType.TryGetValue(ext, out string assetType))
                return assetType;
            return "Other";
        }

        private static string EscapeJson(string str)
        {
            if (str == null) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
