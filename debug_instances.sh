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

echo "Launching Host Instance (Producer)..."
"$GODOT_EXEC" --path "$PROJECT_PATH" --host Producer > host.log 2>&1 &
PID_HOST=$!
echo "Host launching..."

sleep 4

echo "Launching Client 1 (Admirer)..."
"$GODOT_EXEC" --path "$PROJECT_PATH" --join Admirer > client1.log 2>&1 &
echo "Client 1 launching..."

sleep 1

echo "Launching Client 2 (Prophet)..."
"$GODOT_EXEC" --path "$PROJECT_PATH" --join Prophet > client2.log 2>&1 &
echo "Client 2 launching..."

echo "Done! You should see 3 Godot windows auto-connecting."
echo "Logs available in host.log, client1.log, client2.log"
