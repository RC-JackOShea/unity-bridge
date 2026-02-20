# Challenge 25: Package Manager

## Overview

Build the PackageManager (Brief Tool #22) -- programmatic interface to list, search, install, remove, and update Unity packages with compatibility validation. Reads `Packages/manifest.json` and `packages-lock.json` directly for inventory, modifies `manifest.json` programmatically for install/remove/update operations, and can also leverage Unity's `UnityEditor.PackageManager.Client` API for registry searches and validation.

## Brief Reference

Section 9.1 (Package Management) -- List all installed packages from manifest.json and packages-lock.json with name, version, and source classification (registry/git/local/embedded). Search the Unity Package Manager registry for available packages. Install packages by registry name+version, Git URL, or local path. Remove and update packages by modifying manifest.json. Verify compatibility before installation.

## Problem Statement

Unity packages extend the engine's functionality (Input System, TextMeshPro, Netcode, Addressables, etc.). The agent needs to install required packages for features it is building, verify compatible versions, search for available packages, and manage the project's package dependencies -- all without the Unity Package Manager GUI. Two complementary approaches exist: direct manipulation of `Packages/manifest.json` (fast, predictable, works offline for install/remove/update) and the `UnityEditor.PackageManager.Client` API (provides registry search, version resolution, and dependency validation). The tool should combine both approaches for maximum capability.

## Success Criteria

1. `UnityBridge.PackageManagerTool.ListInstalled()` returns all installed packages with name, version, source (registry/git/local/embedded), and resolved path
2. Each package entry includes version and source classification -- distinguishing registry packages, Git URL packages, local file references, and embedded packages
3. `UnityBridge.PackageManagerTool.SearchRegistry(string query)` searches the Unity Package Manager registry for available packages matching the query string
4. `UnityBridge.PackageManagerTool.InstallPackage(string packageId, string version)` adds the package entry to manifest.json and triggers `AssetDatabase.Refresh()` to resolve
5. Installation supports Git URL packages (e.g., `"com.some.package": "https://github.com/user/repo.git#v1.0"`) by accepting a Git URL as the version parameter
6. `UnityBridge.PackageManagerTool.RemovePackage(string packageId)` removes the package entry from manifest.json
7. `UnityBridge.PackageManagerTool.UpdatePackage(string packageId, string targetVersion)` modifies the version string in manifest.json for an existing package
8. `UnityBridge.PackageManagerTool.CheckCompatibility(string packageId, string version)` verifies the package version is compatible with the current Unity version and does not conflict with installed packages
9. Handles already-installed packages gracefully -- `InstallPackage` on an already-installed package reports the existing installation rather than duplicating it, `RemovePackage` on a missing package returns a clear not-found message
10. All methods return structured JSON output with operation status and error details on failure

## Expected Development Work

### New Files

- **`Unity-Bridge/Editor/Tools/PackageManagerTool.cs`** -- Static class in the `UnityBridge` namespace (named to avoid conflict with Unity's `PackageManager` namespace). Two implementation approaches:
  - **Direct manifest editing**: Read/write `Packages/manifest.json` as JSON for install, remove, and update operations. Parse `packages-lock.json` for resolved version and source information. Simple, fast, and works offline.
  - **Unity Client API**: Use `UnityEditor.PackageManager.Client` -- `Client.List()`, `Client.Add()`, `Client.Remove()`, `Client.Search()`. These return `Request` objects that must be polled for completion via `request.IsCompleted`. Best for registry search and compatibility validation.
  - **Recommended hybrid**: Use direct manifest editing for install/remove/update (immediate, predictable), and the Client API for `SearchRegistry` and `CheckCompatibility` (requires registry access). Use `Client.Resolve()` or `AssetDatabase.Refresh()` after manifest modifications to trigger Unity's package resolution.

### JSON Output

**ListInstalled:**
```json
{
  "packages": [
    {
      "name": "com.unity.inputsystem",
      "version": "1.7.0",
      "source": "registry",
      "resolvedPath": "Library/PackageCache/com.unity.inputsystem@1.7.0"
    },
    {
      "name": "com.some.package",
      "version": "https://github.com/user/repo.git#v1.0",
      "source": "git",
      "resolvedPath": "Library/PackageCache/com.some.package@abc1234"
    },
    {
      "name": "com.my.package",
      "version": "file:../my-package",
      "source": "local",
      "resolvedPath": "../my-package"
    }
  ],
  "total": 15
}
```

**SearchRegistry:**
```json
{
  "query": "input",
  "results": [
    {
      "name": "com.unity.inputsystem",
      "displayName": "Input System",
      "latestVersion": "1.7.0",
      "description": "A new input system which can be used as a more extensible and customizable alternative to Unity's classic input system."
    }
  ],
  "totalResults": 3
}
```

**InstallPackage / RemovePackage / UpdatePackage:**
```json
{
  "success": true,
  "operation": "install",
  "packageId": "com.unity.textmeshpro",
  "version": "3.0.6",
  "message": "Package added to manifest.json. AssetDatabase.Refresh() triggered."
}
```

**CheckCompatibility:**
```json
{
  "packageId": "com.unity.textmeshpro",
  "version": "3.0.6",
  "compatible": true,
  "unityVersion": "2022.3.20f1",
  "conflicts": [],
  "message": "Package is compatible with the current Unity version."
}
```

### Package Source Classification

When reading `manifest.json` and `packages-lock.json`, classify each package source:
- **registry** -- version is a semver string and source in lock file is `"registry"` (e.g., `"com.unity.inputsystem": "1.7.0"`)
- **git** -- version is a URL starting with `https://` or `git://` (e.g., `"com.some.package": "https://github.com/user/repo.git#v1.0"`)
- **local** -- version starts with `file:` (e.g., `"com.my.package": "file:../my-package"`)
- **embedded** -- package directory exists directly under `Packages/` (not in the lock file as registry/git)

## Testing Protocol

1. `bash .agent/tools/unity_bridge.sh compile` -- Read `C:/temp/unity_bridge_output.txt`, confirm compilation succeeds.
2. `bash .agent/tools/unity_bridge.sh execute UnityBridge.PackageManagerTool.ListInstalled` -- Read output, verify all installed packages are listed with name, version, source, and resolvedPath. Cross-check against `Packages/manifest.json`.
3. `bash .agent/tools/unity_bridge.sh execute UnityBridge.PackageManagerTool.SearchRegistry '["input"]'` -- Read output, verify registry search returns results including `com.unity.inputsystem`.
4. `bash .agent/tools/unity_bridge.sh execute UnityBridge.PackageManagerTool.CheckCompatibility '["com.unity.textmeshpro", "3.0.6"]'` -- Read output, verify compatibility response with current Unity version.
5. `bash .agent/tools/unity_bridge.sh execute UnityBridge.PackageManagerTool.InstallPackage '["com.unity.ai.navigation", "1.1.5"]'` -- Read output, verify success. Then run `ListInstalled` to confirm the package appears.
6. `bash .agent/tools/unity_bridge.sh execute UnityBridge.PackageManagerTool.RemovePackage '["com.unity.ai.navigation"]'` -- Read output, verify success. Then run `ListInstalled` to confirm the package is gone.
7. `bash .agent/tools/unity_bridge.sh execute UnityBridge.PackageManagerTool.UpdatePackage '["com.unity.inputsystem", "1.8.0"]'` -- Read output, verify version change (revert afterward if needed).
8. Test already-installed handling: `bash .agent/tools/unity_bridge.sh execute UnityBridge.PackageManagerTool.InstallPackage '["com.unity.inputsystem", "1.7.0"]'` -- Verify graceful response indicating already installed.
9. Test not-found handling: `bash .agent/tools/unity_bridge.sh execute UnityBridge.PackageManagerTool.RemovePackage '["com.nonexistent.package"]'` -- Verify structured error response.

## Dependencies

- **Challenge 01 (Execute Endpoint)** -- All methods are invoked via `bash .agent/tools/unity_bridge.sh execute UnityBridge.PackageManagerTool.<Method>`.
