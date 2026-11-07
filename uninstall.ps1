$ErrorActionPreference = 'Stop'
# Desinstala Copilot Launcher del usuario actual.
Get-Process CopilotLauncher -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.Id }
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'CopilotLauncher' -ErrorAction SilentlyContinue
Remove-Item (Join-Path ([Environment]::GetFolderPath('Programs')) 'Copilot Launcher.lnk') -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500
Remove-Item (Join-Path $env:LOCALAPPDATA 'Programs\CopilotLauncher') -Recurse -Force -ErrorAction SilentlyContinue
Write-Host 'Copilot Launcher desinstalado.'
