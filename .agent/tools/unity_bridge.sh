#!/bin/bash

# Unity Bridge Script - For Claude Code Integration
# Provides HTTP API access to Unity Editor via Unity Bridge

# --- Output capture ---
# All output is written to this file so agents can read results reliably.
# The Bash tool on Windows swallows echo stdout; this is the workaround.
OUTPUT_FILE="${UNITY_BRIDGE_OUTPUT:-/c/temp/unity_bridge_output.txt}"
mkdir -p "$(dirname "$OUTPUT_FILE")" 2>/dev/null
exec > "$OUTPUT_FILE" 2>&1

UNITY_SERVER_URL="http://localhost:5556"

TIMEOUT=300  # 5 minutes

# Function to make HTTP request with timeout
make_request() {
    local endpoint="$1"
    local method="$2"
    local data="$3"

    if [ "$method" = "POST" ]; then
        curl -s --max-time $TIMEOUT -X POST "$UNITY_SERVER_URL$endpoint" \
             -H "Content-Type: application/json" \
             -d "$data"
    else
        curl -s --max-time $TIMEOUT -X GET "$UNITY_SERVER_URL$endpoint"
    fi
}

# Function to check if Unity server is running
check_server() {
    echo "Checking Unity server status..."
    response=$(make_request "/health" "GET")
    
    # Extract just the JSON response (first line that starts with {)
    clean_response=$(echo "$response" | grep '^{.*}' | head -1 | tr -d '\r')
    
    # Debug output
    # echo "Debug - Full response: $response"
    # echo "Debug - Clean response: $clean_response"
    
    if [ -n "$clean_response" ] && echo "$clean_response" | grep -q '"status":"ok"'; then
        echo "✓ Unity server is running"
        return 0
    else
        echo "✗ Unity server is not responding"
        echo "Please ensure Unity is running with UnityBridgeServer.cs loaded"
        return 1
    fi
}

# Function to trigger compilation
compile() {
    echo "Triggering Unity compilation..."
    response=$(make_request "/compile" "POST" "{}")
    
    # Extract JSON response
    clean_response=$(echo "$response" | grep '^{.*}' | head -1 | tr -d '\r')
    
    if [ -n "$clean_response" ]; then
        status=$(echo "$clean_response" | grep -o '"status":"[^"]*"' | cut -d'"' -f4)
        
        if [ "$status" = "started" ]; then
            echo "✓ Compilation started successfully"
            echo "Waiting for compilation to complete..."
            
            # Poll for completion (up to 2 minutes)
            max_attempts=24  # 24 * 5 seconds = 2 minutes
            attempt=0
            
            while [ $attempt -lt $max_attempts ]; do
                sleep 5
                status_response=$(make_request "/status" "GET")
                status_clean=$(echo "$status_response" | grep '^{.*}' | head -1 | tr -d '\r')
                
                if echo "$status_clean" | grep -q 'compiling.*False\|"isCompiling":false'; then
                    echo "✓ Compilation completed"
                    if command -v jq >/dev/null 2>&1; then
                        echo "$status_clean" | jq '.'
                    else
                        echo "$status_clean"
                    fi
                    return 0
                fi
                
                echo "  Still compiling... (attempt $((attempt + 1))/$max_attempts)"
                attempt=$((attempt + 1))
            done
            
            echo "⚠ Compilation timeout - check Unity Editor for status"
            return 1
        else
            echo "Compilation response:"
            if command -v jq >/dev/null 2>&1; then
                echo "$clean_response" | jq '.'
            else
                echo "$clean_response"
            fi
        fi
    else
        echo "✗ Failed to communicate with Unity server"
        return 1
    fi
}

# Function to get logs
get_logs() {
    echo "Retrieving Unity logs..."
    response=$(make_request "/logs" "GET")
    
    # Extract JSON response
    clean_response=$(echo "$response" | grep '^{.*}' | head -1 | tr -d '\r')
    
    if [ -n "$clean_response" ]; then
        echo "Logs retrieved:"
        if command -v jq >/dev/null 2>&1; then
            echo "$clean_response" | jq '.'
        else
            echo "$clean_response"
        fi
    else
        echo "✗ Failed to retrieve logs"
        return 1
    fi
}

# Function to get status
get_status() {
    echo "Getting Unity status..."
    response=$(make_request "/status" "GET")
    
    # Extract JSON response
    clean_response=$(echo "$response" | grep '^{.*}' | head -1 | tr -d '\r')
    
    if [ -n "$clean_response" ]; then
        echo "Status retrieved:"
        if command -v jq >/dev/null 2>&1; then
            echo "$clean_response" | jq '.'
        else
            echo "$clean_response"
        fi
    else
        echo "✗ Failed to get status"
        return 1
    fi
}

