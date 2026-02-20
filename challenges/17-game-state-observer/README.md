# Challenge 17: Game State Observer

## Overview

Build a runtime game state observation system that reads component values, GameObject states, and scene hierarchy data during Play Mode. This enables agents to verify expected behaviour -- player position, health values, UI visibility states, score counters -- by querying live runtime data rather than relying solely on screenshots or log output. Includes a condition-based wait system for polling until specific game states are reached.

## Brief Reference

Section 6.2 (Quality Assurance Tools) -- "Observe game state: Read component values during Play mode to verify expected behaviour: player position, health values, UI visibility states, score counters. Wait for conditions: Coroutine-based or poll-based waits for specific game states."

## Problem Statement

During Play Mode testing, agents currently have two ways to verify game state: reading log output (requires the game code to explicitly log values) or taking screenshots (visual only, cannot extract numeric values). Neither approach lets the agent directly ask "what is the player's current health?" or "is the pause menu active?" without custom logging code in every component.

The Game State Observer solves this by using reflection to read any component property or field value at runtime. The agent can query any GameObject's transform, active state, and component values by path, without requiring the game code to expose those values through logs. The `WaitForCondition` method adds poll-based blocking until a specific state is reached, replacing fragile "wait N seconds and hope" patterns with deterministic condition checks.

This tool operates exclusively during Play Mode using runtime APIs (`GameObject.Find`, `GetComponent`, reflection). It does not use editor serialization or `SerializedObject` -- it reads live, in-memory values from running game code.

## Success Criteria

1. `GameStateObserver.GetComponentValue(string goPath, string componentType, string propertyName)` reads a single primitive value (int, float, string, bool) from a component on a running GameObject and returns it as structured JSON.
2. `GetComponentValue` reads `Vector3` and `Quaternion` values, returning them as JSON objects with named fields (`x`, `y`, `z`, `w`).
3. `GetComponentValue` reads the `enabled` state of any `Behaviour`-derived component.
4. `GameStateObserver.GetGameObjectState(string goPath)` returns a JSON object containing `name`, `activeSelf`, `activeInHierarchy`, `position`, `rotation`, `scale`, and a `components` array with each component's type name and enabled state.
5. `GameStateObserver.GetGameObjectState` finds nested GameObjects using hierarchical paths (e.g., `Canvas/Panel/ScoreText`) via `Transform.Find` from root objects.
6. `GameStateObserver.WaitForCondition(string jsonCondition, float timeout)` polls until the specified condition is met or the timeout expires, returning success/failure and elapsed time.
7. `WaitForCondition` supports at least three condition types: `gameObjectActive`, `propertyEquals`, and `logContains`.
8. `GameStateObserver.GetSceneState()` returns all root GameObjects in the active scene with their transform data and active states.
9. All methods return a structured error JSON (not an exception) when the target GameObject is missing, the component is not found, or the property does not exist.
10. All methods enforce a Play Mode guard -- calling any method outside Play Mode returns an error JSON: `{"success": false, "error": "Game State Observer requires Play Mode"}`.

## Expected Development Work

### New Files

- **`Unity-Bridge/Editor/Tools/GameStateObserver.cs`** -- Static class in the `UnityBridge` namespace. Must include:

  - `public static string GetComponentValue(string goPath, string componentType, string propertyName)` -- Locates the GameObject via `GameObject.Find(goPath)` or recursive hierarchy search, calls `GetComponent(Type.GetType(componentType))`, then reads the named property or field via reflection. Returns JSON:
    ```json
    {
      "success": true,
      "gameObject": "Player",
      "component": "PlayerHealth",
      "property": "currentHP",
      "value": 85,
      "valueType": "Int32"
    }
    ```

  - `public static string GetGameObjectState(string goPath)` -- Returns comprehensive state for one GameObject. Returns JSON:
    ```json
    {
      "success": true,
      "name": "Player",
      "activeSelf": true,
      "activeInHierarchy": true,
      "position": {"x": 0.0, "y": 1.0, "z": 0.0},
      "rotation": {"x": 0.0, "y": 90.0, "z": 0.0},
      "scale": {"x": 1.0, "y": 1.0, "z": 1.0},
      "components": [
        {"type": "Transform", "enabled": true},
        {"type": "MeshRenderer", "enabled": true},
        {"type": "PlayerController", "enabled": true},
        {"type": "Rigidbody", "enabled": true}
      ]
    }
    ```

  - `public static string WaitForCondition(string jsonCondition, float timeout)` -- Polls at a configurable interval (default 0.1s) until the condition evaluates to true or the timeout expires. Condition JSON formats:
    ```json
    {"type": "gameObjectActive", "path": "Player", "value": true}
    ```
    ```json
    {"type": "propertyEquals", "path": "Player", "component": "Health", "property": "currentHP", "value": 100}
    ```
    ```json
    {"type": "logContains", "text": "Game Over"}
    ```
    Returns JSON on success:
    ```json
    {
      "success": true,
      "conditionMet": true,
      "elapsedSeconds": 1.35,
      "timeout": 5.0
    }
    ```
    Returns JSON on timeout:
    ```json
    {
      "success": true,
      "conditionMet": false,
      "elapsedSeconds": 5.0,
      "timeout": 5.0,
      "lastState": "GameObject 'Player' activeSelf was false"
    }
    ```

  - `public static string GetSceneState()` -- Enumerates all root GameObjects in the active scene. Returns JSON:
    ```json
    {
      "success": true,
      "sceneName": "SampleScene",
      "rootObjects": [
        {
          "name": "Main Camera",
          "activeSelf": true,
          "position": {"x": 0, "y": 1, "z": -10},
          "childCount": 0
        },
        {
          "name": "Canvas",
          "activeSelf": true,
          "position": {"x": 0, "y": 0, "z": 0},
          "childCount": 3
        }
      ]
    }
    ```

