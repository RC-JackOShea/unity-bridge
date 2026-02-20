# Challenge 27: End-to-End Workflow

## Overview

Execute the complete 15-step development cycle from Section 11 of the brief, implementing the concrete example from Section 12: a pause menu. This is an **integration challenge** -- no new tools are built. Instead, the agent must prove the entire lights-out system works end-to-end by using all previously built tools in a single, real development workflow. The pause menu specification is: "Add a pause menu UI with buttons for Resume, Settings, and Quit. Pressing Escape should toggle the pause menu. Time should freeze when paused."

## Brief Reference

Section 11 (Flow Sequence) -- The complete 15-step development cycle from specification receipt through delivery. Section 12 (Concrete Example -- Pause Menu) -- Full worked example showing introspection, planning, code generation, asset creation, compilation, static analysis, testing, Play Mode interaction, visual validation, iteration, build, and delivery phases applied to a pause menu feature.

## Problem Statement

Individual tools have been built and tested in isolation across Challenges 01-26. Each tool solves a specific problem: scene introspection, UI building, code generation, code review, test running, screenshot validation, build production, and so on. But the lights-out vision requires these tools to work together seamlessly in a real development workflow. This challenge is the integration proof. The agent must follow the exact flow sequence from Section 11, executing all 15 steps end-to-end to implement a pause menu feature. Every applicable tool from previous challenges must be used at the appropriate step. If any step fails, the agent must diagnose and iterate -- just as the flow sequence prescribes. No new editor tools are created in `Unity-Bridge/Editor/Tools/`. This challenge creates gameplay code and assets in the sandbox project.

## Success Criteria

1. **Introspection phase completes** -- Agent runs SceneInventoryTool, UIInventoryTool, and CodebaseAnalyzer to understand the current project state (scenes, existing UI, code conventions, Input System usage) before writing any code
2. **Code generated following conventions** -- PauseMenuController.cs is created via CodeGenerator (or direct write informed by CodebaseAnalyzer output), using project-consistent naming, namespaces, and patterns; includes Escape key detection via Input System, `Time.timeScale` control, and menu toggle logic
3. **UI created with brand consistency** -- Pause menu UI built via UIBuilder (or PrefabCreator) with brand-consistent colors and fonts detected by BrandSystem; includes Canvas, semi-transparent background overlay, centered panel, and three buttons (Resume, Settings, Quit)
4. **Compilation passes** -- All new code compiles successfully via the bridge `compile` command with zero errors
5. **Code review passes** -- CodeReviewer runs on all generated code and reports zero critical issues
6. **Play Mode interaction verifies pause/resume works** -- PlayModeInteractor executes the full sequence: enter Play Mode, press Escape, verify menu is visible, click Resume, verify menu is hidden
7. **Time.timeScale toggles correctly** -- During Play Mode testing, `Time.timeScale == 0` when the pause menu is open and `Time.timeScale == 1` when the pause menu is closed
8. **Screenshots captured at multiple resolutions** -- ScreenshotValidator captures the pause menu at a minimum of two resolutions (e.g., 1920x1080 and 1280x720) and saves them for validation
9. **Visual validation passes** -- VisualValidator checks captured screenshots for overlap issues, contrast problems, and correct element positioning with no critical failures
10. **Complete structured delivery report generated** -- A JSON delivery report is produced containing: files created, tests run and results, code review summary, screenshot paths, visual validation scores, and an overall confidence assessment

## Expected Development Work

### New Files Created During Workflow

No new files are created in `Unity-Bridge/Editor/Tools/`. This challenge creates gameplay code in the sandbox project:

- **`Unity-Sandbox/Unity-Sandbox/Assets/Scripts/UI/PauseMenuController.cs`** -- MonoBehaviour handling Escape key input (via Input System), menu toggle visibility, and `Time.timeScale` management
- **`Unity-Sandbox/Unity-Sandbox/Assets/Prefabs/UI/PauseMenu.prefab`** -- Pause menu prefab (or scene-instantiated via UIBuilder) with Canvas, overlay background, centered panel, Resume/Settings/Quit buttons
- **`Unity-Sandbox/Unity-Sandbox/Assets/Tests/Editor/PauseMenuTests.cs`** -- Edit Mode tests for PauseMenuController logic (time scale toggling, menu state management)
- **`Unity-Sandbox/Unity-Sandbox/Assets/Tests/PlayMode/PauseMenuPlayTests.cs`** -- Play Mode integration tests for the full pause/resume interaction

### Workflow Sequence (Section 11 Flow)

