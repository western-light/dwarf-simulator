#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_ROOT="$(cd "$ROOT/../../../.." && pwd)"
BUILD_DIR="$PROJECT_ROOT/Build/PurrVoiceNativeAudio/Linux-x64"
OUT_DIR="$ROOT/Linux"

cmake -S "$ROOT/Build" -B "$BUILD_DIR" -DCMAKE_BUILD_TYPE=Release
cmake --build "$BUILD_DIR" --config Release

mkdir -p "$OUT_DIR"
cp "$BUILD_DIR/libPurrVoiceNativeAudio.so" "$OUT_DIR/libPurrVoiceNativeAudio.so"
