# Unity Bridge Command

## Overview
The Unity Bridge is a shell script tool that enables communication between external systems and Unity. This command instructs the agent on how to discover, validate, and use the Unity Bridge tool.

## Tool Discovery Protocol

### Context
The agent must first assess it's context to check if the location has been found previously

### Required Locations
The agent must check for the Unity Bridge tool in the following locations **in order**:

1. **Root Directory**: `<project_root>/.claude/tools/unity_bridge.sh`
2. **WSL User Directory**: `~/.claude/tools/unity_bridge.sh`

Note: Once the tool has been located a first time, the location MUST be saved / cached to context for future use.

## Discovery Process
When the Unity Bridge command is invoked, the agent must:

1. **Run Tool Discovery Protocol**: Utilise the protocol to find the appropriate file
3. **Validation**: Verify the found file is executable and accessible
4. **Error Handling**: If neither location contains the tool, raise an error

## Error Handling

### Tool Not Found
If the Unity Bridge tool is not found in either location, the agent must:

```
❌ ERROR: Unity Bridge tool not found
   
   Expected locations:
   • <project_root>/.claude/tools/unity_bridge.sh
   • ~/.claude/tools/unity_bridge.sh
   
   Please ensure the unity_bridge.sh script is available in one of these locations before proceeding.
```

### Tool Found Acknowledgment
When the tool is successfully located, the agent must acknowledge:

```
✅ Unity Bridge tool located at: <discovered_path>
   Ready to execute Unity Bridge commands.
```

## Usage Instructions

### Tool Execution
Once the Unity Bridge tool is discovered and validated:

1. **Always use the discovered script path** for Unity Bridge operations
2. **Pass through all command arguments** to the script
3. **Capture and relay output** from the script execution
4. **Handle script errors** appropriately

### Command Format
```bash
<discovered_path>/unity_bridge.sh [arguments]
```

## Implementation Guidelines

### For Claude and Other Agents
- **Mandatory Discovery**: Always perform tool discovery before first use
- **Cache Location**: Remember the discovered path for the session
- **Re-validate**: Check tool existence if commands start failing
- **User Feedback**: Provide clear status messages during discovery

### Example Discovery Code Pattern
```bash
# Check project root first
if [ -f ".claude/tools/unity_bridge.sh" ]; then
    UNITY_BRIDGE_PATH=".claude/tools/unity_bridge.sh"
elif [ -f "$HOME/.claude/tools/unity_bridge.sh" ]; then
    UNITY_BRIDGE_PATH="$HOME/.claude/tools/unity_bridge.sh"
else
    echo "❌ ERROR: Unity Bridge tool not found in expected locations"
    exit 1
fi

echo "✅ Unity Bridge tool located at: $UNITY_BRIDGE_PATH"
```

## Security Considerations
- Verify script permissions before execution
- Validate script integrity if required
- Use absolute paths when possible
- Handle potential permission issues gracefully

## Notes
- The tool discovery must be performed each time the Unity Bridge command is invoked
- The agent should not assume the tool location remains constant
- Error messages should be clear and actionable for the user
