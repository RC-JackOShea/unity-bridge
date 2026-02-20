# Challenge 22: Code Reviewer & Static Analysis

## Overview

Build the CodeReviewer (Brief Tool #18) -- runs regex-based static analysis on C# source files and returns a structured issue report with severity rankings. Five review categories: Performance, Correctness, Unity Best Practices, Architecture, and Production Readiness. Uses line-by-line scanning with method-scope tracking to detect Unity-specific anti-patterns. Also evaluates Roslyn analyzers (Section 10) for future integration.

## Brief Reference

Section 7.3 (Code Review and Evaluation) -- Full section on code review: Static analysis via regex-based rules (Roslyn evaluated for future). Five review categories: (a) Performance -- allocations in Update, unnecessary Find/GetComponent in hot paths, coroutine misuse; (b) Correctness -- null reference risks, missing null checks on GetComponent, incorrect SerializedObject Update/ApplyModifiedProperties pairing, Undo registration missing; (c) Unity Best Practices -- singleton pattern issues, MonoBehaviour lifecycle ordering, improper DontDestroyOnLoad, mixing legacy/new Input System; (d) Architecture -- circular dependencies, God classes (too many responsibilities), tight coupling; (e) Production Readiness -- debug logging left in, hardcoded values that should be ScriptableObject configs, TODO comments, disabled code blocks. Returns severity-ranked list with file path, line number, and suggested fix. Section 10 -- Roslyn analyzers for future enhancement.

## Problem Statement

The agent generates code (Challenge 21) and modifies existing scripts, but needs to verify quality before delivery. Human code review catches issues that compilers miss: performance anti-patterns like `GetComponent` inside `Update()`, Unity-specific gotchas like missing null checks, architectural problems like God classes, and production readiness concerns like leftover `Debug.Log` calls. An automated code reviewer enables the agent to self-evaluate and iterate on its own output -- generate code, review it, fix issues, review again -- without human intervention.

## Success Criteria

1. `UnityBridge.CodeReviewer.ReviewFile(string filePath)` analyzes a single C# file and returns severity-ranked issues
2. `UnityBridge.CodeReviewer.ReviewProject()` analyzes all C# scripts under `Assets/`
3. `UnityBridge.CodeReviewer.ReviewFiles(string[] filePaths)` analyzes a specific set of files
4. Detects `GetComponent<T>()` and `GameObject.Find()` calls inside `Update()`, `FixedUpdate()`, and `LateUpdate()` (Performance)
5. Detects `new` allocations and string concatenation (`+` on strings) inside Update-family methods (Performance)
6. Detects missing null checks after `GetComponent<T>()` calls (Correctness)
7. Detects `TODO`, `FIXME`, and `HACK` comments (Production Readiness)
8. Detects `Debug.Log` / `Debug.LogWarning` / `Debug.LogError` calls outside `#if UNITY_EDITOR` or `#if DEBUG` guards (Production Readiness)
9. Each issue includes severity ranking: `critical`, `warning`, or `info`
10. Returns structured JSON with: file path, line number, line content, rule ID, category, severity, description, and suggested fix for every issue

## Expected Development Work

### New Files

- **`Unity-Bridge/Editor/Tools/CodeReviewer.cs`** -- Static class in the `UnityBridge` namespace. Regex and line-by-line analysis engine. No external dependencies (no Roslyn). For each rule:
  1. Define the pattern (regex or line condition)
  2. Define the context (e.g., "inside Update method" requires tracking current method scope via brace depth)
  3. Define severity and message template

  **Method scope tracking**: maintain a brace-depth counter. When a line matches a method declaration like `void Update()`, record that we are inside an Update-family method. Increment depth on `{`, decrement on `}`. When depth returns to the method's entry level, we have exited the method. Rules that care about "hot path" context (Performance rules) check whether the current scope is an Update-family method.

  **Architecture rules**: build data from file-level analysis. Count total lines for God class detection. Scan `using` directives for circular dependency hints (requires cross-file analysis from CodebaseAnalyzer data).

  **Roslyn evaluation note**: document in code comments where Roslyn would provide more accurate analysis (e.g., distinguishing `string` concatenation from numeric `+`, understanding type information for null checks). Regex-based analysis is a pragmatic starting point that covers the highest-impact rules.

### Review Categories and Rules

#### (a) Performance
| Rule ID | Pattern | Severity |
|---------|---------|----------|
| `PERF001` | `GameObject.Find(` inside Update-family method | warning |
| `PERF002` | `GetComponent<` inside Update-family method | warning |
| `PERF003` | `FindObjectOfType` inside Update-family method | warning |
| `PERF004` | `new ` allocation inside Update-family method (excluding `new Vector3`, `new Quaternion`) | warning |
| `PERF005` | String concatenation (`" +` or `+ "`) inside Update-family method | info |
| `PERF006` | LINQ method (`.Where(`, `.Select(`, `.OrderBy(`) inside Update-family method | warning |

#### (b) Correctness
| Rule ID | Pattern | Severity |
|---------|---------|----------|
| `CORR001` | `GetComponent<T>()` call without null check on next lines | warning |
| `CORR002` | `SerializedObject` usage without matching `Update()` / `ApplyModifiedProperties()` pair | warning |
| `CORR003` | Modification to serialized property without `Undo.RecordObject` | info |
| `CORR004` | Float comparison using `==` or `!=` | info |

#### (c) Unity Best Practices
| Rule ID | Pattern | Severity |
|---------|---------|----------|
| `BEST001` | Public fields that should be `[SerializeField] private` | info |
| `BEST002` | `SendMessage` or `BroadcastMessage` usage | info |
| `BEST003` | Constructor in a MonoBehaviour-derived class | warning |
| `BEST004` | Static fields in a MonoBehaviour-derived class | info |

#### (d) Architecture
| Rule ID | Pattern | Severity |
|---------|---------|----------|
| `ARCH001` | Class exceeds 500 lines (God class) | warning |
| `ARCH002` | More than 10 `[SerializeField]` fields in one class (complexity) | info |
| `ARCH003` | Circular `using` references between project namespaces (cross-file) | warning |

#### (e) Production Readiness
| Rule ID | Pattern | Severity |
|---------|---------|----------|
| `PROD001` | `TODO` / `FIXME` / `HACK` comment | info |
| `PROD002` | `Debug.Log` / `Debug.LogWarning` / `Debug.LogError` outside conditional compilation | warning |
| `PROD003` | Commented-out code blocks (lines starting with `//` that contain code-like patterns) | info |
| `PROD004` | Hardcoded file paths or URLs in string literals | info |
| `PROD005` | Empty `catch` blocks | warning |

### JSON Output Format

```json
{
  "success": true,
  "filesReviewed": 3,
  "issues": [
    {
      "ruleId": "PERF001",
      "category": "Performance",
      "severity": "warning",
      "file": "Assets/Scripts/PlayerController.cs",
      "line": 42,
      "lineContent": "    var player = GameObject.Find(\"Player\");",
      "description": "GameObject.Find() called inside Update() -- this is expensive and runs every frame",
      "suggestion": "Cache the result in Start() or Awake() and store in a field"
    },
    {
      "ruleId": "PROD001",
      "category": "ProductionReadiness",
      "severity": "info",
      "file": "Assets/Scripts/GameManager.cs",
      "line": 23,
      "lineContent": "    // TODO: implement save system",
      "description": "TODO comment found -- unresolved work item",
      "suggestion": "Resolve or create a tracked issue for this TODO"
    }
  ],
  "summary": {
    "critical": 0,
    "warning": 3,
    "info": 5,
    "byCategory": {
      "Performance": 2,
      "Correctness": 0,
      "UnityBestPractices": 1,
      "Architecture": 0,
      "ProductionReadiness": 5
    }
  }
}
```

### Severity Definitions

- **critical** -- Likely bug or crash at runtime. Must be fixed before shipping. Examples: null dereference in hot path, missing required component.
- **warning** -- Significant quality issue that will cause performance problems, maintenance burden, or subtle bugs. Should be fixed. Examples: `GetComponent` in Update, God class, `Debug.Log` in production.
- **info** -- Style suggestion or minor improvement. Fix when convenient. Examples: TODO comments, public fields that could be private with `[SerializeField]`, string concat in Update.

## Testing Protocol

1. Create a test script with known issues: `Assets/Tests/ReviewTestScript.cs` containing:
   - `GameObject.Find("Player")` inside `void Update()`
   - `GetComponent<Rigidbody>()` inside `void Update()` without caching
   - `new List<int>()` inside `void Update()`
   - A `// TODO: fix this` comment
   - `Debug.Log("test")` without `#if UNITY_EDITOR` guard
   - A class body exceeding 500 lines (or test with a shorter file and verify ARCH001 triggers appropriately)
2. `bash .agent/tools/unity_bridge.sh compile` -- Read `C:/temp/unity_bridge_output.txt`, confirm compilation succeeds.
3. Review the test script:
   `bash .agent/tools/unity_bridge.sh execute UnityBridge.CodeReviewer.ReviewFile '["Assets/Tests/ReviewTestScript.cs"]'` -- Read output.
4. Verify each planted issue is detected with correct rule ID, severity, category, and accurate line number.
5. Review all project scripts:
   `bash .agent/tools/unity_bridge.sh execute UnityBridge.CodeReviewer.ReviewProject` -- Read output, verify summary counts.
6. Review a specific set of files:
   `bash .agent/tools/unity_bridge.sh execute UnityBridge.CodeReviewer.ReviewFiles '["[\"Assets/Tests/ReviewTestScript.cs\"]"]'` -- Read output.
7. Review a clean, well-written file -- verify zero or minimal issues (no false positives).
8. Verify line numbers in the output match the actual line positions in the source files.

## Dependencies

- **Challenge 01 (Execute Endpoint)** -- All methods are invoked via `bash .agent/tools/unity_bridge.sh execute UnityBridge.CodeReviewer.<Method>`.
- **Challenge 20 (Codebase Analyzer)** -- Provides file discovery, classification, and namespace data for architectural analysis (circular dependency detection, cross-file context).
