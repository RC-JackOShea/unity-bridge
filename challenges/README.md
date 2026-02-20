# Unity Bridge — Lights-Out Challenge Series

28 sequential challenges that systematically build every capability described in the [Lights-Out Agentic Layer Brief](../ai-docs/brief-lights-out-unity-agentic-layer.md). Each challenge is a self-contained task an AI agent completes using ONLY the Unity Bridge (`bash .agent/tools/unity_bridge.sh`).

## How to Use

1. Start at Challenge 01 and work sequentially — each challenge builds on prior ones
2. Read the challenge `README.md` for the full specification
3. Complete all Success Criteria using only bridge commands
4. Follow the `outro.md` checklist after completing development work
5. Move to the next challenge

## Challenge Map

### Phase 1: Foundation & Project Introspection (Brief Sections 4.1-4.4)

| # | Challenge | Brief Tools | What It Builds |
|---|-----------|-------------|----------------|
| 01 | [Execute Endpoint](01-execute-endpoint/) | Foundation | `/execute` endpoint for calling any static C# method via bridge |
| 02 | [Scene Inventory](02-scene-inventory/) | Tool #1 | Scene manifest and full hierarchy extraction |
| 03 | [Component Detail Extractor](03-component-detail-extractor/) | Tool #2 | Deep SerializedProperty inspection for any component |
| 04 | [Prefab Inventory](04-prefab-inventory/) | Tools #3, #4 | Prefab discovery, hierarchy, variants, and overrides |
| 05 | [Prefab Creator](05-prefab-creator/) | Tool #5 | Create and modify prefabs from JSON specifications |
| 06 | [Prefab Validator](06-prefab-validator/) | Tool #6 | Rule-based prefab quality validation |
| 07 | [GUID Resolver](07-guid-resolver/) | Tool #8 | GUID-to-asset-path resolution with cached lookup |
| 08 | [YAML Parser](08-yaml-parser/) | Tool #7 | Direct .unity/.prefab YAML parsing without editor API |
| 09 | [Asset Inventory](09-asset-inventory/) | Tool #9 | Full project asset manifest with dependency graph |

### Phase 2: UI/UX Competence (Brief Section 5)

| # | Challenge | Brief Tools | What It Builds |
|---|-----------|-------------|----------------|
| 10 | [UI Inventory](10-ui-inventory/) | Tool #10 | Canvas detection, UI hierarchy, visual properties, event bindings |
| 11 | [UI Builder](11-ui-builder/) | Tool #11 | Programmatic UI creation from JSON specification |
| 12 | [Brand System](12-brand-system/) | — | Brand spec management, extraction, and token-based styling |
| 13 | [Screenshot Validator](13-screenshot-validator/) | Tool #12 | Multi-resolution screenshot capture and pixel comparison |
| 14 | [UI Toolkit Support](14-ui-toolkit-support/) | — | UXML/USS parsing and generation |

### Phase 3: Testing & Validation (Brief Section 6)

| # | Challenge | Brief Tools | What It Builds |
|---|-----------|-------------|----------------|
| 15 | [Test Runner](15-test-runner/) | Tool #13 | Edit/Play Mode test execution with NUnit XML parsing |
| 16 | [Play Mode Interaction](16-play-mode-interaction/) | Tool #14 | Scripted input sequences with state checks |
| 17 | [Game State Observer](17-game-state-observer/) | — | Runtime component value reading and condition waiting |
| 18 | [Visual Validation Pipeline](18-visual-validation-pipeline/) | — | Rule-based screenshot analysis and baseline comparison |
| 19 | [Network Test Orchestrator](19-network-test-orchestrator/) | Tool #15 | ParrelSync multi-instance networking tests |

### Phase 4: Code Intelligence (Brief Section 7)

| # | Challenge | Brief Tools | What It Builds |
|---|-----------|-------------|----------------|
| 20 | [Codebase Analyzer](20-codebase-analyzer/) | Tool #16 | Script classification, inheritance, dependencies, API patterns |
| 21 | [Code Generator](21-code-generator/) | Tool #17 | Convention-aware C# code generation |
| 22 | [Code Reviewer](22-code-reviewer/) | Tool #18 | Static analysis with 5 review categories and severity ranking |

### Phase 5: Build & Deployment (Brief Section 8)

