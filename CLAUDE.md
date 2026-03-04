# Unity Bridge — Agent Instructions

## Project-Specific Configuration

Before working with project-specific assets, scenes, or setup tools, read the `PROJECT.md` file in the Unity project root. It contains the project's namespace, scene paths, prefab inventory, setup tool commands, and test harnesses.

**Template for new projects:** `Unity-Bridge/PROJECT-TEMPLATE.md`

## Unity Interaction Protocol

**ALL Unity interactions MUST go through the bridge script.** Never use raw `curl` to `localhost:5556`. Never bypass the script for any reason.

Script location: `.agent/tools/unity_bridge.sh`

## Two-Step Pattern (Mandatory)

1. **Run the command:** `bash .agent/tools/unity_bridge.sh <command> [args]`
2. **Read the output:** Use the Read tool on `C:/temp/unity_bridge_output.txt`

**Never skip Step 2.** The Bash tool return value will be empty or incomplete. The output file is overwritten on each invocation — read it immediately after each command.

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
| `execute` | `<Method> [argsJson]` | Invoke a static C# method via reflection |
| `integration_test` | `<test_file.json>` | Run single integration test (compile+play lifecycle) |
| `integration_suite` | `<test_directory>` | Run all integration tests in directory |
| `integration_run` | `<parent_directory>` | Run all suites with play mode cycling between groups |

## Execute Endpoint

Invokes any static C# method by fully-qualified name via reflection. New tools are added as static methods in any class — no server or script modifications needed.

**Before using execute extensively, read:** `.agent/docs/tools/execute-endpoint.md`

## Input Events

Input coordinates are screen pixels with `(0,0)` at bottom-left. Tap/hold/multi-tap use `ExecuteEvents` for UI; drag/swipe/pinch use virtual `Touchscreen`. UI input requires: EventSystem, Canvas with GraphicRaycaster, raycastTarget=true, and an interactable component.

**Before using input commands, read:** `.agent/docs/tools/input-events.md`

## Mandatory Compilation Rules

Unity does not automatically compile when files change on disk. **You must explicitly trigger compilation via the bridge** at every point a human developer would normally need Unity to recompile. Failure to compile means Unity is running stale code.

### When to compile

You **MUST** run `compile` in all of the following situations:

1. **After editing any C# file** — Every time you create, modify, or delete a `.cs` file, immediately compile before doing anything else in Unity.
2. **Before entering Play Mode** — Always compile before `play enter`. Never enter Play Mode on stale code.
3. **After exiting Play Mode if code was changed during play** — Compile after `play exit` before re-entering.
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

**When in doubt, compile.** An unnecessary compile costs seconds. A skipped compile wastes entire debugging sessions.

## Agent Validation Protocol

Before reporting work as complete, agents must verify: clean compilation, no Play Mode errors, visual correctness via screenshot, runtime behavior via execute calls, and clean logs.

**Before declaring work complete, read:** `.agent/docs/guides/validation-protocol.md`

## Prefab-First Asset Rules

All game objects and UI must be authored as prefabs, not constructed at runtime. Materials are `.mat` assets, not runtime-created. Use TMPro directly. One ScreenSpace canvas per scene. See PROJECT.md for project-specific prefab paths and setup tools.

**Before creating game objects or UI, read:** `.agent/docs/guides/prefab-first-rules.md`

## Code Separation — Game vs Test Harness

Game code and agent test infrastructure live in separate directories. Test harnesses use `<ProjectNamespace>.BridgeTests` namespace (see PROJECT.md). Game code must never reference the BridgeTests namespace.

**Before creating test harnesses, read:** `.agent/docs/guides/code-separation.md`

## Typical Workflow

```
health → [edit code] → compile → execute / play enter → screenshot → input → play exit → logs
```

## Rules

- **Never** use raw `curl` to `http://localhost:5556`. Always use the bridge script.
- **Never** skip reading the output file after running a command.
- **Never** enter Play Mode without compiling first.
- **Never** assume Unity has auto-compiled after a file edit — it has not.
- **Always** compile after editing any `.cs`, `.asmdef`, or `.asmref` file.
- **Always** use the bridge script at `.agent/tools/unity_bridge.sh`.
- **Always** read `C:/temp/unity_bridge_output.txt` after every invocation.

## Environment

- **OS:** Windows 11 native (no WSL)
- **Shell:** Git Bash (MINGW64)
- **Unity server:** `http://localhost:5556`
- **Output file:** `C:/temp/unity_bridge_output.txt` (overridable via `UNITY_BRIDGE_OUTPUT` env var)

## Bridge Tools Index

### Core Infrastructure

