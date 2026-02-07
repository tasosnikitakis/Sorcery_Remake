@echo off
REM ============================================================================
REM SORCERY+ REMAKE - BUILD SCRIPT
REM Automated build and run script for Windows
REM ============================================================================

echo.
echo ========================================
echo  SORCERY+ REMAKE - BUILD SCRIPT
echo ========================================
echo.

REM Check if .NET SDK is installed
echo [1/5] Checking .NET SDK installation...
dotnet --version >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: .NET SDK not found!
    echo Please install .NET SDK 8.0 from:
    echo https://dotnet.microsoft.com/download/dotnet/8.0
    echo.
    pause
    exit /b 1
)
echo .NET SDK found:
dotnet --version
echo.

REM Check if MonoGame templates are installed
echo [2/5] Checking MonoGame templates...
dotnet new list | findstr /C:"mgdesktopgl" >nul
if %ERRORLEVEL% NEQ 0 (
    echo WARNING: MonoGame templates not found.
    echo Installing MonoGame templates...
    dotnet new install MonoGame.Templates.CSharp
    echo.
)

REM Restore NuGet packages
echo [3/5] Restoring NuGet packages...
dotnet restore
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Failed to restore packages!
    pause
    exit /b 1
)
echo.

REM Build the project
echo [4/5] Building project...
dotnet build --configuration Debug
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Build failed!
    pause
    exit /b 1
)
echo.

REM Run the game
echo [5/5] Launching Sorcery+ Remake...
echo.
echo ========================================
echo  CONTROLS:
echo    Arrow Keys - Move player
echo    Up Arrow   - Thrust (fight gravity)
echo    F1         - Toggle debug info
echo    ESC        - Exit
echo ========================================
echo.
dotnet run

pause