| # | Challenge | Brief Tools | What It Builds |
|---|-----------|-------------|----------------|
| 23 | [Build Pipeline](23-build-pipeline/) | Tools #19, #20 | Build configuration, production, and report parsing |
| 24 | [Build Verifier](24-build-verifier/) | Tool #21 | Launch, monitor, and test built applications |

### Phase 6: Package & Documentation (Brief Section 9)

| # | Challenge | Brief Tools | What It Builds |
|---|-----------|-------------|----------------|
| 25 | [Package Manager](25-package-manager/) | Tool #22 | List, install, remove, update packages programmatically |
| 26 | [Documentation Intelligence](26-documentation-intelligence/) | Tool #23 | Structured API docs, package docs, best practices |

### Phase 7: Integration & Proof (Brief Sections 11-12)

| # | Challenge | Brief Tools | What It Builds |
|---|-----------|-------------|----------------|
| 27 | [End-to-End Workflow](27-end-to-end-workflow/) | All | Full 15-step development cycle — pause menu implementation |
| 28 | [Lights-Out Demo](28-lights-out-demo/) | All | Fully autonomous development from spec to delivery |

## Dependency Graph

```
01 ─┬─ 02 ─── 03 ─── 04 ─── 05 ─── 06
    │
    ├─ 07 ─── 08
    │
    ├─ 09
    │
    ├─ 10 ─── 11 ─── 12
    │
    ├─ 13 ─── 18
    │
    ├─ 14
    │
    ├─ 15
    │
    ├─ 16 ─── 19
    │
    ├─ 17
    │
    ├─ 20 ─── 21
    │    └──── 22
    │
    ├─ 23 ─── 24
    │
    ├─ 25 ─── 26
    │
    └─ 27 (requires 01-26) ─── 28 (requires 01-27)
```

## Brief Coverage Matrix

Every element from the brief is covered:

| Brief Section | Challenges | Coverage |
|---------------|------------|----------|
| 4.1 Scene Introspection | 02, 03 | All 6 questions, Tools #1-2 |
| 4.2 Prefab Introspection | 04, 05, 06 | Read + Write + Validate, Tools #3-6 |
| 4.3 YAML Access | 07, 08 | GUID resolution + YAML parsing, Tools #7-8 |
| 4.4 Asset Inventory | 09 | Full manifest + dependencies, Tool #9 |
| 5.1 UI Introspection | 10 | Canvases, hierarchy, properties, events, Tool #10 |
| 5.2 UI Creation | 11, 12, 13 | Builder + Brand + Visual validation, Tools #11-12 |
| 5.3 UI Toolkit | 14 | UXML/USS parse + generate |
| 6.1 Test Execution | 15 | Edit + Play Mode + Custom validation, Tool #13 |
| 6.2 Play Mode | 16, 17, 18 | Interaction + State + Visual, Tool #14 |
| 6.3 Networking | 19 | ParrelSync orchestration, Tool #15 |
| 7.1 Codebase Analysis | 20 | Classification + Dependencies + API detection, Tool #16 |
| 7.2 Code Generation | 21 | MonoBehaviour + Editor + Tests, Tool #17 |
| 7.3 Code Review | 22 | 5 categories, severity ranking, Tool #18 |
| 8.1 Build Configuration | 23 | Platform, scenes, player settings, Tool #19 |
| 8.2 Build Production | 23 | BuildPlayer + report, Tool #20 |
| 8.3 Build Verification | 24 | Launch + monitor + test builds, Tool #21 |
| 9.1 Package Management | 25 | List, install, remove, update, Tool #22 |
| 9.2 Documentation | 26 | API docs, package docs, best practices, Tool #23 |
| 10 External Tools | 06, 08, 09, 19, 22, 24 | Mooble, unity-yaml-parser, Deps Hunter, ParrelSync, Roslyn, AltTester/GameDriver evaluated |
| 11 Flow Sequence | 27 | Full 15-step cycle |
| 12 Concrete Example | 27 | Pause menu implementation |
| 13 Constraints | All | IL2CPP, Force Text, licensing noted throughout |

## Core Design Principles (Brief Section 3)

Each challenge embodies these principles:

- **Token Efficiency** — All tools return structured JSON, not raw data
- **Programmatic Speed** — Function calls, not agent reasoning loops
- **Deep Introspection** — Full property-level access to everything
- **Visual Competence** — Screenshot capture + validation + brand consistency
- **Full Lifecycle Coverage** — Code → Test → Build → Verify
- **Lights-Out Operation** — Challenge 28 proves zero human intervention