### Key Implementation Details

- **GameObject lookup**: Use `GameObject.Find(goPath)` for root-level objects and simple paths. For nested paths like `Canvas/Panel/Button`, find the root object first, then use `transform.Find("Panel/Button")` to walk the hierarchy. `GameObject.Find` does not find inactive objects -- for inactive targets, iterate `SceneManager.GetActiveScene().GetRootGameObjects()` and walk the hierarchy with `Transform.Find`.
- **Component resolution**: Use `gameObject.GetComponent(componentType)` where `componentType` is resolved via `Type.GetType(name)`. If `Type.GetType` returns null, search all loaded assemblies for a type matching the short name (e.g., `PlayerHealth` without namespace).
- **Reflection for value reading**: Use `type.GetProperty(propertyName)` first, then fall back to `type.GetField(propertyName)`. For private fields, use `BindingFlags.NonPublic | BindingFlags.Instance` to access serialized private fields (common Unity pattern: `[SerializeField] private float _health`). For fields with the underscore convention, also try stripping the leading underscore if the exact name is not found.
- **Value serialization**: Primitives serialize directly. `Vector3`, `Vector2`, `Quaternion`, `Color` serialize as JSON objects with named fields. For complex types, use `JsonUtility.ToJson` or fall back to `ToString()`.
- **WaitForCondition execution**: Use `EditorApplication.update` callbacks to poll the condition each frame. Track elapsed time with `EditorApplication.timeSinceStartup`. Signal completion back to the server response handler when the condition is met or the timeout expires.
- **Log monitoring for `logContains`**: Subscribe to `Application.logMessageReceived` to maintain a buffer of recent log messages. Check the buffer for the target substring on each poll cycle.
- **Play Mode guard**: Every public method must check `EditorApplication.isPlaying` at entry and return an error JSON immediately if not in Play Mode.

## Testing Protocol

1. `bash .agent/tools/unity_bridge.sh health` -- Read `C:/temp/unity_bridge_output.txt`, confirm server is running.
2. Create `Unity-Bridge/Editor/Tools/GameStateObserver.cs` with all four methods.
3. `bash .agent/tools/unity_bridge.sh compile` -- Read output, confirm compilation succeeds with no errors.
4. `bash .agent/tools/unity_bridge.sh play enter` -- Read output, confirm Play Mode entered.
5. `bash .agent/tools/unity_bridge.sh execute UnityBridge.GameStateObserver.GetSceneState` -- Read output, verify JSON listing of root GameObjects in the active scene.
6. Pick a known GameObject from the scene state (e.g., `Main Camera`) and run: `bash .agent/tools/unity_bridge.sh execute UnityBridge.GameStateObserver.GetGameObjectState '["Main Camera"]'` -- Read output, verify JSON contains position, rotation, components.
7. Test component value reading: `bash .agent/tools/unity_bridge.sh execute UnityBridge.GameStateObserver.GetComponentValue '["Main Camera", "Camera", "fieldOfView"]'` -- Read output, verify numeric value returned.
8. Test missing GameObject: `bash .agent/tools/unity_bridge.sh execute UnityBridge.GameStateObserver.GetGameObjectState '["NonExistent/Object"]'` -- Read output, verify structured error JSON, not an exception.
9. Test WaitForCondition with an immediately true condition: `bash .agent/tools/unity_bridge.sh execute UnityBridge.GameStateObserver.WaitForCondition '["{\"type\":\"gameObjectActive\",\"path\":\"Main Camera\",\"value\":true}", 5.0]'` -- Read output, verify conditionMet is true with short elapsed time.
10. Test WaitForCondition timeout: use a condition for a nonexistent GameObject with a short timeout (1 second), verify conditionMet is false and elapsedSeconds is approximately 1.0.
11. `bash .agent/tools/unity_bridge.sh play exit` -- Read output, confirm Play Mode exited.
12. Test Play Mode guard: `bash .agent/tools/unity_bridge.sh execute UnityBridge.GameStateObserver.GetSceneState` -- Read output while NOT in Play Mode, verify error message about requiring Play Mode.

## Dependencies

- **Challenge 01 (Execute Endpoint)** -- All methods are invoked via `bash .agent/tools/unity_bridge.sh execute UnityBridge.GameStateObserver.<Method>`.
