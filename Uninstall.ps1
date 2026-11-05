# 卸载提权哨兵（当前用户）
$ErrorActionPreference = 'SilentlyContinue'

Get-Process UacSentinel | Stop-Process -Force
Start-Sleep -Milliseconds 300

Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'UacSentinel'

$targetDir = Join-Path $env:LOCALAPPDATA 'Programs\UacSentinel'
if (Test-Path $targetDir) {
    Remove-Item $targetDir -Recurse -Force
}

Write-Host ""
Write-Host " 已卸载。如需保留历史记录，请提前备份 $targetDir\data\events.json" -ForegroundColor Green
Write-Host ""
