# Input Events — Deep Reference

## Coordinate System

All input coordinates are **screen pixels** with `(0, 0)` at the **bottom-left** corner of the Game view. The top-right corner is `(Screen.width, Screen.height)`. These match Unity's screen coordinate convention.

To find the Game view resolution while in Play Mode, use `screenshot` — the result reports `width` and `height`.

## How Input Works Internally

The InputEmulator uses **two different mechanisms** depending on the input type:

### Tap / Hold / Multi-tap → ExecuteEvents (UI-compatible)

These actions use Unity's `ExecuteEvents` system to directly dispatch pointer events. This is the only reliable way to click UI elements from editor code.

The sequence for a tap:
1. Raycast into the UI at the given screen coordinates using `EventSystem.current.RaycastAll`
2. If a UI element is hit, walk **up** the hierarchy with `ExecuteEvents.GetEventHandler<IPointerClickHandler>` to find the actual handler (e.g., a `Button` component on a parent object)
3. Fire `pointerDown` → `pointerUp` → `pointerClick` on the resolved handler
4. If no UI element is hit, fall back to queuing a mouse press/release via `InputSystem.QueueStateEvent` on `Mouse.current` (for non-UI game objects)

### Drag / Swipe / Pinch → Virtual Touchscreen

Gesture inputs use a virtual `Touchscreen` device registered with the Input System. Touch events are queued via `InputSystem.QueueDeltaStateEvent` on individual touch slots (`virtualTouchscreen.touches[index]`). These are processed over multiple frames as the gesture interpolates from start to end.

## Determining Tap Coordinates for UI Elements

For a UI element anchored at the bottom-left with these RectTransform settings:
- `anchorMin = (0, 0)`, `anchorMax = (0, 0)`, `pivot = (0, 0)`
- `anchoredPosition = (20, 20)`, `sizeDelta = (200, 80)`

The element spans from `(20, 20)` to `(220, 100)` in screen pixels. Its **center** is at `(120, 60)`. Tap that center point:

```bash
bash .agent/tools/unity_bridge.sh input tap 120 60
```

For elements with different anchoring, calculate the screen-space bounds from the anchor, pivot, anchoredPosition, and sizeDelta. When in doubt, take a `screenshot` and visually identify the element's position — but note the screenshot limitation below.

## UI Requirements for Input to Work

For `tap`, `hold`, and `multi_tap` to interact with UI elements, the scene **must** have:

1. **EventSystem** — with `InputSystemUIInputModule` (not legacy `StandaloneInputModule`), since the bridge's InputEmulator is built on the new Input System
2. **Canvas** — with a `GraphicRaycaster` component
3. **Raycast target** — the UI element (or its `Image`/`Graphic` component) must have `raycastTarget = true` (this is the default)
4. **Interactable component** — a `Button`, `Toggle`, or other `Selectable` that implements `IPointerClickHandler`

If any of these are missing, the raycast will return no hit and the tap will fall through to the mouse fallback (which only affects non-UI objects).

## Screenshot vs Overlay Canvas Trade-off

The screenshot system captures via camera RenderTexture rendering. This means:

- **ScreenSpaceCamera** canvases **are** captured in screenshots (the canvas is rendered by the camera)
- **ScreenSpaceOverlay** canvases are **NOT** captured in screenshots (overlay is composited by Unity after camera rendering)

However, `ScreenSpaceOverlay` is more reliable for input raycasting — the `EventSystem.RaycastAll` call consistently finds overlay elements regardless of camera setup.

**Recommendation:** Use `ScreenSpaceOverlay` for UI that needs to receive input via the bridge. Accept that these elements won't appear in screenshots. If you need to verify UI visually, temporarily switch to `ScreenSpaceCamera` for the screenshot, then switch back.

## Command Examples

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

## Full Example: Create and Click a UI Button

```
# 1. Edit code to create a button (e.g., via prefab placed in scene)
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
