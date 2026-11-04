#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_ROOT="$(cd "$ROOT/../../../.." && pwd)"
BUILD_DIR="$PROJECT_ROOT/Build/PurrVoiceNativeAudio/MacOS-universal"
OUT_DIR="$ROOT/OSX"

cmake -S "$ROOT/Build" -B "$BUILD_DIR" -DCMAKE_BUILD_TYPE=Release -DCMAKE_OSX_ARCHITECTURES="x86_64;arm64"
cmake --build "$BUILD_DIR" --config Release

mkdir -p "$OUT_DIR"
cp "$BUILD_DIR/libPurrVoiceNativeAudio.dylib" "$OUT_DIR/libPurrVoiceNativeAudio.dylib"
