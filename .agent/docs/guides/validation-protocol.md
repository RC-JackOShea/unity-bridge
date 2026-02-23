# Agent Validation Protocol

Before reporting any work as complete, agents **MUST** follow these verification steps. Skipping these steps has historically led to invisible failures that waste entire debugging sessions.

## Mandatory Verification Steps

1. **Compile and confirm zero errors** — Read the compile output, explicitly search for "error". "Compilation started" is not enough — wait for "Compilation completed" with zero errors.
2. **Enter Play Mode and check logs** — Immediately after `play enter`, run `logs` and search for `Exception`, `Error`, `NullReference`. Fix anything found before proceeding.
3. **Take a screenshot after any visual change** — Use `screenshot C:/temp/verify.png`, then read the PNG with the Read tool. Inspect it for correctness.
4. **Verify runtime behavior via bridge execute** — Call test methods, parse the JSON response, and confirm actual values match expected values. Do not assume correctness.
5. **Check logs again after all interactions** — New errors may appear after input, physics simulation, or state changes.
6. **Exit Play Mode before declaring complete** — Always clean up.

## Unity Failure Modes

These are common things that compile successfully but break at runtime. Check for all of them:

| Failure | How to detect |
|---------|---------------|
| Magenta/pink materials | Screenshot shows pink objects. `Shader.Find()` returned null. Use the primitive's existing material or check `shader != null` before creating a material. |
| Event subscription timing | `OnEnable()` fires before reflection-injected fields are set. Use `Start()` for late-bound fields. Verify by triggering the event and checking the subscriber reacted. |
| `Destroy()` is deferred | Object still exists during the same frame / `Physics.Simulate()` call. Use `SetActive(false)` for immediate visual removal, then `Destroy()` for cleanup. |
| ScreenSpaceOverlay invisible in screenshots | Camera RenderTexture capture skips overlay canvases. Verify UI existence via `execute` calls instead of screenshots. |
| Missing scene objects | Prefab instances not placed in scene. Use `SceneInventoryTool.GetSceneHierarchy` or project-specific scene validation to check. |
| `Shader.Find()` returns null at runtime | Shader not included in build or not loaded. Reuse `renderer.material` from primitives instead of creating new materials. |

## Pre-Completion Checklist

Before declaring work complete, verify every applicable item:

- [ ] Compilation completed with zero errors (not just "started")
- [ ] Play Mode entered without exceptions in logs
- [ ] Screenshot taken and inspected (if visual changes were made)
- [ ] No magenta/pink objects visible in screenshot
- [ ] Runtime behavior tested via bridge execute (if logic changes were made)
- [ ] Expected values match actual values in test output
- [ ] Logs checked for errors after all interactions
- [ ] Play Mode exited cleanly
