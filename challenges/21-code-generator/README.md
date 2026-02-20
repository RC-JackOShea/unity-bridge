# Challenge 21: Code Generator

## Overview

Build the CodeGenerator (Brief Tool #17) -- accepts JSON specifications and generates C# scripts that follow detected project conventions. Generates MonoBehaviours, Editor scripts, and test scripts that match the existing codebase style. Convention detection scans existing `.cs` files using regex, building a conventions profile that all generation methods consume.

## Brief Reference

Section 7.2 (Code Generation) -- "Generate MonoBehaviours: Given a specification (purpose, serialized fields, lifecycle methods), generate complete C# script following project conventions. Generate Editor scripts: custom inspectors, editor windows, property drawers. Generate test scripts: Edit Mode and Play Mode test classes with proper NUnit attributes. Respect project conventions: naming, namespaces, organization."

## Problem Statement

The agent can write C# code directly, but without convention awareness, generated code will be inconsistent with the existing project. A MonoBehaviour created from scratch might use `m_health` in a project that uses `_health`, or omit namespaces in a project that wraps everything in `Game.Systems`. The CodeGenerator uses the CodebaseAnalyzer's output (Challenge 20) to produce code that matches the project's naming conventions, namespace structure, file organization, and coding patterns. It also provides structured templates for common Unity script types -- MonoBehaviours, Editor scripts, and test scripts -- ensuring generated code compiles without errors and reads as if a human wrote it.

## Success Criteria

1. `UnityBridge.CodeGenerator.GenerateMonoBehaviour(string jsonSpec)` creates a valid MonoBehaviour script matching project conventions
2. `UnityBridge.CodeGenerator.GenerateEditorScript(string jsonSpec)` creates an Editor script (custom inspector, editor window, or property drawer)
3. `UnityBridge.CodeGenerator.GenerateTestScript(string jsonSpec)` creates an Edit Mode or Play Mode test class with proper NUnit attributes
4. `UnityBridge.CodeGenerator.DetectConventions()` analyzes existing `.cs` files and returns a structured conventions profile (naming patterns, namespace patterns, brace style, using directive ordering, code comment style, file organization)
5. Generated code uses detected namespace pattern from the conventions profile
6. Field naming matches project conventions (camelCase, `_camelCase`, `m_camelCase`, PascalCase, etc.)
7. Private serialized fields use `[SerializeField]` attribute; `[RequireComponent]` attributes are included where specified
8. Generated code includes correct `using` directives based on required APIs
9. Generated code compiles without errors when written to the specified output path
10. Handles existing files safely -- warns instead of overwriting; returns JSON with generated file path, class name, and any warnings

## Expected Development Work

### New Files

- **`Unity-Bridge/Editor/Tools/CodeGenerator.cs`** -- Static class in the `UnityBridge` namespace. Template-based code generation using StringBuilder. Reads conventions from `DetectConventions()` output (which internally leverages CodebaseAnalyzer data from Challenge 20). Templates for:
  - **MonoBehaviour**: lifecycle methods, serialized fields, component references, `[RequireComponent]` attributes
  - **Editor script**: `[CustomEditor]` attribute, `OnInspectorGUI`, `SerializedProperty` handling; or `EditorWindow` with `OnGUI`; or `PropertyDrawer` with `OnGUI`/`CreatePropertyGUI`
  - **Test script**: `[Test]` methods, `[SetUp]`/`[TearDown]`, `[UnityTest]` for Play Mode, proper assembly references

### MonoBehaviour Spec Format

```json
{
  "className": "PlayerHealth",
  "namespace": "Game.Player",
  "outputPath": "Assets/Scripts/Player/PlayerHealth.cs",
  "serializedFields": [
    {"name": "maxHealth", "type": "float", "defaultValue": "100f"},
    {"name": "healthBar", "type": "Slider", "defaultValue": null}
  ],
  "lifecycleMethods": ["Awake", "Update"],
  "customMethods": [
    {
      "name": "TakeDamage",
      "returnType": "void",
      "params": [{"name": "amount", "type": "float"}],
      "body": "// TODO"
    }
  ],
  "requireComponents": ["Rigidbody"],
  "interfaces": ["IDamageable"],
  "events": [
    {"name": "onDeath", "type": "UnityEvent"}
  ]
}
```

### Editor Script Spec Format

```json
{
  "className": "PlayerHealthEditor",
  "namespace": "Game.Player.Editor",
  "outputPath": "Assets/Scripts/Player/Editor/PlayerHealthEditor.cs",
  "editorType": "CustomInspector",
  "targetType": "PlayerHealth",
  "serializedProperties": ["maxHealth", "healthBar"],
  "customGUI": true
}
```

### Test Script Spec Format

```json
{
  "className": "PlayerHealthTests",
  "namespace": "Game.Player.Tests",
  "outputPath": "Assets/Tests/Player/PlayerHealthTests.cs",
  "testMode": "EditMode",
  "testMethods": [
    {"name": "TakeDamage_ReducesHealth", "body": "// Arrange, Act, Assert"},
    {"name": "TakeDamage_ClampsAtZero", "body": "// Arrange, Act, Assert"}
  ],
  "setupBody": "// Create test instance",
  "teardownBody": "// Clean up"
}
```

### Conventions Profile Format

`DetectConventions()` scans existing `.cs` files, analyzes patterns using regex, and returns:

```json
{
  "success": true,
  "conventions": {
    "fieldNaming": "camelCase",
    "privateFieldPrefix": "_",
    "methodNaming": "PascalCase",
    "namespacePattern": "Game.*",
    "usesNamespaces": true,
    "braceStyle": "nextLine",
    "usingDirectiveOrder": "SystemFirst",
    "commentStyle": "xml",
    "usesRegions": false,
    "averageFileLength": 150,
    "filesAnalyzed": 42
  }
}
```

Generation methods consume this profile to produce code matching the project style.

### Convention Detection Details

- Scans all `.cs` files under `Assets/` (excluding auto-generated files)
- Analyzes field naming: counts occurrences of `_camelCase`, `m_camelCase`, plain `camelCase`, `PascalCase` for private fields; picks the majority pattern
- Analyzes namespace patterns: extracts all `namespace X.Y.Z` declarations, finds common root
- Detects brace style: checks whether opening braces follow the same line or the next line after method/class declarations
- Detects using directive ordering: checks whether `System.*` appears before or after `UnityEngine.*`
- Detects comment style: checks for `///` XML doc comments vs `//` inline comments on public members

## Testing Protocol

1. `bash .agent/tools/unity_bridge.sh compile` -- Read `C:/temp/unity_bridge_output.txt`, confirm compilation succeeds.
2. Detect conventions:
   `bash .agent/tools/unity_bridge.sh execute UnityBridge.CodeGenerator.DetectConventions` -- Read output, verify a structured conventions profile is returned with field naming, namespace pattern, and brace style.
3. Generate a MonoBehaviour:
   `bash .agent/tools/unity_bridge.sh execute UnityBridge.CodeGenerator.GenerateMonoBehaviour '["{ \"className\": \"TestGenMono\", \"namespace\": \"Game.Test\", \"outputPath\": \"Assets/Scripts/Test/TestGenMono.cs\", \"serializedFields\": [{ \"name\": \"speed\", \"type\": \"float\", \"defaultValue\": \"5f\" }], \"lifecycleMethods\": [\"Awake\", \"Update\"], \"customMethods\": [], \"requireComponents\": [] }"]'` -- Read output, confirm success.
4. `bash .agent/tools/unity_bridge.sh compile` -- Verify the generated MonoBehaviour compiles without errors.
5. Read the generated file and verify it uses the detected namespace pattern, field naming convention, and proper `[SerializeField]` on private fields.
6. Generate a test script:
   `bash .agent/tools/unity_bridge.sh execute UnityBridge.CodeGenerator.GenerateTestScript '["{ \"className\": \"TestGenTests\", \"namespace\": \"Game.Test.Tests\", \"outputPath\": \"Assets/Tests/TestGenTests.cs\", \"testMode\": \"EditMode\", \"testMethods\": [{ \"name\": \"SampleTest\", \"body\": \"Assert.Pass();\" }] }"]'` -- Read output, confirm success.
7. `bash .agent/tools/unity_bridge.sh compile` -- Verify the generated test script compiles.
8. Generate an editor script:
   `bash .agent/tools/unity_bridge.sh execute UnityBridge.CodeGenerator.GenerateEditorScript '["{ \"className\": \"TestGenEditor\", \"namespace\": \"Game.Test.Editor\", \"outputPath\": \"Assets/Scripts/Test/Editor/TestGenEditor.cs\", \"editorType\": \"CustomInspector\", \"targetType\": \"TestGenMono\" }"]'` -- Read output, confirm success.
9. `bash .agent/tools/unity_bridge.sh compile` -- Verify the generated editor script compiles.
10. Attempt generation to an existing file path -- verify the tool returns a warning and does not overwrite.

## Dependencies

- **Challenge 01 (Execute Endpoint)** -- All methods are invoked via `bash .agent/tools/unity_bridge.sh execute UnityBridge.CodeGenerator.<Method>`.
- **Challenge 20 (Codebase Analyzer)** -- Convention detection relies on codebase analysis data (file discovery, classification, naming pattern data).
