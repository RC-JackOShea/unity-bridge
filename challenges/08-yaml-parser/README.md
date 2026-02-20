# Challenge 08: YAML Parser & Prefab Ripper

## Overview
Build the YAMLPrefabRipper (Brief Tool #7) — parses Unity .prefab and .unity YAML files, returns structured JSON with every GameObject, component (with class ID and type name), property, and cross-file GUID reference. Enables project introspection without a running Unity Editor.

## Brief Reference
Section 4.3 (YAML-Level Access) — YAML knowledge table (file headers, class IDs, FileID, GUID, reference format, stripped instances, MonoBehaviour class ID 114, m_ property prefix). Tool #7 YAMLPrefabRipper. Also references unity-yaml-parser (Python) and UnityPy from Section 10.

## Problem Statement
Editor-based tools require Unity running. For batch analysis, CI pipelines, and external tooling, direct YAML parsing is essential. Unity serializes scenes/prefabs as tagged YAML with `!u!` class ID tags that break standard YAML parsers. A custom parser is needed that understands Unity's format: document separators (`---`), class IDs, FileIDs for internal references, and GUID references for cross-file links.

## Success Criteria
1. `UnityBridge.YAMLParser.ParseFile(string filePath)` parses .unity or .prefab files and returns structured JSON
2. Correctly handles YAML header (`%YAML 1.1`, `%TAG !u! tag:unity3d.com,2011:`)
3. Splits on `--- !u!{classID} &{fileID}` document separators
4. Maps class IDs to type names (1=GameObject, 4=Transform, 20=Camera, 23=MeshRenderer, 33=MeshFilter, 54=Rigidbody, 65=BoxCollider, 108=Light, 114=MonoBehaviour, 222=Canvas, 224=RectTransform)
5. Extracts FileID local identifiers and resolves internal references
6. Identifies GUID references `{fileID: N, guid: HEX, type: N}` in output
7. For MonoBehaviours (114), extracts m_Script GUID and resolves to script path via GUIDResolver
8. Handles stripped prefab instances (identifies them, notes source prefab)
9. Returns summary: total objects, count by type, external reference count
10. Parses files up to 10MB without timeout

## Expected Development Work
### New Files
- `Unity-Bridge/Editor/Tools/YAMLParser.cs` — Line-by-line parser (not standard YAML library). Splits on `---` markers, extracts class ID and fileID from header line, parses indented key-value pairs, collects internal/external references. Includes static ClassID-to-name dictionary.

### JSON Output Format
```json
{
  "filePath": "Assets/Scenes/SampleScene.unity",
  "fileType": "scene",
  "objects": [
    {
      "fileID": 6,
      "classID": 1,
      "typeName": "GameObject",
      "properties": {"m_Name": "Main Camera", "m_TagString": "MainCamera", "m_Layer": 0},
      "internalReferences": [{"property": "m_Component", "targetFileID": 7}],
      "externalReferences": []
    }
  ],
  "summary": {"totalObjects": 45, "byType": {"GameObject": 12, "Transform": 12}, "externalReferences": 8}
}
```

## Testing Protocol
1. `bash .agent/tools/unity_bridge.sh compile` — Confirm success
2. `bash .agent/tools/unity_bridge.sh execute UnityBridge.YAMLParser.ParseFile '["Assets/Scenes/SampleScene.unity"]'` — Read output
3. Verify objects have correct class IDs and type names
4. Verify internal references resolve (Transform m_Father)
5. Test with a .prefab file
6. Check summary statistics

## Dependencies
- Challenge 01 (Execute Endpoint)
- Challenge 07 (GUID Resolver — for external reference resolution)
