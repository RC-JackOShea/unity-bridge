# Challenge 24: Build Verifier

## Overview

Build the BuildVerifier (Brief Tool #21) -- launches a built application as a subprocess, monitors its process state and log output, waits for a ready signal, runs automated checks (process running, log contents, screenshot capture), collects results, and terminates the build cleanly. Returns a structured pass/fail verification report. This is the most complex challenge in the series -- it bridges the gap between editor-based testing and real standalone build testing. Also evaluates AltTester and GameDriver (Section 10) for more sophisticated build interaction.

## Brief Reference

Section 8.3 (Build Verification, Future) -- Launch built application as subprocess, automated gameplay testing via AltTester or GameDriver, visual regression testing (screenshot comparison), performance profiling (frame times, memory, draw calls). Section 10 -- AltTester for in-build UI automation, GameDriver for external-process game testing. Section 13 -- Constraints on build verification: no editor APIs available at runtime, IL2CPP stripping may remove reflection-based utilities.

## Problem Statement

Editor testing (Challenges 15-18) catches most issues, but a category of bugs only manifests in standalone builds: IL2CPP code stripping removes types used via reflection, shader compilation behaves differently on target GPUs, performance characteristics change without the editor overhead, and platform-specific APIs behave differently outside the editor. Build verification is the final quality gate -- launching the actual executable, monitoring its health, checking its log output for errors, and optionally capturing screenshots to verify visual correctness. The challenge is that once a build is running as a separate process, the Unity Editor has no direct API access to it. All interaction must go through external mechanisms: process management, log file monitoring, file-based communication, or network protocols.

## Success Criteria

1. `UnityBridge.BuildVerifier.LaunchBuild(string buildPath)` launches the built application as a subprocess using `System.Diagnostics.Process` and returns the process ID
2. `UnityBridge.BuildVerifier.CheckBuildRunning(int processId)` checks if the build process is still running, returns status (running, exited with code, crashed)
3. `UnityBridge.BuildVerifier.StopBuild(int processId)` terminates the build process cleanly (graceful close first, then kill if unresponsive)
4. Log file monitoring: `UnityBridge.BuildVerifier.GetBuildLogs(string logPath)` reads the Unity Player log file and returns its contents, or reads from a custom log path specified in the test spec
5. Ready signal detection: the verifier can wait for a specific string to appear in the log file before proceeding with checks, with a configurable timeout
6. Crash detection: if the process exits with a non-zero exit code or disappears unexpectedly, the verifier reports it as a crash with the exit code
7. Timeout handling: all operations have configurable timeouts -- launch wait, ready signal wait, overall test duration -- and return structured timeout errors rather than hanging
8. `UnityBridge.BuildVerifier.RunBuildTest(string jsonTestSpec)` runs a complete verification sequence: launch, wait for ready signal, execute checks, collect results, stop build, return report
9. Structured JSON report includes: launch success, process ID, duration, log errors, log warnings, check results (pass/fail per check), screenshots captured, and overall result
10. Evaluate AltTester and GameDriver: document their capabilities for in-build UI automation and external-process testing, note integration requirements, and assess feasibility for future challenges

## Expected Development Work

### New Files

- **`Unity-Bridge/Editor/Tools/BuildVerifier.cs`** -- Static class in the `UnityBridge` namespace. Uses `System.Diagnostics.Process` to launch and manage the build executable. Manages a dictionary of tracked processes by ID.

  **Process management**: `Process.Start()` with `ProcessStartInfo` configured for the build executable. Track processes in a static `Dictionary<int, Process>` so multiple builds can be managed. `Process.HasExited` for status checks. `Process.CloseMainWindow()` followed by `Process.Kill()` for termination.

  **Log file location**: On Windows, Unity Player logs are at `%APPDATA%\..\LocalLow\{companyName}\{productName}\Player.log`. The tool must derive this path from `PlayerSettings.companyName` and `PlayerSettings.productName`, or accept an explicit path in the test spec.

  **Ready signal**: Poll the log file at intervals (e.g., every 500ms) looking for a target string (e.g., "Game started" or "Scene loaded"). Return success when found, or timeout error if the deadline passes.

  **Screenshot capture of running build**: Several approaches, ranked by complexity:
  1. Embed a runtime component that calls `ScreenCapture.CaptureScreenshot()` on a timer or trigger (simplest, requires build preprocessing)
  2. Use Windows screenshot APIs via P/Invoke (`BitBlt` / `PrintWindow`) to capture the build window externally
  3. Use AltTester's screenshot API if integrated
  4. Accept that screenshot capture may be a future enhancement and focus on process + log verification first

  **Check types**: The `RunBuildTest` method iterates over a list of checks defined in the test spec:
  - `processRunning` -- verify the build process is still alive
  - `logContains` -- verify the log file contains a specific string
  - `logDoesNotContain` -- verify the log file does NOT contain a specific string (e.g., "NullReferenceException")
  - `screenshotCapture` -- capture a screenshot after a delay (if screenshot mechanism is available)
  - `waitSeconds` -- pause for a specified duration before the next check

### Test Spec JSON (input to RunBuildTest)

```json
{
  "buildPath": "Builds/Win64/game.exe",
  "waitForReady": {
    "type": "logFile",
    "path": "Builds/Win64/game_Data/output_log.txt",
    "contains": "Game started",
    "timeout": 30
  },
  "checks": [
    {"type": "processRunning"},
    {"type": "logContains", "text": "Scene loaded successfully"},
    {"type": "logDoesNotContain", "text": "NullReferenceException"},
    {"type": "waitSeconds", "seconds": 5},
    {"type": "screenshotCapture", "outputPath": "C:/temp/verify_screenshot.png"}
  ],
  "timeout": 60
}
```

### Verification Report JSON (output from RunBuildTest)

```json
{
  "buildPath": "Builds/Win64/game.exe",
  "launchSuccess": true,
  "processId": 12345,
  "duration": 32.5,
  "readySignalDetected": true,
  "readySignalTime": 8.2,
  "checks": [
    {"type": "processRunning", "passed": true},
    {"type": "logContains", "text": "Scene loaded successfully", "passed": true},
    {"type": "logDoesNotContain", "text": "NullReferenceException", "passed": true},
    {"type": "waitSeconds", "seconds": 5, "passed": true},
    {"type": "screenshotCapture", "outputPath": "C:/temp/verify_screenshot.png", "passed": false, "reason": "Screenshot mechanism not available"}
  ],
  "logErrors": [],
  "logWarnings": ["Shader 'Custom/Water' fallback used"],
  "exitCode": 0,
  "crashed": false,
  "result": "Passed"
}
```

### AltTester / GameDriver Evaluation

The implementation should include a code comment block or separate evaluation section documenting:
- **AltTester**: Open-source UI automation for Unity builds. Requires an `AltTester` component embedded in the build. Provides a TCP-based protocol for remote object queries, input injection, and screenshot capture. Good for UI-heavy testing but adds a dependency to the build.
- **GameDriver**: Commercial tool for external-process game testing. Does not require build modifications. Uses computer vision and API hooks for interaction. More powerful but adds licensing cost and external dependency.
- Both are evaluated per Section 10 but not required for this challenge's implementation. The core implementation uses process management and log file monitoring.

## Testing Protocol

1. First, ensure a build exists from Challenge 23, or produce one:
   `bash .agent/tools/unity_bridge.sh execute UnityBridge.BuildPipelineTool.ProduceBuild '["{ \"outputPath\": \"C:/temp/TestBuild/Game.exe\", \"target\": \"StandaloneWindows64\", \"scenes\": [\"Assets/Scenes/SampleScene.unity\"], \"options\": { \"development\": true } }"]'` -- Read output, confirm build succeeded.
2. `bash .agent/tools/unity_bridge.sh compile` -- Read `C:/temp/unity_bridge_output.txt`, confirm compilation succeeds.
3. Launch the build:
   `bash .agent/tools/unity_bridge.sh execute UnityBridge.BuildVerifier.LaunchBuild '["C:/temp/TestBuild/Game.exe"]'` -- Read output, verify process ID returned.
4. Check if the build is running:
   `bash .agent/tools/unity_bridge.sh execute UnityBridge.BuildVerifier.CheckBuildRunning '["<processId>"]'` -- Read output, verify status is "running".
5. Read build logs:
   `bash .agent/tools/unity_bridge.sh execute UnityBridge.BuildVerifier.GetBuildLogs '[""]'` -- Read output, verify Player.log contents are returned (pass empty string for default log path).
6. Stop the build:
   `bash .agent/tools/unity_bridge.sh execute UnityBridge.BuildVerifier.StopBuild '["<processId>"]'` -- Read output, verify clean termination.
7. Verify the process is no longer running:
   `bash .agent/tools/unity_bridge.sh execute UnityBridge.BuildVerifier.CheckBuildRunning '["<processId>"]'` -- Verify status is "exited".
8. Run a full verification suite:
   `bash .agent/tools/unity_bridge.sh execute UnityBridge.BuildVerifier.RunBuildTest '["{ \"buildPath\": \"C:/temp/TestBuild/Game.exe\", \"waitForReady\": { \"type\": \"logFile\", \"contains\": \"Initialize engine\", \"timeout\": 30 }, \"checks\": [{ \"type\": \"processRunning\" }, { \"type\": \"logDoesNotContain\", \"text\": \"NullReferenceException\" }], \"timeout\": 60 }"]'` -- Read output, verify structured report.
9. Test error handling -- launch a nonexistent build path:
   `bash .agent/tools/unity_bridge.sh execute UnityBridge.BuildVerifier.LaunchBuild '["C:/nonexistent/Game.exe"]'` -- Verify structured error response.

## Dependencies

- **Challenge 01 (Execute Endpoint)** -- All methods are invoked via `bash .agent/tools/unity_bridge.sh execute UnityBridge.BuildVerifier.<Method>`.
- **Challenge 23 (Build Pipeline)** -- Produces the built application that this challenge verifies. The build output path and player settings (companyName, productName) from Challenge 23 are used to locate the executable and derive the Player.log path.
