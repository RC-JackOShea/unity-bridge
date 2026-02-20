using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityBridge
{
    /// <summary>
    /// Extracts all components and serialized properties from a GameObject.
    /// Uses SerializedObject/SerializedProperty for deep property inspection.
    /// Callable via: execute UnityBridge.ComponentDetailExtractor.GetComponentDetails '["scenePath","gameObjectPath"]'
    /// </summary>
    public static class ComponentDetailExtractor
    {
        private const int MAX_PROPERTY_DEPTH = 8;
        private const int MAX_ITERATIONS = 500;

        // Internal Unity properties to skip for cleaner output
        private static readonly HashSet<string> SkipProperties = new HashSet<string>
        {
            "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance",
            "m_PrefabAsset", "m_GameObject", "m_Script"
        };

        /// <summary>
        /// Opens a scene, finds a GameObject by path, and extracts all component details.
        /// </summary>
        public static string GetComponentDetails(string scenePath, string gameObjectPath)
        {
            if (string.IsNullOrEmpty(scenePath))
                return "{\"error\":\"scenePath is required\"}";
            if (string.IsNullOrEmpty(gameObjectPath))
                return "{\"error\":\"gameObjectPath is required\"}";

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
                    scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

                if (!scene.IsValid() || !scene.isLoaded)
                    return "{\"error\":\"Failed to open scene: " + EscapeJson(scenePath) + "\"}";

                var go = ResolveGameObjectPath(scene, gameObjectPath);
                if (go == null)
                    return "{\"error\":\"GameObject not found at path: " + EscapeJson(gameObjectPath) + "\"}";

                var components = go.GetComponents<Component>();
                var compEntries = new List<string>();

                for (int idx = 0; idx < components.Length; idx++)
                {
                    var comp = components[idx];
                    if (comp == null)
                    {
                        compEntries.Add("{\"type\":\"MissingScript\",\"index\":" + idx + ",\"error\":\"Script reference is missing\"}");
                        continue;
                    }

                    string typeName = comp.GetType().FullName;
                    var entry = new System.Text.StringBuilder();
                    entry.Append("{\"type\":\"").Append(EscapeJson(typeName)).Append("\",\"index\":").Append(idx);

                    // Enabled state
                    if (comp is Behaviour b)
                        entry.Append(",\"enabled\":").Append(b.enabled ? "true" : "false");
                    else if (comp is Renderer r)
                        entry.Append(",\"enabled\":").Append(r.enabled ? "true" : "false");
                    else if (comp is Collider c)
                        entry.Append(",\"enabled\":").Append(c.enabled ? "true" : "false");

                    // Properties via SerializedObject
                    entry.Append(",\"properties\":[");
                    var so = new SerializedObject(comp);
                    var iter = so.GetIterator();
                    bool first = true;
                    int iterations = 0;

                    if (iter.NextVisible(true))
                    {
                        do
                        {
                            if (iterations++ > MAX_ITERATIONS) break;
                            if (iter.depth > MAX_PROPERTY_DEPTH) continue;
                            if (SkipProperties.Contains(iter.name)) continue;

                            string propJson = SerializeProperty(iter);
                            if (propJson != null)
                            {
                                if (!first) entry.Append(",");
                                entry.Append(propJson);
                                first = false;
                            }
                        } while (iter.NextVisible(false));
                    }

                    entry.Append("]}");
                    compEntries.Add(entry.ToString());
                }

                return string.Format(
                    "{{\"scenePath\":\"{0}\",\"gameObjectPath\":\"{1}\",\"gameObjectName\":\"{2}\",\"components\":[{3}]}}",
                    EscapeJson(scenePath),
                    EscapeJson(gameObjectPath),
                    EscapeJson(go.name),
                    string.Join(",", compEntries.ToArray())
                );
            }
            finally
            {
                if (!wasAlreadyLoaded && scene.IsValid())
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static GameObject ResolveGameObjectPath(Scene scene, string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            var parts = path.Split('/');
            var rootObjects = scene.GetRootGameObjects();

            // Find root object matching first segment
            GameObject current = null;
            foreach (var root in rootObjects)
            {
                if (root.name == parts[0])
                {
                    current = root;
                    break;
                }
            }
            if (current == null) return null;

            // Traverse children for remaining segments
            for (int i = 1; i < parts.Length; i++)
            {
                var child = current.transform.Find(parts[i]);
                if (child == null) return null;
                current = child.gameObject;
            }

            return current;
        }

        private static string SerializeProperty(SerializedProperty prop)
        {
            string value;
            string typeName = prop.propertyType.ToString();

            try
            {
                switch (prop.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        value = prop.intValue.ToString();
                        break;
                    case SerializedPropertyType.Boolean:
                        value = prop.boolValue ? "true" : "false";
                        break;
                    case SerializedPropertyType.Float:
                        value = prop.floatValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        break;
                    case SerializedPropertyType.String:
                        value = "\"" + EscapeJson(prop.stringValue) + "\"";
                        break;
                    case SerializedPropertyType.Color:
                        var c = prop.colorValue;
                        value = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "{{\"r\":{0},\"g\":{1},\"b\":{2},\"a\":{3}}}", c.r, c.g, c.b, c.a);
                        break;
                    case SerializedPropertyType.ObjectReference:
                        if (prop.objectReferenceValue != null)
                        {
                            var obj = prop.objectReferenceValue;
                            value = string.Format(
                                "{{\"instanceID\":{0},\"name\":\"{1}\",\"type\":\"{2}\"}}",
                                obj.GetInstanceID(),
                                EscapeJson(obj.name),
                                EscapeJson(obj.GetType().Name)
                            );
                        }
                        else
                        {
                            value = "null";
                        }
                        break;
                    case SerializedPropertyType.LayerMask:
                        value = prop.intValue.ToString();
                        break;
                    case SerializedPropertyType.Enum:
                        value = prop.enumValueIndex.ToString();
                        break;
                    case SerializedPropertyType.Vector2:
                        var v2 = prop.vector2Value;
                        value = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "{{\"x\":{0},\"y\":{1}}}", v2.x, v2.y);
                        break;
                    case SerializedPropertyType.Vector3:
                        var v3 = prop.vector3Value;
                        value = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "{{\"x\":{0},\"y\":{1},\"z\":{2}}}", v3.x, v3.y, v3.z);
                        break;
                    case SerializedPropertyType.Vector4:
                        var v4 = prop.vector4Value;
                        value = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "{{\"x\":{0},\"y\":{1},\"z\":{2},\"w\":{3}}}", v4.x, v4.y, v4.z, v4.w);
                        break;
                    case SerializedPropertyType.Rect:
                        var rect = prop.rectValue;
                        value = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "{{\"x\":{0},\"y\":{1},\"width\":{2},\"height\":{3}}}",
                            rect.x, rect.y, rect.width, rect.height);
                        break;
                    case SerializedPropertyType.ArraySize:
                        value = prop.intValue.ToString();
                        typeName = "ArraySize";
                        break;
                    case SerializedPropertyType.AnimationCurve:
                        value = "{\"keys\":" + (prop.animationCurveValue?.length ?? 0) + "}";
                        break;
                    case SerializedPropertyType.Bounds:
                        var bounds = prop.boundsValue;
                        value = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "{{\"center\":{{\"x\":{0},\"y\":{1},\"z\":{2}}},\"extents\":{{\"x\":{3},\"y\":{4},\"z\":{5}}}}}",
                            bounds.center.x, bounds.center.y, bounds.center.z,
                            bounds.extents.x, bounds.extents.y, bounds.extents.z);
                        break;
                    case SerializedPropertyType.Quaternion:
                        var q = prop.quaternionValue;
                        value = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "{{\"x\":{0},\"y\":{1},\"z\":{2},\"w\":{3}}}", q.x, q.y, q.z, q.w);
                        break;
                    case SerializedPropertyType.Vector2Int:
                        var v2i = prop.vector2IntValue;
                        value = string.Format("{{\"x\":{0},\"y\":{1}}}", v2i.x, v2i.y);
                        break;
                    case SerializedPropertyType.Vector3Int:
                        var v3i = prop.vector3IntValue;
                        value = string.Format("{{\"x\":{0},\"y\":{1},\"z\":{2}}}", v3i.x, v3i.y, v3i.z);
                        break;
                    case SerializedPropertyType.RectInt:
                        var ri = prop.rectIntValue;
                        value = string.Format("{{\"x\":{0},\"y\":{1},\"width\":{2},\"height\":{3}}}",
                            ri.x, ri.y, ri.width, ri.height);
                        break;
                    case SerializedPropertyType.BoundsInt:
                        var bi = prop.boundsIntValue;
                        value = string.Format(
                            "{{\"position\":{{\"x\":{0},\"y\":{1},\"z\":{2}}},\"size\":{{\"x\":{3},\"y\":{4},\"z\":{5}}}}}",
                            bi.position.x, bi.position.y, bi.position.z,
                            bi.size.x, bi.size.y, bi.size.z);
                        break;
                    case SerializedPropertyType.Gradient:
                        value = "{\"type\":\"Gradient\"}";
                        break;
                    case SerializedPropertyType.Generic:
                        // Skip generic/compound properties — their children are traversed individually
                        return null;
                    default:
                        value = "\"" + EscapeJson(prop.propertyType.ToString()) + "\"";
                        break;
                }
            }
            catch (Exception)
            {
                value = "\"<error reading value>\"";
            }

            return string.Format(
                "{{\"name\":\"{0}\",\"type\":\"{1}\",\"value\":{2}}}",
                EscapeJson(prop.name),
                EscapeJson(typeName),
                value
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
