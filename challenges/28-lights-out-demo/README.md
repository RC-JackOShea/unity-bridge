# Challenge 28: Lights-Out Demonstration

## Overview

The final challenge: a completely autonomous demonstration where the agent receives a novel specification and executes the full development cycle without any human intervention. This proves the core vision from Section 2 of the brief -- "a system where the human provides specifications, direction, and acceptance criteria, and the agentic layer handles everything from code generation through build verification." The specification is intentionally different from the Challenge 27 pause menu to prove the system is general-purpose, not hard-coded to a single workflow.

## Brief Reference

Section 2 (Why This Exists) -- "A system where the human provides specifications, direction, and acceptance criteria, and the agentic layer handles everything." Section 3 -- All six core design principles (token efficiency, programmatic speed, deep introspection, visual competence, full lifecycle coverage, lights-out operation). Section 11 (Flow Sequence) -- The 15-step development cycle the agent should follow naturally. Section 13 -- Constraints and edge cases the agent must handle autonomously.

## Problem Statement

Challenge 27 demonstrated the end-to-end workflow with a well-defined task and step-by-step guidance mapping to Section 11. This challenge proves the system is truly autonomous. The agent receives a novel feature specification -- one it has never seen before in the challenge series -- and must independently decide how to introspect, plan, implement, test, validate, build, and deliver. There is no prescribed step order, no hint about which tools to use at each phase, and no human available to answer questions or resolve issues. This is the capstone proof that the lights-out agentic layer works.

This challenge differs from Challenge 27 in four key ways:

1. **More complex specification** -- The spec involves a ScriptableObject, a prefab, UI, and tests (more moving parts than a single pause menu)
2. **Fully autonomous decisions** -- The agent must make all architectural and implementation decisions without step-by-step guidance
3. **Unexpected issue handling** -- The agent must diagnose and fix problems without human help
4. **Natural tool usage** -- The agent should use the orchestration flow naturally based on the situation, not follow a predetermined script

## Demonstration Specification

**"Create a simple score counter system: a ScriptableObject-based score manager, a UI display showing the current score in the top-right corner, a trigger zone prefab that adds points when the player enters it, and Edit Mode tests for the score manager logic. The UI should match any existing brand spec."**

## Success Criteria

1. **Autonomous execution** -- Agent completes all steps from introspection through delivery without asking for human input, clarification, or intervention at any point
2. **ScriptableObject score manager created and working** -- A ScoreManager ScriptableObject asset tracks score as an integer, exposes an `AddScore` method, and fires an `onScoreChanged` event
3. **UI displays score correctly** -- A ScoreDisplay (or ScoreHUD) MonoBehaviour in the top-right corner of the screen shows the current score, subscribes to `onScoreChanged`, and updates the text when the score changes
4. **Trigger prefab adds points on enter** -- A ScoreTrigger prefab with a collider (set as trigger) and a ScoreTrigger MonoBehaviour detects `OnTriggerEnter`, adds its configured point value to the ScoreManager, and destroys itself
5. **Edit Mode tests pass** -- Unit tests for ScoreManager logic pass: score addition, initial state, event firing
6. **Play Mode test verifies score increment** -- Integration test enters Play Mode, triggers a score zone, and verifies the score value increases and the UI updates
7. **Code review has no critical issues** -- CodeReviewer runs on all generated code and reports zero critical issues
8. **Visual validation passes** -- Screenshot of the score UI is captured and VisualValidator confirms the HUD element is visible, correctly positioned in the top-right corner, and legible
9. **Build succeeds** -- The project builds successfully for StandaloneWindows64 with all new code and assets included
10. **Delivery report is comprehensive** -- A structured report includes: complete file list, test results (Edit Mode and Play Mode), code review summary, screenshot paths, build status, and a confidence level assessment

## Expected Development Work

### Files the Agent Should Create

The agent determines the exact architecture autonomously. The following is the expected outcome, not a prescription:

- **`Assets/Scripts/Score/ScoreManager.cs`** -- ScriptableObject singleton; tracks score integer, provides `AddScore(int points)` method, fires `onScoreChanged` event (UnityEvent or C# event)
- **`Assets/Scripts/Score/ScoreTrigger.cs`** -- MonoBehaviour with configurable point value, `OnTriggerEnter` detecting objects tagged "Player", calls `ScoreManager.AddScore`, destroys self after trigger
- **`Assets/Scripts/UI/ScoreDisplay.cs`** -- MonoBehaviour with TextMeshPro reference, subscribes to `ScoreManager.onScoreChanged`, updates displayed text
- **`Assets/Prefabs/Collectibles/ScoreTriggerZone.prefab`** -- Prefab with BoxCollider (isTrigger=true), ScoreTrigger component, visual indicator (e.g., colored cube or sphere)
- **`Assets/ScriptableObjects/ScoreManager.asset`** -- ScoreManager ScriptableObject instance
- **`Assets/Tests/Editor/ScoreManagerTests.cs`** -- Edit Mode unit tests for ScoreManager logic (add score, initial value, event subscription)
- **`Assets/Tests/PlayMode/ScoreTriggerTests.cs`** -- Play Mode integration tests for the trigger-to-UI chain

### Expected Autonomous Flow

The agent should naturally follow a flow similar to this, using its own judgment about ordering and tool selection:

1. **Introspect project** -- Understand what exists: scenes, existing scripts, UI framework, render pipeline, Input System version
2. **Plan architecture** -- Decide on ScoreManager (ScriptableObject), ScoreDisplay (MonoBehaviour), ScoreTrigger (MonoBehaviour), prefab structure
3. **Generate code** -- Use CodeGenerator for all scripts, following conventions detected by CodebaseAnalyzer
4. **Create assets** -- Use PrefabCreator for the trigger zone prefab, UIBuilder for the score display HUD
5. **Compile and fix** -- Compile via bridge, diagnose and fix any compilation errors
6. **Review code** -- Run CodeReviewer on all generated files
7. **Run Edit Mode tests** -- Use TestRunner for ScoreManager unit tests
8. **Play Mode test** -- Enter Play Mode, trigger a score zone, verify UI updates
9. **Visual validation** -- Screenshot the score HUD, run VisualValidator
10. **Build** -- Produce a build via BuildPipeline
11. **Deliver** -- Generate the full structured delivery report

### This Challenge is the Agent's Final Exam

This README defines WHAT the outcome should be, not HOW the agent should implement it. The agent must use its judgment, leveraging the tools from Challenges 01-26 and the workflow experience from Challenge 27, to determine the best approach. The agent's ability to make good decisions autonomously is part of what this challenge evaluates.

## Testing Protocol

The agent defines and executes its own testing protocol. The expected minimum coverage:

1. **Edit Mode tests** for ScoreManager logic -- score starts at zero, `AddScore` increments correctly, `onScoreChanged` event fires on score change
2. **Play Mode test** for the collectible trigger interaction -- instantiate a ScoreTrigger and a Player-tagged object, move them into contact, verify score incremented and trigger destroyed
3. **Visual verification** of the score HUD via screenshot -- HUD visible in top-right corner, text is legible
4. **Code review** of all generated files -- zero critical issues
5. **Successful build production** -- StandaloneWindows64 build completes without errors

## Dependencies

- **All previous challenges (01-27)** -- This is the capstone challenge that exercises the full toolchain
- **Challenge 27 (End-to-End Workflow)** -- The workflow experience and any lessons learned from the guided run directly inform autonomous execution
- Every tool built across the challenge series may be used at the agent's discretion
