$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$desktop = Join-Path $root 'src/NarutoCode.Desktop'
$output = Join-Path $root 'artifacts/desktop/make/win-x64'

& (Join-Path $root 'scripts/publish-desktop-backend-windows.ps1')
Push-Location $desktop
try {
  npm ci
  npm test
  npm run typecheck
  npm run make
  New-Item -ItemType Directory -Force -Path $output | Out-Null
  Get-ChildItem -Recurse -Path (Join-Path $desktop 'out/make') -Filter '*.exe' | Copy-Item -Destination $output -Force
}
finally {
  Pop-Location
}
