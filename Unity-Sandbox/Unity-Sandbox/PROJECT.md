# Unity Sandbox — Agent Project Configuration

## Project Identity

- **Root namespace:** `Game`
- **Main scene:** `Assets/Scenes/SampleScene.unity`
- **Prefab directory:** `Assets/Resources/Prefabs/`
- **Materials directory:** `Assets/Materials/`
- **ScriptableObjects directory:** `Assets/Resources/ScriptableObjects/`

## Setup Tools

Project-specific editor tools for asset creation and scene population. These are NOT part of the bridge package — they live in the project's `Assets/Editor/` directory.

| Tool | Invoke Command | Description |
|------|---------------|-------------|
| `SandboxPrefabSetup` | `execute Game.Editor.Setup.SandboxPrefabSetup.CreateAllPrefabs` | Creates all game prefabs, materials, and ScriptableObject assets |
| `SandboxPrefabSetup` | `execute Game.Editor.Setup.SandboxPrefabSetup.ValidatePrefabs` | Validates all expected prefab and asset files exist |
| `SandboxSceneSetup` | `execute Game.Editor.Setup.SandboxSceneSetup.PopulateScene` | Places prefab instances into SampleScene with correct positions |
| `SandboxSceneSetup` | `execute Game.Editor.Setup.SandboxSceneSetup.ValidateScene` | Validates scene has all expected objects with intact prefab links |

## Bridge Test Harnesses

Agent-callable test methods for runtime verification.

- **Namespace:** `Game.BridgeTests`
- **Location:** `Assets/Scripts/BridgeTests/`
- **Convention:** `public static class <Feature>Tests` with methods returning JSON strings

| Test Class | Key Methods | What It Tests |
|-----------|-------------|---------------|
| `ScoreSystemTests` | `GetScoreState()`, `RunScoreTest()` | Score counter system state and behavior |

## Prefabs Inventory

| Prefab | Path | Description |
|--------|------|-------------|
| `EventSystem` | `Assets/Resources/Prefabs/EventSystem.prefab` | EventSystem + InputSystemUIInputModule for UI input |
| `TestPlayer` | `Assets/Resources/Prefabs/TestPlayer.prefab` | Capsule with Rigidbody, tagged "Player" |
| `ScoreTriggerZone` | `Assets/Resources/Prefabs/ScoreTriggerZone.prefab` | Box trigger with gold sphere visual, ScoreTrigger component |
| `UICanvas` | `Assets/Resources/Prefabs/UICanvas.prefab` | ScreenSpaceOverlay canvas with ScoreGroup and PauseGroup |

## Materials

| Material | Path | Description |
|----------|------|-------------|
| `GoldSphere` | `Assets/Materials/GoldSphere.mat` | Gold-colored material for ScoreTriggerZone visual |

## ScriptableObjects

| Asset | Path | Description |
|-------|------|-------------|
| `ScoreManager` | `Assets/Resources/ScriptableObjects/ScoreManager.asset` | Score state management |

## Scene Layout

Objects placed in the main scene and their positions:

| Object | Position | Notes |
|--------|----------|-------|
| `TestPlayer` | `(0, 1, 0)` | Player capsule above ground plane |
| `ScoreTriggerZone` | `(3, 0.5, 3)` | Gold sphere trigger zone |
| `EventSystem` | `(0, 0, 0)` | Required for UI input |
| `UICanvas` | `(0, 0, 0)` | Single canvas for all screen-space UI |

## Project-Specific Notes

- **Single Canvas rule:** All screen-space UI lives under UICanvas. Never add a second ScreenSpace canvas.
- **PauseGroup overlay** starts hidden (`SetActive(false)`). `PauseMenuController` toggles visibility.
- **ScoreTrigger** uses SerializedObject fields: `scoreManager`, `pointValue` (1), `maxUses` (10).
- **Test harness namespace:** `Game.BridgeTests`, location: `Assets/Scripts/BridgeTests/`.
