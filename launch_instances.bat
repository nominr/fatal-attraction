@echo off
setlocal

:: Configuration
:: Path to your Godot executable
set "GODOT_BIN=C:\Users\nomin\Desktop\Godot_v4.5.1-stable_mono_win64.exe"
:: Path to the project root (current directory, trailing backslash removed)
set "PROJECT_PATH=%cd%"

echo --------------------------------------------------
echo Fatal Attraction - Multi-Instance Launcher (Windows)
echo --------------------------------------------------

:: Check if Godot exists
if not exist "%GODOT_BIN%" (
    echo [ERROR] Godot executable NOT found at: "%GODOT_BIN%"
    echo Please edit this .bat file and update the GODOT_BIN path.
    pause
    exit /b 1
)

:: Clear old logs
echo Cleaning up old logs...
break > host.log
break > client1.log
break > client2.log

echo Launching Host (Producer)...
start "" "%GODOT_BIN%" --path "%PROJECT_PATH%" --host Producer
timeout /t 3 /nobreak > nul

echo Launching Client 1 (Admirer)...
start "" "%GODOT_BIN%" --path "%PROJECT_PATH%" --join Admirer

timeout /t 1 /nobreak > nul

echo Launching Client 2 (Prophet)...
start "" "%GODOT_BIN%" --path "%PROJECT_PATH%" --join Prophet

echo --------------------------------------------------
echo All instances launched!
echo Check host.log, client1.log, and client2.log if things go wrong.
echo --------------------------------------------------
pause
