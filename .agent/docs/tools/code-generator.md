# CodeGenerator

Convention-aware C# code generation. Detects project coding conventions (field naming, brace style, namespace patterns) and generates MonoBehaviour, Editor, and Test scripts that follow those conventions.

## Key Methods

| Method | Description |
|--------|-------------|
| `DetectConventions()` | Scan all `.cs` files in Assets and report detected coding conventions |
| `GenerateMonoBehaviour(jsonSpec)` | Generate a MonoBehaviour script with serialized fields, lifecycle methods, events |
| `GenerateEditorScript(jsonSpec)` | Generate a CustomInspector, EditorWindow, or PropertyDrawer script |
| `GenerateTestScript(jsonSpec)` | Generate an NUnit test script (Edit Mode or Play Mode) |

## Usage

```bash
bash .agent/tools/unity_bridge.sh execute UnityBridge.CodeGenerator.DetectConventions
bash .agent/tools/unity_bridge.sh execute UnityBridge.CodeGenerator.GenerateMonoBehaviour '<jsonSpec>'
bash .agent/tools/unity_bridge.sh execute UnityBridge.CodeGenerator.GenerateEditorScript '<jsonSpec>'
bash .agent/tools/unity_bridge.sh execute UnityBridge.CodeGenerator.GenerateTestScript '<jsonSpec>'
```

## DetectConventions Response

```json
{
  "success": true,
  "conventions": {
    "fieldNaming": "_camelCase",
    "privateFieldPrefix": "_",
    "methodNaming": "PascalCase",
    "namespacePattern": "Game.*",
    "usesNamespaces": true,
    "braceStyle": "nextLine",
    "usingDirectiveOrder": "SystemFirst",
    "commentStyle": "xml",
    "usesRegions": false,
    "averageFileLength": 85,
    "filesAnalyzed": 12
  }
}
```

## GenerateMonoBehaviour Spec

```json
{
  "className": "PlayerController",
  "namespace": "Game.Player",
  "outputPath": "Assets/Scripts/Player/PlayerController.cs",
  "serializedFields": [
    {"name": "_speed", "type": "float", "defaultValue": "5.0f"},
    {"name": "_jumpForce", "type": "float", "defaultValue": "10.0f"}
  ],
  "lifecycleMethods": ["Awake", "Start", "Update", "OnDestroy"],
  "customMethods": [
    {"name": "Jump", "returnType": "void", "body": "// jump logic", "params": [{"type": "float", "name": "force"}]}
  ],
  "events": [{"name": "onDeath", "type": "UnityEvent"}],
  "requireComponents": ["Rigidbody", "CapsuleCollider"],
  "interfaces": ["IDamageable"]
}
```

## GenerateEditorScript Spec

```json
{
  "className": "PlayerControllerEditor",
  "namespace": "Game.Editor",
  "outputPath": "Assets/Editor/PlayerControllerEditor.cs",
  "editorType": "CustomInspector",
  "targetType": "PlayerController",
  "serializedProperties": ["_speed", "_jumpForce"]
}
```

`editorType` options: `"CustomInspector"`, `"EditorWindow"`, `"PropertyDrawer"`.

## GenerateTestScript Spec

```json
{
  "className": "PlayerTests",
  "namespace": "Game.Tests",
  "outputPath": "Assets/Tests/PlayerTests.cs",
  "testMode": "EditMode",
  "setupBody": "// create test objects",
  "teardownBody": "// cleanup",
  "testMethods": [
    {"name": "TestJumpAppliesForce", "body": "Assert.IsTrue(true);"},
    {"name": "TestSpeedClamped", "body": "Assert.AreEqual(5f, speed);"}
  ]
}
```

`testMode`: `"EditMode"` uses `[Test]`; `"PlayMode"` uses `[UnityTest]` with `IEnumerator` return type.

## Examples

```bash
# Detect project conventions first
bash .agent/tools/unity_bridge.sh execute UnityBridge.CodeGenerator.DetectConventions

# Generate a MonoBehaviour
bash .agent/tools/unity_bridge.sh execute UnityBridge.CodeGenerator.GenerateMonoBehaviour '{"className":"EnemyAI","namespace":"Game.Enemies","outputPath":"Assets/Scripts/Enemies/EnemyAI.cs","serializedFields":[{"name":"_health","type":"int","defaultValue":"100"}],"lifecycleMethods":["Start","Update"]}'

# Generate a test script
bash .agent/tools/unity_bridge.sh execute UnityBridge.CodeGenerator.GenerateTestScript '{"className":"EnemyTests","outputPath":"Assets/Tests/EnemyTests.cs","testMode":"EditMode","testMethods":[{"name":"TestHealthInit","body":"Assert.AreEqual(100, enemy.Health);"}]}'
```

## Common Pitfalls

- All generate methods refuse to overwrite existing files. Delete the file first or choose a different path.
- The tool writes to the filesystem directly -- run `compile` afterward so Unity picks up the new script.
- `DetectConventions` skips files in `Temp/` and `.generated` files. It scans only under `Assets/`.
- Generated code uses the convention defaults, not the detected conventions. Detection is informational for the agent to adapt its own edits.
- Using types like `Slider`, `Image`, `Button`, `Text` in serialized fields auto-adds `using UnityEngine.UI;`.
