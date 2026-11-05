# 安装到本机（当前用户，无需管理员权限）
$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot 'dist\UacSentinel.exe'
if (-not (Test-Path $src)) {
    Write-Host "找不到 $src ，请确认 dist 文件夹完整。" -ForegroundColor Red
    exit 1
}

$targetDir = Join-Path $env:LOCALAPPDATA 'Programs\UacSentinel'
New-Item -ItemType Directory -Force -Path $targetDir | Out-Null

Get-Process UacSentinel -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400

$targetExe = Join-Path $targetDir 'UacSentinel.exe'
Copy-Item $src $targetExe -Force

$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Set-ItemProperty -Path $run -Name 'UacSentinel' -Value ('"' + $targetExe + '"')

# 本地小模型（可选）：没有就拉一次
$ollama = Get-Command ollama -ErrorAction SilentlyContinue
if ($ollama) {
    $models = & ollama list 2>$null
    if (-not ($models -match 'qwen3\.5:0\.8b')) {
        Write-Host "首次使用需要本地模型 qwen3.5:0.8b（约 1.3 GB），开始拉取..." -ForegroundColor Cyan
        & ollama pull qwen3.5:0.8b
    }
} else {
    Write-Host "未检测到 Ollama：先只用规则分级，装上 Ollama 后会启用本地模型。" -ForegroundColor Yellow
}

Start-Process $targetExe

Write-Host ""
Write-Host " 提权哨兵已安装" -ForegroundColor Green
Write-Host "   程序位置: $targetDir"
Write-Host "   开机自启: 已开启（可在悬浮窗右下角取消）"
Write-Host "   数据记录: $targetDir\data\events.json"
Write-Host ""
Write-Host " 现在屏幕右下角应该出现一个小窗。"
Write-Host " 想立刻看效果，可运行:  Start-Process cmd -Verb RunAs"
Write-Host ""
