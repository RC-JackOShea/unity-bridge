using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityBridge
{
    /// <summary>
    /// Reads live runtime game state during Play Mode.
    /// Uses reflection to query component values, GameObject states, and scene hierarchy.
    /// </summary>
    public static class GameStateObserver
    {
        public static string GetComponentValue(string goPath, string componentType, string propertyName)
        {
            if (!EditorApplication.isPlaying)
                return "{\"success\":false,\"error\":\"Game State Observer requires Play Mode\"}";

            if (string.IsNullOrEmpty(goPath))
                return "{\"success\":false,\"error\":\"goPath is required\"}";
            if (string.IsNullOrEmpty(componentType))
                return "{\"success\":false,\"error\":\"componentType is required\"}";
            if (string.IsNullOrEmpty(propertyName))
                return "{\"success\":false,\"error\":\"propertyName is required\"}";

            var go = FindGameObject(goPath);
            if (go == null)
                return "{\"success\":false,\"error\":\"GameObject not found: " + Esc(goPath) + "\"}";

            var type = ResolveType(componentType);
            if (type == null)
                return "{\"success\":false,\"error\":\"Component type not found: " + Esc(componentType) + "\"}";

            var comp = go.GetComponent(type);
            if (comp == null)
                return "{\"success\":false,\"error\":\"Component not found on " + Esc(go.name) + ": " + Esc(componentType) + "\"}";

            // Special case: enabled
            if (propertyName == "enabled" && comp is Behaviour b)
            {
                return string.Format(
                    "{{\"success\":true,\"gameObject\":\"{0}\",\"component\":\"{1}\",\"property\":\"enabled\",\"value\":{2},\"valueType\":\"Boolean\"}}",
                    Esc(go.name), Esc(componentType), b.enabled ? "true" : "false");
            }

            // Try property first, then field
            object value = null;
            string valueType = "Unknown";
            var prop = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null)
            {
                value = prop.GetValue(comp);
                valueType = prop.PropertyType.Name;
            }
            else
            {
                var field = type.GetField(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null)
                {
                    // Try without underscore prefix
                    field = type.GetField("_" + propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field == null)
                        field = type.GetField("m_" + propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                }
                if (field == null)
                    return "{\"success\":false,\"error\":\"Property/field not found: " + Esc(propertyName) + "\"}";
                value = field.GetValue(comp);
                valueType = field.FieldType.Name;
            }

            string serialized = SerializeValue(value);

            return string.Format(
                "{{\"success\":true,\"gameObject\":\"{0}\",\"component\":\"{1}\",\"property\":\"{2}\",\"value\":{3},\"valueType\":\"{4}\"}}",
                Esc(go.name), Esc(componentType), Esc(propertyName), serialized, Esc(valueType));
        }

        public static string GetGameObjectState(string goPath)
        {
            if (!EditorApplication.isPlaying)
                return "{\"success\":false,\"error\":\"Game State Observer requires Play Mode\"}";

            if (string.IsNullOrEmpty(goPath))
                return "{\"success\":false,\"error\":\"goPath is required\"}";

            var go = FindGameObject(goPath);
            if (go == null)
                return "{\"success\":false,\"error\":\"GameObject not found: " + Esc(goPath) + "\"}";

            var pos = go.transform.position;
            var rot = go.transform.eulerAngles;
            var scale = go.transform.localScale;

            var comps = go.GetComponents<Component>();
            var compEntries = new List<string>();
            foreach (var c in comps)
            {
                if (c == null) continue;
                bool enabled = c is Behaviour b ? b.enabled : true;
                compEntries.Add(string.Format("{{\"type\":\"{0}\",\"enabled\":{1}}}",
                    Esc(c.GetType().Name), enabled ? "true" : "false"));
            }

            return string.Format(CultureInfo.InvariantCulture,
                "{{\"success\":true,\"name\":\"{0}\",\"activeSelf\":{1},\"activeInHierarchy\":{2},\"position\":{{\"x\":{3},\"y\":{4},\"z\":{5}}},\"rotation\":{{\"x\":{6},\"y\":{7},\"z\":{8}}},\"scale\":{{\"x\":{9},\"y\":{10},\"z\":{11}}},\"components\":[{12}]}}",
                Esc(go.name), go.activeSelf ? "true" : "false", go.activeInHierarchy ? "true" : "false",
                pos.x, pos.y, pos.z, rot.x, rot.y, rot.z, scale.x, scale.y, scale.z,
                string.Join(",", compEntries.ToArray()));
        }

        public static string GetSceneState()
        {
            if (!EditorApplication.isPlaying)
                return "{\"success\":false,\"error\":\"Game State Observer requires Play Mode\"}";

            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            var entries = new List<string>();

            foreach (var go in roots)
            {
                var pos = go.transform.position;
                entries.Add(string.Format(CultureInfo.InvariantCulture,
                    "{{\"name\":\"{0}\",\"activeSelf\":{1},\"position\":{{\"x\":{2},\"y\":{3},\"z\":{4}}},\"childCount\":{5}}}",
                    Esc(go.name), go.activeSelf ? "true" : "false",
                    pos.x, pos.y, pos.z, go.transform.childCount));
            }

            return string.Format("{{\"success\":true,\"sceneName\":\"{0}\",\"rootObjects\":[{1}]}}",
                Esc(scene.name), string.Join(",", entries.ToArray()));
        }

        public static string WaitForCondition(string jsonCondition, float timeout)
        {
            if (!EditorApplication.isPlaying)
                return "{\"success\":false,\"error\":\"Game State Observer requires Play Mode\"}";

            if (string.IsNullOrEmpty(jsonCondition))
                return "{\"success\":false,\"error\":\"jsonCondition is required\"}";

            if (timeout <= 0) timeout = 5f;

            try
            {
                var cond = SimpleJson.Parse(jsonCondition);
                string condType = cond.GetString("type");

                double startTime = EditorApplication.timeSinceStartup;
                double endTime = startTime + timeout;
                float pollInterval = 0.1f;
                string lastState = "";

                while (EditorApplication.timeSinceStartup < endTime)
                {
                    if (!EditorApplication.isPlaying)
                        return "{\"success\":false,\"error\":\"Play Mode exited during wait\"}";

                    bool met = EvaluateCondition(cond, condType, out lastState);
                    if (met)
                    {
                        double elapsed = EditorApplication.timeSinceStartup - startTime;
                        return string.Format(CultureInfo.InvariantCulture,
                            "{{\"success\":true,\"conditionMet\":true,\"elapsedSeconds\":{0},\"timeout\":{1}}}",
                            elapsed, timeout);
                    }

                    System.Threading.Thread.Sleep((int)(pollInterval * 1000));
                }

                double totalElapsed = EditorApplication.timeSinceStartup - startTime;
                return string.Format(CultureInfo.InvariantCulture,
                    "{{\"success\":true,\"conditionMet\":false,\"elapsedSeconds\":{0},\"timeout\":{1},\"lastState\":\"{2}\"}}",
                    totalElapsed, timeout, Esc(lastState));
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        private static bool EvaluateCondition(SimpleJson.JsonNode cond, string condType, out string lastState)
        {
            lastState = "";
            switch (condType)
            {
                case "gameObjectActive":
                {
                    string path = cond.GetString("path");
                    bool expected = cond.Get("value")?.AsBool() ?? true;
                    var go = FindGameObject(path);
                    bool actual = go != null && go.activeSelf;
                    lastState = "GameObject '" + path + "' activeSelf was " + actual;
                    return actual == expected;
                }
                case "propertyEquals":
                {
                    string path = cond.GetString("path");
                    string comp = cond.GetString("component");
                    string prop = cond.GetString("property");
                    var expectedNode = cond.Get("value");
                    var go = FindGameObject(path);
                    if (go == null) { lastState = "GameObject '" + path + "' not found"; return false; }
                    var type = ResolveType(comp);
                    if (type == null) { lastState = "Component '" + comp + "' type not found"; return false; }
                    var component = go.GetComponent(type);
                    if (component == null) { lastState = "Component '" + comp + "' not on object"; return false; }
                    var propInfo = type.GetProperty(prop, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    object actual;
                    if (propInfo != null)
                        actual = propInfo.GetValue(component);
                    else
                    {
                        var fieldInfo = type.GetField(prop, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (fieldInfo == null) { lastState = "Property/field '" + prop + "' not found"; return false; }
                        actual = fieldInfo.GetValue(component);
                    }
                    lastState = prop + " = " + (actual?.ToString() ?? "null");
                    string expectedStr = expectedNode?.AsString() ?? "";
                    return actual?.ToString() == expectedStr || actual?.ToString()?.ToLower() == expectedStr.ToLower();
                }
                case "logContains":
                {
                    string text = cond.GetString("text");
                    // Check recent logs via bridge
                    lastState = "Checking logs for: " + text;
                    // We can't easily access the log buffer from here without hooking Application.logMessageReceived
                    // For now, return false and let the caller use check_log action instead
                    return false;
                }
                default:
                    lastState = "Unknown condition type: " + condType;
                    return false;
            }
        }

        private static GameObject FindGameObject(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            // Try direct find first (works for active root objects)
            var go = GameObject.Find(path);
            if (go != null) return go;

            // For hierarchical paths or inactive objects, walk from roots
            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();

            // Check if path contains /
            int slashIdx = path.IndexOf('/');
            string rootName = slashIdx > 0 ? path.Substring(0, slashIdx) : path;
            string childPath = slashIdx > 0 ? path.Substring(slashIdx + 1) : null;

            foreach (var root in roots)
            {
                if (root.name == rootName)
                {
                    if (childPath == null) return root;
                    var child = root.transform.Find(childPath);
                    if (child != null) return child.gameObject;
                }
            }
            return null;
        }

        private static Type ResolveType(string name)
        {
            // Try common Unity types first
            var type = Type.GetType("UnityEngine." + name + ", UnityEngine.CoreModule");
            if (type != null) return type;
            type = Type.GetType("UnityEngine." + name + ", UnityEngine.PhysicsModule");
            if (type != null) return type;

            // Search all assemblies
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var t in asm.GetTypes())
                {
                    if (t.Name == name) return t;
                }
            }
            return null;
        }

        private static string SerializeValue(object value)
        {
            if (value == null) return "null";

            if (value is bool b) return b ? "true" : "false";
            if (value is int i) return i.ToString();
            if (value is float f) return f.ToString(CultureInfo.InvariantCulture);
            if (value is double d) return d.ToString(CultureInfo.InvariantCulture);
            if (value is string s) return "\"" + Esc(s) + "\"";

            if (value is Vector2 v2)
                return string.Format(CultureInfo.InvariantCulture, "{{\"x\":{0},\"y\":{1}}}", v2.x, v2.y);
            if (value is Vector3 v3)
                return string.Format(CultureInfo.InvariantCulture, "{{\"x\":{0},\"y\":{1},\"z\":{2}}}", v3.x, v3.y, v3.z);
            if (value is Quaternion q)
                return string.Format(CultureInfo.InvariantCulture, "{{\"x\":{0},\"y\":{1},\"z\":{2},\"w\":{3}}}", q.x, q.y, q.z, q.w);
            if (value is Color c)
                return string.Format(CultureInfo.InvariantCulture, "{{\"r\":{0},\"g\":{1},\"b\":{2},\"a\":{3}}}", c.r, c.g, c.b, c.a);

            if (value is Enum) return "\"" + Esc(value.ToString()) + "\"";

            return "\"" + Esc(value.ToString()) + "\"";
        }

        private static string Esc(string str)
        {
            if (str == null) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
