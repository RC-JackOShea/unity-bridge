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
    /// Template-based C# code generator. Produces MonoBehaviours, Editor scripts, and
    /// test scripts that follow detected project conventions (naming, namespaces, brace style).
    /// Convention detection scans existing .cs files using regex patterns.
    /// </summary>
    public static class CodeGenerator
    {
        public static string DetectConventions()
        {
            try
            {
                string assetsPath = Application.dataPath;
                var csFiles = Directory.GetFiles(assetsPath, "*.cs", SearchOption.AllDirectories);

                int camelCase = 0, underscorePrefix = 0, mPrefix = 0, pascalCase = 0;
                int nextLineBrace = 0, sameLineBrace = 0;
                int systemFirst = 0, unityFirst = 0;
                bool usesNamespaces = false;
                int xmlComments = 0, inlineComments = 0;
                bool usesRegions = false;
                int totalLines = 0;
                int filesAnalyzed = 0;
                var namespaces = new List<string>();

                foreach (var file in csFiles)
                {
                    // Skip auto-generated and meta files
                    if (file.Contains("Temp") || file.Contains(".generated")) continue;

                    string content;
                    try { content = File.ReadAllText(file); }
                    catch { continue; }

                    string[] lines = content.Split('\n');
                    totalLines += lines.Length;
                    filesAnalyzed++;

                    // Detect field naming
                    foreach (Match m in Regex.Matches(content, @"private\s+\w+\s+(_\w+)\s*[;=]"))
                        underscorePrefix++;
                    foreach (Match m in Regex.Matches(content, @"private\s+\w+\s+(m_\w+)\s*[;=]"))
                        mPrefix++;
                    foreach (Match m in Regex.Matches(content, @"private\s+\w+\s+([a-z]\w+)\s*[;=]"))
                    {
                        string name = m.Groups[1].Value;
                        if (!name.StartsWith("_") && !name.StartsWith("m_"))
                            camelCase++;
                    }

                    // Detect namespace usage
                    foreach (Match m in Regex.Matches(content, @"namespace\s+([\w.]+)"))
                    {
                        usesNamespaces = true;
                        namespaces.Add(m.Groups[1].Value);
                    }

                    // Detect brace style
                    foreach (Match m in Regex.Matches(content, @"(class|void|public|private|protected)\s+\w+[^{]*\{\s*$", RegexOptions.Multiline))
                        sameLineBrace++;
                    foreach (Match m in Regex.Matches(content, @"(class|void|public|private|protected)\s+\w+[^{]*\s*$\s*\{", RegexOptions.Multiline))
                        nextLineBrace++;

                    // Detect using order
                    bool seenSystem = false, seenUnity = false;
                    foreach (var line in lines)
                    {
                        string trimmed = line.Trim();
                        if (trimmed.StartsWith("using System") && !seenUnity) { seenSystem = true; systemFirst++; }
                        if (trimmed.StartsWith("using UnityEngine") || trimmed.StartsWith("using UnityEditor"))
                        {
                            seenUnity = true;
                            if (!seenSystem) unityFirst++;
                        }
                        if (!trimmed.StartsWith("using ")) break;
                    }

                    // Detect comment style
                    if (content.Contains("/// <summary>")) xmlComments++;
                    if (Regex.IsMatch(content, @"^\s*//[^/]", RegexOptions.Multiline)) inlineComments++;

                    // Detect regions
                    if (content.Contains("#region")) usesRegions = true;
                }

                // Determine conventions
                string fieldNaming = "camelCase";
                string privateFieldPrefix = "";
                if (underscorePrefix > camelCase && underscorePrefix > mPrefix) { fieldNaming = "_camelCase"; privateFieldPrefix = "_"; }
                else if (mPrefix > camelCase && mPrefix > underscorePrefix) { fieldNaming = "m_camelCase"; privateFieldPrefix = "m_"; }

                string braceStyle = nextLineBrace > sameLineBrace ? "nextLine" : "sameLine";
                string usingOrder = systemFirst >= unityFirst ? "SystemFirst" : "UnityFirst";
                string commentStyle = xmlComments > inlineComments ? "xml" : "inline";

                string namespacePattern = "";
                if (namespaces.Count > 0)
                {
                    // Find common root
                    var roots = new Dictionary<string, int>();
                    foreach (var ns in namespaces)
                    {
                        string root = ns.Contains(".") ? ns.Substring(0, ns.IndexOf('.')) : ns;
                        if (!roots.ContainsKey(root)) roots[root] = 0;
                        roots[root]++;
                    }
                    string commonRoot = "";
                    int maxCount = 0;
                    foreach (var kv in roots)
                    {
                        if (kv.Value > maxCount) { commonRoot = kv.Key; maxCount = kv.Value; }
                    }
                    namespacePattern = commonRoot + ".*";
                }

                int avgFileLength = filesAnalyzed > 0 ? totalLines / filesAnalyzed : 0;

                return string.Format(
                    "{{\"success\":true,\"conventions\":{{\"fieldNaming\":\"{0}\",\"privateFieldPrefix\":\"{1}\",\"methodNaming\":\"PascalCase\",\"namespacePattern\":\"{2}\",\"usesNamespaces\":{3},\"braceStyle\":\"{4}\",\"usingDirectiveOrder\":\"{5}\",\"commentStyle\":\"{6}\",\"usesRegions\":{7},\"averageFileLength\":{8},\"filesAnalyzed\":{9}}}}}",
                    fieldNaming, privateFieldPrefix, Esc(namespacePattern),
                    usesNamespaces ? "true" : "false", braceStyle, usingOrder,
                    commentStyle, usesRegions ? "true" : "false", avgFileLength, filesAnalyzed);
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        public static string GenerateMonoBehaviour(string jsonSpec)
        {
            if (string.IsNullOrEmpty(jsonSpec))
                return "{\"success\":false,\"error\":\"jsonSpec is required\"}";

            try
            {
                var spec = SimpleJson.Parse(jsonSpec);
                string className = spec.GetString("className") ?? "NewScript";
                string ns = spec.GetString("namespace") ?? "";
                string outputPath = spec.GetString("outputPath") ?? "Assets/Scripts/" + className + ".cs";

                // Check if file exists
                string fullPath = outputPath;
                if (!Path.IsPathRooted(fullPath))
                    fullPath = Path.Combine(Application.dataPath, "..", outputPath);
                if (File.Exists(fullPath))
                    return "{\"success\":false,\"error\":\"File already exists: " + Esc(outputPath) + ". Delete it first or choose a different path.\"}";

                var sb = new StringBuilder();
                var usings = new HashSet<string> { "UnityEngine" };

                // Parse fields
                var fields = spec.Get("serializedFields");
                var lifecycleMethods = spec.Get("lifecycleMethods");
                var customMethods = spec.Get("customMethods");
                var requireComponents = spec.Get("requireComponents");
                var interfaces = spec.Get("interfaces");
                var events = spec.Get("events");

                // Check for needed usings
                if (events != null && events.arr != null)
                {
                    foreach (var ev in events.arr)
                    {
                        string evType = ev.GetString("type") ?? "UnityEvent";
                        if (evType.Contains("UnityEvent")) usings.Add("UnityEngine.Events");
                    }
                }
                if (fields != null && fields.arr != null)
                {
                    foreach (var f in fields.arr)
                    {
                        string fType = f.GetString("type") ?? "";
                        if (fType.Contains("Slider") || fType.Contains("Image") || fType.Contains("Button") || fType.Contains("Text"))
                            usings.Add("UnityEngine.UI");
                    }
                }

                // Write usings
                var sortedUsings = new List<string>(usings);
                sortedUsings.Sort();
                foreach (var u in sortedUsings)
                    sb.AppendLine("using " + u + ";");
                sb.AppendLine();

                // Namespace
                int indent = 0;
                if (!string.IsNullOrEmpty(ns))
                {
                    sb.AppendLine("namespace " + ns);
                    sb.AppendLine("{");
                    indent = 1;
                }

                string ind = new string(' ', indent * 4);

                // RequireComponent attributes
                if (requireComponents != null && requireComponents.arr != null)
                {
                    foreach (var rc in requireComponents.arr)
                    {
                        string compName = rc.AsString();
                        sb.AppendLine(ind + "[RequireComponent(typeof(" + compName + "))]");
                    }
                }

                // Class declaration
                string interfaceList = "";
                if (interfaces != null && interfaces.arr != null && interfaces.arr.Count > 0)
                {
                    var ifaceNames = new List<string>();
                    foreach (var iface in interfaces.arr) ifaceNames.Add(iface.AsString());
                    interfaceList = ", " + string.Join(", ", ifaceNames.ToArray());
                }

                sb.AppendLine(ind + "public class " + className + " : MonoBehaviour" + interfaceList);
                sb.AppendLine(ind + "{");

                string memberInd = ind + "    ";

                // Fields
                if (fields != null && fields.arr != null)
                {
                    foreach (var f in fields.arr)
                    {
                        string fName = f.GetString("name") ?? "field";
                        string fType = f.GetString("type") ?? "float";
                        string defaultVal = f.GetString("defaultValue");

                        string fieldLine = memberInd + "[SerializeField] private " + fType + " " + fName;
                        if (!string.IsNullOrEmpty(defaultVal) && defaultVal != "null")
                            fieldLine += " = " + defaultVal;
                        fieldLine += ";";
                        sb.AppendLine(fieldLine);
                    }
                    sb.AppendLine();
                }

                // Events
                if (events != null && events.arr != null)
                {
                    foreach (var ev in events.arr)
                    {
                        string evName = ev.GetString("name") ?? "onEvent";
                        string evType = ev.GetString("type") ?? "UnityEvent";
                        sb.AppendLine(memberInd + "public " + evType + " " + evName + ";");
                    }
                    sb.AppendLine();
                }

                // Lifecycle methods
                if (lifecycleMethods != null && lifecycleMethods.arr != null)
                {
                    foreach (var lm in lifecycleMethods.arr)
                    {
                        string methodName = lm.AsString();
                        sb.AppendLine(memberInd + "private void " + methodName + "()");
                        sb.AppendLine(memberInd + "{");
                        sb.AppendLine(memberInd + "    ");
                        sb.AppendLine(memberInd + "}");
                        sb.AppendLine();
                    }
                }

                // Custom methods
                if (customMethods != null && customMethods.arr != null)
                {
                    foreach (var cm in customMethods.arr)
                    {
                        string mName = cm.GetString("name") ?? "Method";
                        string mReturn = cm.GetString("returnType") ?? "void";
                        string mBody = cm.GetString("body") ?? "";
                        var mParams = cm.Get("params");

                        var paramParts = new List<string>();
                        if (mParams != null && mParams.arr != null)
                        {
                            foreach (var p in mParams.arr)
                            {
                                paramParts.Add(p.GetString("type") + " " + p.GetString("name"));
                            }
                        }

                        sb.AppendLine(memberInd + "public " + mReturn + " " + mName + "(" + string.Join(", ", paramParts.ToArray()) + ")");
                        sb.AppendLine(memberInd + "{");
                        if (!string.IsNullOrEmpty(mBody))
                            sb.AppendLine(memberInd + "    " + mBody);
                        sb.AppendLine(memberInd + "}");
                        sb.AppendLine();
                    }
                }

                sb.AppendLine(ind + "}");

                if (!string.IsNullOrEmpty(ns))
                    sb.AppendLine("}");

                // Write file
                string dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(fullPath, sb.ToString());

                return string.Format("{{\"success\":true,\"outputPath\":\"{0}\",\"className\":\"{1}\",\"namespace\":\"{2}\"}}",
                    Esc(outputPath), Esc(className), Esc(ns));
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        public static string GenerateEditorScript(string jsonSpec)
        {
            if (string.IsNullOrEmpty(jsonSpec))
                return "{\"success\":false,\"error\":\"jsonSpec is required\"}";

            try
            {
                var spec = SimpleJson.Parse(jsonSpec);
                string className = spec.GetString("className") ?? "NewEditor";
                string ns = spec.GetString("namespace") ?? "";
                string outputPath = spec.GetString("outputPath") ?? "Assets/Editor/" + className + ".cs";
                string editorType = spec.GetString("editorType") ?? "CustomInspector";
                string targetType = spec.GetString("targetType") ?? "";

                string fullPath = outputPath;
                if (!Path.IsPathRooted(fullPath))
                    fullPath = Path.Combine(Application.dataPath, "..", outputPath);
                if (File.Exists(fullPath))
                    return "{\"success\":false,\"error\":\"File already exists: " + Esc(outputPath) + "\"}";

                var sb = new StringBuilder();

                sb.AppendLine("using UnityEngine;");
                sb.AppendLine("using UnityEditor;");
                sb.AppendLine();

                int indent = 0;
                if (!string.IsNullOrEmpty(ns))
                {
                    sb.AppendLine("namespace " + ns);
                    sb.AppendLine("{");
                    indent = 1;
                }

                string ind = new string(' ', indent * 4);
                string memberInd = ind + "    ";
                string bodyInd = memberInd + "    ";

                if (editorType == "CustomInspector")
                {
                    sb.AppendLine(ind + "[CustomEditor(typeof(" + targetType + "))]");
                    sb.AppendLine(ind + "public class " + className + " : Editor");
                    sb.AppendLine(ind + "{");

                    // SerializedProperties
                    var serializedProps = spec.Get("serializedProperties");
                    if (serializedProps != null && serializedProps.arr != null)
                    {
                        foreach (var prop in serializedProps.arr)
                        {
                            string propName = prop.AsString();
                            sb.AppendLine(memberInd + "private SerializedProperty " + propName + "Prop;");
                        }
                        sb.AppendLine();

                        sb.AppendLine(memberInd + "private void OnEnable()");
                        sb.AppendLine(memberInd + "{");
                        foreach (var prop in serializedProps.arr)
                        {
                            string propName = prop.AsString();
                            sb.AppendLine(bodyInd + propName + "Prop = serializedObject.FindProperty(\"" + propName + "\");");
                        }
                        sb.AppendLine(memberInd + "}");
                        sb.AppendLine();
                    }

                    sb.AppendLine(memberInd + "public override void OnInspectorGUI()");
                    sb.AppendLine(memberInd + "{");
                    sb.AppendLine(bodyInd + "serializedObject.Update();");
                    sb.AppendLine();

                    if (serializedProps != null && serializedProps.arr != null)
                    {
                        foreach (var prop in serializedProps.arr)
                        {
                            string propName = prop.AsString();
                            sb.AppendLine(bodyInd + "EditorGUILayout.PropertyField(" + propName + "Prop);");
                        }
                    }
                    else
                    {
                        sb.AppendLine(bodyInd + "DrawDefaultInspector();");
                    }

                    sb.AppendLine();
                    sb.AppendLine(bodyInd + "serializedObject.ApplyModifiedProperties();");
                    sb.AppendLine(memberInd + "}");
                }
                else if (editorType == "EditorWindow")
                {
                    sb.AppendLine(ind + "public class " + className + " : EditorWindow");
                    sb.AppendLine(ind + "{");

                    sb.AppendLine(memberInd + "[MenuItem(\"Window/" + className + "\")]");
                    sb.AppendLine(memberInd + "public static void ShowWindow()");
                    sb.AppendLine(memberInd + "{");
                    sb.AppendLine(bodyInd + "GetWindow<" + className + ">(\"" + className + "\");");
                    sb.AppendLine(memberInd + "}");
                    sb.AppendLine();
                    sb.AppendLine(memberInd + "private void OnGUI()");
                    sb.AppendLine(memberInd + "{");
                    sb.AppendLine(bodyInd + "// Custom editor window GUI");
                    sb.AppendLine(memberInd + "}");
                }
                else if (editorType == "PropertyDrawer")
                {
                    sb.AppendLine(ind + "[CustomPropertyDrawer(typeof(" + targetType + "))]");
                    sb.AppendLine(ind + "public class " + className + " : PropertyDrawer");
                    sb.AppendLine(ind + "{");
                    sb.AppendLine(memberInd + "public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)");
                    sb.AppendLine(memberInd + "{");
                    sb.AppendLine(bodyInd + "EditorGUI.PropertyField(position, property, label, true);");
                    sb.AppendLine(memberInd + "}");
                }

                sb.AppendLine(ind + "}");

                if (!string.IsNullOrEmpty(ns))
                    sb.AppendLine("}");

                string dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(fullPath, sb.ToString());

                return string.Format("{{\"success\":true,\"outputPath\":\"{0}\",\"className\":\"{1}\",\"editorType\":\"{2}\"}}",
                    Esc(outputPath), Esc(className), Esc(editorType));
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        public static string GenerateTestScript(string jsonSpec)
        {
            if (string.IsNullOrEmpty(jsonSpec))
                return "{\"success\":false,\"error\":\"jsonSpec is required\"}";

            try
            {
                var spec = SimpleJson.Parse(jsonSpec);
                string className = spec.GetString("className") ?? "NewTests";
                string ns = spec.GetString("namespace") ?? "";
                string outputPath = spec.GetString("outputPath") ?? "Assets/Tests/" + className + ".cs";
                string testMode = spec.GetString("testMode") ?? "EditMode";
                string setupBody = spec.GetString("setupBody") ?? "";
                string teardownBody = spec.GetString("teardownBody") ?? "";
                var testMethods = spec.Get("testMethods");

                string fullPath = outputPath;
                if (!Path.IsPathRooted(fullPath))
                    fullPath = Path.Combine(Application.dataPath, "..", outputPath);
                if (File.Exists(fullPath))
                    return "{\"success\":false,\"error\":\"File already exists: " + Esc(outputPath) + "\"}";

                var sb = new StringBuilder();

                sb.AppendLine("using NUnit.Framework;");
                if (testMode == "PlayMode")
                {
                    sb.AppendLine("using System.Collections;");
                    sb.AppendLine("using UnityEngine.TestTools;");
                }
                sb.AppendLine("using UnityEngine;");
                sb.AppendLine();

                int indent = 0;
                if (!string.IsNullOrEmpty(ns))
                {
                    sb.AppendLine("namespace " + ns);
                    sb.AppendLine("{");
                    indent = 1;
                }

                string ind = new string(' ', indent * 4);
                string memberInd = ind + "    ";
                string bodyInd = memberInd + "    ";

                sb.AppendLine(ind + "[TestFixture]");
                sb.AppendLine(ind + "public class " + className);
                sb.AppendLine(ind + "{");

                // SetUp
                if (!string.IsNullOrEmpty(setupBody))
                {
                    sb.AppendLine(memberInd + "[SetUp]");
                    sb.AppendLine(memberInd + "public void SetUp()");
                    sb.AppendLine(memberInd + "{");
                    sb.AppendLine(bodyInd + setupBody);
                    sb.AppendLine(memberInd + "}");
                    sb.AppendLine();
                }

                // TearDown
                if (!string.IsNullOrEmpty(teardownBody))
                {
                    sb.AppendLine(memberInd + "[TearDown]");
                    sb.AppendLine(memberInd + "public void TearDown()");
                    sb.AppendLine(memberInd + "{");
                    sb.AppendLine(bodyInd + teardownBody);
                    sb.AppendLine(memberInd + "}");
                    sb.AppendLine();
                }

                // Test methods
                if (testMethods != null && testMethods.arr != null)
                {
                    foreach (var tm in testMethods.arr)
                    {
                        string mName = tm.GetString("name") ?? "TestMethod";
                        string mBody = tm.GetString("body") ?? "Assert.Pass();";

                        if (testMode == "PlayMode")
                        {
                            sb.AppendLine(memberInd + "[UnityTest]");
                            sb.AppendLine(memberInd + "public IEnumerator " + mName + "()");
                        }
                        else
                        {
                            sb.AppendLine(memberInd + "[Test]");
                            sb.AppendLine(memberInd + "public void " + mName + "()");
                        }
                        sb.AppendLine(memberInd + "{");
                        sb.AppendLine(bodyInd + mBody);
                        if (testMode == "PlayMode")
                            sb.AppendLine(bodyInd + "yield return null;");
                        sb.AppendLine(memberInd + "}");
                        sb.AppendLine();
                    }
                }

                sb.AppendLine(ind + "}");

                if (!string.IsNullOrEmpty(ns))
                    sb.AppendLine("}");

                string dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(fullPath, sb.ToString());

                return string.Format("{{\"success\":true,\"outputPath\":\"{0}\",\"className\":\"{1}\",\"testMode\":\"{2}\"}}",
                    Esc(outputPath), Esc(className), Esc(testMode));
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        private static string Esc(string str)
        {
            if (str == null) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
