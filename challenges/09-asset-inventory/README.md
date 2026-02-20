# Challenge 09: Asset Inventory Tool

## Overview
Build AssetInventoryTool (Brief Tool #9) — scans the entire Assets/ directory, produces a manifest of all assets by type (scripts, prefabs, scenes, materials, textures, audio, models, ScriptableObjects, animations, animator controllers), with path, GUID, file size, last modified date, and dependency references. Also evaluates Unity Dependencies Hunter and Asset Relations Viewer (Section 10).

## Brief Reference
Section 4.4 (Asset Inventory) — Tool #9. Section 10 — Unity Dependencies Hunter (unreferenced assets), Asset Relations Viewer (dependency tree).

## Problem Statement
Before making informed decisions, an agent needs a complete project map: what assets exist, their types, sizes, and how they depend on each other. This enables discovering reusable materials, finding unused assets for cleanup, and understanding the full dependency graph.

## Success Criteria
1. `UnityBridge.AssetInventoryTool.GetFullInventory()` returns complete asset manifest
2. Categories: Scripts, Prefabs, Scenes, Materials, Textures, AudioClips, Models, ScriptableObjects, Animations, AnimatorControllers, Shaders, Fonts, Other
3. Each entry: path, guid, fileSize (bytes), lastModified (ISO 8601), assetType
4. `UnityBridge.AssetInventoryTool.GetInventoryByType(string typeName)` filters by type
5. `UnityBridge.AssetInventoryTool.GetAssetDependencies(string assetPath)` returns outbound dependencies
6. `UnityBridge.AssetInventoryTool.GetAssetReferencedBy(string assetPath)` returns inbound references
7. `UnityBridge.AssetInventoryTool.FindUnreferencedAssets()` finds assets not referenced by build scenes or other assets
8. Summary: total assets, count per type, total size, unreferenced count
9. Handles 1000+ assets without timeout
10. GUIDs match .meta files

## Expected Development Work
### New Files
- `Unity-Bridge/Editor/Tools/AssetInventoryTool.cs` — Uses `AssetDatabase.FindAssets("")`, `AssetDatabase.GetAssetPath()`, `AssetDatabase.AssetPathToGUID()`, `AssetDatabase.GetDependencies()`, `System.IO.FileInfo` for size/dates. Type classification via extension + `AssetDatabase.GetMainAssetTypeAtPath()`.

### JSON Output
```json
{
  "summary": {"totalAssets": 156, "totalSizeBytes": 52428800, "byType": {"Scripts": {"count": 45, "sizeBytes": 102400}}},
  "assets": [{"path": "Assets/Scripts/Player.cs", "guid": "abc...", "type": "Script", "fileSize": 2048, "lastModified": "2024-01-15T10:30:00Z"}]
}
```

## Testing Protocol
1. `bash .agent/tools/unity_bridge.sh compile` — Confirm
2. `bash .agent/tools/unity_bridge.sh execute UnityBridge.AssetInventoryTool.GetFullInventory` — Verify all known assets
3. `bash .agent/tools/unity_bridge.sh execute UnityBridge.AssetInventoryTool.GetInventoryByType '["Script"]'` — Verify filter
4. Test GetAssetDependencies on a known asset
5. Test FindUnreferencedAssets

## Dependencies
- Challenge 01 (Execute Endpoint)
- Challenge 07 (GUID Resolver)
