@echo off
echo ========================================
echo Fatal Attraction - First Time Setup
echo ========================================
echo.
echo Building C# project...
dotnet build
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ERROR: Build failed! Make sure you have .NET 8.0 SDK installed.
    echo Download from: https://dotnet.microsoft.com/download
    pause
    exit /b 1
)
echo.
echo ========================================
echo Build successful!
echo You can now open the project in Godot.
echo ========================================
pause
