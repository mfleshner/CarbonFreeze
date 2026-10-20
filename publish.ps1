# Builds a single-file, self-contained win-x64 SaveSwitch.exe into .\publish
# Usage:  .\publish.ps1
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

dotnet publish src\SaveSwitch.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

Write-Host ""
Write-Host "Published to $(Join-Path $PSScriptRoot 'publish')"
Get-ChildItem publish\SaveSwitch.exe | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}}, LastWriteTime
