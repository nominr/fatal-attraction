#!/bin/bash

# Configuration
# Path to the Godot executable
# IMPORTANT: Use forward slashes (/) and ensure the path is correct for your system.
GODOT_BIN="C:/Users/nomin/Desktop/Godot_v4.5.1-stable_mono_win64.exe"
# Path to the Godot project (the directory containing project.godot)
PROJECT_PATH="C:/Users/nomin/Desktop/fatal-attraction"

echo "--------------------------------------------------"
echo "Fatal Attraction - Multi-Instance Launcher Debug"
echo "--------------------------------------------------"
echo "Checking paths..."
echo "GODOT_BIN: $GODOT_BIN"
echo "PROJECT_PATH: $PROJECT_PATH"

# Verify Godot executable exists
if [ -f "$GODOT_BIN" ]; then
    echo "[OK] Godot executable found."
else
    echo "[ERROR] Godot executable NOT found at '$GODOT_BIN'."
    echo "Attempting to find it at /c/Users/nomin/Desktop/..."
    GODOT_BIN="/c/Users/nomin/Desktop/Godot_v4.5.1-stable_mono_win64.exe"
    if [ -f "$GODOT_BIN" ]; then
        echo "[OK] Found at $GODOT_BIN"
    else
        echo "[FATAL] Godot executable not found. Please check your path in the script."
        exit 1
    fi
fi

# Verify Project path exists
if [ -f "$PROJECT_PATH/project.godot" ]; then
    echo "[OK] Project file found."
else
    echo "[ERROR] project.godot NOT found in '$PROJECT_PATH'."
    echo "Checking current directory..."
    if [ -f "./project.godot" ]; then
        PROJECT_PATH=$(pwd)
        echo "[OK] Using current directory: $PROJECT_PATH"
    else
        echo "[FATAL] Could not find project.godot. Please run this script from the project root."
        exit 1
    fi
fi

# Clear logs
echo "Clearing old logs..."
> host.log
> client1.log
> client2.log

echo "Launching Host Instance (Producer)..."
# Using -d to run in debug mode, or --verbose for extra info
"$GODOT_BIN" --path "$PROJECT_PATH" --host Producer --verbose > host.log 2>&1 &
PID_HOST=$!
echo "Host launched (PID: $PID_HOST)"

# Wait for host to initialize
sleep 3

# Check if Host is still running
if ! ps -p $PID_HOST > /dev/null; then
    echo "[WARNING] Host process (PID $PID_HOST) seems to have exited immediately. Check host.log"
fi

echo "Launching Client 1 (Admirer)..."
"$GODOT_BIN" --path "$PROJECT_PATH" --join Admirer > client1.log 2>&1 &
PID_CLIENT1=$!
echo "Client 1 launched (PID: $PID_CLIENT1)"

sleep 1

echo "Launching Client 2 (Prophet)..."
"$GODOT_BIN" --path "$PROJECT_PATH" --join Prophet > client2.log 2>&1 &
PID_CLIENT2=$!
echo "Client 2 launched (PID: $PID_CLIENT2)"

echo "--------------------------------------------------"
echo "All instances started!"
echo "Check the terminal for immediate path errors."
echo "Check *.log files for Godot engine errors."
echo "--------------------------------------------------"