# Function to clear logs
clear_logs() {
    echo "Clearing Unity logs..."
    response=$(make_request "/clear" "POST" "{}")
    
    # Extract JSON response
    clean_response=$(echo "$response" | grep '^{.*}' | head -1 | tr -d '\r')
    
    if [ -n "$clean_response" ]; then
        echo "Logs cleared:"
        if command -v jq >/dev/null 2>&1; then
            echo "$clean_response" | jq '.'
        else
            echo "$clean_response"
        fi
    else
        echo "✗ Failed to clear logs"
        return 1
    fi
}

# Function to control play mode
play_mode() {
    local action="$1"

    if [ -z "$action" ]; then
        # GET - query play mode state
        echo "Querying Play Mode state..."
        response=$(make_request "/playmode" "GET")
        clean_response=$(echo "$response" | grep '^{.*}' | head -1 | tr -d '\r')
        if [ -n "$clean_response" ]; then
            if command -v jq >/dev/null 2>&1; then
                echo "$clean_response" | jq '.'
            else
                echo "$clean_response"
            fi
        else
            echo "✗ Failed to query play mode"
            return 1
        fi
    else
        # POST - enter or exit
        echo "Setting Play Mode: $action..."
        response=$(make_request "/playmode" "POST" "{\"action\":\"$action\"}")
        clean_response=$(echo "$response" | grep '^{.*}' | head -1 | tr -d '\r')

        if [ -n "$clean_response" ]; then
            status=$(echo "$clean_response" | grep -o '"status":"[^"]*"' | cut -d'"' -f4)
            if [ "$status" = "started" ]; then
                echo "✓ Play Mode transition started"

                # Poll until transition completes (up to 30 seconds)
                local target_state
                if [ "$action" = "enter" ]; then
                    target_state="playing"
                else
                    target_state="stopped"
                fi

                max_attempts=30
                attempt=0
                while [ $attempt -lt $max_attempts ]; do
                    sleep 1
                    poll=$(make_request "/playmode" "GET")
                    poll_clean=$(echo "$poll" | grep '^{.*}' | head -1 | tr -d '\r')
                    if echo "$poll_clean" | grep -q "\"state\":\"$target_state\""; then
                        echo "✓ Play Mode is now: $target_state"
                        return 0
                    fi
                    attempt=$((attempt + 1))
                done
                echo "⚠ Play Mode transition timeout"
                return 1
            else
                if command -v jq >/dev/null 2>&1; then
                    echo "$clean_response" | jq '.'
                else
                    echo "$clean_response"
                fi
            fi
        else
            echo "✗ Failed to set play mode"
            return 1
        fi
    fi
}

# Function to capture screenshot
take_screenshot() {
    local path="$1"

    if [ -n "$path" ]; then
        echo "Capturing screenshot to file: $path..."
        response=$(make_request "/screenshot" "POST" "{\"format\":\"file\",\"filePath\":\"$path\"}")
    else
        echo "Capturing screenshot as base64..."
        response=$(make_request "/screenshot" "POST" "{\"format\":\"base64\"}")
    fi

    clean_response=$(echo "$response" | grep '^{.*}' | head -1 | tr -d '\r')

    if [ -n "$clean_response" ]; then
        status=$(echo "$clean_response" | grep -o '"status":"[^"]*"' | cut -d'"' -f4)
        if [ "$status" = "success" ]; then
            echo "✓ Screenshot captured"
            if command -v jq >/dev/null 2>&1; then
                # Show metadata without the base64 blob
                echo "$clean_response" | jq '{status, message, data: {filePath: .data.filePath, width: .data.width, height: .data.height}}'
            else
                # Strip base64 blob to avoid dumping huge output
                echo "$clean_response" | sed 's/"base64":"[^"]*"/"base64":"(omitted)"/g'
            fi
        else
            echo "✗ Screenshot failed"
            if command -v jq >/dev/null 2>&1; then
                echo "$clean_response" | jq '.'
            else
                echo "$clean_response"
            fi
            return 1
        fi
    else
        echo "✗ Failed to capture screenshot"
        return 1
    fi
}

