using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace UnityBridge
{
    /// <summary>
    /// Regex-based static analysis for C# source files. Five review categories:
    /// Performance, Correctness, Unity Best Practices, Architecture, Production Readiness.
    /// Uses line-by-line scanning with method-scope tracking to detect Unity-specific anti-patterns.
    /// Roslyn analyzers evaluated for future enhancement (noted in comments).
    /// </summary>
    public static class CodeReviewer
    {
        public static string ReviewFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return "{\"success\":false,\"error\":\"filePath is required\"}";

            string fullPath = filePath;
            if (!Path.IsPathRooted(fullPath))
                fullPath = Path.Combine(Application.dataPath, "..", filePath);

            if (!File.Exists(fullPath))
                return "{\"success\":false,\"error\":\"File not found: " + Esc(filePath) + "\"}";

            try
            {
                var issues = AnalyzeFile(fullPath, filePath);
                return FormatResult(new string[] { filePath }, issues);
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        public static string ReviewProject()
        {
            try
            {
                string assetsPath = Application.dataPath;
                var csFiles = Directory.GetFiles(assetsPath, "*.cs", SearchOption.AllDirectories);
                var allIssues = new List<string>();
                var reviewedFiles = new List<string>();

                foreach (var fullPath in csFiles)
                {
                    string relativePath = "Assets" + fullPath.Substring(assetsPath.Length).Replace("\\", "/");
                    reviewedFiles.Add(relativePath);
                    var issues = AnalyzeFile(fullPath, relativePath);
                    allIssues.AddRange(issues);
                }

                return FormatResult(reviewedFiles.ToArray(), allIssues);
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        public static string ReviewFiles(string jsonFilePaths)
        {
            if (string.IsNullOrEmpty(jsonFilePaths))
                return "{\"success\":false,\"error\":\"jsonFilePaths is required (JSON array of paths)\"}";

            try
            {
                var parsed = SimpleJson.Parse(jsonFilePaths);
                if (parsed.arr == null)
                    return "{\"success\":false,\"error\":\"Expected JSON array of file paths\"}";

                var allIssues = new List<string>();
                var reviewedFiles = new List<string>();

                foreach (var node in parsed.arr)
                {
                    string filePath = node.AsString();
                    string fullPath = filePath;
                    if (!Path.IsPathRooted(fullPath))
                        fullPath = Path.Combine(Application.dataPath, "..", filePath);

                    if (!File.Exists(fullPath)) continue;

                    reviewedFiles.Add(filePath);
                    var issues = AnalyzeFile(fullPath, filePath);
                    allIssues.AddRange(issues);
                }

                return FormatResult(reviewedFiles.ToArray(), allIssues);
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        private static List<string> AnalyzeFile(string fullPath, string displayPath)
        {
            var issues = new List<string>();
            string[] lines = File.ReadAllLines(fullPath);

            // State tracking
            bool isMonoBehaviour = false;
            bool inUpdateMethod = false;
            int updateBraceDepth = 0;
            int currentBraceDepth = 0;
            int serializedFieldCount = 0;
            bool hasSerializedObjectUsage = false;
            bool hasApplyModifiedProperties = false;
            int totalLines = lines.Length;
            bool inConditionalCompilation = false;

            // Patterns
            var methodDeclPattern = new Regex(@"^\s*(public|private|protected|internal|static|\s)*(void|bool|int|float|string|IEnumerator|\w+)\s+(Update|FixedUpdate|LateUpdate)\s*\(");
            var classPattern = new Regex(@"class\s+\w+\s*.*:\s*.*MonoBehaviour");
            var getComponentInline = new Regex(@"GetComponent\s*<");
            var gameObjectFind = new Regex(@"GameObject\.Find\s*\(");
            var findObjectOfType = new Regex(@"FindObjectOfType");
            var newAllocation = new Regex(@"\bnew\s+(?!Vector[234]|Quaternion|Color|Rect|Bounds)(\w+)");
            var stringConcat = new Regex(@"""[^""]*""\s*\+|\+\s*""[^""]*""");
            var linqPattern = new Regex(@"\.(Where|Select|OrderBy|GroupBy|ToList|ToArray|FirstOrDefault|Any|All|Count)\s*\(");
            var debugLogPattern = new Regex(@"Debug\.(Log|LogWarning|LogError)\s*\(");
            var todoPattern = new Regex(@"//\s*(TODO|FIXME|HACK)\b", RegexOptions.IgnoreCase);
            var publicFieldPattern = new Regex(@"^\s*public\s+\w+(\<[^>]+\>)?\s+\w+\s*[;=]");
            var sendMessagePattern = new Regex(@"\.(SendMessage|BroadcastMessage)\s*\(");
            var constructorPattern = new Regex(@"^\s*(public|private|protected)\s+\w+\s*\([^)]*\)\s*{?\s*$");
            var staticFieldPattern = new Regex(@"^\s*(public|private|protected|internal)\s+static\s+\w+");
            var serializedFieldAttr = new Regex(@"\[SerializeField\]");
            var emptyCatchPattern = new Regex(@"catch\s*\([^)]*\)\s*\{\s*\}");
            var hardcodedPathPattern = new Regex(@"""[A-Z]:\\|""\/[a-z]|""https?://|""ftp://");
            var floatComparePattern = new Regex(@"\b\w+\s*[!=]=\s*\d+\.\d+f?\b|\b\d+\.\d+f?\s*[!=]=");
            var serializedObjectPattern = new Regex(@"new\s+SerializedObject|serializedObject\b");
            var applyModifiedPattern = new Regex(@"ApplyModifiedProperties\s*\(");
            var ifEditorPattern = new Regex(@"#if\s+(UNITY_EDITOR|DEBUG)");
            var endifPattern = new Regex(@"#endif");

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.Trim();
                int lineNum = i + 1;

                // Track conditional compilation
                if (ifEditorPattern.IsMatch(trimmed)) { inConditionalCompilation = true; continue; }
                if (endifPattern.IsMatch(trimmed)) { inConditionalCompilation = false; continue; }

                // Skip comments and empty lines for some checks
                if (trimmed.StartsWith("//") && !todoPattern.IsMatch(trimmed) && !IsCommentedCode(trimmed))
                    continue;

                // Track class type
                if (classPattern.IsMatch(line)) isMonoBehaviour = true;

                // Track brace depth
                foreach (char c in line)
                {
                    if (c == '{') currentBraceDepth++;
                    if (c == '}')
                    {
                        currentBraceDepth--;
                        if (inUpdateMethod && currentBraceDepth < updateBraceDepth)
                            inUpdateMethod = false;
                    }
                }

                // Track Update-family methods
                if (methodDeclPattern.IsMatch(line))
                {
                    inUpdateMethod = true;
                    updateBraceDepth = currentBraceDepth;
                }

                // Track SerializeField count
                if (serializedFieldAttr.IsMatch(line)) serializedFieldCount++;

                // Track SerializedObject usage
                if (serializedObjectPattern.IsMatch(line)) hasSerializedObjectUsage = true;
                if (applyModifiedPattern.IsMatch(line)) hasApplyModifiedProperties = true;

                // === Performance Rules (in Update-family methods) ===
                if (inUpdateMethod)
                {
                    if (gameObjectFind.IsMatch(line))
                        issues.Add(FormatIssue("PERF001", "Performance", "warning", displayPath, lineNum, trimmed,
                            "GameObject.Find() called inside Update() -- expensive per-frame lookup",
                            "Cache the result in Start() or Awake() and store in a field"));

                    if (getComponentInline.IsMatch(line))
                        issues.Add(FormatIssue("PERF002", "Performance", "warning", displayPath, lineNum, trimmed,
                            "GetComponent<T>() called inside Update() -- expensive per-frame call",
                            "Cache the component reference in Start() or Awake()"));

                    if (findObjectOfType.IsMatch(line))
                        issues.Add(FormatIssue("PERF003", "Performance", "warning", displayPath, lineNum, trimmed,
                            "FindObjectOfType called inside Update() -- very expensive per-frame search",
                            "Cache the result in Start() or use a singleton pattern"));

                    if (newAllocation.IsMatch(line))
                        issues.Add(FormatIssue("PERF004", "Performance", "warning", displayPath, lineNum, trimmed,
                            "Object allocation inside Update() -- causes GC pressure",
                            "Pre-allocate and reuse objects, or use object pooling"));

                    if (stringConcat.IsMatch(line))
                        issues.Add(FormatIssue("PERF005", "Performance", "info", displayPath, lineNum, trimmed,
                            "String concatenation inside Update() -- allocates garbage each frame",
                            "Use StringBuilder or string.Format for repeated string operations"));

                    if (linqPattern.IsMatch(line))
                        issues.Add(FormatIssue("PERF006", "Performance", "warning", displayPath, lineNum, trimmed,
                            "LINQ method inside Update() -- creates allocations from iterators",
                            "Use manual loops or cache LINQ results outside Update"));
                }

                // === Correctness Rules ===
                // CORR001: GetComponent without null check (check next few lines)
                if (getComponentInline.IsMatch(line) && !inUpdateMethod)
                {
                    bool hasNullCheck = false;
                    for (int j = i + 1; j < Math.Min(i + 4, lines.Length); j++)
                    {
                        if (lines[j].Contains("!= null") || lines[j].Contains("== null") || lines[j].Contains("?."))
                        {
                            hasNullCheck = true;
                            break;
                        }
                    }
                    if (!hasNullCheck)
                        issues.Add(FormatIssue("CORR001", "Correctness", "warning", displayPath, lineNum, trimmed,
                            "GetComponent<T>() without null check on subsequent lines",
                            "Add a null check: if (comp != null) or use the ?. operator"));
                }

                // CORR004: Float comparison with == or !=
                if (floatComparePattern.IsMatch(line))
                    issues.Add(FormatIssue("CORR004", "Correctness", "info", displayPath, lineNum, trimmed,
                        "Float comparison using == or != -- may be imprecise due to floating point",
                        "Use Mathf.Approximately() for float comparisons"));

                // === Unity Best Practices ===
                if (isMonoBehaviour)
                {
                    // BEST001: Public fields (should use [SerializeField] private)
                    if (publicFieldPattern.IsMatch(line) && !line.Contains("static") &&
                        !line.Contains("const") && !line.Contains("readonly"))
                    {
                        issues.Add(FormatIssue("BEST001", "UnityBestPractices", "info", displayPath, lineNum, trimmed,
                            "Public field -- consider [SerializeField] private for better encapsulation",
                            "Change to: [SerializeField] private <type> <name>;"));
                    }

                    // BEST003: Constructor in MonoBehaviour
                    if (constructorPattern.IsMatch(line) && !line.Contains("static"))
                    {
                        string className = "";
                        var classMatch = Regex.Match(File.ReadAllText(fullPath), @"class\s+(\w+)");
                        if (classMatch.Success) className = classMatch.Groups[1].Value;
                        if (!string.IsNullOrEmpty(className) && line.Contains(className))
                        {
                            issues.Add(FormatIssue("BEST003", "UnityBestPractices", "warning", displayPath, lineNum, trimmed,
                                "Constructor in MonoBehaviour -- Unity manages lifecycle, use Awake() instead",
                                "Move initialization logic to Awake() or Start()"));
                        }
                    }

                    // BEST004: Static fields in MonoBehaviour
                    if (staticFieldPattern.IsMatch(line) && !line.Contains("const"))
                    {
                        issues.Add(FormatIssue("BEST004", "UnityBestPractices", "info", displayPath, lineNum, trimmed,
                            "Static field in MonoBehaviour -- may cause issues with scene reloading",
                            "Consider using a ScriptableObject or static manager class instead"));
                    }
                }

                // BEST002: SendMessage usage
                if (sendMessagePattern.IsMatch(line))
                    issues.Add(FormatIssue("BEST002", "UnityBestPractices", "info", displayPath, lineNum, trimmed,
                        "SendMessage/BroadcastMessage is slow and not type-safe",
                        "Use direct method calls, events, or UnityEvent instead"));

                // === Production Readiness ===
                // PROD001: TODO/FIXME/HACK
                if (todoPattern.IsMatch(trimmed))
                    issues.Add(FormatIssue("PROD001", "ProductionReadiness", "info", displayPath, lineNum, trimmed,
                        "TODO/FIXME/HACK comment -- unresolved work item",
                        "Resolve or create a tracked issue for this item"));

                // PROD002: Debug.Log outside conditional compilation
                if (debugLogPattern.IsMatch(line) && !inConditionalCompilation)
                    issues.Add(FormatIssue("PROD002", "ProductionReadiness", "warning", displayPath, lineNum, trimmed,
                        "Debug.Log outside #if UNITY_EDITOR or #if DEBUG guard",
                        "Wrap in #if UNITY_EDITOR ... #endif or remove for production"));

                // PROD003: Commented-out code
                if (IsCommentedCode(trimmed))
                    issues.Add(FormatIssue("PROD003", "ProductionReadiness", "info", displayPath, lineNum, trimmed,
                        "Commented-out code block detected",
                        "Remove dead code -- use version control to preserve history"));

                // PROD004: Hardcoded paths/URLs
                if (hardcodedPathPattern.IsMatch(line))
                    issues.Add(FormatIssue("PROD004", "ProductionReadiness", "info", displayPath, lineNum, trimmed,
                        "Hardcoded file path or URL in string literal",
                        "Use a configuration file, ScriptableObject, or const/static field"));

                // PROD005: Empty catch blocks
                if (emptyCatchPattern.IsMatch(line))
                    issues.Add(FormatIssue("PROD005", "ProductionReadiness", "warning", displayPath, lineNum, trimmed,
                        "Empty catch block -- silently swallows exceptions",
                        "Log the exception or handle it explicitly"));
            }

            // === Architecture Rules (file-level) ===
            // ARCH001: God class (>500 lines)
            if (totalLines > 500)
                issues.Add(FormatIssue("ARCH001", "Architecture", "warning", displayPath, 1,
                    "File has " + totalLines + " lines",
                    "Class exceeds 500 lines -- may have too many responsibilities",
                    "Consider splitting into smaller, focused classes"));

            // ARCH002: Too many SerializeField (>10)
            if (serializedFieldCount > 10)
                issues.Add(FormatIssue("ARCH002", "Architecture", "info", displayPath, 1,
                    serializedFieldCount + " [SerializeField] attributes",
                    "More than 10 serialized fields -- class may be too complex",
                    "Consider grouping related fields into ScriptableObjects or sub-components"));

            // CORR002: SerializedObject without ApplyModifiedProperties
            if (hasSerializedObjectUsage && !hasApplyModifiedProperties)
                issues.Add(FormatIssue("CORR002", "Correctness", "warning", displayPath, 1,
                    "SerializedObject used without ApplyModifiedProperties",
                    "SerializedObject modifications won't persist without ApplyModifiedProperties()",
                    "Add serializedObject.ApplyModifiedProperties() after making changes"));

            return issues;
        }

        private static bool IsCommentedCode(string trimmed)
        {
            if (!trimmed.StartsWith("//")) return false;
            string afterComment = trimmed.Substring(2).Trim();
            // Check for code-like patterns
            if (afterComment.Contains(";") && (afterComment.Contains("=") || afterComment.Contains("(") || afterComment.Contains(".")))
                return true;
            if (Regex.IsMatch(afterComment, @"^\s*(if|else|for|while|return|var|int|float|string|bool)\s"))
                return true;
            return false;
        }

        private static string FormatIssue(string ruleId, string category, string severity,
            string file, int line, string lineContent, string description, string suggestion)
        {
            return string.Format(
                "{{\"ruleId\":\"{0}\",\"category\":\"{1}\",\"severity\":\"{2}\",\"file\":\"{3}\",\"line\":{4},\"lineContent\":\"{5}\",\"description\":\"{6}\",\"suggestion\":\"{7}\"}}",
                ruleId, category, severity, Esc(file), line, Esc(lineContent), Esc(description), Esc(suggestion));
        }

        private static string FormatResult(string[] files, List<string> issues)
        {
            int critical = 0, warning = 0, info = 0;
            var byCategory = new Dictionary<string, int>();

            foreach (var issue in issues)
            {
                if (issue.Contains("\"critical\"")) critical++;
                else if (issue.Contains("\"warning\"")) warning++;
                else info++;

                // Count by category
                var catMatch = Regex.Match(issue, "\"category\":\"(\\w+)\"");
                if (catMatch.Success)
                {
                    string cat = catMatch.Groups[1].Value;
                    if (!byCategory.ContainsKey(cat)) byCategory[cat] = 0;
                    byCategory[cat]++;
                }
            }

            var catEntries = new List<string>();
            foreach (var kv in byCategory)
                catEntries.Add("\"" + kv.Key + "\":" + kv.Value);

            return string.Format(
                "{{\"success\":true,\"filesReviewed\":{0},\"issues\":[{1}],\"summary\":{{\"critical\":{2},\"warning\":{3},\"info\":{4},\"byCategory\":{{{5}}}}}}}",
                files.Length, string.Join(",", issues.ToArray()), critical, warning, info,
                string.Join(",", catEntries.ToArray()));
        }

        private static string Esc(string str)
        {
            if (str == null) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

        // Roslyn evaluation note: Regex-based analysis is a pragmatic starting point.
        // Roslyn would provide more accurate analysis for:
        // - Distinguishing string concatenation from numeric + operator (PERF005)
        // - Understanding type information for null checks (CORR001)
        // - Detecting actual MonoBehaviour inheritance across partial classes
        // - Analyzing cross-file dependencies for circular reference detection (ARCH003)
        // - Understanding control flow for more precise empty catch detection
    }
}
