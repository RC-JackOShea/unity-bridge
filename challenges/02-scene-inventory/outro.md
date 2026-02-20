# Challenge 02: Post-Completion Checklist

## Documentation Updates

- [ ] Add `SceneInventoryTool` to the Project Structure table in `CLAUDE.md` with path `Unity-Bridge/Editor/Tools/SceneInventoryTool.cs` and description "Scene discovery and hierarchy extraction tool"
- [ ] Document both methods (`GetSceneManifest`, `GetSceneHierarchy`) with their full signatures and JSON output formats
- [ ] Add usage examples showing the two-step pattern:
  ```
  bash .agent/tools/unity_bridge.sh execute UnityBridge.SceneInventoryTool.GetSceneManifest
  # Then read C:/temp/unity_bridge_output.txt
  ```
- [ ] Add a note explaining that `GetSceneHierarchy` temporarily opens the scene additively and closes it after extraction — the agent should not call this while in Play Mode

## Verification Steps

- [ ] Run `GetSceneManifest` and confirm it finds ALL `.unity` files under `Assets/`
- [ ] Cross-check the `buildIndex` values against Unity's Build Settings (File > Build Settings)
- [ ] Run `GetSceneHierarchy` on every scene found in the manifest and confirm valid JSON for each
- [ ] Verify that scenes with zero root GameObjects return `{"scenePath": "...", "hierarchy": []}` rather than an error
- [ ] Verify that the currently open scene in the editor is not changed after calling `GetSceneHierarchy`
- [ ] Test with a scene path that does not exist and confirm a clear error message is returned

## Code Quality

- [ ] Ensure scenes opened for inspection via `EditorSceneManager.OpenScene()` are always closed via `EditorSceneManager.CloseScene()` — use try/finally to guarantee cleanup
- [ ] Verify no scene modifications are saved accidentally during inspection (do not call `EditorSceneManager.SaveScene()`)
- [ ] Check behavior when the target scene is already open in the editor — avoid opening it a second time
- [ ] Test with deeply nested hierarchies (10+ levels) and confirm no stack overflow
- [ ] Verify memory usage is reasonable for scenes with many GameObjects (1000+)

## Knowledge Transfer

- [ ] Note any Unity API quirks encountered, such as:
  - `EditorSceneManager.OpenScene` behavior when the scene is already loaded
  - Differences between `scene.GetRootGameObjects()` and iterating transforms
  - How disabled GameObjects appear in the hierarchy
- [ ] Record the JSON schema as a reference for future tools that consume scene data
- [ ] Verify that Challenge 03 (Component Detail Extractor) can build on this by reusing the scene-opening and GameObject-path-resolution patterns
