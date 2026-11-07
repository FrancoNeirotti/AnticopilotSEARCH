$ErrorActionPreference = 'Stop'
# Instala Copilot Launcher para el usuario actual (no requiere administrador).
$src = Join-Path $PSScriptRoot 'publish\CopilotLauncher.exe'
if (-not (Test-Path $src)) { throw "No se encontró $src. Ejecutá primero build.ps1." }

$dest = Join-Path $env:LOCALAPPDATA 'Programs\CopilotLauncher'
New-Item -ItemType Directory -Force -Path $dest | Out-Null

Get-Process CopilotLauncher -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.Id }
Start-Sleep -Milliseconds 500
Copy-Item $src $dest -Force
$exe = Join-Path $dest 'CopilotLauncher.exe'

# Inicio automático con Windows
Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'CopilotLauncher' -Value "`"$exe`" --silent"

# Acceso directo en el menú Inicio
$lnk = Join-Path ([Environment]::GetFolderPath('Programs')) 'Copilot Launcher.lnk'
$shell = New-Object -ComObject WScript.Shell
$sc = $shell.CreateShortcut($lnk)
$sc.TargetPath = $exe
$sc.Save()

Start-Process $exe
Write-Host "Instalado en $exe y en ejecución. Presioná la tecla Copilot (o Alt+Espacio)."
