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

echo "Launching Host Instance..."
open -n "$GODOT_APP_PATH" --args --path "$PROJECT_PATH" &
PID_HOST=$!
echo "Host launching..."

sleep 2

echo "Launching Client 1..."
open -n "$GODOT_APP_PATH" --args --path "$PROJECT_PATH" &
echo "Client 1 launching..."

sleep 1

echo "Launching Client 2..."
open -n "$GODOT_APP_PATH" --args --path "$PROJECT_PATH" &
echo "Client 2 launching..."

echo "Done! You should see 3 Godot windows."
echo "1. Host: Enter Name -> Select 'Producer' -> Host"
echo "2. Client 1: Enter Name -> IP '127.0.0.1' -> Select 'Admirer' -> Join"
echo "3. Client 2: Enter Name -> IP '127.0.0.1' -> Select 'Prophet' -> Join"
