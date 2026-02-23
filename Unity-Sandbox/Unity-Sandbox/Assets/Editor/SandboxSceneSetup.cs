using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Editor.Setup
{
    /// <summary>
    /// Editor tool that places prefab instances directly into SampleScene.
    /// Run via: execute Game.Editor.Setup.SandboxSceneSetup.PopulateScene
    /// </summary>
    public static class SandboxSceneSetup
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string PrefabDir = "Assets/Resources/Prefabs";

        private static readonly string[] ExpectedObjects = {
            "EventSystem",
            "TestPlayer",
            "ScoreTriggerZone",
            "UICanvas"
        };

        // Also clean up legacy names from before the single-canvas migration
        private static readonly string[] LegacyNames = {
            "ScoreHUD",
            "PauseMenu"
        };

        private static readonly Dictionary<string, Vector3> Positions = new Dictionary<string, Vector3>
        {
            { "TestPlayer", new Vector3(0, 1f, 0) },
            { "ScoreTriggerZone", new Vector3(3, 0.5f, 3) }
        };

        public static string PopulateScene()
        {
            try
            {
                // Open the scene
                var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                if (!scene.IsValid())
                    return "{\"success\":false,\"error\":\"Failed to open scene: " + ScenePath + "\"}";

                // Remove existing game objects that match our prefab names
                // Keep default Unity objects (Main Camera, Directional Light)
                var rootObjects = scene.GetRootGameObjects();
                foreach (var go in rootObjects)
                {
                    if (IsGamePrefabInstance(go.name))
                    {
                        UnityEngine.Object.DestroyImmediate(go);
                    }
                }

                // Instantiate each prefab
                var placed = new List<string>();
                foreach (var name in ExpectedObjects)
                {
                    string prefabPath = PrefabDir + "/" + name + ".prefab";
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    if (prefab == null)
                    {
                        return "{\"success\":false,\"error\":\"Prefab not found: " + prefabPath + "\"}";
                    }

                    // PrefabUtility.InstantiatePrefab preserves the prefab link
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    instance.name = name;

                    // Set position if specified
                    if (Positions.TryGetValue(name, out var pos))
                    {
                        instance.transform.position = pos;
                    }

                    placed.Add(name);
                }

                // Save the scene
                EditorSceneManager.SaveScene(scene);

                return "{\"success\":true,\"message\":\"Scene populated with " + placed.Count + " prefab instances\",\"placed\":[\"" + string.Join("\",\"", placed.ToArray()) + "\"]}";
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        public static string ValidateScene()
        {
            try
            {
                var scene = SceneManager.GetActiveScene();
                if (scene.path != ScenePath)
                {
                    // Try to open it
                    scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                }

                var rootObjects = scene.GetRootGameObjects();
                var found = new List<string>();
                var missing = new List<string>();

                foreach (var expected in ExpectedObjects)
                {
                    bool exists = false;
                    foreach (var go in rootObjects)
                    {
                        if (go.name == expected)
                        {
                            exists = true;
                            break;
                        }
                    }

                    if (exists)
                        found.Add(expected);
                    else
                        missing.Add(expected);
                }

                // Check for prefab link integrity
                var broken = new List<string>();
                foreach (var go in rootObjects)
                {
                    if (Array.IndexOf(ExpectedObjects, go.name) >= 0)
                    {
                        var prefabStatus = PrefabUtility.GetPrefabInstanceStatus(go);
                        if (prefabStatus == PrefabInstanceStatus.MissingAsset ||
                            prefabStatus == PrefabInstanceStatus.NotAPrefab)
                        {
                            broken.Add(go.name);
                        }
                    }
                }

                bool valid = missing.Count == 0 && broken.Count == 0;
                string result = "{\"success\":" + (valid ? "true" : "false");
                result += ",\"found\":[\"" + string.Join("\",\"", found.ToArray()) + "\"]";

                if (missing.Count > 0)
                    result += ",\"missing\":[\"" + string.Join("\",\"", missing.ToArray()) + "\"]";
                if (broken.Count > 0)
                    result += ",\"brokenPrefabLinks\":[\"" + string.Join("\",\"", broken.ToArray()) + "\"]";

                result += "}";
                return result;
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        private static bool IsGamePrefabInstance(string name)
        {
            foreach (var expected in ExpectedObjects)
            {
                if (name == expected || name.StartsWith(expected + " ")) return true;
            }
            foreach (var legacy in LegacyNames)
            {
                if (name == legacy || name.StartsWith(legacy + " ")) return true;
            }
            return false;
        }

        private static string Esc(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
        }
    }
}
