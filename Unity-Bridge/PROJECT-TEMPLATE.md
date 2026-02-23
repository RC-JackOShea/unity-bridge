# [Project Name] — Agent Project Configuration

> Copy this file to your Unity project root as `PROJECT.md` and fill in each section.

## Project Identity

- **Root namespace:** `<YourNamespace>` (e.g., `Game`, `MyApp`)
- **Main scene:** `<path>` (e.g., `Assets/Scenes/MainScene.unity`)
- **Prefab directory:** `<path>` (e.g., `Assets/Resources/Prefabs/`)
- **Materials directory:** `<path>` (e.g., `Assets/Materials/`)
- **ScriptableObjects directory:** `<path>` (e.g., `Assets/Resources/ScriptableObjects/`)

## Setup Tools

Project-specific editor tools for asset creation and scene population. These are NOT part of the bridge package — they live in your project's `Assets/Editor/` directory.

| Tool | Invoke Command | Description |
|------|---------------|-------------|
| `<ClassName>` | `execute <Namespace.Class.Method>` | What it does |

## Bridge Test Harnesses

Agent-callable test methods for runtime verification.

- **Namespace:** `<ProjectNamespace>.BridgeTests`
- **Location:** `Assets/Scripts/BridgeTests/`
- **Convention:** `public static class <Feature>Tests` with methods returning JSON strings

| Test Class | Key Methods | What It Tests |
|-----------|-------------|---------------|
| `<Feature>Tests` | `GetState()`, `RunTest()` | Description |

## Prefabs Inventory

| Prefab | Path | Description |
|--------|------|-------------|
| `<Name>` | `Assets/Resources/Prefabs/<Name>.prefab` | What it is |

## Scene Layout

Objects placed in the main scene and their positions:

| Object | Position | Notes |
|--------|----------|-------|
| `<Name>` | `(x, y, z)` | Purpose |

## Project-Specific Notes

Add any project-specific conventions, dependencies, or gotchas here.
