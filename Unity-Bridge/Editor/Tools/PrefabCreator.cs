using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UnityBridge
{
    /// <summary>
    /// Creates and modifies prefabs from JSON specifications.
    /// First WRITE tool — enables programmatic asset creation.
    /// </summary>
    public static class PrefabCreator
    {
        public static string CreatePrefab(string jsonSpec)
        {
            if (string.IsNullOrEmpty(jsonSpec))
                return "{\"error\":\"jsonSpec is required\"}";

            SimpleJson.JsonNode spec;
            try { spec = SimpleJson.Parse(jsonSpec); }
            catch (Exception e) { return "{\"error\":\"JSON parse error: " + Esc(e.Message) + "\"}"; }

            string outputPath = spec.GetString("outputPath");
            if (string.IsNullOrEmpty(outputPath))
                return "{\"error\":\"outputPath is required\"}";
            if (!outputPath.StartsWith("Assets/"))
                return "{\"error\":\"outputPath must start with Assets/\"}";
            if (!outputPath.EndsWith(".prefab"))
                return "{\"error\":\"outputPath must end with .prefab\"}";

            var rootSpec = spec.Get("root");
            if (rootSpec == null)
                return "{\"error\":\"root object is required\"}";

            // Ensure directory exists
            EnsureDirectoryExists(outputPath);

            GameObject tempRoot = null;
            int goCount = 0;
            int compCount = 0;
            try
            {
                tempRoot = BuildGameObject(rootSpec, ref goCount, ref compCount, out string buildError);
                if (buildError != null)
                    return "{\"error\":\"" + Esc(buildError) + "\"}";

                bool success;
                PrefabUtility.SaveAsPrefabAsset(tempRoot, outputPath, out success);
                if (!success)
                    return "{\"error\":\"PrefabUtility.SaveAsPrefabAsset failed for " + Esc(outputPath) + "\"}";

                AssetDatabase.Refresh();

                return string.Format(
                    "{{\"success\":true,\"prefabPath\":\"{0}\",\"gameObjectCount\":{1},\"componentCount\":{2}}}",
                    Esc(outputPath), goCount, compCount);
            }
            catch (Exception e)
            {
                return "{\"error\":\"" + Esc(e.Message) + "\"}";
            }
            finally
            {
                if (tempRoot != null)
                    UnityEngine.Object.DestroyImmediate(tempRoot);
            }
        }

        public static string ModifyPrefab(string prefabPath, string jsonPatch)
        {
            if (string.IsNullOrEmpty(prefabPath))
                return "{\"error\":\"prefabPath is required\"}";
            if (string.IsNullOrEmpty(jsonPatch))
                return "{\"error\":\"jsonPatch is required\"}";

            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabAsset == null)
                return "{\"error\":\"Prefab not found: " + Esc(prefabPath) + "\"}";

            SimpleJson.JsonNode patch;
            try { patch = SimpleJson.Parse(jsonPatch); }
            catch (Exception e) { return "{\"error\":\"JSON parse error: " + Esc(e.Message) + "\"}"; }

            var operations = patch.GetArray("operations");
            if (operations == null || operations.Count == 0)
                return "{\"error\":\"No operations specified\"}";

            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(prefabPath);
                if (root == null)
                    return "{\"error\":\"Failed to load prefab contents\"}";

                int opsApplied = 0;
                foreach (var op in operations)
                {
                    string opType = op.GetString("op");
                    string result = ApplyOperation(root, op, opType);
                    if (result != null)
                        return "{\"error\":\"Operation " + opsApplied + " (" + Esc(opType) + "): " + Esc(result) + "\"}";
                    opsApplied++;
                }

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                AssetDatabase.Refresh();

                return string.Format(
                    "{{\"success\":true,\"prefabPath\":\"{0}\",\"operationsApplied\":{1}}}",
                    Esc(prefabPath), opsApplied);
            }
            catch (Exception e)
            {
                return "{\"error\":\"" + Esc(e.Message) + "\"}";
            }
            finally
            {
                if (root != null)
                    PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static string ApplyOperation(GameObject root, SimpleJson.JsonNode op, string opType)
        {
            switch (opType)
            {
                case "addComponent":
                {
                    string goPath = op.GetString("gameObjectPath") ?? "";
                    string compType = op.GetString("componentType");
                    if (string.IsNullOrEmpty(compType)) return "componentType is required";
                    var target = FindChild(root, goPath);
                    if (target == null) return "GameObject not found: " + goPath;
                    var type = ResolveComponentType(compType);
                    if (type == null) return "Component type not found: " + compType;
                    var comp = target.AddComponent(type);
                    if (comp == null) return "Failed to add component: " + compType;
                    var props = op.Get("properties");
                    if (props != null) SetProperties(comp, props);
                    return null;
                }
                case "removeComponent":
                {
                    string goPath = op.GetString("gameObjectPath") ?? "";
                    string compType = op.GetString("componentType");
                    if (string.IsNullOrEmpty(compType)) return "componentType is required";
                    var target = FindChild(root, goPath);
                    if (target == null) return "GameObject not found: " + goPath;
                    var type = ResolveComponentType(compType);
                    if (type == null) return "Component type not found: " + compType;
                    int idx = op.GetInt("componentIndex", 0);
                    var comps = target.GetComponents(type);
                    if (idx >= comps.Length) return "Component index out of range";
                    UnityEngine.Object.DestroyImmediate(comps[idx]);
                    return null;
                }
                case "setProperty":
                {
                    string goPath = op.GetString("gameObjectPath") ?? "";
                    string compType = op.GetString("componentType");
                    string propName = op.GetString("propertyName");
                    var propValue = op.Get("propertyValue");
                    if (string.IsNullOrEmpty(compType)) return "componentType is required";
                    if (string.IsNullOrEmpty(propName)) return "propertyName is required";
                    var target = FindChild(root, goPath);
                    if (target == null) return "GameObject not found: " + goPath;
                    var type = ResolveComponentType(compType);
                    if (type == null) return "Component type not found: " + compType;
                    int idx = op.GetInt("componentIndex", 0);
                    var comps = target.GetComponents(type);
                    if (idx >= comps.Length) return "Component index out of range";
                    SetSingleProperty(comps[idx], propName, propValue);
                    return null;
                }
                case "addChild":
                {
                    string parentPath = op.GetString("parentPath") ?? "";
                    var childSpec = op.Get("child");
                    if (childSpec == null) return "child spec is required";
                    var parent = FindChild(root, parentPath);
                    if (parent == null) return "Parent not found: " + parentPath;
                    int gc = 0, cc = 0;
                    var child = BuildGameObject(childSpec, ref gc, ref cc, out string err);
                    if (err != null) { if (child != null) UnityEngine.Object.DestroyImmediate(child); return err; }
                    child.transform.SetParent(parent.transform, false);
                    return null;
                }
                case "removeChild":
                {
                    string goPath = op.GetString("gameObjectPath");
                    if (string.IsNullOrEmpty(goPath)) return "gameObjectPath is required";
                    var target = FindChild(root, goPath);
                    if (target == null) return "GameObject not found: " + goPath;
                    UnityEngine.Object.DestroyImmediate(target);
                    return null;
                }
                default:
                    return "Unknown operation: " + opType;
            }
        }

        private static GameObject FindChild(GameObject root, string path)
        {
            if (string.IsNullOrEmpty(path)) return root;
            var parts = path.Split('/');
            Transform current = root.transform;
            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part)) continue;
                var child = current.Find(part);
                if (child == null) return null;
                current = child;
            }
            return current.gameObject;
        }

        private static GameObject BuildGameObject(SimpleJson.JsonNode spec, ref int goCount, ref int compCount, out string error)
        {
            error = null;
            string name = spec.GetString("name") ?? "GameObject";
            var go = new GameObject(name);
            goCount++;

            // Tag and layer
            string tag = spec.GetString("tag");
            if (!string.IsNullOrEmpty(tag))
            {
                try { go.tag = tag; } catch { }
            }
            int layer = spec.GetInt("layer", -1);
            if (layer >= 0) go.layer = layer;

            // Components
            var components = spec.GetArray("components");
            if (components != null)
            {
                foreach (var compSpec in components)
                {
                    string typeName = compSpec.GetString("type");
                    if (string.IsNullOrEmpty(typeName)) continue;

                    // Transform is always present, just configure it
                    if (typeName == "Transform")
                    {
                        var props = compSpec.Get("properties");
                        if (props != null) SetProperties(go.transform, props);
                        compCount++;
                        continue;
                    }

                    var type = ResolveComponentType(typeName);
                    if (type == null)
                    {
                        error = "Component type not found: " + typeName;
                        UnityEngine.Object.DestroyImmediate(go);
                        return null;
                    }

                    var comp = go.AddComponent(type);
                    if (comp == null)
                    {
                        error = "Failed to add component: " + typeName;
                        UnityEngine.Object.DestroyImmediate(go);
                        return null;
                    }
                    compCount++;

                    var compProps = compSpec.Get("properties");
                    if (compProps != null) SetProperties(comp, compProps);
                }
            }

            // Children
            var children = spec.GetArray("children");
            if (children != null)
            {
                foreach (var childSpec in children)
                {
                    var child = BuildGameObject(childSpec, ref goCount, ref compCount, out error);
                    if (error != null)
                    {
                        UnityEngine.Object.DestroyImmediate(go);
                        return null;
                    }
                    child.transform.SetParent(go.transform, false);
                }
            }

            return go;
        }

        private static Type ResolveComponentType(string name)
        {
            // Try UnityEngine first
            var type = Type.GetType("UnityEngine." + name + ", UnityEngine.CoreModule");
            if (type != null) return type;

            type = Type.GetType("UnityEngine." + name + ", UnityEngine.PhysicsModule");
            if (type != null) return type;

            type = Type.GetType("UnityEngine." + name + ", UnityEngine.AudioModule");
            if (type != null) return type;

            // Search all assemblies
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var t in asm.GetTypes())
                {
                    if (t.Name == name && typeof(Component).IsAssignableFrom(t))
                        return t;
                }
            }
            return null;
        }

        private static void SetProperties(Component comp, SimpleJson.JsonNode props)
        {
            if (props == null || props.obj == null) return;
            var so = new SerializedObject(comp);
            foreach (var kv in props.obj)
            {
                SetSingleProperty(so, kv.Key, kv.Value);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetSingleProperty(Component comp, string propName, SimpleJson.JsonNode value)
        {
            var so = new SerializedObject(comp);
            SetSingleProperty(so, propName, value);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetSingleProperty(SerializedObject so, string propName, SimpleJson.JsonNode value)
        {
            var prop = so.FindProperty(propName);
            if (prop == null) return;
            SetPropertyValue(prop, value);
        }

        private static void SetPropertyValue(SerializedProperty prop, SimpleJson.JsonNode value)
        {
            if (value == null) return;

            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    prop.intValue = value.AsInt();
                    break;
                case SerializedPropertyType.Boolean:
                    prop.boolValue = value.AsBool();
                    break;
                case SerializedPropertyType.Float:
                    prop.floatValue = value.AsFloat();
                    break;
                case SerializedPropertyType.String:
                    prop.stringValue = value.AsString();
                    break;
                case SerializedPropertyType.Color:
                    if (value.obj != null)
                    {
                        prop.colorValue = new Color(
                            value.GetFloat("r", 0), value.GetFloat("g", 0),
                            value.GetFloat("b", 0), value.GetFloat("a", 1));
                    }
                    break;
                case SerializedPropertyType.Enum:
                    prop.enumValueIndex = value.AsInt();
                    break;
                case SerializedPropertyType.Vector2:
                    if (value.obj != null)
                        prop.vector2Value = new Vector2(value.GetFloat("x", 0), value.GetFloat("y", 0));
                    break;
                case SerializedPropertyType.Vector3:
                    if (value.obj != null)
                        prop.vector3Value = new Vector3(value.GetFloat("x", 0), value.GetFloat("y", 0), value.GetFloat("z", 0));
                    break;
                case SerializedPropertyType.Vector4:
                    if (value.obj != null)
                        prop.vector4Value = new Vector4(value.GetFloat("x", 0), value.GetFloat("y", 0), value.GetFloat("z", 0), value.GetFloat("w", 0));
                    break;
                case SerializedPropertyType.Rect:
                    if (value.obj != null)
                        prop.rectValue = new Rect(value.GetFloat("x", 0), value.GetFloat("y", 0), value.GetFloat("width", 0), value.GetFloat("height", 0));
                    break;
                case SerializedPropertyType.Quaternion:
                    if (value.obj != null)
                        prop.quaternionValue = new Quaternion(value.GetFloat("x", 0), value.GetFloat("y", 0), value.GetFloat("z", 0), value.GetFloat("w", 1));
                    break;
                case SerializedPropertyType.LayerMask:
                    prop.intValue = value.AsInt();
                    break;
            }
        }

        private static void EnsureDirectoryExists(string assetPath)
        {
            // assetPath: "Assets/Prefabs/Sub/MyPrefab.prefab"
            string dir = System.IO.Path.GetDirectoryName(assetPath);
            if (string.IsNullOrEmpty(dir)) return;
            dir = dir.Replace("\\", "/");

            if (AssetDatabase.IsValidFolder(dir)) return;

            var parts = dir.Split('/');
            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static string Esc(string str)
        {
            if (str == null) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }

    /// <summary>
    /// Minimal JSON parser for dynamic structures. Returns a tree of JsonNode.
    /// </summary>
    public static class SimpleJson
    {
        public class JsonNode
        {
            public string str;
            public double? num;
            public bool? boolean;
            public bool isNull;
            public List<JsonNode> arr;
            public Dictionary<string, JsonNode> obj;

            public string AsString() { return str ?? (num.HasValue ? num.Value.ToString(CultureInfo.InvariantCulture) : ""); }
            public int AsInt() { return num.HasValue ? (int)num.Value : (int.TryParse(str, out int v) ? v : 0); }
            public float AsFloat() { return num.HasValue ? (float)num.Value : (float.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0); }
            public bool AsBool() { return boolean ?? (str == "true") || (num.HasValue && num.Value != 0); }

            public JsonNode Get(string key) { return obj != null && obj.ContainsKey(key) ? obj[key] : null; }
            public string GetString(string key) { var n = Get(key); return n?.AsString(); }
            public int GetInt(string key, int def = 0) { var n = Get(key); return n != null ? n.AsInt() : def; }
            public float GetFloat(string key, float def = 0) { var n = Get(key); return n != null ? n.AsFloat() : def; }
            public List<JsonNode> GetArray(string key) { var n = Get(key); return n?.arr; }
        }

        public static JsonNode Parse(string json)
        {
            int pos = 0;
            return ParseValue(json, ref pos);
        }

        private static JsonNode ParseValue(string json, ref int pos)
        {
            SkipWhitespace(json, ref pos);
            if (pos >= json.Length) throw new Exception("Unexpected end of JSON");

            char c = json[pos];
            if (c == '{') return ParseObject(json, ref pos);
            if (c == '[') return ParseArray(json, ref pos);
            if (c == '"') return new JsonNode { str = ParseString(json, ref pos) };
            if (c == 't' || c == 'f') return ParseBool(json, ref pos);
            if (c == 'n') return ParseNull(json, ref pos);
            return ParseNumber(json, ref pos);
        }

        private static JsonNode ParseObject(string json, ref int pos)
        {
            pos++; // skip {
            var obj = new Dictionary<string, JsonNode>();
            SkipWhitespace(json, ref pos);
            if (pos < json.Length && json[pos] == '}') { pos++; return new JsonNode { obj = obj }; }

            while (pos < json.Length)
            {
                SkipWhitespace(json, ref pos);
                string key = ParseString(json, ref pos);
                SkipWhitespace(json, ref pos);
                if (pos < json.Length && json[pos] == ':') pos++;
                var value = ParseValue(json, ref pos);
                obj[key] = value;
                SkipWhitespace(json, ref pos);
                if (pos < json.Length && json[pos] == ',') { pos++; continue; }
                if (pos < json.Length && json[pos] == '}') { pos++; break; }
                throw new Exception("Expected ',' or '}' at position " + pos);
            }
            return new JsonNode { obj = obj };
        }

        private static JsonNode ParseArray(string json, ref int pos)
        {
            pos++; // skip [
            var arr = new List<JsonNode>();
            SkipWhitespace(json, ref pos);
            if (pos < json.Length && json[pos] == ']') { pos++; return new JsonNode { arr = arr }; }

            while (pos < json.Length)
            {
                arr.Add(ParseValue(json, ref pos));
                SkipWhitespace(json, ref pos);
                if (pos < json.Length && json[pos] == ',') { pos++; continue; }
                if (pos < json.Length && json[pos] == ']') { pos++; break; }
                throw new Exception("Expected ',' or ']' at position " + pos);
            }
            return new JsonNode { arr = arr };
        }

        private static string ParseString(string json, ref int pos)
        {
            if (pos >= json.Length || json[pos] != '"') throw new Exception("Expected '\"' at position " + pos);
            pos++; // skip opening "
            var sb = new System.Text.StringBuilder();
            while (pos < json.Length)
            {
                char c = json[pos++];
                if (c == '"') return sb.ToString();
                if (c == '\\' && pos < json.Length)
                {
                    char esc = json[pos++];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (pos + 4 <= json.Length)
                            {
                                string hex = json.Substring(pos, 4);
                                sb.Append((char)Convert.ToInt32(hex, 16));
                                pos += 4;
                            }
                            break;
                        default: sb.Append(esc); break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            throw new Exception("Unterminated string");
        }

        private static JsonNode ParseNumber(string json, ref int pos)
        {
            int start = pos;
            if (pos < json.Length && json[pos] == '-') pos++;
            while (pos < json.Length && char.IsDigit(json[pos])) pos++;
            if (pos < json.Length && json[pos] == '.')
            {
                pos++;
                while (pos < json.Length && char.IsDigit(json[pos])) pos++;
            }
            if (pos < json.Length && (json[pos] == 'e' || json[pos] == 'E'))
            {
                pos++;
                if (pos < json.Length && (json[pos] == '+' || json[pos] == '-')) pos++;
                while (pos < json.Length && char.IsDigit(json[pos])) pos++;
            }
            string numStr = json.Substring(start, pos - start);
            double val = double.Parse(numStr, CultureInfo.InvariantCulture);
            return new JsonNode { num = val };
        }

        private static JsonNode ParseBool(string json, ref int pos)
        {
            if (json.Substring(pos).StartsWith("true")) { pos += 4; return new JsonNode { boolean = true }; }
            if (json.Substring(pos).StartsWith("false")) { pos += 5; return new JsonNode { boolean = false }; }
            throw new Exception("Invalid boolean at position " + pos);
        }

        private static JsonNode ParseNull(string json, ref int pos)
        {
            if (json.Substring(pos).StartsWith("null")) { pos += 4; return new JsonNode { isNull = true }; }
            throw new Exception("Invalid null at position " + pos);
        }

        private static void SkipWhitespace(string json, ref int pos)
        {
            while (pos < json.Length && char.IsWhiteSpace(json[pos])) pos++;
        }
    }
}
