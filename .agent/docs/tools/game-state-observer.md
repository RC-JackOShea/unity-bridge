# GameStateObserver

Runtime game state inspection via reflection. Reads live component property values, GameObject transforms, and scene hierarchy during Play Mode. All methods require Play Mode to be active.

## Key Methods

| Method | Description |
|--------|-------------|
| `GetComponentValue(goPath, componentType, propertyName)` | Read a single property/field from a component on a GameObject |
| `GetGameObjectState(goPath)` | Get transform, active state, and component list for a GameObject |
| `GetSceneState()` | List all root GameObjects in the active scene with positions |
| `WaitForCondition(jsonCondition, timeout)` | Poll until a condition is met or timeout expires |

## Usage

```bash
bash .agent/tools/unity_bridge.sh execute UnityBridge.GameStateObserver.GetComponentValue '["Player", "Rigidbody", "velocity"]'
bash .agent/tools/unity_bridge.sh execute UnityBridge.GameStateObserver.GetGameObjectState '["Player"]'
bash .agent/tools/unity_bridge.sh execute UnityBridge.GameStateObserver.GetSceneState
bash .agent/tools/unity_bridge.sh execute UnityBridge.GameStateObserver.WaitForCondition '["<jsonCondition>", "5"]'
```

## GameObject Paths

`goPath` supports:
- Simple names: `"Player"` (uses `GameObject.Find`)
- Hierarchical paths: `"Canvas/Panel/ScoreText"` (walks scene roots, then `Transform.Find`)
- Inactive objects are found via scene root enumeration

## WaitForCondition Types

```json
{"type": "gameObjectActive", "path": "Player", "value": true}
{"type": "propertyEquals", "path": "ScoreManager", "component": "ScoreSystem", "property": "Score", "value": "10"}
{"type": "logContains", "text": "GameOver"}
```

Note: `logContains` is not fully implemented and always returns false. Use PlayModeInteractor's `check_log` instead.

## Response Formats

**GetComponentValue:**
```json
{"success":true,"gameObject":"Player","component":"Rigidbody","property":"velocity","value":{"x":0,"y":-9.8,"z":0},"valueType":"Vector3"}
```

**GetGameObjectState:**
```json
{"success":true,"name":"Player","activeSelf":true,"activeInHierarchy":true,"position":{"x":0,"y":1,"z":0},"rotation":{"x":0,"y":0,"z":0},"scale":{"x":1,"y":1,"z":1},"components":[{"type":"Transform","enabled":true},{"type":"Rigidbody","enabled":true}]}
```

**GetSceneState:**
```json
{"success":true,"sceneName":"SampleScene","rootObjects":[{"name":"Main Camera","activeSelf":true,"position":{"x":0,"y":1,"z":-10},"childCount":0}]}
```

**WaitForCondition:**
```json
{"success":true,"conditionMet":true,"elapsedSeconds":0.3,"timeout":5}
```

## Examples

```bash
# Check the score text during Play Mode
bash .agent/tools/unity_bridge.sh execute UnityBridge.GameStateObserver.GetComponentValue '["Canvas/ScoreText", "TextMeshProUGUI", "text"]'

# Get full state of a game object
bash .agent/tools/unity_bridge.sh execute UnityBridge.GameStateObserver.GetGameObjectState '["Player"]'

# Wait up to 5 seconds for player to reach position
bash .agent/tools/unity_bridge.sh execute UnityBridge.GameStateObserver.WaitForCondition '["{\"type\":\"propertyEquals\",\"path\":\"Player\",\"component\":\"Transform\",\"property\":\"position\",\"value\":\"(0.0, 1.0, 0.0)\"}","5"]'
```

## Common Pitfalls

- All methods return an error if not in Play Mode. Always `play enter` first.
- Property lookup tries: exact name, `_` prefix, `m_` prefix. Use the C# property name (e.g., `velocity`), not the serialized name.
- Component types are resolved by short name (`Rigidbody`, `Transform`) -- fully qualified names also work.
- Values are serialized: primitives as JSON literals, Vector2/3/Quaternion/Color as objects, enums and others as strings.
- `WaitForCondition` blocks the thread. Use short timeouts (under 10s) to avoid bridge timeouts.
