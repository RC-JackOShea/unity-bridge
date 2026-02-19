# Brief: Lights-Out Agentic Layer for Unity Development

## 1. Project Context

A solo Unity/C# engineer wants to build a comprehensive agentic automation layer that eliminates human-in-the-loop requirements for the entire Unity development lifecycle. The technology stack is Unity (C#), with AI agents orchestrated through an agentic framework (likely Claude Code, MCP servers, or similar). The domain is game development — encompassing scene composition, prefab authoring, UI layout, networking, testing, build pipeline, and deployment. The end goal is a system where the human provides specifications, direction, and acceptance criteria, and the agentic layer handles everything from code generation through build verification — a "lights out" engineering workflow for Unity.

## 2. Why This Exists

Unity game development currently has a natural barrier protecting it from full AI automation: the Unity Editor is GUI-heavy, and much of a Unity engineer's work involves visual interaction with the editor — hitting Play, inspecting the Game window, dragging UI elements, configuring components in the Inspector. This GUI dependency creates a gap that prevents AI agents from achieving parity with human Unity developers. The speaker wants to systematically close that gap by building programmatic tooling that gives agents the same depth of access and understanding that a human developer gets from the Unity Editor GUI, but through scripts, APIs, and structured data extraction — optimised for speed and token efficiency.

## 3. Core Design Principles

These principles must govern every architectural decision in the system:

| Principle | Description |
|---|---|
| **Token Efficiency** | Prefer local script execution that returns structured data over sending raw content to the agent for processing. Use compute resources (CPU, disk I/O) instead of token consumption wherever possible. |
| **Programmatic Speed** | Build helper scripts and editor tools that return information immediately via function calls, not through agent reasoning loops. The agent should call a function and get a structured response, not read a file and think about it. |
| **Deep Introspection** | The agent must understand the Unity project at every level — scenes, hierarchies, components, property values, prefab structures, asset references, UI layout, and package dependencies. |
| **Visual Competence** | Code correctness is necessary but insufficient. The agent must also produce visually correct results — proper UI layout, consistent colours and branding, good spatial composition. |
| **Full Lifecycle Coverage** | The agentic layer must cover the complete development lifecycle: code generation, asset creation, testing, evaluation, code review, build configuration, build production, and build verification. |
| **Lights-Out Operation** | No human interaction required between specification input and deliverable output. All validation, review, and quality assurance happens within the agentic layer. |

## 4. Phase 1: Project Introspection Engine

### Objective

Build a suite of Unity Editor scripts and helper tools that allow the agentic layer to rapidly extract structured information about any Unity project — scenes, hierarchies, components, prefabs, assets, and their relationships — without consuming agent tokens for parsing.

### 4.1 Scene Introspection

The agent needs to answer these questions programmatically:

| Question | Implementation Approach |
|---|---|
| What scenes exist in the project? | Scan `EditorBuildSettings.scenes` for build scenes; additionally glob `Assets/**/*.unity` for all scene files. |
| What scenes are in the build? | Read `EditorBuildSettings.scenes` array, filtering for `enabled == true`. |
| What is in each scene? | Use `EditorSceneManager.OpenScene()` then traverse the root GameObjects and their full hierarchies. Serialize to structured JSON. |
| What components are on each GameObject? | For each GameObject, iterate `GetComponents<Component>()` and extract type names, serialised property values via `SerializedObject`/`SerializedProperty`. |
| What are the parent-child relationships? | Walk `Transform.parent` / `Transform.GetChild(i)` to build a hierarchy tree. |
| What assets does the scene reference? | Parse the `.unity` YAML file for GUID references, cross-reference against `.meta` files to resolve asset paths. |

**Required Editor Scripts:**

1. **SceneInventoryTool** — Opens each scene (or reads YAML directly), outputs a JSON manifest containing: scene name, scene path, build index, root GameObjects, full hierarchy tree with component lists and key property values.

2. **ComponentDetailExtractor** — Given a GameObject path (e.g., `Canvas/MainMenu/StartButton`), returns all components with all serialised property names, types, and current values as structured JSON.

