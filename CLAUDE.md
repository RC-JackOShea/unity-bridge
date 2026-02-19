# Unity Bridge — Agent Instructions

## Unity Interaction Protocol

**ALL Unity interactions MUST go through the bridge script.** Never use raw `curl` to `localhost:5556`. Never bypass the script for any reason.

Script location: `.agent/tools/unity_bridge.sh`

## How to Invoke

```bash
bash .agent/tools/unity_bridge.sh <command> [args]
```

## How to Read Output

The bridge script redirects all output to a file because the Claude Code Bash tool on Windows swallows `echo` stdout — agents see nothing from direct execution.

After **every** invocation, read the output file:

```
C:/temp/unity_bridge_output.txt
```

Use the **Read** tool (not `cat`) to read this file.

## Two-Step Pattern (Mandatory)

1. **Step 1 — Run the command:**
   ```bash
   bash .agent/tools/unity_bridge.sh health
   ```
2. **Step 2 — Read the output file:**
   Use the Read tool on `C:/temp/unity_bridge_output.txt`

**Never skip Step 2.** The Bash tool return value will be empty or incomplete.

## Available Commands

| Command | Arguments | Description |
|---------|-----------|-------------|
| `health` | — | Check if Unity server is running |
| `status` | — | Get Unity editor status (compiling, play mode, etc.) |
| `compile` | — | Trigger compilation and wait for completion |
| `logs` | — | Retrieve Unity console logs |
| `clear` | — | Clear Unity console logs |
| `play` | `enter` or `exit` | Enter/exit Play Mode (no arg = query state) |
| `screenshot` | `[file_path]` | Capture screenshot to file or base64 |
| `input tap` | `X Y [duration]` | Tap at screen coordinates |
| `input hold` | `X Y [duration]` | Hold/long press |
| `input drag` | `SX SY EX EY [duration]` | Drag gesture |
| `input swipe` | `SX SY EX EY [duration]` | Swipe gesture |
| `input pinch` | `CX CY SD ED [duration]` | Pinch gesture |
| `input multi_tap` | `X Y [count] [interval]` | Multi-tap |

## Input Events — Deep Reference

### Coordinate System

All input coordinates are **screen pixels** with `(0, 0)` at the **bottom-left** corner of the Game view. The top-right corner is `(Screen.width, Screen.height)`. These match Unity's screen coordinate convention.

To find the Game view resolution while in Play Mode, use `screenshot` — the result reports `width` and `height`.

### How Input Works Internally

The InputEmulator uses **two different mechanisms** depending on the input type:

#### Tap / Hold / Multi-tap → ExecuteEvents (UI-compatible)

These actions use Unity's `ExecuteEvents` system to directly dispatch pointer events. This is the only reliable way to click UI elements from editor code.

The sequence for a tap:
1. Raycast into the UI at the given screen coordinates using `EventSystem.current.RaycastAll`
2. If a UI element is hit, walk **up** the hierarchy with `ExecuteEvents.GetEventHandler<IPointerClickHandler>` to find the actual handler (e.g., a `Button` component on a parent object)
3. Fire `pointerDown` → `pointerUp` → `pointerClick` on the resolved handler
4. If no UI element is hit, fall back to queuing a mouse press/release via `InputSystem.QueueStateEvent` on `Mouse.current` (for non-UI game objects)

#### Drag / Swipe / Pinch → Virtual Touchscreen

Gesture inputs use a virtual `Touchscreen` device registered with the Input System. Touch events are queued via `InputSystem.QueueDeltaStateEvent` on individual touch slots (`virtualTouchscreen.touches[index]`). These are processed over multiple frames as the gesture interpolates from start to end.

### Determining Tap Coordinates for UI Elements

For a UI element anchored at the bottom-left with these RectTransform settings:
- `anchorMin = (0, 0)`, `anchorMax = (0, 0)`, `pivot = (0, 0)`
- `anchoredPosition = (20, 20)`, `sizeDelta = (200, 80)`

The element spans from `(20, 20)` to `(220, 100)` in screen pixels. Its **center** is at `(120, 60)`. Tap that center point:

```bash
bash .agent/tools/unity_bridge.sh input tap 120 60
```

For elements with different anchoring, calculate the screen-space bounds from the anchor, pivot, anchoredPosition, and sizeDelta. When in doubt, take a `screenshot` and visually identify the element's position — but note the screenshot limitation below.

### UI Requirements for Input to Work

For `tap`, `hold`, and `multi_tap` to interact with UI elements, the scene **must** have:

1. **EventSystem** — with `InputSystemUIInputModule` (not legacy `StandaloneInputModule`), since the bridge's InputEmulator is built on the new Input System
2. **Canvas** — with a `GraphicRaycaster` component
3. **Raycast target** — the UI element (or its `Image`/`Graphic` component) must have `raycastTarget = true` (this is the default)
4. **Interactable component** — a `Button`, `Toggle`, or other `Selectable` that implements `IPointerClickHandler`

If any of these are missing, the raycast will return no hit and the tap will fall through to the mouse fallback (which only affects non-UI objects).

### Screenshot vs Overlay Canvas Trade-off

The screenshot system captures via camera RenderTexture rendering. This means:

- **ScreenSpaceCamera** canvases **are** captured in screenshots (the canvas is rendered by the camera)
- **ScreenSpaceOverlay** canvases are **NOT** captured in screenshots (overlay is composited by Unity after camera rendering)

However, `ScreenSpaceOverlay` is more reliable for input raycasting — the `EventSystem.RaycastAll` call consistently finds overlay elements regardless of camera setup.

