# TestRunner

Test execution and result parsing. Runs Edit Mode and Play Mode tests via the Unity Test Runner API, parses NUnit XML results, and provides project-wide validation checks.

## Key Methods

| Method | Description |
|--------|-------------|
| `RunEditModeTests(filter)` | Run Edit Mode tests, optionally filtered by name |
| `RunPlayModeTests(filter)` | Run Play Mode tests, optionally filtered by name |
| `RunAllTests()` | Run both Edit Mode and Play Mode tests |
| `GetTestList()` | Discover all test methods across loaded assemblies |
| `RunValidation(validatorName)` | Run a project-wide validation check |

## Usage

```bash
bash .agent/tools/unity_bridge.sh execute UnityBridge.TestRunner.RunEditModeTests '[""]'
bash .agent/tools/unity_bridge.sh execute UnityBridge.TestRunner.RunEditModeTests '["ScoreTests"]'
bash .agent/tools/unity_bridge.sh execute UnityBridge.TestRunner.RunPlayModeTests '[""]'
bash .agent/tools/unity_bridge.sh execute UnityBridge.TestRunner.RunAllTests
bash .agent/tools/unity_bridge.sh execute UnityBridge.TestRunner.GetTestList
bash .agent/tools/unity_bridge.sh execute UnityBridge.TestRunner.RunValidation '["missingscripts"]'
```

## Test Execution Strategy

1. Tries the `TestRunnerApi` (programmatic test execution)
2. Falls back to parsing NUnit XML result files (`TestResults-editmode.xml`, `TestResults-playmode.xml`)
3. Final fallback: discovers tests via reflection, executes Edit Mode tests directly (respects `[SetUp]`/`[TearDown]`)

Play Mode tests (`[UnityTest]`) cannot be executed directly via reflection and will report as `Skipped` in the fallback path.

## Available Validators

| Validator Name | What It Checks |
|---------------|----------------|
| `missingscripts` | Prefabs with null/missing script components |
| `missingreferences` | Prefabs with broken object references (non-zero ID but null value) |
| `tmpusage` | Prefabs using legacy `UI.Text` instead of TextMeshProUGUI |
| `collideronrigidbody` | Prefabs with Rigidbody but no Collider |

## Response Format

**Test Results:**
```json
{
  "success": true,
  "testPlatform": "EditMode",
  "results": [
    {"name": "TestAdd", "fullName": "MyTests.TestAdd", "status": "Passed", "duration": 0.001, "message": ""}
  ],
  "summary": {"total": 5, "passed": 4, "failed": 1, "skipped": 0}
}
```

**Validation Results:**
```json
{
  "success": true,
  "validator": "missingscripts",
  "issues": ["Missing script in prefab: Assets/Prefabs/Old.prefab"],
  "issueCount": 1,
  "passed": false
}
```

## Examples

```bash
# Run all edit mode tests
bash .agent/tools/unity_bridge.sh execute UnityBridge.TestRunner.RunEditModeTests '[""]'

# Run only tests matching "Score"
bash .agent/tools/unity_bridge.sh execute UnityBridge.TestRunner.RunEditModeTests '["Score"]'

# Check for broken references in all prefabs
bash .agent/tools/unity_bridge.sh execute UnityBridge.TestRunner.RunValidation '["missingreferences"]'

# List all discovered tests
bash .agent/tools/unity_bridge.sh execute UnityBridge.TestRunner.GetTestList
```

## Common Pitfalls

- Pass an empty string `""` as the filter to run all tests -- do not omit the argument.
- Play Mode tests require Play Mode to run properly. The fallback reflection executor marks them as `Skipped`.
- The TestRunnerApi executes asynchronously. Results may not be immediately available -- the tool waits 2 seconds before checking.
- Validators scan all prefabs in the project via `AssetDatabase.FindAssets("t:Prefab")`, which can be slow on large projects.
- `RunAllTests` returns a combined JSON with `editMode` and `playMode` keys, each containing their own results structure.