### 4.2 Prefab Introspection and Manipulation

The agent needs deep access to prefabs — both reading and writing.

**Reading Prefabs:**

| Capability | Implementation |
|---|---|
| List all prefabs in the project | Glob `Assets/**/*.prefab` and return paths, names, GUIDs. |
| Extract prefab structure | Use `PrefabUtility.LoadPrefabContents(path)` to load a prefab in isolation, then traverse its hierarchy exactly as with scenes. Alternatively, parse the `.prefab` YAML file directly. |
| Identify components and property values | Same `SerializedObject`/`SerializedProperty` traversal as scene objects. |
| Resolve nested prefab references | Read `m_CorrespondingSourcePrefab` and `m_PrefabInstance` fields from YAML, or use `PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot()`. |
| Detect prefab variants and their overrides | Use `PrefabUtility.GetPropertyModifications()` to list all overrides a variant applies to its base. |
| Identify what prefabs are spawnable at runtime | Search all scripts for `Instantiate()` calls referencing prefab fields; cross-reference `[SerializeField]` prefab references. |

**Writing/Modifying Prefabs:**

| Capability | Implementation |
|---|---|
| Create new prefabs programmatically | Build a GameObject hierarchy in code, configure components, then call `PrefabUtility.SaveAsPrefabAsset(root, path)`. |
| Modify existing prefabs | Use `PrefabUtility.LoadPrefabContents()` → modify → `PrefabUtility.SaveAsPrefabAsset()` then `PrefabUtility.UnloadPrefabContents()`. |
| Evaluate prefabs for problems | Build a validation tool that checks for: missing script references, broken asset references, components with default/unset values, incorrect layer assignments, missing colliders on interactive objects, UI elements with zero size. |

**Required Editor Scripts:**

3. **PrefabInventoryTool** — Scans all prefabs, outputs JSON manifest with path, GUID, root component list, nested prefab references, variant base references.

4. **PrefabDetailExtractor** — Given a prefab path, loads it and returns full hierarchy with all components and property values as JSON.

5. **PrefabCreator** — Accepts a JSON specification describing a prefab hierarchy (GameObjects, components, property values) and produces a `.prefab` asset.

6. **PrefabValidator** — Runs a configurable set of validation rules against a prefab and returns a structured report of issues.

### 4.3 YAML-Level Access

Unity serialises scenes and prefabs as YAML (when Force Text serialisation is enabled). Direct YAML parsing gives the agent access to project data without opening the Unity Editor.

**Key YAML Knowledge:**

