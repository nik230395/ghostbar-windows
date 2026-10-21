$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
Write-Host "Built: publish\Ghostbar.exe"
