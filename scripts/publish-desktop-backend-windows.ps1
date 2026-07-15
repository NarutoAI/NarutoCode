$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$output = Join-Path $root 'artifacts/desktop/backend/win-x64'
$resource = Join-Path $root 'src/NarutoCode.Desktop/resources/backend/win-x64'
$dotnet = if (Test-Path "$HOME/.dotnet/dotnet") { "$HOME/.dotnet/dotnet" } else { 'dotnet' }

Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $output, $resource
& $dotnet publish (Join-Path $root 'src/NarutoCode.Desktop.Api/NarutoCode.Desktop.Api.csproj') `
  -c Release -r win-x64 -p:PublishAot=true -p:SelfContained=true -o $output
New-Item -ItemType Directory -Force -Path $resource | Out-Null
Copy-Item -Recurse -Force (Join-Path $output '*') $resource
if (-not (Test-Path (Join-Path $resource 'narutocode-desktop-api.exe'))) {
  throw 'Windows Desktop API executable was not published.'
}
