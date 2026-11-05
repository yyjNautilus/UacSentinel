# 重新编译 UacSentinel.exe
# 用法: powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) { throw '找不到 .NET Framework 4 的 csc.exe' }

New-Item -ItemType Directory -Force -Path (Join-Path $root 'dist') | Out-Null

& $csc /nologo /target:winexe /codepage:65001 /optimize+ `
    /win32icon:"$root\assets\app.ico" `
    /out:"$root\dist\UacSentinel.exe" `
    /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll `
    /r:System.Management.dll /r:System.Web.Extensions.dll `
    "$root\src\UacSentinel.cs"

if ($LASTEXITCODE -ne 0) { throw "编译失败 ($LASTEXITCODE)" }
Write-Host "已生成 dist\UacSentinel.exe" -ForegroundColor Green
