# Configuration
$GODOT_APP_PATH = "C:\Users\isalo\Downloads\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64.exe"
$PROJECT_PATH = Get-Location

# Kill existing Godot instances
Get-Process Godot_v4.5.1-stable_mono_win64 -ErrorAction SilentlyContinue | Stop-Process

# Launch Host (Producer)
Write-Host "Launching Host Instance (Producer)..."
Start-Process -FilePath $GODOT_APP_PATH -ArgumentList "--path `"$PROJECT_PATH`" --host Producer"

Start-Sleep -Seconds 2

# Launch Client 1 (Admirer)
Write-Host "Launching Client 1 (Admirer)..."
Start-Process -FilePath $GODOT_APP_PATH -ArgumentList "--path `"$PROJECT_PATH`" --join Admirer"

Start-Sleep -Seconds 1

# Launch Client 2 (Prophet)
Write-Host "Launching Client 2 (Prophet)..."
Start-Process -FilePath $GODOT_APP_PATH -ArgumentList "--path `"$PROJECT_PATH`" --join Prophet"

Write-Host "All instances launched!"
