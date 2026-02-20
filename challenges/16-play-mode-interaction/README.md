# Challenge 16: Play Mode Interaction Framework

## Overview
Build PlayModeInteractor (Brief Tool #14) — enters Play mode, executes a scripted sequence of inputs and state checks, captures screenshots at key moments, exits Play mode, and returns a structured interaction report. This enables fully automated gameplay testing.

## Brief Reference
Section 6.2 (Play Mode Interaction) — Full section: Enter/exit play mode, simulate input (Input System TestFixture), interact with UI (ExecuteEvents), control gameplay systems (WASD, abilities, menus), observe game state, capture visual output, wait for conditions.

## Problem Statement
The most critical capability gap: hitting Play, interacting with the game, and observing behavior currently requires a human. The bridge already supports individual commands (play enter, input tap, screenshot), but complex testing requires orchestrated sequences — click a button, wait for a scene load, verify state, take screenshot, input WASD movement, verify position changed. The PlayModeInteractor chains these into a single callable script.

## Success Criteria
1. `UnityBridge.PlayModeInteractor.RunSequence(string jsonScript)` executes a complete interaction script
2. Script supports actions: `enter_play`, `exit_play`, `wait_frames`, `wait_seconds`, `wait_condition`, `input_tap`, `input_hold`, `input_drag`, `input_key`, `screenshot`, `check_state`, `check_log`, `clear_logs`
3. `wait_condition` polls a game state expression (e.g., component property value) until true or timeout
4. `check_state` reads a component property at a GameObject path and compares against expected value
5. `check_log` verifies a specific log message appeared
6. Each action produces a result entry in the report (success/fail, duration, captured data)
7. Screenshots taken at marked points are saved and referenced in report
8. Sequence aborts on critical failure with partial report
9. Returns comprehensive report: actions executed, results per action, total duration, screenshots, pass/fail
10. Handles Unity errors during play mode gracefully (captures error, includes in report)

## Expected Development Work
### New Files
- `Unity-Bridge/Editor/Tools/PlayModeInteractor.cs` — Coroutine or EditorCoroutine-based sequence executor. Enters play mode, processes action queue one by one. For state checks, uses GameObject.Find + GetComponent + reflection to read property values. For waits, uses frame counting or time measurement. Collects results per action.

### Script Format
```json
{
  "name": "TestPauseMenu",
  "actions": [
    {"type": "enter_play"},
    {"type": "wait_seconds", "duration": 1.0},
    {"type": "screenshot", "path": "C:/temp/initial.png"},
    {"type": "input_tap", "x": 960, "y": 540},
    {"type": "wait_frames", "count": 10},
    {"type": "check_log", "contains": "Button Clicked"},
    {"type": "check_state", "gameObject": "GameManager", "component": "GameManager", "property": "isPaused", "expected": true},
    {"type": "screenshot", "path": "C:/temp/after_click.png"},
    {"type": "exit_play"}
  ]
}
```

### Report Format
```json
{
  "sequenceName": "TestPauseMenu",
  "overallResult": "Passed",
  "totalDuration": 3.456,
  "actions": [
    {"index": 0, "type": "enter_play", "result": "success", "duration": 0.5},
    {"index": 3, "type": "input_tap", "result": "success", "details": {"x": 960, "y": 540}},
    {"index": 5, "type": "check_log", "result": "success", "details": {"found": "Button Clicked"}},
    {"index": 6, "type": "check_state", "result": "success", "details": {"actual": true, "expected": true}}
  ],
  "screenshots": ["C:/temp/initial.png", "C:/temp/after_click.png"],
  "errors": []
}
```

## Testing Protocol
1. Ensure a scene with interactive elements exists (e.g., SpawnClickableButton from the sandbox)
2. `bash .agent/tools/unity_bridge.sh compile` — Confirm
3. Create a sequence script that: enters play, waits 1s, taps the button, checks logs for click message, screenshots, exits play
4. `bash .agent/tools/unity_bridge.sh execute UnityBridge.PlayModeInteractor.RunSequence '["{...script json...}"]'` — Read output
5. Verify all actions show "success"
6. Verify screenshot files exist
7. Test a sequence with a failing check_state — verify failure reported correctly

## Dependencies
- Challenge 01 (Execute Endpoint)
- Bridge input/screenshot/play mode capabilities (existing)
