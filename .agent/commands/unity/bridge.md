# Unity Bridge Command

## Overview
The Unity Bridge is a shell script tool that enables communication between external systems and Unity. This command instructs the agent on how to discover, validate, and use the Unity Bridge tool.

## Tool Discovery Protocol

### Context
The agent must first assess its context to check if the location has been found previously.

### Required Locations
The agent must check for the Unity Bridge tool at:

1. **Project Root**: `<project_root>/.agent/tools/unity_bridge.sh`

Note: Once the tool has been located a first time, the location MUST be saved / cached to context for future use.

## Reading Output

After every invocation, read `C:/temp/unity_bridge_output.txt` for results. This is necessary because echo stdout is not visible in the Claude Code Bash tool on Windows. The output file is overwritten on each invocation, so read it immediately after each command.

**Two-step pattern:**
1. Run: `bash .agent/tools/unity_bridge.sh <command> [args]`
2. Read: Use the Read tool on `C:/temp/unity_bridge_output.txt`

## Discovery Process
When the Unity Bridge command is invoked, the agent must:

1. **Run Tool Discovery Protocol**: Utilise the protocol to find the appropriate file
3. **Validation**: Verify the found file is executable and accessible
4. **Error Handling**: If the tool is not found, raise an error

## Error Handling

### Tool Not Found
If the Unity Bridge tool is not found, the agent must:

```
ERROR: Unity Bridge tool not found

   Expected location:
   - <project_root>/.agent/tools/unity_bridge.sh

   Please ensure the unity_bridge.sh script is available before proceeding.
```

### Tool Found Acknowledgment
When the tool is successfully located, the agent must acknowledge:

```
Unity Bridge tool located at: <discovered_path>
   Ready to execute Unity Bridge commands.
```

## Usage Instructions

### Tool Execution
Once the Unity Bridge tool is discovered and validated:

1. **Always use the discovered script path** for Unity Bridge operations
2. **Pass through all command arguments** to the script
3. **Read output from `C:/temp/unity_bridge_output.txt`** after every invocation
4. **Handle script errors** appropriately

### Command Format
```bash
bash <discovered_path>/unity_bridge.sh [arguments]
```

## Implementation Guidelines

### For Claude and Other Agents
- **Mandatory Discovery**: Always perform tool discovery before first use
- **Cache Location**: Remember the discovered path for the session
- **Always Read Output File**: After every invocation, read `C:/temp/unity_bridge_output.txt`
- **Re-validate**: Check tool existence if commands start failing
- **User Feedback**: Provide clear status messages during discovery
- **Never use raw curl**: All Unity interactions go through the bridge script

### Example Discovery Code Pattern
```bash
# Check project root
if [ -f ".agent/tools/unity_bridge.sh" ]; then
    UNITY_BRIDGE_PATH=".agent/tools/unity_bridge.sh"
else
    echo "ERROR: Unity Bridge tool not found in expected location"
    exit 1
fi

echo "Unity Bridge tool located at: $UNITY_BRIDGE_PATH"
```

## Input Events Reference

### Coordinate System

All input coordinates are **screen pixels**. `(0, 0)` is the **bottom-left** corner of the Game view. `(Screen.width, Screen.height)` is the top-right. Use the `screenshot` command to determine the current Game view resolution (reported in the result as `width` and `height`).

### Input Commands

#### `input tap X Y [duration]`
Simulates a single tap/click at screen coordinates `(X, Y)`. Duration is optional (default `0.05s`).

- **UI elements:** Uses `ExecuteEvents` — raycasts into the UI, walks up the hierarchy to find the click handler (e.g., `Button`), and fires `pointerDown` → `pointerUp` → `pointerClick`.
- **Non-UI:** Falls back to queuing a mouse press/release on `Mouse.current`.

```bash
bash .agent/tools/unity_bridge.sh input tap 120 60
```

#### `input hold X Y [duration]`
Simulates a long press at `(X, Y)` for `duration` seconds (default `1.0s`). Fires `pointerDown` immediately, then `pointerUp` + `pointerClick` after the duration elapses.

```bash
bash .agent/tools/unity_bridge.sh input hold 500 400 2.0
```

#### `input multi_tap X Y [count] [interval]`
Simulates multiple taps at `(X, Y)`. Default count is `2`, default interval is `0.15s`.

```bash
bash .agent/tools/unity_bridge.sh input multi_tap 300 300 3 0.2
```

#### `input drag SX SY EX EY [duration]`
Drags from `(SX, SY)` to `(EX, EY)` over `duration` seconds (default `0.3s`). Uses virtual Touchscreen device with touch `Began` → `Moved` → `Ended` events.

```bash
bash .agent/tools/unity_bridge.sh input drag 100 500 400 500 0.5
```

#### `input swipe SX SY EX EY [duration]`
Same as drag but semantically a swipe (faster gesture). Default duration `0.15s`.

```bash
bash .agent/tools/unity_bridge.sh input swipe 500 200 500 800 0.3
```

#### `input pinch CX CY SD ED [duration]`
Two-finger pinch centered at `(CX, CY)`. `SD` is starting distance between fingers, `ED` is ending distance. Uses two virtual touch slots. Default duration `0.5s`.

```bash
bash .agent/tools/unity_bridge.sh input pinch 500 500 200 50 0.5
```

### How to Calculate Tap Coordinates for UI Elements

Given a RectTransform with:
- `anchorMin = (0, 0)`, `anchorMax = (0, 0)`, `pivot = (0, 0)`
- `anchoredPosition = (20, 20)`, `sizeDelta = (200, 80)`

The element occupies screen pixels `(20, 20)` to `(220, 100)`. Its center is `(120, 60)`.

For other anchor configurations, you need to account for the anchor position relative to the parent, the pivot offset, and the canvas scaling mode.

### UI Prerequisites for Input

For tap/hold/multi_tap to interact with UI elements, the scene must have:

1. **EventSystem** with **InputSystemUIInputModule** (not `StandaloneInputModule`)
2. **Canvas** with a **GraphicRaycaster** component
3. UI element must have **raycastTarget = true** on its Graphic/Image component
4. An interactive component (e.g., `Button`) that implements `IPointerClickHandler`

### Known Limitations

- **ScreenSpaceOverlay** canvases are not captured in screenshots (camera RenderTexture approach), but they work reliably for input raycasting
- **ScreenSpaceCamera** canvases appear in screenshots but require a camera reference to be set on the Canvas component
- Gesture inputs (drag/swipe/pinch) use a virtual Touchscreen device and may not interact with UI elements that only respond to pointer events (not touch events)
- Input events dispatched via `ExecuteEvents` execute synchronously in the editor update loop — check `logs` after a brief delay to see handler output

## Security Considerations
- Verify script permissions before execution
- Validate script integrity if required
- Use absolute paths when possible
- Handle potential permission issues gracefully

## Notes
- The tool discovery must be performed each time the Unity Bridge command is invoked
- The agent should not assume the tool location remains constant
- Error messages should be clear and actionable for the user
