# Challenge 03: Component Detail Extractor

## Overview

Build the ComponentDetailExtractor (Brief Tool #2) — given a GameObject path within a scene (e.g., `Canvas/MainMenu/StartButton`), return ALL components with ALL serialized property names, types, and current values as structured JSON. This provides deep inspection beyond just component type names.

## Brief Reference

Section 4.1 (Scene Introspection) — "What components are on each GameObject? For each GameObject, iterate `GetComponents<Component>()` and extract type names, serialised property values via `SerializedObject`/`SerializedProperty`."

## Problem Statement

Challenge 02 returns component type names, but agents often need to know exact property values — camera field of view, light intensity, RectTransform anchoring values, custom MonoBehaviour serialized fields. Without deep property extraction, the agent cannot understand or reproduce component configurations. For example, if the agent needs to create a camera with matching settings, it must know the current camera's FOV, clear flags, clipping planes, and render path — not just that a "Camera" component exists.

## Success Criteria

1. A static method `UnityBridge.ComponentDetailExtractor.GetComponentDetails(string scenePath, string gameObjectPath)` exists and is callable via the execute endpoint
2. For each component on the target GameObject, returns: full type name, enabled state, and complete list of serialized properties
3. Each property includes: `name` (the serialized field name like `m_FieldOfView`), `type` (string representation of `SerializedPropertyType`), `value` (serialized to a JSON-compatible format)
4. Handles all Unity built-in value types correctly:
   - `Vector2`, `Vector3`, `Vector4` as `{"x": N, "y": N, ...}`
   - `Quaternion` as `{"x": N, "y": N, "z": N, "w": N}`
   - `Color` as `{"r": N, "g": N, "b": N, "a": N}`
   - `Rect` as `{"x": N, "y": N, "width": N, "height": N}`
   - `Bounds` as `{"center": {...}, "extents": {...}}`
   - `AnimationCurve` as `{"keys": N}` (key count)
   - `LayerMask` as integer value
5. Handles object references by returning: `{"instanceID": N, "name": "objectName", "type": "typeName"}` or `null` if unset
6. Handles arrays and lists of serialized properties with element values
7. GameObject path resolution works with `/` separator for hierarchy traversal (e.g., `"Canvas/Panel/Button"` finds the Button child of Panel child of Canvas)
8. Returns a meaningful error JSON if the GameObject is not found at the given path
9. Works for both scene objects and prefab instances within scenes
10. All properties from `SerializedProperty` iteration are captured (using the `SerializedProperty.Next(true)` depth-first traversal pattern)

## Expected Development Work

### New Files

- **`Unity-Bridge/Editor/Tools/ComponentDetailExtractor.cs`** — Namespace: `UnityBridge`. Contains:
  - `public static string GetComponentDetails(string scenePath, string gameObjectPath)` — Opens the scene (reusing the pattern from Challenge 02), resolves the GameObject by path, iterates all components, and extracts properties
  - `private static object SerializeProperty(SerializedProperty prop)` — Converts a `SerializedProperty` to a JSON-compatible C# object based on its `propertyType`. Must handle the full `SerializedPropertyType` enum:
    - `Integer` / `Boolean` / `Float` / `String` — direct value
    - `Color` — `{"r", "g", "b", "a"}`
    - `ObjectReference` — `{"instanceID", "name", "type"}` or null
    - `LayerMask` — integer
    - `Enum` — integer value (and optionally the enum name)
    - `Vector2` / `Vector3` / `Vector4` — component objects
    - `Rect` — `{"x", "y", "width", "height"}`
    - `ArraySize` — integer (array length)
    - `Character` — string (single char)
    - `AnimationCurve` — `{"keys": N}`
    - `Bounds` — `{"center", "extents"}`
    - `Gradient` — `{"colorKeys": N, "alphaKeys": N}`
    - `Quaternion` — `{"x", "y", "z", "w"}`
    - `ExposedReference` — the referenced object info
    - `Vector2Int` / `Vector3Int` — integer component objects
    - `RectInt` — `{"x", "y", "width", "height"}`
    - `BoundsInt` — `{"position", "size"}`
    - `ManagedReference` — type name and serialized fields
  - `private static GameObject ResolveGameObjectPath(Scene scene, string path)` — Splits path on `/`, finds root object matching the first segment, then traverses `Transform.Find()` for remaining segments

### JSON Output Format

```json
{
  "scenePath": "Assets/Scenes/SampleScene.unity",
  "gameObjectPath": "Main Camera",
  "gameObjectName": "Main Camera",
  "components": [
    {
      "type": "UnityEngine.Transform",
      "index": 0,
      "properties": [
        {"name": "m_LocalPosition", "type": "Vector3", "value": {"x": 0.0, "y": 1.0, "z": -10.0}},
        {"name": "m_LocalRotation", "type": "Quaternion", "value": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 1.0}},
        {"name": "m_LocalScale", "type": "Vector3", "value": {"x": 1.0, "y": 1.0, "z": 1.0}}
      ]
    },
    {
      "type": "UnityEngine.Camera",
      "index": 1,
      "enabled": true,
      "properties": [
        {"name": "m_ClearFlags", "type": "Enum", "value": 1},
        {"name": "m_BackGroundColor", "type": "Color", "value": {"r": 0.19, "g": 0.3, "b": 0.47, "a": 0.0}},
        {"name": "field of view", "type": "Float", "value": 60.0},
        {"name": "m_NearClipPlane", "type": "Float", "value": 0.3},
        {"name": "m_FarClipPlane", "type": "Float", "value": 1000.0}
      ]
    }
  ]
}
```

### Key Implementation Details

- **SerializedObject/SerializedProperty pattern:** Create a `new SerializedObject(component)`, get the iterator via `serializedObject.GetIterator()`, call `iterator.Next(true)` in a loop to traverse all properties depth-first. Skip properties whose depth exceeds a maximum (default 10) to avoid infinite traversal.
- **Property filtering:** Skip internal Unity properties that start with `m_PrefabInstance`, `m_PrefabAsset`, `m_GameObject`, `m_ObjectHideFlags` to reduce noise. Optionally include them behind a flag.
- **Null component handling:** Some GameObjects have missing script references that result in null components from `GetComponents<Component>()`. Check for null and report as `{"type": "MissingScript", "error": "Script reference is missing"}`.
- **Scene management:** Reuse the additive scene opening pattern from Challenge 02. Open the scene, find the object, extract data, close the scene.

## Testing Protocol

1. `bash .agent/tools/unity_bridge.sh compile` — Read `C:/temp/unity_bridge_output.txt`, confirm compilation succeeded
2. `bash .agent/tools/unity_bridge.sh execute UnityBridge.ComponentDetailExtractor.GetComponentDetails '["Assets/Scenes/SampleScene.unity", "Main Camera"]'` — Read output
3. Verify the Camera component properties are returned with numeric values (not just type names)
4. Verify the Transform component includes `m_LocalPosition`, `m_LocalRotation`, `m_LocalScale` with Vector3/Quaternion values
5. Test with a nested GameObject path if one exists in the scene (e.g., `"Canvas/Panel/Button"`)
6. Test error case with a non-existent path: `bash .agent/tools/unity_bridge.sh execute UnityBridge.ComponentDetailExtractor.GetComponentDetails '["Assets/Scenes/SampleScene.unity", "NonExistentObject"]'` — Verify structured error response
7. Verify that object reference properties show reference info (`instanceID`, `name`, `type`) rather than causing an error
8. Verify that array properties (if any exist on the target components) include element values

## Dependencies

- **Challenge 01 (Execute Endpoint)** — methods are called via the execute endpoint
- **Challenge 02 (Scene Inventory)** — reuses the scene opening/closing patterns and provides scene paths for testing
