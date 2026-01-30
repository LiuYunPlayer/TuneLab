@echo off
REM Build script for Windows

setlocal enabledelayedexpansion

set "SCRIPT_DIR=%~dp0"
set "PROJECT_DIR=%SCRIPT_DIR%.."
set "BUILD_DIR=%PROJECT_DIR%\build"

echo ========================================
echo PluginHost Build Script for Windows
echo ========================================

REM Check for CMake
where cmake >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo Error: CMake not found. Please install CMake and add it to PATH.
    exit /b 1
)

REM Create build directory
if not exist "%BUILD_DIR%" mkdir "%BUILD_DIR%"
cd /d "%BUILD_DIR%"

REM Configure with CMake
echo.
echo Configuring with CMake...
cmake -G "Visual Studio 18 2026" -A x64 "%PROJECT_DIR%"
if %ERRORLEVEL% NEQ 0 (
    echo Error: CMake configuration failed.
    exit /b 1
)

REM Build
echo.
echo Building...
cmake --build . --config Release
if %ERRORLEVEL% NEQ 0 (
    echo Error: Build failed.
    exit /b 1
)

echo.
echo ========================================
echo Build completed successfully!
echo Output: %BUILD_DIR%\bin\Release\PluginHost.dll
echo ========================================

endlocal
