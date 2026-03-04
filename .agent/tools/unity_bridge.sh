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

# Helper: extract compilation errors from a /status JSON response.
# Prints each error line and returns 0 if errors found, 1 if clean.
check_compile_errors() {
    local status_json="$1"
    local errors=""

    if command -v jq >/dev/null 2>&1; then
        errors=$(echo "$status_json" | jq -r '
            .data.compilationHistory // [] | .[-1:] | .[].errors // [] | .[]' 2>/dev/null)
    else
        # Fallback: grep for error strings inside compilationHistory
        errors=$(echo "$status_json" | grep -oP '"errors":\["[^]]*"\]' | grep -oP '(?<=")[^"]+error[^"]+(?=")' )
    fi

    if [ -n "$errors" ]; then
        echo "✗ Compilation errors detected:"
        echo "$errors" | while IFS= read -r line; do
            [ -n "$line" ] && echo "  ERROR: $line"
        done
        return 0  # errors found
    fi
    return 1  # no errors
}

# Helper: extract setup.scene from a test JSON file.
# Returns the scene name or empty string if not declared.
get_test_scene() {
    local test_file="$1"
    if command -v jq >/dev/null 2>&1; then
        jq -r '.setup.scene // empty' "$test_file" 2>/dev/null
    else
        grep -o '"scene"[[:space:]]*:[[:space:]]*"[^"]*"' "$test_file" | head -1 | cut -d'"' -f4
    fi
}

# Helper: load a scene by name during play mode via SceneInventoryTool.
load_scene() {
    local scene_name="$1"
    if [ -n "$scene_name" ]; then
        echo "  Loading scene: $scene_name..."
        curl -s --max-time 10 -X POST "$UNITY_SERVER_URL/execute" \
            -H "Content-Type: application/json" \
            -d "{\"method\":\"UnityBridge.SceneInventoryTool.LoadScene\",\"args\":[\"$scene_name\"]}" >/dev/null
        sleep 1
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
            max_attempts=120  # 120 * 1 second = 2 minutes
            attempt=0
            last_status=""

            while [ $attempt -lt $max_attempts ]; do
                sleep 1
                status_response=$(make_request "/status" "GET")
                last_status=$(echo "$status_response" | grep '^{.*}' | head -1 | tr -d '\r')

                if echo "$last_status" | grep -q 'compiling.*False\|"isCompiling":false'; then
                    # Compilation finished — now check for errors
                    if check_compile_errors "$last_status"; then
                        echo ""
                        echo "$last_status"
                        return 1
                    fi

                    echo "✓ Compilation completed (no errors)"
                    echo "$last_status"
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
        # Pre-flight: if entering play mode, check for compile errors first
        if [ "$action" = "enter" ]; then
            preflight=$(make_request "/status" "GET")
            preflight_clean=$(echo "$preflight" | grep '^{.*}' | head -1 | tr -d '\r')
            if check_compile_errors "$preflight_clean"; then
                echo "✗ Refusing to enter Play Mode — unresolved compilation errors."
                echo "  Fix the errors above and run 'compile' before entering Play Mode."
                return 1
            fi
        fi

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

# Function to execute a static method via reflection
execute_method() {
    local method_path="$1"
    local args_json="$2"

    if [ -z "$method_path" ]; then
        echo "Usage: $0 execute <Namespace.Class.Method> [argsJson]"
        echo "Example: $0 execute UnityBridge.BridgeTools.Ping"
        echo "Example: $0 execute UnityBridge.BridgeTools.Add '[1,2]'"
        return 1
    fi

    echo "Executing: $method_path..."

    # Build JSON body - args are passed as string array elements
    if [ -z "$args_json" ] || [ "$args_json" = "[]" ]; then
        json_body="{\"method\":\"$method_path\",\"args\":[]}"
    else
        # The args come as a JSON array string like '[1,2]' or '["hello"]'
        # We need to convert to a string array for the ExecuteRequest DTO
        # Strip outer brackets to peek at the first element
        inner=$(echo "$args_json" | sed 's/^ *\[//;s/\] *$//')
        first_char=$(echo "$inner" | sed 's/^ *//' | cut -c1)

        if [ "$first_char" = '"' ]; then
            # Args are already JSON strings — pass through directly
            # This preserves internal commas/brackets in string values
            json_body="{\"method\":\"$method_path\",\"args\":$args_json}"
        else
            # Numeric/boolean args — wrap each as a string
            IFS=',' read -ra TOKENS <<< "$inner"
            args_str=""
            for token in "${TOKENS[@]}"; do
                trimmed=$(echo "$token" | sed 's/^ *//;s/ *$//')
                if [ -n "$args_str" ]; then
                    args_str="$args_str,"
                fi
                args_str="$args_str\"$trimmed\""
            done
            json_body="{\"method\":\"$method_path\",\"args\":[$args_str]}"
        fi
    fi

    response=$(make_request "/execute" "POST" "$json_body")
    clean_response=$(echo "$response" | grep '^{.*}' | head -1 | tr -d '\r')

    if [ -n "$clean_response" ]; then
        if echo "$clean_response" | grep -q '"success":true'; then
            echo "✓ Execute succeeded"
        else
            echo "✗ Execute failed"
        fi
        if command -v jq >/dev/null 2>&1; then
            echo "$clean_response" | jq '.'
        else
            echo "$clean_response"
        fi
    else
        echo "✗ Failed to execute method"
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
    "execute")
        check_server && execute_method "$2" "$3"
        ;;
    "integration_test")
        # Run a single integration test file (async — visually observable in Unity)
        # Orchestrates: compile → play enter → start async test → poll → play exit
        test_file="$2"
        if [ -z "$test_file" ]; then
            echo "Usage: $0 integration_test <test_file.json>"
            exit 1
        fi
        check_server || exit 1
        compile || exit 1
        play_mode "enter" || exit 1

        # Focus Unity so the player loop runs at full speed
        powershell -Command "(New-Object -ComObject WScript.Shell).AppActivate('Unity')" 2>/dev/null

        # Start test asynchronously (one action per frame for visual feedback)
        execute_method "UnityBridge.IntegrationTestRunner.RunTestAsync" "[\"$test_file\"]"

        # Poll for completion
        echo ""
        echo "Waiting for test to complete (watch Unity!)..."
        test_done=false
        poll_body='{"method":"UnityBridge.IntegrationTestRunner.GetTestStatus","args":[]}'
        for attempt in $(seq 1 120); do
            sleep 1
            poll_response=$(curl -s --max-time 10 -X POST "$UNITY_SERVER_URL/execute" \
                -H "Content-Type: application/json" -d "$poll_body")
            poll_json=$(echo "$poll_response" | grep '^{.*}' | head -1 | tr -d '\r')

            if echo "$poll_json" | grep -q '"status":"completed"'; then
                echo "✓ Test completed"
                echo "$poll_json" | jq '.result' 2>/dev/null || echo "$poll_json"
                test_done=true
                break
            fi

            if ! echo "$poll_json" | grep -q '"status":"running"'; then
                echo "✗ Test ended unexpectedly"
                echo "$poll_json"
                test_done=true
                break
            fi

            # Show progress
            progress=$(echo "$poll_json" | grep -o '"progress":[0-9]*' | head -1 | cut -d: -f2)
            total=$(echo "$poll_json" | grep -o '"total":[0-9]*' | head -1 | cut -d: -f2)
            if [ -n "$progress" ] && [ -n "$total" ]; then
                echo "  Progress: $progress/$total actions"
            fi
        done

        if [ "$test_done" = false ]; then
            echo "✗ Test timed out after 120 seconds"
        fi

        play_mode "exit"
        ;;
    "integration_suite")
        # Run all integration tests in a directory using the coroutine runner
        # Each test runs via RunTestAsync for visual execution in the Game view
        test_dir="$2"
        if [ -z "$test_dir" ]; then
            echo "Usage: $0 integration_suite <test_directory>"
            exit 1
        fi
        check_server || exit 1
        compile || exit 1
        play_mode "enter" || exit 1
        powershell -Command "(New-Object -ComObject WScript.Shell).AppActivate('Unity')" 2>/dev/null

        # Collect test files (sorted alphabetically)
        mapfile -t test_files < <(ls "$test_dir"/*.json 2>/dev/null | sort)
        total_files=${#test_files[@]}

        if [ $total_files -eq 0 ]; then
            echo "✗ No .json test files found in: $test_dir"
            play_mode "exit"
            exit 1
        fi

        echo ""
        echo "========================================="
        echo "  Integration Suite: $total_files tests"
        echo "========================================="

        suite_passed=0
        suite_failed=0
        poll_body='{"method":"UnityBridge.IntegrationTestRunner.GetTestStatus","args":[]}'

        for i in "${!test_files[@]}"; do
            test_file="${test_files[$i]}"
            test_num=$((i + 1))
            test_basename=$(basename "$test_file")

            echo ""
            echo "[$test_num/$total_files] $test_basename"
            echo "-----------------------------------------"

            # Reload scene between tests to reset state (skip first — scene is fresh)
            if [ $i -gt 0 ]; then
                load_scene "$(get_test_scene "$test_file")"
            fi

            # Convert to Unity-relative path (Assets/...) for the C# side
            unity_path=$(echo "$test_file" | sed 's|.*\(Assets/\)|\1|')

            # Start async test
            start_body="{\"method\":\"UnityBridge.IntegrationTestRunner.RunTestAsync\",\"args\":[\"$unity_path\"]}"
            start_response=$(curl -s --max-time 10 -X POST "$UNITY_SERVER_URL/execute" \
                -H "Content-Type: application/json" -d "$start_body")
            start_json=$(echo "$start_response" | grep '^{.*}' | head -1 | tr -d '\r')

            if ! echo "$start_json" | grep -q '"status":"started"'; then
                echo "  ✗ Failed to start test"
                echo "  $start_json"
                suite_failed=$((suite_failed + 1))
                continue
            fi

            test_name=$(echo "$start_json" | grep -o '"name":"[^"]*"' | head -1 | cut -d'"' -f4)
            action_count=$(echo "$start_json" | grep -o '"actionCount":[0-9]*' | cut -d: -f2)
            echo "  $test_name ($action_count actions)"

            # Poll for completion
            test_done=false
            for attempt in $(seq 1 120); do
                sleep 1
                poll_response=$(curl -s --max-time 10 -X POST "$UNITY_SERVER_URL/execute" \
                    -H "Content-Type: application/json" -d "$poll_body")
                poll_json=$(echo "$poll_response" | grep '^{.*}' | head -1 | tr -d '\r')

                if echo "$poll_json" | grep -q '"status":"completed"'; then
                    overall=$(echo "$poll_json" | grep -o '"overallResult":"[^"]*"' | cut -d'"' -f4)
                    duration=$(echo "$poll_json" | grep -o '"duration":[0-9.]*' | head -1 | cut -d: -f2)

                    if [ "$overall" = "Passed" ]; then
                        echo "  ✓ PASSED (${duration}s)"
                        suite_passed=$((suite_passed + 1))
                    else
                        echo "  ✗ FAILED (${duration}s)"
                        # Show errors if jq available
                        if command -v jq >/dev/null 2>&1; then
                            echo "$poll_json" | jq -r '.result.errors[]? // empty' 2>/dev/null | while IFS= read -r err; do
                                echo "    - $err"
                            done
                        fi
                        suite_failed=$((suite_failed + 1))
                    fi
                    test_done=true
                    break
                fi

                if ! echo "$poll_json" | grep -q '"status":"running"'; then
                    echo "  ✗ Test ended unexpectedly"
                    suite_failed=$((suite_failed + 1))
                    test_done=true
                    break
                fi

                # Progress
                progress=$(echo "$poll_json" | grep -o '"progress":[0-9]*' | head -1 | cut -d: -f2)
                total=$(echo "$poll_json" | grep -o '"total":[0-9]*' | head -1 | cut -d: -f2)
                [ -n "$progress" ] && [ -n "$total" ] && echo "  Progress: $progress/$total"
            done

            if [ "$test_done" = false ]; then
                echo "  ✗ Test timed out"
                suite_failed=$((suite_failed + 1))
            fi
        done

        # Summary
        total=$((suite_passed + suite_failed))
        echo ""
        echo "========================================="
        if [ $suite_failed -eq 0 ]; then
            echo "  ✓ All $total tests passed!"
        else
            echo "  Results: $suite_passed/$total passed, $suite_failed failed"
        fi
        echo "========================================="

        play_mode "exit"
        ;;
    "integration_run")
        # Run all tests in a parent directory with play mode cycling between groups.
        # Standalone .json files each get their own play mode session (max isolation).
        # Subdirectory suites share one play mode session (scene reload between tests).
        parent_dir="$2"
        if [ -z "$parent_dir" ]; then
            echo "Usage: $0 integration_run <parent_directory>"
            exit 1
        fi
        check_server || exit 1
        compile || exit 1

        # Discover standalone .json files at top level
        mapfile -t standalone_files < <(ls "$parent_dir"/*.json 2>/dev/null | sort)

        # Discover subdirectories containing .json files
        mapfile -t suite_dirs < <(for d in "$parent_dir"/*/; do
            [ -d "$d" ] && ls "$d"*.json >/dev/null 2>&1 && echo "$d"
        done | sort)

        total_groups=$(( ${#standalone_files[@]} + ${#suite_dirs[@]} ))

        if [ $total_groups -eq 0 ]; then
            echo "✗ No tests or suites found in: $parent_dir"
            exit 1
        fi

        echo ""
        echo "========================================="
        echo "  Integration Run: $parent_dir"
        echo "  ${#standalone_files[@]} standalone test(s), ${#suite_dirs[@]} suite(s)"
        echo "========================================="

        run_passed=0
        run_failed=0
        group_num=0
        poll_body='{"method":"UnityBridge.IntegrationTestRunner.GetTestStatus","args":[]}'

        # --- Standalone tests (each gets its own play mode session) ---
        for test_file in "${standalone_files[@]}"; do
            group_num=$((group_num + 1))
            test_basename=$(basename "$test_file")

            echo ""
            echo "[$group_num/$total_groups] $test_basename (standalone)"
            echo "========================================="

            play_mode "enter" || { run_failed=$((run_failed + 1)); continue; }
            powershell -Command "(New-Object -ComObject WScript.Shell).AppActivate('Unity')" 2>/dev/null

            # Load declared starting scene
            load_scene "$(get_test_scene "$test_file")"

            unity_path=$(echo "$test_file" | sed 's|.*\(Assets/\)|\1|')

            # Start async test
            start_body="{\"method\":\"UnityBridge.IntegrationTestRunner.RunTestAsync\",\"args\":[\"$unity_path\"]}"
            start_response=$(curl -s --max-time 10 -X POST "$UNITY_SERVER_URL/execute" \
                -H "Content-Type: application/json" -d "$start_body")
            start_json=$(echo "$start_response" | grep '^{.*}' | head -1 | tr -d '\r')

            if ! echo "$start_json" | grep -q '"status":"started"'; then
                echo "  ✗ Failed to start test"
                echo "  $start_json"
                run_failed=$((run_failed + 1))
                play_mode "exit"
                continue
            fi

            test_name=$(echo "$start_json" | grep -o '"name":"[^"]*"' | head -1 | cut -d'"' -f4)
            action_count=$(echo "$start_json" | grep -o '"actionCount":[0-9]*' | cut -d: -f2)
            echo "  $test_name ($action_count actions)"

            # Poll for completion
            test_done=false
            for attempt in $(seq 1 120); do
                sleep 1
                poll_response=$(curl -s --max-time 10 -X POST "$UNITY_SERVER_URL/execute" \
                    -H "Content-Type: application/json" -d "$poll_body")
                poll_json=$(echo "$poll_response" | grep '^{.*}' | head -1 | tr -d '\r')

                if echo "$poll_json" | grep -q '"status":"completed"'; then
                    overall=$(echo "$poll_json" | grep -o '"overallResult":"[^"]*"' | cut -d'"' -f4)
                    duration=$(echo "$poll_json" | grep -o '"duration":[0-9.]*' | head -1 | cut -d: -f2)

                    if [ "$overall" = "Passed" ]; then
                        echo "  ✓ PASSED (${duration}s)"
                        run_passed=$((run_passed + 1))
                    else
                        echo "  ✗ FAILED (${duration}s)"
                        if command -v jq >/dev/null 2>&1; then
                            echo "$poll_json" | jq -r '.result.errors[]? // empty' 2>/dev/null | while IFS= read -r err; do
                                echo "    - $err"
                            done
                        fi
                        run_failed=$((run_failed + 1))
                    fi
                    test_done=true
                    break
                fi

                if ! echo "$poll_json" | grep -q '"status":"running"'; then
                    echo "  ✗ Test ended unexpectedly"
                    run_failed=$((run_failed + 1))
                    test_done=true
                    break
                fi

                progress=$(echo "$poll_json" | grep -o '"progress":[0-9]*' | head -1 | cut -d: -f2)
                total=$(echo "$poll_json" | grep -o '"total":[0-9]*' | head -1 | cut -d: -f2)
                [ -n "$progress" ] && [ -n "$total" ] && echo "  Progress: $progress/$total"
            done

            if [ "$test_done" = false ]; then
                echo "  ✗ Test timed out"
                run_failed=$((run_failed + 1))
            fi

            play_mode "exit"
        done

        # --- Suite directories (each gets one play mode session, scene reload between tests) ---
        for suite_dir in "${suite_dirs[@]}"; do
            group_num=$((group_num + 1))
            suite_name=$(basename "$suite_dir")
            mapfile -t suite_files < <(ls "$suite_dir"*.json 2>/dev/null | sort)
            suite_count=${#suite_files[@]}

            echo ""
            echo "[$group_num/$total_groups] $suite_name/ ($suite_count tests)"
            echo "========================================="

            play_mode "enter" || { run_failed=$((run_failed + suite_count)); continue; }
            powershell -Command "(New-Object -ComObject WScript.Shell).AppActivate('Unity')" 2>/dev/null

            for j in "${!suite_files[@]}"; do
                test_file="${suite_files[$j]}"
                test_num=$((j + 1))
                test_basename=$(basename "$test_file")

                echo ""
                echo "  [$test_num/$suite_count] $test_basename"
                echo "  -----------------------------------------"

                # Load declared starting scene (handles both first test and reloads between tests)
                load_scene "$(get_test_scene "$test_file")"

                unity_path=$(echo "$test_file" | sed 's|.*\(Assets/\)|\1|')

                start_body="{\"method\":\"UnityBridge.IntegrationTestRunner.RunTestAsync\",\"args\":[\"$unity_path\"]}"
                start_response=$(curl -s --max-time 10 -X POST "$UNITY_SERVER_URL/execute" \
                    -H "Content-Type: application/json" -d "$start_body")
                start_json=$(echo "$start_response" | grep '^{.*}' | head -1 | tr -d '\r')

                if ! echo "$start_json" | grep -q '"status":"started"'; then
                    echo "  ✗ Failed to start test"
                    echo "  $start_json"
                    run_failed=$((run_failed + 1))
                    continue
                fi

                test_name=$(echo "$start_json" | grep -o '"name":"[^"]*"' | head -1 | cut -d'"' -f4)
                action_count=$(echo "$start_json" | grep -o '"actionCount":[0-9]*' | cut -d: -f2)
                echo "  $test_name ($action_count actions)"

                test_done=false
                for attempt in $(seq 1 120); do
                    sleep 1
                    poll_response=$(curl -s --max-time 10 -X POST "$UNITY_SERVER_URL/execute" \
                        -H "Content-Type: application/json" -d "$poll_body")
                    poll_json=$(echo "$poll_response" | grep '^{.*}' | head -1 | tr -d '\r')

                    if echo "$poll_json" | grep -q '"status":"completed"'; then
                        overall=$(echo "$poll_json" | grep -o '"overallResult":"[^"]*"' | cut -d'"' -f4)
                        duration=$(echo "$poll_json" | grep -o '"duration":[0-9.]*' | head -1 | cut -d: -f2)

                        if [ "$overall" = "Passed" ]; then
                            echo "  ✓ PASSED (${duration}s)"
                            run_passed=$((run_passed + 1))
                        else
                            echo "  ✗ FAILED (${duration}s)"
                            if command -v jq >/dev/null 2>&1; then
                                echo "$poll_json" | jq -r '.result.errors[]? // empty' 2>/dev/null | while IFS= read -r err; do
                                    echo "    - $err"
                                done
                            fi
                            run_failed=$((run_failed + 1))
                        fi
                        test_done=true
                        break
                    fi

                    if ! echo "$poll_json" | grep -q '"status":"running"'; then
                        echo "  ✗ Test ended unexpectedly"
                        run_failed=$((run_failed + 1))
                        test_done=true
                        break
                    fi

                    progress=$(echo "$poll_json" | grep -o '"progress":[0-9]*' | head -1 | cut -d: -f2)
                    total=$(echo "$poll_json" | grep -o '"total":[0-9]*' | head -1 | cut -d: -f2)
                    [ -n "$progress" ] && [ -n "$total" ] && echo "  Progress: $progress/$total"
                done

                if [ "$test_done" = false ]; then
                    echo "  ✗ Test timed out"
                    run_failed=$((run_failed + 1))
                fi
            done

            play_mode "exit"
        done

        # Aggregate summary
        total=$((run_passed + run_failed))
        echo ""
        echo "========================================="
        echo "  Integration Run Complete"
        echo "  Groups: $total_groups | Tests: $total"
        if [ $run_failed -eq 0 ]; then
            echo "  ✓ All $total tests passed!"
        else
            echo "  Results: $run_passed/$total passed, $run_failed failed"
        fi
        echo "========================================="
        ;;
    *)
        echo "Unity Bridge Script"
        echo "Usage: $0 {compile|logs|status|clear|health|play|screenshot|input|execute|integration_test|integration_suite|integration_run}"
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
        echo "  execute <Method> [args] - Execute static method via reflection"
        echo "  integration_test <file> - Run single integration test (compile+play lifecycle)"
        echo "  integration_suite <dir> - Run all integration tests in directory"
        echo "  integration_run <dir>   - Run all suites with play mode cycling"
        echo ""
        echo "Example: $0 play enter"
        echo "Example: $0 screenshot C:/temp/test.png"
        echo "Example: $0 input tap 500 300"
        echo "Example: $0 input multi_tap 500 300 3"
        echo "Example: $0 execute UnityBridge.BridgeTools.Ping"
        echo "Example: $0 execute UnityBridge.BridgeTools.Add '[1,2]'"
        echo "Example: $0 integration_test Assets/Tests/Integration/test.json"
        echo "Example: $0 integration_suite Assets/Tests/Integration"
        echo "Example: $0 integration_run Assets/Tests/Integration"
        exit 1
        ;;
esac

echo ""
echo "[unity_bridge] Output written to: $OUTPUT_FILE"