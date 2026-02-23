# BuildPipelineTool

Build configuration, execution, and report parsing. Reads and sets build settings, runs player builds, and returns structured build reports with errors, warnings, and timing.

## Key Methods

| Method | Description |
|--------|-------------|
| `GetCurrentBuildConfig()` | Return current build target, scenes, player settings, and options |
| `ConfigureBuild(jsonSpec)` | Apply build configuration (scenes, player settings, target, options) |
| `ProduceBuild(jsonSpec)` | Execute a player build and return the build report |
| `GetBuildReport()` | Retrieve the last build report (cached from `ProduceBuild`) |

## Usage

```bash
bash .agent/tools/unity_bridge.sh execute UnityBridge.BuildPipelineTool.GetCurrentBuildConfig
bash .agent/tools/unity_bridge.sh execute UnityBridge.BuildPipelineTool.ConfigureBuild '<jsonSpec>'
bash .agent/tools/unity_bridge.sh execute UnityBridge.BuildPipelineTool.ProduceBuild '<jsonSpec>'
bash .agent/tools/unity_bridge.sh execute UnityBridge.BuildPipelineTool.GetBuildReport
```

## ConfigureBuild Spec

```json
{
  "scenes": ["Assets/Scenes/SampleScene.unity"],
  "target": "StandaloneWindows64",
  "playerSettings": {
    "companyName": "MyCompany",
    "productName": "MyGame",
    "bundleVersion": "1.0.0"
  },
  "options": {
    "development": true,
    "scriptingBackend": "IL2CPP"
  }
}
```

## ProduceBuild Spec

```json
{
  "outputPath": "Builds/MyGame.exe",
  "target": "StandaloneWindows64",
  "scenes": ["Assets/Scenes/SampleScene.unity"],
  "options": {
    "development": false
  }
}
```

If `scenes` is omitted, the currently configured `EditorBuildSettings.scenes` are used.

## Response Formats

**GetCurrentBuildConfig:**
```json
{
  "activeBuildTarget": "StandaloneWindows64",
  "buildTargetGroup": "Standalone",
  "scriptingBackend": "Mono2x",
  "apiCompatibilityLevel": "NET_Unity_4_8",
  "scenes": [{"path": "Assets/Scenes/SampleScene.unity", "enabled": true}],
  "playerSettings": {"companyName": "DefaultCompany", "productName": "MyProject", "bundleVersion": "0.1"},
  "development": false
}
```

**ProduceBuild:**
```json
{
  "success": true,
  "result": "Succeeded",
  "totalTime": 45.2,
  "totalSize": 52428800,
  "errors": [],
  "warnings": ["Shader XYZ compiled with warnings"],
  "steps": [{"name": "Building Player", "duration": 30.1}],
  "outputPath": "Builds/MyGame.exe"
}
```

## Examples

```bash
# Check current build configuration
bash .agent/tools/unity_bridge.sh execute UnityBridge.BuildPipelineTool.GetCurrentBuildConfig

# Configure for Windows standalone dev build
bash .agent/tools/unity_bridge.sh execute UnityBridge.BuildPipelineTool.ConfigureBuild '{"target":"StandaloneWindows64","options":{"development":true}}'

# Produce a build
bash .agent/tools/unity_bridge.sh execute UnityBridge.BuildPipelineTool.ProduceBuild '{"outputPath":"Builds/MyGame.exe","target":"StandaloneWindows64"}'
```

## Common Pitfalls

- `ProduceBuild` runs a full Unity build -- this is slow (30s to minutes). Use sparingly.
- Scene paths must exist in the project. The tool validates each scene path via `AssetDatabase.LoadAssetAtPath`.
- `target` uses Unity's `BuildTarget` enum names: `StandaloneWindows64`, `StandaloneOSX`, `Android`, `iOS`, `WebGL`, etc.
- Switching build targets with `ConfigureBuild` can trigger a domain reload. Wait and re-compile after switching.
- `GetBuildReport` only returns data after a `ProduceBuild` call in the same session. It is not persisted across Unity restarts.
