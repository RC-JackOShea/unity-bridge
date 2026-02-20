# Challenge 19: Network Test Orchestrator (ParrelSync)

## Overview

Build the NetworkTestOrchestrator (Brief Tool #15) -- manages ParrelSync clone instances for multi-player testing. ParrelSync allows running multiple Unity Editor instances of the same project via symlinked folders, enabling automated host/client networking tests without manual intervention.

## Brief Reference

Section 6.3 (Networking Testing / ParrelSync) -- ParrelSync creates symlinked project clones that run as independent Unity Editor instances. The orchestrator detects ParrelSync installation, manages clone lifecycle (create, launch, query), identifies the current instance's role (original vs clone), detects which networking framework is in use (Netcode for GameObjects, Mirror, Photon, Fish-Networking), and runs coordinated multi-instance test scenarios with structured result collection.

## Problem Statement

Multiplayer testing is uniquely challenging for automation -- it requires multiple Unity Editor instances running simultaneously with coordinated actions. A human developer manually opens two editors, starts one as host, joins the other as client, performs actions on both, and visually confirms synchronization. This is slow, error-prone, and impossible to automate without a multi-instance orchestrator.

ParrelSync solves the "multiple editors" problem by creating symlinked project clones that share the same Assets folder. The NetworkTestOrchestrator builds on top of ParrelSync to manage the full lifecycle: detect installation, create clones, launch them as subprocesses, assign host/client roles, run scripted test scenarios across both instances, and collect results into a structured report. If ParrelSync is not installed, the tool must handle this gracefully and report what is needed for installation.

## Success Criteria

1. `UnityBridge.NetworkTestOrchestrator.GetParrelSyncStatus()` detects whether ParrelSync is installed, lists existing clones, and reports if the current instance is a clone or the original.
2. `UnityBridge.NetworkTestOrchestrator.CreateClone()` creates a new ParrelSync clone instance via the ParrelSync API (`ClonesManager.CreateClone`).
3. `UnityBridge.NetworkTestOrchestrator.LaunchClone(string clonePath)` starts a clone Unity Editor instance as a subprocess via `System.Diagnostics.Process`.
4. Detection of clone vs original -- the tool correctly identifies whether the running editor is the original project or a ParrelSync clone using `ClonesManager.IsClone()`.
5. Detection of the networking framework in use -- inspects assemblies and `manifest.json` to identify Netcode for GameObjects, Mirror, Photon, or Fish-Networking (or none).
6. `UnityBridge.NetworkTestOrchestrator.RunNetworkTest(string jsonTestSpec)` orchestrates a complete multi-instance test: starts the original as host, starts the clone as client, runs the scripted action sequences on both, and collects results.
7. Results are collected from both instances via shared temp files (both instances share the same project files through ParrelSync symlinks).
8. The tool returns structured JSON results including connection status, state sync verification, per-action outcomes, and overall pass/fail.
9. When ParrelSync is not installed, all methods return a structured error JSON explaining the absence and providing installation instructions (package URL for `manifest.json`), rather than throwing exceptions.
10. Subprocess management is robust -- launched clone processes are tracked, cleaned up on test completion or failure, and do not leave orphaned Unity Editor instances.

## Expected Development Work

### New Files

- **`Unity-Bridge/Editor/Tools/NetworkTestOrchestrator.cs`** -- Static class in the `UnityBridge` namespace. Must include:

  - `public static string GetParrelSyncStatus()` -- Checks for ParrelSync installation by attempting to resolve `ParrelSync.ClonesManager` via reflection or conditional compilation (`#if` with scripting define). If installed, queries `ClonesManager.GetCloneProjectsPath()` to list existing clones. Checks `ClonesManager.IsClone()` for the current instance. Returns JSON:
    ```json
    {
      "success": true,
      "installed": true,
      "isClone": false,
      "clones": [
        {"path": "C:/Projects/MyGame_clone0", "name": "MyGame_clone0"}
      ]
    }
    ```
    When not installed:
    ```json
    {
      "success": true,
      "installed": false,
      "isClone": false,
      "clones": [],
      "installInstructions": "Add to Packages/manifest.json: \"com.veriorpies.parrelsync\": \"https://github.com/VeriorPies/ParrelSync.git?path=/ParrelSync\""
    }
    ```

  - `public static string CreateClone()` -- Calls `ClonesManager.CreateClone()` (or equivalent API) to create a new symlinked clone. Returns the clone path on success, or an error if ParrelSync is not installed. Returns JSON:
    ```json
    {
      "success": true,
      "clonePath": "C:/Projects/MyGame_clone0",
      "cloneName": "MyGame_clone0"
    }
    ```

  - `public static string LaunchClone(string clonePath)` -- Starts a new Unity Editor process targeting the clone project path. Uses `System.Diagnostics.Process.Start` with the Unity Editor executable path (discovered via `EditorApplication.applicationPath`) and `-projectPath` argument. Returns JSON:
    ```json
    {
      "success": true,
      "processId": 12345,
      "clonePath": "C:/Projects/MyGame_clone0"
    }
    ```

  - `public static string RunNetworkTest(string jsonTestSpec)` -- Parses the test specification, orchestrates the full test sequence. Writes role assignments and action scripts to shared temp files. Launches clone if not already running. Monitors shared result files for completion. Collects and merges results from both instances. Returns JSON:
    ```json
    {
      "success": true,
      "testName": "BasicConnectionTest",
      "overallResult": "Passed",
      "networkFramework": "NetcodeForGameObjects",
      "host": {
        "actions": [
          {"type": "start_host", "result": "success"},
          {"type": "wait_seconds", "result": "success", "duration": 2.0},
          {"type": "check_state", "result": "success", "property": "connectedClients", "expected": 1, "actual": 1}
        ]
      },
      "client": {
        "actions": [
          {"type": "wait_seconds", "result": "success", "duration": 1.0},
          {"type": "start_client", "result": "success"},
          {"type": "wait_condition", "result": "success", "condition": "isConnected", "elapsed": 1.2}
        ]
      },
      "validations": [
        {"type": "connectionEstablished", "result": "passed"},
        {"type": "stateSync", "property": "playerPosition", "result": "passed"}
      ]
    }
    ```

  - `public static string DetectNetworkFramework()` -- Inspects loaded assemblies for known networking framework types and checks `Packages/manifest.json` for package references. Returns JSON:
    ```json
    {
      "success": true,
      "framework": "NetcodeForGameObjects",
      "detected": ["Unity.Netcode.Runtime"],
      "version": "1.5.2"
    }
    ```

### Test Spec Format

```json
{
  "testName": "BasicConnectionTest",
  "networkFramework": "NetcodeForGameObjects",
  "hostScene": "Assets/Scenes/NetworkTestScene.unity",
  "clientScene": "Assets/Scenes/NetworkTestScene.unity",
  "hostActions": [
    {"type": "start_host"},
    {"type": "wait_seconds", "duration": 2},
    {"type": "check_state", "property": "connectedClients", "expected": 1}
  ],
  "clientActions": [
    {"type": "wait_seconds", "duration": 1},
    {"type": "start_client"},
    {"type": "wait_condition", "condition": "isConnected", "timeout": 5}
  ],
  "validations": [
    {"type": "connectionEstablished"},
    {"type": "stateSync", "property": "playerPosition"}
  ]
}
```

### Important Notes

- **ParrelSync may not be installed.** The tool must detect this at runtime and degrade gracefully. Use reflection or `#if` conditional compilation with a scripting define symbol to avoid hard compile errors against missing ParrelSync types.
- **Inter-instance communication** uses shared temp files in the project's `Temp/` directory (symlinked by ParrelSync, accessible to both instances) or a dedicated folder like `C:/temp/unity_bridge_network_test/`.
- **Subprocess lifecycle** must be carefully managed. Track launched processes by PID, implement timeouts, and ensure cleanup even if the orchestrator crashes or the test times out.
- **Networking framework detection** should check for: `Unity.Netcode.Runtime` (Netcode for GameObjects), `Mirror` assembly, `Photon.Pun`/`Photon.Realtime` assemblies, and `FishNet.Runtime` assembly.

## Testing Protocol

1. `bash .agent/tools/unity_bridge.sh compile` -- Read `C:/temp/unity_bridge_output.txt`, confirm compilation succeeds with no errors.
2. `bash .agent/tools/unity_bridge.sh execute UnityBridge.NetworkTestOrchestrator.GetParrelSyncStatus` -- Read output. Verify structured JSON is returned regardless of whether ParrelSync is installed.
3. `bash .agent/tools/unity_bridge.sh execute UnityBridge.NetworkTestOrchestrator.DetectNetworkFramework` -- Read output. Verify framework detection returns valid JSON (may report "none" if no networking package is present).
4. If ParrelSync is installed: `bash .agent/tools/unity_bridge.sh execute UnityBridge.NetworkTestOrchestrator.CreateClone` -- Read output. Verify clone creation succeeds or reports existing clone.
5. If ParrelSync is installed and clone exists: `bash .agent/tools/unity_bridge.sh execute UnityBridge.NetworkTestOrchestrator.LaunchClone '["C:/path/to/clone"]'` -- Read output. Verify process ID is returned.
6. Test graceful degradation: if ParrelSync is not installed, verify all methods return structured error JSON with installation instructions rather than throwing exceptions.
7. Full multi-instance test only if ParrelSync is installed, a clone exists, and a networking framework is detected -- this is an advanced integration test that may not be feasible in all environments.

## Dependencies

- **Challenge 01 (Execute Endpoint)** -- All methods are invoked via `bash .agent/tools/unity_bridge.sh execute UnityBridge.NetworkTestOrchestrator.<Method>`.
- **Challenge 16 (Play Mode Interaction)** -- The action sequence format in test specs builds on the interaction framework for scripted host/client actions.
- **ParrelSync package (external dependency)** -- Must be installed separately. The tool must function (with degraded capability) without it.
