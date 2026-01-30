#!/bin/bash
# Build script for Linux and macOS

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
BUILD_DIR="$PROJECT_DIR/build"

echo "========================================"
echo "PluginHost Build Script"
echo "========================================"

# Check for CMake
if ! command -v cmake &> /dev/null; then
    echo "Error: CMake not found. Please install CMake."
    exit 1
fi

# Detect platform
if [[ "$OSTYPE" == "darwin"* ]]; then
    PLATFORM="macOS"
    GENERATOR="Xcode"
    LIB_EXT="dylib"
elif [[ "$OSTYPE" == "linux-gnu"* ]]; then
    PLATFORM="Linux"
    GENERATOR="Unix Makefiles"
    LIB_EXT="so"
else
    echo "Unsupported platform: $OSTYPE"
    exit 1
fi

echo "Platform: $PLATFORM"

# Create build directory
mkdir -p "$BUILD_DIR"
cd "$BUILD_DIR"

# Configure with CMake
echo ""
echo "Configuring with CMake..."
cmake -G "$GENERATOR" -DCMAKE_BUILD_TYPE=Release "$PROJECT_DIR"

# Build
echo ""
echo "Building..."
cmake --build . --config Release -- -j$(nproc 2>/dev/null || sysctl -n hw.ncpu)

echo ""
echo "========================================"
echo "Build completed successfully!"
echo "Output: $BUILD_DIR/bin/libPluginHost.$LIB_EXT"
echo "========================================"
