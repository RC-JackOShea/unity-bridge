# Brief: Unity Bridge — Agent Sandbox for Autonomous Testing

## 1. Project Context

This project extends an existing **Unity Bridge** — a tool that enables an AI agent (such as Claude Code or a Claude Cowork plugin) to programmatically interact with a running Unity Editor instance over a socket connection. The bridge currently supports basic operations (connecting, writing code, triggering recompilation, reading compiler output). The goal is to extend the bridge into a full agent sandbox where the agent can autonomously open a Unity project, enter/exit Play Mode, capture screenshots, read logs, emulate touch inputs, run test sequences, and report results — all without a human in the loop.

**Technology stack:** Unity Editor (C#), socket-based communication protocol (existing bridge), agent tooling (Claude Code / Cowork plugin), PowerShell or equivalent client-side scripting.

**Target platform context:** The Unity project appears to be a mobile/touch application (references to finger presses, pinch-to-zoom, tap interactions).

---

## 2. Why This Exists

There is a gap between agentic coding frameworks (which excel at browser automation — taking screenshots, clicking elements, reading state) and game engine development environments like Unity. The speaker wants to bridge that gap so that an AI agent can do for Unity what browser-use agents already do for web applications: observe state, take actions, read results, and iterate autonomously.

The immediate motivation is **automated test execution**: the agent should be able to run through UI flows in a Unity application, capture errors, identify recurring issues, and suggest fixes — potentially running overnight without human supervision.

---

## 3. Existing System — What the Bridge Does Today

Before extending the bridge, the downstream agent must understand what already exists. The existing Unity Bridge file will be available in the working directory alongside this brief.

**The agent must read and analyse the existing bridge code first** to understand the current architecture before making any changes.

### Known Current Capabilities

| Capability | Description |
|---|---|
| Socket connection | Opens a socket between the agent and Unity Editor for bidirectional communication |
| Code injection | Agent can write/modify C# code in the Unity project |
| Recompilation trigger | Agent can instruct Unity to recompile the project |
| Compiler output capture | Agent can read the results of recompilation (success/failure, errors) |

### What the Agent Must Determine from the Existing Code

- What protocol does the socket use (TCP, WebSocket, named pipe)?
- What message format is used (JSON, plain text, custom)?
- Is the bridge a C# Unity Editor script, a PowerShell client script, or both?
- What is the current command vocabulary (what commands can the agent send)?
- How does the bridge handle errors and disconnections?
- What Unity Editor APIs does the bridge already call?

---

## 4. Phase 1: Extend the Unity Bridge (Current Scope)

**Objective:** Extend the existing Unity Bridge to support all capabilities required for an agent to autonomously test a Unity application without human intervention.

### 4.1 Play Mode Control

The bridge must support entering and exiting Unity Play Mode programmatically.

| Command | Behaviour |
|---|---|
| Enter Play Mode | Instruct Unity to enter Play Mode. Wait for Play Mode to be fully active before returning success. |
| Exit Play Mode | Instruct Unity to exit Play Mode. Wait for Play Mode to fully stop before returning success. |
| Query Play Mode State | Return whether Unity is currently in Play Mode, compiling, or in Edit Mode. |

**Why this matters:** Play Mode entry/exit is the foundation for all testing. The agent needs to enter Play Mode at key moments, run test interactions, then exit to inspect logs and errors.

**Relevant Unity APIs to investigate:** `EditorApplication.isPlaying`, `EditorApplication.EnterPlaymode()`, `EditorApplication.ExitPlaymode()`, `EditorApplication.playModeStateChanged`.

### 4.2 Log Extraction

The bridge must capture Unity console logs while the application is running in Play Mode.

| Requirement | Detail |
|---|---|
| Capture all log levels | `Debug.Log`, `Debug.LogWarning`, `Debug.LogError`, `Debug.LogException` |
| Include stack traces | Full stack trace for errors and exceptions |
| Timestamped | Each log entry must include a timestamp |
| Buffered retrieval | Agent can request "give me all logs since last retrieval" |
| Filterable | Agent can request only errors, only warnings, etc. |

**Relevant Unity APIs to investigate:** `Application.logMessageReceived`, `Application.logMessageReceivedThreaded`.

### 4.3 Game View Screenshot Capture

The bridge must capture a screenshot of the Unity Game View and return the image data to the agent.

| Requirement | Detail |
|---|---|
| Capture source | The Game View window contents (what the player would see) |
| Output format | PNG image file saved to a known path, or base64-encoded image data returned over the socket |
| On-demand | Agent triggers capture at any point during Play Mode |
| Resolution | Match the current Game View resolution |

**Why this matters:** The agent uses screenshots to assess visual state — confirming UI elements loaded correctly, buttons are visible, screens transitioned properly. This mirrors how browser-use agents take screenshots to assess webpage state.

**Relevant Unity APIs to investigate:** `ScreenCapture.CaptureScreenshot()`, `Camera.Render()`, `RenderTexture`, `Texture2D.ReadPixels()`.

### 4.4 Input Emulation

The bridge must allow the agent to emulate human touch/pointer inputs within the running Unity application during Play Mode.

#### Basic Inputs (Implement First)

| Input Type | Parameters | Description |
|---|---|---|
| Single tap / finger press | `(x, y)` screen coordinates | Simulates a single touch at the specified position |
| Tap and hold | `(x, y)`, duration | Simulates pressing and holding at a position |
| Drag | `(startX, startY)`, `(endX, endY)`, duration | Simulates a finger drag from start to end position |

#### Advanced Inputs (Implement After Basic Inputs Work)

| Input Type | Parameters | Description |
|---|---|---|
| Pinch zoom in | `(centreX, centreY)`, start distance, end distance | Two-finger pinch gesture moving fingers inward |
| Pinch zoom out | `(centreX, centreY)`, start distance, end distance | Two-finger pinch gesture moving fingers outward |
| Two-finger drag | `(startX, startY)`, `(endX, endY)`, finger separation | Simulates two fingers dragging together |
| Swipe | `(startX, startY)`, direction, velocity | Quick directional swipe gesture |
| Multi-tap | `(x, y)`, tap count | Double-tap, triple-tap, etc. |

**Design principle:** All emulated inputs should mimic authentic human interactions — variable timing, natural motion curves, realistic finger separation distances. The agent should be able to drive these inputs the same way it drives browser automation (specifying coordinates and actions).

**Relevant Unity APIs to investigate:** `UnityEngine.InputSystem` (new Input System), `Input.simulateMouseWithTouches`, `UnityEngine.EventSystems`, `Touch` struct simulation. Also consider whether the Unity project uses the legacy Input Manager or the new Input System package, as the injection approach differs.

### 4.5 Error Analysis and Reporting

After a test run, the agent must be able to:

1. Exit Play Mode
2. Retrieve all captured logs
3. Identify recurring errors (same error appearing multiple times)
4. Group errors by type/source
5. For each error group, locate the relevant source code in the Unity project
6. Suggest potential fixes based on the error messages and source code context

This capability is built on top of sections 4.1 (Play Mode control) and 4.2 (log extraction) — it is an agent-level workflow, not a bridge command, but the bridge must provide the raw data to support it.

---

## 5. Example Test Flow Sequence

This is the canonical test flow the speaker described. The bridge must support every step in this sequence.

| Step | Action | Bridge Capability Used |
|---|---|---|
| 1 | Enter Play Mode | Play Mode Control |
| 2 | Wait for application to fully load | Screenshot Capture + Log Extraction (confirm load complete) |
| 3 | Tap the "Server" button to connect/load in | Input Emulation (single tap) |
| 4 | Wait for server connection to establish | Screenshot Capture + Log Extraction |
| 5 | Tap through all navigation tabs (X, Y, N, Z — placeholder names) | Input Emulation (sequential taps) |
| 6 | Capture screenshot after each tab to verify content loaded | Screenshot Capture |
| 7 | Exit Play Mode | Play Mode Control |
| 8 | Retrieve all logs from the session | Log Extraction |
| 9 | Analyse errors — identify recurring issues | Error Analysis (agent-level) |
| 10 | Locate source code for errors and suggest fixes | Error Analysis (agent-level) |
| 11 | Report findings back to the user | Agent output |

---

## 6. Architecture Principle: Agent-as-Tool-User

The bridge should be designed so that each capability is exposed as a **discrete, callable tool/command** that an agent can invoke. The agent orchestrates the sequence; the bridge executes individual commands.

This mirrors how browser-use agents work:
- The browser exposes primitives (navigate, click, screenshot, read DOM)
- The agent decides what sequence to call them in

Similarly, the Unity Bridge exposes primitives (enter play mode, tap at coordinates, capture screenshot, get logs) and the agent decides the test sequence.

**The bridge should NOT contain test logic.** The bridge is infrastructure. Test sequences are defined by the agent at runtime based on user instructions.

---

## 7. Constraints and Edge Cases

| Constraint | Detail |
|---|---|
| No CI/CD integration for now | The speaker explicitly stated: do not worry about CI/CD, LLM orchestration, or the automation layer around tests at this stage. Focus on the bridge capabilities only. |
| Human-out-of-the-loop | The bridge must operate without requiring human confirmation or interaction. The agent sandbox should be fully autonomous. |
| Existing bridge is the starting point | Do not build from scratch. Extend the existing bridge code. Read and understand it first. |
| Socket-based architecture | Maintain the existing socket-based communication pattern. |
| Unity Editor context | All operations happen inside the Unity Editor (not a standalone build). The Game View within the Editor is the test viewport. |
| Tab names are placeholders | "X, Y, N, Z" are placeholder names for UI navigation tabs. The actual tab names will be determined at test runtime by the agent reading the UI. |
| Mobile/touch target | The Unity application targets touch input (finger presses, pinch gestures), so input emulation must simulate touch events, not just mouse clicks. |

---

## 8. Phase 2: Future Work (Do Not Implement — Awareness Only)

The following items were explicitly called out as future scope. They are documented here so the bridge architecture does not inadvertently block them, but **no implementation work should be done on these items now**.

| Future Capability | Description |
|---|---|
| Project opening | Agent tells the bridge which Unity project path to open; the bridge launches Unity with that project and waits for it to be fully loaded. |
| Git integration | Agent pulls latest branches from GitHub before running tests. |
| Autonomous overnight test runs | Agent runs continuously overnight executing test suites and compiling reports. |
| CI/CD pipeline integration | Bridge commands are invoked from a CI/CD pipeline rather than an interactive agent session. |
| LLM orchestration layer | A higher-level system that schedules and coordinates agent test runs. |

**Architectural implication:** The bridge's command interface should be clean and stateless enough that any of these future callers could invoke the same commands without refactoring.

---

## 9. Deliverables Expected

The downstream agent is expected to produce:

1. **An extended Unity Bridge** — the existing bridge code modified to support all Phase 1 capabilities (Play Mode control, log extraction, screenshot capture, input emulation).
2. **Documentation of the bridge command interface** — a clear list of every command the bridge accepts, its parameters, and its return values.
3. **Validation that the bridge works** — the agent should use the bridge to execute the example test flow (Section 5) against a running Unity instance and report results.

The deliverable should be structured such that it can be packaged as a **Claude Cowork plugin or Claude Code tool** that any agent session can use to interact with Unity.

---

## 10. Open Questions

| # | Question | Why It Matters |
|---|---|---|
| 1 | Does the Unity project use the **legacy Input Manager** or the **new Input System** package? | Input emulation approach differs significantly between the two systems. |
| 2 | What Unity version is the project targeting? | API availability varies by version (e.g., `EditorApplication.EnterPlaymode()` vs older APIs). |
| 3 | Is the Game View always visible/focused when the agent needs to capture screenshots? | If the Game View is behind other windows or minimised, screenshot capture may return blank images. |
| 4 | What is the expected latency tolerance for socket commands? | Should the bridge respond within milliseconds, or are multi-second waits acceptable (e.g., waiting for Play Mode to fully start)? |
| 5 | Should the bridge support multiple simultaneous agent connections, or is single-agent-at-a-time sufficient? | Affects socket server architecture. |
| 6 | How should the bridge handle Unity compilation errors that prevent Play Mode entry? | The agent needs to know "I tried to enter Play Mode but Unity has compile errors" — should the bridge return the errors, or should the agent check compilation status separately first? |
| 7 | What is the file path convention for the existing bridge code? | The agent needs to know where to find the bridge in the project directory to read and extend it. |
| 8 | Should input emulation coordinates be in screen pixels or normalised (0-1) coordinates? | Normalised coordinates are resolution-independent; pixel coordinates are easier to derive from screenshots. |
| 9 | For the "authentic human interaction" input emulation, what level of realism is needed in Phase 1 vs future phases? | Phase 1 could use simple linear interpolation for drags; realistic bezier curves and variable timing could be deferred. |
