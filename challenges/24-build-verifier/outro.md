# Challenge 24: Post-Completion Checklist

## Documentation Updates

- [ ] Document all BuildVerifier methods: `LaunchBuild`, `CheckBuildRunning`, `StopBuild`, `GetBuildLogs`, `RunBuildTest` -- parameters, return types, and JSON formats
- [ ] Document the test spec JSON format: `buildPath`, `waitForReady` (type, path, contains, timeout), `checks` array (processRunning, logContains, logDoesNotContain, screenshotCapture, waitSeconds), and top-level `timeout`
- [ ] Document the verification report JSON format: launchSuccess, processId, duration, readySignalDetected, checks results, logErrors, logWarnings, exitCode, crashed, result
- [ ] Document all supported check types with examples: `processRunning`, `logContains`, `logDoesNotContain`, `waitSeconds`, `screenshotCapture`
- [ ] Document the log file monitoring approach: polling interval, default Player.log path derivation from PlayerSettings, custom log path override

## Verification Steps

- [ ] Produce a build (via Challenge 23) and launch it with `LaunchBuild` -- verify process ID is returned and the application window appears
- [ ] Verify `CheckBuildRunning` correctly reports "running" for an active process and "exited" after termination
- [ ] Verify `StopBuild` terminates the process cleanly -- confirm the process disappears and no orphaned processes remain
- [ ] Verify `GetBuildLogs` reads the Player.log file and returns its contents with Unity engine initialization messages
- [ ] Test timeout handling: use `RunBuildTest` with a `waitForReady` string that will never appear and a short timeout -- verify it returns a structured timeout error, not a hang
- [ ] Test crash detection: launch a build that exits immediately (e.g., invalid build or a build with an intentional crash) and verify the report shows `crashed: true` with the non-zero exit code
- [ ] Run a full `RunBuildTest` suite and verify all check results are correctly reported in the structured JSON report
- [ ] Verify no orphaned processes remain after all tests complete -- check Windows Task Manager or `tasklist`

## Code Quality

- [ ] Subprocess cleanup: ensure all tracked processes are terminated if the Unity Editor is closed or the bridge server shuts down -- register cleanup in `AssemblyReloadEvents` or `EditorApplication.quitting`
- [ ] Handle builds that hang on startup: `StopBuild` should attempt `CloseMainWindow()` first, then escalate to `Kill()` after a grace period
- [ ] Handle missing build path: return a clear error if the executable does not exist at the specified path, not a `Win32Exception`
- [ ] Handle log file not yet created: the Player.log may not exist immediately after launch -- poll with retries before reporting an error
- [ ] Cross-platform process management considerations: document that `Process.CloseMainWindow()` is Windows-specific, `Process.Kill()` is cross-platform but forceful, and macOS/Linux builds may need different termination signals
- [ ] Prevent resource leaks: dispose `Process` objects after termination, remove them from the tracking dictionary, limit the maximum number of concurrent tracked processes

## Knowledge Transfer

- [ ] Document AltTester evaluation: capabilities (remote object queries, UI automation, screenshot capture via TCP protocol), requirements (AltTester component must be embedded in build), trade-offs (adds build dependency, excellent for UI testing), and recommendation for future integration
- [ ] Document GameDriver evaluation: capabilities (external-process testing without build modifications, computer vision support), requirements (commercial license, external tool installation), trade-offs (no build changes needed, but adds cost and complexity), and recommendation
- [ ] Document limitations of editor-based build testing: no direct API access to running build, all communication must go through external channels (process management, file system, network), editor cannot inject input or read game state from a running build without an embedded agent
- [ ] Document Player.log file locations per platform: Windows (`%APPDATA%\..\LocalLow\{companyName}\{productName}\Player.log`), macOS (`~/Library/Logs/Unity/Player.log`), Linux (`~/.config/unity3d/{companyName}\{productName}\Player.log`) -- note that only Windows is actively tested in this challenge
- [ ] Document the overall build verification strategy: Challenge 23 produces builds, Challenge 24 verifies them; together they form the complete Section 8 build pipeline; AltTester/GameDriver integration would be a future enhancement for richer in-build testing
