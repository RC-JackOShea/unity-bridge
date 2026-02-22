using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace UnityBridge
{
    /// <summary>
    /// Documentation intelligence: retrieves structured API docs via reflection,
    /// reads package documentation (README, CHANGELOG, Documentation~),
    /// provides curated best practices, and compares alternative approaches.
    /// All output is JSON-structured and token-efficient.
    /// </summary>
    public static class DocFetcher
    {
        private static Dictionary<string, string> apiCache = new Dictionary<string, string>();

        public static string GetUnityAPIDocs(string className)
        {
            if (string.IsNullOrEmpty(className))
                return "{\"success\":false,\"error\":\"className is required\"}";

            if (apiCache.ContainsKey(className))
                return apiCache[className];

            try
            {
                Type type = ResolveType(className);
                if (type == null)
                    return "{\"success\":false,\"error\":\"Class not found: " + Esc(className) + "\"}";

                // Inheritance chain
                var inheritanceChain = new List<string>();
                var baseType = type.BaseType;
                while (baseType != null && baseType != typeof(object))
                {
                    inheritanceChain.Add(baseType.Name);
                    baseType = baseType.BaseType;
                }

                // Methods (public instance + static, excluding get_/set_ accessors and Object methods)
                var methods = new List<string>();
                var seenMethods = new HashSet<string>();
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (method.IsSpecialName) continue; // skip property accessors
                    if (seenMethods.Contains(method.Name)) continue;
                    seenMethods.Add(method.Name);

                    var paramEntries = new List<string>();
                    foreach (var p in method.GetParameters())
                    {
                        paramEntries.Add(string.Format("{{\"name\":\"{0}\",\"type\":\"{1}\"}}", Esc(p.Name), Esc(FormatTypeName(p.ParameterType))));
                    }

                    methods.Add(string.Format(
                        "{{\"name\":\"{0}\",\"parameters\":[{1}],\"returnType\":\"{2}\",\"isStatic\":{3}}}",
                        Esc(method.Name), string.Join(",", paramEntries.ToArray()),
                        Esc(FormatTypeName(method.ReturnType)), method.IsStatic ? "true" : "false"));
                }

                // Properties (public instance + static)
                var properties = new List<string>();
                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    properties.Add(string.Format(
                        "{{\"name\":\"{0}\",\"type\":\"{1}\",\"canRead\":{2},\"canWrite\":{3}}}",
                        Esc(prop.Name), Esc(FormatTypeName(prop.PropertyType)),
                        prop.CanRead ? "true" : "false", prop.CanWrite ? "true" : "false"));
                }

                var inheritEntries = new List<string>();
                foreach (var inh in inheritanceChain) inheritEntries.Add("\"" + Esc(inh) + "\"");

                string result = string.Format(
                    "{{\"success\":true,\"className\":\"{0}\",\"namespace\":\"{1}\",\"inheritsFrom\":[{2}],\"methods\":[{3}],\"properties\":[{4}],\"methodCount\":{5},\"propertyCount\":{6}}}",
                    Esc(type.Name), Esc(type.Namespace ?? ""),
                    string.Join(",", inheritEntries.ToArray()),
                    string.Join(",", methods.ToArray()),
                    string.Join(",", properties.ToArray()),
                    methods.Count, properties.Count);

                apiCache[className] = result;
                return result;
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        public static string GetPackageDocs(string packageName)
        {
            if (string.IsNullOrEmpty(packageName))
                return "{\"success\":false,\"error\":\"packageName is required\"}";

            try
            {
                // Find package path via PackageManagerTool's ListInstalled or direct search
                string packagePath = FindPackagePath(packageName);
                if (string.IsNullOrEmpty(packagePath))
                    return "{\"success\":false,\"error\":\"Package not installed or path not found: " + Esc(packageName) + "\"}";

                string readmeSummary = "";
                string readmePath = Path.Combine(packagePath, "README.md");
                if (File.Exists(readmePath))
                {
                    string content = File.ReadAllText(readmePath);
                    readmeSummary = content.Length > 2000 ? content.Substring(0, 2000) + "..." : content;
                }

                // Changelog
                var changelogEntries = new List<string>();
                string changelogPath = Path.Combine(packagePath, "CHANGELOG.md");
                if (File.Exists(changelogPath))
                {
                    string clContent = File.ReadAllText(changelogPath);
                    var versionMatches = Regex.Matches(clContent, @"##\s*\[?(\d+\.\d+\.\d+[^\]]*)\]?[^\n]*\n([^#]*)", RegexOptions.Multiline);
                    for (int i = 0; i < Math.Min(3, versionMatches.Count); i++)
                    {
                        string ver = versionMatches[i].Groups[1].Value.Trim();
                        string summary = versionMatches[i].Groups[2].Value.Trim();
                        if (summary.Length > 200) summary = summary.Substring(0, 200) + "...";
                        changelogEntries.Add(string.Format("{{\"version\":\"{0}\",\"summary\":\"{1}\"}}", Esc(ver), Esc(summary)));
                    }
                }

                // Documentation files
                var docFiles = new List<string>();
                string docsDir = Path.Combine(packagePath, "Documentation~");
                if (Directory.Exists(docsDir))
                {
                    foreach (var file in Directory.GetFiles(docsDir, "*.md"))
                    {
                        docFiles.Add("\"" + Esc(Path.GetFileName(file)) + "\"");
                    }
                }

                // Get version from package.json
                string version = "";
                string pkgJsonPath = Path.Combine(packagePath, "package.json");
                if (File.Exists(pkgJsonPath))
                {
                    try
                    {
                        var pkgJson = SimpleJson.Parse(File.ReadAllText(pkgJsonPath));
                        version = pkgJson.GetString("version") ?? "";
                    }
                    catch { }
                }

                return string.Format(
                    "{{\"success\":true,\"packageName\":\"{0}\",\"version\":\"{1}\",\"readmeSummary\":\"{2}\",\"changelogRecent\":[{3}],\"documentationFiles\":[{4}],\"resolvedPath\":\"{5}\"}}",
                    Esc(packageName), Esc(version), Esc(readmeSummary),
                    string.Join(",", changelogEntries.ToArray()),
                    string.Join(",", docFiles.ToArray()),
                    Esc(packagePath.Replace("\\", "/")));
            }
            catch (Exception e)
            {
                return "{\"success\":false,\"error\":\"" + Esc(e.Message) + "\"}";
            }
        }

        public static string GetBestPractices(string subsystem)
        {
            if (string.IsNullOrEmpty(subsystem))
                return "{\"success\":false,\"error\":\"subsystem is required\"}";

            string key = subsystem.ToLowerInvariant().Replace(" ", "");

            switch (key)
            {
                case "inputsystem":
                    return FormatBestPractices("Input System",
                        new[] {
                            "Use Input Action Assets (.inputactions) rather than hardcoded bindings for rebindable controls",
                            "Use PlayerInput component for automatic action map switching between gameplay and UI contexts",
                            "Use InputAction.ReadValue<T>() in Update, not callbacks, for frame-consistent input reads",
                            "Set up control schemes (Keyboard+Mouse, Gamepad) to enable automatic device switching",
                            "Use InputSystem.onDeviceChange to handle device connect/disconnect at runtime"
                        },
                        new[] {
                            "Forgetting to enable action maps -- actions do not fire unless their parent map is enabled",
                            "Using both old and new input systems simultaneously without setting Active Input Handling to Both",
                            "Not disposing InputAction instances created at runtime",
                            "Using SendMessage mode for PlayerInput events instead of UnityEvents or C# events"
                        });

                case "addressables":
                    return FormatBestPractices("Addressables",
                        new[] {
                            "Use AssetReference fields instead of direct references for large assets to reduce initial load time",
                            "Release assets with Addressables.Release() when no longer needed to free memory",
                            "Use labels to group assets for batch loading (e.g., all assets for a level)",
                            "Configure remote build paths for assets that should be downloaded post-install",
                            "Use Addressables.LoadSceneAsync() for additive scene loading with automatic dependency management"
                        },
                        new[] {
                            "Forgetting to release loaded assets causes memory leaks",
                            "Loading the same asset multiple times without caching the handle",
                            "Not handling load failures -- always check AsyncOperationHandle.Status",
                            "Mixing direct references and Addressable references for the same asset"
                        });

                case "netcode":
                case "netcodefor":
                case "netcodeforgameobjects":
                    return FormatBestPractices("Netcode for GameObjects",
                        new[] {
                            "Place NetworkObject component on the root of every networked prefab",
                            "Use NetworkVariable<T> for state that needs automatic synchronization",
                            "Use ServerRpc for client-to-server communication, ClientRpc for server-to-client",
                            "Mark NetworkVariable changes as server-authoritative -- clients read, server writes",
                            "Use NetworkManager.Singleton.OnClientConnectedCallback for connection handling"
                        },
                        new[] {
                            "Modifying NetworkVariables on the client side -- only the server should write",
                            "Not registering prefabs in the NetworkManager's NetworkPrefabs list",
                            "Using MonoBehaviour.Destroy instead of NetworkObject.Despawn for networked objects",
                            "Forgetting to call base.OnNetworkSpawn() in overridden methods"
                        });

                default:
                    return "{\"success\":false,\"error\":\"No best practices available for: " + Esc(subsystem) + ". Available: Input System, Addressables, Netcode\"}";
            }
        }

        public static string CompareApproaches(string topic)
        {
            if (string.IsNullOrEmpty(topic))
                return "{\"success\":false,\"error\":\"topic is required\"}";

            string key = topic.ToLowerInvariant().Replace(" ", "");

            if (key.Contains("ugui") && key.Contains("uitoolkit") || key.Contains("canvasvs"))
            {
                return FormatComparison("UGUI vs UI Toolkit",
                    "UGUI (Canvas-based)",
                    new[] { "Mature and well-documented", "Full visual editor with drag-and-drop", "Extensive third-party asset support", "Works in both editor and runtime", "World-space UI support" },
                    new[] { "Canvas rebuild performance issues at scale", "No CSS-like styling system", "Difficult to version-control complex layouts", "Deep hierarchy nesting for complex UIs" },
                    "UI Toolkit (VisualElement/USS)",
                    new[] { "CSS-like styling with USS", "Better performance for complex UIs (retained mode)", "UXML layouts are human-readable and diff-friendly", "Shared paradigm between editor UI and runtime UI" },
                    new[] { "Runtime support still maturing", "Fewer third-party assets and examples", "No built-in world-space UI support yet", "Learning curve for developers coming from UGUI" },
                    "Use UGUI for projects needing world-space UI, extensive asset store support, or targeting platforms where UI Toolkit runtime is not yet stable. Use UI Toolkit for editor tools, HUD-style screen-space UI, and new projects that benefit from CSS-like styling.");
            }

            if (key.Contains("input") && (key.Contains("old") || key.Contains("new") || key.Contains("manager") || key.Contains("system")))
            {
                return FormatComparison("Old Input Manager vs New Input System",
                    "Old Input Manager",
                    new[] { "Simple API (Input.GetAxis, Input.GetKeyDown)", "No package dependency required", "Extensive community tutorials and examples", "Zero setup for basic input" },
                    new[] { "No rebinding support at runtime", "Polling-based only (no event-driven option)", "Limited to predefined axis/button names", "No multi-device or multi-player support" },
                    "New Input System",
                    new[] { "Action-based with runtime rebinding", "Event-driven and polling APIs", "Multi-device and multi-player support", "Input Action Assets for designer-friendly configuration", "Better testability and simulation" },
                    new[] { "More complex initial setup", "Requires package installation", "Learning curve for action maps and bindings", "Some features still evolving" },
                    "Use the New Input System for any project that needs runtime rebinding, gamepad support, multi-player input, or event-driven input handling. Use Old Input Manager only for quick prototypes or legacy projects where migration cost is not justified.");
            }

            return "{\"success\":false,\"error\":\"No comparison available for: " + Esc(topic) + ". Available: UGUI vs UI Toolkit, Old Input Manager vs New Input System\"}";
        }

        private static string FormatBestPractices(string subsystem, string[] practices, string[] pitfalls)
        {
            var practiceEntries = new List<string>();
            foreach (var p in practices) practiceEntries.Add("\"" + Esc(p) + "\"");
            var pitfallEntries = new List<string>();
            foreach (var p in pitfalls) pitfallEntries.Add("\"" + Esc(p) + "\"");

            return string.Format(
                "{{\"success\":true,\"subsystem\":\"{0}\",\"practices\":[{1}],\"commonPitfalls\":[{2}]}}",
                Esc(subsystem),
                string.Join(",", practiceEntries.ToArray()),
                string.Join(",", pitfallEntries.ToArray()));
        }

        private static string FormatComparison(string topic, string nameA, string[] prosA, string[] consA,
            string nameB, string[] prosB, string[] consB, string recommendation)
        {
            return string.Format(
                "{{\"success\":true,\"topic\":\"{0}\",\"optionA\":{{\"name\":\"{1}\",\"pros\":[{2}],\"cons\":[{3}]}},\"optionB\":{{\"name\":\"{4}\",\"pros\":[{5}],\"cons\":[{6}]}},\"recommendation\":\"{7}\"}}",
                Esc(topic), Esc(nameA), FormatStringArray(prosA), FormatStringArray(consA),
                Esc(nameB), FormatStringArray(prosB), FormatStringArray(consB), Esc(recommendation));
        }

        private static string FormatStringArray(string[] items)
        {
            var entries = new List<string>();
            foreach (var item in items) entries.Add("\"" + Esc(item) + "\"");
            return string.Join(",", entries.ToArray());
        }

        private static string FindPackagePath(string packageName)
        {
            // Check Packages/ directory (local packages, git packages)
            string packagesDir = Path.Combine(Application.dataPath, "..", "Packages");
            string directPath = Path.Combine(packagesDir, packageName);
            if (Directory.Exists(directPath)) return directPath;

            // Check Library/PackageCache/
            string cacheDir = Path.Combine(Application.dataPath, "..", "Library", "PackageCache");
            if (Directory.Exists(cacheDir))
            {
                foreach (var dir in Directory.GetDirectories(cacheDir))
                {
                    string dirName = Path.GetFileName(dir);
                    if (dirName.StartsWith(packageName + "@"))
                        return dir;
                }
            }

            return null;
        }

        private static Type ResolveType(string name)
        {
            // Try common Unity types first
            string[] modules = { "UnityEngine.CoreModule", "UnityEngine.PhysicsModule",
                "UnityEngine.UIModule", "UnityEngine.AudioModule", "UnityEngine.AnimationModule",
                "UnityEngine.ParticleSystemModule", "UnityEngine.IMGUIModule" };

            foreach (var mod in modules)
            {
                var type = Type.GetType("UnityEngine." + name + ", " + mod);
                if (type != null) return type;
            }

            // Try UnityEditor
            var edType = Type.GetType("UnityEditor." + name + ", UnityEditor.CoreModule");
            if (edType != null) return edType;

            // Search all assemblies
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var t in asm.GetTypes())
                {
                    if (t.Name == name || t.FullName == name)
                        return t;
                }
            }
            return null;
        }

        private static string FormatTypeName(Type type)
        {
            if (type == typeof(void)) return "void";
            if (type == typeof(int)) return "int";
            if (type == typeof(float)) return "float";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(string)) return "string";
            if (type == typeof(double)) return "double";
            if (type.IsGenericType)
            {
                string baseName = type.Name.Split('`')[0];
                var args = new List<string>();
                foreach (var arg in type.GetGenericArguments())
                    args.Add(FormatTypeName(arg));
                return baseName + "<" + string.Join(",", args.ToArray()) + ">";
            }
            return type.Name;
        }

        private static string Esc(string str)
        {
            if (str == null) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
