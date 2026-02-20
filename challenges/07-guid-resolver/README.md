# Challenge 07: GUID Resolver & Meta Cache

## Overview

Build the GUIDResolver (Brief Tool #8) — given a GUID, resolve it to an asset path by scanning `.meta` files. Maintain a cached lookup table for speed. This enables cross-reference resolution needed by YAML parsing and asset dependency tracking.

## Brief Reference

Section 4.3 (YAML-Level Access) — "GUIDResolver — Given a GUID, resolves it to an asset path by scanning .meta files. Maintains a cached lookup table for speed." Also Section 4.1 — "What assets does the scene reference? Parse the .unity YAML file for GUID references, cross-reference against .meta files to resolve asset paths."

## Problem Statement

Unity uses GUIDs (32-character hex strings stored in `.meta` files) as the primary mechanism for asset cross-referencing. Scene files (`.unity`) reference materials by GUID. Scripts reference prefabs by GUID. Prefab instances reference their source by GUID. Without GUID resolution, the agent cannot follow these references to understand how assets are connected — a material GUID in a scene file is meaningless without knowing it maps to `Assets/Materials/PlayerMat.mat`. This tool is the foundation for dependency analysis, asset graph construction, and YAML-level scene parsing.

## Success Criteria

1. `UnityBridge.GUIDResolver.BuildCache()` scans all `.meta` files and builds an in-memory `Dictionary<string, string>` mapping GUID to asset path. Returns JSON with cache statistics.
2. `UnityBridge.GUIDResolver.Resolve(string guid)` returns the asset path for a single GUID as JSON: `{"guid": "...", "assetPath": "...", "assetType": "..."}`
3. `UnityBridge.GUIDResolver.ResolveMultiple(string guidsJson)` accepts a JSON array of GUID strings and returns an array of resolution results in a single call
4. `UnityBridge.GUIDResolver.GetCacheStats()` returns: cache entry count, last build timestamp, build duration in milliseconds
5. Cache is built automatically on first use (lazy initialization) — if `Resolve` is called before `BuildCache`, the cache builds itself
6. Cache can be explicitly rebuilt on demand via `BuildCache()` (e.g., after adding new assets)
7. Resolves folder `.meta` files as well as asset `.meta` files
8. Returns a clear null/not-found response for GUIDs that do not match any asset: `{"guid": "...", "assetPath": null, "error": "GUID not found in cache"}`
9. Performance: resolving a single GUID takes less than 1ms after the cache is built
10. Cache building handles large projects (10,000+ `.meta` files) without timeout or excessive memory usage

## Expected Development Work

### New Files

- **`Unity-Bridge/Editor/Tools/GUIDResolver.cs`** — Namespace: `UnityBridge`. Contains:
  - `private static Dictionary<string, string> _cache` — The GUID-to-path mapping
  - `private static DateTime _lastBuildTime` — Timestamp of last cache build
  - `private static long _buildDurationMs` — How long the last build took
  - `public static string BuildCache()` — Two implementation approaches:
    - **Primary (recommended):** Use `AssetDatabase.FindAssets("")` to get all asset GUIDs, then `AssetDatabase.GUIDToAssetPath()` for each. This leverages Unity's built-in database and is fast.
    - **Fallback (for external tools):** Scan all `.meta` files under `Assets/` and `Packages/`, parse the `guid:` YAML line from each, build the dictionary manually. Also writes a JSON cache file to `Library/guid_cache.json` for external Python/shell tools to use.
  - `public static string Resolve(string guid)` — Looks up the GUID in the cache. If cache is empty, calls `BuildCache()` first (lazy init). Returns JSON with asset path and type.
  - `public static string ResolveMultiple(string guidsJson)` — Parses the JSON array, resolves each GUID, returns array of results.
  - `public static string GetCacheStats()` — Returns cache metadata as JSON.
  - `private static string GetAssetType(string assetPath)` — Determines asset type from file extension or by loading the asset type via `AssetDatabase.GetMainAssetTypeAtPath()`.

### JSON Output Formats

**BuildCache response:**
```json
{
  "success": true,
  "cacheSize": 1523,
  "buildDurationMs": 45,
  "timestamp": "2026-02-19T10:30:00Z"
}
```

**Resolve response (found):**
```json
{
  "guid": "a1b2c3d4e5f67890a1b2c3d4e5f67890",
  "assetPath": "Assets/Materials/PlayerMat.mat",
  "assetType": "Material"
}
```

**Resolve response (not found):**
```json
{
  "guid": "0000000000000000000000000000dead",
  "assetPath": null,
  "assetType": null,
  "error": "GUID not found in cache. It may be a built-in Unity resource or the cache may need rebuilding."
}
```

**ResolveMultiple response:**
```json
{
  "results": [
    {"guid": "abc123...", "assetPath": "Assets/Scripts/Player.cs", "assetType": "MonoScript"},
    {"guid": "def456...", "assetPath": null, "assetType": null, "error": "GUID not found in cache"}
  ],
  "resolved": 1,
  "unresolved": 1,
  "total": 2
}
```

**GetCacheStats response:**
```json
{
  "cacheSize": 1523,
  "lastBuildTime": "2026-02-19T10:30:00Z",
  "buildDurationMs": 45,
  "isCacheBuilt": true
}
```

### Key Implementation Details

- **AssetDatabase approach (primary):** `AssetDatabase.FindAssets("")` returns ALL asset GUIDs in the project. For each GUID, `AssetDatabase.GUIDToAssetPath(guid)` returns the path. This is the fastest and most reliable approach since Unity already maintains this mapping internally.
- **Manual .meta scan (fallback):** Use `Directory.GetFiles("Assets", "*.meta", SearchOption.AllDirectories)` to find all `.meta` files. Each `.meta` file's second line is typically `guid: <32-char-hex>`. Parse this with a simple regex or string split. The corresponding asset path is the `.meta` file path with the `.meta` extension removed.
- **Asset type detection:** Use `AssetDatabase.GetMainAssetTypeAtPath(path)` to get the `System.Type`, then return `type.Name` (e.g., "Material", "MonoScript", "Texture2D", "Prefab").
- **Built-in Unity GUIDs:** Some GUIDs reference Unity's built-in resources (default materials, built-in shaders). These may not resolve via `AssetDatabase.GUIDToAssetPath()`. Return a descriptive error noting this possibility.
- **Thread safety:** The cache dictionary may be accessed from the main thread during Unity operations. Since all bridge calls execute on the main thread, explicit locking is not required, but note this assumption in comments.
- **External cache file:** Optionally write the cache to `Library/guid_cache.json` for external tools. The `Library/` directory is Unity's project-local cache and is not version-controlled.

## Testing Protocol

1. `bash .agent/tools/unity_bridge.sh compile` — Read `C:/temp/unity_bridge_output.txt`, confirm compilation succeeded
2. Build the cache: `bash .agent/tools/unity_bridge.sh execute UnityBridge.GUIDResolver.BuildCache` — Read output, verify success and cache size > 0
3. Check cache stats: `bash .agent/tools/unity_bridge.sh execute UnityBridge.GUIDResolver.GetCacheStats` — Read output, verify `isCacheBuilt: true` and reasonable `cacheSize`
4. Find a known GUID to test resolution: read any `.meta` file in the project (e.g., `Assets/Scenes/SampleScene.unity.meta`) and extract the `guid:` value
5. Resolve the known GUID: `bash .agent/tools/unity_bridge.sh execute UnityBridge.GUIDResolver.Resolve '["<extracted-guid>"]'` — Read output, verify the correct asset path is returned
6. Test with an invalid/nonexistent GUID: `bash .agent/tools/unity_bridge.sh execute UnityBridge.GUIDResolver.Resolve '["00000000deadbeef00000000deadbeef"]'` — Read output, verify error response with null path
7. Test ResolveMultiple with an array of 2-3 GUIDs (mix of valid and invalid): `bash .agent/tools/unity_bridge.sh execute UnityBridge.GUIDResolver.ResolveMultiple '["[\"<guid1>\", \"<guid2>\", \"invalidguid\"]"]'` — Read output, verify mixed results
8. Test lazy initialization: if possible, restart the bridge and call `Resolve` directly without calling `BuildCache` first — verify the cache auto-builds

## Dependencies

- **Challenge 01 (Execute Endpoint)** — methods are called via the execute endpoint
