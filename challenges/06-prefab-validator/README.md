# Challenge 06: Prefab Validator

## Overview

Build the PrefabValidator (Brief Tool #6) — runs a configurable set of validation rules against prefabs and returns a structured report of issues. This is the first quality assurance tool, essential for the agent to self-check its own created assets. Also evaluates the external tool Mooble for integration.

## Brief Reference

Section 4.2 — "Evaluate prefabs for problems — Build a validation tool that checks for: missing script references, broken asset references, components with default/unset values, incorrect layer assignments, missing colliders on interactive objects, UI elements with zero size." Section 10 — "Mooble: Static analysis tool for prefabs and scenes with customisable rules."

## Problem Statement

When the agent creates or modifies prefabs (Challenge 05), it needs to verify they are valid before moving on. Common issues include: missing scripts (GUID references to deleted or renamed scripts), null object references in fields that should be populated, UI elements with zero-size RectTransforms that will be invisible, interactive objects without colliders that cannot receive input, and components left at default values that should have been configured. Without validation, the agent might produce assets that appear correct in code but fail at runtime — a silent bug that wastes entire debugging sessions.

## Success Criteria

1. `UnityBridge.PrefabValidator.ValidatePrefab(string prefabPath)` validates a single prefab and returns a structured JSON report
2. `UnityBridge.PrefabValidator.ValidateAllPrefabs()` validates every prefab in the project and returns an aggregate report
3. `UnityBridge.PrefabValidator.ValidatePrefabWithRules(string prefabPath, string rulesJson)` validates with a configurable rule set (enable/disable specific rules)
4. Returns structured JSON report with issues categorized by severity: `error`, `warning`, `info`
5. The following validation rules are implemented:
   - **MissingScript** (error) — MonoBehaviour with null `m_Script` reference; detected via `component == null` after `GetComponents<Component>()`
   - **BrokenObjectReference** (warning) — SerializedProperty of `ObjectReference` type with null value where the field name suggests it should be set (e.g., not optional fields)
   - **ZeroSizeRectTransform** (warning) — RectTransform with `sizeDelta.x == 0` or `sizeDelta.y == 0`, indicating an invisible UI element
   - **EmptyGameObject** (info) — GameObject with no components except Transform (may be intentional as an organizational container)
   - **RigidbodyWithoutCollider** (warning) — GameObject with a Rigidbody but no Collider component
   - **DisabledComponent** (info) — Behaviour components where `enabled == false`, which may indicate forgotten-to-enable components
   - **DuplicateComponents** (warning) — Multiple components of the same type on the same GameObject (often unintentional, e.g., two AudioSources)
   - **MissingMeshReference** (warning) — MeshFilter with null mesh reference
   - **MissingMaterialReference** (warning) — MeshRenderer with null or empty materials array
6. Each issue includes: `rule` name, `severity` level, `gameObjectPath` within the prefab, `component` type (if applicable), `description` (human-readable explanation)
7. Returns summary counts: `{"errors": N, "warnings": N, "info": N}`

## Expected Development Work

### New Files

- **`Unity-Bridge/Editor/Tools/PrefabValidator.cs`** — Namespace: `UnityBridge`. Architecture:
  - `public static string ValidatePrefab(string prefabPath)` — Loads the prefab via `PrefabUtility.LoadPrefabContents()`, runs all validation rules against the hierarchy, unloads, returns JSON report
  - `public static string ValidateAllPrefabs()` — Finds all prefabs via `AssetDatabase.FindAssets("t:Prefab")`, validates each, aggregates results
  - `public static string ValidatePrefabWithRules(string prefabPath, string rulesJson)` — Same as ValidatePrefab but accepts a JSON object specifying which rules to enable (e.g., `{"MissingScript": true, "EmptyGameObject": false}`)
  - Private rule methods, each with signature `List<ValidationIssue> Check<RuleName>(GameObject root)`:
    - `CheckMissingScript(GameObject root)` — Recursively iterate all children, call `GetComponents<Component>()`, check for null entries
    - `CheckBrokenObjectReference(GameObject root)` — For each component, create `SerializedObject`, iterate properties, find `ObjectReference` types with null values
    - `CheckZeroSizeRectTransform(GameObject root)` — Find all `RectTransform` components, check `sizeDelta`
    - `CheckEmptyGameObject(GameObject root)` — Count components on each GameObject (excluding Transform)
    - `CheckRigidbodyWithoutCollider(GameObject root)` — Check for Rigidbody without any Collider
    - `CheckDisabledComponent(GameObject root)` — Find Behaviour components where `enabled == false`
    - `CheckDuplicateComponents(GameObject root)` — Group components by type, flag duplicates
    - `CheckMissingMeshReference(GameObject root)` — Check MeshFilter.sharedMesh for null
    - `CheckMissingMaterialReference(GameObject root)` — Check MeshRenderer.sharedMaterials for null/empty

### JSON Output Format

**Single prefab validation:**
```json
{
  "prefabPath": "Assets/Prefabs/Player.prefab",
  "valid": false,
  "issues": [
    {
      "rule": "MissingScript",
      "severity": "error",
      "gameObjectPath": "Player/OldComponent",
      "component": null,
      "description": "GameObject 'OldComponent' has a missing script reference — the MonoBehaviour's script asset could not be found"
    },
    {
      "rule": "RigidbodyWithoutCollider",
      "severity": "warning",
      "gameObjectPath": "Player",
      "component": "Rigidbody",
      "description": "GameObject 'Player' has a Rigidbody but no Collider component — physics interactions will not work as expected"
    }
  ],
  "summary": {
    "errors": 1,
    "warnings": 1,
    "info": 0,
    "totalIssues": 2
  }
}
```

**All prefabs validation:**
```json
{
  "prefabsValidated": 15,
  "prefabsWithIssues": 3,
  "results": [
    {"prefabPath": "Assets/Prefabs/Player.prefab", "errors": 1, "warnings": 1, "info": 0},
    {"prefabPath": "Assets/Prefabs/Enemy.prefab", "errors": 0, "warnings": 2, "info": 1}
  ],
  "totalSummary": {
    "errors": 1,
    "warnings": 3,
    "info": 1,
    "totalIssues": 5
  }
}
```

### Key Implementation Details

- **Rule architecture:** Each rule is a separate private method returning `List<ValidationIssue>`. This makes it easy to add new rules in future challenges. The `ValidationIssue` is an internal class/struct with `rule`, `severity`, `gameObjectPath`, `component`, `description` fields.
- **GameObject path construction:** As validation recursively traverses the hierarchy, build the path string by concatenating parent names with `/`. E.g., root "Player" -> child "Model" -> child "Mesh" produces path "Player/Model/Mesh".
- **Configurable rules:** The `rulesJson` parameter is a JSON object where keys are rule names and values are booleans. Default: all rules enabled. If a rule name is not in the JSON, use its default (enabled).
- **Performance with ValidateAllPrefabs:** For large projects, this could be slow. Consider reporting progress or limiting output to prefabs with issues only.

## Testing Protocol

1. Create a test prefab with known issues using PrefabCreator (Challenge 05) or by writing a small editor script:
   - An object with Rigidbody but no Collider (triggers RigidbodyWithoutCollider)
   - An empty child GameObject (triggers EmptyGameObject)
   - If possible, a missing mesh reference on a MeshFilter (triggers MissingMeshReference)
2. `bash .agent/tools/unity_bridge.sh compile` — Read `C:/temp/unity_bridge_output.txt`, confirm compilation succeeded
3. `bash .agent/tools/unity_bridge.sh execute UnityBridge.PrefabValidator.ValidatePrefab '["Assets/TestPrefabs/TestCube.prefab"]'` — Read output
4. Verify the RigidbodyWithoutCollider warning is detected (if the test prefab was set up accordingly)
5. Verify EmptyGameObject info issue is detected (if applicable)
6. Run `ValidateAllPrefabs`: `bash .agent/tools/unity_bridge.sh execute UnityBridge.PrefabValidator.ValidateAllPrefabs` — Read output, confirm it processes all prefabs without crashing
7. Test with a clean, properly configured prefab and verify zero issues (or only info-level issues)
8. Test rule configuration — disable the EmptyGameObject rule and verify it no longer appears:
   ```
   bash .agent/tools/unity_bridge.sh execute UnityBridge.PrefabValidator.ValidatePrefabWithRules '["Assets/TestPrefabs/TestCube.prefab", "{\"EmptyGameObject\": false}"]'
   ```

## Dependencies

- **Challenge 01 (Execute Endpoint)** — methods are called via the execute endpoint
- **Challenge 04 (Prefab Inventory/Detail Extractor)** — for understanding prefab structure and for listing all prefabs
- **Challenge 05 (Prefab Creator)** — for creating test prefabs with intentional issues to validate against
