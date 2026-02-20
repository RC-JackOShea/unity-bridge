using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnityBridge
{
    /// <summary>
    /// Discovers all prefabs in the project and returns structured JSON manifest.
    /// Identifies variants, nested prefabs, and root components.
    /// </summary>
    public static class PrefabInventoryTool
    {
        public static string GetPrefabManifest()
        {
            var guids = AssetDatabase.FindAssets("t:Prefab");
            var entries = new List<string>();

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/")) continue;

                string name = System.IO.Path.GetFileNameWithoutExtension(path);

                // Load prefab to inspect
                var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefabAsset == null) continue;

                bool isVariant = PrefabUtility.GetPrefabAssetType(prefabAsset) == PrefabAssetType.Variant;
                string basePrefab = "null";
                if (isVariant)
                {
                    var source = PrefabUtility.GetCorrespondingObjectFromSource(prefabAsset);
                    if (source != null)
                    {
                        string basePath = AssetDatabase.GetAssetPath(source);
                        basePrefab = "\"" + EscapeJson(basePath) + "\"";
                    }
                }

                // Root components
                var rootComps = prefabAsset.GetComponents<Component>();
                var compNames = new List<string>();
                foreach (var comp in rootComps)
                {
                    if (comp == null) continue;
                    compNames.Add("\"" + EscapeJson(comp.GetType().Name) + "\"");
                }

                // Nested prefabs
                var nestedPaths = new List<string>();
                FindNestedPrefabs(prefabAsset.transform, path, nestedPaths);

                entries.Add(string.Format(
                    "{{\"path\":\"{0}\",\"guid\":\"{1}\",\"name\":\"{2}\",\"isVariant\":{3},\"basePrefab\":{4},\"rootComponents\":[{5}],\"nestedPrefabs\":[{6}]}}",
                    EscapeJson(path), EscapeJson(guid), EscapeJson(name),
                    isVariant ? "true" : "false", basePrefab,
                    string.Join(",", compNames.ToArray()),
                    string.Join(",", nestedPaths.ToArray())
                ));
            }

            return string.Format(
                "{{\"prefabs\":[{0}],\"totalPrefabs\":{1}}}",
                string.Join(",", entries.ToArray()), entries.Count
            );
        }

        private static void FindNestedPrefabs(Transform parent, string ownPath, List<string> results)
        {
            foreach (Transform child in parent)
            {
                if (PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject))
                {
                    string nestedPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child.gameObject);
                    if (!string.IsNullOrEmpty(nestedPath) && nestedPath != ownPath)
                        results.Add("\"" + EscapeJson(nestedPath) + "\"");
                }
                FindNestedPrefabs(child, ownPath, results);
            }
        }

        private static string EscapeJson(string str)
        {
            if (str == null) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
