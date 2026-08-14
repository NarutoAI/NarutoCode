#!/bin/sh
set -eu

project_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
desktop_directory="$project_root/src/NarutoCode.Desktop"
backend_executable="$desktop_directory/resources/backend/osx-arm64/narutocode-desktop-api"

if [ "$(uname -s)" != "Darwin" ] || [ "$(uname -m)" != "arm64" ]; then
  printf '%s\n' '此本地启动脚本仅支持 macOS arm64。' >&2
  exit 1
fi

if [ ! -x "$backend_executable" ]; then
  printf '未找到可执行的 Desktop API：%s\n' "$backend_executable" >&2
  printf '%s\n' '请先生成或复制 osx-arm64 Native AOT 后端产物。' >&2
  exit 1
fi

if [ ! -x "$desktop_directory/node_modules/.bin/electron-forge" ]; then
  printf '未找到 Desktop Node 依赖：%s\n' "$desktop_directory/node_modules" >&2
  printf '%s\n' '请先在 src/NarutoCode.Desktop 目录执行 npm install。' >&2
  exit 1
fi

export NARUTOCODE_DESKTOP_API_EXECUTABLE="$backend_executable"
exec npm --prefix "$desktop_directory" run start
