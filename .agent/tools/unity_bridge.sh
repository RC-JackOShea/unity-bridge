#!/bin/bash

# Unity Bridge Script - For Claude Code Integration
# Provides HTTP API access to Unity Editor via Unity Bridge

# Detect if running in WSL
WSL_DETECTED=false
if grep -qi microsoft /proc/version; then
    WSL_DETECTED=true
    echo "WSL detected - using Windows curl for Unity connection"
fi

UNITY_SERVER_URL="http://localhost:5556"

TIMEOUT=300  # 5 minutes

# Function to make HTTP request with timeout
make_request() {
    local endpoint="$1"
    local method="$2"
    local data="$3"
    
    if [ "$WSL_DETECTED" = true ]; then
        # Use Windows curl from WSL - use simpler command format
        if [ "$method" = "POST" ]; then
            cmd.exe /c "curl -s -X POST $UNITY_SERVER_URL$endpoint -H \"Content-Type: application/json\" -d \"$data\""
        else
            cmd.exe /c "curl -s $UNITY_SERVER_URL$endpoint"
        fi
    else
        # Use native curl
        if [ "$method" = "POST" ]; then
            curl -s --max-time $TIMEOUT -X POST "$UNITY_SERVER_URL$endpoint" \
                 -H "Content-Type: application/json" \
                 -d "$data"
        else
            curl -s --max-time $TIMEOUT -X GET "$UNITY_SERVER_URL$endpoint"
        fi
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
    *)
        echo "Unity Bridge Script"
        echo "Usage: $0 {compile|logs|status|clear|health}"
        echo ""
        echo "Commands:"
        echo "  compile  - Trigger Unity compilation and get results"
        echo "  logs     - Get current Unity console logs"
        echo "  status   - Get Unity server status"
        echo "  clear    - Clear Unity logs"
        echo "  health   - Check if Unity server is running"
        echo ""
        echo "Example: $0 compile"
        exit 1
        ;;
esac