# Function to emulate input
emulate_input() {
    local action="$1"
    shift

    if [ -z "$action" ]; then
        # GET - query input status
        echo "Querying input status..."
        response=$(make_request "/input" "GET")
        clean_response=$(echo "$response" | grep '^{.*}' | head -1 | tr -d '\r')
        if [ -n "$clean_response" ]; then
            if command -v jq >/dev/null 2>&1; then
                echo "$clean_response" | jq '.'
            else
                echo "$clean_response"
            fi
        else
            echo "✗ Failed to query input status"
            return 1
        fi
        return 0
    fi

    local json_data=""

    case "$action" in
        "tap")
            local x="$1" y="$2"
            if [ -z "$x" ] || [ -z "$y" ]; then
                echo "Usage: $0 input tap X Y [duration]"
                return 1
            fi
            local dur="${3:-0.05}"
            json_data="{\"action\":\"tap\",\"x\":$x,\"y\":$y,\"duration\":$dur}"
            ;;
        "hold")
            local x="$1" y="$2"
            if [ -z "$x" ] || [ -z "$y" ]; then
                echo "Usage: $0 input hold X Y [duration]"
                return 1
            fi
            local dur="${3:-1.0}"
            json_data="{\"action\":\"hold\",\"x\":$x,\"y\":$y,\"duration\":$dur}"
            ;;
        "drag")
            local sx="$1" sy="$2" ex="$3" ey="$4"
            if [ -z "$sx" ] || [ -z "$sy" ] || [ -z "$ex" ] || [ -z "$ey" ]; then
                echo "Usage: $0 input drag startX startY endX endY [duration]"
                return 1
            fi
            local dur="${5:-0.3}"
            json_data="{\"action\":\"drag\",\"startX\":$sx,\"startY\":$sy,\"endX\":$ex,\"endY\":$ey,\"duration\":$dur}"
            ;;
        "swipe")
            local sx="$1" sy="$2" ex="$3" ey="$4"
            if [ -z "$sx" ] || [ -z "$sy" ] || [ -z "$ex" ] || [ -z "$ey" ]; then
                echo "Usage: $0 input swipe startX startY endX endY [duration]"
                return 1
            fi
            local dur="${5:-0.15}"
            json_data="{\"action\":\"swipe\",\"startX\":$sx,\"startY\":$sy,\"endX\":$ex,\"endY\":$ey,\"duration\":$dur}"
            ;;
        "pinch")
            local cx="$1" cy="$2" sd="$3" ed="$4"
            if [ -z "$cx" ] || [ -z "$cy" ] || [ -z "$sd" ] || [ -z "$ed" ]; then
                echo "Usage: $0 input pinch centerX centerY startDist endDist [duration]"
                return 1
            fi
            local dur="${5:-0.5}"
            json_data="{\"action\":\"pinch\",\"centerX\":$cx,\"centerY\":$cy,\"startDistance\":$sd,\"endDistance\":$ed,\"duration\":$dur}"
            ;;
        "multi_tap")
            local x="$1" y="$2"
            if [ -z "$x" ] || [ -z "$y" ]; then
                echo "Usage: $0 input multi_tap X Y [count] [interval]"
                return 1
            fi
            local count="${3:-2}"
            local interval="${4:-0.15}"
            json_data="{\"action\":\"multi_tap\",\"x\":$x,\"y\":$y,\"count\":$count,\"interval\":$interval}"
            ;;
        *)
            echo "Unknown input action: $action"
            echo "Available: tap, hold, drag, swipe, pinch, multi_tap"
            return 1
            ;;
    esac

    echo "Sending input: $action..."
    response=$(make_request "/input" "POST" "$json_data")
    clean_response=$(echo "$response" | grep '^{.*}' | head -1 | tr -d '\r')

    if [ -n "$clean_response" ]; then
        status=$(echo "$clean_response" | grep -o '"status":"[^"]*"' | cut -d'"' -f4)
        if [ "$status" = "started" ]; then
            echo "✓ Input queued"
        fi
        if command -v jq >/dev/null 2>&1; then
            echo "$clean_response" | jq '.'
        else
            echo "$clean_response"
        fi
    else
        echo "✗ Failed to send input"
        return 1
    fi
}

# Main script logic
case "$1" in
    "compile")
        check_server && compile
        ;;
    "logs")
        check_server && get_logs
        ;;
    "status")
        check_server && get_status
        ;;
    "clear")
        check_server && clear_logs
        ;;
    "health")
        check_server
        ;;
    "play")
        check_server && play_mode "$2"
        ;;
    "screenshot")
        check_server && take_screenshot "$2"
        ;;
    "input")
        shift
        check_server && emulate_input "$@"
        ;;
    *)
        echo "Unity Bridge Script"
        echo "Usage: $0 {compile|logs|status|clear|health|play|screenshot|input}"
        echo ""
        echo "Commands:"
        echo "  compile              - Trigger Unity compilation and get results"
        echo "  logs                 - Get current Unity console logs"
        echo "  status               - Get Unity server status"
        echo "  clear                - Clear Unity logs"
        echo "  health               - Check if Unity server is running"
        echo "  play [enter|exit]    - Control/query Play Mode"
        echo "  screenshot [path]    - Capture screenshot (file or base64)"
        echo "  input tap X Y [dur]  - Emulate tap at screen coordinates"
        echo "  input hold X Y [dur] - Emulate hold/long press"
        echo "  input drag SX SY EX EY [dur]  - Emulate drag gesture"
        echo "  input swipe SX SY EX EY [dur] - Emulate swipe gesture"
        echo "  input pinch CX CY SD ED [dur] - Emulate pinch gesture"
        echo "  input multi_tap X Y [count] [interval] - Emulate multi-tap"
        echo ""
        echo "Example: $0 play enter"
        echo "Example: $0 screenshot C:/temp/test.png"
        echo "Example: $0 input tap 500 300"
        echo "Example: $0 input multi_tap 500 300 3"
        exit 1
        ;;
esac

echo ""
echo "[unity_bridge] Output written to: $OUTPUT_FILE"