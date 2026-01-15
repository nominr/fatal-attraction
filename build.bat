@echo off
REM Fatal Attraction - Build and Run Script (Windows)

setlocal enabledelayedexpansion

echo.
echo ===================================
echo Fatal Attraction - C# Game Engine
echo ===================================
echo.

REM Check for .NET
where dotnet >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: .NET SDK not found. Please install .NET 6.0 or higher.
    echo Visit: https://dotnet.microsoft.com/download
    exit /b 1
)

echo [OK] .NET SDK found:
dotnet --version
echo.

REM Restore dependencies
echo [*] Restoring dependencies...
dotnet restore
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Restore failed
    exit /b 1
)
echo [OK] Dependencies restored
echo.

REM Build
echo [*] Building project...
dotnet build
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Build failed
    exit /b 1
)
echo [OK] Build successful
echo.

REM Run
echo [*] Starting game...
echo.
dotnet run

pause
