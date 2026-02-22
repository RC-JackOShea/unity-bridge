using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UnityBridge
{
    /// <summary>
    /// Wraps Unity Test Framework execution, runs Edit Mode and Play Mode tests,
    /// parses NUnit XML results, and returns structured pass/fail/error reports.
    /// Uses the TestRunnerApi for programmatic test execution when available,
    /// with fallback to direct NUnit XML result file parsing.
    /// Also supports custom project-wide validation checks.
    /// </summary>
    public static class TestRunner
    {
        public static string RunEditModeTests(string filter)
        {
            return RunTests("EditMode", filter);
        }

        public static string RunPlayModeTests(string filter)
        {
            return RunTests("PlayMode", filter);
        }

        public static string RunAllTests()
        {
            string editResults = RunTests("EditMode", "");
            string playResults = RunTests("PlayMode", "");

            return string.Format("{{\"success\":true,\"editMode\":{0},\"playMode\":{1}}}", editResults, playResults);
        }

        public static string GetTestList()
        {
            try
            {
                var testMethods = new List<string>();

                // Scan assemblies for test methods
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string asmName = asm.GetName().Name;
                    // Only scan test assemblies and user assemblies
                    if (asmName.Contains("Test") || asmName.Contains("test") ||
                        asmName == "Assembly-CSharp-Editor" || asmName == "Assembly-CSharp")
                    {
                        try
                        {
                            foreach (var type in asm.GetTypes())
                            {
                                bool hasTestFixture = false;
                                foreach (var attr in type.GetCustomAttributes(true))
                                {
                                    if (attr.GetType().Name == "TestFixtureAttribute")
                                    {
                                        hasTestFixture = true;
                                        break;
                                    }
                                }
                                if (!hasTestFixture) continue;

                                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                                {
                                    bool isTest = false;
                                    bool isUnityTest = false;
                                    string category = "";

                                    foreach (var attr in method.GetCustomAttributes(true))
                                    {
                                        string attrName = attr.GetType().Name;
                                        if (attrName == "TestAttribute") isTest = true;
                                        if (attrName == "UnityTestAttribute") { isTest = true; isUnityTest = true; }
                                        if (attrName == "CategoryAttribute")
                                        {
                                            var nameProp = attr.GetType().GetProperty("Name");
                                            if (nameProp != null) category = nameProp.GetValue(attr)?.ToString() ?? "";
                                        }
                                    }

                                    if (!isTest) continue;

                                    string platform = isUnityTest ? "PlayMode" : "EditMode";
                                    testMethods.Add(string.Format(
                                        "{{\"name\":\"{0}\",\"fullName\":\"{1}.{0}\",\"class\":\"{1}\",\"platform\":\"{2}\",\"category\":\"{3}\"}}",
                                        Esc(method.Name), Esc(type.FullName), platform, Esc(category)));
                                }
                            }
                        }
                        catch { }
                    }
                }

                return string.Format("{{\"success\":true,\"tests\":[{0}],\"totalTests\":{1}}}",
                    string.Join(",", testMethods.ToArray()), testMethods.Count);
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        public static string RunValidation(string validatorName)
        {
            if (string.IsNullOrEmpty(validatorName))
                return "{\"success\":false,\"error\":\"validatorName is required\"}";

            try
            {
                var issues = new List<string>();

                switch (validatorName.ToLowerInvariant())
                {
                    case "missingscripts":
                        issues = ValidateNoMissingScripts();
                        break;
                    case "missingreferences":
                        issues = ValidateNoMissingReferences();
                        break;
                    case "tmpusage":
                        issues = ValidateAllUIUsesTMP();
                        break;
                    case "collideronrigidbody":
                        issues = ValidateColliderOnRigidbody();
                        break;
                    default:
                        return "{\"success\":false,\"error\":\"Unknown validator: " + Esc(validatorName) + ". Available: missingscripts, missingreferences, tmpusage, collideronrigidbody\"}";
                }

                var issueEntries = new List<string>();
                foreach (var issue in issues)
                {
                    issueEntries.Add("\"" + Esc(issue) + "\"");
                }

                return string.Format("{{\"success\":true,\"validator\":\"{0}\",\"issues\":[{1}],\"issueCount\":{2},\"passed\":{3}}}",
                    Esc(validatorName), string.Join(",", issueEntries.ToArray()), issues.Count,
                    issues.Count == 0 ? "true" : "false");
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        private static string RunTests(string platform, string filter)
        {
            try
            {
                // Try to use TestRunnerApi via reflection (avoids hard dependency on test assemblies)
                var apiType = FindType("UnityEditor.TestTools.TestRunner.Api.TestRunnerApi");
                if (apiType != null)
                {
                    return RunTestsViaApi(apiType, platform, filter);
                }

                // Fallback: scan for existing NUnit XML results
                string resultPath = FindTestResultXml(platform);
                if (!string.IsNullOrEmpty(resultPath) && File.Exists(resultPath))
                {
                    return ParseNUnitXml(resultPath, platform);
                }

                // Fallback: use reflection to discover and report tests
                return DiscoverAndReportTests(platform, filter);
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"platform\":\"" + Esc(platform) + "\",\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        private static string RunTestsViaApi(Type apiType, string platform, string filter)
        {
            try
            {
                // Create TestRunnerApi instance
                var apiInstance = ScriptableObject.CreateInstance(apiType);

                // Build ExecutionSettings
                var settingsType = FindType("UnityEditor.TestTools.TestRunner.Api.ExecutionSettings");
                if (settingsType == null)
                    return DiscoverAndReportTests(platform, filter);

                var filterType = FindType("UnityEditor.TestTools.TestRunner.Api.Filter");
                if (filterType == null)
                    return DiscoverAndReportTests(platform, filter);

                var filterInstance = Activator.CreateInstance(filterType);

                // Set test mode
                var testModeType = FindType("UnityEditor.TestTools.TestRunner.Api.TestMode");
                if (testModeType != null)
                {
                    object testMode = platform == "PlayMode"
                        ? Enum.Parse(testModeType, "PlayMode")
                        : Enum.Parse(testModeType, "EditMode");
                    var testModeField = filterType.GetField("testMode");
                    if (testModeField != null) testModeField.SetValue(filterInstance, testMode);
                }

                // Set filter name if provided
                if (!string.IsNullOrEmpty(filter))
                {
                    var testNamesField = filterType.GetField("testNames");
                    if (testNamesField != null)
                    {
                        testNamesField.SetValue(filterInstance, new string[] { filter });
                    }
                }

                // Create ExecutionSettings with filter
                var settingsCtors = settingsType.GetConstructors();
                object settings = null;
                foreach (var ctor in settingsCtors)
                {
                    var parms = ctor.GetParameters();
                    if (parms.Length == 1 && parms[0].ParameterType == filterType)
                    {
                        settings = ctor.Invoke(new object[] { filterInstance });
                        break;
                    }
                }
                if (settings == null)
                    settings = Activator.CreateInstance(settingsType);

                // Execute
                var executeMethod = apiType.GetMethod("Execute", new Type[] { settingsType });
                if (executeMethod != null)
                {
                    executeMethod.Invoke(apiInstance, new object[] { settings });
                    // The API runs asynchronously; check for results file
                    System.Threading.Thread.Sleep(2000);

                    string resultPath = FindTestResultXml(platform);
                    if (!string.IsNullOrEmpty(resultPath) && File.Exists(resultPath))
                    {
                        return ParseNUnitXml(resultPath, platform);
                    }
                }

                UnityEngine.Object.DestroyImmediate(apiInstance);
                return DiscoverAndReportTests(platform, filter);
            }
            catch
            {
                return DiscoverAndReportTests(platform, filter);
            }
        }

        private static string DiscoverAndReportTests(string platform, string filter)
        {
            var testMethods = new List<string>();
            bool isPlayMode = platform == "PlayMode";

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in asm.GetTypes())
                    {
                        bool hasTestFixture = false;
                        foreach (var attr in type.GetCustomAttributes(true))
                        {
                            if (attr.GetType().Name == "TestFixtureAttribute")
                            {
                                hasTestFixture = true;
                                break;
                            }
                        }
                        if (!hasTestFixture) continue;

                        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                        {
                            bool isTest = false;
                            bool isUnityTest = false;

                            foreach (var attr in method.GetCustomAttributes(true))
                            {
                                string attrName = attr.GetType().Name;
                                if (attrName == "TestAttribute") isTest = true;
                                if (attrName == "UnityTestAttribute") { isTest = true; isUnityTest = true; }
                            }

                            if (!isTest) continue;
                            if (isPlayMode && !isUnityTest) continue;
                            if (!isPlayMode && isUnityTest) continue;

                            if (!string.IsNullOrEmpty(filter) &&
                                !method.Name.Contains(filter) &&
                                !type.FullName.Contains(filter))
                                continue;

                            // Try to run the test directly via reflection
                            string status = "Skipped";
                            string message = "";
                            double duration = 0;

                            if (!isPlayMode)
                            {
                                try
                                {
                                    var instance = Activator.CreateInstance(type);
                                    // Run SetUp if exists
                                    foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                                    {
                                        foreach (var a in m.GetCustomAttributes(true))
                                        {
                                            if (a.GetType().Name == "SetUpAttribute")
                                            {
                                                m.Invoke(instance, null);
                                                break;
                                            }
                                        }
                                    }

                                    double startTime = EditorApplication.timeSinceStartup;
                                    method.Invoke(instance, null);
                                    duration = EditorApplication.timeSinceStartup - startTime;
                                    status = "Passed";

                                    // Run TearDown if exists
                                    foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                                    {
                                        foreach (var a in m.GetCustomAttributes(true))
                                        {
                                            if (a.GetType().Name == "TearDownAttribute")
                                            {
                                                m.Invoke(instance, null);
                                                break;
                                            }
                                        }
                                    }
                                }
                                catch (TargetInvocationException tie)
                                {
                                    var inner = tie.InnerException;
                                    if (inner != null && inner.GetType().Name == "SuccessException")
                                    {
                                        status = "Passed";
                                    }
                                    else
                                    {
                                        status = "Failed";
                                        message = inner?.Message ?? tie.Message;
                                    }
                                }
                                catch (Exception e)
                                {
                                    status = "Failed";
                                    message = e.Message;
                                }
                            }

                            testMethods.Add(string.Format(CultureInfo.InvariantCulture,
                                "{{\"name\":\"{0}\",\"fullName\":\"{1}.{0}\",\"status\":\"{2}\",\"duration\":{3},\"message\":\"{4}\"}}",
                                Esc(method.Name), Esc(type.FullName), status, duration, Esc(message)));
                        }
                    }
                }
                catch { }
            }

            int passed = 0, failed = 0, skipped = 0;
            foreach (var t in testMethods)
            {
                if (t.Contains("\"Passed\"")) passed++;
                else if (t.Contains("\"Failed\"")) failed++;
                else skipped++;
            }

            return string.Format(
                "{{\"success\":true,\"testPlatform\":\"{0}\",\"results\":[{1}],\"summary\":{{\"total\":{2},\"passed\":{3},\"failed\":{4},\"skipped\":{5}}}}}",
                Esc(platform), string.Join(",", testMethods.ToArray()), testMethods.Count, passed, failed, skipped);
        }

        private static string FindTestResultXml(string platform)
        {
            string projectPath = Path.Combine(Application.dataPath, "..");
            // Unity stores test results at project root
            string editModeResult = Path.Combine(projectPath, "TestResults-editmode.xml");
            string playModeResult = Path.Combine(projectPath, "TestResults-playmode.xml");
            return platform == "PlayMode" ? playModeResult : editModeResult;
        }

        private static string ParseNUnitXml(string xmlPath, string platform)
        {
            try
            {
                string xml = File.ReadAllText(xmlPath);
                var results = new List<string>();
                int passed = 0, failed = 0, skipped = 0;

                // Simple XML parsing for NUnit test-case elements
                int idx = 0;
                while (true)
                {
                    idx = xml.IndexOf("<test-case", idx);
                    if (idx < 0) break;

                    int endTag = xml.IndexOf("/>", idx);
                    int endTag2 = xml.IndexOf("</test-case>", idx);
                    int end = endTag > 0 ? endTag : endTag2;
                    if (end < 0) break;

                    string element = xml.Substring(idx, end - idx + 2);

                    string name = GetXmlAttr(element, "name");
                    string fullName = GetXmlAttr(element, "fullname");
                    string result = GetXmlAttr(element, "result");
                    string durationStr = GetXmlAttr(element, "duration");
                    double duration = 0;
                    double.TryParse(durationStr, NumberStyles.Any, CultureInfo.InvariantCulture, out duration);

                    string message = "";
                    int msgStart = element.IndexOf("<message>");
                    if (msgStart > 0)
                    {
                        int msgEnd = element.IndexOf("</message>", msgStart);
                        if (msgEnd > 0)
                            message = element.Substring(msgStart + 9, msgEnd - msgStart - 9);
                    }

                    string status = result == "Passed" ? "Passed" : result == "Failed" ? "Failed" : "Skipped";
                    if (status == "Passed") passed++;
                    else if (status == "Failed") failed++;
                    else skipped++;

                    results.Add(string.Format(CultureInfo.InvariantCulture,
                        "{{\"name\":\"{0}\",\"fullName\":\"{1}\",\"status\":\"{2}\",\"duration\":{3},\"message\":\"{4}\"}}",
                        Esc(name), Esc(fullName), status, duration, Esc(message)));

                    idx = end + 1;
                }

                return string.Format(
                    "{{\"success\":true,\"testPlatform\":\"{0}\",\"source\":\"nunit-xml\",\"results\":[{1}],\"summary\":{{\"total\":{2},\"passed\":{3},\"failed\":{4},\"skipped\":{5}}}}}",
                    Esc(platform), string.Join(",", results.ToArray()), results.Count, passed, failed, skipped);
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"XML parse error: " + Esc(e.Message) + "\"}";
            }
        }

        private static string GetXmlAttr(string element, string attrName)
        {
            string search = attrName + "=\"";
            int start = element.IndexOf(search);
            if (start < 0) return "";
            start += search.Length;
            int end = element.IndexOf("\"", start);
            if (end < 0) return "";
            return element.Substring(start, end - start);
        }

        // --- Custom Validators ---

        private static List<string> ValidateNoMissingScripts()
        {
            var issues = new List<string>();
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");
            foreach (var guid in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;

                var components = go.GetComponentsInChildren<Component>(true);
                foreach (var comp in components)
                {
                    if (comp == null)
                    {
                        issues.Add("Missing script in prefab: " + path);
                        break;
                    }
                }
            }
            return issues;
        }

        private static List<string> ValidateNoMissingReferences()
        {
            var issues = new List<string>();
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");
            foreach (var guid in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;

                var components = go.GetComponentsInChildren<Component>(true);
                foreach (var comp in components)
                {
                    if (comp == null) continue;
                    var so = new SerializedObject(comp);
                    var sp = so.GetIterator();
                    while (sp.NextVisible(true))
                    {
                        if (sp.propertyType == SerializedPropertyType.ObjectReference &&
                            sp.objectReferenceValue == null &&
                            sp.objectReferenceInstanceIDValue != 0)
                        {
                            issues.Add("Missing reference in " + path + " -> " + comp.GetType().Name + "." + sp.name);
                        }
                    }
                }
            }
            return issues;
        }

        private static List<string> ValidateAllUIUsesTMP()
        {
            var issues = new List<string>();
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");
            foreach (var guid in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;

                var textComponents = go.GetComponentsInChildren<UnityEngine.UI.Text>(true);
                foreach (var text in textComponents)
                {
                    issues.Add("Legacy UI.Text found in " + path + " -> " + GetPath(text.transform) + " (use TextMeshProUGUI instead)");
                }
            }
            return issues;
        }

        private static List<string> ValidateColliderOnRigidbody()
        {
            var issues = new List<string>();
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");
            foreach (var guid in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;

                var rigidbodies = go.GetComponentsInChildren<Rigidbody>(true);
                foreach (var rb in rigidbodies)
                {
                    if (rb.GetComponent<Collider>() == null)
                    {
                        issues.Add("Rigidbody without Collider in " + path + " -> " + GetPath(rb.transform));
                    }
                }
            }
            return issues;
        }

        private static string GetPath(Transform t)
        {
            if (t.parent == null) return t.name;
            return GetPath(t.parent) + "/" + t.name;
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = asm.GetType(fullName);
                if (type != null) return type;
            }
            return null;
        }

        private static string Esc(string str)
        {
            if (str == null) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
