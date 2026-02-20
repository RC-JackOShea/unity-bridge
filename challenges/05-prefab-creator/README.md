# Challenge 05: Prefab Creator

## Overview

Build the PrefabCreator (Brief Tool #5) — accepts a JSON specification describing a prefab hierarchy (GameObjects, components, property values) and produces a `.prefab` asset. This is the first WRITE tool, enabling the agent to create Unity assets programmatically.

## Brief Reference

Section 4.2 (Prefab Introspection and Manipulation) — Writing/Modifying Prefabs: "Create new prefabs programmatically — Build a GameObject hierarchy in code, configure components, then call `PrefabUtility.SaveAsPrefabAsset()`. Modify existing prefabs — Use `PrefabUtility.LoadPrefabContents()` -> modify -> `SaveAsPrefabAsset()` -> `UnloadPrefabContents()`."

## Problem Statement

The agent can now READ scenes and prefabs but cannot CREATE assets. To achieve lights-out operation, the agent must create prefabs from specifications — this is how it will build UI elements, gameplay objects, and reusable components without ever touching the Unity Editor GUI. Manual prefab creation through Unity's interface is impossible for an agent; programmatic creation through a JSON spec is the only path.

## Success Criteria

1. `UnityBridge.PrefabCreator.CreatePrefab(string jsonSpec)` accepts a JSON specification string and creates a `.prefab` file at the specified output path
2. The JSON spec supports: `outputPath` (where to save), `root` object with `name`, `components[]`, and `children[]` (recursive)
3. Supports adding common Unity built-in components: `Transform`, `MeshFilter`, `MeshRenderer`, `BoxCollider`, `SphereCollider`, `CapsuleCollider`, `MeshCollider`, `Rigidbody`, `AudioSource`, `Light`, `Camera`
4. Supports adding MonoBehaviour components by script class name — resolved via `AppDomain.CurrentDomain.GetAssemblies()` type search
5. Sets serialized property values on components via `SerializedObject`/`SerializedProperty` (reusing patterns from Challenge 03)
6. Returns JSON result: `{"success": true, "prefabPath": "Assets/...", "gameObjectCount": N, "componentCount": N}`
7. `UnityBridge.PrefabCreator.ModifyPrefab(string prefabPath, string jsonPatch)` loads an existing prefab, applies modifications (add/remove components, change property values, add/remove children), and saves
8. Created prefabs are valid, loadable in the Unity Editor, and appear in the Project window after `AssetDatabase.Refresh()`
9. Properly creates intermediate directories if the output path's parent folders do not exist
10. Returns structured error JSON if creation fails (invalid component type, invalid path, serialization error)

## Expected Development Work

### New Files

- **`Unity-Bridge/Editor/Tools/PrefabCreator.cs`** — Namespace: `UnityBridge`. Contains:
  - `public static string CreatePrefab(string jsonSpec)` — Parses the JSON spec, builds a temporary GameObject hierarchy in memory, adds and configures components, calls `PrefabUtility.SaveAsPrefabAsset()` to save, then destroys the temporary objects with `Object.DestroyImmediate()`
  - `public static string ModifyPrefab(string prefabPath, string jsonPatch)` — Uses `PrefabUtility.LoadPrefabContents()` to load the prefab in isolation, applies the patch operations, calls `PrefabUtility.SaveAsPrefabAsset()` to save, then `PrefabUtility.UnloadPrefabContents()` to clean up
  - Private helper methods for: building a single GameObject from a JSON node, resolving component types by name, setting properties via `SerializedObject`, creating directories via `AssetDatabase.CreateFolder()`

### JSON Input Spec Format (CreatePrefab)

```json
{
  "outputPath": "Assets/Prefabs/MyPrefab.prefab",
  "root": {
    "name": "MyPrefab",
    "tag": "Untagged",
    "layer": 0,
    "components": [
      {
        "type": "BoxCollider",
        "properties": {
          "m_Size": {"x": 1.0, "y": 2.0, "z": 1.0},
          "m_Center": {"x": 0.0, "y": 1.0, "z": 0.0}
        }
      },
      {
        "type": "Rigidbody",
        "properties": {
          "m_Mass": 5.0,
          "m_UseGravity": true
        }
      }
    ],
    "children": [
      {
        "name": "Visual",
        "components": [
          {
            "type": "MeshFilter",
            "properties": {}
          },
          {
            "type": "MeshRenderer",
            "properties": {}
          }
        ],
        "children": []
      }
    ]
  }
}
```

### JSON Patch Format (ModifyPrefab)

```json
{
  "operations": [
    {
      "op": "addComponent",
      "gameObjectPath": "",
      "componentType": "Rigidbody",
      "properties": {"m_Mass": 10.0}
    },
    {
      "op": "removeComponent",
      "gameObjectPath": "Visual",
      "componentType": "MeshRenderer"
    },
    {
      "op": "setProperty",
      "gameObjectPath": "",
      "componentType": "BoxCollider",
      "componentIndex": 0,
      "propertyName": "m_Size",
      "propertyValue": {"x": 2.0, "y": 2.0, "z": 2.0}
    },
    {
      "op": "addChild",
      "parentPath": "",
      "child": {
        "name": "NewChild",
        "components": [],
        "children": []
      }
    },
    {
      "op": "removeChild",
      "gameObjectPath": "Visual"
    }
  ]
}
```

### Key Implementation Details

- **Component type resolution:** Accept short names like `"BoxCollider"` and resolve to full types. First try `UnityEngine.<name>`, then search all assemblies. For MonoBehaviours, search by class name across all loaded assemblies.
- **Property setting via SerializedObject:** After adding a component, create a `SerializedObject` for it, find each property by name with `FindProperty()`, and set the value based on type. Reuse the type-detection logic from Challenge 03 in reverse.
- **Mesh references:** Setting `MeshFilter.mesh` to a built-in mesh (Cube, Sphere, etc.) requires loading via `Resources.GetBuiltinResource<Mesh>("Cube.fbx")` or similar. Support common built-in mesh names as special values.
- **Directory creation:** Use `AssetDatabase.CreateFolder()` which works with Unity's asset pipeline. Parse the output path and create each missing directory segment.
- **Cleanup:** After `PrefabUtility.SaveAsPrefabAsset()`, always call `Object.DestroyImmediate()` on the temporary root GameObject. After `PrefabUtility.LoadPrefabContents()` modifications, always call `PrefabUtility.UnloadPrefabContents()` in a finally block.
- **AssetDatabase.Refresh():** Call this after creating or modifying prefabs so Unity recognizes the new/changed asset.

## Testing Protocol

1. Create `PrefabCreator.cs` with both methods
2. `bash .agent/tools/unity_bridge.sh compile` — Read `C:/temp/unity_bridge_output.txt`, confirm compilation succeeded
3. Create a simple prefab with a BoxCollider:
   ```
   bash .agent/tools/unity_bridge.sh execute UnityBridge.PrefabCreator.CreatePrefab '["{\"outputPath\":\"Assets/TestPrefabs/TestCube.prefab\",\"root\":{\"name\":\"TestCube\",\"components\":[{\"type\":\"BoxCollider\",\"properties\":{}}],\"children\":[]}}"]'
   ```
   Read output, verify success response with prefab path
4. Verify the created prefab via PrefabDetailExtractor (if Challenge 04 is complete):
   ```
   bash .agent/tools/unity_bridge.sh execute UnityBridge.PrefabDetailExtractor.GetPrefabDetail '["Assets/TestPrefabs/TestCube.prefab"]'
   ```
   Read output, verify the prefab has a BoxCollider component
5. Test with nested children — create a prefab with a root and one child
6. Test modification — add a Rigidbody to the previously created prefab:
   ```
   bash .agent/tools/unity_bridge.sh execute UnityBridge.PrefabCreator.ModifyPrefab '["Assets/TestPrefabs/TestCube.prefab", "{\"operations\":[{\"op\":\"addComponent\",\"gameObjectPath\":\"\",\"componentType\":\"Rigidbody\",\"properties\":{\"m_Mass\":5.0}}]}"]'
   ```
   Read output, verify success
7. Verify modification persisted — re-read the prefab and confirm Rigidbody is present
8. Test error case — invalid component type name, verify structured error response

## Dependencies

- **Challenge 01 (Execute Endpoint)** — methods are called via the execute endpoint
- **Challenge 04 (Prefab Detail Extractor)** — used for verifying created prefabs have the expected structure
