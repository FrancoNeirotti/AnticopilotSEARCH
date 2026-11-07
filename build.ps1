$ErrorActionPreference = 'Stop'
# Compila un .exe autónomo (no requiere .NET instalado para ejecutarlo).
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    $local = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
    if (Test-Path $local) { $dotnet = $local } else { throw 'No se encontró el SDK de .NET 8. Instalalo con: winget install Microsoft.DotNet.SDK.8' }
} else { $dotnet = $dotnet.Source }

& $dotnet publish (Join-Path $PSScriptRoot 'src\CopilotLauncher') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -o (Join-Path $PSScriptRoot 'publish') -nologo
if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación' }
Write-Host "Listo: $(Join-Path $PSScriptRoot 'publish\CopilotLauncher.exe')"