| Step | Action | Tools Used |
|------|--------|------------|
| 1 | Receive specification | (The pause menu spec above) |
| 2 | Project introspection | SceneInventoryTool, UIInventoryTool, CodebaseAnalyzer |
| 3 | Plan generation | Agent determines files to create, prefabs to build, UI to lay out |
| 4 | Code generation | CodeGenerator to write PauseMenuController.cs matching project conventions |
| 5 | Asset creation | UIBuilder to create pause menu UI with brand-consistent colors/fonts |
| 6 | Compilation check | Bridge `compile` command, check for errors |
| 7 | Static analysis | CodeReviewer on all new code |
| 8 | Edit Mode testing | TestRunner for Edit Mode tests on PauseMenuController logic |
| 9 | Play Mode testing | PlayModeInteractor: enter play, press Escape, verify menu visible, verify `Time.timeScale==0`, click Resume, verify menu hidden, verify `Time.timeScale==1` |
| 10 | Visual validation | ScreenshotValidator at multiple resolutions, VisualValidator checks |
| 11 | Iteration | If anything fails, fix and re-run from the appropriate step |
| 12 | Build production | BuildPipeline to produce a build with the pause menu included |
| 13 | Delivery | Structured report: files created, tests passed, screenshots, confidence assessment |

### Delivery Report Format

```json
{
  "task": "Pause Menu Implementation",
  "specification": "Add a pause menu UI with buttons for Resume, Settings, and Quit. Pressing Escape should toggle the pause menu. Time should freeze when paused.",
  "filesCreated": [
    "Assets/Scripts/UI/PauseMenuController.cs",
    "Assets/Prefabs/UI/PauseMenu.prefab",
    "Assets/Tests/Editor/PauseMenuTests.cs",
    "Assets/Tests/PlayMode/PauseMenuPlayTests.cs"
  ],
  "introspection": {
    "scenesFound": 1,
    "existingUI": [],
    "conventionsDetected": ["camelCase fields", "namespace per folder", "Input System v1.x"]
  },
  "testResults": {
    "editMode": {"passed": 3, "failed": 0, "skipped": 0},
    "playMode": {"passed": 4, "failed": 0, "skipped": 0}
  },
  "codeReview": {"critical": 0, "warnings": 0, "info": 1},
  "visualValidation": {
    "overallScore": 95,
    "issues": [],
    "resolutionsTested": ["1920x1080", "1280x720"]
  },
  "screenshots": {
    "1920x1080": "C:/temp/pause_menu_1080p.png",
    "1280x720": "C:/temp/pause_menu_720p.png"
  },
  "buildStatus": "success",
  "confidence": "high"
}
```

## Testing Protocol

This challenge IS the test -- the entire workflow is the testing protocol:

1. **Introspection** -- Execute SceneInventoryTool, UIInventoryTool, CodebaseAnalyzer via `bash .agent/tools/unity_bridge.sh execute` and read `C:/temp/unity_bridge_output.txt` after each. Verify meaningful project data is returned.
2. **Code generation** -- Write PauseMenuController.cs. Compile via `bash .agent/tools/unity_bridge.sh compile`. Read output, confirm "Compilation completed" with zero errors.
3. **UI creation** -- Build pause menu UI via UIBuilder. Compile again. Verify no errors.
4. **Code review** -- Run CodeReviewer on new files via `bash .agent/tools/unity_bridge.sh execute`. Read output, verify zero critical issues.
5. **Edit Mode tests** -- Run TestRunner for Edit Mode tests via `bash .agent/tools/unity_bridge.sh execute`. Read output, verify all tests pass.
6. **Play Mode interaction** -- Enter Play Mode via `bash .agent/tools/unity_bridge.sh play enter`. Read output. Send Escape input. Take screenshot to verify menu is visible. Send Resume tap. Take screenshot to verify menu is hidden. Exit Play Mode via `bash .agent/tools/unity_bridge.sh play exit`. Read output after every command.
7. **Visual validation** -- Capture screenshots at multiple resolutions. Run VisualValidator. Read output, verify no critical issues.
8. **Build** -- Trigger build via BuildPipeline tool. Read output, verify build succeeds.
9. **Delivery** -- Compile the structured JSON delivery report from all collected results.
10. **Iteration** -- If any step above fails, diagnose the issue, fix the code or assets, recompile, and re-run from the appropriate step.

## Dependencies

- **All previous challenges (01-26)** that have been completed, or at minimum:
- **Challenge 01 (Execute Endpoint)** -- All tool invocations go through the execute endpoint
- **Challenge 02 (Scene Inventory)** -- Project introspection: understanding existing scenes
- **Challenge 10 (UI Inventory)** -- Project introspection: understanding existing UI elements
- **Challenge 11 (UI Builder)** -- Creating the pause menu UI with proper layout and styling
- **Challenge 12 (Brand System)** -- Detecting and applying brand-consistent colors and fonts
- **Challenge 15 (Test Runner)** -- Running Edit Mode and Play Mode tests
- **Challenge 16 (Play Mode Interaction)** -- Entering Play Mode, sending input, observing results
- **Challenge 17 (Game State Observer)** -- Verifying Time.timeScale and menu visibility state
- **Challenge 18 (Visual Validation Pipeline)** -- Screenshot capture and visual validation
- **Challenge 20 (Codebase Analyzer)** -- Detecting project conventions before code generation
- **Challenge 21 (Code Generator)** -- Generating PauseMenuController.cs with correct conventions
- **Challenge 22 (Code Reviewer)** -- Static analysis of generated code
- **Challenge 23 (Build Pipeline)** -- Producing a build with the pause menu included
