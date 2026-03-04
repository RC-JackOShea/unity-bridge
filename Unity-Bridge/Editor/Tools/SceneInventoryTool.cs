using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityBridge
{
    /// <summary>
    /// Scene discovery and hierarchy extraction tool.
    /// Callable via: execute UnityBridge.SceneInventoryTool.GetSceneManifest
    /// and: execute UnityBridge.SceneInventoryTool.GetSceneHierarchy '["scenePath"]'
    ///
    /// Do not call GetSceneHierarchy while in Play Mode — it opens scenes additively.
    /// </summary>
    public static class SceneInventoryTool
    {
        private const int MAX_DEPTH = 50;

        // ── Scene Management ──────────────────────────────────────────

        /// <summary>
        /// Returns the name of the currently active scene.
        /// Works in both Edit and Play mode.
        /// Callable via: execute UnityBridge.SceneInventoryTool.GetActiveScene
        /// </summary>
        public static string GetActiveScene()
        {
            var scene = SceneManager.GetActiveScene();
            return string.Format(
                "{{\"success\":true,\"name\":\"{0}\",\"path\":\"{1}\",\"buildIndex\":{2},\"isLoaded\":{3}}}",
                EscapeJson(scene.name),
                EscapeJson(scene.path),
                scene.buildIndex,
                scene.isLoaded ? "true" : "false"
            );
        }

        /// <summary>
        /// Loads a scene by name at runtime (Play Mode only).
        /// Callable via: execute UnityBridge.SceneInventoryTool.LoadScene '["SampleScene"]'
        /// </summary>
        public static string LoadScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
                return "{\"success\":false,\"error\":\"sceneName is required\"}";

            if (!Application.isPlaying)
                return "{\"success\":false,\"error\":\"LoadScene requires Play Mode\"}";

            SceneManager.LoadScene(sceneName);
            return string.Format(
                "{{\"success\":true,\"scene\":\"{0}\"}}",
                EscapeJson(sceneName)
            );
        }

        /// <summary>
        /// Opens a scene in the Editor (Edit Mode only).
        /// Callable via: execute UnityBridge.SceneInventoryTool.OpenScene '["Assets/Scenes/MyScene.unity"]'
        /// </summary>
        public static string OpenScene(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath))
                return "{\"success\":false,\"error\":\"scenePath is required\"}";

            if (Application.isPlaying)
                return "{\"success\":false,\"error\":\"OpenScene cannot be used in Play Mode — use LoadScene instead\"}";

            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
                return string.Format(
                    "{{\"success\":false,\"error\":\"Failed to open scene: {0}\"}}",
                    EscapeJson(scenePath)
                );

            return string.Format(
                "{{\"success\":true,\"name\":\"{0}\",\"path\":\"{1}\"}}",
                EscapeJson(scene.name),
                EscapeJson(scene.path)
            );
        }

        // ── Scene Discovery ──────────────────────────────────────────

        /// <summary>
        /// Discovers all .unity scene files in Assets/ and returns build info.
        /// </summary>
        public static string GetSceneManifest()
        {
            // Find all scene assets
            var guids = AssetDatabase.FindAssets("t:Scene");
            var scenePaths = new List<string>();
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".unity") && path.StartsWith("Assets/"))
                    scenePaths.Add(path);
            }

            // Build scene info from EditorBuildSettings
            var buildSceneMap = new Dictionary<string, int>();
            var buildSceneEnabled = new Dictionary<string, bool>();
            var buildScenes = EditorBuildSettings.scenes;
            for (int i = 0; i < buildScenes.Length; i++)
            {
                buildSceneMap[buildScenes[i].path] = i;
                buildSceneEnabled[buildScenes[i].path] = buildScenes[i].enabled;
            }

            // Build JSON
            var entries = new List<string>();
            int buildCount = 0;
            foreach (var scenePath in scenePaths)
            {
                var name = System.IO.Path.GetFileNameWithoutExtension(scenePath);
                bool isInBuild = buildSceneMap.ContainsKey(scenePath);
                int buildIndex = isInBuild ? buildSceneMap[scenePath] : -1;
                bool isEnabled = isInBuild && buildSceneEnabled[scenePath];
                if (isInBuild) buildCount++;

                // Get root object count by checking if scene is loaded
                int rootCount = 0;
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var s = SceneManager.GetSceneAt(i);
                    if (s.path == scenePath && s.isLoaded)
                    {
                        rootCount = s.GetRootGameObjects().Length;
                        break;
                    }
                }

                entries.Add(string.Format(
                    "{{\"name\":\"{0}\",\"path\":\"{1}\",\"buildIndex\":{2},\"isInBuild\":{3},\"isEnabled\":{4},\"rootGameObjects\":{5}}}",
                    EscapeJson(name),
                    EscapeJson(scenePath),
                    buildIndex,
                    isInBuild ? "true" : "false",
                    isEnabled ? "true" : "false",
                    rootCount
                ));
            }

            return string.Format(
                "{{\"scenes\":[{0}],\"totalScenes\":{1},\"buildScenes\":{2}}}",
                string.Join(",", entries.ToArray()),
                scenePaths.Count,
                buildCount
            );
        }

        /// <summary>
        /// Opens a scene additively, extracts full GameObject hierarchy, then closes it.
        /// Returns nested JSON with GameObjects, components, and children.
        /// </summary>
        public static string GetSceneHierarchy(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath))
                return "{\"error\":\"scenePath is required\"}";

            // Verify the scene file exists
            var asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
            if (asset == null)
                return "{\"error\":\"Scene not found: " + EscapeJson(scenePath) + "\"}";

            // Check if scene is already loaded
            Scene scene = default;
            bool wasAlreadyLoaded = false;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.path == scenePath)
                {
                    scene = s;
                    wasAlreadyLoaded = true;
                    break;
                }
            }

            try
            {
                if (!wasAlreadyLoaded)
                {
                    scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                }

                if (!scene.IsValid() || !scene.isLoaded)
                    return "{\"error\":\"Failed to open scene: " + EscapeJson(scenePath) + "\"}";

                var rootObjects = scene.GetRootGameObjects();
                var hierarchyEntries = new List<string>();
                foreach (var go in rootObjects)
                {
                    hierarchyEntries.Add(SerializeGameObject(go, 0));
                }

                return string.Format(
                    "{{\"scenePath\":\"{0}\",\"hierarchy\":[{1}]}}",
                    EscapeJson(scenePath),
                    string.Join(",", hierarchyEntries.ToArray())
                );
            }
            finally
            {
                // Close the scene if we opened it
                if (!wasAlreadyLoaded && scene.IsValid())
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static string SerializeGameObject(GameObject go, int depth)
        {
            if (depth > MAX_DEPTH)
                return "{\"name\":\"[MAX_DEPTH_EXCEEDED]\",\"activeSelf\":false,\"tag\":\"Untagged\",\"layer\":0,\"components\":[],\"children\":[]}";

            // Components
            var components = go.GetComponents<Component>();
            var compEntries = new List<string>();
            foreach (var comp in components)
            {
                if (comp == null) continue; // missing script
                string typeName = comp.GetType().FullName;
                if (comp is Behaviour behaviour)
                {
                    compEntries.Add(string.Format(
                        "{{\"type\":\"{0}\",\"enabled\":{1}}}",
                        EscapeJson(typeName),
                        behaviour.enabled ? "true" : "false"
                    ));
                }
                else if (comp is Renderer renderer)
                {
                    compEntries.Add(string.Format(
                        "{{\"type\":\"{0}\",\"enabled\":{1}}}",
                        EscapeJson(typeName),
                        renderer.enabled ? "true" : "false"
                    ));
                }
                else if (comp is Collider collider)
                {
                    compEntries.Add(string.Format(
                        "{{\"type\":\"{0}\",\"enabled\":{1}}}",
                        EscapeJson(typeName),
                        collider.enabled ? "true" : "false"
                    ));
                }
                else
                {
                    compEntries.Add(string.Format(
                        "{{\"type\":\"{0}\"}}",
                        EscapeJson(typeName)
                    ));
                }
            }

            // Children
            var childEntries = new List<string>();
            foreach (Transform child in go.transform)
            {
                childEntries.Add(SerializeGameObject(child.gameObject, depth + 1));
            }

            return string.Format(
                "{{\"name\":\"{0}\",\"activeSelf\":{1},\"tag\":\"{2}\",\"layer\":{3},\"components\":[{4}],\"children\":[{5}]}}",
                EscapeJson(go.name),
                go.activeSelf ? "true" : "false",
                EscapeJson(go.tag),
                go.layer,
                string.Join(",", compEntries.ToArray()),
                string.Join(",", childEntries.ToArray())
            );
        }

        private static string EscapeJson(string str)
        {
            if (str == null) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
