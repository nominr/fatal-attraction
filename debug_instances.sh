#!/bin/bash

# Configuration
GODOT_APP_PATH="/Applications/Godot_mono.app"
PROJECT_PATH=$(pwd)

# Check if Godot exists
if [ ! -d "$GODOT_APP_PATH" ]; then
    echo "Error: Godot.app not found at $GODOT_APP_PATH"
    echo "Please update the GODOT_APP_PATH variable in this script."
    exit 1
fi

GODOT_EXEC="$GODOT_APP_PATH/Contents/MacOS/Godot"

echo "Launching Host Instance..."
"$GODOT_EXEC" --path "$PROJECT_PATH" > host.log 2>&1 &
PID_HOST=$!
echo "Host launching..."

sleep 2

echo "Launching Client 1..."
echo "Launching Client 1..."
"$GODOT_EXEC" --path "$PROJECT_PATH" > client1.log 2>&1 &
echo "Client 1 launching..."

sleep 1

echo "Launching Client 2..."
echo "Launching Client 2..."
"$GODOT_EXEC" --path "$PROJECT_PATH" > client2.log 2>&1 &
echo "Client 2 launching..."

echo "Done! You should see 3 Godot windows."
echo "1. Host: Enter Name -> Select 'Producer' -> Host"
echo "2. Client 1: Enter Name -> IP '127.0.0.1' -> Select 'Admirer' -> Join"
echo "3. Client 2: Enter Name -> IP '127.0.0.1' -> Select 'Prophet' -> Join"

echo "Logs available in host.log, client1.log, client2.log"
