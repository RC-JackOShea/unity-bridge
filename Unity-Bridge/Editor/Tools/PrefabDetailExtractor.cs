using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnityBridge
{
    /// <summary>
    /// Extracts full hierarchy of a prefab with components, properties, and variant overrides.
    /// Uses PrefabUtility.LoadPrefabContents for isolated inspection.
    /// </summary>
    public static class PrefabDetailExtractor
    {
        private const int MAX_DEPTH = 50;
        private const int MAX_PROPERTY_DEPTH = 8;
        private const int MAX_ITERATIONS = 500;

        private static readonly HashSet<string> SkipProperties = new HashSet<string>
        {
            "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance",
            "m_PrefabAsset", "m_GameObject", "m_Script"
        };

        public static string GetPrefabDetail(string prefabPath)
        {
            if (string.IsNullOrEmpty(prefabPath))
                return "{\"error\":\"prefabPath is required\"}";

            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabAsset == null)
                return "{\"error\":\"Prefab not found: " + EscapeJson(prefabPath) + "\"}";

            bool isVariant = PrefabUtility.GetPrefabAssetType(prefabAsset) == PrefabAssetType.Variant;
            string basePrefab = "null";
            if (isVariant)
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(prefabAsset);
                if (source != null)
                    basePrefab = "\"" + EscapeJson(AssetDatabase.GetAssetPath(source)) + "\"";
            }

            // Load prefab contents in isolation
            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(prefabPath);
                if (root == null)
                    return "{\"error\":\"Failed to load prefab contents\"}";

                string hierarchyJson = SerializeGameObject(root, 0);

                // Get property modifications (overrides for variants)
                var overrides = new List<string>();
                var mods = PrefabUtility.GetPropertyModifications(prefabAsset);
                if (mods != null)
                {
                    foreach (var mod in mods)
                    {
                        if (mod.target == null) continue;
                        overrides.Add(string.Format(
                            "{{\"targetPath\":\"{0}\",\"targetObject\":\"{1}\",\"value\":\"{2}\"}}",
                            EscapeJson(mod.propertyPath),
                            EscapeJson(mod.target.name),
                            EscapeJson(mod.value ?? "")
                        ));
                    }
                }

                return string.Format(
                    "{{\"prefabPath\":\"{0}\",\"isVariant\":{1},\"basePrefab\":{2},\"hierarchy\":[{3}],\"overrides\":[{4}]}}",
                    EscapeJson(prefabPath),
                    isVariant ? "true" : "false",
                    basePrefab,
                    hierarchyJson,
                    string.Join(",", overrides.ToArray())
                );
            }
            finally
            {
                if (root != null)
                    PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static string SerializeGameObject(GameObject go, int depth)
        {
            if (depth > MAX_DEPTH)
                return "{\"name\":\"[MAX_DEPTH]\",\"activeSelf\":false,\"components\":[],\"children\":[]}";

            // Nested prefab detection
            bool isNested = depth > 0 && PrefabUtility.IsAnyPrefabInstanceRoot(go);
            string nestedSource = "null";
            if (isNested)
            {
                string srcPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                if (!string.IsNullOrEmpty(srcPath))
                    nestedSource = "\"" + EscapeJson(srcPath) + "\"";
            }

            // Components
            var components = go.GetComponents<Component>();
            var compEntries = new List<string>();
            for (int idx = 0; idx < components.Length; idx++)
            {
                var comp = components[idx];
                if (comp == null)
                {
                    compEntries.Add("{\"type\":\"MissingScript\",\"error\":\"Script reference is missing\"}");
                    continue;
                }

                var sb = new System.Text.StringBuilder();
                sb.Append("{\"type\":\"").Append(EscapeJson(comp.GetType().FullName)).Append("\"");

                if (comp is Behaviour b)
                    sb.Append(",\"enabled\":").Append(b.enabled ? "true" : "false");

                sb.Append(",\"properties\":[");
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
                            if (!first) sb.Append(",");
                            sb.Append(propJson);
                            first = false;
                        }
                    } while (iter.NextVisible(false));
                }
                sb.Append("]}");
                compEntries.Add(sb.ToString());
            }

            // Children
            var childEntries = new List<string>();
            foreach (Transform child in go.transform)
                childEntries.Add(SerializeGameObject(child.gameObject, depth + 1));

            return string.Format(
                "{{\"name\":\"{0}\",\"activeSelf\":{1},\"isNestedPrefabInstance\":{2},\"nestedPrefabSource\":{3},\"components\":[{4}],\"children\":[{5}]}}",
                EscapeJson(go.name),
                go.activeSelf ? "true" : "false",
                isNested ? "true" : "false",
                nestedSource,
                string.Join(",", compEntries.ToArray()),
                string.Join(",", childEntries.ToArray())
            );
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
                        value = prop.intValue.ToString(); break;
                    case SerializedPropertyType.Boolean:
                        value = prop.boolValue ? "true" : "false"; break;
                    case SerializedPropertyType.Float:
                        value = prop.floatValue.ToString(System.Globalization.CultureInfo.InvariantCulture); break;
                    case SerializedPropertyType.String:
                        value = "\"" + EscapeJson(prop.stringValue) + "\""; break;
                    case SerializedPropertyType.Color:
                        var c = prop.colorValue;
                        value = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "{{\"r\":{0},\"g\":{1},\"b\":{2},\"a\":{3}}}", c.r, c.g, c.b, c.a); break;
                    case SerializedPropertyType.ObjectReference:
                        if (prop.objectReferenceValue != null)
                        {
                            var obj = prop.objectReferenceValue;
                            value = string.Format("{{\"instanceID\":{0},\"name\":\"{1}\",\"type\":\"{2}\"}}",
                                obj.GetInstanceID(), EscapeJson(obj.name), EscapeJson(obj.GetType().Name));
                        }
                        else value = "null";
                        break;
                    case SerializedPropertyType.Enum:
                        value = prop.enumValueIndex.ToString(); break;
                    case SerializedPropertyType.Vector2:
                        var v2 = prop.vector2Value;
                        value = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "{{\"x\":{0},\"y\":{1}}}", v2.x, v2.y); break;
                    case SerializedPropertyType.Vector3:
                        var v3 = prop.vector3Value;
                        value = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "{{\"x\":{0},\"y\":{1},\"z\":{2}}}", v3.x, v3.y, v3.z); break;
                    case SerializedPropertyType.Vector4:
                        var v4 = prop.vector4Value;
                        value = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "{{\"x\":{0},\"y\":{1},\"z\":{2},\"w\":{3}}}", v4.x, v4.y, v4.z, v4.w); break;
                    case SerializedPropertyType.Rect:
                        var rect = prop.rectValue;
                        value = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "{{\"x\":{0},\"y\":{1},\"width\":{2},\"height\":{3}}}",
                            rect.x, rect.y, rect.width, rect.height); break;
                    case SerializedPropertyType.Quaternion:
                        var q = prop.quaternionValue;
                        value = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "{{\"x\":{0},\"y\":{1},\"z\":{2},\"w\":{3}}}", q.x, q.y, q.z, q.w); break;
                    case SerializedPropertyType.LayerMask:
                        value = prop.intValue.ToString(); break;
                    case SerializedPropertyType.ArraySize:
                        value = prop.intValue.ToString(); typeName = "ArraySize"; break;
                    case SerializedPropertyType.AnimationCurve:
                        value = "{\"keys\":" + (prop.animationCurveValue?.length ?? 0) + "}"; break;
                    case SerializedPropertyType.Bounds:
                        var b2 = prop.boundsValue;
                        value = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "{{\"center\":{{\"x\":{0},\"y\":{1},\"z\":{2}}},\"extents\":{{\"x\":{3},\"y\":{4},\"z\":{5}}}}}",
                            b2.center.x, b2.center.y, b2.center.z, b2.extents.x, b2.extents.y, b2.extents.z); break;
                    case SerializedPropertyType.Generic:
                        return null;
                    default:
                        value = "\"" + EscapeJson(prop.propertyType.ToString()) + "\""; break;
                }
            }
            catch { value = "\"<error>\""; }

            return string.Format("{{\"name\":\"{0}\",\"type\":\"{1}\",\"value\":{2}}}",
                EscapeJson(prop.name), EscapeJson(typeName), value);
        }

        private static string EscapeJson(string str)
        {
            if (str == null) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
