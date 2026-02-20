# Challenge-Solving Meta Prompt

> **Usage:** Copy the prompt below into Claude Code, replacing every `XX` with the two-digit challenge number (e.g., `01`, `14`, `28`) and `CHALLENGE-NAME` with the folder name suffix (e.g., `execute-endpoint`, `ui-toolkit-support`). The challenge folder names follow the pattern `XX-CHALLENGE-NAME`.

---

## Prompt (copy from here)

```
You are solving Challenge XX of the Unity Bridge Lights-Out series. The challenge folder is `challenges/XX-CHALLENGE-NAME/`. Your mission is to fully implement every requirement, pass every success criterion, complete the outro checklist, and produce a clean git history — all using the full Claude Code feature set: sub-agents, teams, task tracking, plan mode, parallel tool calls, and git worktrees where appropriate.

IMPORTANT: All Unity interactions MUST go through the bridge script using the mandatory two-step pattern:
1. Run: `bash .agent/tools/unity_bridge.sh <command> [args]`
2. Read: Use the Read tool on `C:/temp/unity_bridge_output.txt`
Never skip step 2. Never use raw curl to localhost:5556.

---

## Phase 1: Reconnaissance

Launch Explore agents IN PARALLEL to investigate:

1. **Challenge spec** — Read `challenges/XX-CHALLENGE-NAME/README.md` thoroughly. Extract: Overview, Success Criteria (numbered list), Expected Development Work (new files, modified files, key implementation details), Testing Protocol, and Dependencies.

2. **Outro checklist** — Read `challenges/XX-CHALLENGE-NAME/outro.md`. Extract every checkbox item — these are additional deliverables beyond the success criteria.

3. **Dependency challenges** — Check the Dependencies section of the README. If this challenge depends on prior challenges, read those challenge READMEs to understand what APIs/patterns already exist that this challenge builds on. Also inspect the actual implementation files from those dependencies to understand existing code patterns.

4. **Existing codebase** — Examine the files listed in "Expected Development Work" that already exist (files to be modified). Understand their current structure, patterns, and conventions. Also examine `CLAUDE.md` and `.agent/tools/unity_bridge.sh` for current state.

5. **Project patterns** — Look at `Unity-Bridge/Editor/` for existing code conventions: namespacing, JSON serialization approach, error handling patterns, main-thread execution patterns.

After all Explore agents return, synthesize findings into a clear picture of what exists, what needs to be created, and what needs to be modified.

---

## Phase 2: Architecture (Plan Mode)

Enter plan mode. Design the implementation approach:

1. **File inventory** — List every file that will be created or modified, with a one-line description of changes.

2. **Task decomposition** — Break the work into discrete, atomic tasks. Each task should correspond to one logical unit of work that gets its own commit. Examples:
   - "Create BridgeTools.cs with Ping and Add methods"
   - "Add /execute route handler to UnityBridgeServer.cs"
   - "Add execute command to unity_bridge.sh"
   - "Update CLAUDE.md with execute command documentation"

3. **Parallelization assessment** — Determine if tasks can be parallelized:
   - **3+ independent new files** → Use teams with git worktrees (each agent works in its own worktree to avoid file conflicts)
   - **1-2 files or interdependent changes** → Work sequentially in the main worktree
   - Document which files can be written concurrently and which have ordering dependencies

4. **Git strategy** — Plan the branch and commit sequence:
   - Branch name: `challenge/XX-CHALLENGE-NAME`
   - List planned commits in order with message previews
   - If using worktrees, plan the worktree creation and reconciliation steps

5. **Verification plan** — Map each success criterion to a specific test command and expected output. Map each outro checkbox to a verification action.

Exit plan mode for approval.

---

## Phase 3: Branch & Setup

After plan approval, set up the working environment:

### Git Branch
```bash
git checkout -b challenge/XX-CHALLENGE-NAME master
```

### Task Tracking
Create tasks using TaskCreate — one task per logical work item from the plan. Include:
- Clear subject in imperative form (e.g., "Create MethodExecutor.cs")
- Description with acceptance criteria
- activeForm in present continuous (e.g., "Creating MethodExecutor.cs")

Set up task dependencies with TaskUpdate where ordering matters (e.g., "Modify UnityBridgeServer.cs" is blocked by "Create MethodExecutor.cs" if the server imports it).

### Team & Worktrees (if parallelizable)
If the plan calls for parallel work:

1. Create the team:
   ```
   TeamCreate with descriptive name like "challenge-XX"
   ```

2. Create git worktrees for each parallel agent:
   ```bash
   git worktree add ../wt-agent-alpha challenge/XX-CHALLENGE-NAME
   git worktree add ../wt-agent-beta challenge/XX-CHALLENGE-NAME
   ```

3. Spawn agents via Task tool with `team_name` parameter, instructing each agent to work in its assigned worktree directory.

4. Assign tasks to agents via TaskUpdate with `owner`.

If NOT parallelizable, skip team/worktree creation and work sequentially.

---

## Phase 4: Implementation

Execute the plan. For each task:

1. **Claim the task** — `TaskUpdate` status to `in_progress`
2. **Write the code** — Create or modify the file(s)
3. **Compile immediately** — After every C# file change:
   ```bash
   bash .agent/tools/unity_bridge.sh compile
   ```
   Then read `C:/temp/unity_bridge_output.txt`. Do NOT proceed until compilation succeeds with no errors.
4. **Commit the work** — Each logical unit gets its own commit:
   ```bash
   git add <specific-files>
   git commit -m "$(cat <<'EOF'
   challenge-XX: <description of what this commit does>

   Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>
   EOF
   )"
   ```
5. **Mark task complete** — `TaskUpdate` status to `completed`
6. **Move to next task** — Check `TaskList` for next available work

### Commit Convention
- Format: `challenge-XX: <lowercase description>`
- Examples:
  - `challenge-01: add BridgeTools.cs with Ping and Add methods`
  - `challenge-01: add MethodExecutor.cs with reflection-based invocation`
  - `challenge-01: add /execute endpoint to UnityBridgeServer.cs`
  - `challenge-01: add execute command to unity_bridge.sh`
  - `challenge-01: update CLAUDE.md with execute command docs`

### Compilation Rules (Mandatory)
You MUST compile after:
- Creating any `.cs` file
- Modifying any `.cs` file
- Deleting any `.cs` file
- Modifying any `.asmdef` or `.asmref` file
- Before entering Play Mode

An unnecessary compile costs seconds. A skipped compile wastes entire debugging sessions.

### If Using Parallel Agents
- Each agent works ONLY in its assigned worktree directory
- Each agent commits independently in its worktree
- Agents must not modify the same file — the plan must ensure file-disjoint task assignments
- After all agents complete, the lead agent reconciles:
  1. Return to main worktree
  2. Pull commits from each worktree (they share the branch)
  3. Resolve any conflicts
  4. Verify the combined state compiles

---

## Phase 5: Verification

Systematically verify EVERY success criterion from the README.

### Pre-verification
```bash
bash .agent/tools/unity_bridge.sh compile
```
Read output. Confirm clean compilation.

### Test Each Criterion
For each numbered success criterion in the README:

1. State the criterion number and text
2. Execute the specific test (bridge command, code inspection, etc.)
3. Read the output
4. Record: PASS or FAIL with evidence

If using Play Mode for testing:
```bash
bash .agent/tools/unity_bridge.sh compile
bash .agent/tools/unity_bridge.sh play enter
# Read output, confirm play mode entered
bash .agent/tools/unity_bridge.sh clear
# Run test commands...
bash .agent/tools/unity_bridge.sh play exit
# Read output
```

### Handle Failures
If any criterion fails:
1. Diagnose the root cause
2. Fix the code
3. Compile (mandatory after code change)
4. Commit the fix: `challenge-XX: fix <what was wrong>`
5. Re-verify the failing criterion
6. Re-verify any criteria that might be affected by the fix

### Verification Commit
After all criteria pass:
```bash
git commit --allow-empty -m "$(cat <<'EOF'
challenge-XX: all success criteria verified

Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>
EOF
)"
```

---

## Phase 6: Outro, Delivery & Git Finalization

### Complete the Outro Checklist
Read `challenges/XX-CHALLENGE-NAME/outro.md`. Address every checkbox:

- **Documentation updates** — Make the specified changes to CLAUDE.md, code comments, etc.
- **Verification steps** — Run each verification command, confirm results
- **Code quality items** — Check each quality criterion, fix if needed
- **Knowledge transfer items** — Add required comments, documentation

Compile after any code changes. Commit documentation and quality updates:
```bash
git commit -m "$(cat <<'EOF'
challenge-XX: complete outro checklist items

Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>
EOF
)"
```

### Clean Up Worktrees (if used)
```bash
git worktree remove ../wt-agent-alpha
git worktree remove ../wt-agent-beta
```

### Clean Up Team (if used)
Send shutdown requests to all teammates, then delete the team.

### Delivery Report
Produce a structured summary:

```
## Challenge XX: CHALLENGE-NAME — Delivery Report

