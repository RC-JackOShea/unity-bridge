# PrefabCreator

JSON-driven prefab creation and modification. Creates new prefabs from hierarchical specs and patches existing prefabs with add/remove/set operations.

## Key Methods

| Method | Description |
|--------|-------------|
| `CreatePrefab(jsonSpec)` | Build a new prefab from a JSON hierarchy spec and save it as a `.prefab` asset |
| `ModifyPrefab(prefabPath, jsonPatch)` | Apply a list of patch operations to an existing prefab |

## Usage

```bash
bash .agent/tools/unity_bridge.sh execute UnityBridge.PrefabCreator.CreatePrefab '<jsonSpec>'
bash .agent/tools/unity_bridge.sh execute UnityBridge.PrefabCreator.ModifyPrefab '["Assets/Prefabs/My.prefab", "<jsonPatch>"]'
```

## CreatePrefab JSON Spec

```json
{
  "outputPath": "Assets/Resources/Prefabs/MyCube.prefab",
  "root": {
    "name": "MyCube",
    "tag": "Player",
    "layer": 0,
    "components": [
      { "type": "Transform", "properties": { "m_LocalPosition": {"x":0,"y":1,"z":0} } },
      { "type": "MeshFilter" },
      { "type": "MeshRenderer" },
      { "type": "BoxCollider" }
    ],
    "children": [
      { "name": "Child", "components": [{ "type": "SpriteRenderer" }] }
    ]
  }
}
```

## ModifyPrefab JSON Patch

Supported operations: `addComponent`, `removeComponent`, `setProperty`, `addChild`, `removeChild`.

```json
{
  "operations": [
    { "op": "addComponent", "gameObjectPath": "", "componentType": "Rigidbody", "properties": { "m_Mass": 2.0 } },
    { "op": "setProperty", "gameObjectPath": "", "componentType": "Rigidbody", "propertyName": "m_UseGravity", "propertyValue": true },
    { "op": "removeComponent", "gameObjectPath": "Child", "componentType": "SpriteRenderer", "componentIndex": 0 },
    { "op": "addChild", "parentPath": "", "child": { "name": "NewChild", "components": [] } },
    { "op": "removeChild", "gameObjectPath": "Child" }
  ]
}
```

`gameObjectPath` is `/`-delimited relative to the prefab root. Empty string targets the root itself.

## Examples

```bash
# Create a simple cube prefab
bash .agent/tools/unity_bridge.sh execute UnityBridge.PrefabCreator.CreatePrefab '{"outputPath":"Assets/Resources/Prefabs/TestCube.prefab","root":{"name":"TestCube","components":[{"type":"MeshFilter"},{"type":"MeshRenderer"},{"type":"BoxCollider"}]}}'
# Response: {"success":true,"prefabPath":"Assets/Resources/Prefabs/TestCube.prefab","gameObjectCount":1,"componentCount":3}

# Add a Rigidbody to an existing prefab
bash .agent/tools/unity_bridge.sh execute UnityBridge.PrefabCreator.ModifyPrefab '["Assets/Resources/Prefabs/TestCube.prefab","{\"operations\":[{\"op\":\"addComponent\",\"gameObjectPath\":\"\",\"componentType\":\"Rigidbody\"}]}"]'
# Response: {"success":true,"prefabPath":"Assets/Resources/Prefabs/TestCube.prefab","operationsApplied":1}
```

## Common Pitfalls

- `outputPath` must start with `Assets/` and end with `.prefab` -- other paths are rejected.
- Component types are resolved by short name (e.g., `Rigidbody`, `BoxCollider`). Use the class name, not the full namespace.
- Properties are set via `SerializedProperty`, so use Unity's internal names (e.g., `m_Mass`, `m_UseGravity`), not C# property names.
- Directories are auto-created if missing, but the `Assets/` prefix is mandatory.
- After creating or modifying prefabs, run `compile` to ensure Unity picks up the changes.
