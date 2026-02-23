# Integration Testing Guide

Agent-driven integration testing: explore a running game, discover UI, and output repeatable test files.

## Overview

The integration testing framework lets agents:
1. Enter Play Mode and explore via screenshots + UI scanning
2. Discover interactive elements with screen positions
3. Build structured test files with element-based actions and assertions
4. Save tests that replay independently — no agent needed

## Tools

| Tool | Purpose |
|------|---------|
| `PlayModeUIScanner` | Runtime UI discovery — screen positions, text, interaction state |
| `IntegrationTestRunner` | Load and execute test JSON files, aggregate suite reports |
| `IntegrationTestWriter` | Validate and save test definition files |

## Agent Exploration-to-Test Workflow

```
1. bash .agent/tools/unity_bridge.sh play enter
2. execute PlayModeUIScanner.ScanUI → learn element paths + screen positions
3. screenshot → visual context
4. execute PlayModeUIScanner.GetInteractables → interactive elements only
5. input tap (coordinates from scan) → interact with elements
6. Repeat 2-5 until task is understood
7. Construct test JSON with tap_element + assert_* actions
8. execute IntegrationTestWriter.SaveTest → save to disk
9. execute IntegrationTestRunner.RunTest → verify it passes
10. bash .agent/tools/unity_bridge.sh play exit
```

## PlayModeUIScanner Methods

### ScanUI()

Full UI tree scan. Returns all canvases with computed screen positions.

```
execute UnityBridge.PlayModeUIScanner.ScanUI
```

Output includes:
- `screenWidth`, `screenHeight` — current resolution
- `canvases[]` — each canvas with nested element tree
- `interactableElements[]` — flat convenience list of interactive elements

Each element has:
- `name`, `path` — identity and hierarchy path
- `screenRect` — `{x, y, w, h}` in screen pixels (0,0 = bottom-left)
- `screenCenter` — `{x, y}` center point for tapping
- `text` — text content (TMP or legacy)
- `interactionType` — Button, Toggle, Slider, InputField, Dropdown, ScrollRect
- `interactable` — whether the element accepts input
- `alpha` — effective alpha (multiplied CanvasGroup chain)

### FindElement(nameOrPath)

Single element lookup by name or full hierarchy path.

```
execute UnityBridge.PlayModeUIScanner.FindElement '["Canvas/TabBar/HomeTab"]'
```

### GetInteractables()

Lightweight scan — only interactive elements with screen centers and labels.

```
execute UnityBridge.PlayModeUIScanner.GetInteractables
```

## Test Definition Format

```json
{
  "version": 1,
  "name": "Test name",
  "description": "What this test verifies",
  "tags": ["smoke", "navigation"],
  "setup": {
    "clearLogs": true,
    "waitAfterPlay": 1.0
  },
  "actions": [
    { "type": "scan_ui" },
    { "type": "tap_element", "path": "Canvas/Button" },
    { "type": "wait_seconds", "duration": 0.5 },
    { "type": "assert_active", "path": "Canvas/Panel", "expected": true },
    { "type": "assert_text", "path": "Canvas/Label", "expected": "Hello" },
    { "type": "screenshot", "path": "C:/temp/tests/result.png" }
  ],
  "teardown": {
    "screenshotOnFailure": true,
    "failurePath": "C:/temp/tests/failures/"
  }
}
```

## Action Types

### Original (from PlayModeInteractor)

| Type | Parameters |
|------|-----------|
| `enter_play` | — |
| `exit_play` | — |
| `wait_frames` | `count` |
| `wait_seconds` | `duration` |
| `wait_condition` | `gameObject`, `component`, `property`, `expected`, `timeout` |
| `input_tap` | `x`, `y`, `duration` |
| `input_hold` | `x`, `y`, `duration` |
| `input_drag` | `sx`, `sy`, `ex`, `ey`, `duration` |
| `input_key` | `key`, `duration` |
| `screenshot` | `path` |
| `check_state` | `gameObject`, `component`, `property`, `expected` |
| `check_log` | `contains` |
| `clear_logs` | — |

### New (element-based)

| Type | Parameters | Description |
|------|-----------|-------------|
| `scan_ui` | — | Runs PlayModeUIScanner.ScanUI() |
| `tap_element` | `path`, `duration` (opt) | Finds element, taps its screen center |
| `assert_active` | `path`, `expected` (bool) | Assert element active/inactive |
| `assert_text` | `path`, `expected`, `contains` (opt bool) | Assert text match |
| `assert_interactable` | `path` | Assert element is interactable |
| `assert_not_visible` | `path` | Assert element missing, inactive, or alpha=0 |
| `assert_screenshot` | `baseline`, `threshold` (opt) | Compare screenshot to baseline |

Use `tap_element` for resolution-independent interaction. Use `input_tap` when you need precise pixel coordinates.

## Running Tests

### Via Execute (manual, in Play Mode)

```
execute UnityBridge.IntegrationTestRunner.RunTest '["Assets/Tests/Integration/test.json"]'
execute UnityBridge.IntegrationTestRunner.RunSuite '["Assets/Tests/Integration"]'
execute UnityBridge.IntegrationTestRunner.RunByTag '["Assets/Tests/Integration","smoke"]'
execute UnityBridge.IntegrationTestRunner.ListTests '["Assets/Tests/Integration"]'
```

### Via Shell (automated lifecycle)

```bash
# Single test — handles compile + play enter/exit
bash .agent/tools/unity_bridge.sh integration_test Assets/Tests/Integration/test.json

# All tests in directory
bash .agent/tools/unity_bridge.sh integration_suite Assets/Tests/Integration
```

## Saving Tests

```
execute UnityBridge.IntegrationTestWriter.SaveTest '["{ JSON test definition }", "Assets/Tests/Integration/my_test.json"]'
execute UnityBridge.IntegrationTestWriter.ValidateTest '["{ JSON test definition }"]'
```

Validation checks:
- `name` field required
- `actions` array required and non-empty
- All action types recognized
- `path` field present for element-based actions
- `expected` field present for `assert_text`
- `baseline` field present for `assert_screenshot`
- Warning if no assertions exist

## Test Reports

Reports are saved to `C:/temp/integration_test_results/` with timestamps.

Suite report format:
```json
{
  "success": true,
  "directory": "Assets/Tests/Integration",
  "duration": 12.5,
  "summary": { "total": 3, "passed": 2, "failed": 1 },
  "tests": [ ... ]
}
```

## Test File Storage

Default location: `Assets/Tests/Integration/` (version-controlled). Tests can run from any path.
