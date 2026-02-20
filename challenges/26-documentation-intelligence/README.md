# Challenge 26: Documentation Intelligence

## Overview

Build the DocFetcher (Brief Tool #23) -- retrieves and returns structured documentation summaries optimized for agent consumption. Given a Unity API class name, an installed package name, or a subsystem topic, the tool returns concise, token-efficient structured data rather than raw markdown or HTML. Enables the agent to look up unfamiliar APIs, read package documentation, review best practices, and compare alternative approaches without consuming tokens on full web pages or unstructured text.

## Brief Reference

Section 9.2 (Documentation Comprehension) -- Retrieve Unity API documentation for classes via reflection and local docs. Read package README.md, CHANGELOG.md, and Documentation~ folders from installed packages. Return curated best practices for major subsystems (Input System, Addressables, Netcode, etc.). Compare alternative approaches (old vs new Input System, UGUI vs UI Toolkit) with structured pros/cons/recommendation.

## Problem Statement

The agent's training data has a knowledge cutoff and does not include documentation for all Unity API versions, third-party packages, or recently added subsystems. When the agent encounters an unfamiliar API or needs to verify correct usage patterns, it needs structured, concise documentation -- not raw HTML from web pages or unformatted README files. The DocFetcher provides four capabilities: (1) API documentation via reflection on loaded assemblies and local Unity docs, (2) package documentation by reading files from the package cache, (3) curated best practices for common subsystems, and (4) structured comparisons when multiple approaches exist for the same task. All output is JSON-structured and token-efficient.

## Success Criteria

1. `UnityBridge.DocFetcher.GetUnityAPIDocs(string className)` returns a structured summary of a Unity API class including namespace, inheritance chain, public methods with signatures, and public properties
2. `UnityBridge.DocFetcher.GetPackageDocs(string packageName)` reads README.md from the installed package's directory (in `Library/PackageCache/` or `Packages/`) and returns its content in a structured format
3. Package documentation extraction also reads CHANGELOG.md when present and returns the most recent entries
4. Documentation from the `Documentation~/` folder inside package directories is discovered and returned when available
5. `UnityBridge.DocFetcher.GetBestPractices(string subsystem)` returns structured best practices for at least 3 subsystems (e.g., Input System, Addressables, Netcode)
6. `UnityBridge.DocFetcher.CompareApproaches(string topic)` returns a structured comparison with pros, cons, and recommendation for at least 2 topics (e.g., old Input Manager vs new Input System, UGUI vs UI Toolkit)
7. Handles missing documentation gracefully -- when a class is not found, a package is not installed, or a subsystem has no curated best practices, the tool returns a clear structured message rather than an exception
8. All output is structured JSON, not raw markdown -- documentation text is organized into labeled fields (description, methods, properties, sections) for programmatic consumption
9. Results are cached for repeated queries -- calling `GetUnityAPIDocs("Transform")` twice does not re-reflect the type or re-parse files
10. Output is token-efficient -- method signatures are concise (no XML doc verbosity), README content is summarized or truncated to key sections, best practices are bullet points not essays

## Expected Development Work

### New Files

- **`Unity-Bridge/Editor/Tools/DocFetcher.cs`** -- Static class in the `UnityBridge` namespace. Four main subsystems:

  **API Documentation (reflection-based)**: Use `System.Type` inspection to extract class information from loaded assemblies. Resolve class names across Unity assemblies (`UnityEngine`, `UnityEditor`, etc.) using `AppDomain.CurrentDomain.GetAssemblies()`. Extract `Type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)`, `Type.GetProperties()`, inheritance chain via `Type.BaseType`. Optionally parse XML documentation comments from Unity assemblies if the XML doc files exist at `{EditorApplication.applicationContentsPath}/Data/Managed/UnityEngine.xml` or similar paths.

  **Package Documentation (file-based)**: Resolve the installed package path using the Package Manager API or by scanning `Library/PackageCache/{packageName}@*/` and `Packages/{packageName}/`. Read `README.md`, `CHANGELOG.md`, and any files under `Documentation~/`. Depends on Challenge 25 (PackageManagerTool) for reliable package path resolution via `ListInstalled()`.

  **Best Practices (curated data)**: Maintain a structured dictionary of best practices for common Unity subsystems. At minimum: Input System (action maps, binding paths, PlayerInput component usage), Addressables (asset loading patterns, memory management, build settings), Netcode for GameObjects (NetworkObject setup, RPCs, NetworkVariable patterns). These can be embedded as static data in the tool or loaded from a JSON resource file.

  **Approach Comparisons (curated data)**: Maintain structured comparison templates for common Unity decision points. At minimum: old Input Manager vs new Input System, UGUI (Canvas/Image/Button) vs UI Toolkit (VisualElement/USS). Each comparison includes pros, cons, and a recommendation. Can be extended with additional topics.

### JSON Output

**GetUnityAPIDocs:**
```json
{
  "className": "Transform",
  "namespace": "UnityEngine",
  "inheritsFrom": ["Component", "Object"],
  "description": "Position, rotation, and scale of an object.",
  "methods": [
    {"name": "Translate", "parameters": [{"name": "translation", "type": "Vector3"}, {"name": "relativeTo", "type": "Space"}], "returnType": "void"},
    {"name": "Rotate", "parameters": [{"name": "eulers", "type": "Vector3"}, {"name": "relativeTo", "type": "Space"}], "returnType": "void"},
    {"name": "LookAt", "parameters": [{"name": "target", "type": "Transform"}], "returnType": "void"}
  ],
  "properties": [
    {"name": "position", "type": "Vector3", "canRead": true, "canWrite": true},
    {"name": "rotation", "type": "Quaternion", "canRead": true, "canWrite": true},
    {"name": "localScale", "type": "Vector3", "canRead": true, "canWrite": true},
    {"name": "childCount", "type": "int", "canRead": true, "canWrite": false}
  ]
}
```

**GetPackageDocs:**
```json
{
  "packageName": "com.unity.inputsystem",
  "version": "1.7.0",
  "readmeSummary": "The Input System package implements a system to use any kind of Input Device to control your Unity content...",
  "changelogRecent": [
    {"version": "1.7.0", "summary": "Added support for ..."},
    {"version": "1.6.3", "summary": "Fixed ..."}
  ],
  "documentationFiles": [
    "Documentation~/InputSystem.md",
    "Documentation~/Actions.md",
    "Documentation~/Devices.md"
  ],
  "resolvedPath": "Library/PackageCache/com.unity.inputsystem@1.7.0"
}
```

**GetBestPractices:**
```json
{
  "subsystem": "Input System",
  "practices": [
    "Use Input Action Assets (.inputactions) rather than hardcoded bindings for rebindable controls",
    "Use PlayerInput component for automatic action map switching between gameplay and UI contexts",
    "Use InputAction.ReadValue<T>() in Update, not callbacks, for frame-consistent input reads",
    "Set up control schemes (Keyboard+Mouse, Gamepad) to enable automatic device switching",
    "Use InputSystem.onDeviceChange to handle device connect/disconnect at runtime"
  ],
  "commonPitfalls": [
    "Forgetting to enable action maps -- actions do not fire unless their parent map is enabled",
    "Using both old and new input systems simultaneously without setting Active Input Handling to 'Both' in Player Settings",
    "Not disposing InputAction instances created at runtime"
  ]
}
```

**CompareApproaches:**
```json
{
  "topic": "UGUI vs UI Toolkit",
  "optionA": {
    "name": "UGUI (Canvas-based)",
    "pros": [
      "Mature and well-documented",
      "Full visual editor with drag-and-drop",
      "Extensive third-party asset support",
      "Works in both editor and runtime"
    ],
    "cons": [
      "Canvas rebuild performance issues at scale",
      "No CSS-like styling system",
      "Difficult to version-control complex layouts"
    ]
  },
  "optionB": {
    "name": "UI Toolkit (VisualElement/USS)",
    "pros": [
      "CSS-like styling with USS",
      "Better performance for complex UIs (retained mode)",
      "UXML layouts are human-readable and diff-friendly",
      "Shared paradigm between editor UI and runtime UI"
    ],
    "cons": [
      "Runtime support still maturing",
      "Fewer third-party assets and examples",
      "No built-in world-space UI support yet"
    ]
  },
  "recommendation": "Use UGUI for projects needing world-space UI, extensive asset store support, or targeting platforms where UI Toolkit runtime is not yet stable. Use UI Toolkit for editor tools, HUD-style screen-space UI, and new projects that benefit from CSS-like styling."
}
```

## Testing Protocol

1. `bash .agent/tools/unity_bridge.sh compile` -- Read `C:/temp/unity_bridge_output.txt`, confirm compilation succeeds.
2. `bash .agent/tools/unity_bridge.sh execute UnityBridge.DocFetcher.GetUnityAPIDocs '["MonoBehaviour"]'` -- Read output, verify methods (Start, Update, Awake, etc.) and properties are listed with correct signatures.
3. `bash .agent/tools/unity_bridge.sh execute UnityBridge.DocFetcher.GetUnityAPIDocs '["Transform"]'` -- Read output, verify methods (Translate, Rotate, LookAt) and properties (position, rotation, localScale).
4. `bash .agent/tools/unity_bridge.sh execute UnityBridge.DocFetcher.GetPackageDocs '["com.unity.inputsystem"]'` -- Read output, verify README content is returned and documentation files are listed.
5. `bash .agent/tools/unity_bridge.sh execute UnityBridge.DocFetcher.GetBestPractices '["Input System"]'` -- Read output, verify structured practices and pitfalls are returned.
6. `bash .agent/tools/unity_bridge.sh execute UnityBridge.DocFetcher.CompareApproaches '["UGUI vs UI Toolkit"]'` -- Read output, verify two options with pros/cons and a recommendation.
7. Test missing class handling: `bash .agent/tools/unity_bridge.sh execute UnityBridge.DocFetcher.GetUnityAPIDocs '["NonExistentClassName"]'` -- Verify graceful structured error response.
8. Test uninstalled package handling: `bash .agent/tools/unity_bridge.sh execute UnityBridge.DocFetcher.GetPackageDocs '["com.nonexistent.package"]'` -- Verify structured error response.
9. Test cache behavior: run `GetUnityAPIDocs '["Camera"]'` twice and verify the second call returns quickly from cache.

## Dependencies

- **Challenge 01 (Execute Endpoint)** -- All methods are invoked via `bash .agent/tools/unity_bridge.sh execute UnityBridge.DocFetcher.<Method>`.
- **Challenge 25 (Package Manager)** -- `GetPackageDocs` depends on PackageManagerTool for reliable package path resolution. `ListInstalled()` provides the `resolvedPath` for each installed package, which DocFetcher uses to locate README.md, CHANGELOG.md, and Documentation~ folders.
