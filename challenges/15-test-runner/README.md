# Challenge 15: Test Runner Integration

## Overview
Build the TestRunner (Brief Tool #13) — wraps Unity Test Framework CLI execution, runs Edit Mode and Play Mode test suites, parses NUnit XML results, and returns structured pass/fail/error reports as JSON. Also supports writing and running custom validation tests.

## Brief Reference
Section 6.1 (Automated Test Execution) — Edit Mode tests via CLI (`unity -runTests -testPlatform editmode`), Play Mode tests (`-testPlatform playmode`), parse NUnit XML results. Custom validation tests for project-wide rules.

## Problem Statement
Testing is fundamental to lights-out operation — the agent must verify its own work without human review. Unity Test Framework supports Edit Mode tests (logic without Play mode) and Play Mode tests (runtime behavior). Currently, there's no way for the agent to run tests and get structured results. The TestRunner bridges this gap, enabling automated test execution and result parsing.

## Success Criteria
1. `UnityBridge.TestRunner.RunEditModeTests(string filter)` runs Edit Mode tests and returns structured results
2. `UnityBridge.TestRunner.RunPlayModeTests(string filter)` runs Play Mode tests and returns structured results
3. `UnityBridge.TestRunner.RunAllTests()` runs both Edit and Play Mode tests
4. Results include: test name, status (pass/fail/skip/error), duration, error message and stack trace for failures
5. Filter parameter supports: specific test name, test class name, category, or wildcard
6. `UnityBridge.TestRunner.GetTestList()` returns all available tests without running them
7. Uses Unity's TestRunnerApi (UnityEditor.TestTools.TestRunner.Api) for programmatic execution instead of CLI — avoids needing to launch separate Unity process
8. Handles test timeouts gracefully
9. Returns summary: total tests, passed, failed, skipped, errors, total duration
10. Custom validation test scaffold: `UnityBridge.TestRunner.RunValidation(string validatorName)` executes project-wide validation checks (all prefabs have colliders, all UI text uses TMP, no missing references)

## Expected Development Work
### New Files
- `Unity-Bridge/Editor/Tools/TestRunner.cs` — Uses `UnityEditor.TestTools.TestRunner.Api.TestRunnerApi` to execute tests programmatically. Registers callbacks via `ICallbacks` interface to collect results. Alternatively, if API is complex, can execute tests via `EditorApplication.ExecuteMenuItem("Window/General/Test Runner")` approach or direct NUnit runner invocation.
- `Unity-Bridge/Editor/Tools/ProjectValidators.cs` — Custom validation methods: ValidateNoMissingScripts(), ValidateAllUIUsesTMP(), ValidateNoMissingReferences(), ValidateColliderOnRigidbody(). Each returns a list of validation issues.

### Assembly Definition Note
Test scripts require reference to `UnityEditor.TestRunner` and `UnityEngine.TestRunner` assemblies. May need to update `UnityBridge.Editor.asmdef` to include these references.

### JSON Output Format
```json
{
  "testPlatform": "EditMode",
  "results": [
    {
      "name": "TestPlayerHealth",
      "fullName": "Tests.PlayerTests.TestPlayerHealth",
      "status": "Passed",
      "duration": 0.023,
      "message": null,
      "stackTrace": null
    },
    {
      "name": "TestInvalidDamage",
      "fullName": "Tests.PlayerTests.TestInvalidDamage",
      "status": "Failed",
      "duration": 0.015,
      "message": "Expected 100 but was 0",
      "stackTrace": "at Tests.PlayerTests.TestInvalidDamage() in Assets/Tests/PlayerTests.cs:line 45"
    }
  ],
  "summary": {"total": 10, "passed": 9, "failed": 1, "skipped": 0, "errors": 0, "duration": 1.234}
}
```

## Testing Protocol
1. Create a simple Edit Mode test class: `Assets/Tests/Editor/SampleEditTests.cs` with one passing and one failing test
2. `bash .agent/tools/unity_bridge.sh compile` — Confirm
3. `bash .agent/tools/unity_bridge.sh execute UnityBridge.TestRunner.GetTestList` — Read output, verify test discovered
4. `bash .agent/tools/unity_bridge.sh execute UnityBridge.TestRunner.RunEditModeTests '[""]'` — Read output, verify results
5. Verify passing test shows "Passed", failing test shows "Failed" with message
6. Run RunValidation for a custom validator
7. Fix the failing test, recompile, re-run — verify all pass

## Dependencies
- Challenge 01 (Execute Endpoint)
