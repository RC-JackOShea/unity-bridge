# Phase 1 Extension — Session Notes

**Date**: 2026-02-19
**Branch**: master (uncommitted changes)

---

## What Was Implemented

All 6 files from the plan were created/modified:

### 1. `Unity-Bridge/Editor/UnityBridge.Editor.asmdef` — MODIFIED
- Added `"Unity.InputSystem"` to references array
- **Status**: Compiled successfully in Unity

### 2. `Unity-Bridge/Editor/UnityBridgeServer.cs` — MODIFIED (~680 lines, +564 net)
- `QueueUnityAction<T>(Func<T>, int timeoutMs)` — synchronous variant using `ManualResetEventSlim`
- `ReadRequestBody(HttpListenerRequest)` — POST body reader utility
- `LogEntry.id` — auto-incrementing int field, `nextLogId` counter
- Enhanced `HandleGetLogs(HttpListenerRequest)` — accepts `?level=`, `?since=`, `?limit=` query params; response wraps with `LogsResponseData` (logs, lastId, totalCount)
- `HandlePlayModePost` / `HandlePlayModeGet` — play mode enter/exit/poll
- `EditorApplication.playModeStateChanged` subscription for transition tracking
- `HandleScreenshot` — delegates to `ScreenshotCapture` via synchronous main-thread call
- `HandleInputPost` / `HandleInputGet` — parses input requests, delegates to `InputEmulator`
- `IsPlaying()` public API for editor window
- DTO classes: `PlayModeRequest`, `ScreenshotRequest`, `InputRequest`, `PlayModeStateData`, `PlayModeTransitionData`, `LogsResponseData`, `ScreenshotResponseData`, `InputStartedData`
- `InputEmulator.Initialize()` called from `Initialize()` on main thread
- `nextLogId` reset in `HandleClear()` and `ClearLogsManually()`
- **Status**: Compiled and running successfully

### 3. `Unity-Bridge/Editor/ScreenshotCapture.cs` — CREATED (~95 lines)
- `CaptureAsBase64(int superSize)` — returns base64 PNG + dimensions
- `CaptureToFile(string path, int superSize)` — writes PNG to disk
- Both use `ScreenCapture.CaptureScreenshotAsTexture()`, require main thread
- **Status**: Tested and working

### 4. `Unity-Bridge/Editor/InputEmulator.cs` — CREATED (~290 lines)
- Virtual `Touchscreen` via `InputSystem.AddDevice<Touchscreen>()`
- Frame-based `Update()` hooked to `EditorApplication.update`
- `InputSequence` hierarchy: `TapSequence`, `HoldSequence`, `DragSequence`, `PinchSequence`, `MultiTapSequence`
- Thread-safe pending queue (`lock(pendingLock)`) for HTTP→main-thread handoff
- Cleanup on `AssemblyReloadEvents.beforeAssemblyReload`
- **Status**: Tested and working

### 5. `Unity-Bridge/Editor/UnityBridgeWindow.cs` — MODIFIED (~20 lines)
- Added cyan "PLAYING" status dot indicator in `DrawServerStatus()`
- Updated `ExtractCommand()` to recognize `playmode`, `screenshot`, `input`
- Updated `OnLogReceived` filter for new endpoint names
- **Status**: Compiled successfully

### 6. `.agent/tools/unity_bridge.sh` — MODIFIED (~180 lines added)
- `play [enter|exit]` — play mode control with polling
- `screenshot [path]` — capture to file or base64
- `input tap|hold|drag|swipe|pinch X Y [...]` — all input types
- Updated usage/help text
- **Status**: Not yet validated via bash execution (shell issues at end of session)

---

## Validation Results

### Endpoints Tested via curl — ALL PASSING

| Endpoint | Method | Result |
|----------|--------|--------|
| `GET /health` | GET | `{"status":"ok"}` |
| `GET /playmode` | GET | Returns `isPlaying`, `isPaused`, `state` |
| `POST /playmode {"action":"enter"}` | POST | Enters play mode, returns transition data |
| `POST /playmode {"action":"exit"}` | POST | Exits play mode, returns transition data |
| `GET /logs` | GET | Returns logs with `id` field |
| `GET /logs?level=log&limit=2` | GET | Level filtering + limit working |
| `GET /logs?since=3` | GET | Incremental retrieval working |
| `POST /screenshot {"format":"file"}` | POST | 2560x1600 PNG saved to `C:/temp/bridge_test.png` |
| `POST /screenshot {"format":"base64"}` | POST | ~95KB base64 response, HTTP 200 |
| `POST /input {"action":"tap",...}` | POST | `input_1` returned, completed |
| `POST /input {"action":"hold",...}` | POST | `input_2` returned, completed |
| `POST /input {"action":"drag",...}` | POST | `input_3` returned, completed |
| `POST /input {"action":"swipe",...}` | POST | `input_4` returned, completed |
| `POST /input {"action":"pinch",...}` | POST | `input_5` returned, completed |
| `POST /input {"action":"multi_tap",...}` | POST | `input_6` returned, completed |
| `GET /input` | GET | Shows 6 completed, 0 active |

### Error Cases Tested — ALL PASSING

| Scenario | Response |
|----------|----------|
| Screenshot without play mode | `"Play Mode must be active to capture screenshots"` |
| Input without play mode | `"Play Mode must be active to emulate input"` |
| Exit play mode when stopped | `"Not in Play Mode"` |
| Invalid play mode action | `"Unknown action 'invalid'. Use 'enter' or 'exit'."` |

---

## Bug Found & Fixed During Validation

### InputEmulator.Initialize() called from wrong thread
- **Symptom**: `POST /input` returned empty response (HTTP connection dropped)
- **Root cause**: `InputEmulator.EnqueueInput()` lazily called `Initialize()` which calls `InputSystem.AddDevice<Touchscreen>()` — this must run on the main thread, but was called from the HTTP thread pool
- **Fix**:
  1. Moved `InputEmulator.Initialize()` call to `UnityBridgeServer.Initialize()` (runs on main thread during `[InitializeOnLoad]`)
  2. Changed `EnqueueInput()` to throw `InvalidOperationException` if not initialized instead of lazy init
- **Verified**: After recompile, InputEmulator init log appears and all input types work

---

## Remaining Work

### Not Yet Validated
- [ ] Client script (`unity_bridge.sh`) — shell became unresponsive during testing; needs manual validation
- [ ] Full end-to-end test flow from plan Section 5 (enter play → screenshot → tap → navigate → screenshot → exit → logs → analyse)
- [ ] Window UI visuals — cyan "PLAYING" dot needs visual confirmation in Unity Editor

### Not Committed
All changes are uncommitted on `master`. Files to stage:
```
M  .agent/tools/unity_bridge.sh
M  Unity-Bridge/Editor/UnityBridge.Editor.asmdef
M  Unity-Bridge/Editor/UnityBridgeServer.cs
M  Unity-Bridge/Editor/UnityBridgeWindow.cs
?? Unity-Bridge/Editor/InputEmulator.cs
?? Unity-Bridge/Editor/ScreenshotCapture.cs
```

---

## Architecture Notes

- **Pixel coordinates** for input match screenshot output directly
- **Linear interpolation** for drags/swipes (bezier deferred to Phase 2)
- **Async POST → GET poll** pattern for play mode and input (consistent with existing compile pattern)
- **Synchronous capture** for screenshots via `ManualResetEventSlim` (blocks HTTP thread briefly)
- **Virtual Touchscreen device** cleaned up on assembly reload to avoid device leak
- Play mode transition states tracked via `EditorApplication.playModeStateChanged` callback