### Files Changed
- `path/to/file.cs` — Created / Modified (description)
- ...

### Commits
<output of: git log --oneline master..challenge/XX-CHALLENGE-NAME>

### Success Criteria Results
1. [PASS] <criterion text>
2. [PASS] <criterion text>
...

### Outro Checklist
- [x] <item>
- [x] <item>
...

### Notes
<any observations, warnings, or recommendations for the next challenge>
```

Run `git log --oneline master..challenge/XX-CHALLENGE-NAME` to populate the commits section.

### Final State
The branch `challenge/XX-CHALLENGE-NAME` is ready for review and merge to master. Do NOT merge or push unless explicitly asked.

---

## Hard Rules (Never Violate)

1. **Bridge protocol** — Always use `bash .agent/tools/unity_bridge.sh`. Always read `C:/temp/unity_bridge_output.txt` after every invocation. Never use raw curl.
2. **Compile after every code change** — No exceptions. Always read the output.
3. **Atomic commits** — One logical unit per commit. Never bundle unrelated changes.
4. **Commit message format** — `challenge-XX: lowercase description` with co-author trailer.
5. **No force pushes** — Never `git push --force`. Never `git reset --hard` on shared branches.
6. **No amending** — Never `git commit --amend` on commits that are already part of history.
7. **Task tracking** — Create tasks at the start, update status as you work, check the list between tasks.
8. **Verify everything** — Every success criterion tested individually. Every outro checkbox addressed.
9. **Read before edit** — Never modify a file you haven't read first in this session.
10. **Plan before implement** — Always use plan mode for the architecture phase. Get approval before writing code.
```

---

## Quick Reference

| Placeholder | Replace With | Example |
|---|---|---|
| `XX` | Two-digit challenge number | `01`, `14`, `28` |
| `CHALLENGE-NAME` | Folder name suffix (after the number-dash) | `execute-endpoint`, `ui-toolkit-support` |

The challenge folder pattern is: `challenges/XX-CHALLENGE-NAME/`

**Example for Challenge 01:**
- Replace `XX` with `01`
- Replace `CHALLENGE-NAME` with `execute-endpoint`
- Branch becomes `challenge/01-execute-endpoint`
- Commits become `challenge-01: ...`
- Files at `challenges/01-execute-endpoint/README.md` and `outro.md`