**Recommendation:** Use `ScreenSpaceOverlay` for UI that needs to receive input via the bridge. Accept that these elements won't appear in screenshots. If you need to verify UI visually, temporarily switch to `ScreenSpaceCamera` for the screenshot, then switch back.

### Input Command Examples

```bash
# Tap the center of a button at screen position (120, 60)
bash .agent/tools/unity_bridge.sh input tap 120 60

# Long press at (500, 400) for 2 seconds
bash .agent/tools/unity_bridge.sh input hold 500 400 2.0

# Double-tap at (300, 300)
bash .agent/tools/unity_bridge.sh input multi_tap 300 300 2

# Drag from (100, 500) to (400, 500) over 0.5 seconds
bash .agent/tools/unity_bridge.sh input drag 100 500 400 500 0.5

# Swipe up from (500, 200) to (500, 800) over 0.3 seconds
bash .agent/tools/unity_bridge.sh input swipe 500 200 500 800 0.3

# Pinch at center (500, 500), starting distance 200, ending distance 50
bash .agent/tools/unity_bridge.sh input pinch 500 500 200 50 0.5
```

### Full Example: Create and Click a UI Button

```
# 1. Edit code to create a button (e.g., via RuntimeInitializeOnLoadMethod)
# 2. Compile
bash .agent/tools/unity_bridge.sh compile
# Read output — confirm compilation succeeded

# 3. Enter Play Mode
bash .agent/tools/unity_bridge.sh play enter
# Read output — confirm play mode entered

# 4. Clear logs so we only see new output
bash .agent/tools/unity_bridge.sh clear
# Read output

# 5. Tap the button (coordinates from RectTransform calculation)
bash .agent/tools/unity_bridge.sh input tap 120 60
# Read output — confirm input enqueued

# 6. Wait a moment, then check logs for the click handler output
bash .agent/tools/unity_bridge.sh logs
# Read output — look for the expected log message

# 7. Exit Play Mode
bash .agent/tools/unity_bridge.sh play exit
# Read output
```

## Mandatory Compilation Rules

Unity does not automatically compile when files change on disk. **You must explicitly trigger compilation via the bridge** at every point a human developer would normally need Unity to recompile. Failure to compile means Unity is running stale code.

### When to compile

You **MUST** run `compile` in all of the following situations:

1. **After editing any C# file** — Every time you create, modify, or delete a `.cs` file (scripts, editors, ScriptableObjects, shaders with C# wrappers, assembly definitions), immediately compile before doing anything else in Unity.
2. **Before entering Play Mode** — Always compile before `play enter`. Never enter Play Mode on stale code.
3. **After exiting Play Mode if code was changed during play** — If you edited scripts while Play Mode was active, compile after `play exit` before re-entering.
4. **After modifying assembly definitions** (`.asmdef` / `.asmref`) — These change compilation structure and require a fresh compile.
5. **After adding/removing/moving script files** — File operations that change what Unity sees on disk require compilation.
6. **Before running any test or validation step** — Ensure the code Unity is executing matches what is on disk.

### Compilation sequence

```
# After any code edit:
bash .agent/tools/unity_bridge.sh compile
# Read output, confirm "Compilation completed" before proceeding

# Before entering play mode (even if you just compiled):
bash .agent/tools/unity_bridge.sh compile
bash .agent/tools/unity_bridge.sh play enter
```

### What happens if you skip compilation

- Unity runs **old code** that does not match the files on disk.
- Bugs appear fixed in source but persist at runtime — extremely confusing.
- New scripts or components are missing or throw `MissingReferenceException`.
- Test results are meaningless because they test stale assemblies.

**When in doubt, compile.** An unnecessary compile costs seconds. A skipped compile wastes entire debugging sessions.

## Typical Workflow

```
health → [edit code] → compile → play enter → screenshot → input → play exit → logs
```

1. Check server is running (`health`)
2. Edit code as needed
3. **Compile** (`compile`) — mandatory after any code change
4. **Compile again** if unsure — always safe, never harmful
5. Enter Play Mode (`play enter`) — only after successful compilation
6. Take screenshots to see the game state (`screenshot C:/temp/screen.png`)
7. Send input to interact (`input tap 500 400`)
8. Exit Play Mode when done (`play exit`)
9. If code was edited during play, **compile** before re-entering
10. Check logs for errors (`logs`)

## Rules

- **Never** use raw `curl` to `http://localhost:5556`. Always use the bridge script.
- **Never** skip reading the output file after running a command.
- **Never** enter Play Mode without compiling first.
- **Never** assume Unity has auto-compiled after a file edit — it has not.
- **Always** compile after editing any `.cs`, `.asmdef`, or `.asmref` file.
- **Always** use the bridge script at `.agent/tools/unity_bridge.sh`.
- **Always** read `C:/temp/unity_bridge_output.txt` after every invocation.
- The output file is overwritten on each invocation — read it immediately after each command.

## Environment

- **OS:** Windows 11 native (no WSL)
- **Shell:** Git Bash (MINGW64)
- **Unity server:** `http://localhost:5556`
- **Output file:** `C:/temp/unity_bridge_output.txt` (overridable via `UNITY_BRIDGE_OUTPUT` env var)

## Project Structure

| Path | Description |
|------|-------------|
| `.agent/tools/unity_bridge.sh` | Bridge script (the agent interface) |
| `.agent/commands/unity/bridge.md` | Bridge discovery documentation |
| `Unity-Bridge/Editor/UnityBridgeServer.cs` | HTTP server running inside Unity Editor |
| `Unity-Bridge/Editor/UnityBridgeWindow.cs` | Editor window UI for the bridge |
| `Unity-Bridge/Editor/InputEmulator.cs` | Input emulation handler |
| `Unity-Bridge/Editor/ScreenshotCapture.cs` | Screenshot capture handler |
