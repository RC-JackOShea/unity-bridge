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
