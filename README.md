# Unity Bridge

Unity Bridge provides HTTP API access to Unity Editor for external development tools. Trigger Unity compilation, retrieve console logs, and get real-time status from WSL, Claude Code, or any external system.

## Quick Start

### 1. Install Unity Package
Drag the `Unity-Bridge/` folder into your Unity project's Assets directory.

### 2. Verify Installation
- Unity will automatically start the HTTP server on port 5556
- Check Unity Console for: `[UnityBridge] Started on http://localhost:5556`
- Menu items appear under `Tools > Unity Bridge`

### 3. Test External Access
From WSL or any terminal:
```bash
cd Scripts/
./unity_bridge.sh health
```

## Usage

### Command Line Interface
The `Scripts/unity_bridge.sh` script provides these commands:

```bash
# Check if Unity Bridge is running
./unity_bridge.sh health

# Trigger Unity compilation and wait for results
./unity_bridge.sh compile

# Get current Unity console logs
./unity_bridge.sh logs

# Get Unity status and compilation state
./unity_bridge.sh status

# Clear Unity logs
./unity_bridge.sh clear
```

### WSL Compatibility
Unity Bridge automatically detects WSL environments and uses Windows curl for cross-system communication. No additional configuration needed.

### Claude Code Integration
Use the shell script directly in Claude Code workflows:
```bash
# Check Unity status before making changes
./Scripts/unity_bridge.sh status

# Make code changes, then compile and get results
./Scripts/unity_bridge.sh compile

# Review any compilation errors or warnings
./Scripts/unity_bridge.sh logs
```

## API Endpoints

Unity Bridge exposes these HTTP endpoints on `localhost:5556`:

- `GET /health` - Server health check
- `POST /compile` - Trigger Unity compilation
- `GET /logs` - Retrieve Unity console logs  
- `GET /status` - Get compilation status and server state
- `POST /clear` - Clear Unity logs

## Unity Editor Integration

### Menu Items
- `Tools > Unity Bridge > Start Server` - Manually start HTTP server
- `Tools > Unity Bridge > Stop Server` - Stop HTTP server
- `Tools > Unity Bridge > Test Compile` - Trigger test compilation
- `Tools > Unity Bridge > Clear Logs` - Clear console logs

### Background Processing
Unity Bridge works even when Unity Editor is minimized, using background threading for HTTP requests while maintaining Unity API compatibility.

## Project Structure

```
unity-log-system/
├── Unity-Bridge/           # Unity package (drag into Unity)
│   └── Assets/Unity-Bridge/
│       ├── Editor/
│       │   ├── UnityBridgeServer.cs      # Main HTTP server
│       │   └── UnityBridge.Editor.asmdef # Assembly definition
│       └── package.json                  # Package metadata
├── Scripts/                # External tools
│   └── unity_bridge.sh    # Command line interface
└── README.md              # This file
```

## Requirements

- Unity 2020.3 or later
- Windows (for Unity Editor)
- WSL/Linux (optional, for external tools)
- curl (automatically detected)

## Troubleshooting

### Unity Server Not Responding
1. Check Unity Console for "[UnityBridge] Started on..." message
2. Try restarting via `Tools > Unity Bridge > Start Server`
3. Ensure no firewall blocking port 5556

### WSL Connection Issues
- Unity Bridge automatically handles WSL-to-Windows networking
- If issues persist, run Unity as Administrator for full network access

### Compilation Not Triggering
- Unity must be the active application for compilation events
- Check Unity Console for compilation messages
- Use `./unity_bridge.sh status` to monitor progress

## Future Expansion

Unity Bridge is designed as an extensible platform. Future features may include:
- Asset management operations
- Scene manipulation
- Build pipeline integration
- Custom Unity tool execution