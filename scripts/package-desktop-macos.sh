#!/bin/sh
set -eu

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
DESKTOP="$ROOT/src/NarutoCode.Desktop"
OUTPUT="$ROOT/artifacts/desktop/make/osx-arm64"

"$ROOT/scripts/publish-desktop-backend-macos.sh"
cd "$DESKTOP"
npm ci
npm test
npm run typecheck
npm run make
mkdir -p "$OUTPUT"
find "$DESKTOP/out/make" -type f -name '*.dmg' -exec cp {} "$OUTPUT/" \;
