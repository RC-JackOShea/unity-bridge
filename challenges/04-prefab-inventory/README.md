# Challenge 04: Prefab Inventory & Detail Extractor

## Overview

Build the PrefabInventoryTool (Brief Tool #3) and PrefabDetailExtractor (Brief Tool #4) — tools to discover all prefabs in the project and extract their full hierarchies with components and properties. This covers both reading capabilities from Section 4.2 of the brief.

## Brief Reference

Section 4.2 (Prefab Introspection) — Reading Prefabs: "List all prefabs, extract prefab structure, identify components and properties, resolve nested prefab references, detect variants and overrides, identify spawnable prefabs." Tools #3 and #4.

## Problem Statement

Prefabs are Unity's fundamental reusable asset type. An agent building gameplay systems needs to know what prefabs exist, what they contain, whether they are variants of other prefabs, and what overrides they apply. Currently, the only way to understand prefabs is to read `.prefab` YAML files — which is complex, token-expensive, and requires GUID resolution to understand cross-references. These tools provide structured JSON access to all prefab information through the execute endpoint.

## Success Criteria

1. `UnityBridge.PrefabInventoryTool.GetPrefabManifest()` returns JSON listing all prefabs in the project
2. Each prefab entry includes: `path`, `guid`, `name`, `isVariant`, `basePrefab` (path of base prefab if variant, null otherwise), `rootComponents` (list of component type names on the root object), `nestedPrefabs` (list of paths to prefabs nested inside this one)
3. `UnityBridge.PrefabDetailExtractor.GetPrefabDetail(string prefabPath)` returns the full hierarchy of a prefab with components and property values (same depth as Challenge 03)
4. Nested prefab instances within the hierarchy are identified with their source prefab path
5. Variant overrides are listed using `PrefabUtility.GetPropertyModifications()` — each override includes the target property path and the modified value
6. Spawnable prefab detection: scans MonoBehaviour scripts for `[SerializeField]` fields of type `GameObject` or specific prefab types, and cross-references which prefabs are assigned to these fields in scenes
7. Missing script references on prefab GameObjects are reported as errors rather than crashing the tool
8. Both methods are callable via the execute endpoint
9. Output is valid, parseable JSON in all cases
10. Handles projects with zero prefabs gracefully (returns empty array)

## Expected Development Work

### New Files

- **`Unity-Bridge/Editor/Tools/PrefabInventoryTool.cs`** — Namespace: `UnityBridge`. Contains:
  - `public static string GetPrefabManifest()` — Uses `AssetDatabase.FindAssets("t:Prefab")` to discover all prefabs, `AssetDatabase.GUIDToAssetPath()` for paths, `PrefabUtility.GetPrefabAssetType()` to classify each asset, `PrefabUtility.IsPartOfVariantPrefab()` for variant detection, and `PrefabUtility.GetCorrespondingObjectFromSource()` to resolve the base prefab for variants

- **`Unity-Bridge/Editor/Tools/PrefabDetailExtractor.cs`** — Namespace: `UnityBridge`. Contains:
  - `public static string GetPrefabDetail(string prefabPath)` — Uses `PrefabUtility.LoadPrefabContents()` to load the prefab in an isolated editing environment, performs the same recursive hierarchy/component/property traversal used in Challenges 02 and 03, then calls `PrefabUtility.UnloadPrefabContents()` to clean up
  - Nested prefab detection: for each child in the hierarchy, check `PrefabUtility.GetNearestPrefabInstanceRoot()` and `PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot()` to identify nested prefab instances
  - Override extraction: call `PrefabUtility.GetPropertyModifications()` on the prefab root and serialize each modification

### JSON Output Formats

**GetPrefabManifest response:**
```json
{
  "prefabs": [
    {
      "path": "Assets/Prefabs/Player.prefab",
      "guid": "a1b2c3d4e5f6...",
      "name": "Player",
      "isVariant": false,
      "basePrefab": null,
      "rootComponents": ["Transform", "PlayerController", "Rigidbody", "CapsuleCollider"],
      "nestedPrefabs": ["Assets/Prefabs/Weapon.prefab"],
      "referencedByScripts": []
    }
  ],
  "totalPrefabs": 1
}
```

**GetPrefabDetail response:**
```json
{
  "prefabPath": "Assets/Prefabs/Player.prefab",
  "isVariant": false,
  "basePrefab": null,
  "hierarchy": [
    {
      "name": "Player",
      "activeSelf": true,
      "isNestedPrefabInstance": false,
      "nestedPrefabSource": null,
      "components": [
        {
          "type": "UnityEngine.Transform",
          "properties": [
            {"name": "m_LocalPosition", "type": "Vector3", "value": {"x": 0, "y": 0, "z": 0}}
          ]
        }
      ],
      "children": [
        {
          "name": "WeaponMount",
          "isNestedPrefabInstance": true,
          "nestedPrefabSource": "Assets/Prefabs/Weapon.prefab",
          "components": [],
          "children": []
        }
      ]
    }
  ],
  "overrides": [
    {
      "targetPath": "m_LocalPosition.x",
      "targetObject": "WeaponMount",
      "value": "1.5"
    }
  ]
}
```

### Key Implementation Details

- **PrefabUtility.LoadPrefabContents/UnloadPrefabContents:** This pair loads a prefab into an isolated scene for editing. Always wrap in try/finally to ensure `UnloadPrefabContents` is called even if an exception occurs. Do not call `PrefabUtility.SaveAsPrefabAsset()` — this is a read-only operation.
- **Variant detection:** `PrefabUtility.IsPartOfVariantPrefab()` returns true for variants. To find the base, load the asset and check `PrefabUtility.GetCorrespondingObjectFromSource()`.
- **Nested prefab scanning:** After loading prefab contents, iterate all children. For each, `PrefabUtility.GetNearestPrefabInstanceRoot()` returns the root of any nested prefab instance, and `PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot()` gives its source path.
- **Property reuse:** The component property serialization logic from Challenge 03 (`SerializedObject`/`SerializedProperty` traversal) should be reused. Consider extracting it into a shared utility class if not already done.

## Testing Protocol

1. `bash .agent/tools/unity_bridge.sh compile` — Read `C:/temp/unity_bridge_output.txt`, confirm compilation succeeded
2. `bash .agent/tools/unity_bridge.sh execute UnityBridge.PrefabInventoryTool.GetPrefabManifest` — Read output
3. If prefabs exist, verify the JSON contains correct entries with paths, GUIDs, and component lists
4. If no prefabs exist in the project, create a simple test prefab programmatically (write a quick editor script or use PrefabCreator from Challenge 05 if available), compile, then re-run the manifest
5. Pick a prefab path from the manifest and run: `bash .agent/tools/unity_bridge.sh execute UnityBridge.PrefabDetailExtractor.GetPrefabDetail '["Assets/path/to/prefab.prefab"]'` — Read output
6. Verify the hierarchy contains the expected GameObjects and components with property values
7. If a variant prefab exists, verify `isVariant: true` and `basePrefab` points to the correct source
8. Verify that the tool handles prefabs with missing scripts (if any exist) without crashing

## Dependencies

- **Challenge 01 (Execute Endpoint)** — methods are called via the execute endpoint
- **Challenge 03 (Component Detail Extractor)** — reuses the `SerializedObject`/`SerializedProperty` serialization logic for extracting component property values
