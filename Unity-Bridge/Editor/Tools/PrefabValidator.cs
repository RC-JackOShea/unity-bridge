using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnityBridge
{
    /// <summary>
    /// Validates prefabs against configurable rules and returns structured issue reports.
    /// </summary>
    public static class PrefabValidator
    {
        private struct Issue
        {
            public string rule, severity, gameObjectPath, component, description;
        }

        private static readonly Dictionary<string, bool> DefaultRules = new Dictionary<string, bool>
        {
            {"MissingScript", true},
            {"BrokenObjectReference", true},
            {"ZeroSizeRectTransform", true},
            {"EmptyGameObject", true},
            {"RigidbodyWithoutCollider", true},
            {"DisabledComponent", true},
            {"DuplicateComponents", true},
            {"MissingMeshReference", true},
            {"MissingMaterialReference", true}
        };

        public static string ValidatePrefab(string prefabPath)
        {
            return ValidateInternal(prefabPath, DefaultRules);
        }

        public static string ValidateAllPrefabs()
        {
            var guids = AssetDatabase.FindAssets("t:Prefab");
            var results = new List<string>();
            int totalErrors = 0, totalWarnings = 0, totalInfo = 0;
            int withIssues = 0;

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/")) continue;

                var issues = RunValidation(path, DefaultRules);
                int e = 0, w = 0, inf = 0;
                foreach (var iss in issues)
                {
                    if (iss.severity == "error") e++;
                    else if (iss.severity == "warning") w++;
                    else inf++;
                }

                if (issues.Count > 0) withIssues++;
                totalErrors += e; totalWarnings += w; totalInfo += inf;

                results.Add(string.Format(
                    "{{\"prefabPath\":\"{0}\",\"errors\":{1},\"warnings\":{2},\"info\":{3}}}",
                    Esc(path), e, w, inf));
            }

            return string.Format(
                "{{\"prefabsValidated\":{0},\"prefabsWithIssues\":{1},\"results\":[{2}],\"totalSummary\":{{\"errors\":{3},\"warnings\":{4},\"info\":{5},\"totalIssues\":{6}}}}}",
                guids.Length, withIssues, string.Join(",", results.ToArray()),
                totalErrors, totalWarnings, totalInfo, totalErrors + totalWarnings + totalInfo);
        }

        public static string ValidatePrefabWithRules(string prefabPath, string rulesJson)
        {
            var rules = new Dictionary<string, bool>(DefaultRules);
            if (!string.IsNullOrEmpty(rulesJson))
            {
                try
                {
                    var parsed = SimpleJson.Parse(rulesJson);
                    if (parsed.obj != null)
                    {
                        foreach (var kv in parsed.obj)
                            rules[kv.Key] = kv.Value.AsBool();
                    }
                }
                catch (Exception e)
                {
                    return "{\"error\":\"Rules JSON parse error: " + Esc(e.Message) + "\"}";
                }
            }
            return ValidateInternal(prefabPath, rules);
        }

        private static string ValidateInternal(string prefabPath, Dictionary<string, bool> rules)
        {
            if (string.IsNullOrEmpty(prefabPath))
                return "{\"error\":\"prefabPath is required\"}";

            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabAsset == null)
                return "{\"error\":\"Prefab not found: " + Esc(prefabPath) + "\"}";

            var issues = RunValidation(prefabPath, rules);

            int errors = 0, warnings = 0, info = 0;
            var issueJsons = new List<string>();
            foreach (var iss in issues)
            {
                if (iss.severity == "error") errors++;
                else if (iss.severity == "warning") warnings++;
                else info++;

                issueJsons.Add(string.Format(
                    "{{\"rule\":\"{0}\",\"severity\":\"{1}\",\"gameObjectPath\":\"{2}\",\"component\":{3},\"description\":\"{4}\"}}",
                    Esc(iss.rule), Esc(iss.severity), Esc(iss.gameObjectPath),
                    iss.component != null ? "\"" + Esc(iss.component) + "\"" : "null",
                    Esc(iss.description)));
            }

            return string.Format(
                "{{\"prefabPath\":\"{0}\",\"valid\":{1},\"issues\":[{2}],\"summary\":{{\"errors\":{3},\"warnings\":{4},\"info\":{5},\"totalIssues\":{6}}}}}",
                Esc(prefabPath), errors == 0 ? "true" : "false",
                string.Join(",", issueJsons.ToArray()),
                errors, warnings, info, issues.Count);
        }

        private static List<Issue> RunValidation(string prefabPath, Dictionary<string, bool> rules)
        {
            var issues = new List<Issue>();
            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(prefabPath);
                if (root == null) return issues;

                if (IsEnabled(rules, "MissingScript")) CheckMissingScript(root, root.name, issues);
                if (IsEnabled(rules, "BrokenObjectReference")) CheckBrokenObjectReference(root, root.name, issues);
                if (IsEnabled(rules, "ZeroSizeRectTransform")) CheckZeroSizeRectTransform(root, root.name, issues);
                if (IsEnabled(rules, "EmptyGameObject")) CheckEmptyGameObject(root, root.name, issues);
                if (IsEnabled(rules, "RigidbodyWithoutCollider")) CheckRigidbodyWithoutCollider(root, root.name, issues);
                if (IsEnabled(rules, "DisabledComponent")) CheckDisabledComponent(root, root.name, issues);
                if (IsEnabled(rules, "DuplicateComponents")) CheckDuplicateComponents(root, root.name, issues);
                if (IsEnabled(rules, "MissingMeshReference")) CheckMissingMeshReference(root, root.name, issues);
                if (IsEnabled(rules, "MissingMaterialReference")) CheckMissingMaterialReference(root, root.name, issues);
            }
            catch { }
            finally
            {
                if (root != null) PrefabUtility.UnloadPrefabContents(root);
            }
            return issues;
        }

        private static bool IsEnabled(Dictionary<string, bool> rules, string name)
        {
            return rules.ContainsKey(name) && rules[name];
        }

        private static void CheckMissingScript(GameObject go, string path, List<Issue> issues)
        {
            var comps = go.GetComponents<Component>();
            foreach (var c in comps)
            {
                if (c == null)
                {
                    issues.Add(new Issue
                    {
                        rule = "MissingScript", severity = "error", gameObjectPath = path,
                        component = null,
                        description = "GameObject '" + go.name + "' has a missing script reference"
                    });
                }
            }
            foreach (Transform child in go.transform)
                CheckMissingScript(child.gameObject, path + "/" + child.name, issues);
        }

        private static void CheckBrokenObjectReference(GameObject go, string path, List<Issue> issues)
        {
            var comps = go.GetComponents<Component>();
            foreach (var c in comps)
            {
                if (c == null) continue;
                try
                {
                    var so = new SerializedObject(c);
                    var iter = so.GetIterator();
                    if (iter.NextVisible(true))
                    {
                        do
                        {
                            if (iter.propertyType == SerializedPropertyType.ObjectReference
                                && iter.objectReferenceValue == null
                                && iter.objectReferenceInstanceIDValue != 0)
                            {
                                issues.Add(new Issue
                                {
                                    rule = "BrokenObjectReference", severity = "warning", gameObjectPath = path,
                                    component = c.GetType().Name,
                                    description = "Property '" + iter.name + "' on " + c.GetType().Name + " has a broken object reference"
                                });
                            }
                        } while (iter.NextVisible(false));
                    }
                }
                catch { }
            }
            foreach (Transform child in go.transform)
                CheckBrokenObjectReference(child.gameObject, path + "/" + child.name, issues);
        }

        private static void CheckZeroSizeRectTransform(GameObject go, string path, List<Issue> issues)
        {
            var rt = go.GetComponent<RectTransform>();
            if (rt != null)
            {
                var sd = rt.sizeDelta;
                if (sd.x == 0 || sd.y == 0)
                {
                    issues.Add(new Issue
                    {
                        rule = "ZeroSizeRectTransform", severity = "warning", gameObjectPath = path,
                        component = "RectTransform",
                        description = "RectTransform has zero " + (sd.x == 0 && sd.y == 0 ? "width and height" : sd.x == 0 ? "width" : "height")
                    });
                }
            }
            foreach (Transform child in go.transform)
                CheckZeroSizeRectTransform(child.gameObject, path + "/" + child.name, issues);
        }

        private static void CheckEmptyGameObject(GameObject go, string path, List<Issue> issues)
        {
            var comps = go.GetComponents<Component>();
            int nonTransform = 0;
            foreach (var c in comps)
            {
                if (c != null && !(c is Transform)) nonTransform++;
            }
            if (nonTransform == 0 && go.transform.childCount == 0)
            {
                issues.Add(new Issue
                {
                    rule = "EmptyGameObject", severity = "info", gameObjectPath = path,
                    component = null,
                    description = "GameObject '" + go.name + "' has no components besides Transform and no children"
                });
            }
            foreach (Transform child in go.transform)
                CheckEmptyGameObject(child.gameObject, path + "/" + child.name, issues);
        }

        private static void CheckRigidbodyWithoutCollider(GameObject go, string path, List<Issue> issues)
        {
            if (go.GetComponent<Rigidbody>() != null && go.GetComponent<Collider>() == null)
            {
                issues.Add(new Issue
                {
                    rule = "RigidbodyWithoutCollider", severity = "warning", gameObjectPath = path,
                    component = "Rigidbody",
                    description = "GameObject '" + go.name + "' has a Rigidbody but no Collider"
                });
            }
            foreach (Transform child in go.transform)
                CheckRigidbodyWithoutCollider(child.gameObject, path + "/" + child.name, issues);
        }

        private static void CheckDisabledComponent(GameObject go, string path, List<Issue> issues)
        {
            var comps = go.GetComponents<Component>();
            foreach (var c in comps)
            {
                if (c is Behaviour b && !b.enabled && !(c is Transform))
                {
                    issues.Add(new Issue
                    {
                        rule = "DisabledComponent", severity = "info", gameObjectPath = path,
                        component = c.GetType().Name,
                        description = c.GetType().Name + " is disabled on '" + go.name + "'"
                    });
                }
            }
            foreach (Transform child in go.transform)
                CheckDisabledComponent(child.gameObject, path + "/" + child.name, issues);
        }

        private static void CheckDuplicateComponents(GameObject go, string path, List<Issue> issues)
        {
            var typeCounts = new Dictionary<Type, int>();
            var comps = go.GetComponents<Component>();
            foreach (var c in comps)
            {
                if (c == null) continue;
                var t = c.GetType();
                if (t == typeof(Transform)) continue;
                if (!typeCounts.ContainsKey(t)) typeCounts[t] = 0;
                typeCounts[t]++;
            }
            foreach (var kv in typeCounts)
            {
                if (kv.Value > 1)
                {
                    issues.Add(new Issue
                    {
                        rule = "DuplicateComponents", severity = "warning", gameObjectPath = path,
                        component = kv.Key.Name,
                        description = kv.Key.Name + " appears " + kv.Value + " times on '" + go.name + "'"
                    });
                }
            }
            foreach (Transform child in go.transform)
                CheckDuplicateComponents(child.gameObject, path + "/" + child.name, issues);
        }

        private static void CheckMissingMeshReference(GameObject go, string path, List<Issue> issues)
        {
            var mf = go.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh == null)
            {
                issues.Add(new Issue
                {
                    rule = "MissingMeshReference", severity = "warning", gameObjectPath = path,
                    component = "MeshFilter",
                    description = "MeshFilter on '" + go.name + "' has no mesh assigned"
                });
            }
            foreach (Transform child in go.transform)
                CheckMissingMeshReference(child.gameObject, path + "/" + child.name, issues);
        }

        private static void CheckMissingMaterialReference(GameObject go, string path, List<Issue> issues)
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                var mats = mr.sharedMaterials;
                if (mats == null || mats.Length == 0)
                {
                    issues.Add(new Issue
                    {
                        rule = "MissingMaterialReference", severity = "warning", gameObjectPath = path,
                        component = "MeshRenderer",
                        description = "MeshRenderer on '" + go.name + "' has no materials"
                    });
                }
                else
                {
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] == null)
                        {
                            issues.Add(new Issue
                            {
                                rule = "MissingMaterialReference", severity = "warning", gameObjectPath = path,
                                component = "MeshRenderer",
                                description = "MeshRenderer on '" + go.name + "' has null material at index " + i
                            });
                            break;
                        }
                    }
                }
            }
            foreach (Transform child in go.transform)
                CheckMissingMaterialReference(child.gameObject, path + "/" + child.name, issues);
        }

        private static string Esc(string str)
        {
            if (str == null) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