| Tool | Key Methods | Description |
|------|-------------|-------------|
| `BridgeTools` | `Ping`, `Add` | Connectivity test and basic operations |
| `MethodExecutor` | — | Reflection-based method executor for /execute |

### Scene & Asset Introspection

| Tool | Key Methods | Doc |
|------|-------------|-----|
| `SceneInventoryTool` | `GetSceneHierarchy`, `FindByName` | Scene discovery and hierarchy extraction |
| `ComponentDetailExtractor` | `GetComponentDetails` | Deep component property extraction |
| `GUIDResolver` | `Resolve`, `ResolveAll` | GUID-to-asset-path resolution |
| `PrefabInventoryTool` | `ListPrefabs`, `GetPrefabDetails` | Prefab discovery with variant detection |
| `PrefabDetailExtractor` | `ExtractDetails` | Full prefab hierarchy with overrides |
| `AssetInventoryTool` | `GetManifest`, `FindUnreferenced` | Asset manifest and dependency analysis |
| `UIInventoryTool` | `ScanCanvases`, `GetUITree` | Canvas scanning and UI tree extraction |
| `YAMLParser` | `Parse` | Unity YAML file parser for .prefab/.unity |

### Creation & Modification

| Tool | Key Methods | Doc |
|------|-------------|-----|
| `PrefabCreator` | `Create`, `Modify` | [prefab-creator.md](.agent/docs/tools/prefab-creator.md) |
| `UIBuilder` | `Build` | [ui-builder.md](.agent/docs/tools/ui-builder.md) |
| `CodeGenerator` | `Generate` | [code-generator.md](.agent/docs/tools/code-generator.md) |
| `UIToolkitTools` | `ParseUXML`, `GenerateUXML` | UXML/USS parsing and generation |

### Validation & Testing

| Tool | Key Methods | Doc |
|------|-------------|-----|
| `PrefabValidator` | `Validate` | Prefab validation with configurable rules |
| `VisualValidator` | `Validate` | [visual-validator.md](.agent/docs/tools/visual-validator.md) |
| `ScreenshotValidator` | `Capture`, `Compare` | Multi-resolution capture and pixel comparison |
| `TestRunner` | `RunTests`, `RunEditModeTests` | [test-runner.md](.agent/docs/tools/test-runner.md) |
| `CodeReviewer` | `Review` | Regex-based static analysis |

### Runtime & Play Mode

| Tool | Key Methods | Doc |
|------|-------------|-----|
| `GameStateObserver` | `GetState`, `ObserveField` | [game-state-observer.md](.agent/docs/tools/game-state-observer.md) |
| `PlayModeInteractor` | `RunSequence` | [play-mode-interactor.md](.agent/docs/tools/play-mode-interactor.md) |
| `PlayModeUIScanner` | `ScanUI`, `FindElement`, `GetInteractables` | Runtime UI discovery with screen positions |
| `IntegrationTestRunner` | `RunTest`, `RunSuite`, `ListTests`, `RunByTag` | [integration-testing.md](.agent/docs/guides/integration-testing.md) |
| `IntegrationTestWriter` | `SaveTest`, `ValidateTest` | Validate and save integration test files |

### Build & Package

| Tool | Key Methods | Doc |
|------|-------------|-----|
| `BuildPipelineTool` | `GetConfig`, `Build` | [build-pipeline.md](.agent/docs/tools/build-pipeline.md) |
| `BuildVerifier` | `Verify` | Build launch, monitoring, log analysis |
| `PackageManagerTool` | `List`, `Install`, `Remove` | Package management operations |

### Code Intelligence, Branding & Networking

| Tool | Key Methods | Doc |
|------|-------------|-----|
| `CodebaseAnalyzer` | `Analyze`, `GetDependencies` | C# codebase analysis and conventions |
| `DocFetcher` | `GetAPIDocs`, `Compare` | API docs via reflection, best practices |
| `BrandSystem` | `Apply`, `Validate` | [brand-system.md](.agent/docs/tools/brand-system.md) |
| `NetworkTestOrchestrator` | `Setup`, `RunTest` | Multi-instance network test orchestration |

## Project Structure

| Path | Description |
|------|-------------|
| `.agent/tools/unity_bridge.sh` | Bridge script (the agent interface) |
| `.agent/docs/` | On-demand reference docs (`tools/`, `guides/`) |
| `Unity-Bridge/Editor/` | Bridge package — server, input, screenshot, tools |
| `Unity-Bridge/Editor/Tools/` | All bridge tools (see index above) |
| `Unity-Bridge/PROJECT-TEMPLATE.md` | Template for project-specific configuration |
| `<project-root>/PROJECT.md` | Per-project config (namespace, scenes, prefabs, setup tools) |
