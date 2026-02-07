#!/bin/bash
# ============================================================================
# SORCERY+ REMAKE - BUILD SCRIPT (Linux/macOS)
# Automated build and run script
# ============================================================================

echo ""
echo "========================================"
echo " SORCERY+ REMAKE - BUILD SCRIPT"
echo "========================================"
echo ""

# Check if .NET SDK is installed
echo "[1/5] Checking .NET SDK installation..."
if ! command -v dotnet &> /dev/null; then
    echo "ERROR: .NET SDK not found!"
    echo "Please install .NET SDK 8.0 from:"
    echo "https://dotnet.microsoft.com/download/dotnet/8.0"
    echo ""
    exit 1
fi
echo ".NET SDK found:"
dotnet --version
echo ""

# Check if MonoGame templates are installed
echo "[2/5] Checking MonoGame templates..."
if ! dotnet new list | grep -q "mgdesktopgl"; then
    echo "WARNING: MonoGame templates not found."
    echo "Installing MonoGame templates..."
    dotnet new install MonoGame.Templates.CSharp
    echo ""
fi

# Restore NuGet packages
echo "[3/5] Restoring NuGet packages..."
if ! dotnet restore; then
    echo "ERROR: Failed to restore packages!"
    exit 1
fi
echo ""

# Build the project
echo "[4/5] Building project..."
if ! dotnet build --configuration Debug; then
    echo "ERROR: Build failed!"
    exit 1
fi
echo ""

# Run the game
echo "[5/5] Launching Sorcery+ Remake..."
echo ""
echo "========================================"
echo " CONTROLS:"
echo "   Arrow Keys - Move player"
echo "   Up Arrow   - Thrust (fight gravity)"
echo "   F1         - Toggle debug info"
echo "   ESC        - Exit"
echo "========================================"
echo ""
dotnet run