| Concept | Detail |
|---|---|
| File header | `%YAML 1.1` / `%TAG !u! tag:unity3d.com,2011:` |
| Object separator | `---` between each serialised object |
| Class IDs | `!u!1` = GameObject, `!u!4` = Transform, `!u!114` = MonoBehaviour, `!u!20` = Camera, `!u!23` = MeshRenderer, etc. ([Full reference](https://docs.unity3d.com/Manual/ClassIDReference.html)) |
| FileID | Local identifier within a file (e.g., `&6`) |
| GUID | Global identifier stored in `.meta` files, used for cross-file references |
| Reference format | `{fileID: 11500000, guid: <32-char-hex>, type: 3}` |
| Stripped instances | Nested prefab placeholders with minimal data; full content lives in the source prefab file |
| MonoBehaviour scripts | Always Class ID 114; `m_Script` field contains the GUID of the C# script |
| Property prefix | Most properties prefixed with `m_` (e.g., `m_Name`, `m_LocalPosition`) |

**External YAML Parsing Tools:**

- **unity-yaml-parser** (Python, `pip install unityparser`) — Recommended. Loads Unity YAML files as Python objects, preserves formatting on save. Use `UnityDocument.load_yaml()`, filter with `doc.filter(class_names=['MonoBehaviour'])`.
- **UnityPy** (Python) — For binary asset extraction and editing.
- **Custom C# scripts inside Unity** — More reliable than external parsing; can resolve all references through Unity's own APIs.

**Required Tools:**

7. **YAMLPrefabRipper** — A script (Python or C# CLI) that takes a `.prefab` or `.unity` file path and returns structured JSON containing: every GameObject, every component with class ID and type name, every property with name and value, every cross-file reference with resolved asset path. This is the "rip the YAML out of the prefab" tool the speaker explicitly requested.

8. **GUIDResolver** — Given a GUID, resolves it to an asset path by scanning `.meta` files. Maintains a cached lookup table for speed.

### 4.4 Asset Inventory

9. **AssetInventoryTool** — Scans the entire `Assets/` directory and produces a manifest of all assets by type: scripts, prefabs, scenes, materials, textures, audio clips, models, ScriptableObjects, animations, animator controllers. For each asset: path, GUID, file size, last modified date, and inbound/outbound references.

## 5. Phase 2: UI/UX Competence Layer

### Objective

Give the agentic layer the ability to create, evaluate, and modify Unity UI with visual quality — not just functional correctness but proper layout, spacing, colour consistency, and branding adherence.

### 5.1 UI Introspection

| Capability | Detail |
|---|---|
| Identify all UI canvases and their render modes | Scan scenes for `Canvas` components; extract render mode (Screen Space Overlay, Screen Space Camera, World Space), sort order, reference camera. |
| Map UI hierarchy | UI elements are deeply nested (Canvas → Panel → Layout Group → Buttons). Build a tree representation with element types, anchoring, pivot, size, position. |
| Extract visual properties | For each UI element: colours (text colour, image colour, button normal/highlighted/pressed/selected colours), font, font size, sprite references, material references. |
| Detect layout groups | Identify `HorizontalLayoutGroup`, `VerticalLayoutGroup`, `GridLayoutGroup` — extract spacing, padding, child alignment, child force expand settings. |
| Identify interactive elements | Buttons, toggles, sliders, input fields, dropdowns, scroll views. Extract their event listeners (onClick, onValueChanged, etc.) and what methods they invoke. |

### 5.2 UI Creation and Modification

| Capability | Detail |
|---|---|
| Create UI elements programmatically | Instantiate Canvas, Panel, Button, Text (TextMeshPro), Image, Layout Groups. Set all properties via script. |
| Apply consistent branding | Accept a "brand spec" (primary colour, secondary colour, accent colour, font family, font sizes for H1/H2/body, corner radius, spacing scale) and apply it across all created UI elements. |
| Layout intelligence | When creating UI, apply proper anchoring (stretch vs fixed), responsive layout groups, correct pivot points, appropriate padding and spacing. The agent must understand *why* a UI element should be anchored to top-left vs stretch-to-fill. |
| Visual validation | Capture screenshots of the Game view at multiple resolutions (via `ScreenCapture.CaptureScreenshot()`), then use vision capabilities to assess: overlapping elements, text overflow, inconsistent spacing, colour contrast issues, elements outside the visible area. |

### 5.3 UI Toolkit (Modern UI)

For projects using UI Toolkit instead of (or alongside) Unity UI:

| Capability | Detail |
|---|---|
| Parse UXML files | Read `.uxml` structure files to understand UI element hierarchy. |
| Parse USS files | Read `.uss` style sheets to understand applied styles. |
| Generate UXML/USS programmatically | Create UI structure and styling files from specifications. |
| Bind UI to data | Set up data bindings between UI elements and C# data sources. |

**Required Editor Scripts:**

10. **UIInventoryTool** — Scans all canvases in a scene and outputs complete UI tree with visual properties, layout settings, and interaction bindings as JSON.

11. **UIBuilder** — Accepts a JSON UI specification and creates the corresponding UI hierarchy in a scene, complete with layout groups, anchoring, colours, fonts, and event bindings.

12. **UIScreenshotValidator** — Enters Play mode, captures screenshots at specified resolutions, and outputs them for vision-based assessment.

## 6. Phase 3: Testing and Validation Layer

### Objective

Enable the agentic layer to verify that code and assets are functional — from unit tests through Play mode interaction to full gameplay validation — without any human involvement.

### 6.1 Automated Test Execution

| Test Type | Implementation |
|---|---|
| Edit Mode tests | Run via CLI: `unity -runTests -testPlatform editmode -testResults results.xml -batchmode -quit`. Parse NUnit XML results. |
| Play Mode tests | Run via CLI: `unity -runTests -testPlatform playmode -testResults results.xml -batchmode -quit`. Parse NUnit XML results. |
| Custom validation tests | Write editor scripts that validate project-wide rules (e.g., all prefabs have colliders, all UI text uses TextMeshPro, no missing references). |

### 6.2 Play Mode Interaction

This is the critical capability that currently requires a human — hitting Play, looking at the Game window, interacting with UI, and observing behaviour.

| Capability | Implementation |
|---|---|
| Enter/Exit Play mode programmatically | `EditorApplication.isPlaying = true/false` from an editor script. |
| Simulate input | Use Unity's **Input System Test Framework** (`InputTestFixture`): `Press(keyboard.wKey)`, `Set(mouse.position, new Vector2(x,y))`, `Click(mouse.leftButton)`. For legacy input: inject events via `Input.SimulateTouch()` or custom input abstraction layer. |
| Interact with UI | Find UI elements by path or type, simulate clicks via `ExecuteEvents.Execute(button, pointerData, ExecuteEvents.pointerClickHandler)`. |
| Control gameplay systems | Move a third-person controller via simulated WASD/stick input, trigger abilities via simulated button presses, open inventory/menu systems via simulated key events. The agent must be able to exercise full gameplay loops — not just click buttons but navigate 3D space, trigger combat, interact with objects. |
| Observe game state | Read component values during Play mode to verify expected behaviour: player position, health values, UI visibility states, score counters. |
| Capture visual output | `ScreenCapture.CaptureScreenshot()` at key moments during gameplay for vision-based validation. |
| Wait for conditions | Coroutine-based or poll-based waits for specific game states before proceeding (e.g., wait for scene load, wait for animation complete, wait for UI transition). |

### 6.3 Networking Testing (ParrelSync)

**ParrelSync** allows running multiple Unity Editor instances of the same project simultaneously using symlinked Assets/Packages/ProjectSettings folders.

| Capability | Implementation |
|---|---|
| Create clone instances | ParrelSync API: `ClonesManager.CreateCloneFromPath()` or via editor menu. |
| Detect clone vs original | `ClonesManager.IsClone()` — use to auto-configure one instance as host and one as client. |
| Orchestrate multi-instance testing | Build an orchestration script that: (1) starts the original as host, (2) starts the clone as client, (3) runs a test scenario across both, (4) collects results from both. |
| Validate networking behaviour | After both instances are running, verify: connection established, game state synchronised, RPCs received, network objects spawned on both sides. |

**Required Editor Scripts:**

13. **TestRunner** — Wraps Unity Test Framework CLI execution. Runs specified test suites, parses XML results, returns structured pass/fail/error report as JSON.

14. **PlayModeInteractor** — Enters Play mode, executes a scripted sequence of inputs and state checks, captures screenshots, exits Play mode, returns a structured interaction report.

15. **NetworkTestOrchestrator** — Manages ParrelSync instances for multi-player testing scenarios.

## 7. Phase 4: Code Intelligence Layer

### Objective

Give the agentic layer deep understanding of the C# codebase — not just reading files but understanding class hierarchies, dependencies, API usage patterns, and the ability to generate correct Unity C# code.

### 7.1 Codebase Analysis

| Capability | Implementation |
|---|---|
| List all scripts and their types | Scan `Assets/**/*.cs`, classify as MonoBehaviour, ScriptableObject, Editor script, pure C# class, interface, enum, struct. |
| Map class inheritance hierarchies | Parse using Roslyn analyzers or regex-based extraction to build inheritance trees. |
| Identify component dependencies | For each MonoBehaviour, identify `[RequireComponent]` attributes, `GetComponent<T>()` calls, serialised references to other components. |
| Map event/message flow | Identify `UnityEvent` fields, `SendMessage`/`BroadcastMessage` calls, C# event/delegate patterns, observer patterns. |
| Identify API usage patterns | Detect which Unity APIs are used (old Input vs new Input System, UGUI vs UI Toolkit, legacy rendering vs URP/HDRP). |
| Detect Input System in use | Determine whether the project uses the legacy `UnityEngine.Input` API, the new `UnityEngine.InputSystem`, or both. Check Player Settings for Active Input Handling (Old, New, or Both). Flag any code that mixes systems incorrectly. Provide migration guidance if the project should move from legacy to new Input System. |

### 7.2 Code Generation

| Capability | Implementation |
|---|---|
| Generate MonoBehaviours | Given a specification (component purpose, serialised fields, lifecycle methods needed), generate a complete C# script following project conventions. |
| Generate Editor scripts | Create custom inspectors, editor windows, property drawers as needed by other tools. |
| Generate test scripts | Create Edit Mode and Play Mode test classes with proper attributes and setup. |
| Respect project conventions | Analyse existing code for naming conventions, namespace structure, code organisation patterns, and match them in generated code. |

### 7.3 Code Review and Evaluation

| Capability | Implementation |
|---|---|
| Static analysis | Use Roslyn analyzers for code quality checks. Apply Unity-specific rules (e.g., don't use `Find()` in `Update()`, avoid allocations in hot paths). |
| Dependency validation | Verify that new code doesn't introduce circular dependencies or break existing references. |
| API correctness | Verify that Unity APIs are used correctly (e.g., `SerializedObject.Update()`/`ApplyModifiedProperties()` pairing, proper Undo registration). |

**Required Tools:**

16. **CodebaseAnalyser** — Scans all C# files, outputs structured JSON: classes, inheritance, component dependencies, Unity API usage, event flow.

17. **CodeGenerator** — Accepts specifications, generates C# scripts following detected project conventions.

18. **CodeReviewer** — Runs static analysis on specified files, returns structured issue report. Review categories include: (a) **Performance** — allocations in Update/FixedUpdate, unnecessary Find/GetComponent calls in hot paths, coroutine misuse; (b) **Correctness** — null reference risks, missing null checks on GetComponent, incorrect use of SerializedObject without Update/ApplyModifiedProperties pairing, Undo registration missing for editor tools; (c) **Unity Best Practices** — singleton pattern issues, MonoBehaviour lifecycle ordering problems, improper use of DontDestroyOnLoad, mixing legacy and new Input System calls; (d) **Architecture** — circular dependencies, God classes, tight coupling between systems that should communicate via events; (e) **Production Readiness** — debug logging left in, hardcoded values that should be ScriptableObject configs, TODO comments, disabled code blocks. Returns a severity-ranked issue list (critical/warning/info) with file path, line number, and suggested fix.

## 8. Phase 5: Build and Deployment Pipeline

### Objective

Enable the agentic layer to configure build settings, produce builds for target platforms, and potentially run and verify those builds — all without human intervention.

### 8.1 Build Configuration

| Capability | Implementation |
|---|---|
| Set target platform | `EditorUserBuildSettings.SwitchActiveBuildTarget(buildTargetGroup, buildTarget)` |
| Configure build scenes | Modify `EditorBuildSettings.scenes` array. |
| Set player settings | Access `PlayerSettings` API: company name, product name, bundle identifier, icon, splash screen, scripting backend (Mono vs IL2CPP), API compatibility level. |
| Configure quality settings | Programmatic access to `QualitySettings` for each platform. |
| Set up build profiles | Unity 6+ Build Profiles system for managing multiple build configurations. |

### 8.2 Build Production

| Capability | Implementation |
|---|---|
| Produce a build | `BuildPipeline.BuildPlayer(options)` with configured `BuildPlayerOptions`. |
| CLI build | `unity -batchmode -executeMethod BuildScript.Build -buildTarget <platform> -quit` |
| Capture build results | `BuildPipeline.BuildPlayer()` returns `BuildReport` with success/failure, errors, warnings, file sizes, build time. |
| Post-build validation | Implement `IPostprocessBuildWithReport` to run checks after build completion. |

### 8.3 Build Verification (Future)

| Capability | Implementation |
|---|---|
| Launch built application | Execute the built binary as a subprocess. |
| Automated gameplay testing on build | Use **AltTester** or **GameDriver** to interact with the running build — find UI elements, simulate input, verify game state. |
| Visual regression testing | Capture screenshots from the running build, compare against baseline images. |
| Performance profiling | Capture frame times, memory usage, draw calls from the running build. |

**Required Editor Scripts:**

19. **BuildConfigurator** — Accepts a JSON build specification (platform, scenes, player settings, quality settings) and applies all configuration.

20. **BuildProducer** — Executes a build with specified configuration, returns structured build report.

21. **BuildVerifier** — Launches a built application, runs automated interaction tests, returns pass/fail report.

## 9. Phase 6: Package and Documentation Intelligence

### Objective

Enable the agentic layer to discover, install, evaluate, and correctly use Unity packages — including understanding their documentation and API surfaces.

### 9.1 Package Management

| Capability | Implementation |
|---|---|
| List installed packages | Read `Packages/manifest.json` and `Packages/packages-lock.json`. |
| Search for packages | Query Unity Package Manager registry, Asset Store, or known Git repositories. |
| Install packages | Edit `Packages/manifest.json` to add dependency entries (version, git URL, or local path). Call `AssetDatabase.Refresh()`. |
| Remove packages | Remove entries from `manifest.json`. |
| Update packages | Modify version strings in `manifest.json`. |
| Resolve conflicts | Check for version conflicts and dependency incompatibilities. |

### 9.2 Documentation Comprehension

| Capability | Implementation |
|---|---|
| Fetch Unity API documentation | Use web search/fetch to retrieve official Unity documentation for specific APIs. |
| Parse package documentation | Read README.md, CHANGELOG.md, and documentation folders from installed packages. |
| Identify best practices | For a given Unity subsystem (e.g., Input System, Addressables, Netcode for GameObjects), retrieve and summarise current best practices. |
| Compare approaches | When multiple approaches exist (e.g., old Input vs new Input System), produce a structured comparison with recommendations for the project context. |

**Required Tools:**

22. **PackageManager** — Programmatic interface to list, install, remove, and update packages. Validates compatibility before installation.

23. **DocFetcher** — Given a Unity API class or package name, retrieves and returns structured documentation summary optimised for agent consumption (not full web pages).

## 10. Existing Tools and Integrations to Leverage

The following existing open-source tools and Unity features should be evaluated for integration into the agentic layer rather than building from scratch:

| Tool | Purpose | Source |
|---|---|---|
| **MCP Unity servers** (CoderGamester/mcp-unity, IvanMurzak/Unity-MCP, CoplayDev/unity-mcp) | Bridge between AI agents and Unity Editor via Model Context Protocol. Provides tools and resources for agent interaction. | GitHub |
| **ParrelSync** | Multi-instance Unity Editor for networking testing. | GitHub (VeriorPies/ParrelSync) |
| **unity-yaml-parser** | Python library for parsing Unity YAML files externally. | GitHub (socialpoint-labs/unity-yaml-parser) |
| **AltTester** | Open-source game testing automation — interacts with game objects via scripts, supports C#/Python/Java. | alttester.com |
| **GameDriver** | Commercial game testing tool with HierarchyPath query language, works against Editor or device builds. | gamedriver.io |
| **Mooble** | Static analysis tool for prefabs and scenes with customisable rules. | GitHub (uken/mooble) |
| **Unity Dependencies Hunter** | Finds unreferenced assets in a project. | GitHub (AlexeyPerov/Unity-Dependencies-Hunter) |
| **Asset Relations Viewer** | Displays asset dependencies in tree view. | GitHub (innogames/asset-relations-viewer) |
| **GameCI** | Open-source CI/CD for Unity — Dockerised Unity for automated testing and building. | game.ci |
| **Roslyn analyzers** | C# static analysis and code generation at compile time. | NuGet / Unity 6+ native support |

## 11. Flow Sequence: Agent Development Cycle

This is the end-to-end flow the agentic layer should execute when given a development task:

1. **Receive specification** — Human provides a natural-language description of what to build or change.
2. **Project introspection** — Agent runs inventory tools to understand current project state: scenes, prefabs, scripts, UI, packages. This step uses helper scripts that return structured JSON, not token-heavy file reading.
3. **Plan generation** — Agent creates an implementation plan: files to create/modify, prefabs to build, UI to lay out, tests to write.
4. **Code generation** — Agent writes C# scripts, editor tools, and test scripts. Follows detected project conventions.
5. **Asset creation** — Agent creates prefabs, UI hierarchies, materials, or other assets programmatically via editor scripts.
6. **Compilation check** — Agent triggers a Unity domain reload and checks for compilation errors.
7. **Static analysis** — Agent runs code review tools and Roslyn analysers against new/modified code.
8. **Edit Mode testing** — Agent runs Edit Mode tests to validate logic that does not require Play mode.
9. **Play Mode testing** — Agent enters Play mode, runs scripted interaction sequences, captures screenshots, verifies game state.
10. **Network testing** (if applicable) — Agent uses ParrelSync to spin up host and client instances, runs multi-player test scenarios.
11. **Visual validation** — Agent uses vision capabilities to assess captured screenshots for visual correctness.
12. **Iteration** — If any step fails, agent diagnoses the issue, makes corrections, and re-runs from the appropriate step.
13. **Build production** — Once all tests pass, agent configures and produces a build.
14. **Build verification** — Agent launches the build, runs automated tests against the running application.
15. **Delivery** — Agent reports results to the human with: summary of changes, test results, build artefacts, screenshot evidence, and confidence assessment.

## 12. Concrete Example: End-to-End Agent Workflow

**Specification received:** "Add a pause menu UI with buttons for Resume, Settings, and Quit. Pressing Escape should toggle the pause menu. Time should freeze when paused."

**Phase 1 — Introspection:** Agent runs SceneInventoryTool → learns the main gameplay scene is `Scenes/Level01.unity`, it has a Canvas named `HUDCanvas` using Screen Space Overlay. Runs UIInventoryTool → learns existing UI uses TextMeshPro, primary colour is `#1A73E8`, font is `Roboto`, button style uses rounded corners with `#FFFFFF` text on coloured backgrounds. Runs CodebaseAnalyser → detects the project uses new Input System, finds an existing `GameManager` singleton with a `isPaused` boolean.

**Phase 2 — Plan:** Agent determines: create `PauseMenuController.cs` MonoBehaviour, create a `PauseMenu` prefab with Canvas Group for fade, create three buttons using detected brand colours, bind Escape key via Input System action, set `Time.timeScale = 0` when paused.

**Phase 3 — Execution:** Agent uses CodeGenerator to write `PauseMenuController.cs` matching project namespace and conventions. Uses PrefabCreator to build the pause menu prefab with correct hierarchy (Canvas → Panel → VerticalLayoutGroup → three Buttons with TextMeshPro labels). Uses UIBuilder to apply brand colours, font, and spacing consistent with existing HUD.

**Phase 4 — Validation:** Agent runs CodeReviewer on new script → no issues. Triggers compilation → passes. Runs Edit Mode tests for PauseMenuController logic. Enters Play mode via PlayModeInteractor → simulates Escape key press → verifies pause menu is visible, `Time.timeScale == 0`, simulates Resume button click → verifies menu hidden, `Time.timeScale == 1`. Captures screenshots of pause menu at 1920x1080 and 1280x720 → passes to vision model for layout validation.

**Phase 5 — Delivery:** Agent reports: 2 files created, 1 prefab created, 4 tests passing, 2 screenshots attached, confidence: high.

## 13. Constraints and Edge Cases

| Constraint | Detail |
|---|---|
| **Force Text serialisation required** | The project must use Force Text asset serialisation mode for YAML parsing to work. Binary serialised assets cannot be parsed externally. |
| **Unity Editor must be running for most tools** | Editor scripts require a running Unity Editor instance. Batch mode (`-batchmode`) can run headless but still requires a Unity installation. |
| **ParrelSync limitations** | Clone instances share Assets/Packages/ProjectSettings via symlinks. Changes in one instance are immediately visible in others — this is a feature but also a source of potential conflicts during concurrent editing. |
| **IL2CPP restrictions** | When building with IL2CPP backend: no `dynamic` keyword, no runtime code generation, no `System.Reflection.Emit`. Generated code must be AOT-compatible. |
| **Stripped prefab instances** | When parsing YAML for nested prefabs, stripped instances contain only placeholder data. Full content must be resolved by loading the source prefab separately. |
| **Unity version differences** | SerializeReference format changed between Unity 2020 and 2021. Input System API differs from legacy Input. UI Toolkit availability and completeness varies by version. All tools must be version-aware. |
| **Token budget** | The entire motivation for helper scripts is to avoid sending large amounts of raw project data through the agent. Every tool must return the minimum structured data needed, not dump entire files. |
| **Vision model dependency** | Visual validation (UI layout, screenshot assessment) requires vision-capable models. The quality of visual assessment is bounded by the vision model's capabilities. |
| **Unity licensing** | Running Unity in batch mode for CI/CD requires appropriate licensing (Unity Pro or higher for some features). |

## 14. Deliverables Expected

The downstream agent is expected to produce:

1. **Architecture document** — Detailed technical design for the agentic layer, covering tool interfaces, data flow, orchestration patterns, and integration points.
2. **Editor script toolkit** — The numbered tools (1-23) listed across all phases, implemented as Unity Editor scripts in C#, each returning structured JSON via console output or file output.
3. **YAML parsing toolkit** — Python or C# tools for external project introspection without requiring a running Unity Editor.
4. **Orchestration layer** — The system that chains tools together to execute the development cycle flow (Section 11), handles errors, and manages iteration loops.
5. **Test suite** — Tests for the agentic tools themselves — verifying that introspection tools return correct data, that creation tools produce valid assets, that validation tools detect known issues.
6. **Documentation** — Usage guides for each tool, integration instructions, and configuration reference.

## 15. Open Questions

| Question | Context |
|---|---|
| Which Unity version(s) must be supported? | Tool implementations and available APIs differ significantly across Unity 2021 LTS, 2022 LTS, Unity 6, and Unity 6.2+. |
| Which networking framework is in use? | Netcode for GameObjects, Mirror, Photon, Fish-Networking, or custom? This affects how ParrelSync orchestration and network testing are implemented. |
| Which render pipeline? | Built-in, URP, or HDRP? Affects visual validation baselines and material/shader handling. |
| What constitutes "visual correctness"? | Is there a brand guide, style guide, or reference designs the agent should validate against? How is "looks good" operationalised? |
| What is the target agentic framework? | Claude Code with MCP? Custom orchestration? This determines how tools are exposed (as MCP tools, CLI commands, or function calls). |
| What is the acceptable iteration time? | How long can the agent spend on a single task before delivering? Affects depth of testing and validation. |
| Should the system work on existing projects or only new ones? | Retrofitting introspection into an existing large project has different challenges than building greenfield with agentic tooling from day one. |
| What is the minimum confidence threshold for delivery? | What test coverage, validation depth, and screenshot evidence is required before the agent can declare a task complete? |
| How should the agent handle Unity Editor crashes? | Unity can crash during batch operations. The system needs crash recovery and state persistence. |
| What happens when the agent encounters a Unity feature it doesn't have a tool for? | Should the agent build new tools on the fly, request human help, or skip that aspect? |
