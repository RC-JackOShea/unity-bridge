# PlayModeInteractor

Scripted Play Mode interaction sequences. Executes an ordered list of actions (inputs, waits, state checks, screenshots, log checks) and returns a structured pass/fail report. Designed for automated end-to-end testing.

## Key Methods

| Method | Description |
|--------|-------------|
| `RunSequence(jsonScript)` | Execute a named sequence of actions and return results |

## Usage

```bash
bash .agent/tools/unity_bridge.sh execute UnityBridge.PlayModeInteractor.RunSequence '<jsonScript>'
```

**Important:** You must enter Play Mode with `play enter` before calling `RunSequence`. The interactor verifies Play Mode is active but cannot transition into it.

## Action Types

| Action | Key Parameters | Description |
|--------|---------------|-------------|
| `enter_play` | -- | Verify Play Mode is active (does not enter it) |
| `exit_play` | -- | Verify Play Mode is stopped (does not exit it) |
| `wait_frames` | `count` | Approximate frame wait (default 10) |
| `wait_seconds` | `duration` | Sleep for N seconds (default 1.0) |
| `wait_condition` | `gameObject`, `component`, `property`, `expected`, `timeout` | Poll until a condition is met or timeout |
| `input_tap` | `x`, `y`, `duration` | Tap at screen coordinates |
| `input_hold` | `x`, `y`, `duration` | Hold/long-press at coordinates |
| `input_drag` | `sx`/`startX`, `sy`/`startY`, `ex`/`endX`, `ey`/`endY`, `duration` | Drag gesture |
| `input_key` | `key`, `duration` | Simulate key press (placeholder) |
| `screenshot` | `path` | Capture screenshot to file |
| `check_state` | `gameObject`, `component`, `property`, `expected` | Assert a runtime value |
| `check_log` | `contains`/`text` | Assert a log message was captured |
| `clear_logs` | -- | Clear captured log buffer |

Any action can set `"critical": true` to abort the sequence on failure.

## JSON Script Structure

```json
{
  "name": "ButtonClickTest",
  "actions": [
    {"type": "enter_play"},
    {"type": "clear_logs"},
    {"type": "wait_seconds", "duration": 0.5},
    {"type": "input_tap", "x": 120, "y": 60},
    {"type": "wait_seconds", "duration": 0.3},
    {"type": "check_log", "contains": "Button clicked", "critical": true},
    {"type": "screenshot", "path": "C:/temp/after_click.png"},
    {"type": "check_state", "gameObject": "ScoreText", "component": "TextMeshProUGUI", "property": "text", "expected": "Score: 1"}
  ]
}
```

## Response Format

```json
{
  "success": true,
  "sequenceName": "ButtonClickTest",
  "overallResult": "Passed",
  "totalDuration": 1.23,
  "actions": [
    {"index": 0, "type": "enter_play", "result": "success", "duration": 0.001, "details": "Play Mode is active"},
    {"index": 1, "type": "input_tap", "result": "success", "duration": 0.2, "details": "Tapped at (120, 60)"}
  ],
  "screenshots": ["C:/temp/after_click.png"],
  "errors": []
}
```

## Examples

```bash
# Full click-and-verify sequence
bash .agent/tools/unity_bridge.sh execute UnityBridge.PlayModeInteractor.RunSequence '{"name":"TapTest","actions":[{"type":"enter_play"},{"type":"wait_seconds","duration":0.5},{"type":"input_tap","x":500,"y":300},{"type":"wait_seconds","duration":0.3},{"type":"check_log","contains":"Clicked"},{"type":"screenshot","path":"C:/temp/tap_result.png"}]}'

# Wait for a condition then screenshot
bash .agent/tools/unity_bridge.sh execute UnityBridge.PlayModeInteractor.RunSequence '{"name":"WaitForScore","actions":[{"type":"enter_play"},{"type":"wait_condition","gameObject":"ScoreManager","component":"ScoreSystem","property":"Score","expected":"10","timeout":5.0,"critical":true},{"type":"screenshot","path":"C:/temp/score10.png"}]}'
```

## Common Pitfalls

- You **must** call `play enter` before `RunSequence` and `play exit` after -- the interactor cannot transition Play Mode itself.
- `check_state` finds GameObjects via `GameObject.Find()` which only finds active objects. For hierarchical paths, use `/`-separated names.
- `wait_condition` blocks the thread with polling. Keep timeout values reasonable (under 10s).
- `input_key` is a placeholder and does not simulate real key presses via the Input System.
- All captured logs are cleared when a new sequence starts. Use `clear_logs` within a sequence to reset mid-run.
