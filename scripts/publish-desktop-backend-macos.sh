#!/bin/sh
set -eu

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
OUT="$ROOT/artifacts/desktop/backend/osx-arm64"
RESOURCE="$ROOT/src/NarutoCode.Desktop/resources/backend/osx-arm64"

rm -rf "$OUT" "$RESOURCE"
"$HOME/.dotnet/dotnet" publish "$ROOT/src/NarutoCode.Desktop.Api/NarutoCode.Desktop.Api.csproj" \
  -c Release -r osx-arm64 -p:PublishAot=true -p:SelfContained=true -o "$OUT"
mkdir -p "$RESOURCE"
cp -R "$OUT/." "$RESOURCE/"
test -x "$RESOURCE/narutocode-desktop-api"
