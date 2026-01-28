@echo off
REM Configuration - Update this path to match your Godot installation
set "GODOT_PATH=C:\Users\nomin\Desktop\Godot_v4.5.1-stable_mono_win64.exe"
set "PROJECT_PATH=%cd%"

REM Cleanup existing instances
echo Killing existing Godot processes...
taskkill /F /IM Godot*.exe >nul 2>&1
timeout /t 1 /nobreak >nul

REM Check if Godot exists
if not exist "%GODOT_PATH%" (
    echo Error: Godot not found at %GODOT_PATH%
    echo Please update the GODOT_PATH variable in this script.
    echo Common locations:
    echo   - C:\Program Files\Godot\Godot_v4.3-stable_mono_win64.exe
    echo   - C:\Godot\Godot_v4.3-stable_mono_win64.exe
    echo   - %USERPROFILE%\Downloads\Godot_v4.3-stable_mono_win64.exe
    pause
    exit /b 1
)

echo Launching Host Instance (Producer)...
start "Godot Host" "%GODOT_PATH%" --path "%PROJECT_PATH%" --host Producer > host.log 2>&1
echo Host launching...

timeout /t 4 /nobreak >nul

echo Launching Client 1 (Admirer)...
start "Godot Client 1" "%GODOT_PATH%" --path "%PROJECT_PATH%" --join Admirer > client1.log 2>&1
echo Client 1 launching...

timeout /t 1 /nobreak >nul

echo Launching Client 2 (Prophet)...
start "Godot Client 2" "%GODOT_PATH%" --path "%PROJECT_PATH%" --join Prophet > client2.log 2>&1
echo Client 2 launching...

echo.
echo Done! You should see 3 Godot windows auto-connecting.
echo Logs available in host.log, client1.log, client2.log
pause
