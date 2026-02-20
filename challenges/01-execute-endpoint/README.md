# Challenge 01: Execute Endpoint

## Overview

Add a `/execute` endpoint to the Unity Bridge server that allows agents to invoke any static editor method by fully-qualified name and receive structured JSON results. This is the foundation for ALL subsequent challenges — every introspection and manipulation tool will be exposed as a static method callable through this endpoint.

## Brief Reference

Section 11 (Flow Sequence) — Step 2 "Project introspection — Agent runs inventory tools... helper scripts that return structured JSON." The execute endpoint is the mechanism by which all 23 numbered tools in the brief become callable by the agent.

## Problem Statement

The bridge currently supports a fixed set of commands (health, compile, play, etc.). To build the 23 tools described in the brief, we need a generic mechanism to call arbitrary static C# methods from the agent. Without this, each new tool would require modifying the bridge server's HTTP handler, the bash script, and the protocol documentation — an unsustainable approach for 23+ tools.

The execute endpoint solves this by accepting a method name and optional JSON arguments, invoking the method via reflection, and returning the result as JSON.

## Success Criteria

1. A new `POST /execute` endpoint exists in `UnityBridgeServer.cs`
2. The endpoint accepts JSON body: `{"method": "Namespace.Class.MethodName", "args": [...]}`
3. It resolves the method via reflection across all loaded assemblies
4. It invokes the static method with deserialized arguments
5. It returns the method's return value serialized as JSON: `{"success": true, "result": ...}`
6. On error (method not found, invocation exception), it returns: `{"success": false, "error": "description"}`
7. A new `execute` command exists in `unity_bridge.sh`
8. The command syntax is: `bash .agent/tools/unity_bridge.sh execute Namespace.Class.Method [json_args]`
9. A simple test method `UnityBridge.BridgeTools.Ping()` returns `{"message": "pong", "timestamp": "..."}` and is callable via the endpoint
10. `CLAUDE.md` is updated with the execute command documentation

## Expected Development Work

### New Files

- **`Unity-Bridge/Editor/BridgeTools.cs`** — Static utility class containing the `Ping` test method and serving as the namespace home for all future tool methods. Namespace: `UnityBridge`. Must include:
  - `public static string Ping()` — returns JSON string `{"message": "pong", "timestamp": "<ISO8601>"}`
  - `public static string Add(int a, int b)` — returns JSON string `{"result": <sum>}` (for testing argument passing)

- **`Unity-Bridge/Editor/MethodExecutor.cs`** — Reflection-based method resolution and invocation engine. Namespace: `UnityBridge`. Must handle:
  - Method lookup across all loaded assemblies via `AppDomain.CurrentDomain.GetAssemblies()`
  - Parsing the method string as `Namespace.Class.Method` — split on last `.` to separate type name from method name
  - Parameter type conversion from JSON string arguments to C# types
  - Void vs non-void return value methods
  - Exception wrapping — catch `TargetInvocationException` and return the inner exception message

### Modified Files

- **`Unity-Bridge/Editor/UnityBridgeServer.cs`** — Add `/execute` route handler in the HTTP request processing method. The handler must:
  - Read the POST body as a JSON string
  - Parse `method` and `args` fields
  - Delegate to `MethodExecutor.Execute(method, args)`
  - Return the JSON response with appropriate HTTP status codes (200 for success, 400 for bad request, 500 for invocation errors)
  - Execute on Unity's main thread using the existing `ExecuteOnMainThread` pattern

- **`.agent/tools/unity_bridge.sh`** — Add `execute` command case that:
  - Takes the first argument as the method name
  - Takes an optional second argument as JSON args array
  - POSTs to `http://localhost:5556/execute` with body `{"method": "<arg1>", "args": <arg2 or []>}`

- **`CLAUDE.md`** — Add `execute` to the Available Commands table with syntax and description. Add an "Execute Endpoint" section explaining how all future tools are exposed through this mechanism.

### Key Implementation Details

- Use `System.Reflection` to find methods: iterate `AppDomain.CurrentDomain.GetAssemblies()`, search for the type by full name, then find the static method by name
- The method string format is `Namespace.Class.Method` — split on the last `.` to get the type full name and the method name (e.g., `UnityBridge.BridgeTools.Ping` splits into type `UnityBridge.BridgeTools` and method `Ping`)
- For JSON serialization, use Unity's `JsonUtility` for simple objects or a lightweight JSON approach. Note that `JsonUtility` cannot serialize dictionaries or top-level primitives — consider using `EditorJsonUtility` or building JSON strings manually for the response wrapper
- Execute on Unity's main thread — reflection-invoked methods may call Unity APIs that require the main thread
- Return value must be valid JSON — if the method returns a string that is already JSON, embed it directly in the result field; if void, return `{"success": true, "result": null}`
- Methods that return strings are assumed to return JSON strings — embed them as-is in the result field (do not double-serialize)

## Testing Protocol

1. `bash .agent/tools/unity_bridge.sh health` — Read `C:/temp/unity_bridge_output.txt`, confirm server is running
2. Create/edit the new C# files: `BridgeTools.cs`, `MethodExecutor.cs`, and modify `UnityBridgeServer.cs`
3. `bash .agent/tools/unity_bridge.sh compile` — Read output, confirm compilation completed successfully with no errors
4. `bash .agent/tools/unity_bridge.sh execute UnityBridge.BridgeTools.Ping` — Read output
5. Verify response contains `{"success": true, "result": {"message": "pong", ...}}`
6. Test error case: `bash .agent/tools/unity_bridge.sh execute NonExistent.Class.Method` — Read output, verify response contains `{"success": false, "error": "..."}`
7. Test with arguments: `bash .agent/tools/unity_bridge.sh execute UnityBridge.BridgeTools.Add '[1, 2]'` — Read output, verify result contains 3
8. Test with wrong argument count: `bash .agent/tools/unity_bridge.sh execute UnityBridge.BridgeTools.Ping '[1]'` — Read output, verify error response

## Dependencies

None — this is the foundation challenge.
