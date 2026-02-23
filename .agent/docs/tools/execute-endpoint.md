# Execute Endpoint

The `/execute` endpoint invokes any static C# method by fully-qualified name via reflection. This is the foundation for all tool methods — new tools are added as static methods in any class, callable without modifying the server or script.

## Usage

```bash
bash .agent/tools/unity_bridge.sh execute <Namespace.Class.Method> [argsJson]
```

## JSON Request/Response Format

**Request** (POST `/execute`):
```json
{"method": "UnityBridge.BridgeTools.Ping", "args": []}
{"method": "UnityBridge.BridgeTools.Add", "args": ["1", "2"]}
```

The `args` array contains string representations of each argument. The executor converts them to the method's parameter types (int, float, string, bool, etc.).

**Response** (returned directly, not wrapped in ApiResponse):
```json
{"success": true, "result": {"message": "pong", "timestamp": "2024-01-01T00:00:00Z"}}
{"success": false, "error": "Type not found: Foo.Bar"}
```

Methods returning `string` are assumed to return valid JSON and are embedded directly in the `result` field. Methods returning `void` produce `"result": null`.

## Examples

```bash
# Connectivity test
bash .agent/tools/unity_bridge.sh execute UnityBridge.BridgeTools.Ping

# Pass arguments
bash .agent/tools/unity_bridge.sh execute UnityBridge.BridgeTools.Add '[1,2]'

# Error case — nonexistent method
bash .agent/tools/unity_bridge.sh execute Fake.Class.Method
```

## Security Note

The execute endpoint can invoke **any** static method in any loaded assembly. This is by design for agent tooling but means the bridge should not be exposed to untrusted networks. Keep it on localhost only.
