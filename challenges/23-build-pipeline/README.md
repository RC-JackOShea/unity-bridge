# Challenge 23: Build Configuration & Production

## Overview

Build the BuildConfigurator (Brief Tool #19) and BuildProducer (Brief Tool #20) -- programmatically read and set build configuration (target platform, scripting backend, scenes, player settings, quality settings) and execute builds via `BuildPipeline.BuildPlayer()`, returning structured `BuildReport` JSON with success/failure, errors, warnings, file sizes, build time, and output path. Also adds a `build` command to the bridge script with subcommands `config`, `produce`, and `report`. References GameCI (Section 10) for CI/CD integration.

## Brief Reference

Section 8.1 (Build Configuration) -- `EditorUserBuildSettings.SwitchActiveBuildTarget()`, `EditorBuildSettings.scenes`, `PlayerSettings` API (companyName, productName, bundleIdentifier, scriptingBackend, apiCompatibilityLevel), QualitySettings, Build Profiles. Section 8.2 (Build Production) -- `BuildPipeline.BuildPlayer()`, CLI builds, `BuildReport` parsing. Section 10 -- GameCI for CI/CD pipeline integration. Section 13 -- IL2CPP restrictions (no `dynamic`, no `Reflection.Emit`), Unity licensing for batch mode.

## Problem Statement

Building a Unity project involves configuring dozens of interrelated settings -- target platform, scripting backend, which scenes to include and in what order, player settings (company name, product name, bundle identifier), API compatibility level, and development/release flags -- then executing the build and interpreting the result. Currently this requires the Unity GUI or manual batch scripts. For lights-out operation, the agent must read the current build configuration, modify it programmatically, execute builds, and parse detailed build reports to determine success or diagnose failures. Platform switching (e.g., from Windows to Android) involves `EditorUserBuildSettings.SwitchActiveBuildTarget()` which triggers a domain reload, adding further complexity.

## Success Criteria

1. `UnityBridge.BuildPipelineTool.GetCurrentBuildConfig()` returns current build settings as structured JSON: active target platform, scripting backend, scenes in build (paths and enabled state), company name, product name, bundle identifier, API compatibility level
2. `UnityBridge.BuildPipelineTool.ConfigureBuild(string jsonSpec)` sets all build settings: target platform (StandaloneWindows64, StandaloneOSX, StandaloneLinux64, Android, iOS, WebGL), scripting backend (Mono, IL2CPP), build scenes list, player settings (companyName, productName, bundleIdentifier), quality settings, and development build flag
3. Platform switching via `EditorUserBuildSettings.SwitchActiveBuildTarget(buildTargetGroup, buildTarget)` works correctly with proper domain reload handling
4. Scene list configuration validates that all specified scene paths exist as assets before applying
5. `UnityBridge.BuildPipelineTool.ProduceBuild(string jsonSpec)` executes `BuildPipeline.BuildPlayer()` with the configured options and returns a structured `BuildReport` as JSON
6. Build report includes: success/failure result, total build time, total output size, errors array, warnings array, step-by-step breakdown (name and duration per step), and output path
7. `UnityBridge.BuildPipelineTool.GetBuildReport()` returns the last build report without re-running the build
8. Build validation runs before execution: all scenes exist, target platform is installed, output path is writable, scripting backend is available for the target
9. A new `build` command is added to `unity_bridge.sh` with subcommands: `config` (read current config), `produce` (execute build), `report` (get last report)
10. All output is structured JSON -- no raw text, no Unity console dumps

## Expected Development Work

### New Files

- **`Unity-Bridge/Editor/Tools/BuildPipelineTool.cs`** -- Static class in the `UnityBridge` namespace. Named to avoid conflict with Unity's own `BuildPipeline` class. Wraps `EditorUserBuildSettings`, `PlayerSettings`, `BuildPipeline.BuildPlayer()`, and `BuildReport` parsing.

  **Configuration APIs used**: `PlayerSettings.companyName`, `PlayerSettings.productName`, `PlayerSettings.SetApplicationIdentifier(buildTargetGroup, identifier)`, `PlayerSettings.SetScriptingBackend(buildTargetGroup, scriptingBackend)`, `PlayerSettings.GetApiCompatibilityLevel(buildTargetGroup)`, `EditorBuildSettings.scenes`, `EditorUserBuildSettings.SwitchActiveBuildTarget(buildTargetGroup, buildTarget)`.

  **Build execution**: `BuildPipeline.BuildPlayer(buildPlayerOptions)` where `BuildPlayerOptions` specifies scenes, locationPathName, target, and options (Development, AutoRunPlayer, etc.).

  **Report parsing**: `BuildReport` from `UnityEditor.Build.Reporting` -- iterate `report.steps` for timing, `report.summary` for result/totalTime/totalSize, and `report.GetFiles()` for output file details.

  **Platform switching**: `EditorUserBuildSettings.SwitchActiveBuildTarget()` triggers a domain reload. The tool must handle this gracefully -- either by detecting the reload and resuming, or by requiring a two-step workflow (switch platform, then build in a separate call).

### Modified Files

- **`.agent/tools/unity_bridge.sh`** -- Add `build` command with subcommands:
  - `build config` -- calls `UnityBridge.BuildPipelineTool.GetCurrentBuildConfig`
  - `build produce <jsonSpec>` -- calls `UnityBridge.BuildPipelineTool.ProduceBuild`
  - `build report` -- calls `UnityBridge.BuildPipelineTool.GetBuildReport`

### Config JSON (input to ConfigureBuild)

```json
{
  "target": "StandaloneWindows64",
  "scenes": [
    "Assets/Scenes/MainMenu.unity",
    "Assets/Scenes/Level01.unity",
    "Assets/Scenes/SampleScene.unity"
  ],
  "playerSettings": {
    "companyName": "MyCompany",
    "productName": "MyGame",
    "bundleIdentifier": "com.mycompany.mygame"
  },
  "options": {
    "scriptingBackend": "IL2CPP",
    "development": false,
    "apiCompatibilityLevel": "NET_Standard_2_0"
  }
}
```

### Build Options JSON (input to ProduceBuild)

```json
{
  "outputPath": "Builds/Win64/game.exe",
  "target": "StandaloneWindows64",
  "scenes": ["Assets/Scenes/SampleScene.unity"],
  "options": {
    "development": false,
    "scriptingBackend": "IL2CPP"
  },
  "playerSettings": {
    "companyName": "MyCompany",
    "productName": "MyGame"
  }
}
```

### Build Report JSON (output from ProduceBuild and GetBuildReport)

```json
{
  "success": true,
  "result": "Succeeded",
  "totalTime": 45.2,
  "totalSize": 52428800,
  "errors": [],
  "warnings": ["Shader 'Custom/Water' has errors on platform StandaloneWindows64"],
  "steps": [
    {"name": "Compile Scripts", "duration": 12.3},
    {"name": "Build Player Content", "duration": 25.1},
    {"name": "Post-process", "duration": 7.8}
  ],
  "outputPath": "Builds/Win64/game.exe",
  "outputFiles": [
    {"path": "Builds/Win64/game.exe", "size": 1048576},
    {"path": "Builds/Win64/game_Data/", "size": 51380224}
  ]
}
```

### Supported Build Targets

| Target String | BuildTarget Enum | BuildTargetGroup |
|---------------|-----------------|------------------|
| `StandaloneWindows64` | `BuildTarget.StandaloneWindows64` | `BuildTargetGroup.Standalone` |
| `StandaloneOSX` | `BuildTarget.StandaloneOSX` | `BuildTargetGroup.Standalone` |
| `StandaloneLinux64` | `BuildTarget.StandaloneLinux64` | `BuildTargetGroup.Standalone` |
| `Android` | `BuildTarget.Android` | `BuildTargetGroup.Android` |
| `iOS` | `BuildTarget.iOS` | `BuildTargetGroup.iOS` |
| `WebGL` | `BuildTarget.WebGL` | `BuildTargetGroup.WebGL` |

## Testing Protocol

1. `bash .agent/tools/unity_bridge.sh compile` -- Read `C:/temp/unity_bridge_output.txt`, confirm compilation succeeds.
2. Read current configuration:
   `bash .agent/tools/unity_bridge.sh execute UnityBridge.BuildPipelineTool.GetCurrentBuildConfig` -- Read output, verify it returns active platform, scene list, player settings, scripting backend, and API compatibility level.
3. Configure build settings:
   `bash .agent/tools/unity_bridge.sh execute UnityBridge.BuildPipelineTool.ConfigureBuild '["{ \"target\": \"StandaloneWindows64\", \"scenes\": [\"Assets/Scenes/SampleScene.unity\"], \"playerSettings\": { \"companyName\": \"TestCompany\", \"productName\": \"TestGame\" }, \"options\": { \"development\": true, \"scriptingBackend\": \"Mono\" } }"]'` -- Read output, verify settings applied.
4. Re-read config to confirm changes persisted:
   `bash .agent/tools/unity_bridge.sh execute UnityBridge.BuildPipelineTool.GetCurrentBuildConfig` -- Verify updated values.
5. Produce a development build:
   `bash .agent/tools/unity_bridge.sh execute UnityBridge.BuildPipelineTool.ProduceBuild '["{ \"outputPath\": \"C:/temp/TestBuild/Game.exe\", \"target\": \"StandaloneWindows64\", \"scenes\": [\"Assets/Scenes/SampleScene.unity\"], \"options\": { \"development\": true } }"]'` -- Read output, verify build report JSON with success, timing, file sizes.
6. Verify output files exist at specified path.
7. Get last build report:
   `bash .agent/tools/unity_bridge.sh execute UnityBridge.BuildPipelineTool.GetBuildReport` -- Verify it matches the report from step 5.
8. Test the bridge command shorthand:
   `bash .agent/tools/unity_bridge.sh build config` -- Read output, verify it returns the same config.
9. Test error handling -- produce a build with a nonexistent scene:
   `bash .agent/tools/unity_bridge.sh execute UnityBridge.BuildPipelineTool.ProduceBuild '["{ \"outputPath\": \"C:/temp/TestBuild/Bad.exe\", \"scenes\": [\"Assets/Scenes/NonExistent.unity\"] }"]'` -- Verify structured error response.

## Dependencies

- **Challenge 01 (Execute Endpoint)** -- All methods are invoked via `bash .agent/tools/unity_bridge.sh execute UnityBridge.BuildPipelineTool.<Method>`.
- **Challenge 02 (Scene Inventory)** -- Scene list validation uses scene inventory data to confirm that scenes specified in the build configuration actually exist as project assets.
