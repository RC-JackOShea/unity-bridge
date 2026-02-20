# Challenge 06: Post-Completion Checklist

## Documentation Updates

- [ ] Document all nine validation rules with descriptions, severity levels, and what triggers each rule:
  - MissingScript (error)
  - BrokenObjectReference (warning)
  - ZeroSizeRectTransform (warning)
  - EmptyGameObject (info)
  - RigidbodyWithoutCollider (warning)
  - DisabledComponent (info)
  - DuplicateComponents (warning)
  - MissingMeshReference (warning)
  - MissingMaterialReference (warning)
- [ ] Document rule configuration — how to enable/disable specific rules via the `rulesJson` parameter
- [ ] Provide examples of common issue patterns and how to fix them (useful for agents that receive validation reports)
- [ ] Add all three methods (`ValidatePrefab`, `ValidateAllPrefabs`, `ValidatePrefabWithRules`) to `CLAUDE.md`

## Verification Steps

- [ ] Test each validation rule individually by crafting a prefab that triggers ONLY that specific rule
- [ ] Verify `ValidateAllPrefabs` handles an empty project (zero prefabs) by returning an empty results array
- [ ] Verify `ValidateAllPrefabs` handles a project with many prefabs (if available) without timeout
- [ ] Test that a properly configured, valid prefab returns zero issues (or only info-level issues like EmptyGameObject for organizational containers)
- [ ] Confirm issue descriptions are clear enough for an agent to understand the problem and take corrective action
- [ ] Test rule disabling — verify that disabled rules produce no issues even when the condition exists

## Code Quality

- [ ] Ensure validation is entirely read-only — no prefab modifications should occur during validation
- [ ] Ensure `PrefabUtility.UnloadPrefabContents()` is always called (try/finally) in `ValidatePrefab`
- [ ] Handle edge cases: prefab with only a Transform component, prefab with deeply nested children (20+ levels), prefab with null components (missing scripts)
- [ ] Verify performance with large prefabs (100+ GameObjects) — validation should complete in under 5 seconds
- [ ] Ensure the `ValidateAllPrefabs` aggregation handles partial failures gracefully (if one prefab fails to load, report the error and continue with the rest)

## Knowledge Transfer

- [ ] Document which rules are most useful for agent-created content — priority ordering for the agent to check
- [ ] Note any false positive patterns that specific rules might trigger (e.g., EmptyGameObject on intentional container objects, DisabledComponent on legitimately disabled components)
- [ ] Evaluate whether the external tool Mooble (Section 10 of brief) offers additional validation rules worth integrating in a future challenge
- [ ] Document how to add a new validation rule — the rule method signature, how to register it, and how to add it to the configurable rule set
