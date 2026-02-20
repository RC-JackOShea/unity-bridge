# Challenge 20: Codebase Analyzer

## Overview

Build the CodebaseAnalyser (Brief Tool #16) -- scans all C# files in the project, classifies scripts by type (MonoBehaviour, ScriptableObject, EditorScript, plain C#, interface, enum, struct), maps inheritance hierarchies, identifies component dependencies, detects event/message flow patterns, analyzes API usage, and detects Input System configuration. Returns structured JSON enabling agents to understand existing code patterns before generating new code.

## Brief Reference

Section 7.1 (Codebase Analysis) -- Full section on codebase analysis: list all scripts classified by type, map inheritance hierarchies, identify component dependencies (`[RequireComponent]`, `GetComponent`), map event/message flow (`UnityEvent`, `SendMessage`, C# events/delegates), detect API usage patterns (old vs new Input System, UGUI vs UI Toolkit, rendering pipeline), detect Input System mode via Player Settings Active Input Handling.

## Problem Statement

Code generation (Challenge 21) and code review (Challenge 22) require deep understanding of existing codebase patterns. Without analysis, an agent generating a new MonoBehaviour has no way to know: what namespaces the project uses, whether fields follow `_camelCase` or `m_camelCase` convention, whether the project uses the new Input System or legacy Input, what base classes or interfaces are common, how components reference each other, or what event patterns are followed.

The Codebase Analyzer solves this by scanning all `Assets/**/*.cs` files and extracting structural information through regex-based parsing. It classifies every script, builds inheritance trees, maps dependencies between components, identifies event flow patterns, and detects which Unity APIs the project relies on. The output is a comprehensive JSON report that other tools and the agent itself can use to generate consistent, idiomatic code.

## Success Criteria

1. `UnityBridge.CodebaseAnalyzer.AnalyzeProject()` scans all C# scripts under `Assets/` and returns a complete structured analysis as JSON.
2. Script classification correctly identifies: `MonoBehaviour`, `ScriptableObject`, `EditorScript` (scripts in `Editor/` folders or deriving from `Editor`/`EditorWindow`), `Interface`, `Enum`, `Struct`, `AbstractClass`, `StaticClass`, and `PureClass` (plain C# classes).
3. Inheritance tree: for each class, the base class chain is extracted from `class X : Y` patterns, building a tree structure.
4. Component dependencies are detected: `[RequireComponent(typeof(X))]` attributes, `GetComponent<X>()` calls, and `[SerializeField]` references to other component types.
5. Event flow is mapped: `UnityEvent` fields, `SendMessage`/`BroadcastMessage` calls, C# `event` and `delegate` declarations, and observer pattern implementations.
6. API usage patterns are detected: old Input (`UnityEngine.Input`, `Input.GetKey`, `Input.GetAxis`), new Input System (`UnityEngine.InputSystem`, `InputAction`), UGUI (`UnityEngine.UI`), UI Toolkit (`UnityEngine.UIElements`), and rendering pipeline (`UnityEngine.Rendering.Universal`, `UnityEngine.Rendering.HighDefinition`).
7. `UnityBridge.CodebaseAnalyzer.GetScriptDetails(string scriptPath)` performs deep analysis of a single script, returning: class name, base class, interfaces, serialized fields, Unity lifecycle methods used (`Awake`, `Start`, `Update`, `OnEnable`, etc.), `GetComponent` calls, and `[RequireComponent]` attributes.
8. `UnityBridge.CodebaseAnalyzer.DetectInputSystem()` determines whether the project uses legacy Input, new Input System, or both -- checks Player Settings `Active Input Handling` and scans code for usage of each system.
9. `UnityBridge.CodebaseAnalyzer.MapDependencies()` builds a dependency graph between scripts: which scripts reference which other scripts via `GetComponent`, `[RequireComponent]`, `[SerializeField]` type references, and `using` directives for project namespaces.
10. Convention detection: identifies naming patterns (field naming style, method naming style), whether the project uses namespaces, `#region` usage, average file length, and common code organization patterns.

## Expected Development Work

### New Files

- **`Unity-Bridge/Editor/Tools/CodebaseAnalyzer.cs`** -- Static class in the `UnityBridge` namespace. Uses regex-based parsing of C# source files (not Roslyn) for simplicity and zero external dependencies. Must include:

  - `public static string AnalyzeProject()` -- Scans `Assets/**/*.cs` using `Directory.GetFiles` with recursive search. For each file, reads the source text and applies regex patterns to extract structural information. Aggregates results into a summary. Returns JSON:
    ```json
    {
      "success": true,
      "summary": {
        "totalScripts": 45,
        "byType": {
          "MonoBehaviour": 20,
          "ScriptableObject": 5,
          "EditorScript": 8,
          "Interface": 3,
          "Enum": 4,
          "Struct": 2,
          "PureClass": 3
        },
        "namespaces": ["Game.Player", "Game.UI", "Game.Networking"],
        "inputSystem": "New",
        "uiFramework": "UGUI",
        "renderPipeline": "URP"
      },
      "scripts": [
        {
          "path": "Assets/Scripts/PlayerController.cs",
          "className": "PlayerController",
          "namespace": "Game.Player",
          "classification": "MonoBehaviour",
          "baseClass": "MonoBehaviour",
          "interfaces": ["IDamageable"],
          "serializedFields": [
            {"name": "healthBar", "type": "Slider"},
            {"name": "moveSpeed", "type": "float"}
          ],
          "lifecycleMethods": ["Awake", "Start", "Update", "OnDestroy"],
          "dependencies": {
            "requireComponent": ["Rigidbody", "CapsuleCollider"],
            "getComponent": ["Animator", "AudioSource"],
            "serializedRefs": [{"name": "healthBar", "type": "Slider"}]
          },
          "events": {
            "unityEvents": ["onDeath", "onDamage"],
            "csharpEvents": ["OnHealthChanged"],
            "sendMessage": []
          },
          "apiUsage": ["InputSystem", "Rigidbody", "Animator"]
        }
      ],
      "conventions": {
        "fieldNaming": "camelCase",
        "methodNaming": "PascalCase",
        "usesRegions": false,
        "usesNamespaces": true,
        "averageFileLength": 150
      }
    }
    ```

  - `public static string GetScriptDetails(string scriptPath)` -- Deep analysis of a single script file. Reads the file, applies all regex patterns, and returns detailed information. Returns JSON:
    ```json
    {
      "success": true,
      "path": "Assets/Scripts/PlayerController.cs",
      "className": "PlayerController",
      "namespace": "Game.Player",
      "classification": "MonoBehaviour",
      "baseClass": "MonoBehaviour",
      "interfaces": ["IDamageable", "ISerializationCallbackReceiver"],
      "serializedFields": [
        {"name": "_health", "type": "float", "visibility": "private"},
        {"name": "maxHealth", "type": "float", "visibility": "public"}
      ],
      "lifecycleMethods": ["Awake", "Start", "Update", "FixedUpdate", "OnDestroy"],
      "getComponentCalls": ["Animator", "Rigidbody", "AudioSource"],
      "requireComponentAttrs": ["Rigidbody", "CapsuleCollider"],
      "unityEvents": ["onDeath", "onDamage"],
      "csharpEvents": ["OnHealthChanged"],
      "delegateDeclarations": ["HealthChangeHandler"],
      "sendMessageCalls": [],
      "broadcastMessageCalls": [],
      "usingDirectives": ["UnityEngine", "UnityEngine.InputSystem", "System.Collections.Generic"],
      "lineCount": 185
    }
    ```

  - `public static string DetectInputSystem()` -- Checks Player Settings via `PlayerSettings.GetScriptingDefineSymbolsForGroup` for `ENABLE_INPUT_SYSTEM` and `ENABLE_LEGACY_INPUT_MANAGER`. Also scans scripts for `using UnityEngine.InputSystem` vs `Input.GetKey`/`Input.GetAxis` usage. Returns JSON:
    ```json
    {
      "success": true,
      "playerSettingsMode": "New",
      "codeUsage": {
        "legacyInputUsage": 3,
        "newInputSystemUsage": 12,
        "scriptsUsingLegacy": ["Assets/Scripts/OldController.cs"],
        "scriptsUsingNew": ["Assets/Scripts/PlayerController.cs", "Assets/Scripts/UIManager.cs"]
      },
      "recommendation": "Project primarily uses New Input System with 3 legacy references that should be migrated"
    }
    ```

  - `public static string MapDependencies()` -- Builds a dependency graph. For each script, identifies outgoing references to other project scripts via `GetComponent<T>()`, `[RequireComponent(typeof(T))]`, `[SerializeField]` fields typed as project classes, and `using` directives for project namespaces. Returns JSON:
    ```json
    {
      "success": true,
      "nodes": [
        {"script": "PlayerController", "path": "Assets/Scripts/PlayerController.cs"},
        {"script": "HealthSystem", "path": "Assets/Scripts/HealthSystem.cs"}
      ],
      "edges": [
        {"from": "PlayerController", "to": "HealthSystem", "type": "GetComponent"},
        {"from": "PlayerController", "to": "Rigidbody", "type": "RequireComponent"},
        {"from": "UIManager", "to": "PlayerController", "type": "SerializeField"}
      ]
    }
    ```

### Key Regex Patterns

- **Class declarations**: `class\s+(\w+)\s*(?:<[^>]+>)?\s*:\s*([^{]+)` -- captures class name and base class / interface list
- **Attributes**: `\[RequireComponent\(typeof\((\w+)\)\)\]` -- captures required component types
- **GetComponent calls**: `GetComponent<(\w+)>\s*\(` -- captures component type parameter
- **UnityEvent fields**: `(?:public|private|protected)\s+UnityEvent(?:<[^>]*>)?\s+(\w+)` -- captures event field names
- **SerializeField**: `\[SerializeField\]\s*(?:private|protected)?\s*(\w+(?:<[^>]*>)?)\s+(\w+)` -- captures type and field name
- **C# events**: `event\s+(\w+(?:<[^>]*>)?)\s+(\w+)` -- captures delegate type and event name
- **SendMessage**: `SendMessage\s*\(\s*"(\w+)"` -- captures message name
- **Lifecycle methods**: `(?:void|IEnumerator)\s+(Awake|Start|Update|FixedUpdate|LateUpdate|OnEnable|OnDisable|OnDestroy|OnGUI|OnValidate)\s*\(` -- captures Unity lifecycle method names
- **Namespace**: `namespace\s+([\w.]+)` -- captures namespace
- **Using directives**: `using\s+([\w.]+)\s*;` -- captures imported namespaces
- **Interface/enum/struct**: `(interface|enum|struct)\s+(\w+)` -- captures type keyword and name

### Alternative: Roslyn

If `Microsoft.CodeAnalysis` is available in the project (e.g., via a package reference), Roslyn provides accurate AST-based analysis. However, regex is simpler, has no dependencies, and is sufficient for the classification and pattern-matching tasks described here. Roslyn evaluation should be noted as a future enhancement.

## Testing Protocol

1. `bash .agent/tools/unity_bridge.sh compile` -- Read `C:/temp/unity_bridge_output.txt`, confirm compilation succeeds with no errors.
2. `bash .agent/tools/unity_bridge.sh execute UnityBridge.CodebaseAnalyzer.AnalyzeProject` -- Read output. Verify JSON contains a `summary` with script counts and a `scripts` array with classified entries.
3. Verify known scripts appear with correct classification -- e.g., `SpawnClickableButton` should be classified as `MonoBehaviour`, editor scripts under `Unity-Bridge/Editor/` should be classified as `EditorScript`.
4. Verify inheritance detection: runtime scripts should show `MonoBehaviour` as base class, editor tools should show `Editor` or `EditorWindow`.
5. `bash .agent/tools/unity_bridge.sh execute UnityBridge.CodebaseAnalyzer.GetScriptDetails '["Assets/path/to/known/script.cs"]'` -- Read output. Verify detailed analysis of a specific script matches its actual content.
6. `bash .agent/tools/unity_bridge.sh execute UnityBridge.CodebaseAnalyzer.DetectInputSystem` -- Read output. Verify Input System detection matches the project's actual Player Settings configuration.
7. `bash .agent/tools/unity_bridge.sh execute UnityBridge.CodebaseAnalyzer.MapDependencies` -- Read output. Verify dependency edges exist for known component relationships in the project.
8. Verify convention detection: check that `fieldNaming`, `methodNaming`, and `usesNamespaces` values match the actual code style observed in project files.
9. Test with a script containing syntax errors or unusual patterns -- verify the analyzer handles it gracefully (skips or reports the issue) rather than failing the entire analysis.

## Dependencies

- **Challenge 01 (Execute Endpoint)** -- All methods are invoked via `bash .agent/tools/unity_bridge.sh execute UnityBridge.CodebaseAnalyzer.<Method>`.
