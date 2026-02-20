# Challenge 02: Scene Inventory

## Overview

Build the SceneInventoryTool (Brief Tool #1) — an editor script that programmatically discovers all scenes in the project, identifies which are in the build, and extracts the full GameObject hierarchy with component lists for each scene. This is the first real introspection tool and establishes the pattern all subsequent tools will follow.

## Brief Reference

Section 4.1 (Scene Introspection) — All six questions: What scenes exist? What scenes are in the build? What is in each scene? What components are on each GameObject? What are parent-child relationships? What assets does the scene reference?

## Problem Statement

An AI agent cannot look at the Unity Editor to see what scenes exist or what is in them. Currently, the only way to know project content is to read source files as text — which is token-expensive and does not capture runtime-assembled hierarchies. The agent needs a single function call that returns a complete, structured JSON manifest of all scenes and their contents.

## Success Criteria

1. A static method `UnityBridge.SceneInventoryTool.GetSceneManifest()` exists and is callable via the execute endpoint
2. The returned JSON contains an array of scene entries, each with: `name`, `path`, `buildIndex` (or -1 if not in build), `isInBuild`, `isEnabled`
3. A static method `UnityBridge.SceneInventoryTool.GetSceneHierarchy(string scenePath)` exists
4. The hierarchy JSON contains nested GameObjects with: `name`, `activeSelf`, `tag`, `layer`, `components[]` (each with component `type` name and `enabled` state), `children[]` (recursive)
5. Component entries include at minimum: component type name and enabled state (where applicable — not all components have an `enabled` property)
6. The manifest correctly lists ALL `.unity` files found in `Assets/`
7. Build scene list matches `EditorBuildSettings.scenes` with correct indices
8. Hierarchy traversal handles deeply nested objects (10+ levels) without stack overflow
9. Output is valid, parseable JSON in all cases
10. Methods are callable via: `bash .agent/tools/unity_bridge.sh execute UnityBridge.SceneInventoryTool.GetSceneManifest`

## Expected Development Work

### New Files

- **`Unity-Bridge/Editor/Tools/SceneInventoryTool.cs`** — Namespace: `UnityBridge`. Contains both static methods:
  - `public static string GetSceneManifest()` — Uses `AssetDatabase.FindAssets("t:Scene")` to find all `.unity` files, cross-references with `EditorBuildSettings.scenes` for build info
  - `public static string GetSceneHierarchy(string scenePath)` — Opens the scene via `EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive)` for inspection, performs recursive `Transform` traversal, then closes the scene to restore the original editor state

### Directory Structure

Create `Unity-Bridge/Editor/Tools/` directory if it does not already exist. All future tool scripts will live here.

### JSON Output Formats

**GetSceneManifest response:**
```json
{
  "scenes": [
    {
      "name": "SampleScene",
      "path": "Assets/Scenes/SampleScene.unity",
      "buildIndex": 0,
      "isInBuild": true,
      "isEnabled": true,
      "rootGameObjects": 5
    }
  ],
  "totalScenes": 1,
  "buildScenes": 1
}
```

**GetSceneHierarchy response:**
```json
{
  "scenePath": "Assets/Scenes/SampleScene.unity",
  "hierarchy": [
    {
      "name": "Main Camera",
      "activeSelf": true,
      "tag": "MainCamera",
      "layer": 0,
      "components": [
        {"type": "UnityEngine.Camera", "enabled": true},
        {"type": "UnityEngine.AudioListener", "enabled": true}
      ],
      "children": []
    },
    {
      "name": "Directional Light",
      "activeSelf": true,
      "tag": "Untagged",
      "layer": 0,
      "components": [
        {"type": "UnityEngine.Light", "enabled": true}
      ],
      "children": []
    }
  ]
}
```

### Key Implementation Details

- **Scene discovery:** `AssetDatabase.FindAssets("t:Scene")` returns GUIDs. Convert each to a path with `AssetDatabase.GUIDToAssetPath()`. Filter to only `.unity` files under `Assets/`.
- **Build scene matching:** Iterate `EditorBuildSettings.scenes` to build a dictionary of path-to-index. Scenes not in the dictionary get `buildIndex: -1`.
- **Hierarchy extraction:** When opening a scene additively, be careful not to modify the currently active scene. After extraction, close the additively-opened scene with `EditorSceneManager.CloseScene()`.
- **Component enabled state:** Not all components inherit from `Behaviour` (which has `enabled`). Check `component is Behaviour` before accessing `.enabled`. For non-Behaviour components (e.g., `Transform`, `MeshFilter`), omit the enabled field or set it to `null`.
- **Recursive traversal:** Use a recursive method that takes a `Transform` and builds the JSON node. Guard against extremely deep hierarchies with a max depth parameter (default 50).

## Testing Protocol

1. `bash .agent/tools/unity_bridge.sh compile` — Read `C:/temp/unity_bridge_output.txt`, confirm compilation succeeded
2. `bash .agent/tools/unity_bridge.sh execute UnityBridge.SceneInventoryTool.GetSceneManifest` — Read output
3. Verify the JSON contains at least one scene entry (SampleScene or whatever exists in the project)
4. Verify the `path` field points to a valid `.unity` file
5. Verify `buildIndex` and `isInBuild` are consistent with each other
6. `bash .agent/tools/unity_bridge.sh execute UnityBridge.SceneInventoryTool.GetSceneHierarchy '["Assets/Scenes/SampleScene.unity"]'` — Read output (adjust path to match an actual scene found in step 3)
7. Verify the hierarchy contains expected root GameObjects (Main Camera, Directional Light are typical defaults)
8. Verify each GameObject has a `components` array with at least `Transform`
9. Verify nested children (if any exist) are represented recursively
10. Verify the scene was not left open in the editor after hierarchy extraction (check via `status` command)

## Dependencies

- **Challenge 01 (Execute Endpoint)** must be complete — SceneInventoryTool methods are called via the execute endpoint
