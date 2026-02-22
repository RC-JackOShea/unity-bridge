using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace UnityBridge
{
    /// <summary>
    /// Scans all C# files in Assets/ and returns structured analysis:
    /// script classification, inheritance, dependencies, events, API usage, conventions.
    /// </summary>
    public static class CodebaseAnalyzer
    {
        private static readonly string[] LifecycleMethods = {
            "Awake", "Start", "Update", "FixedUpdate", "LateUpdate",
            "OnEnable", "OnDisable", "OnDestroy", "OnGUI", "OnValidate",
            "OnApplicationQuit", "OnApplicationPause", "OnCollisionEnter",
            "OnCollisionExit", "OnTriggerEnter", "OnTriggerExit", "Reset"
        };

        public static string AnalyzeProject()
        {
            string assetsPath = Application.dataPath;
            if (!Directory.Exists(assetsPath))
                return "{\"error\":\"Assets directory not found\"}";

            var files = Directory.GetFiles(assetsPath, "*.cs", SearchOption.AllDirectories);
            var scripts = new List<string>();
            var typeCounts = new Dictionary<string, int>();
            var allNamespaces = new HashSet<string>();
            int totalLines = 0;
            bool usesNewInput = false, usesLegacyInput = false;
            bool usesUGUI = false, usesUIToolkit = false;
            bool usesURP = false, usesHDRP = false;
            int camelFields = 0, underscoreFields = 0, mFields = 0;
            bool usesNamespaces = false, usesRegions = false;

            foreach (var file in files)
            {
                string relativePath = "Assets" + file.Substring(assetsPath.Length).Replace("\\", "/");
                try
                {
                    string source = File.ReadAllText(file);
                    var info = AnalyzeScript(source, relativePath);
                    scripts.Add(info.json);

                    string cls = info.classification;
                    if (!typeCounts.ContainsKey(cls)) typeCounts[cls] = 0;
                    typeCounts[cls]++;

                    if (!string.IsNullOrEmpty(info.ns)) { allNamespaces.Add(info.ns); usesNamespaces = true; }
                    totalLines += info.lineCount;

                    if (info.usesNewInput) usesNewInput = true;
                    if (info.usesLegacyInput) usesLegacyInput = true;
                    if (info.usesUGUI) usesUGUI = true;
                    if (info.usesUIToolkit) usesUIToolkit = true;
                    if (info.usesURP) usesURP = true;
                    if (info.usesHDRP) usesHDRP = true;
                    if (info.usesRegions) usesRegions = true;

                    camelFields += info.camelFields;
                    underscoreFields += info.underscoreFields;
                    mFields += info.mFields;
                }
                catch { }
            }

            string inputSystem = usesNewInput && usesLegacyInput ? "Both" : usesNewInput ? "New" : usesLegacyInput ? "Legacy" : "None";
            string uiFramework = usesUGUI && usesUIToolkit ? "Both" : usesUGUI ? "UGUI" : usesUIToolkit ? "UIToolkit" : "None";
            string renderPipeline = usesURP ? "URP" : usesHDRP ? "HDRP" : "BuiltIn";
            string fieldNaming = underscoreFields > camelFields && underscoreFields > mFields ? "_camelCase" :
                                 mFields > camelFields ? "m_camelCase" : "camelCase";

            var byType = new List<string>();
            foreach (var kv in typeCounts)
                byType.Add(string.Format("\"{0}\":{1}", Esc(kv.Key), kv.Value));

            var nsArr = new List<string>();
            foreach (var ns in allNamespaces) nsArr.Add("\"" + Esc(ns) + "\"");

            int avg = files.Length > 0 ? totalLines / files.Length : 0;

            return string.Format(
                "{{\"success\":true,\"summary\":{{\"totalScripts\":{0},\"byType\":{{{1}}},\"namespaces\":[{2}],\"inputSystem\":\"{3}\",\"uiFramework\":\"{4}\",\"renderPipeline\":\"{5}\"}},\"scripts\":[{6}],\"conventions\":{{\"fieldNaming\":\"{7}\",\"methodNaming\":\"PascalCase\",\"usesRegions\":{8},\"usesNamespaces\":{9},\"averageFileLength\":{10}}}}}",
                files.Length, string.Join(",", byType.ToArray()),
                string.Join(",", nsArr.ToArray()),
                inputSystem, uiFramework, renderPipeline,
                string.Join(",", scripts.ToArray()),
                fieldNaming,
                usesRegions ? "true" : "false",
                usesNamespaces ? "true" : "false",
                avg);
        }

        public static string GetScriptDetails(string scriptPath)
        {
            if (string.IsNullOrEmpty(scriptPath))
                return "{\"error\":\"scriptPath is required\"}";

            string fullPath;
            if (scriptPath.StartsWith("Assets/"))
                fullPath = Path.Combine(Application.dataPath.Replace("/Assets", ""), scriptPath.Replace("/", "\\"));
            else
                fullPath = scriptPath;

            if (!File.Exists(fullPath))
                return "{\"error\":\"File not found: " + Esc(scriptPath) + "\"}";

            try
            {
                string source = File.ReadAllText(fullPath);
                string relativePath = scriptPath.StartsWith("Assets/") ? scriptPath :
                    "Assets" + fullPath.Substring(Application.dataPath.Length).Replace("\\", "/");
                var info = AnalyzeScript(source, relativePath);
                return "{\"success\":true," + info.json.Substring(1); // Replace opening { with {success:true,
            }
            catch (Exception e)
            {
                return "{\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        public static string DetectInputSystem()
        {
            string assetsPath = Application.dataPath;
            var files = Directory.GetFiles(assetsPath, "*.cs", SearchOption.AllDirectories);
            int legacyCount = 0, newCount = 0;
            var legacyScripts = new List<string>();
            var newScripts = new List<string>();

            foreach (var file in files)
            {
                string source = File.ReadAllText(file);
                string relativePath = "Assets" + file.Substring(assetsPath.Length).Replace("\\", "/");
                bool hasLegacy = Regex.IsMatch(source, @"Input\.Get(Key|Axis|Button|Mouse)");
                bool hasNew = source.Contains("UnityEngine.InputSystem") || Regex.IsMatch(source, @"InputAction\b");

                if (hasLegacy) { legacyCount++; legacyScripts.Add("\"" + Esc(relativePath) + "\""); }
                if (hasNew) { newCount++; newScripts.Add("\"" + Esc(relativePath) + "\""); }
            }

            string mode;
#if ENABLE_INPUT_SYSTEM && ENABLE_LEGACY_INPUT_MANAGER
            mode = "Both";
#elif ENABLE_INPUT_SYSTEM
            mode = "New";
#elif ENABLE_LEGACY_INPUT_MANAGER
            mode = "Legacy";
#else
            mode = "Unknown";
#endif
            string recommendation = newCount > legacyCount && legacyCount > 0 ?
                "Project primarily uses New Input System with " + legacyCount + " legacy references that should be migrated" :
                legacyCount > newCount && newCount > 0 ?
                "Project primarily uses Legacy Input with " + newCount + " new Input System references" :
                newCount > 0 ? "Project uses New Input System" :
                legacyCount > 0 ? "Project uses Legacy Input" : "No input system usage detected";

            return string.Format(
                "{{\"success\":true,\"playerSettingsMode\":\"{0}\",\"codeUsage\":{{\"legacyInputUsage\":{1},\"newInputSystemUsage\":{2},\"scriptsUsingLegacy\":[{3}],\"scriptsUsingNew\":[{4}]}},\"recommendation\":\"{5}\"}}",
                mode, legacyCount, newCount,
                string.Join(",", legacyScripts.ToArray()), string.Join(",", newScripts.ToArray()),
                Esc(recommendation));
        }

        public static string MapDependencies()
        {
            string assetsPath = Application.dataPath;
            var files = Directory.GetFiles(assetsPath, "*.cs", SearchOption.AllDirectories);

            var nodes = new List<string>();
            var edges = new List<string>();
            var classNames = new HashSet<string>();

            // First pass: collect all class names
            foreach (var file in files)
            {
                string source = File.ReadAllText(file);
                var classMatch = Regex.Match(source, @"class\s+(\w+)");
                if (classMatch.Success) classNames.Add(classMatch.Groups[1].Value);
            }

            // Second pass: build graph
            foreach (var file in files)
            {
                string source = File.ReadAllText(file);
                string relativePath = "Assets" + file.Substring(assetsPath.Length).Replace("\\", "/");
                var classMatch = Regex.Match(source, @"class\s+(\w+)");
                if (!classMatch.Success) continue;
                string className = classMatch.Groups[1].Value;

                nodes.Add(string.Format("{{\"script\":\"{0}\",\"path\":\"{1}\"}}", Esc(className), Esc(relativePath)));

                // GetComponent<T>
                foreach (Match m in Regex.Matches(source, @"GetComponent<(\w+)>"))
                {
                    string target = m.Groups[1].Value;
                    edges.Add(string.Format("{{\"from\":\"{0}\",\"to\":\"{1}\",\"type\":\"GetComponent\"}}", Esc(className), Esc(target)));
                }

                // RequireComponent
                foreach (Match m in Regex.Matches(source, @"\[RequireComponent\(typeof\((\w+)\)\)\]"))
                {
                    string target = m.Groups[1].Value;
                    edges.Add(string.Format("{{\"from\":\"{0}\",\"to\":\"{1}\",\"type\":\"RequireComponent\"}}", Esc(className), Esc(target)));
                }

                // SerializeField with project types
                foreach (Match m in Regex.Matches(source, @"\[SerializeField\]\s*(?:private|protected)?\s*(\w+)\s+(\w+)"))
                {
                    string fieldType = m.Groups[1].Value;
                    if (classNames.Contains(fieldType))
                        edges.Add(string.Format("{{\"from\":\"{0}\",\"to\":\"{1}\",\"type\":\"SerializeField\"}}", Esc(className), Esc(fieldType)));
                }
            }

            return string.Format("{{\"success\":true,\"nodes\":[{0}],\"edges\":[{1}]}}",
                string.Join(",", nodes.ToArray()), string.Join(",", edges.ToArray()));
        }

        private struct ScriptInfo
        {
            public string json, classification, ns;
            public int lineCount, camelFields, underscoreFields, mFields;
            public bool usesNewInput, usesLegacyInput, usesUGUI, usesUIToolkit, usesURP, usesHDRP, usesRegions;
        }

        private static ScriptInfo AnalyzeScript(string source, string path)
        {
            var info = new ScriptInfo();
            info.lineCount = source.Split('\n').Length;

            // Namespace
            var nsMatch = Regex.Match(source, @"namespace\s+([\w.]+)");
            info.ns = nsMatch.Success ? nsMatch.Groups[1].Value : "";

            // Using directives
            var usings = new List<string>();
            foreach (Match m in Regex.Matches(source, @"using\s+([\w.]+)\s*;"))
                usings.Add("\"" + Esc(m.Groups[1].Value) + "\"");

            // Class declaration
            string className = "";
            string baseClass = "";
            var interfaces = new List<string>();
            var classMatch = Regex.Match(source, @"(?:public|internal|private|protected)?\s*(?:abstract\s+|static\s+|sealed\s+|partial\s+)*(?:class|struct|interface|enum)\s+(\w+)(?:\s*<[^>]+>)?\s*(?::\s*([^{]+))?");
            if (classMatch.Success)
            {
                className = classMatch.Groups[1].Value;
                if (classMatch.Groups[2].Success)
                {
                    var bases = classMatch.Groups[2].Value.Split(',');
                    for (int i = 0; i < bases.Length; i++)
                    {
                        string b = bases[i].Trim().Split('<')[0].Trim();
                        if (string.IsNullOrEmpty(b)) continue;
                        if (i == 0 && !b.StartsWith("I") || b == "MonoBehaviour" || b == "ScriptableObject" || b == "Editor" || b == "EditorWindow")
                            baseClass = b;
                        else
                            interfaces.Add("\"" + Esc(b) + "\"");
                    }
                    if (string.IsNullOrEmpty(baseClass) && bases.Length > 0)
                    {
                        string first = bases[0].Trim().Split('<')[0].Trim();
                        if (!string.IsNullOrEmpty(first) && !first.StartsWith("I"))
                            baseClass = first;
                    }
                }
            }

            // Classification
            bool isEditor = path.Contains("/Editor/") || baseClass == "Editor" || baseClass == "EditorWindow";
            bool isInterface = Regex.IsMatch(source, @"\binterface\s+" + Regex.Escape(className));
            bool isEnum = Regex.IsMatch(source, @"\benum\s+" + Regex.Escape(className));
            bool isStruct = Regex.IsMatch(source, @"\bstruct\s+" + Regex.Escape(className));
            bool isAbstract = Regex.IsMatch(source, @"\babstract\s+class\s+" + Regex.Escape(className));
            bool isStatic = Regex.IsMatch(source, @"\bstatic\s+class\s+" + Regex.Escape(className));

            if (isInterface) info.classification = "Interface";
            else if (isEnum) info.classification = "Enum";
            else if (isStruct) info.classification = "Struct";
            else if (isEditor) info.classification = "EditorScript";
            else if (baseClass == "MonoBehaviour") info.classification = "MonoBehaviour";
            else if (baseClass == "ScriptableObject") info.classification = "ScriptableObject";
            else if (isAbstract) info.classification = "AbstractClass";
            else if (isStatic) info.classification = "StaticClass";
            else info.classification = "PureClass";

            // Lifecycle methods
            var lifecycle = new List<string>();
            foreach (var lm in LifecycleMethods)
            {
                if (Regex.IsMatch(source, @"(?:void|IEnumerator)\s+" + lm + @"\s*\("))
                    lifecycle.Add("\"" + lm + "\"");
            }

            // SerializeField
            var serializedFields = new List<string>();
            foreach (Match m in Regex.Matches(source, @"\[SerializeField\]\s*(?:private|protected)?\s*(\w+(?:<[^>]*>)?)\s+(\w+)"))
            {
                serializedFields.Add(string.Format("{{\"name\":\"{0}\",\"type\":\"{1}\"}}", Esc(m.Groups[2].Value), Esc(m.Groups[1].Value)));
            }
            // Public fields (also serialized)
            foreach (Match m in Regex.Matches(source, @"public\s+(\w+(?:<[^>]*>)?)\s+(\w+)\s*[;=]"))
            {
                string fieldName = m.Groups[2].Value;
                string fieldType = m.Groups[1].Value;
                if (fieldType == "class" || fieldType == "void" || fieldType == "static" || fieldType == "override") continue;
                serializedFields.Add(string.Format("{{\"name\":\"{0}\",\"type\":\"{1}\"}}", Esc(fieldName), Esc(fieldType)));
            }

            // Field naming convention
            foreach (Match m in Regex.Matches(source, @"(?:private|protected)\s+\w+\s+(\w+)\s*[;=]"))
            {
                string fname = m.Groups[1].Value;
                if (fname.StartsWith("_")) info.underscoreFields++;
                else if (fname.StartsWith("m_")) info.mFields++;
                else info.camelFields++;
            }

            // Dependencies
            var getCompCalls = new List<string>();
            foreach (Match m in Regex.Matches(source, @"GetComponent<(\w+)>"))
                getCompCalls.Add("\"" + Esc(m.Groups[1].Value) + "\"");

            var reqComp = new List<string>();
            foreach (Match m in Regex.Matches(source, @"\[RequireComponent\(typeof\((\w+)\)\)\]"))
                reqComp.Add("\"" + Esc(m.Groups[1].Value) + "\"");

            // Events
            var unityEvents = new List<string>();
            foreach (Match m in Regex.Matches(source, @"(?:public|private|protected)\s+UnityEvent(?:<[^>]*>)?\s+(\w+)"))
                unityEvents.Add("\"" + Esc(m.Groups[1].Value) + "\"");

            var csharpEvents = new List<string>();
            foreach (Match m in Regex.Matches(source, @"event\s+\w+(?:<[^>]*>)?\s+(\w+)"))
                csharpEvents.Add("\"" + Esc(m.Groups[1].Value) + "\"");

            var sendMessages = new List<string>();
            foreach (Match m in Regex.Matches(source, @"SendMessage\s*\(\s*""(\w+)"""))
                sendMessages.Add("\"" + Esc(m.Groups[1].Value) + "\"");

            // API usage
            info.usesLegacyInput = Regex.IsMatch(source, @"Input\.Get(Key|Axis|Button|Mouse)");
            info.usesNewInput = source.Contains("UnityEngine.InputSystem") || Regex.IsMatch(source, @"InputAction\b");
            info.usesUGUI = source.Contains("UnityEngine.UI");
            info.usesUIToolkit = source.Contains("UnityEngine.UIElements");
            info.usesURP = source.Contains("UnityEngine.Rendering.Universal");
            info.usesHDRP = source.Contains("UnityEngine.Rendering.HighDefinition");
            info.usesRegions = source.Contains("#region");

            var sb = new StringBuilder();
            sb.Append("{\"path\":\"").Append(Esc(path)).Append("\"");
            sb.Append(",\"className\":\"").Append(Esc(className)).Append("\"");
            sb.Append(",\"namespace\":\"").Append(Esc(info.ns)).Append("\"");
            sb.Append(",\"classification\":\"").Append(Esc(info.classification)).Append("\"");
            sb.Append(",\"baseClass\":\"").Append(Esc(baseClass)).Append("\"");
            sb.Append(",\"interfaces\":[").Append(string.Join(",", interfaces.ToArray())).Append("]");
            sb.Append(",\"serializedFields\":[").Append(string.Join(",", serializedFields.ToArray())).Append("]");
            sb.Append(",\"lifecycleMethods\":[").Append(string.Join(",", lifecycle.ToArray())).Append("]");
            sb.Append(",\"dependencies\":{");
            sb.Append("\"requireComponent\":[").Append(string.Join(",", reqComp.ToArray())).Append("]");
            sb.Append(",\"getComponent\":[").Append(string.Join(",", getCompCalls.ToArray())).Append("]");
            sb.Append("}");
            sb.Append(",\"events\":{");
            sb.Append("\"unityEvents\":[").Append(string.Join(",", unityEvents.ToArray())).Append("]");
            sb.Append(",\"csharpEvents\":[").Append(string.Join(",", csharpEvents.ToArray())).Append("]");
            sb.Append(",\"sendMessage\":[").Append(string.Join(",", sendMessages.ToArray())).Append("]");
            sb.Append("}");
            sb.Append(",\"usingDirectives\":[").Append(string.Join(",", usings.ToArray())).Append("]");
            sb.Append(",\"lineCount\":").Append(info.lineCount);
            sb.Append("}");

            info.json = sb.ToString();
            return info;
        }

        private static string Esc(string str)
        {
            if (str == null) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
